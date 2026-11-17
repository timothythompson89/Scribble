/// ItemsView (virtualization) and ScrollViewer scrolling, on a bare element tree. The agreed bar
/// (M8): the same elements realized for 100 and 10,000 items, a scroll step's layout and draw list
/// within 1.5×, and scrolling to item 9,000 without realizing the ones between.
module Scribble.Tests.ItemsViewTests

open System
open System.Collections.Generic
open System.Diagnostics
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping

type Item = { Id: int; Height: float }

let private items (n: int) (height: int -> float) = Array.init n (fun i -> { Id = i; Height = height i })
let private variable (i: int) = 20.0 + float (i % 3) * 20.0

/// A 300×200 tree: a ScrollViewer holding an ItemsView. `made` counts calls of the view function.
let private build (source: Item array) (layout: ItemsLayout) (scroller: Scroller) (made: int ref) =
    let t = ElementTree(textSystem ())
    t.Update(
        ScrollViewer.create [
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source source
                    ItemsView.key (fun (it: Item) -> it.Id)
                    ItemsView.view (fun (it: Item) ->
                        made.Value <- made.Value + 1
                        Border.create [ Border.name (string it.Id); Border.height it.Height; Border.background Color.White ])
                    ItemsView.layout layout
                    ItemsView.scroller scroller
                ])
        ])
    t.UpdateLayout(Size.Create(300.0, 200.0))
    let sv = t.Root.Value
    t, sv, sv.Children[0]

let private realized (e: Element) =
    match ItemsView.realizedCount e with
    | ValueSome(struct (r, _)) -> r
    | ValueNone -> failwith "not an ItemsView"

let private shown (iv: Element) = [ for c in iv.Children -> int c.Name ]

let private layoutAt (t: ElementTree) = t.UpdateLayout(Size.Create(300.0, 200.0))

[<Fact>]
let ``only the items near the viewport are realized`` () =
    let t, _, iv = build (items 10_000 (fun _ -> 20.0)) ItemsLayout.stack (Scroller()) (ref 0)
    // 200 visible plus 100 of overscan below: 15 rows of 20.
    Assert.InRange(realized iv, 10, 20)
    Assert.Equal(0, List.head (shown iv))
    Assert.Equal(200_000.0, iv.DesiredSize.Height)

[<Fact>]
let ``the same elements are realized for 100 and 10,000 items`` () =
    for offset in [ 0.0; 1000.0 ] do
        let counts =
            [ for n in [ 100; 10_000 ] ->
                  let t, sv, iv = build (items n variable) ItemsLayout.stack (Scroller()) (ref 0)
                  ScrollViewer.jumpTo sv offset
                  layoutAt t
                  realized iv ]
        Assert.Equal(counts[0], counts[1])

[<Fact>]
let ``a scroll step costs about the same for 100 and 10,000 items`` () =
    let measure (n: int) =
        let t, sv, _ = build (items n variable) ItemsLayout.stack (Scroller()) (ref 0)
        let mutable offset = 0.0
        let step () =
            // Down and back so both lists stay in their middle part (100 items are 4,000 high).
            offset <- (if offset >= 1500.0 then 500.0 else offset + 37.0)
            ScrollViewer.jumpTo sv offset
            layoutAt t
        for _ in 1..50 do step ()
        let best (f: unit -> unit) = [ for _ in 1..60 -> (let sw = Stopwatch.StartNew() in f (); sw.Elapsed.TotalMilliseconds) ] |> List.min
        best step, best (fun () -> t.Render(DrawList 1.0))
    let smallLayout, smallDraw = measure 100
    let bigLayout, bigDraw = measure 10_000
    // A small floor keeps timer noise on very fast steps from deciding it.
    Assert.True(bigLayout <= 1.5 * smallLayout + 0.02, $"layout: 10,000 items {bigLayout:F4} ms, 100 items {smallLayout:F4} ms")
    Assert.True(bigDraw <= 1.5 * smallDraw + 0.02, $"draw list: 10,000 items {bigDraw:F4} ms, 100 items {smallDraw:F4} ms")

[<Fact>]
let ``scrolling to item 9,000 realizes only what's around it`` () =
    let made = ref 0
    let scroller = Scroller()
    let t, sv, iv = build (items 10_000 variable) ItemsLayout.stack scroller made
    let before = made.Value
    scroller.ScrollTo(9000, ScrollAlign.Start)
    layoutAt t
    // Only a window around 9,000: never the 8,000 or so in between.
    Assert.True(made.Value - before < 60, $"{made.Value - before} views made")
    Assert.All(shown iv, fun i -> Assert.InRange(i, 8_900, 9_100))
    // It's at the viewport's top (exactly, even though the items above are only estimated).
    let item = iv.Children |> Seq.find (fun c -> c.Name = "9000")
    Assert.Equal(0.0, item.PositionInTree.Y, 3)
    Assert.Equal(9000, (scroller.FirstVisible.Value.Key :?> int))

