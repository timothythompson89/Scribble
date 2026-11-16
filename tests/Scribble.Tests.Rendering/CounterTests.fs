/// The counter sample against its reference picture.
module Scribble.Tests.Rendering.CounterTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Samples.CounterDemo

type CounterTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Render(view: View) =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome Common.darkTheme // as in the app (the count's text color comes from it)
        tree.Update view
        tree.UpdateLayout(Size.Create(480.0, 180.0))
        let size: PixelSize = { Width = 480; Height = 180 }
        fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
            f.Clear(Color.Hex 0x181825)
            tree.Render f.DrawList)

    [<Fact>]
    member this.``the counter``() =
        let ignoreMsg (_: Scribble.Samples.CounterDemo.ElmishTab.Msg) = ()
        let image = this.Render(Scribble.Samples.CounterDemo.ElmishTab.view 3 ignoreMsg)
        match Snapshot.check "counter@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``the components demo``() =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome(Theme.dark []) // as in the app: Reset (disabled) fades
        tree.Update(Scribble.Samples.Counter.demo ())
        tree.UpdateLayout(Size.Create(520.0, 520.0))
        let size: PixelSize = { Width = 520; Height = 520 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
        match Snapshot.check "counter-demo@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    /// M7 step 1: the Styles tab (classes, a state block, text inside a card), with the card hovered.
    [<Fact>]
    member _.``the styles page, card hovered``() =
        let tree = ElementTree(fixture.Text)
        tree.Update(StylesTab.stylesPage ())
        let size = Size.Create(520.0, 360.0)
        tree.UpdateLayout size
        tree.RunAfterFrame()
        let rec findCard (e: Element) : Element option =
            if e.Type.Name = "Border" && not (e.Get LayoutProps.classes).IsEmpty then Some e
            else e.Children |> Seq.tryPick findCard
        let card = (findCard tree.Root.Value).Value
        card.ChangePointersOver 1
        tree.UpdateLayout size
        let pixels: PixelSize = { Width = 520; Height = 360 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
        match Snapshot.check "styles-page@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    /// M7 step 2b: the demo in the light theme (the window theme; the
    /// window's background follows it, as the clear color here does).
    [<Fact>]
    member _.``the components demo, light``() =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome Common.lightTheme // what the host gives its layers when Theme.current is light
        tree.Update(Scribble.Samples.Counter.demo ())
        tree.UpdateLayout(Size.Create(520.0, 520.0))
        let size: PixelSize = { Width = 520; Height = 520 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear (Common.lightTheme.Get Palette.background)
                tree.Render f.DrawList)
        tree.Root <- ValueNone
        match Snapshot.check "counter-demo-light@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    /// M7 step 2b: the Styles tab in the light theme (Primary and Danger with white text, light cards).
    [<Fact>]
    member _.``the styles page, light``() =
        let tree = ElementTree(fixture.Text)
        tree.Update(Border.create [ Border.theme Common.lightTheme; Border.child (StylesTab.stylesPage ()) ])
        tree.UpdateLayout(Size.Create(520.0, 360.0))
        let pixels: PixelSize = { Width = 520; Height = 360 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear (Common.lightTheme.Get Palette.background)
                tree.Render f.DrawList)
        match Snapshot.check "styles-page-light@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
