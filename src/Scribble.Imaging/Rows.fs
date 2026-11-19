/// The decoders' inner loops over rows of bytes, each written twice: scalar, and with `Vector128` (SSE2/SSSE3, NEON,
/// wasm SIMD). The two give the same bytes for every input (tests run both over the same rows); the unsuffixed
/// function picks the vector one where the hardware has it. DESIGN.md, "Vectorising the PNG decoder".
///
/// Everything is on pointers into arrays the caller pinned and sized. A vector loop only loads and stores inside
/// the `count` bytes (or `width` pixels) it was given; the scalar loop finishes the tail.
module internal Scribble.Imaging.Rows

open System
open System.Runtime.CompilerServices
open System.Runtime.Intrinsics
open Microsoft.FSharp.NativeInterop

/// Whether the vector paths are used: the hardware has 128-bit vectors (the pixel loads assume little-endian).
let useVectors () = Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian

let inline private get (p: nativeptr<byte>) (i: int) = int (NativePtr.get p i)

// ---- PNG's row filters ---------------------------------------------------------------------------------

/// Undoes one row's filter in place from byte `from` on (the bytes before are done): `count` bytes at `cur`, the
/// row above (already unfiltered, or zeros) at `up`, `step` bytes between a byte and the same byte of the pixel
/// before. Filters past 4 do nothing (the caller refuses them).
let private unfilterFrom (filter: byte) (cur: nativeptr<byte>) (up: nativeptr<byte>) (from: int) (count: int) (step: int) =
    let inline add (i: int) (v: int) = NativePtr.set cur i (byte (get cur i + v))
    let first = min step count
    match filter with
    | 1uy ->
        for i in max from step .. count - 1 do
            add i (get cur (i - step))
    | 2uy ->
        for i in from .. count - 1 do
            add i (get up i)
    | 3uy ->
        for i in from .. first - 1 do
            add i (get up i >>> 1)
        for i in max from step .. count - 1 do
            add i ((get cur (i - step) + get up i) >>> 1)
    | 4uy ->
        for i in from .. first - 1 do
            add i (get up i)
        for i in max from step .. count - 1 do
            let a = get cur (i - step)
            let b = get up i
            let c = get up (i - step)
            // Paeth's distances from p = a + b - c, written without p: |p - a| = |b - c| and so on. Branch free
            // (noisy pictures make the branches unpredictable), ties going to a, then b, as the spec says.
            let inline absolute (v: int) = (v ^^^ (v >>> 31)) - (v >>> 31)
            let pa = absolute (b - c)
            let pb = absolute (a - c)
            let pc = absolute (a + b - c - c)
            let mutable best = a
            let mutable distance = pa
            if pb < distance then
                best <- b
                distance <- pb
            if pc < distance then best <- c
            add i best
    | _ -> ()

/// Undoes one row's filter in place, a byte at a time.
let unfilterScalar (filter: byte) (cur: nativeptr<byte>) (up: nativeptr<byte>) (count: int) (step: int) =
    unfilterFrom filter cur up 0 count step

/// One pixel in the low bytes of a vector: 4 bytes loaded for 3 or 4 bytes a pixel, 8 for 6 or 8 (`wide`). For 3
/// and 6 the extra bytes are the next pixel's: each byte keeps to its own lane, and they are never stored.
let inline private loadPixel (p: nativeptr<byte>) (wide: bool) : Vector128<byte> =
    if wide then
        Vector128.CreateScalar(Unsafe.ReadUnaligned<uint64>(NativePtr.toVoidPtr p)).AsByte()
    else
        Vector128.CreateScalar(Unsafe.ReadUnaligned<uint32>(NativePtr.toVoidPtr p)).AsByte()

/// Stores exactly `step` bytes (3, 4, 6 or 8) from the low end of a vector.
let inline private storePixel (v: Vector128<byte>) (p: nativeptr<byte>) (step: int) =
    if step = 4 then
        Unsafe.WriteUnaligned<uint32>(NativePtr.toVoidPtr p, v.AsUInt32().ToScalar())
    elif step = 3 then
        let x = v.AsUInt32().ToScalar()
        Unsafe.WriteUnaligned<uint16>(NativePtr.toVoidPtr p, uint16 x)
        NativePtr.set p 2 (byte (x >>> 16))
    elif step = 8 then
        Unsafe.WriteUnaligned<uint64>(NativePtr.toVoidPtr p, v.AsUInt64().ToScalar())
    else
        let x = v.AsUInt64().ToScalar()
        Unsafe.WriteUnaligned<uint32>(NativePtr.toVoidPtr p, uint32 x)
        Unsafe.WriteUnaligned<uint16>(NativePtr.toVoidPtr (NativePtr.add p 4), uint16 (x >>> 32))

// The three loops below undo a filter a pixel at a time, the pixel before kept in a register (starting as zeros,
// which is what the filters take to the left of a row), and return how many bytes they did: every pixel a whole
// load fits in, so the last pixel of a 3- or 6-byte row is left to the scalar loop. Inline, so `step` is a constant
// in each use.

