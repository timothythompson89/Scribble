/// The Counter and the Gallery against their reference pictures.
module Scribble.Tests.Rendering.GalleryTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Samples.Gallery

/// The Gallery's window size.
let private width, height = 720, 560

type GalleryTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Render(view: View) =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome Common.darkTheme // as in the app (the count's text color comes from it)
        tree.Update view
        tree.UpdateLayout(Size.Create(360.0, 200.0))
        let size: PixelSize = { Width = 360; Height = 200 }
        fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
            f.Clear(Color.Hex 0x181825)
            tree.Render f.DrawList)

    /// The Counter sample (the README's first example) at its window size.
    [<Fact>]
    member this.``the counter``() =
        let ignoreMsg (_: Scribble.Samples.Counter.Msg) = ()
        let image = this.Render(Scribble.Samples.Counter.view 3 ignoreMsg)
        match Snapshot.check "counter@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``the gallery``() =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome(Theme.dark []) // as in the app: Reset (disabled) fades
        tree.Update(Scribble.Samples.GalleryApp.gallery ())
        tree.UpdateLayout(Size.Create(float width, float height))
        let size: PixelSize = { Width = width; Height = height }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
        match Snapshot.check "gallery@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    /// M7 step 1: the Styles page (classes, a state block, text inside a card), with the card hovered.
    [<Fact>]
    member _.``the styles page, card hovered``() =
        let tree = ElementTree(fixture.Text)
        tree.Update(Border.create [ Border.margin 24.0; Border.content (StylesPage.demo ()) ])
        let size = Size.Create(520.0, 360.0)
        tree.UpdateLayout size
        tree.RunAfterFrame()
        let rec findCard (e: Element) : Element option =
            if e.Type.Name = "Border" && (e.Get LayoutProps.classes) = [ "card" ] then Some e
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

    /// M7 step 2b: the Gallery in the light theme (the window theme; the
    /// window's background follows it, as the clear color here does).
    [<Fact>]
    member _.``the gallery, light``() =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome Common.lightTheme // what the host gives its layers when Theme.current is light
        tree.Update(Scribble.Samples.GalleryApp.gallery ())
        tree.UpdateLayout(Size.Create(float width, float height))
        let size: PixelSize = { Width = width; Height = height }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear (Common.lightTheme.Get Palette.background)
                tree.Render f.DrawList)
        tree.Root <- ValueNone
        match Snapshot.check "gallery-light@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    /// M7 step 2b: the Styles page in the light theme (Primary and Danger with white text, light cards).
    [<Fact>]
    member _.``the styles page, light``() =
        let tree = ElementTree(fixture.Text)
        tree.Update(Border.create [ Border.theme Common.lightTheme; Border.padding 24.0; Border.content (StylesPage.demo ()) ])
        tree.UpdateLayout(Size.Create(520.0, 360.0))
        let pixels: PixelSize = { Width = 520; Height = 360 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear (Common.lightTheme.Get Palette.background)
                tree.Render f.DrawList)
        match Snapshot.check "styles-page-light@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
