/// M8 step 2 on the GPU: the Gallery's Layout page (Grid, DockPanel, Canvas, WrapPanel,
/// Separators, dashed and dotted borders), and the inspector's Grid overlay on its form.
module Scribble.Tests.Rendering.LayoutPageTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Inspector
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Samples.Gallery

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let rec private firstText (e: Element) : string =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then e.Get TextProps.text
    else e.Children |> Seq.map firstText |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""

/// Tall enough to show the whole page without scrolling.
let private width, height = 720.0, 1100.0

type LayoutPageTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    /// The Gallery with the Layout page selected.
    member private _.LayoutPage() =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome Common.darkTheme
        tree.Update(Scribble.Samples.GalleryApp.gallery ())
        let size = Size.Create(width, height)
        tree.UpdateLayout size
        tree.RunAfterFrame()
        let button = all tree.Root.Value |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && firstText e = "Layout")
        (button.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
        tree.UpdateLayout size
        tree.RunAfterFrame()
        tree.UpdateLayout size
        tree

    member private _.Shoot(trees: ElementTree list, name: string) =
        let size: PixelSize = { Width = int width; Height = int height }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                for t in trees do
                    t.Render f.DrawList)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``the Layout page``() =
        let tree = this.LayoutPage()
        // The form's star column keeps at least 120 DIPs; its buttons sit in Auto columns.
        let form = all tree.Root.Value |> Seq.find (fun e -> e.Name = "form")
        Assert.True(form.Children[1].Bounds.Width >= 120.0)
        this.Shoot([ tree ], "layout-page@1x")

    [<Fact>]
    member this.``the inspector's Grid overlay``() =
        let tree = this.LayoutPage()
        let form = all tree.Root.Value |> Seq.find (fun e -> e.Name = "form")
        // The highlight as the inspector's layer draws it: its lines and gaps over the form.
        let highlight = ElementTree(fixture.Text)
        highlight.Update(Highlight.create [ Highlight.target (ValueSome form); Highlight.outlineOnly true ])
        highlight.UpdateLayout(Size.Create(width, height))
        Assert.True((Grid.edges form).IsSome)
        this.Shoot([ tree; highlight ], "layout-grid-overlay@1x")
