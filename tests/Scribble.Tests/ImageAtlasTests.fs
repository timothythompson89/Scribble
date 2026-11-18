/// Images on the GPU without a GPU (DESIGN.md, "M8 step 3b", (c)): decoded images and their mip levels, the draw
/// list's image shapes, and the image atlas on a fake device (pages, uploads, runs, release).
module Scribble.Tests.ImageAtlasTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Imaging
open Scribble.Rendering

let private solid (w: int) (h: int) (r: byte) (g: byte) (b: byte) (a: byte) =
    DecodedImage(w, h, Array.init (w * h * 4) (fun i -> match i % 4 with 0 -> r | 1 -> g | 2 -> b | _ -> a))

/// A fake backend: records what the atlas asks for.
type private FakePages() =
    let pages = Dictionary<int, int * int>()
    member val Uploads = ResizeArray<int * int * int * int * int>() // page, x, y, w, h
    member val Destroyed = ResizeArray<int>()
    member _.Pages = pages

    interface IImagePages with
        member _.Create(page, w, h) = pages[page] <- (w, h)
        member this.Destroy page =
            pages.Remove page |> ignore
            this.Destroyed.Add page
        member this.Upload(page, x, y, level) = this.Uploads.Add((page, x, y, level.Width, level.Height))

let private frame (draw: DrawList -> unit) =
    let list = DrawList(1.0)
    draw list
    list

// ---- Decoded images and mip levels ----------------------------------------------------------------

[<Fact>]
let ``halving averages 2 × 2 blocks with rounding, odd edges counted twice`` () =
    // 3 × 1 grey ramp: 0, 101, 255 (opaque).
    let px = [| 0uy; 0uy; 0uy; 255uy; 101uy; 101uy; 101uy; 255uy; 255uy; 255uy; 255uy; 255uy |]
    let image = DecodedImage(3, 1, px)
    Assert.Equal(3, image.LevelCount) // 3×1, 2×1, 1×1
    let l1 = image.Level 1
    Assert.Equal((2, 1), (l1.Width, l1.Height))
    // (0 + 101 + 0 + 101 + 2) / 4 = 51; the odd last column: (255 × 4 + 2) / 4 = 255.
    Assert.Equal<byte[]>([| 51uy; 51uy; 51uy; 255uy; 255uy; 255uy; 255uy; 255uy |], l1.Pixels)
    let l2 = image.Level 2
    Assert.Equal((1, 1), (l2.Width, l2.Height))
    Assert.Equal(byte ((51 * 2 + 255 * 2 + 2) / 4), l2.Pixels[0])

[<Fact>]
let ``levels are built when asked for; decoding builds them all`` () =
    let image = solid 64 32 10uy 20uy 30uy 255uy
    Assert.Equal(7, image.LevelCount) // 64 … 1
    Assert.Equal(1, image.LevelsBuilt)
    image.Level 2 |> ignore
    Assert.Equal(3, image.LevelsBuilt)
    let decoded = ImageDecoder.decode (IO.File.ReadAllBytes(IO.Path.Combine(AppContext.BaseDirectory, "PngSuite", "basn6a08.png")))
    Assert.Equal(decoded.LevelCount, decoded.LevelsBuilt)

[<Fact>]
let ``an image over 8192 on a side is halved until it fits`` () =
    let image = DecodedImage(8194, 3, Array.zeroCreate (8194 * 3 * 4))
    Assert.True image.Reduced
    Assert.Equal((4097, 2), (image.Width, image.Height))
    Assert.Equal((8194, 3), (image.OriginalWidth, image.OriginalHeight))
    Assert.False (solid 8192 1 0uy 0uy 0uy 0uy).Reduced

// ---- The draw list --------------------------------------------------------------------------------

[<Fact>]
let ``the draw list picks the first level shrunk by less than 2×, by the axis that shrinks more`` () =
    let image = solid 400 100 0uy 0uy 0uy 255uy
    let level (w: float) (h: float) =
        let list = frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, w, h), image))
        list.Images[0].Level
    Assert.Equal(0, level 400.0 100.0)
    Assert.Equal(0, level 201.0 100.0) // 1.99×
    Assert.Equal(1, level 200.0 100.0) // 2×
    Assert.Equal(2, level 80.0 100.0) // 5× across, 1× down: 5 → 2.5 → 1.25
    Assert.Equal(0, level 1600.0 400.0) // enlarged

[<Fact>]
let ``drawing a released image throws`` () =
    let image = solid 4 4 0uy 0uy 0uy 255uy
    image.Release()
    Assert.Throws<ObjectDisposedException>(fun () -> frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 4.0, 4.0), image)) |> ignore)
    |> ignore

