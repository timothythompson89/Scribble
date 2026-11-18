/// The image pipeline's edges (review of M8 step 3b, 2026-11-17): hostile and huge PNG files, the atlas's packer
/// and pages under random use, and the image cache under many threads (decodes and downloads finishing while
/// holders come and go).
module Scribble.Tests.ImageRobustnessTests

open System
open System.Collections.Generic
open System.IO
open System.IO.Compression
open System.Threading
open System.Threading.Tasks
open Xunit
open Scribble.Imaging
open Scribble.Rendering
open Scribble.Core

let private suite = Path.Combine(AppContext.BaseDirectory, "PngSuite")

let private validFiles () =
    Directory.GetFiles(suite, "*.png") |> Array.filter (fun f -> not (Path.GetFileName(f).StartsWith "x")) |> Array.sort

// ---- A PNG writer with any header ----------------------------------------------------------------------

let private be32 (v: uint32) = [| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |]

let private chunk (out: Stream) (kind: string) (body: byte[]) =
    let bytes = Array.concat [ Text.Encoding.ASCII.GetBytes kind; body ]
    out.Write(be32 (uint32 body.Length))
    out.Write(bytes)
    out.Write(be32 (Crc32.compute bytes 0 bytes.Length))

let private zlib (raw: byte[]) =
    use compressed = new MemoryStream()
    (use z = new ZLibStream(compressed, CompressionLevel.SmallestSize, true)
     z.Write(raw))
    compressed.ToArray()

/// A PNG with this header and these (uncompressed) image data bytes, compressed.
let private png (width: uint32) (height: uint32) (depth: byte) (colourType: byte) (raw: byte[]) =
    use out = new MemoryStream()
    out.Write([| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |])
    chunk out "IHDR" (Array.concat [ be32 width; be32 height; [| depth; colourType; 0uy; 0uy; 0uy |] ])
    chunk out "IDAT" (zlib raw)
    chunk out "IEND" [||]
    out.ToArray()

/// Rewrites every chunk's CRC to match its data.
let private fixCrcs (data: byte[]) =
    let mutable pos = 8
    while pos + 12 <= data.Length do
        let length = (int data[pos] <<< 24) ||| (int data[pos + 1] <<< 16) ||| (int data[pos + 2] <<< 8) ||| int data[pos + 3]
        if length < 0 || pos + 12 + length > data.Length then pos <- data.Length
        else
            let crc = Crc32.compute data (pos + 4) (length + 4)
            let at = pos + 8 + length
            Array.blit (be32 crc) 0 data at 4
            pos <- at + 4

/// The chunks of a PNG file (after its signature), as whole byte arrays (length, type, data, CRC).
let private chunksOf (data: byte[]) =
    let result = ResizeArray<byte[]>()
    let mutable pos = 8
    while pos + 12 <= data.Length do
        let length = (int data[pos] <<< 24) ||| (int data[pos + 1] <<< 16) ||| (int data[pos + 2] <<< 8) ||| int data[pos + 3]
        if length < 0 || pos + 12 + length > data.Length then pos <- data.Length
        else
            result.Add data[pos .. pos + 11 + length]
            pos <- pos + 12 + length
    result

/// Decodes `data` and says how it went: decoded (with its size) or a clean failure. Anything else fails the test,
/// and so does a decode that allocates far more than the picture it gives (or, failing, more than `failBudget`).
let private decodeChecked (label: string) (failBudget: int64) (data: byte[]) =
    let before = GC.GetAllocatedBytesForCurrentThread()
    let outcome =
        try
            let d = ImageDecoder.decode data
            Ok(int64 d.OriginalWidth * int64 d.OriginalHeight)
        with
        | :? ImageFormatException as e -> Error e.Message
        | e ->
            Assert.Fail(label + ": " + e.GetType().Name + ": " + e.Message)
            Error ""
    let allocated = GC.GetAllocatedBytesForCurrentThread() - before
    match outcome with
    | Ok pixels ->
        // The picture, its raw rows (at most 16 bytes a pixel at 16-bit RGBA) and mip levels, plus the file.
        let budget = pixels * 28L + int64 data.Length * 4L + 1_000_000L
        if allocated > budget then Assert.Fail($"{label}: decoding allocated {allocated} bytes for {pixels} pixels")
    | Error message ->
        if allocated > failBudget then Assert.Fail($"{label}: failing ({message}) allocated {allocated} bytes")
    outcome

