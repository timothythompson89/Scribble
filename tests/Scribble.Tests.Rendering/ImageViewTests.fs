/// M8 step 3b (d) on the GPU: the Gallery's Images page through ImageView (its embedded pictures, decoded inline):
/// the stretch modes and directions, smooth against pixelated, a 2× avatar and a round one, a pattern from pixels.
module Scribble.Tests.Rendering.ImageViewTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core

type ImageViewTests(fixture: TextRenderingTests.TextFixture) =
    interface IClassFixture<TextRenderingTests.TextFixture>

    [<Fact>]
    member _.``the Gallery's Images page``() =
        ImageCache.DecodeInlineOnThisThread <- true
        let theme = Theme.dark []
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome theme
        tree.Update(Scribble.Samples.Gallery.ImagesPage.view ())
        let width, height = 720.0, 1560.0
        tree.UpdateLayout(Size.Create(width, height))
        tree.RunAfterFrame()
        let pixels: PixelSize = { Width = int width; Height = int height }
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(theme.Get Palette.background)
                tree.Render f.DrawList)
        match Snapshot.check "images-page@1x" image with
        | Some failure -> Assert.Fail failure
        | None -> ()
        Assert.Empty fixture.Gpu.Errors