[<Fact>]
let ``rolling back removes image shapes; clipped-away images record nothing`` () =
    let image = solid 4 4 0uy 0uy 0uy 255uy
    let list = DrawList(1.0)
    list.DrawImage(Rect.Create(0.0, 0.0, 4.0, 4.0), image)
    let mark = list.Mark
    list.DrawImage(Rect.Create(4.0, 0.0, 4.0, 4.0), image)
    list.FillRect(Rect.Create(0.0, 0.0, 1.0, 1.0), Color.White)
    list.RollBack mark
    Assert.Equal(1, list.Count)
    Assert.Equal(1, list.Images.Count)
    list.PushClip(Rect.Create(0.0, 0.0, 10.0, 10.0))
    list.DrawImage(Rect.Create(20.0, 0.0, 4.0, 4.0), image)
    Assert.Equal(1, list.Images.Count)

[<Fact>]
let ``image rectangles are snapped to whole pixels`` () =
    let list = DrawList(1.5)
    list.DrawImage(Rect.Create(1.1, 2.2, 10.0, 10.0), solid 4 4 0uy 0uy 0uy 255uy)
    let r = list.InstanceRect 0
    Assert.Equal(Rect.Create(2.0, 3.0, 15.0, 15.0), r)

// ---- The atlas ------------------------------------------------------------------------------------

[<Fact>]
let ``images share a page; only drawn levels are uploaded`` () =
    let fake = FakePages()
    use atlas = new ImageAtlas(fake, 8192)
    let a = solid 100 50 255uy 0uy 0uy 255uy
    let b = solid 30 30 0uy 255uy 0uy 255uy
    let runs =
        atlas.Prepare(frame (fun l ->
            l.DrawImage(Rect.Create(0.0, 0.0, 100.0, 50.0), a)
            l.FillRect(Rect.Create(0.0, 0.0, 5.0, 5.0), Color.White)
            l.DrawImage(Rect.Create(0.0, 0.0, 7.0, 7.0), b))) // b shrunk 4.3×: level 2 (8 × 8)
    Assert.Equal(1, atlas.PageCount)
    Assert.Equal<(int * int) seq>([ (2048, 2048) ], fake.Pages.Values)
    Assert.Equal<(int * int) seq>([ (100, 50); (8, 8) ], fake.Uploads |> Seq.map (fun (_, _, _, w, h) -> w, h))
    Assert.Equal<ImageRun seq>([ { First = 0; Count = 3; Page = 0 } ], runs)
    // The next frame draws the same: nothing uploaded again.
    atlas.Prepare(frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 100.0, 50.0), a))) |> ignore
    Assert.Equal(2, atlas.Uploads)

[<Fact>]
let ``work follows what is drawn, not what exists`` () =
    let fake = FakePages()
    use atlas = new ImageAtlas(fake, 8192)
    let images = Array.init 1000 (fun i -> solid 16 16 (byte i) 0uy 0uy 255uy)
    atlas.Prepare(frame (fun l -> for i in [ 3; 500; 999 ] do l.DrawImage(Rect.Create(0.0, 0.0, 16.0, 16.0), images[i]))) |> ignore
    Assert.Equal(3, atlas.Uploads)
    Assert.Equal(3, atlas.ResidentLevels)

[<Fact>]
let ``a frame without images is one run with no page`` () =
    use atlas = new ImageAtlas(FakePages(), 8192)
    let runs = atlas.Prepare(frame (fun l -> l.FillRect(Rect.Create(0.0, 0.0, 5.0, 5.0), Color.White)))
    Assert.Equal<ImageRun seq>([ { First = 0; Count = 1; Page = -1 } ], runs)
    Assert.Equal(0, atlas.PageCount)
    Assert.Empty(atlas.Prepare(DrawList(1.0)))

[<Fact>]
let ``images on different pages split the frame into runs, in drawing order`` () =
    let fake = FakePages()
    use atlas = new ImageAtlas(fake, 8192, 64) // small pages: two 40-pixel images can't share one
    let a = solid 40 40 255uy 0uy 0uy 255uy
    let b = solid 40 40 0uy 255uy 0uy 255uy
    let runs =
        atlas.Prepare(frame (fun l ->
            l.FillRect(Rect.Create(0.0, 0.0, 5.0, 5.0), Color.White) // 0: joins the first run
            l.DrawImage(Rect.Create(0.0, 0.0, 40.0, 40.0), a) // 1: page 0
            l.FillRect(Rect.Create(0.0, 0.0, 5.0, 5.0), Color.White) // 2
            l.DrawImage(Rect.Create(0.0, 0.0, 40.0, 40.0), b) // 3: page 1
            l.DrawImage(Rect.Create(0.0, 0.0, 40.0, 40.0), a) // 4: page 0 again
            l.FillRect(Rect.Create(0.0, 0.0, 5.0, 5.0), Color.White))) // 5
    Assert.Equal<ImageRun seq>(
        [ { First = 0; Count = 3; Page = 0 }; { First = 3; Count = 1; Page = 1 }; { First = 4; Count = 2; Page = 0 } ],
        runs)

