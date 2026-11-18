/// Scribble.Imaging's PNG decoder (DESIGN.md, "M8 step 3b", decisions 1–4 and 23): PngSuite against what WIC
/// decoded on Tim's machine (`tools/MakePngReferences.fsx`), its corrupt files, fuzzing for clean failures, and
/// round trips through a small PNG writer.
module Scribble.Tests.PngTests

open System
open System.Diagnostics
open System.IO
open System.IO.Compression
open Xunit
open Scribble.Imaging

let private suite = Path.Combine(AppContext.BaseDirectory, "PngSuite")

let private files (corrupt: bool) =
    Directory.GetFiles(suite, "*.png")
    |> Array.map Path.GetFileNameWithoutExtension
    |> Array.filter (fun n -> n.StartsWith "x" = corrupt)
    |> Array.sort

let validFiles () : obj[] seq = files false |> Seq.map (fun n -> [| box n |])
let corruptFiles () : obj[] seq = files true |> Seq.map (fun n -> [| box n |])

let private read (name: string) = File.ReadAllBytes(Path.Combine(suite, name + ".png"))

/// The WIC reference as the decoder should give it: 16-bit straight channels rounded to 8, then premultiplied.
/// `nudge` moves every 16-bit colour channel by that much first (see `wicGrey16Exception`).
let private reference (name: string) (nudge: int) =
    use z = new ZLibStream(File.OpenRead(Path.Combine(suite, name + ".wic")), CompressionMode.Decompress)
    use m = new MemoryStream()
    z.CopyTo m
    let b = m.ToArray()
    let w = BitConverter.ToInt32(b, 0)
    let h = BitConverter.ToInt32(b, 4)
    let ch (i: int) (by: int) =
        Pixels.round16 (Math.Clamp(int (BitConverter.ToUInt16(b, 8 + i * 2)) + by, 0, 65535))
    let pixels = Array.zeroCreate<byte> (w * h * 4)
    for p in 0 .. w * h - 1 do
        let a = ch (p * 4 + 3) 0
        for c in 0..2 do
            pixels[p * 4 + c] <- byte (Pixels.premultiply (ch (p * 4 + c) nudge) a)
        pixels[p * 4 + 3] <- byte a
    w, h, pixels

/// The one place WIC is off (found 2026-11-17, checked by hand): converting 16-bit greyscale it returns some
/// samples one 16-bit step from what the file holds (basn0g16 (13, 6) stores 0x8100, WIC gives 0x8101), which can
/// cross an 8-bit rounding boundary. For those files a channel may match the reference moved by one step.
let private wicGrey16Exception (name: string) = name.EndsWith "0g16"

let private firstDifference (expected: byte[]) (actual: byte[]) (width: int) =
    let i = Seq.findIndex id (Seq.map2 (<>) expected actual)
    let p = i / 4
    let px (a: byte[]) = String.Join(",", a[p * 4 .. p * 4 + 3])
    let count = Seq.map2 (<>) expected actual |> Seq.filter id |> Seq.length
    $"{count} channels differ; first at pixel ({p % width}, {p / width}): expected {px expected}, got {px actual}"

[<Theory; MemberData(nameof validFiles)>]
let ``every PngSuite file decodes to what WIC gives`` (name: string) =
    let w, h, expected = reference name 0
    let image = ImageDecoder.decode (read name)
    Assert.Equal((w, h), (image.Width, image.Height))
    if expected <> image.Pixels then
        let allowed =
            wicGrey16Exception name
            && (let _, _, down = reference name -1
                let _, _, up = reference name 1
                Array.forall id (Array.init expected.Length (fun i ->
                    let v = image.Pixels[i]
                    v = expected[i] || v = down[i] || v = up[i])))
        if not allowed then
            Assert.Fail(name + ": " + firstDifference expected image.Pixels w)

[<Theory; MemberData(nameof validFiles)>]
let ``the header gives the size without decoding`` (name: string) =
    let data = read name
    let info = ImageDecoder.readInfo data
    let image = ImageDecoder.decode data
    Assert.Equal((image.Width, image.Height), (info.Width, info.Height))
    // The signature and IHDR alone are enough.
    Assert.Equal(info, ImageDecoder.readInfo data[.. 32])

[<Theory; MemberData(nameof corruptFiles)>]
let ``PngSuite's corrupt files fail cleanly`` (name: string) =
    Assert.Throws<ImageFormatException>(fun () -> ImageDecoder.decode (read name) |> ignore) |> ignore

[<Fact>]
let ``data that isn't an image fails cleanly`` () =
    for data in [ [||]; [| 137uy; 80uy |]; Text.Encoding.UTF8.GetBytes "GIF89a, not a PNG"; Array.zeroCreate 64 ] do
        Assert.Throws<ImageFormatException>(fun () -> ImageDecoder.decode data |> ignore) |> ignore
        Assert.Throws<ImageFormatException>(fun () -> ImageDecoder.readInfo data |> ignore) |> ignore

