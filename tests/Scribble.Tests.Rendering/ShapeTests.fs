/// Shapes rendered through the real WebGPU backend, checked pixel by pixel and against
/// reference screenshots.
module Scribble.Tests.Rendering.ShapeTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Tests.Rendering.ClearTests

let private render (gpu: GpuFixture) (width: int) (height: int) (scale: float) draw =
    gpu.Device.RenderOffscreen({ Width = width; Height = height }, scale, draw)

let private rgb (c: Color) =
    (byte (round (c.R * 255.0f)), byte (round (c.G * 255.0f)), byte (round (c.B * 255.0f)), 255uy)

type ShapeTests(gpu: GpuFixture) =
    interface IClassFixture<GpuFixture>

    [<Fact>]
    member _.``a filled rect covers exactly its pixels``() =
        let red = Color.Rgb(1.0f, 0.0f, 0.0f)
        let bitmap = render gpu 20 20 1.0 (fun f ->
            f.Clear Color.Black
            f.DrawList.FillRect(Rect.Create(5.0, 5.0, 10.0, 10.0), red))
        expectPixel bitmap 5 5 (rgb red)
        expectPixel bitmap 14 14 (rgb red)
        expectPixel bitmap 4 5 (0uy, 0uy, 0uy, 255uy)
        expectPixel bitmap 15 14 (0uy, 0uy, 0uy, 255uy)
        Assert.Empty gpu.Errors

    [<Fact>]
    member _.``rounded corners are cut and the inside is solid``() =
        let bitmap = render gpu 40 40 1.0 (fun f ->
            f.Clear Color.Black
            f.DrawList.FillRoundedRect(Rect.Create(0.0, 0.0, 40.0, 40.0), CornerRadius.Uniform 16.0, Color.White))
        expectPixel bitmap 0 0 (0uy, 0uy, 0uy, 255uy)
        expectPixel bitmap 20 20 (255uy, 255uy, 255uy, 255uy)
        expectPixel bitmap 20 0 (255uy, 255uy, 255uy, 255uy)

    [<Fact>]
    member _.``borders are drawn inside the edge``() =
        let bitmap = render gpu 20 20 2.0 (fun f ->
            f.Clear Color.Black
            f.DrawList.DrawBox(Rect.Create(0.0, 0.0, 10.0, 10.0), CornerRadius.Zero, Color.White, 1.0, Color.Rgb(0.0f, 0.0f, 1.0f)))
        // 1 DIP at 2× = 2 pixels of border, then fill.
        expectPixel bitmap 0 10 (0uy, 0uy, 255uy, 255uy)
        expectPixel bitmap 1 10 (0uy, 0uy, 255uy, 255uy)
        expectPixel bitmap 2 10 (255uy, 255uy, 255uy, 255uy)

    [<Fact>]
    member _.``clips cut shapes at their edge``() =
        let bitmap = render gpu 20 20 1.0 (fun f ->
            f.Clear Color.Black
            f.DrawList.PushClip(Rect.Create(0.0, 0.0, 10.0, 20.0))
            f.DrawList.FillRect(Rect.Create(0.0, 0.0, 20.0, 20.0), Color.White)
            f.DrawList.PopClip())
        expectPixel bitmap 9 10 (255uy, 255uy, 255uy, 255uy)
        expectPixel bitmap 10 10 (0uy, 0uy, 0uy, 255uy)

    [<Fact>]
    member _.``translucent shapes blend over what's below``() =
        let bitmap = render gpu 10 10 1.0 (fun f ->
            f.Clear Color.Black
            f.DrawList.FillRect(Rect.Create(0.0, 0.0, 10.0, 10.0), Color.Rgba(1.0f, 1.0f, 1.0f, 0.5f)))
        expectPixel bitmap 5 5 (128uy, 128uy, 128uy, 255uy)

    [<Fact>]
    member _.``shadows fade out from the shape``() =
        let bitmap = render gpu 60 60 1.0 (fun f ->
            f.Clear Color.White
            f.DrawList.DrawShadow(Rect.Create(20.0, 20.0, 20.0, 20.0), CornerRadius.Zero, 16.0, Color.Black))
        let (centre, _, _, _) = bitmap.GetPixel(30, 30)
        let (near, _, _, _) = bitmap.GetPixel(30, 44)
        let (far, _, _, _) = bitmap.GetPixel(30, 59)
        Assert.True(centre < near && near < far, $"expected darker towards the centre: {centre} < {near} < {far}")

    [<Theory>]
    [<InlineData(1.0)>]
    [<InlineData(1.5)>]
    [<InlineData(2.0)>]
    member _.``shapes scene matches its reference``(scale: float) =
        let bitmap = render gpu (int (400.0 * scale)) (int (300.0 * scale)) scale Scenes.shapes
        Assert.Empty gpu.Errors
        match Snapshot.check $"shapes@{scale}x" bitmap with
        | Some failure -> Assert.Fail failure
        | None -> ()

    [<Fact>]
    member _.``many shapes in one frame``() =
        let bitmap = render gpu 100 100 1.0 (fun f ->
            f.Clear Color.Black
            for i in 0..9999 do
                f.DrawList.FillRect(Rect.Create(float (i % 100), float (i / 100), 1.0, 1.0), Color.White))
        expectPixel bitmap 99 99 (255uy, 255uy, 255uy, 255uy)
        Assert.Empty gpu.Errors
