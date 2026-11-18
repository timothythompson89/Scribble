/// PNG reading for screenshot tests, and writing through Scribble.Imaging's encoder: 8-bit RGBA or RGB, no interlacing.
/// Decoding understands every filter type, so references edited or re-saved by other tools load too.
module Scribble.Tests.Rendering.Png

open System
open System.IO
open System.IO.Compression
open Scribble.Platform
open Scribble.Rendering

let private signature = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]

/// Encodes a bitmap as a PNG (the bitmap's bytes as they are: references are opaque, so premultiplied or not is the
/// same).
let encode (bitmap: Bitmap) : byte[] =
    Scribble.Imaging.Png.encode (bitmap.Size.Width, bitmap.Size.Height, bitmap.Pixels)

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
