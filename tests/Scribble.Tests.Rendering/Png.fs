/// Minimal PNG reading and writing for screenshot tests: 8-bit RGBA, no interlacing.
/// Decoding understands every filter type, so references edited or re-saved by other tools load too.
module Scribble.Tests.Rendering.Png

open System
open System.IO
open System.IO.Compression
open Scribble.Platform
open Scribble.Rendering

let private signature = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]

let private crcTable =
    Array.init 256 (fun n ->
        let mutable c = uint32 n
        for _ in 0..7 do
            c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
        c)

let private crc (bytes: byte[]) =
    let mutable c = 0xFFFFFFFFu
    for b in bytes do
        c <- crcTable[int ((c ^^^ uint32 b) &&& 0xFFu)] ^^^ (c >>> 8)
    c ^^^ 0xFFFFFFFFu

let private bigEndian (v: uint32) =
    [| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |]

let private writeChunk (out: Stream) (kind: string) (data: byte[]) =
    out.Write(bigEndian (uint32 data.Length))
    let body = Array.append (Text.Encoding.ASCII.GetBytes kind) data
    out.Write body
    out.Write(bigEndian (crc body))

/// Encodes a bitmap as an RGBA PNG.
let encode (bitmap: Bitmap) : byte[] =
    let w, h = bitmap.Size.Width, bitmap.Size.Height
    use out = new MemoryStream()
    out.Write signature
    writeChunk out "IHDR" (Array.concat [ bigEndian (uint32 w); bigEndian (uint32 h); [| 8uy; 6uy; 0uy; 0uy; 0uy |] ])
    let raw = new MemoryStream()
    (
        use z = new ZLibStream(raw, CompressionLevel.Optimal, true)
        for y in 0 .. h - 1 do
            z.WriteByte 0uy // filter: none
            z.Write(bitmap.Pixels, y * w * 4, w * 4)
    )
    writeChunk out "IDAT" (raw.ToArray())
    writeChunk out "IEND" [||]
    out.ToArray()

let save (path: string) (bitmap: Bitmap) =
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath path)) |> ignore
    File.WriteAllBytes(path, encode bitmap)

/// Decodes an 8-bit RGBA (color type 6) or RGB (color type 2) PNG.
let decode (png: byte[]) : Bitmap =
    if png.Length < 8 || png[0..7] <> signature then invalidArg (nameof png) "Not a PNG file."
    let readU32 i = (uint32 png[i] <<< 24) ||| (uint32 png[i + 1] <<< 16) ||| (uint32 png[i + 2] <<< 8) ||| uint32 png[i + 3]
    let mutable pos = 8
    let mutable width, height, channels = 0, 0, 0
    let idat = new MemoryStream()
    let mutable finished = false
    while not finished do
        let length = int (readU32 pos)
        let kind = Text.Encoding.ASCII.GetString(png, pos + 4, 4)
        let data = pos + 8
        match kind with
        | "IHDR" ->
            width <- int (readU32 data)
            height <- int (readU32 (data + 4))
            if png[data + 8] <> 8uy || png[data + 12] <> 0uy then invalidOp "Only 8-bit, non-interlaced PNGs are supported."
            channels <-
                match png[data + 9] with
                | 6uy -> 4
                | 2uy -> 3
                | t -> invalidOp $"Unsupported PNG color type {t}."
        | "IDAT" -> idat.Write(png, data, length)
        | "IEND" -> finished <- true
        | _ -> ()
        pos <- data + length + 4

    idat.Position <- 0L
    use z = new ZLibStream(idat, CompressionMode.Decompress)
    let stride = width * channels
    let raw = Array.zeroCreate<byte> ((stride + 1) * height)
    let mutable read = 0
    while read < raw.Length do
        let n = z.Read(raw, read, raw.Length - read)
        if n = 0 then invalidOp "Truncated PNG data."
        read <- read + n

    let rows = Array.zeroCreate<byte> (stride * height)
    for y in 0 .. height - 1 do
        let filter = raw[y * (stride + 1)]
        for x in 0 .. stride - 1 do
            let value = raw[y * (stride + 1) + 1 + x]
            let a = if x >= channels then int rows[y * stride + x - channels] else 0
            let b = if y > 0 then int rows[(y - 1) * stride + x] else 0
            let c = if x >= channels && y > 0 then int rows[(y - 1) * stride + x - channels] else 0
            let predicted =
                match filter with
                | 0uy -> 0
                | 1uy -> a
                | 2uy -> b
                | 3uy -> (a + b) / 2
                | 4uy ->
                    let p = a + b - c
                    let pa, pb, pc = abs (p - a), abs (p - b), abs (p - c)
                    if pa <= pb && pa <= pc then a elif pb <= pc then b else c
                | f -> invalidOp $"Bad PNG filter {f}."
            rows[y * stride + x] <- byte ((int value + predicted) &&& 0xFF)

    let pixels =
        if channels = 4 then rows
        else Array.init (width * height * 4) (fun i -> if i % 4 = 3 then 255uy else rows[i / 4 * 3 + i % 4])

    { Size = { Width = width; Height = height }; Pixels = pixels }

let load (path: string) = decode (File.ReadAllBytes path)