let inline private subPixels (cur: nativeptr<byte>) (count: int) (step: int) : int =
    let wide = step > 4
    let load = if wide then 8 else 4
    let mutable a = Vector128<byte>.Zero
    let mutable i = 0
    while i + load <= count do
        let p = NativePtr.add cur i
        a <- a + loadPixel p wide
        storePixel a p step
        i <- i + step
    i

let inline private averagePixels (cur: nativeptr<byte>) (up: nativeptr<byte>) (count: int) (step: int) : int =
    let wide = step > 4
    let load = if wide then 8 else 4
    let mutable a = Vector128<byte>.Zero
    let mutable i = 0
    while i + load <= count do
        let p = NativePtr.add cur i
        let b = loadPixel (NativePtr.add up i) wide
        // (a + b) >> 1 without the ninth bit: the bits they share, plus half of those they don't.
        a <- loadPixel p wide + ((a &&& b) + Vector128.ShiftRightLogical(a ^^^ b, 1))
        storePixel a p step
        i <- i + step
    i

let inline private paethPixels (cur: nativeptr<byte>) (up: nativeptr<byte>) (count: int) (step: int) : int =
    let wide = step > 4
    let load = if wide then 8 else 4
    let low = Vector128.Create(255s)
    // In 16-bit lanes, as the distances need a sign: a is the pixel before, c the one above it.
    let mutable a = Vector128<int16>.Zero
    let mutable c = Vector128<int16>.Zero
    let mutable i = 0
    while i + load <= count do
        let p = NativePtr.add cur i
        let b = Vector128.WidenLower(loadPixel (NativePtr.add up i) wide).AsInt16()
        let x = Vector128.WidenLower(loadPixel p wide).AsInt16()
        let da = b - c
        let db = a - c
        let pa = Vector128.Abs da
        let pb = Vector128.Abs db
        let pc = Vector128.Abs(da + db)
        let smallest = Vector128.Min(pa, Vector128.Min(pb, pc))
        // Ties go to a, then b, as in the scalar loop.
        let nearest =
            Vector128.ConditionalSelect(
                Vector128.Equals(smallest, pa),
                a,
                Vector128.ConditionalSelect(Vector128.Equals(smallest, pb), b, c)
            )
        a <- (x + nearest) &&& low
        c <- b
        storePixel (Vector128.Narrow(a.AsUInt16(), a.AsUInt16())) p step
        i <- i + step
    i

/// Undoes one row's filter in place with `Vector128` where the filter and `step` allow it (Up always; Sub, Average
/// and Paeth for 3, 4, 6 and 8 bytes a pixel), the rest and the tail a byte at a time.
let unfilterVector (filter: byte) (cur: nativeptr<byte>) (up: nativeptr<byte>) (count: int) (step: int) =
    let mutable from = 0
    match filter with
    | 1uy ->
        from <-
            match step with
            | 3 -> subPixels cur count 3
            | 4 -> subPixels cur count 4
            | 6 -> subPixels cur count 6
            | 8 -> subPixels cur count 8
            | _ -> 0
    | 2uy ->
        while from + 16 <= count do
            let p = NativePtr.add cur from
            Vector128.Store(Vector128.Load p + Vector128.Load(NativePtr.add up from), p)
            from <- from + 16
    | 3uy ->
        from <-
            match step with
            | 3 -> averagePixels cur up count 3
            | 4 -> averagePixels cur up count 4
            | 6 -> averagePixels cur up count 6
            | 8 -> averagePixels cur up count 8
            | _ -> 0
    | 4uy ->
        from <-
            match step with
            | 3 -> paethPixels cur up count 3
            | 4 -> paethPixels cur up count 4
            | 6 -> paethPixels cur up count 6
            | 8 -> paethPixels cur up count 8
            | _ -> 0
    | _ -> ()
    unfilterFrom filter cur up from count step

/// Undoes one row's filter in place: `count` bytes at `cur` (after its filter byte), the row above (already
/// unfiltered, or zeros) at `up`, `step` bytes between a byte and the same byte of the pixel before. This is the
/// decoder's hot loop.
let unfilter (filter: byte) (cur: nativeptr<byte>) (up: nativeptr<byte>) (count: int) (step: int) =
    if useVectors () then unfilterVector filter cur up count step
    else unfilterScalar filter cur up count step

// ---- Rows to premultiplied RGBA ------------------------------------------------------------------------

