/// Images on the GPU (DESIGN.md, "M8 step 3b", (c)): exact copies, whole-number filtering checked against the same
/// formula on the CPU, mip levels, pages and drawing order, and a picture of the stretch, scaling, alpha and
/// corner cases (also compared across backends by BackendTests). Images are made here, procedurally.
module Scribble.Tests.Rendering.ImageTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Imaging
open Scribble.Tests.Rendering.ClearTests

/// A decoded image from a function giving straight RGBA per pixel (premultiplied here).
let internal make (w: int) (h: int) (f: int -> int -> int * int * int * int) =
    let px = Array.zeroCreate<byte> (w * h * 4)
    for y in 0 .. h - 1 do
        for x in 0 .. w - 1 do
            let r, g, b, a = f x y
            let i = (y * w + x) * 4
            let pm c = byte ((c * a + 127) / 255)
            px[i] <- pm r
            px[i + 1] <- pm g
            px[i + 2] <- pm b
            px[i + 3] <- byte a
    DecodedImage(w, h, px)

/// Random premultiplied pixels.
let private noise (seed: int) (w: int) (h: int) =
    let random = Random(seed)
    make w h (fun _ _ -> random.Next 256, random.Next 256, random.Next 256, random.Next 256)

/// An 8 × 8 sprite: a face, for pixelated enlarging.
let internal sprite =
    let rows =
        [| "..####.."; ".#....#."; "#.#..#.#"; "#......#"; "#.#..#.#"; "#..##..#"; ".#....#."; "..####.." |]
    make 8 8 (fun x y -> if rows[y][x] = '#' then (250, 200, 40, 255) else (40, 60, 120, 255))

/// A soft disc with a hue sweep: alpha edges and smooth colour.
let internal gradient =
    make 64 64 (fun x y ->
        let dx, dy = float x - 31.5, float y - 31.5
        let d = sqrt (dx * dx + dy * dy)
        let a = int (Math.Clamp((32.0 - d) * 40.0, 0.0, 255.0))
        (x * 4, y * 4, 255 - x * 2, a))

/// Fine concentric rings: shows aliasing when shrunk without mip levels.
let internal rings =
    make 256 256 (fun x y ->
        let dx, dy = float x - 127.5, float y - 127.5
        let v = if int (sqrt (dx * dx + dy * dy) / 2.0) % 2 = 0 then 255 else 0
        (v, v, v, 255))

/// The picture: 520 × 300 DIPs.
let internal draw (f: IFrame) =
    let l = f.DrawList
    f.Clear(Color.Hex 0x181825)
    // A checkerboard behind, so alpha shows.
    for cy in 0..14 do
        for cx in 0..25 do
            if (cx + cy) % 2 = 0 then l.FillRect(Rect.Create(float cx * 20.0, float cy * 20.0, 20.0, 20.0), Color.Hex 0x24243A)
    // Row 1: enlarging (smooth, pixelated), 1:1 with alpha, stretched on one axis.
    l.DrawImage(Rect.Create(10.0, 10.0, 64.0, 64.0), sprite)
    l.DrawImage(Rect.Create(84.0, 10.0, 64.0, 64.0), sprite, scaling = ImageScaling.Pixelated)
    l.DrawImage(Rect.Create(158.0, 10.0, 64.0, 64.0), gradient)
    l.DrawImage(Rect.Create(232.0, 10.0, 128.0, 64.0), gradient)
    l.DrawImage(Rect.Create(370.0, 10.0, 64.0, 64.0), gradient, radius = CornerRadius.Uniform 20.0)
    l.DrawImage(Rect.Create(444.0, 10.0, 21.0, 64.0), sprite, scaling = ImageScaling.Pixelated) // shrunk across: smooth
    // Row 2: shrinking through mip levels (2×, 5×, 10×), a part enlarged, opacity, a rounded avatar.
    l.DrawImage(Rect.Create(10.0, 90.0, 128.0, 128.0), rings)
    l.DrawImage(Rect.Create(148.0, 90.0, 51.0, 51.0), rings)
    l.DrawImage(Rect.Create(148.0, 150.0, 26.0, 26.0), rings)
    l.DrawImage(Rect.Create(210.0, 90.0, 128.0, 128.0), rings, source = Rect.Create(96.0, 96.0, 32.0, 32.0))
    l.PushOpacity 0.5
    l.DrawImage(Rect.Create(348.0, 90.0, 64.0, 64.0), gradient)
    l.PopOpacity()
    l.DrawImage(Rect.Create(422.0, 90.0, 88.0, 88.0), sprite, radius = CornerRadius.Uniform 44.0)
    // Row 3: clipped, and an image between shapes (order kept).
    l.PushClip(Rect.Create(10.0, 230.0, 60.0, 30.0))
    l.DrawImage(Rect.Create(10.0, 230.0, 64.0, 64.0), gradient)
    l.PopClip()
    l.FillRect(Rect.Create(90.0, 230.0, 60.0, 60.0), Color.Hex 0x89B4FA)
    l.DrawImage(Rect.Create(110.0, 240.0, 40.0, 40.0), gradient)
    l.FillRoundedRect(Rect.Create(130.0, 260.0, 40.0, 30.0), CornerRadius.Uniform 6.0, Color.Hex 0xF38BA8)

