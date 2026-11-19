/// Our own PNG decoder (DESIGN.md, "M8 step 3b", decisions 1–4), and the encoder at the end. The decoder reads
/// every static PNG (all colour types and bit depths, tRNS, Adam7 interlacing), 16-bit channels rounded to 8, alpha premultiplied, sRGB assumed (gAMA, iCCP,
/// cHRM, sRGB and sBIT ignored); an APNG shows its default image (IDAT). Compression is the BCL's `ZLibStream`.
/// Corrupt files throw `ImageFormatException`: a bad CRC on a critical chunk, an unknown critical chunk, bad
/// header values, a missing palette or image data, image data that ends early. Ancillary chunks with a bad CRC
/// are skipped, as libpng does.
module Scribble.Imaging.Png

open System
open System.IO
open System.IO.Compression
open Microsoft.FSharp.NativeInterop

let private signature = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]

/// Whether the data starts with PNG's signature.
let isPng (data: byte[]) =
    data.Length >= 8 && ReadOnlySpan<byte>(data, 0, 8).SequenceEqual(ReadOnlySpan<byte>(signature))

let private fail (message: string) : 'T = raise (ImageFormatException("PNG: " + message))

let inline private u32 (d: byte[]) (i: int) =
    (uint32 d[i] <<< 24) ||| (uint32 d[i + 1] <<< 16) ||| (uint32 d[i + 2] <<< 8) ||| uint32 d[i + 3]

let inline private u16 (d: byte[]) (i: int) = (int d[i] <<< 8) ||| int d[i + 1]

type private Header =
    { Width: int
      Height: int
      BitDepth: int
      ColorType: int
      Interlaced: bool }

    member h.Channels =
        match h.ColorType with
        | 0 | 3 -> 1
        | 4 -> 2
        | 2 -> 3
        | _ -> 4

    /// Bytes in one row of `width` pixels, without its filter byte.
    member h.RowBytes(width: int) = (int64 width * int64 (h.Channels * h.BitDepth) + 7L) / 8L

    /// The distance between a byte and the same byte of the pixel before, for the filters (at least 1).
    member h.FilterStep = max 1 (h.Channels * h.BitDepth / 8)

/// A chunk's place in the data: its 4-letter type at `Type`, its data at `Start`, `Length` bytes.
[<Struct>]
type private Chunk = { Type: uint32; Start: int; Length: int; CrcOk: bool }

let private chunkType (s: string) =
    (uint32 s[0] <<< 24) ||| (uint32 s[1] <<< 16) ||| (uint32 s[2] <<< 8) ||| uint32 s[3]

let private IHDR = chunkType "IHDR"
let private PLTE = chunkType "PLTE"
let private tRNS = chunkType "tRNS"
let private IDAT = chunkType "IDAT"
let private IEND = chunkType "IEND"

let private typeName (t: uint32) =
    String([| for shift in [ 24; 16; 8; 0 ] -> char ((t >>> shift) &&& 0xFFu) |])

/// The chunk starting at `pos`, or ValueNone at the exact end of the data.
let private readChunk (data: byte[]) (pos: int) : Chunk voption =
    if pos = data.Length then
        ValueNone
    elif data.Length - pos < 12 then
        fail "the file ends inside a chunk"
    else
        let length = u32 data pos
        if length > 0x7FFFFFFFu || int64 data.Length - int64 pos - 12L < int64 length then
            fail ("the file ends inside a chunk (" + typeName (u32 data (pos + 4)) + ")")
        let length = int length
        let stored = u32 data (pos + 8 + length)
        ValueSome
            { Type = u32 data (pos + 4)
              Start = pos + 8
              Length = length
              CrcOk = Crc32.compute data (pos + 4) (length + 4) = stored }