[<Fact>]
let ``a level bigger than a page gets a page of its own size`` () =
    let fake = FakePages()
    use atlas = new ImageAtlas(fake, 8192)
    let big = solid 3000 100 0uy 0uy 0uy 255uy
    atlas.Prepare(frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 3000.0, 100.0), big))) |> ignore
    Assert.Equal<(int * int) seq>([ (3000, 100) ], fake.Pages.Values)
    // Drawn small, it fits an ordinary page.
    atlas.Prepare(frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 300.0, 10.0), big))) |> ignore
    Assert.Contains((2048, 2048), fake.Pages.Values)

[<Fact>]
let ``a device with a lower limit draws the first level that fits`` () =
    let fake = FakePages()
    use atlas = new ImageAtlas(fake, 4096)
    let photo = solid 6000 10 0uy 0uy 0uy 255uy
    atlas.Prepare(frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 6000.0, 10.0), photo))) |> ignore
    Assert.Equal<(int * int) seq>([ (3000, 5) ], fake.Uploads |> Seq.map (fun (_, _, _, w, h) -> w, h))

[<Fact>]
let ``released images give their space back at the next frame, and an emptied page goes`` () =
    let fake = FakePages()
    use atlas = new ImageAtlas(fake, 8192)
    let a = solid 10 10 0uy 0uy 0uy 255uy
    let b = solid 10 10 0uy 0uy 0uy 255uy
    atlas.Prepare(frame (fun l ->
        l.DrawImage(Rect.Create(0.0, 0.0, 10.0, 10.0), a)
        l.DrawImage(Rect.Create(0.0, 0.0, 10.0, 10.0), b))) |> ignore
    a.Release()
    atlas.Prepare(frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 10.0, 10.0), b))) |> ignore
    Assert.Equal(1, atlas.ResidentLevels)
    Assert.Equal(1, atlas.PageCount)
    b.Release()
    atlas.Prepare(DrawList(1.0)) |> ignore
    Assert.Equal(0, atlas.PageCount)
    Assert.Equal<int seq>([ 0 ], fake.Destroyed)

[<Fact>]
let ``the atlas writes where each image reads`` () =
    use atlas = new ImageAtlas(FakePages(), 8192)
    let image = solid 20 10 0uy 0uy 0uy 255uy
    let list = frame (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 40.0, 10.0), image, source = Rect.Create(5.0, 0.0, 10.0, 10.0)))
    atlas.Prepare list |> ignore
    let d = list.InstanceData
    // Origin texel 5 (the slot is at 0, 0), steps 1/4 and 1 texel per pixel, reads clamped to texels 5..14, 0..9.
    Assert.Equal<float32[]>([| 5.0f; 0.0f; 16384.0f; 65536.0f; 5.0f; 0.0f; 14.0f; 9.0f |], d.Array[8..15])
    Assert.Equal(0, BitConverter.SingleToInt32Bits d.Array[19])

// ---- Packing --------------------------------------------------------------------------------------

[<Fact>]
let ``a freed region is reused, and freed space at the end comes back`` () =
    let packer = ShelfPacker(100, 100)
    let a = packer.TryAllocate(30, 20)
    let b = packer.TryAllocate(30, 20)
    let c = packer.TryAllocate(30, 20)
    Assert.Equal((0, 30, 60), (a.X, b.X, c.X))
    packer.Free b
    let d = packer.TryAllocate(25, 18) // fits b's region
    Assert.Equal(30, d.X)
    packer.Free c
    packer.Free d
    let e = packer.TryAllocate(70, 20) // the shelf's end moved back to 30
    Assert.Equal((30, 0), (e.X, e.Y))
    Assert.Null(packer.TryAllocate(101, 1))
    packer.Free a
    packer.Free e
    Assert.True packer.IsEmpty
    let f = packer.TryAllocate(100, 100) // every shelf gone: the whole page is free
    Assert.Equal((0, 0), (f.X, f.Y))