/// The shader's filtering, on the CPU, in the level's own coordinates (translation by the atlas slot changes
/// nothing: the slot is whole texels). Straight from DESIGN.md (c) 4.
let private filtered (level: ImageLevel) (sx: float) (sy: float) (sw: float) (sh: float) (dw: int) (dh: int) (pixelated: bool) =
    let originX = int (Math.Round(sx * 256.0)) * 256
    let originY = int (Math.Round(sy * 256.0)) * 256
    let step (s: float) (t: int) = Math.Clamp(int (Math.Round(s / float t * 65536.0)), 1, (1 <<< 24) - 1)
    let stepX, stepY = step sw dw, step sh dh
    let x0, y0 = Math.Clamp(int (floor sx), 0, level.Width - 1), Math.Clamp(int (floor sy), 0, level.Height - 1)
    let x1 = Math.Clamp(int (ceil (sx + sw)) - 1, 0, level.Width - 1)
    let y1 = Math.Clamp(int (ceil (sy + sh)) - 1, 0, level.Height - 1)
    let axis (origin: int) (st: int) (j: int) (lo: int) (hi: int) =
        let s = origin + j * st + (st >>> 1) - 32768
        let nearest = pixelated && st <= 65536
        let t = s >>> 8
        let b = if nearest then (s + 32768) >>> 16 else t >>> 8
        let w = if nearest then 0 else t &&& 255
        Math.Clamp(b, lo, hi), Math.Clamp(b + 1, lo, hi), w
    let px (x: int) (y: int) (c: int) = int level.Pixels[(y * level.Width + x) * 4 + c]
    let out = Array.zeroCreate<byte> (dw * dh * 4)
    for j in 0 .. dh - 1 do
        let ya, yb, wy = axis originY stepY j y0 y1
        for i in 0 .. dw - 1 do
            let xa, xb, wx = axis originX stepX i x0 x1
            for c in 0..3 do
                let sum =
                    px xa ya c * ((256 - wx) * (256 - wy))
                    + px xb ya c * (wx * (256 - wy))
                    + px xa yb c * ((256 - wx) * wy)
                    + px xb yb c * (wx * wy)
                out[(j * dw + i) * 4 + c] <- byte ((sum + 32768) >>> 16)
    out

let private region (bitmap: Bitmap) (x: int) (y: int) (w: int) (h: int) =
    let out = Array.zeroCreate<byte> (w * h * 4)
    for row in 0 .. h - 1 do
        Array.blit bitmap.Pixels (((y + row) * bitmap.Size.Width + x) * 4) out (row * w * 4) (w * 4)
    out

let private firstDifference (expected: byte[]) (actual: byte[]) (w: int) =
    match Seq.tryFindIndex id (Seq.map2 (<>) expected actual) with
    | None -> ""
    | Some i ->
        let p = i / 4
        let count = Seq.map2 (<>) expected actual |> Seq.filter id |> Seq.length
        $"{count} channels differ; first at ({p % w}, {p / w}): expected {String.Join(',', expected[p * 4 .. p * 4 + 3])}, got {String.Join(',', actual[p * 4 .. p * 4 + 3])}"