[<Fact>]
let ``the first visible item stays put while estimated sizes above it get measured`` () =
    let scroller = Scroller()
    let t, _, iv = build (items 10_000 variable) ItemsLayout.stack scroller (ref 0)
    scroller.ScrollTo(5000, ScrollAlign.Start)
    layoutAt t
    let topOf (name: string) = (iv.Children |> Seq.find (fun c -> c.Name = name)).PositionInTree.Y
    let mutable previous = topOf "5000"
    // Scroll up in steps into items that were only estimated: 5,000 moves down exactly by each step
    // (6 steps: it stays inside the 100 DIPs of overscan below the viewport).
    for _ in 1..6 do
        scroller.ScrollBy(-30.0, false)
        layoutAt t
        let now = topOf "5000"
        Assert.Equal(previous + 30.0, now, 3)
        previous <- now

[<Fact>]
let ``scrolled-out elements are reused`` () =
    let t, sv, iv = build (items 10_000 (fun _ -> 20.0)) ItemsLayout.stack (Scroller()) (ref 0)
    let seen = HashSet<Element>(HashIdentity.Reference)
    for step in 1..100 do
        ScrollViewer.jumpTo sv (float step * 50.0)
        layoutAt t
        for c in iv.Children do
            seen.Add c |> ignore
    // Scrolling through 250 items used little more than one screen's elements.
    Assert.True(seen.Count < 3 * realized iv, $"{seen.Count} elements for {realized iv} realized")

[<Fact>]
let ``outside a ScrollViewer every item is realized`` () =
    let t = ElementTree(textSystem ())
    t.Update(
        StackPanel.create [
            StackPanel.children [
                ItemsView.create [
                    ItemsView.source (items 50 (fun _ -> 10.0))
                    ItemsView.key (fun (it: Item) -> it.Id)
                    ItemsView.view (fun (it: Item) -> Border.create [ Border.height it.Height ])
                ]
            ]
        ])
    t.UpdateLayout(Size.Create(300.0, 200.0))
    Assert.Equal(50, realized t.Root.Value.Children[0])

[<Fact>]
let ``wrap and grid lay items out in cells`` () =
    let _, _, wrap = build (items 1000 (fun _ -> 40.0)) (ItemsLayout.wrap 50.0 40.0) (Scroller()) (ref 0)
    // 288 wide (300 less the 12 gutter): 5 cells per row; item 7 is row 1, column 2.
    let seven = wrap.Children |> Seq.find (fun c -> c.Name = "7")
    Assert.Equal(100.0, seven.Bounds.X)
    Assert.Equal(40.0, seven.Bounds.Y)
    Assert.Equal(200.0 * 40.0, wrap.DesiredSize.Height)
    let _, _, grid = build (items 10 (fun _ -> 30.0)) (ItemsLayout.grid 4) (Scroller()) (ref 0)
    let five = grid.Children |> Seq.find (fun c -> c.Name = "5")
    Assert.Equal(72.0, five.Bounds.X)
    Assert.Equal(30.0, five.Bounds.Y)
    Assert.Equal(90.0, grid.DesiredSize.Height)

[<Fact>]
let ``ScrollTo with Nearest doesn't move what already shows`` () =
    let scroller = Scroller()
    let t, _, _ = build (items 1000 (fun _ -> 20.0)) ItemsLayout.stack scroller (ref 0)
    scroller.ScrollTo(3, ScrollAlign.Nearest, false)
    layoutAt t
    Assert.Equal(0.0, scroller.Offset)
    scroller.ScrollTo(20, ScrollAlign.Nearest, false)
    layoutAt t
    // Item 20 (400..420) at the bottom of a 200-high viewport.
    Assert.Equal(220.0, scroller.Offset)
    scroller.ScrollTo(100, ScrollAlign.Center, false)
    layoutAt t
    Assert.Equal(2000.0 + 10.0 - 100.0, scroller.Offset)

[<Fact>]
let ``a ScrollViewer keeps a gutter for its bar unless it overlays it`` () =
    let widthIn (space: ScrollBarSpace) =
        let t = ElementTree(textSystem ())
        t.Update(ScrollViewer.create [ ScrollViewer.scrollBarSpace space; ScrollViewer.content (Border.create [ Border.height 50.0 ]) ])
        t.UpdateLayout(Size.Create(300.0, 200.0))
        t.Root.Value.Children[0].Bounds.Width
    // Kept even though the content fits (so it never changes width when it starts to overflow).
    Assert.Equal(288.0, widthIn ScrollBarSpace.Reserved)
    Assert.Equal(300.0, widthIn ScrollBarSpace.Overlay)

[<Fact>]
let ``horizontal scrolling gives the content its own width`` () =
    let t = ElementTree(textSystem ())
    t.Update(
        ScrollViewer.create [
            ScrollViewer.horizontal ScrollBarMode.Auto
            ScrollViewer.content (Border.create [ Border.width 1000.0; Border.height 50.0 ])
        ])
    t.UpdateLayout(Size.Create(300.0, 200.0))
    let content = t.Root.Value.Children[0]
    Assert.Equal(1000.0, content.Bounds.Width)