let private readHeader (data: byte[]) : Header =
    if not (isPng data) then fail "not a PNG file (its signature is wrong)"
    match readChunk data 8 with
    | ValueSome c when c.Type = IHDR && c.Length = 13 ->
        if not c.CrcOk then fail "the header (IHDR) is corrupt: its CRC doesn't match"
        let s = c.Start
        let width = u32 data s
        let height = u32 data (s + 4)
        let bitDepth = int data[s + 8]
        let colorType = int data[s + 9]
        if width = 0u || height = 0u || width > 0x7FFFFFFFu || height > 0x7FFFFFFFu then
            fail ("bad size " + string width + " × " + string height)
        let depthOk =
            match colorType with
            | 0 -> bitDepth = 1 || bitDepth = 2 || bitDepth = 4 || bitDepth = 8 || bitDepth = 16
            | 3 -> bitDepth = 1 || bitDepth = 2 || bitDepth = 4 || bitDepth = 8
            | 2 | 4 | 6 -> bitDepth = 8 || bitDepth = 16
            | _ -> fail ("unknown colour type " + string colorType)
        if not depthOk then
            fail ("bit depth " + string bitDepth + " isn't allowed for colour type " + string colorType)
        if data[s + 10] <> 0uy then fail ("unknown compression method " + string data[s + 10])
        if data[s + 11] <> 0uy then fail ("unknown filter method " + string data[s + 11])
        if data[s + 12] > 1uy then fail ("unknown interlace method " + string data[s + 12])
        { Width = int width
          Height = int height
          BitDepth = bitDepth
          ColorType = colorType
          Interlaced = data[s + 12] = 1uy }
    | _ -> fail "the file doesn't start with a header (IHDR)"

/// The image's size from its header, without decoding it.
let readInfo (data: byte[]) : ImageInfo =
    let h = readHeader data
    { Width = h.Width; Height = h.Height }

// ---- Rows to RGBA ------------------------------------------------------------------------------------

/// What turns samples into premultiplied RGBA: the palette (256 entries, premultiplied, opaque black past its
/// end, as browsers show out-of-range indices) and the tRNS colour key (raw samples; -1 when there is none).
type private Colours =
    { Palette: byte[]
      KeyGrey: int
      KeyR: int
      KeyG: int
      KeyB: int }

/// One unfiltered row (`width` pixels at `src` in `buf`) written as premultiplied RGBA to `dst`, a pixel every
/// `dstStep` bytes (4, or more for an Adam7 pass).
let private convertRow (h: Header) (k: Colours) (buf: byte[]) (src: int) (width: int) (out: byte[]) (dst: int) (dstStep: int) =
    let inline put (o: int) (r: int) (g: int) (b: int) (a: int) =
        out[o] <- byte (Pixels.premultiply r a)
        out[o + 1] <- byte (Pixels.premultiply g a)
        out[o + 2] <- byte (Pixels.premultiply b a)
        out[o + 3] <- byte a
    let inline opaque (o: int) (r: int) (g: int) (b: int) =
        out[o] <- byte r
        out[o + 1] <- byte g
        out[o + 2] <- byte b
        out[o + 3] <- 255uy
    let inline clear (o: int) =
        out[o] <- 0uy
        out[o + 1] <- 0uy
        out[o + 2] <- 0uy
        out[o + 3] <- 0uy
    match h.ColorType, h.BitDepth with
    | 0, 16 ->
        for x in 0 .. width - 1 do
            let v = u16 buf (src + x * 2)
            let o = dst + x * dstStep
            if v = k.KeyGrey then clear o
            else
                let g = Pixels.round16 v
                opaque o g g g
    | 0, depth ->
        let mask = (1 <<< depth) - 1
        let scale = 255 / mask
        for x in 0 .. width - 1 do
            let bit = x * depth
            let v = (int buf[src + (bit >>> 3)] >>> (8 - depth - (bit &&& 7))) &&& mask
            let o = dst + x * dstStep
            if v = k.KeyGrey then clear o
            else
                let g = v * scale
                opaque o g g g
    | 3, depth ->
        let mask = (1 <<< depth) - 1
        let p = k.Palette
        for x in 0 .. width - 1 do
            let bit = x * depth
            let i = ((int buf[src + (bit >>> 3)] >>> (8 - depth - (bit &&& 7))) &&& mask) * 4
            let o = dst + x * dstStep
            out[o] <- p[i]
            out[o + 1] <- p[i + 1]
            out[o + 2] <- p[i + 2]
            out[o + 3] <- p[i + 3]
    | 2, 8 ->
        for x in 0 .. width - 1 do
            let s = src + x * 3
            let r = int buf[s]
            let g = int buf[s + 1]
            let b = int buf[s + 2]
            let o = dst + x * dstStep
            if r = k.KeyR && g = k.KeyG && b = k.KeyB then clear o else opaque o r g b
    | 2, _ ->
        for x in 0 .. width - 1 do
            let s = src + x * 6
            let r = u16 buf s
            let g = u16 buf (s + 2)
            let b = u16 buf (s + 4)
            let o = dst + x * dstStep
            if r = k.KeyR && g = k.KeyG && b = k.KeyB then clear o
            else opaque o (Pixels.round16 r) (Pixels.round16 g) (Pixels.round16 b)
    | 4, 8 ->
        for x in 0 .. width - 1 do
            let s = src + x * 2
            let g = int buf[s]
            put (dst + x * dstStep) g g g (int buf[s + 1])
    | 4, _ ->
        for x in 0 .. width - 1 do
            let s = src + x * 4
            let g = Pixels.round16 (u16 buf s)
            put (dst + x * dstStep) g g g (Pixels.round16 (u16 buf (s + 2)))
    | _, 8 ->
        for x in 0 .. width - 1 do
            let s = src + x * 4
            put (dst + x * dstStep) (int buf[s]) (int buf[s + 1]) (int buf[s + 2]) (int buf[s + 3])
    | _ ->
        for x in 0 .. width - 1 do
            let s = src + x * 8
            put
                (dst + x * dstStep)
                (Pixels.round16 (u16 buf s))
                (Pixels.round16 (u16 buf (s + 2)))
                (Pixels.round16 (u16 buf (s + 4)))
                (Pixels.round16 (u16 buf (s + 6)))