type ImageTests(fixture: TextRenderingTests.TextFixture) =
    interface IClassFixture<TextRenderingTests.TextFixture>

    member private _.Render (w: int) (h: int) (draw: DrawList -> unit) =
        fixture.Gpu.Device.RenderOffscreen({ Width = w; Height = h }, 1.0, fun f ->
            f.Clear Color.Transparent
            draw f.DrawList)

    [<Fact>]
    member this.``drawn at its own size, an image is copied exactly``() =
        let image = noise 1 37 23
        let bitmap = this.Render 50 40 (fun l -> l.DrawImage(Rect.Create(3.0, 4.0, 37.0, 23.0), image))
        let actual = region bitmap 3 4 37 23
        if actual <> image.Pixels then Assert.Fail(firstDifference image.Pixels actual 37)
        Assert.Equal((0uy, 0uy, 0uy, 0uy), bitmap.GetPixel(2, 4))
        Assert.Equal((0uy, 0uy, 0uy, 0uy), bitmap.GetPixel(40, 4))

    [<Fact>]
    member this.``a part of an image is copied exactly, and reads stay inside it``() =
        let image = noise 2 20 20
        let bitmap = this.Render 20 20 (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 6.0, 5.0), image, source = Rect.Create(7.0, 3.0, 6.0, 5.0)))
        let expected = filtered (image.Level 0) 7.0 3.0 6.0 5.0 6 5 false
        let actual = region bitmap 0 0 6 5
        if actual <> expected then Assert.Fail(firstDifference expected actual 6)

    [<Theory>]
    [<InlineData(3, 2, false)>] // enlarged 3× and 2× (smooth)
    [<InlineData(3, 2, true)>] // pixelated: whole texels repeated
    [<InlineData(-3, -2, false)>] // shrunk to a third and a half
    [<InlineData(5, -2, true)>] // pixelated across, shrunk (smooth) down
    member this.``filtering is the whole-number formula, exactly``(fx: int, fy: int, pixelated: bool) =
        let image = noise 3 24 18
        let size (n: int) (by: int) = if by > 0 then n * by else n / -by
        let dw, dh = size 24 fx, size 18 fy
        let scaling = if pixelated then ImageScaling.Pixelated else ImageScaling.Smooth
        let bitmap = this.Render (dw + 4) (dh + 4) (fun l -> l.DrawImage(Rect.Create(2.0, 2.0, float dw, float dh), image, scaling = scaling))
        // The level the draw list picks: the first shrunk by less than 2×, by the axis that shrinks more.
        let mutable ratio = max (24.0 / float dw) (18.0 / float dh)
        let mutable k = 0
        while ratio >= 2.0 do
            ratio <- ratio / 2.0
            k <- k + 1
        let level = image.Level k
        let expected = filtered level 0.0 0.0 (float level.Width) (float level.Height) dw dh pixelated
        let actual = region bitmap 2 2 dw dh
        if actual <> expected then Assert.Fail(firstDifference expected actual dw)

    [<Fact>]
    member this.``shrunk 5× it draws mip level 2, filtered exactly``() =
        let image = noise 4 100 60
        let bitmap = this.Render 24 16 (fun l -> l.DrawImage(Rect.Create(0.0, 0.0, 20.0, 12.0), image))
        let level = image.Level 2 // 25 × 15: shrunk 1.25× more
        Assert.Equal((25, 15), (level.Width, level.Height))
        let expected = filtered level 0.0 0.0 25.0 15.0 20 12 false
        let actual = region bitmap 0 0 20 12
        if actual <> expected then Assert.Fail(firstDifference expected actual 20)

    [<Fact>]
    member this.``images on several pages keep the drawing order``() =
        // Three 1500² images can't share a 2048² page: three pages, with shapes between them.
        let a = make 1500 1500 (fun _ _ -> (255, 0, 0, 255))
        let b = make 1500 1500 (fun _ _ -> (0, 255, 0, 255))
        let c = make 1500 1500 (fun _ _ -> (0, 0, 255, 255))
        let part = Rect.Create(0.0, 0.0, 20.0, 20.0)
        let bitmap =
            this.Render 60 20 (fun l ->
                l.DrawImage(Rect.Create(0.0, 0.0, 20.0, 20.0), a, source = part)
                l.FillRect(Rect.Create(10.0, 0.0, 20.0, 20.0), Color.White) // over a, under b
                l.DrawImage(Rect.Create(20.0, 0.0, 20.0, 20.0), b, source = part)
                l.DrawImage(Rect.Create(30.0, 0.0, 20.0, 20.0), c, source = part)
                l.DrawImage(Rect.Create(45.0, 0.0, 10.0, 20.0), a, source = part)) // back to the first page
        Assert.Equal((255uy, 0uy, 0uy, 255uy), bitmap.GetPixel(5, 10))
        Assert.Equal((255uy, 255uy, 255uy, 255uy), bitmap.GetPixel(15, 10))
        Assert.Equal((0uy, 255uy, 0uy, 255uy), bitmap.GetPixel(25, 10))
        Assert.Equal((0uy, 0uy, 255uy, 255uy), bitmap.GetPixel(40, 10))
        Assert.Equal((255uy, 0uy, 0uy, 255uy), bitmap.GetPixel(50, 10))
        Assert.Empty fixture.Gpu.Errors
        for image in [ a; b; c ] do
            image.Release()

    [<Fact>]
    member this.``opacity and rounded corners fade and cut``() =
        let white = make 20 20 (fun _ _ -> (255, 255, 255, 255))
        let bitmap =
            this.Render 40 20 (fun l ->
                l.PushOpacity 0.5
                l.DrawImage(Rect.Create(0.0, 0.0, 20.0, 20.0), white)
                l.PopOpacity()
                l.DrawImage(Rect.Create(20.0, 0.0, 20.0, 20.0), white, radius = CornerRadius.Uniform 10.0))
        Assert.Equal((128uy, 128uy, 128uy, 128uy), bitmap.GetPixel(10, 10))
        Assert.Equal((0uy, 0uy, 0uy, 0uy), bitmap.GetPixel(20, 0))
        Assert.Equal((255uy, 255uy, 255uy, 255uy), bitmap.GetPixel(30, 10))

    [<Theory>]
    [<InlineData(1.0)>]
    [<InlineData(2.0)>]
    member _.``images: stretching, scaling, mip levels, alpha, corners``(scale: float) =
        let size: PixelSize = { Width = int (520.0 * scale); Height = int (300.0 * scale) }
        let image = fixture.Gpu.Device.RenderOffscreen(size, scale, draw)
        match Snapshot.check ("images@" + string (int scale) + "x") image with
        | Some failure -> Assert.Fail failure
        | None -> ()
        Assert.Empty fixture.Gpu.Errors