// ---- Hostile files ------------------------------------------------------------------------------------

/// Finding 1: the decoder allocated for the size the header claims before reading any image data.
[<Fact>]
let ``a header claiming a huge picture with little data fails without allocating for it`` () =
    // 9,000 × 9,000 RGBA (324 MB of pixels, under the limit) with 1,000 bytes of image data.
    let data = png 9000u 9000u 8uy 6uy (Array.zeroCreate 1000)
    match decodeChecked "huge header" (16L * 1024L * 1024L) data with
    | Error message -> Assert.Contains("ends early", message)
    | Ok _ -> Assert.Fail "decoded"

/// Finding 1: a small file whose data really does inflate to a picture over the limit (a "zlib bomb").
[<Fact>]
let ``a picture over the pixel limit fails before it is inflated`` () =
    let width = 20000
    let height = 7000 // 140 M pixels: over the limit, under .NET's array limit
    let row = Array.zeroCreate<byte> (1 + width * 4)
    use raw = new MemoryStream()
    (use z = new ZLibStream(raw, CompressionLevel.SmallestSize, true)
     for _ in 1..height do
         z.Write(row))
    use out = new MemoryStream()
    out.Write([| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |])
    chunk out "IHDR" (Array.concat [ be32 (uint32 width); be32 (uint32 height); [| 8uy; 6uy; 0uy; 0uy; 0uy |] ])
    chunk out "IDAT" (raw.ToArray())
    chunk out "IEND" [||]
    let data = out.ToArray()
    Assert.True(data.Length < 1_000_000, $"the file is {data.Length} bytes")
    match decodeChecked "bomb" (16L * 1024L * 1024L) data with
    | Error message -> Assert.Contains("too big", message)
    | Ok _ -> Assert.Fail "decoded"
    // The size is still readable (a layout can reserve the space before failing).
    Assert.Equal(width, (ImageDecoder.readInfo data).Width)

[<Fact>]
let ``random headers fail cleanly and never allocate for a picture they don't have`` () =
    let random = Random(4321)
    let sizes = [| 0u; 1u; 2u; 7u; 8u; 9u; 255u; 65535u; 100000u; 0x7FFFFFFFu; 0x80000000u; 0xFFFFFFFFu |]
    for file in validFiles () do
        let original = File.ReadAllBytes file
        for _ in 1..15 do
            let data = Array.copy original
            let pick () = if random.Next 3 = 0 then uint32 (random.Next(1, 300)) else sizes[random.Next sizes.Length]
            Array.blit (be32 (pick ())) 0 data 16 4
            Array.blit (be32 (pick ())) 0 data 20 4
            if random.Next 3 = 0 then data[24] <- byte ([| 1; 2; 4; 8; 16; 3; 0 |][random.Next 7])
            if random.Next 3 = 0 then data[25] <- byte ([| 0; 2; 3; 4; 6; 1; 7 |][random.Next 7])
            if random.Next 5 = 0 then data[28] <- byte (random.Next 3)
            fixCrcs data
            decodeChecked (Path.GetFileName file) (16L * 1024L * 1024L) data |> ignore

