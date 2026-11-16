/// The samples at phone size (390 × 844 DIPs at 3×, a common phone), so their narrow layouts can
/// be seen without a phone: Hello's toolbar under its title, Counter's tabs wrapped, every tab.
module Scribble.Tests.Rendering.PhoneTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Samples.CounterDemo

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
    [<InlineData("Elmish")>]
    [<InlineData("Load")>]
    [<InlineData("List")>]
    [<InlineData("Layers")>]
    [<InlineData("Styles")>]
    member this.``Counter on a phone``(tab: string) =
        let tree = ElementTree(fixture.Text, Scale = scale)
        tree.Theme <- ValueSome Common.darkTheme
        tree.Update(Scribble.Samples.Counter.demo ())
        let size = Size.Create(width, height)
        tree.UpdateLayout size
        tree.RunAfterFrame()
        let button = all tree.Root.Value |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && firstText e = tab)
        (button.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
        tree.UpdateLayout size
        tree.RunAfterFrame()
        tree.UpdateLayout size
        // Nothing sticks out past the right edge (the window has no horizontal scrolling).
        let rec widest (e: Element) (x: float) : float =
            if not e.IsVisible then 0.0
            else
                let right = x + e.Bounds.X + e.Bounds.Width
                e.Children |> Seq.fold (fun m c -> max m (widest c (x + e.Bounds.X))) right
        Assert.True(widest tree.Root.Value 0.0 <= width + 0.5, $"something reaches x = {widest tree.Root.Value 0.0}")
        this.Shoot(tree, Color.Hex 0x181825, "counter-phone-" + tab.ToLowerInvariant() + "@3x")
