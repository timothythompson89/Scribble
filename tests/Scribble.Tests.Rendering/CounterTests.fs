/// The counter sample against its reference picture.
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
    member this.``the counter``() =
        let ignoreMsg (_: Scribble.Samples.Counter.Msg) = ()
        let image = this.Render(Scribble.Samples.Counter.view 3 ignoreMsg)
        match Snapshot.check "counter@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``the components demo``() =
        let tree = ElementTree(fixture.Text)
        tree.Update(Scribble.Samples.Counter.demo ())
        tree.UpdateLayout(Size.Create(480.0, 520.0))
        let size: PixelSize = { Width = 480; Height = 520 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
        match Snapshot.check "counter-demo@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
