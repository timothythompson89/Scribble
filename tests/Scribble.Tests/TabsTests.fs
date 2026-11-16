/// The Counter sample's tabs: switching shows one page and every page keeps its state.
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

/// The demo's StackPanel: the tab row, then the three pages.
let private parts (t: ElementTree) =
    let panel = t.Root.Value.Children[0]
    panel.Children[0], [ for i in 1..3 -> panel.Children[i] ]

let private texts (e: Element) =
    [ for x in all e do if obj.ReferenceEquals(x.Type, TextBlock.Type) then x.Get TextProps.text ]

[<Fact>]
let ``only the selected page shows, and switching tabs keeps every page's state`` () =
    let tree = ElementTree(textSystem ())
    tree.Update(Scribble.Samples.Counter.demo ())
    frame tree
    let tabs, pages = parts tree
    Assert.Equal<bool list>([ true; false; false ], [ for p in pages -> p.IsVisible ])

    // Change the Elmish page, then switch to the List page and change it too.
    click pages[0] "+"
    click pages[0] "+"
    frame tree
    click tabs "List"
    frame tree
    Assert.Equal<bool list>([ false; false; true ], [ for p in pages -> p.IsVisible ])
    click pages[2] "Add"
    frame tree
    Assert.Contains("Cherries", texts pages[2]) |> ignore

    // Back to Elmish: the count survived, and the pages are the same elements (patched, not rebuilt).
    click tabs "Elmish"
    frame tree
    let tabs2, pages2 = parts tree
    Assert.Equal<bool list>([ true; false; false ], [ for p in pages2 -> p.IsVisible ])
    Assert.True(List.forall2 (fun (a: Element) b -> obj.ReferenceEquals(a, b)) pages pages2)
    Assert.Contains("Count: 2", texts pages2[0])
    click tabs2 "List"
    frame tree
    Assert.Contains("Cherries", texts pages2[2])

[<Fact>]
let ``hidden pages aren't laid out, so they take no room`` () =
    let tree = ElementTree(textSystem ())
    tree.Update(Scribble.Samples.Counter.demo ())
    frame tree
    let _, pages = parts tree
    Assert.True(pages[0].Bounds.Height > 0.0)
    Assert.Equal(0.0, pages[1].Bounds.Height)
    Assert.Equal(0.0, pages[2].Bounds.Height)
