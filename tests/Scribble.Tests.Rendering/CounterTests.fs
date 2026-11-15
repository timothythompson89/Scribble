/// The counter sample in both syntaxes: settings lists and the experimental builders must produce
/// the same picture.
module Scribble.Tests.Rendering.CounterTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests

type CounterTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Render(view: View) =
        let tree = ElementTree(fixture.Text)
        tree.Update view
        tree.UpdateLayout(Size.Create(360.0, 180.0))
        let size: PixelSize = { Width = 360; Height = 180 }
        fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
            f.Clear(Color.Hex 0x181825)
            tree.Render f.DrawList)

    [<Fact>]
    member this.``both syntaxes draw the same counter``() =
        let ignoreMsg (_: Scribble.Samples.Counter.Msg) = ()
        let lists = this.Render(Scribble.Samples.Counter.view 3 ignoreMsg)
        let builders = this.Render(Scribble.Samples.CounterBuilders.view 3 ignoreMsg)
        let result = Snapshot.compare lists builders
        Console.WriteLine($"counter: {result.DifferentPixels} of {result.TotalPixels} pixels differ between the two syntaxes")
        Assert.Equal(0, result.DifferentPixels)
        match Snapshot.check "counter@1x" lists with
        | Some error -> Assert.Fail error
        | None -> ()