[<Fact>]
let ``chunks dropped, repeated, swapped, or given wrong lengths fail cleanly`` () =
    let random = Random(99)
    let signature = [| 137uy; 80uy; 78uy; 71uy; 13uy; 10uy; 26uy; 10uy |]
    let extras =
        [| "PLTE", Array.init 30 byte; "tRNS", Array.init 300 byte; "tRNS", [||]; "IDAT", [| 0uy |]; "IHDR", Array.zeroCreate 13
           "aBCD", [| 1uy |]; "ABCD", [| 1uy |]; "IEND", [| 1uy; 2uy |]; "PLTE", [||] |]
    for file in validFiles () do
        let original = File.ReadAllBytes file
        let chunks = chunksOf original
        for _ in 1..25 do
            let list = ResizeArray(chunks)
            for _ in 1 .. random.Next(1, 4) do
                match random.Next 5 with
                | 0 when list.Count > 1 -> list.RemoveAt(random.Next(1, list.Count))
                | 1 -> list.Insert(random.Next(1, list.Count + 1), list[random.Next list.Count])
                | 2 when list.Count > 2 ->
                    let i, j = random.Next(1, list.Count), random.Next(1, list.Count)
                    let t = list[i]
                    list[i] <- list[j]
                    list[j] <- t
                | 3 ->
                    let kind, body = extras[random.Next extras.Length]
                    use m = new MemoryStream()
                    chunk m kind body
                    list.Insert(random.Next(1, list.Count + 1), m.ToArray())
                | _ ->
                    // A wrong length field (the CRC then can't be fixed: it reads from the wrong place).
                    let i = random.Next list.Count
                    let c = Array.copy list[i]
                    Array.blit (be32 (uint32 (random.Next()))) 0 c 0 4
                    list[i] <- c
            let data = Array.concat (Seq.append [ signature ] list)
            if random.Next 2 = 0 then fixCrcs data
            decodeChecked (Path.GetFileName file) (16L * 1024L * 1024L) data |> ignore

[<Fact>]
let ``many random byte changes behind valid CRCs fail cleanly`` () =
    let random = Random(2026)
    for file in validFiles () do
        let original = File.ReadAllBytes file
        for _ in 1..60 do
            let data = Array.copy original
            for _ in 1 .. random.Next(1, 12) do
                let i = random.Next(8, data.Length)
                data[i] <-
                    match random.Next 4 with
                    | 0 -> 0uy
                    | 1 -> 255uy
                    | 2 -> data[i] ^^^ (1uy <<< random.Next 8)
                    | _ -> byte (random.Next 256)
            fixCrcs data
            decodeChecked (Path.GetFileName file) (16L * 1024L * 1024L) data |> ignore

// ---- The atlas under random use -----------------------------------------------------------------------

[<Fact>]
let ``the shelf packer never overlaps, stays in bounds and frees everything`` () =
    let random = Random(7)
    let packer = ShelfPacker(512, 512)
    let live = ResizeArray<PackedSlot * int * int>()
    let overlaps (s: PackedSlot, w: int, h: int) (t: PackedSlot, tw: int, th: int) =
        s.X < t.X + tw && t.X < s.X + w && s.Y < t.Y + th && t.Y < s.Y + h
    for step in 1..20000 do
        if live.Count > 0 && random.Next 100 < 45 then
            let i = random.Next live.Count
            let s, _, _ = live[i]
            packer.Free s
            live.RemoveAt i
        else
            let w = if random.Next 10 = 0 then random.Next(1, 520) else random.Next(1, 64)
            let h = if random.Next 10 = 0 then random.Next(1, 520) else random.Next(1, 64)
            match packer.TryAllocate(w, h) with
            | null -> Assert.True(w > 512 || h > 512 || live.Count > 0, $"step {step}: an empty page refused {w} × {h}")
            | s ->
                Assert.True(s.X >= 0 && s.Y >= 0 && s.X + w <= 512 && s.Y + h <= 512, $"step {step}: {w} × {h} at {s.X}, {s.Y}")
                for other in live do
                    if overlaps (s, w, h) other then
                        let o, _, _ = other
                        Assert.Fail($"step {step}: {w} × {h} at {s.X}, {s.Y} overlaps the region at {o.X}, {o.Y}")
                live.Add((s, w, h))
        Assert.Equal(live.Count, packer.Used)
    for s, _, _ in live do
        packer.Free s
    Assert.True packer.IsEmpty
    Assert.NotNull(packer.TryAllocate(512, 512))