let private rgba8From (src: nativeptr<byte>) (dst: nativeptr<byte>) (from: int) (width: int) (dstStep: int) =
    for x in from .. width - 1 do
        let s = NativePtr.add src (x * 4)
        let d = NativePtr.add dst (x * dstStep)
        let a = int (NativePtr.get s 3)
        if a = 255 then
            Unsafe.WriteUnaligned<uint32>(NativePtr.toVoidPtr d, Unsafe.ReadUnaligned<uint32>(NativePtr.toVoidPtr s))
        elif a = 0 then
            Unsafe.WriteUnaligned<uint32>(NativePtr.toVoidPtr d, 0u)
        else
            NativePtr.set d 0 (byte (Pixels.premultiply (int (NativePtr.get s 0)) a))
            NativePtr.set d 1 (byte (Pixels.premultiply (int (NativePtr.get s 1)) a))
            NativePtr.set d 2 (byte (Pixels.premultiply (int (NativePtr.get s 2)) a))
            NativePtr.set d 3 (byte a)

/// 8-bit straight RGBA (`width` pixels at `src`) premultiplied into `dst`, a pixel every `dstStep` bytes (4, or
/// more for an Adam7 pass), a pixel at a time.
let rgba8Scalar (src: nativeptr<byte>) (dst: nativeptr<byte>) (width: int) (dstStep: int) =
    rgba8From src dst 0 width dstStep

/// As `rgba8Scalar`, four pixels at a time when they are packed (`dstStep` 4).
let rgba8Vector (src: nativeptr<byte>) (dst: nativeptr<byte>) (width: int) (dstStep: int) =
    let mutable x = 0
    if dstStep = 4 then
        // Each pixel's alpha beside its three colours, and 255 beside the alpha itself (a · 255 / 255 = a).
        let alphas = Vector128.Create(3uy, 3uy, 3uy, 3uy, 7uy, 7uy, 7uy, 7uy, 11uy, 11uy, 11uy, 11uy, 15uy, 15uy, 15uy, 15uy)
        let keep = Vector128.Create(0xFF000000u).AsByte()
        let half = Vector128.Create(128us)
        // (c · a + 127) / 255 without dividing: with t = c · a + 128 it is (t + (t >> 8)) >> 8 for every c and a.
        let inline scale (c: Vector128<uint16>) (a: Vector128<uint16>) =
            let t = c * a + half
            Vector128.ShiftRightLogical(t + Vector128.ShiftRightLogical(t, 8), 8)
        while x + 4 <= width do
            let v = Vector128.Load(NativePtr.add src (x * 4))
            let m = Vector128.Shuffle(v, alphas) ||| keep
            let lo = scale (Vector128.WidenLower v) (Vector128.WidenLower m)
            let hi = scale (Vector128.WidenUpper v) (Vector128.WidenUpper m)
            Vector128.Store(Vector128.Narrow(lo, hi), NativePtr.add dst (x * 4))
            x <- x + 4
    rgba8From src dst x width dstStep

/// 8-bit RGBA rows, the commonest kind.
let rgba8 (src: nativeptr<byte>) (dst: nativeptr<byte>) (width: int) (dstStep: int) =
    if useVectors () then rgba8Vector src dst width dstStep
    else rgba8Scalar src dst width dstStep

let private rgb8From (src: nativeptr<byte>) (dst: nativeptr<byte>) (from: int) (width: int) (dstStep: int) =
    for x in from .. width - 1 do
        let s = NativePtr.add src (x * 3)
        let d = NativePtr.add dst (x * dstStep)
        NativePtr.set d 0 (NativePtr.get s 0)
        NativePtr.set d 1 (NativePtr.get s 1)
        NativePtr.set d 2 (NativePtr.get s 2)
        NativePtr.set d 3 255uy

/// 8-bit RGB without a colour key (`width` pixels at `src`) as opaque RGBA into `dst`, a pixel every `dstStep`
/// bytes, a pixel at a time.
let rgb8Scalar (src: nativeptr<byte>) (dst: nativeptr<byte>) (width: int) (dstStep: int) =
    rgb8From src dst 0 width dstStep

/// As `rgb8Scalar`, four pixels at a time when they are packed (`dstStep` 4): 16 bytes loaded, 12 of them used.
let rgb8Vector (src: nativeptr<byte>) (dst: nativeptr<byte>) (width: int) (dstStep: int) =
    let mutable x = 0
    if dstStep = 4 then
        let spread = Vector128.Create(0uy, 1uy, 2uy, 0uy, 3uy, 4uy, 5uy, 0uy, 6uy, 7uy, 8uy, 0uy, 9uy, 10uy, 11uy, 0uy)
        let opaque = Vector128.Create(0xFF000000u).AsByte()
        while x * 3 + 16 <= width * 3 do
            let v = Vector128.Load(NativePtr.add src (x * 3))
            Vector128.Store(Vector128.Shuffle(v, spread) ||| opaque, NativePtr.add dst (x * 4))
            x <- x + 4
    rgb8From src dst x width dstStep

/// 8-bit RGB rows without a colour key.
let rgb8 (src: nativeptr<byte>) (dst: nativeptr<byte>) (width: int) (dstStep: int) =
    if useVectors () then rgb8Vector src dst width dstStep
    else rgb8Scalar src dst width dstStep
