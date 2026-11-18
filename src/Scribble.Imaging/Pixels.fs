namespace Scribble.Imaging

open System


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

    /// The next mip level: half the size (rounded up), each pixel the rounded average of the 2 × 2 block above it;
    /// at an odd edge the last row or column counts twice. Premultiplied, so averaging is right.
    let halve (level: ImageLevel) : ImageLevel =
        let w, h, src = level.Width, level.Height, level.Pixels
        let nw, nh = max 1 ((w + 1) / 2), max 1 ((h + 1) / 2)
        let out = GC.AllocateUninitializedArray<byte> (nw * nh * 4)
        for y in 0 .. nh - 1 do
            let y0 = min (y * 2) (h - 1)
            let y1 = min (y * 2 + 1) (h - 1)
            for x in 0 .. nw - 1 do
                let x0 = min (x * 2) (w - 1)
                let x1 = min (x * 2 + 1) (w - 1)
                let a = (y0 * w + x0) * 4
                let b = (y0 * w + x1) * 4
                let c = (y1 * w + x0) * 4
                let d = (y1 * w + x1) * 4
                let o = (y * nw + x) * 4
                for k in 0..3 do
                    out[o + k] <- byte ((int src[a + k] + int src[b + k] + int src[c + k] + int src[d + k] + 2) / 4)
        { Width = nw; Height = nh; Pixels = out }

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