// ---- Decoding ----------------------------------------------------------------------------------------

/// Adam7's passes: first column, first row, column step, row step.
let private passes = [| 0, 0, 8, 8; 4, 0, 8, 8; 0, 4, 4, 8; 2, 0, 4, 4; 0, 2, 2, 4; 1, 0, 2, 2; 0, 1, 1, 2 |]

/// The passes that hold pixels, with their sizes: (x0, y0, dx, dy, width, height). A plain image is one pass.
let private passesOf (h: Header) =
    if not h.Interlaced then
        [| 0, 0, 1, 1, h.Width, h.Height |]
    else
        passes
        |> Array.map (fun (x0, y0, dx, dy) -> x0, y0, dx, dy, (h.Width - x0 + dx - 1) / dx, (h.Height - y0 + dy - 1) / dy)
        |> Array.filter (fun (_, _, _, _, w, ht) -> w > 0 && ht > 0)

let private inflate (data: byte[]) (idat: ResizeArray<Chunk>) (size: int) : byte[] =
    let total = idat |> Seq.sumBy (fun c -> int64 c.Length)
    if total > int64 Array.MaxLength then fail "too much image data"
    let compressed = Array.zeroCreate<byte> (int total)
    let mutable at = 0
    for c in idat do
        Buffer.BlockCopy(data, c.Start, compressed, at, c.Length)
        at <- at + c.Length
    // Deflate packs at most about 1,032 to 1: data that can't fill the size the header claims fails before anything
    // is allocated for it.
    if int64 size > total * 1040L + 1024L then fail "the image data ends early"
    // The buffer grows as data actually inflates, so a file that claims more than it holds costs what it holds.
    let mutable result = GC.AllocateUninitializedArray<byte>(int (min (int64 size) (max 65536L (total * 32L))))
    let mutable read = 0
    try
        use z = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress)
        let mutable reading = true
        while reading do
            if read = result.Length then
                if read = size then reading <- false
                else
                    let bigger = GC.AllocateUninitializedArray<byte>(int (min (int64 size) (int64 result.Length * 2L)))
                    Buffer.BlockCopy(result, 0, bigger, 0, read)
                    result <- bigger
            if reading then
                let n = z.Read(Span<byte>(result, read, result.Length - read))
                if n = 0 then reading <- false else read <- read + n
    with
    | :? InvalidDataException as e -> fail ("the image data is corrupt (" + e.Message + ")")
    | :? IOException as e -> fail ("the image data is corrupt (" + e.Message + ")") // ZLibException
    if read < size then fail "the image data ends early"
    result

let private colours (h: Header) (data: byte[]) (plte: Chunk voption) (trns: Chunk voption) : Colours =
    let palette = Array.zeroCreate<byte> (256 * 4)
    for i in 0..255 do
        palette[i * 4 + 3] <- 255uy
    let mutable keyGrey, keyR, keyG, keyB = -1, -1, -1, -1
    match plte with
    | ValueSome p when h.ColorType = 3 ->
        if p.Length % 3 <> 0 || p.Length = 0 || p.Length > 768 then
            fail ("the palette (PLTE) has a bad length, " + string p.Length)
        let entries = p.Length / 3
        let alpha (i: int) =
            match trns with
            | ValueSome t when i < t.Length -> int data[t.Start + i]
            | _ -> 255
        for i in 0 .. entries - 1 do
            let a = alpha i
            let s = p.Start + i * 3
            palette[i * 4] <- byte (Pixels.premultiply (int data[s]) a)
            palette[i * 4 + 1] <- byte (Pixels.premultiply (int data[s + 1]) a)
            palette[i * 4 + 2] <- byte (Pixels.premultiply (int data[s + 2]) a)
            palette[i * 4 + 3] <- byte a
    | ValueNone when h.ColorType = 3 -> fail "a palette image without its palette (PLTE)"
    | _ -> () // a suggested palette for a true-colour image: not needed
    match trns with
    | ValueSome t when h.ColorType = 0 && t.Length >= 2 -> keyGrey <- u16 data t.Start
    | ValueSome t when h.ColorType = 2 && t.Length >= 6 ->
        keyR <- u16 data t.Start
        keyG <- u16 data (t.Start + 2)
        keyB <- u16 data (t.Start + 4)
    | _ -> ()
    { Palette = palette
      KeyGrey = keyGrey
      KeyR = keyR
      KeyG = keyG
      KeyB = keyB }

