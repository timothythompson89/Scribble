/// Text drawn through the real GPU backend with real HarfBuzz shaping.
module Scribble.Tests.Rendering.TextRenderingTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Text.HarfBuzz
open Scribble.Text.Emoji
open Scribble.Tests.Rendering.ClearTests

/// GPU device plus a text system on its atlas, shared by the text tests.
type TextFixture() =
    let gpu = new GpuFixture()
    let shaper = new HarfBuzzShaper()
    let text = TextSystem.create gpu.Device shaper [ TextSystem.fallbacks [ Emoji.Twemoji() ] ]
    member _.Gpu = gpu
    member _.Text = text
    interface IDisposable with
        member _.Dispose() =
            (shaper :> IDisposable).Dispose()
            (gpu :> IDisposable).Dispose()

type TextRenderingTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    [<Fact>]
    member _.``text leaves ink inside its measured box and none outside``() =
        let font = Fonts.Regular()
        let m = fixture.Text.Measure("Hamburgefonstiv", font, 20.0)
        let bitmap =
            fixture.Gpu.Device.RenderOffscreen({ Width = 300; Height = 60 }, 1.0, fun f ->
                f.Clear Color.White
                f.DrawList.DrawText(fixture.Text, "Hamburgefonstiv", font, 20.0, 10.0, 10.0, Color.Black))
        let mutable inkInside, inkOutside = 0, 0
        for y in 0..59 do
            for x in 0..299 do
                let (r, _, _, _) = bitmap.GetPixel(x, y)
                if r < 128uy then
                    if float x >= 9.0 && float x <= 11.0 + m.Width && float y >= 9.0 && float y <= 11.0 + m.Height then
                        inkInside <- inkInside + 1
                    else
                        inkOutside <- inkOutside + 1
        Assert.True(inkInside > 200, $"expected visible text, found {inkInside} dark pixels")
        Assert.Equal(0, inkOutside)
        Assert.Empty fixture.Gpu.Errors

    [<Fact>]
    member _.``shaping applies kerning``() =
        let font = Fonts.Regular()
        let kerned = fixture.Text.Measure("AV", font, 100.0).Width
        let separate = fixture.Text.Measure("A", font, 100.0).Width + fixture.Text.Measure("V", font, 100.0).Width
        Assert.True(kerned < separate, $"AV ({kerned}) should be narrower than A + V ({separate})")

    [<Theory>]
    [<InlineData(1.0)>]
    [<InlineData(2.0)>]
    member _.``text scene matches its reference``(scale: float) =
        let bitmap =
            fixture.Gpu.Device.RenderOffscreen(
                { Width = int (520.0 * scale); Height = int (260.0 * scale) },
                scale,
                Scenes.text fixture.Text
            )
        Assert.Empty fixture.Gpu.Errors
        match Snapshot.check $"text@{scale}x" bitmap with
        | Some failure -> Assert.Fail failure
        | None -> ()

    [<Fact>]
    member _.``emoji keep their own colors whatever the text color``() =
        let bitmap =
            fixture.Gpu.Device.RenderOffscreen({ Width = 60; Height = 40 }, 1.0, fun f ->
                f.Clear Color.White
                f.DrawList.DrawText(fixture.Text, "🔴", Fonts.Regular(), 28.0, 10.0, 2.0, Color.Black))
        let mutable red = 0
        for y in 0..39 do
            for x in 0..59 do
                let (r, g, b, _) = bitmap.GetPixel(x, y)
                if r > 180uy && g < 100uy && b < 100uy then red <- red + 1
        Assert.True(red > 100, $"expected a red disc, found {red} red pixels")
        Assert.Empty fixture.Gpu.Errors

    [<Theory>]
    [<InlineData(1.0)>]
    [<InlineData(2.0)>]
    member _.``emoji scene matches its reference``(scale: float) =
        let bitmap =
            fixture.Gpu.Device.RenderOffscreen(
                { Width = int (560.0 * scale); Height = int (250.0 * scale) },
                scale,
                Scenes.emoji fixture.Text
            )
        Assert.Empty fixture.Gpu.Errors
        match Snapshot.check $"emoji@{scale}x" bitmap with
        | Some failure -> Assert.Fail failure
        | None -> ()
