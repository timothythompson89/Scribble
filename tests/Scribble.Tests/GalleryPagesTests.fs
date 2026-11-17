/// The Gallery's pages (a TabControl since M8 step 3a): a page is built on its first visit,
/// switching shows one page, and every visited page keeps its state.
module Scribble.Tests.GalleryPagesTests

open System
open Xunit
open Scribble.Core
open Scribble.Tests.TestShaping

let private frame (t: ElementTree) =
    t.UpdateLayout(Size.Create(720.0, 560.0))
    t.RunAfterFrame()

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let rec private firstText (e: Element) : string =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then e.Get TextProps.text
    else e.Children |> Seq.map firstText |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""

/// Clicks the first element under `scope` with an onClick whose label is `label`.
let private click (scope: Element) (label: string) =
    let target =
        all scope |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && firstText e = label)
    (target.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))

let private tabControl (t: ElementTree) = all t.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TabControl.Type))

/// The realized pages (TabItem elements), in tab order.
let private pages (t: ElementTree) =
    [ for c in (tabControl t).Children do if obj.ReferenceEquals(c.Type, TabItem.Type) then c ]

let private texts (e: Element) =
    [ for x in all e do if obj.ReferenceEquals(x.Type, TextBlock.Type) then x.Get TextProps.text ]

[<Fact>]
let ``only the selected page shows, and switching tabs keeps every visited page's state`` () =
    let tree = ElementTree(textSystem ())
    tree.Update(Scribble.Samples.GalleryApp.gallery ())
    frame tree
    // Only the first page exists yet.
    let components = (pages tree)[0]
    Assert.Equal(1, (pages tree).Length)

    // Change the Components page (the Elmish counter's "+" comes first, then the list), then switch to the
    // Styles page and change it too.
    click components "+"
    click components "+"
    click components "Add"
    frame tree
    Assert.Contains("Cherries", texts components) |> ignore
    click (tabControl tree) "Styles"
    frame tree
    let styles = (pages tree)[1]
    Assert.Equal<bool list>([ false; true ], [ for p in pages tree -> p.IsVisible ])
    click styles "Compact buttons"
    frame tree
    Assert.Contains("Normal buttons", texts styles) |> ignore

    // Back to Components: the count and the list survived, and the pages are the same elements (patched, not
    // rebuilt).
    click (tabControl tree) "Components"
    frame tree
    Assert.Equal<bool list>([ true; false ], [ for p in pages tree -> p.IsVisible ])
    Assert.Same(components, (pages tree)[0])
    Assert.Same(styles, (pages tree)[1])
    Assert.Contains("Count: 2", texts components)
    Assert.Contains("Cherries", texts components)
    click (tabControl tree) "Styles"
    frame tree
    Assert.Contains("Normal buttons", texts styles)

[<Fact>]
let ``pages not visited aren't built, and hidden ones aren't laid out`` () =
    let tree = ElementTree(textSystem ())
    tree.Update(Scribble.Samples.GalleryApp.gallery ())
    frame tree
    let headers = all (tabControl tree) |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, TabHeader.Type)) |> Seq.length
    Assert.Equal(6, headers)
    Assert.Equal(1, (pages tree).Length)
    let first = (pages tree)[0]
    Assert.True(first.Bounds.Height > 0.0)
    click (tabControl tree) "Layout"
    frame tree
    Assert.False first.IsVisible
    Assert.Equal(2, (pages tree).Length)

[<Fact>]
let ``every page opens with its title and a line on what it shows`` () =
    let tree = ElementTree(textSystem ())
    tree.Update(Scribble.Samples.GalleryApp.gallery ())
    frame tree
    let titles = [ "Components"; "Layout"; "Controls"; "Scrolling"; "Layers"; "Styles" ]
    for title in titles do
        click (tabControl tree) title
        frame tree
        let page = pages tree |> List.find (fun p -> p.IsVisible)
        match texts page with
        | first :: line :: _ ->
            Assert.Equal(title, first)
            Assert.True(line.Length > 20, $"the {title} page's line: \"{line}\"")
        | other -> Assert.Fail($"the {title} page starts with {other.Length} texts")

/// Scrolling page: "Add message" scrolls the list to the new message and the page just enough to show all of it
/// (the page's bottom is below the window at 720 × 560).
[<Theory>]
[<InlineData(560.0)>]
[<InlineData(500.0)>]
[<InlineData(420.0)>]
let ``"Add message" shows the whole new message, moving the page as needed`` (height: float) =
    let tree = ElementTree(textSystem ())
    tree.Update(Scribble.Samples.GalleryApp.gallery ())
    let size = Size.Create(720.0, height)
    let settle () =
        for _ in 1 .. 40 do
            tree.Time <- tree.Time + TimeSpan.FromMilliseconds 16.0
            tree.UpdateLayout size
            tree.RunAfterFrame()
    settle ()
    click (tabControl tree) "Scrolling"
    settle ()
    for n in 1 .. 3 do
        click (tabControl tree).Parent.Value "Add message"
        settle ()
        let row =
            all tree.Root.Value
            |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TextBlock.Type) && (e.Get TextProps.text).StartsWith("#10,00" + string n + " "))
            |> fun text -> text.Parent.Value
        let p = row.PositionInTree
        Assert.True(p.Y >= 0.0 && p.Y + row.Bounds.Height <= height + 0.5,
                    $"message {n}: at {p.Y}..{p.Y + row.Bounds.Height} in a window {height} high; shows {row.VisibleBounds.Height} of {row.Bounds.Height}")
        Assert.Equal(row.Bounds.Height, row.VisibleBounds.Height, 1)