/// Decodes a PNG file to premultiplied RGBA. Throws `ImageFormatException` when it can't.
let decode (data: byte[]) : DecodedImage =
    let h = readHeader data
    if int64 h.Width * int64 h.Height > Pixels.maxDecodePixels then
        fail ("too big to decode, " + string h.Width + " × " + string h.Height)
    let mutable plte = ValueNone
    let mutable trns = ValueNone
    let idat = ResizeArray<Chunk>()
    let mutable pos = 8 + 25 // after the signature and IHDR
    let mutable ended = false
    while not ended do
        match readChunk data pos with
        | ValueNone -> ended <- true // no IEND: fine if the image data is all there
        | ValueSome c ->
            pos <- c.Start + c.Length + 4
            let critical = (c.Type >>> 24) &&& 0x20u = 0u
            if not c.CrcOk then
                if critical then fail ("the " + typeName c.Type + " chunk is corrupt: its CRC doesn't match")
            elif c.Type = IEND then ended <- true
            elif c.Type = IDAT then idat.Add c
            elif c.Type = PLTE then plte <- ValueSome c
            elif c.Type = tRNS then trns <- ValueSome c
            elif c.Type = IHDR then fail "a second header (IHDR)"
            elif critical then fail ("unknown critical chunk " + typeName c.Type)
    if idat.Count = 0 then fail "no image data (IDAT)"
    let k = colours h data plte trns
    let layout = passesOf h
    let size =
        layout |> Array.sumBy (fun (_, _, _, _, w, ht) -> int64 ht * (1L + h.RowBytes w))
    if size > int64 Array.MaxLength then fail ("too big to decode, " + string h.Width + " × " + string h.Height)
    let raw = inflate data idat (int size)
    let out = GC.AllocateUninitializedArray<byte> (h.Width * h.Height * 4) // every pixel is written (all passes)
    let step = h.FilterStep
    let zeros = Array.zeroCreate<byte> (int (h.RowBytes h.Width) + step)
    let fast = h.BitDepth = 8 && (h.ColorType = 6 || (h.ColorType = 2 && k.KeyR < 0))
    // Pinned once; every pointer below stays inside them: `raw` is exactly the passes' rows with their filter
    // bytes, `out` exactly Width × Height pixels, `zeros` a row above the first with `step` bytes before it.
    use rawPtr = fixed raw
    use zerosPtr = fixed zeros
    use outPtr = fixed out
    let mutable at = 0
    for x0, y0, dx, dy, w, ht in layout do
        let rowBytes = int (h.RowBytes w)
        for y in 0 .. ht - 1 do
            let row = at + 1
            let cur = NativePtr.add rawPtr row
            let up = if y = 0 then NativePtr.add zerosPtr step else NativePtr.add rawPtr (row - rowBytes - 1)
            let filter = raw[at]
            if filter > 4uy then fail ("unknown row filter " + string filter)
            Rows.unfilter filter cur up rowBytes step
            let dst = ((y0 + y * dy) * h.Width + x0) * 4
            if fast && h.ColorType = 6 then Rows.rgba8 cur (NativePtr.add outPtr dst) w (dx * 4)
            elif fast then Rows.rgb8 cur (NativePtr.add outPtr dst) w (dx * 4)
            else convertRow h k raw row w out dst (dx * 4)
            at <- at + 1 + rowBytes
    DecodedImage(h.Width, h.Height, out)

// --- Encoding (DESIGN.md, "Rendering to an image and saving it", decision 1) ------------------------------

let private writeU32 (out: Stream) (v: uint32) =
    out.WriteByte(byte (v >>> 24))
    out.WriteByte(byte (v >>> 16))
    out.WriteByte(byte (v >>> 8))
    out.WriteByte(byte v)