type private CountingPages() =
    member val Pages = Dictionary<int, int * int>()
    member val Uploads = 0 with get, set
    interface IImagePages with
        member this.Create(page, w, h) =
            Assert.False(this.Pages.ContainsKey page, $"page {page} made twice")
            this.Pages[page] <- (w, h)
        member this.Destroy page = Assert.True(this.Pages.Remove page, $"page {page} destroyed but never made")
        member this.Upload(page, x, y, level) =
            let w, h = this.Pages[page]
            Assert.True(x >= 0 && y >= 0 && x + level.Width <= w && y + level.Height <= h, "an upload outside its page")
            this.Uploads <- this.Uploads + 1

[<Fact>]
let ``the atlas under random drawing and releasing gives every page back at the end`` () =
    let random = Random(11)
    let pages = CountingPages()
    use atlas = new ImageAtlas(pages, 4096, 256)
    let images = ResizeArray<DecodedImage>()
    let make () =
        let w = if random.Next 20 = 0 then random.Next(257, 700) else random.Next(1, 120)
        let h = if random.Next 20 = 0 then random.Next(257, 700) else random.Next(1, 120)
        DecodedImage(w, h, Array.zeroCreate (w * h * 4))
    for _ in 1..40 do
        images.Add(make ())
    for _ in 1..400 do
        let list = DrawList(1.0)
        for _ in 1 .. random.Next(0, 25) do
            let image = images[random.Next images.Count]
            let size = float (random.Next(1, 300))
            list.DrawImage(Rect.Create(float (random.Next 500), float (random.Next 500), size, size * 0.7), image)
        atlas.Prepare list |> ignore
        // Some pictures go, new ones come.
        for _ in 1 .. random.Next(0, 3) do
            let i = random.Next images.Count
            images[i].Release()
            images[i] <- make ()
    for image in images do
        image.Release()
    atlas.Prepare(DrawList(1.0)) |> ignore
    Assert.Equal(0, atlas.ResidentLevels)
    Assert.Equal(0, atlas.PageCount)
    Assert.Empty pages.Pages

// ---- The cache under many threads ---------------------------------------------------------------------

let private smallPngs =
    lazy
        (validFiles ()
         |> Array.map File.ReadAllBytes
         |> Array.filter (fun b -> (ImageDecoder.readInfo b).Width <= 64))

/// Waits (real time, bounded) until `condition` holds.
let private eventually (what: string) (condition: unit -> bool) =
    let deadline = DateTime.UtcNow + TimeSpan.FromSeconds 20.0
    while not (condition ()) && DateTime.UtcNow < deadline do
        Thread.Sleep 5
    Assert.True(condition (), "timed out waiting: " + what)

