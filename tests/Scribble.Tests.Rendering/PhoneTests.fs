/// The samples at phone size (390 × 844 DIPs at 3×, a common phone), so their narrow layouts can
/// be seen without a phone: Hello's toolbar under its title, the Gallery's tabs scrolling sideways, every page.
module Scribble.Tests.Rendering.PhoneTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Samples.Gallery

let private width, height, scale = 390.0, 844.0, 3.0

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let rec private firstText (e: Element) : string =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then e.Get TextProps.text
    else e.Children |> Seq.map firstText |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""

type PhoneTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Shoot(tree: ElementTree, background: Color, name: string) =
        let size: PixelSize = { Width = int (width * scale); Height = int (height * scale) }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, scale, fun f ->
                f.Clear background
                tree.Render f.DrawList)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``Hello on a phone: the toolbar wraps under the title, and the cards scroll``() =
        let tree = ElementTree(fixture.Text, Scale = scale)
        tree.Theme <- ValueSome(Theme.dark [])
        tree.Update(Scribble.Samples.HelloApp.view Scribble.Samples.HelloApp.init ignore)
        tree.UpdateLayout(Size.Create(width, height))
        let scroller = tree.Root.Value.Children[2]
        // The cards go past the bottom: the ScrollViewer has something to scroll.
        Assert.True(scroller.Children[0].Bounds.Height > scroller.Bounds.Height)
        this.Shoot(tree, Scribble.Samples.Dashboard.background, "hello-phone@3x")

    [<Theory>]
    [<InlineData("Components")>]
    [<InlineData("Scrolling")>]
    [<InlineData("Layers")>]
    [<InlineData("Styles")>]
    [<InlineData("Layout")>]
    [<InlineData("Controls")>]
    [<InlineData("Lists")>]
    member this.``the Gallery on a phone``(tab: string) =
        let tree = ElementTree(fixture.Text, Scale = scale)
        tree.Theme <- ValueSome Common.darkTheme
        tree.Update(Scribble.Samples.GalleryApp.gallery ())
        let size = Size.Create(width, height)
        tree.UpdateLayout size
        tree.RunAfterFrame()
        let button = all tree.Root.Value |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && firstText e = tab)
        (button.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
        tree.UpdateLayout size
        tree.RunAfterFrame()
        tree.UpdateLayout size
        // The selected header scrolls into view (animated, about 200 ms): past it.
        for ms in [ 100.0; 1000.0 ] do
            tree.Time <- System.TimeSpan.FromMilliseconds ms
            tree.UpdateLayout size
            tree.RunAfterFrame()
        // Nothing sticks out past the right edge (the window has no horizontal scrolling); what a
        // scroller holds is clipped to it (the tab headers scroll sideways).
        let rec widest (e: Element) (x: float) : float =
            if not e.IsVisible then 0.0
            else
                let right = x + e.Bounds.X + e.Bounds.Width
                if obj.ReferenceEquals(e.Type, ScrollViewer.Type) then right
                else e.Children |> Seq.fold (fun m c -> max m (widest c (x + e.Bounds.X))) right
        Assert.True(widest tree.Root.Value 0.0 <= width + 0.5, $"something reaches x = {widest tree.Root.Value 0.0}")
        this.Shoot(tree, Color.Hex 0x181825, "gallery-phone-" + tab.ToLowerInvariant() + "@3x")
        // The selected tab's header was scrolled into view (the headers don't all fit).
        let header = all tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TabHeader.Type) && firstText e = tab)
        Assert.True(abs (header.VisibleBounds.Width - header.Bounds.Width) < 0.5, $"the {tab} header shows {header.VisibleBounds.Width} of {header.Bounds.Width}")