let private writeChunk (out: Stream) (kind: string) (body: byte[]) =
    let chunk = Array.zeroCreate<byte> (4 + body.Length)
    for i in 0..3 do
        chunk[i] <- byte kind[i]
    Array.blit body 0 chunk 4 body.Length
    writeU32 out (uint32 body.Length)
    out.Write(chunk, 0, chunk.Length)
    writeU32 out (Crc32.compute chunk 0 chunk.Length)

let inline private paeth (a: int) (b: int) (c: int) =
    let p = a + b - c
    let pa = abs (p - a)
    let pb = abs (p - b)
    let pc = abs (p - c)
    if pa <= pb && pa <= pc then a elif pb <= pc then b else c

/// Filters `cur` (one row of `count` bytes, `step` bytes a pixel) against `up` (the row above, zeros for the first)
/// with `filter` into `dst` (from 1; `dst[0]` is the filter byte), and returns the row's cost: the sum of its bytes
/// read as signed, the usual heuristic for which filter compresses best.
let private filterRow (filter: int) (cur: byte[]) (curAt: int) (up: byte[]) (upAt: int) (count: int) (step: int) (dst: byte[]) =
    dst[0] <- byte filter
    let mutable cost = 0
    for i in 0 .. count - 1 do
        let x = int cur[curAt + i]
        let a = if i >= step then int cur[curAt + i - step] else 0
        let b = int up[upAt + i]
        let c = if i >= step then int up[upAt + i - step] else 0
        let v =
            match filter with
            | 0 -> x
            | 1 -> x - a
            | 2 -> x - b
            | 3 -> x - ((a + b) >>> 1)
            | _ -> x - paeth a b c
        let f = byte v
        dst[i + 1] <- f
        cost <- cost + (if f < 128uy then int f else 256 - int f)
    cost

/// A PNG of straight (not premultiplied) RGBA pixels, `width × height × 4` bytes, row by row from the top: 8 bits a
/// channel, RGB when every pixel is opaque (a quarter smaller), the filter each row compresses best with (the
/// smallest sum of its bytes), `ZLibStream` at its smallest.
let encode (width: int, height: int, rgba: byte[]) : byte[] =
    if isNull rgba then nullArg (nameof rgba)
    if width < 1 || height < 1 || int64 width * int64 height * 4L <> int64 rgba.Length then
        invalidArg (nameof rgba) "Pixels must be width × height × 4 bytes (RGBA), at least 1 × 1."
    let mutable opaque = true
    let mutable i = 3
    while opaque && i < rgba.Length do
        if rgba[i] <> 255uy then opaque <- false
        i <- i + 4
    let step = if opaque then 3 else 4
    let rowBytes = width * step
    // The rows in the file's channel order (RGBA copied as is, else RGB taken out of it).
    let pixels =
        if not opaque then rgba
        else
            let p = GC.AllocateUninitializedArray<byte> (width * height * 3)
            let mutable s = 0
            let mutable d = 0
            while s < rgba.Length do
                p[d] <- rgba[s]
                p[d + 1] <- rgba[s + 1]
                p[d + 2] <- rgba[s + 2]
                s <- s + 4
                d <- d + 3
            p
    let zeros = Array.zeroCreate<byte> rowBytes
    let best = Array.zeroCreate<byte> (rowBytes + 1)
    let trial = Array.zeroCreate<byte> (rowBytes + 1)
    use compressed = new MemoryStream()
    (
        use z = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)
        for y in 0 .. height - 1 do
            let at = y * rowBytes
            let up, upAt = if y = 0 then zeros, 0 else pixels, at - rowBytes
            let mutable bestCost = filterRow 0 pixels at up upAt rowBytes step best
            for f in 1..4 do
                let cost = filterRow f pixels at up upAt rowBytes step trial
                if cost < bestCost then
                    bestCost <- cost
                    Array.blit trial 0 best 0 trial.Length
            z.Write(best, 0, best.Length)
    )
    use out = new MemoryStream()
    out.Write(signature, 0, signature.Length)
    let header = Array.zeroCreate<byte> 13
    let put (at: int) (v: int) =
        header[at] <- byte (v >>> 24)
        header[at + 1] <- byte (v >>> 16)
        header[at + 2] <- byte (v >>> 8)
        header[at + 3] <- byte v
    put 0 width
    put 4 height
    header[8] <- 8uy // bit depth
    header[9] <- (if opaque then 2uy else 6uy) // colour type: RGB or RGBA
    writeChunk out "IHDR" header
    writeChunk out "IDAT" (compressed.ToArray())
    writeChunk out "IEND" [||]
    out.ToArray()
