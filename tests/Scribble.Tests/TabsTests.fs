/// The Counter sample's tabs (a TabControl since M8 step 3a): a page is built on its first visit,
/// switching shows one page, and every visited page keeps its state.
module Scribble.Tests.TabsTests

open System
open Xunit
open Scribble.Core
open Scribble.Tests.TestShaping

let private frame (t: ElementTree) =
    t.UpdateLayout(Size.Create(520.0, 520.0))
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
    tree.Update(Scribble.Samples.Counter.demo ())
    frame tree
    // Only the first page exists yet.
    let elmish = (pages tree)[0]
    Assert.Equal(1, (pages tree).Length)

    // Change the Elmish page, then switch to the List page and change it too.
    click elmish "+"
    click elmish "+"
    frame tree
    click (tabControl tree) "List"
    frame tree
    let list = (pages tree)[1]
    Assert.Equal<bool list>([ false; true ], [ for p in pages tree -> p.IsVisible ])
    click list "Add"
    frame tree
    Assert.Contains("Cherries", texts list) |> ignore

    // Back to Elmish: the count survived, and the pages are the same elements (patched, not rebuilt).
    click (tabControl tree) "Elmish"
    frame tree
    Assert.Equal<bool list>([ true; false ], [ for p in pages tree -> p.IsVisible ])
    Assert.Same(elmish, (pages tree)[0])
    Assert.Same(list, (pages tree)[1])
    Assert.Contains("Count: 2", texts elmish)
    click (tabControl tree) "List"
    frame tree
    Assert.Contains("Cherries", texts list)

[<Fact>]
let ``pages not visited aren't built, and hidden ones aren't laid out`` () =
    let tree = ElementTree(textSystem ())
    tree.Update(Scribble.Samples.Counter.demo ())
    frame tree
    let headers = all (tabControl tree) |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, TabHeader.Type)) |> Seq.length
    Assert.Equal(7, headers)
    Assert.Equal(1, (pages tree).Length)
    let first = (pages tree)[0]
    Assert.True(first.Bounds.Height > 0.0)
    click (tabControl tree) "Layout"
    frame tree
    Assert.False first.IsVisible
    Assert.Equal(2, (pages tree).Length)