[<Fact>]
let ``many threads holding and letting go of pictures while they decode leave the cache consistent`` () =
    let cache = ImageCache(2L * 1024L * 1024L)
    let run = Guid.NewGuid().ToString("N")
    let files = smallPngs.Value
    let broken = Text.Encoding.ASCII.GetBytes "not an image"
    let images =
        Array.init 40 (fun i ->
            let bytes = if i % 9 = 8 then broken else files[i % files.Length]
            Image.fromBytes ($"{run}-{i}", bytes) [])
    let seen = System.Collections.Concurrent.ConcurrentBag<ImageEntry>()
    let worker (seed: int) =
        Task.Run(fun () ->
            let random = Random(seed)
            let held = ResizeArray<ImageEntry>()
            for _ in 1..3000 do
                if held.Count > 0 && random.Next 100 < 50 then
                    let i = random.Next held.Count
                    cache.Release held[i]
                    held.RemoveAt i
                else
                    let e = cache.Acquire images[random.Next images.Length]
                    seen.Add e
                    held.Add e
                    // What an element does while drawing: looks at the state; a ready picture must not be released.
                    match cache.StateOf e with
                    | ImageState.Ready d -> Assert.False(d.IsReleased, "a held picture was released")
                    | _ -> ()
            for e in held do
                cache.Release e)
    Task.WaitAll [| for seed in 1..8 -> worker seed |]
    let entries = seen |> Seq.distinct |> Seq.toArray
    eventually "every decode to finish" (fun () -> entries |> Array.forall (fun e -> cache.StateOf e <> ImageState.Loading))
    for e in entries do
        let struct (_, _, holders) = cache.Describe e
        Assert.Equal(0, holders)
    Assert.True(cache.PooledBytes <= cache.PoolLimit, $"pool {cache.PooledBytes} over its limit")
    // The cache never hands out a released picture: what it still has for each key is whole.
    for image in images do
        let e = cache.Acquire image
        eventually "a decode to finish" (fun () -> cache.StateOf e <> ImageState.Loading)
        match cache.StateOf e with
        | ImageState.Ready d -> Assert.False(d.IsReleased, image.Key + " was handed out released")
        | _ -> ()
        cache.Release e
    Assert.True(cache.PooledBytes <= cache.PoolLimit)

[<Fact>]
let ``downloads finishing, failing and being cancelled while holders come and go free every place`` () =
    let cache = ImageCache(1024L * 1024L)
    let run = Guid.NewGuid().ToString("N")
    let bytes = smallPngs.Value[0]
    let fetch (url: Uri) (token: CancellationToken) : Task<Result<byte[], exn>> =
        task {
            let n = url.AbsoluteUri.Length + int url.Query.Length
            try
                do! Task.Delay(Random.Shared.Next(0, 4), token)
                return
                    match Random.Shared.Next 6 with
                    | 0 -> Error(System.Net.Http.HttpRequestException("refused") :> exn)
                    | 1 -> Ok(Text.Encoding.ASCII.GetBytes "garbage")
                    | _ -> Ok bytes
            with e ->
                return Error e
        }
    let images = Array.init 30 (fun i -> Image($"url:{run}/{i}", ImageSource.Url($"https://example.test/{run}/{i}.png", fetch), 1.0))
    let worker (seed: int) =
        Task.Run(fun () ->
            let random = Random(seed)
            let held = ResizeArray<ImageEntry>()
            for _ in 1..1500 do
                if held.Count > 0 && random.Next 100 < 55 then
                    let i = random.Next held.Count
                    cache.Release held[i]
                    held.RemoveAt i
                else held.Add(cache.Acquire images[random.Next images.Length])
                if random.Next 50 = 0 then Thread.Sleep 1
            for e in held do
                cache.Release e)
    Task.WaitAll [| for seed in 1..6 -> worker seed |]
    eventually "every download to end" (fun () -> cache.Downloads = struct (0, 0))
    eventually "nothing left loading" (fun () ->
        images |> Array.forall (fun i ->
            not (cache.Contains i.Key) || (let e = cache.Acquire i in let s = cache.StateOf e in cache.Release e; s <> ImageState.Loading)))
    Assert.True(cache.PooledBytes <= cache.PoolLimit)

/// Finding 2: a download function whose task faults (instead of returning Error) kept its place forever.
[<Fact>]
let ``a download function that throws fails that picture and frees its place`` () =
    let cache = ImageCache(1024L * 1024L)
    let run = Guid.NewGuid().ToString("N")
    let fetch (_: Uri) (_: CancellationToken) : Task<Result<byte[], exn>> = Task.FromException<Result<byte[], exn>>(InvalidOperationException "boom")
    let image = Image($"url:{run}", ImageSource.Url($"https://example.test/{run}.png", fetch), 1.0)
    let e = cache.Acquire image
    eventually "the place to be freed" (fun () -> cache.Downloads = struct (0, 0))
    eventually "the picture to fail" (fun () ->
        match cache.StateOf e with
        | ImageState.Failed _ -> true
        | _ -> false)
    cache.Release e