/// Decoding either succeeds or throws ImageFormatException: never another exception.
let private decodesOrFailsCleanly (label: string) (data: byte[]) =
    try
        ImageDecoder.decode data |> ignore
    with
    | :? ImageFormatException -> ()
    | e -> Assert.Fail(label + ": " + e.GetType().Name + ": " + e.Message)

[<Fact>]
let ``a file cut short anywhere fails cleanly`` () =
    for name in files false do
        let data = read name
        let step = max 1 (data.Length / 40)
        for length in 0..step .. data.Length - 1 do
            decodesOrFailsCleanly $"{name} cut to {length}" data[.. length - 1]
    // Cut inside the image data (after the last IDAT's CRC is gone, before IEND): "ends early" or a CRC error.
    let data = read "basn6a08"
    Assert.Throws<ImageFormatException>(fun () -> ImageDecoder.decode data[.. data.Length - 30] |> ignore) |> ignore

/// Rewrites every chunk's CRC to match its data, so corruption reaches the zlib, filter and colour code.
let private fixCrcs (data: byte[]) =
    let mutable pos = 8
    while pos + 12 <= data.Length do
        let length = (int data[pos] <<< 24) ||| (int data[pos + 1] <<< 16) ||| (int data[pos + 2] <<< 8) ||| int data[pos + 3]
        if length < 0 || pos + 12 + length > data.Length then pos <- data.Length
        else
            let crc = Crc32.compute data (pos + 4) (length + 4)
            let at = pos + 8 + length
            data[at] <- byte (crc >>> 24)
            data[at + 1] <- byte (crc >>> 16)
            data[at + 2] <- byte (crc >>> 8)
            data[at + 3] <- byte crc
            pos <- at + 4

[<Fact>]
let ``corrupted bytes behind valid CRCs fail cleanly`` () =
    let random = Random(1234)
    for name in files false do
        let original = read name
        for _ in 1..20 do
            let data = Array.copy original
            for _ in 1 .. random.Next(1, 4) do
                let i = random.Next(8, data.Length)
                data[i] <- byte (random.Next 256)
            fixCrcs data
            decodesOrFailsCleanly name data

[<Fact>]
let ``rounding: 16 bits to 8 and premultiplying are exact`` () =
    for v in 0..255 do
        Assert.Equal(v, Pixels.round16 (v * 257))
    for v in 0..65535 do
        Assert.Equal(int (Math.Round(float v * 255.0 / 65535.0, MidpointRounding.AwayFromZero)), Pixels.round16 v)
    for c in 0..255 do
        for a in 0..255 do
            Assert.Equal(int (Math.Round(float (c * a) / 255.0, MidpointRounding.AwayFromZero)), Pixels.premultiply c a)

// ---- A small PNG writer (8-bit RGBA, chosen filters), for round trips and the timing test ----------------

let private chunk (out: Stream) (kind: string) (body: byte[]) =
    let bytes = Array.concat [ Text.Encoding.ASCII.GetBytes kind; body ]
    let be (v: uint32) = [| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |]
    out.Write(be (uint32 body.Length))
    out.Write(bytes)
    out.Write(be (Crc32.compute bytes 0 bytes.Length))

let private be32 (v: int) = [| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |]

/// Filters a row of straight RGBA bytes (`prev` is the row above, zeros for the first).
let private filterRow (filter: int) (row: byte[]) (prev: byte[]) =
    let left i = if i >= 4 then int row[i - 4] else 0
    let upLeft i = if i >= 4 then int prev[i - 4] else 0
    Array.init row.Length (fun i ->
        let a, b, c = left i, int prev[i], upLeft i
        let predictor =
            match filter with
            | 0 -> 0
            | 1 -> a
            | 2 -> b
            | 3 -> (a + b) / 2
            | _ ->
                let p = a + b - c
                let pa, pb, pc = abs (p - a), abs (p - b), abs (p - c)
                if pa <= pb && pa <= pc then a elif pb <= pc then b else c
        byte (int row[i] - predictor))

/// A PNG of straight RGBA pixels, row y filtered with `filterOf y`; `extra` chunks go before IDAT.
let private writePng (width: int) (height: int) (rgba: byte[]) (filterOf: int -> int) (extra: (string * byte[]) list) =
    use out = new MemoryStream()
    out.Write([| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |])
    chunk out "IHDR" (Array.concat [ be32 width; be32 height; [| 8uy; 6uy; 0uy; 0uy; 0uy |] ])
    for kind, body in extra do
        chunk out kind body
    use raw = new MemoryStream()
    let mutable prev = Array.zeroCreate<byte> (width * 4)
    for y in 0 .. height - 1 do
        let row = rgba[y * width * 4 .. (y + 1) * width * 4 - 1]
        let f = filterOf y
        raw.WriteByte(byte f)
        raw.Write(filterRow f row prev)
        prev <- row
    use compressed = new MemoryStream()
    (use z = new ZLibStream(compressed, CompressionLevel.Fastest, true)
     z.Write(raw.ToArray()))
    chunk out "IDAT" (compressed.ToArray())
    chunk out "IEND" [||]
    out.ToArray()

