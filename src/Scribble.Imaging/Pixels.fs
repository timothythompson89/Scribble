namespace Scribble.Imaging

open System
open System.Runtime.CompilerServices
open System.Runtime.Intrinsics
open Microsoft.FSharp.NativeInterop

/// An image file's size, read from its header without decoding it (so the first layout is right).
[<Struct>]
type ImageInfo = { Width: int; Height: int }

/// The data isn't an image Scribble can read: an unknown format, a corrupt or truncated file, or one too big to
/// decode. Decoders throw only this (anything else would be a bug in the decoder).
type ImageFormatException(message: string) =
    inherit Exception(message)

/// One mip level of a decoded image: premultiplied RGBA, row by row from the top, no padding.
[<Struct>]
type ImageLevel = { Width: int; Height: int; Pixels: byte[] }

/// The colour arithmetic decoders share, so every format rounds the same way.
module internal Pixels =
    /// A 16-bit channel rounded to the nearest 8-bit one (`v * 257` comes back as `v`).
    let round16 (v: int) = (v * 255 + 32767) / 65535

    /// A straight colour channel times alpha, rounded to nearest.
    let premultiply (c: int) (a: int) = (c * a + 127) / 255

    /// The largest picture a decoder decodes: 100 million pixels (400 MB decoded; a 100-megapixel camera's photo). A
    /// small file can claim, and even hold, far more (zlib packs 1,000 to 1): this keeps one from taking all memory.
    let maxDecodePixels = 100_000_000L

    /// One row of the next mip level from output pixel `from` on: `nw` pixels at `o`, each the rounded average of
    /// two pixels of `r0` and the two below them in `r1` (rows of `w` pixels; at an odd edge the last column counts
    /// twice).
    let private halveRowFrom (r0: nativeptr<byte>) (r1: nativeptr<byte>) (w: int) (o: nativeptr<byte>) (from: int) (nw: int) =
        let inline get (p: nativeptr<byte>) (i: int) = int (NativePtr.get p i)
        for x in from .. nw - 1 do
            let a = min (x * 2) (w - 1) * 4
            let b = min (x * 2 + 1) (w - 1) * 4
            for k in 0..3 do
                NativePtr.set o (x * 4 + k) (byte ((get r0 (a + k) + get r0 (b + k) + get r1 (a + k) + get r1 (b + k) + 2) / 4))

    /// As `halveRowFrom` from 0, two output pixels (four across, two rows) at a time with `Vector128` while four
    /// source pixels are left; returns how many output pixels it wrote.
    let private halveRowVector (r0: nativeptr<byte>) (r1: nativeptr<byte>) (w: int) (o: nativeptr<byte>) : int =
        let low = Vector128.Create(0x00FFus)
        let two = Vector128.Create(2us)
        // `v` holds one channel pair (red and blue, or green and alpha) of four pixels, the two rows already added,
        // in 16-bit lanes. Adding each 64-bit half's upper pixel to its lower one leaves the sums of two pixels
        // across in lanes 0, 1, 4 and 5 (at most 1,020: no carry between lanes); the other lanes are dropped later.
        let inline average (v: Vector128<uint16>) =
            let s = v.AsUInt64()
            Vector128.ShiftRightLogical((s + Vector128.ShiftRightLogical(s, 32)).AsUInt16() + two, 2)
        let mutable x = 0
        while x * 8 + 16 <= w * 4 do
            let t = Vector128.Load(NativePtr.add r0 (x * 8)).AsUInt16()
            let u = Vector128.Load(NativePtr.add r1 (x * 8)).AsUInt16()
            let rb = average ((t &&& low) + (u &&& low))
            let ga = average (Vector128.ShiftRightLogical(t, 8) + Vector128.ShiftRightLogical(u, 8))
            // Back to bytes: an output pixel in the low 4 bytes of each 64-bit half, then the two side by side.
            let pixels = (rb ||| Vector128.ShiftLeft(ga, 8)).AsUInt64()
            let packed = Vector128.Narrow(pixels, pixels).AsUInt64().ToScalar()
            Unsafe.WriteUnaligned<uint64>(NativePtr.toVoidPtr (NativePtr.add o (x * 4)), packed)
            x <- x + 2
        x

    /// `halve`, with `Vector128` or without (the same bytes either way; tests compare them).
    let halveWith (vectors: bool) (level: ImageLevel) : ImageLevel =
        let w, h, src = level.Width, level.Height, level.Pixels
        let nw, nh = max 1 ((w + 1) / 2), max 1 ((h + 1) / 2)
        let out = GC.AllocateUninitializedArray<byte> (nw * nh * 4)
        use s = fixed src
        use d = fixed out
        for y in 0 .. nh - 1 do
            let r0 = NativePtr.add s (min (y * 2) (h - 1) * w * 4)
            let r1 = NativePtr.add s (min (y * 2 + 1) (h - 1) * w * 4)
            let o = NativePtr.add d (y * nw * 4)
            let from = if vectors then halveRowVector r0 r1 w o else 0
            halveRowFrom r0 r1 w o from nw
        { Width = nw; Height = nh; Pixels = out }

    /// The next mip level: half the size (rounded up), each pixel the rounded average of the 2 × 2 block above it;
    /// at an odd edge the last row or column counts twice. Premultiplied, so averaging is right.
    let halve (level: ImageLevel) : ImageLevel =
        halveWith (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian) level

/// Counts releases process-wide, so a GPU device knows when to look for released images (DecodedImage.Release).
type internal ReleaseCounter private () =
    [<DefaultValue>] static val mutable private count: int
    static member Count = lock typeof<ReleaseCounter> (fun () -> ReleaseCounter.count)
    static member Bump() = lock typeof<ReleaseCounter> (fun () -> ReleaseCounter.count <- ReleaseCounter.count + 1)

/// An image's pixels once read from its file (or given as pixels), ready to draw: premultiplied RGBA, sRGB, with its
/// mip levels (each half the size of the one before, down to 1 × 1). Shared by every place that shows the image and
/// every GPU device that draws it; thread-safe. An image over `MaxSize` on a side is halved until it fits when it is
/// made (`Reduced`).
///
/// `Release()` says nobody will draw it again: GPU devices give back its atlas space at their next frame, and drawing
/// it throws. Core calls it (DESIGN.md, "M8 step 3b", decision 18 and (c) 9).
[<Sealed>]
type DecodedImage(width: int, height: int, pixels: byte[]) =
    do
        if width < 1 || height < 1 then invalidArg (nameof width) "An image is at least 1 × 1."
        if int64 width * int64 height * 4L <> int64 pixels.Length then
            invalidArg (nameof pixels) "Pixels must be width × height × 4 bytes (premultiplied RGBA)."

    static let maxSize = 8192

    let levels =
        let mutable level = { Width = width; Height = height; Pixels = pixels }
        while level.Width > maxSize || level.Height > maxSize do
            level <- Pixels.halve level
        ResizeArray [ level ]

    let first = levels[0]

    let levelCount =
        let mutable n = 1
        let mutable w, h = first.Width, first.Height
        while w > 1 || h > 1 do
            w <- max 1 ((w + 1) / 2)
            h <- max 1 ((h + 1) / 2)
            n <- n + 1
        n

    let mutable released = false

    /// The largest width or height a decoded image keeps (WebGPU's default texture limit).
    static member MaxSize = maxSize

    /// Width in pixels (after any reduction).
    member _.Width = first.Width

    /// Height in pixels (after any reduction).
    member _.Height = first.Height

    /// The full-size pixels: premultiplied RGBA, row by row from the top, no padding.
    member _.Pixels = first.Pixels

    /// The size the pixels came in at, before any reduction.
    member _.OriginalWidth = width
    member _.OriginalHeight = height

    /// Whether the image was over `MaxSize` on a side and has been halved to fit.
    member _.Reduced = first.Width <> width || first.Height <> height

    /// How many mip levels there are, level 0 (full size) down to 1 × 1.
    member _.LevelCount = levelCount

    /// Mip level `k` (0 = full size), built from the ones above it if it hasn't been yet.
    member _.Level(k: int) : ImageLevel =
        if k < 0 || k >= levelCount then raise (ArgumentOutOfRangeException(nameof k))
        lock levels (fun () ->
            while levels.Count <= k do
                levels.Add(Pixels.halve levels[levels.Count - 1])
            levels[k])

    /// Builds every mip level now (decoders do, off the UI thread where they run).
    member this.BuildLevels() = this.Level(levelCount - 1) |> ignore

    /// How many levels are built so far.
    member _.LevelsBuilt = lock levels (fun () -> levels.Count)

    /// Says nobody will draw the image again; see the type's summary.
    member _.Release() =
        if not released then
            released <- true
            ReleaseCounter.Bump()

    member _.IsReleased = released

    /// For GPU devices: changes whenever any decoded image is released, so a device only looks for released images
    /// when there are some.
    static member ReleaseCount = ReleaseCounter.Count