let private premultiplied (rgba: byte[]) =
    Array.init rgba.Length (fun i ->
        if i % 4 = 3 then rgba[i] else byte (Pixels.premultiply (int rgba[i]) (int rgba[i - i % 4 + 3])))

let private randomPixels (seed: int) (width: int) (height: int) =
    let random = Random(seed)
    let rgba = Array.zeroCreate<byte> (width * height * 4)
    random.NextBytes rgba
    rgba

[<Fact>]
let ``round trip through every filter`` () =
    let width, height = 37, 25
    let rgba = randomPixels 7 width height
    for filter in 0..4 do
        let image = ImageDecoder.decode (writePng width height rgba (fun _ -> filter) [])
        Assert.Equal<byte[]>(premultiplied rgba, image.Pixels)
    // Mixed, row by row.
    let image = ImageDecoder.decode (writePng width height rgba (fun y -> y % 5) [])
    Assert.Equal<byte[]>(premultiplied rgba, image.Pixels)

[<Fact>]
let ``an APNG shows its default image`` () =
    let width, height = 8, 6
    let first = randomPixels 3 width height
    let other = randomPixels 4 width height
    // acTL and fcTL before IDAT, then an fdAT frame after it: all ancillary, all skipped.
    let actl = Array.concat [ be32 2; be32 0 ]
    let fctl = Array.concat [ be32 0; be32 width; be32 height; be32 0; be32 0; [| 0uy; 1uy; 0uy; 10uy; 0uy; 0uy |] ]
    let apng = writePng width height first (fun _ -> 0) [ "acTL", actl; "fcTL", fctl ]
    let frame = writePng width height other (fun _ -> 0) []
    // Insert a junk fdAT (sequence number plus some of the other frame's bytes) before IEND.
    use out = new MemoryStream()
    out.Write(apng, 0, apng.Length - 12)
    chunk out "fdAT" (Array.concat [ be32 1; frame[33..60] ])
    out.Write(apng, apng.Length - 12, 12)
    let image = ImageDecoder.decode (out.ToArray())
    Assert.Equal<byte[]>(premultiplied first, image.Pixels)

[<Fact>]
let ``a file without IEND decodes when its image data is all there`` () =
    let rgba = randomPixels 9 5 5
    let png = writePng 5 5 rgba (fun _ -> 4) []
    Assert.Equal<byte[]>(premultiplied rgba, (ImageDecoder.decode png[.. png.Length - 13]).Pixels)

[<Fact>]
let ``an unknown critical chunk fails; an unknown ancillary one is skipped`` () =
    let rgba = randomPixels 11 4 4
    let ok = writePng 4 4 rgba (fun _ -> 0) [ "zzZz", [| 1uy; 2uy |] ]
    Assert.Equal<byte[]>(premultiplied rgba, (ImageDecoder.decode ok).Pixels)
    let bad = writePng 4 4 rgba (fun _ -> 0) [ "ZZZZ", [| 1uy; 2uy |] ]
    let e = Assert.Throws<ImageFormatException>(fun () -> ImageDecoder.decode bad |> ignore)
    Assert.Contains("ZZZZ", e.Message)

/// A 1024² picture that compresses like a real one (smooth gradients with some noise), Paeth filtered.
let private bigPng () =
    let n = 1024
    let random = Random(5)
    let rgba = Array.zeroCreate<byte> (n * n * 4)
    for y in 0 .. n - 1 do
        for x in 0 .. n - 1 do
            let i = (y * n + x) * 4
            rgba[i] <- byte (x / 4)
            rgba[i + 1] <- byte (y / 4)
            rgba[i + 2] <- byte ((x + y) / 8 + random.Next 8)
            rgba[i + 3] <- byte (255 - random.Next 2 * (x % 64))
    rgba, writePng n n rgba (fun _ -> 4) []

[<Fact>]
let ``a 1024 square PNG decodes correctly`` () =
    let rgba, png = bigPng ()
    let image = ImageDecoder.decode png
    Assert.Equal((1024, 1024), (image.Width, image.Height))
    Assert.Equal<byte[]>(premultiplied rgba, image.Pixels)

[<Timing.TimingFact; Trait("Category", "Timing")>]
let ``decoding a 1024 square PNG is fast`` () =
    let _, png = bigPng ()
    for _ in 1..3 do
        ImageDecoder.decode png |> ignore
    let best =
        Seq.init 10 (fun _ ->
            let sw = Stopwatch.StartNew()
            ImageDecoder.decode png |> ignore
            sw.Elapsed.TotalMilliseconds)
        |> Seq.min
    Console.WriteLine($"PNG 1024² decode: {best:F2} ms ({png.Length / 1024} KB)")
    // Measured 24 ms on Tim's machine (2026-11-17): about 8 of it is the BCL's zlib. A guard against regressions.
    Assert.True(best < 40.0, $"decoding took {best:F2} ms")
