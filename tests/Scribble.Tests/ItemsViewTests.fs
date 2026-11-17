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

[<Timing.TimingFact; Trait("Category", "Timing")>]
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
let ``scrolling a list to an item moves the page around it only as far as needed`` () =
    // A 200-high page that scrolls: 60 of header, a 100-high list of 20-high rows, 300 below.
    let t = ElementTree(textSystem ())
    let list, page = Scroller(), Scroller()
    t.Update(
        ScrollViewer.create [
            ScrollViewer.scroller page
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [
                        Border.create [ Border.height 60.0 ]
                        ScrollViewer.create [
                            ScrollViewer.height 100.0
                            ScrollViewer.content (
                                ItemsView.create [
                                    ItemsView.source [| 0 .. 999 |]
                                    ItemsView.view (fun (i: int) -> Border.create [ Border.name (string i); Border.height 20.0 ])
                                    ItemsView.scroller list
                                ])
                        ]
                        Border.create [ Border.height 300.0 ]
                    ]
                ])
        ])
    let settle () =
        for _ in 1 .. 3 do
            layoutAt t
    settle ()
    // The list shows whole on the page: only the list moves, though the item goes to its top.
    list.ScrollTo(500, ScrollAlign.Start, false)
    settle ()
    Assert.Equal(10_000.0, list.Offset)
    Assert.Equal(0.0, page.Offset)
    // The page scrolled the list's top out of view: the page moves just enough to show the item
    // (at the list's top, 60 down the page), not to put it at the page's top.
    page.ScrollToOffset(150.0, false)
    settle ()
    list.ScrollTo(700, ScrollAlign.Start, false)
    settle ()
    Assert.Equal(14_000.0, list.Offset)
    Assert.Equal(60.0, page.Offset)

/// The Gallery's "Add message": an item added at the end of a list whose bottom is below the page's
/// viewport, then scrolled to (End). The whole item must show, in the list and on the page.
[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``an item added and scrolled to shows whole, on a page that has to move too`` (animate: bool) =
    // A 200-high page: a 16 margin and 150 of header, then a 100-high list (its bottom 66 below the page's viewport).
    let t = ElementTree(textSystem ())
    let list, page = Scroller(), Scroller()
    let view (n: int) =
        ScrollViewer.create [
            ScrollViewer.scroller page
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.margin (0.0, 16.0, 0.0, 24.0) // a page's margin: the page must count it (it once stopped 16 short)
                    StackPanel.children [
                        Border.create [ Border.height 150.0 ]
                        ScrollViewer.create [
                            ScrollViewer.height 100.0
                            ScrollViewer.content (
                                ItemsView.create [
                                    ItemsView.source [| 0 .. n - 1 |]
                                    ItemsView.view (fun (i: int) -> Border.create [ Border.name (string i); Border.height (if i = n - 1 then 40.0 else 20.0) ])
                                    ItemsView.scroller list
                                ])
                        ]
                        Border.create [ Border.height 300.0 ]
                    ]
                ])
        ]
    t.Update(view 30)
    let settle () =
        for _ in 1 .. 40 do
            t.Time <- t.Time + TimeSpan.FromMilliseconds 16.0
            layoutAt t
            t.RunAfterFrame()
    settle ()
    t.Update(view 31)
    list.ScrollTo(30, ScrollAlign.End, animate)
    settle ()
    let rec find (e: Element) = if e.Name = "30" then Some e else e.Children |> Seq.tryPick find
    let item = (find t.Root.Value).Value
    let p = item.PositionInTree
    // In the window (0..200) and in the list's viewport, all 40 DIPs of it.
    Assert.True(p.Y >= 0.0 && p.Y + 40.0 <= 200.0, $"the item is at {p.Y}..{p.Y + 40.0} in a 200-high window (page offset {page.Offset}, list offset {list.Offset})")
    Assert.Equal(40.0, item.VisibleBounds.Height, 3)

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

// --- Styles on realization (DESIGN.md, "Recycling measured; styles on realization; new sources") ---

/// A 300×200 tree: a reference Button (styled the normal way), then a ScrollViewer holding an
/// ItemsView of `n` rows made by `row`.
let private styledList (styles: Style list) (theme: Theme voption) (n: int) (row: int -> View) =
    let t = ElementTree(textSystem ())
    t.Styles <- styles
    t.Theme <- theme
    let scroller = Scroller()
    t.Update(
        StackPanel.create [
            StackPanel.children [
                Button.create [ Button.name "reference"; Button.text "Reference" ]
                ScrollViewer.create [
                    ScrollViewer.height 150.0
                    ScrollViewer.content (
                        ItemsView.create [
                            ItemsView.source [| 0 .. n - 1 |]
                            ItemsView.view row
                            ItemsView.scroller scroller
                        ])
                ]
            ]
        ])
    layoutAt t
    let sv = t.Root.Value.Children[1]
    t, scroller, t.Root.Value.Children[0], sv.Children[0]

[<Fact>]
let ``rows are styled before they're measured, in the layout that realizes them`` () =
    // A class style adds 10 of padding all round: every row is 20 + 2 × 10 high.
    let styles = [ Border.style [ Style.cls "row" ] [ Border.padding 10.0 ] ]
    let row (i: int) =
        Border.create [ Border.name (string i); Border.classes [ "row" ]; Border.child (Border.create [ Border.height 20.0 ]) ]
    let t, scroller, _, iv = styledList styles ValueNone 1_000 row
    for step in 1 .. 60 do
        // Steps of 37 (not a multiple of the row height): rows come in recycled and new.
        scroller.ScrollBy(37.0, false)
        layoutAt t
        for c in iv.Children do
            Assert.True(c.DesiredSize.Height = 40.0, $"step {step}: row {c.Name} measured {c.DesiredSize.Height} high")
        Assert.True(t.FirstQueuedRestyle.IsNone, $"step {step}: a restyle was left queued")

[<Fact>]
let ``a themed Button in a row that just scrolled in has its theme look in its first frame`` () =
    let row (i: int) =
        Border.create [ Border.name (string i); Border.child (Button.create [ Button.name ("button " + string i); Button.text "Reply" ]) ]
    let t, scroller, reference, iv = styledList [] (ValueSome(Theme.dark [])) 1_000 row
    let themed = reference.Get BorderProps.background
    Assert.NotEqual(BorderProps.background.Default, themed)
    for step in 1 .. 40 do
        scroller.ScrollBy(53.0, false)
        layoutAt t
        for c in iv.Children do
            let button = c.Children[0]
            Assert.True((button.Get BorderProps.background) = themed, $"step {step}: {button.Name} has no theme background")
        Assert.True(t.FirstQueuedRestyle.IsNone, $"step {step}: a restyle was left queued")

// --- New sources (DESIGN.md, "Recycling measured; styles on realization; new sources", decision 3) ---

let private sourceOf (a: int[]) = ItemsSource(a, (fun () -> a.Length), (fun i -> box a[i]), (fun () -> box a))

[<Fact>]
let ``the key store updated in place agrees with one built from scratch, through random edits`` () =
    let r = Random 11
    let keyFn = TypedKeyFunc<int, int>(fun x -> x * 7) :> KeyFunc
    let mutable store = keyFn.CreateStore()
    let mutable items = Array.init 50 id
    let mutable next = 50
    // Heights measured so far, by key (as ItemsView records them).
    let measured = Dictionary<int, float>()
    for step in 1 .. 400 do
        let n = items.Length
        items <-
            match r.Next 7 with
            | 0 -> Array.append items (Array.init (r.Next(1, 5)) (fun _ -> next <- next + 1; next))            // append
            | 1 -> Array.append (Array.init (r.Next(1, 5)) (fun _ -> next <- next + 1; next)) items            // insert at the front
            | 2 when n > 5 -> let i = r.Next n in Array.append items[.. i - 1] items[i + 1 ..]                  // remove one
            | 3 when n > 5 -> items[r.Next(1, 4) ..]                                                            // drop the first few
            | 4 when n > 5 -> items[.. n - 1 - r.Next(1, 4)]                                                    // drop the last few
            | 5 when n > 2 -> let a = Array.copy items in let i, j = r.Next n, r.Next n in (let t = a[i] in a[i] <- a[j]; a[j] <- t); a  // swap two
            | _ -> Array.copy items                                                                             // same keys, new array
        store <- store.Update(sourceOf items, keyFn)
        // Measure a few items, as scrolling would.
        for _ in 1 .. 3 do
            if items.Length > 0 then
                let i = r.Next items.Length
                let h = float (r.Next(10, 60))
                store.Record(i, h) |> ignore
                measured[items[i] * 7] <- h
        let fresh = keyFn.CreateStore().Update(sourceOf items, keyFn)
        for i in 0 .. items.Length - 1 do
            match measured.TryGetValue(items[i] * 7) with
            | true, h -> fresh.Record(i, h) |> ignore
            | _ -> ()
        Assert.Equal(items.Length, store.Count)
        for i in 0 .. items.Length - 1 do
            Assert.Equal(box (items[i] * 7), store.KeyAt i)
            Assert.True((store.IndexOf(box (items[i] * 7)) = ValueSome i), $"step {step}: index of item {i}")
            Assert.Equal(fresh.Height i, store.Height i)
        for i in 0 .. items.Length do
            Assert.True((store.Prefix i = fresh.Prefix i), $"step {step}: sums before {i}")
        // A key that's gone isn't found.
        Assert.True((store.IndexOf(box -7) = ValueNone))

[<Fact>]
let ``appending one item to 10,000 allocates little in the ItemsView`` () =
    let t = ElementTree(textSystem ())
    let scroller = Scroller()
    let view (source: Item[]) =
        ScrollViewer.create [
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source source
                    ItemsView.key (fun (it: Item) -> it.Id)
                    ItemsView.view (fun (it: Item) -> Border.create [ Border.height it.Height ])
                    ItemsView.scroller scroller
                ])
        ]
    let mutable source = items 10_000 variable
    t.Update(view source)
    layoutAt t
    scroller.ScrollToIndex(5_000, ScrollAlign.Start, false)
    layoutAt t
    let append () =
        // The app's new array is made before measuring: only Scribble's work counts.
        let next = Array.append source [| { Id = source.Length; Height = 30.0 } |]
        let v = view next
        source <- next
        let before = GC.GetAllocatedBytesForCurrentThread()
        t.Update v
        layoutAt t
        GC.GetAllocatedBytesForCurrentThread() - before
    // Warm up (first calls make caches and compile code), then the real measurement.
    for _ in 1 .. 3 do append () |> ignore
    let bytes = append ()
    Assert.True(bytes <= 64L * 1024L, $"{bytes / 1024L} KB allocated (at most 64 KB)")

// --- Sideways scrolling (M8 step 3a++) ------------------------------------------------------------

/// A 300×200 ScrollViewer that also scrolls sideways, holding an ItemsView of rows 20 high whose
/// content is `width i` wide (rows named by number).
let private buildSideways (source: int array) (width: int -> float) (layout: ItemsLayout) (scroller: Scroller) =
    let t = ElementTree(textSystem ())
    let view (src: int array) =
        ScrollViewer.create [
            ScrollViewer.horizontal ScrollBarMode.Auto
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source src
                    ItemsView.key id
                    ItemsView.view (fun (i: int) ->
                        Border.create [
                            Border.name (string i)
                            Border.height 20.0
                            Border.child (Border.create [ Border.width (width i); Border.horizontalAlignment HorizontalAlignment.Left ])
                        ])
                    ItemsView.layout layout
                    ItemsView.scroller scroller
                ])
        ]
    t.Update(view source)
    layoutAt t
    let sv = t.Root.Value
    t, sv, sv.Children[0], (fun (src: int array) -> t.Update(view src); layoutAt t)

[<Theory>]
[<InlineData(false)>]
[<InlineData(true)>]
let ``a stack scrolling sideways is as wide as its widest row, and every row spans that width`` (fixedHeights: bool) =
    let layout = if fixedHeights then ItemsLayout.fixedStack 20.0 else ItemsLayout.stack
    let _, sv, iv, _ = buildSideways [| 0..999 |] (fun i -> if i = 3 then 640.0 else 120.0) layout (Scroller())
    Assert.Equal(640.0, iv.Bounds.Width)
    for row in iv.Children do
        Assert.Equal(640.0, row.Bounds.Width)
    let struct (_, _, vw, _) = ScrollViewer.viewportOf sv
    Assert.True(640.0 > vw) // wider than the viewport: the bar shows

[<Fact>]
let ``the width doesn't shrink while scrolling, grows at a wider row, and shrinks when the widest leaves`` () =
    let width i = if i = 0 then 500.0 elif i = 400 then 900.0 else 100.0 + float (i % 7) * 10.0
    let scroller = Scroller()
    let t, sv, iv, update = buildSideways [| 0..999 |] width ItemsLayout.stack scroller
    Assert.Equal(500.0, iv.Bounds.Width)
    // Far down, with row 0 long released: still 500.
    ScrollViewer.jumpTo sv 4000.0
    layoutAt t
    Assert.DoesNotContain(0, shown iv)
    Assert.Equal(500.0, iv.Bounds.Width)
    // Row 400 comes into view: 900.
    ScrollViewer.jumpTo sv 7900.0
    layoutAt t
    Assert.Contains(400, shown iv)
    Assert.Equal(900.0, iv.Bounds.Width)
    // Row 400 leaves the source: back to the widest measured one still there (row 0's 500).
    update [| for i in 0..999 do if i <> 400 then yield i |]
    Assert.Equal(500.0, iv.Bounds.Width)
    // And row 0 too: the widest of the others measured so far.
    update [| for i in 1..999 do if i <> 400 then yield i |]
    Assert.InRange(iv.DesiredSize.Width, 100.0, 160.0) // arranged no narrower than the viewport

[<Fact>]
let ``wrap and grid layouts keep flowing to the viewport's width when the ScrollViewer scrolls sideways`` () =
    for layout in [ ItemsLayout.wrap 100.0 40.0; ItemsLayout.grid 3 ] do
        let _, sv, iv, _ = buildSideways [| 0..99 |] (fun _ -> 50.0) layout (Scroller())
        let struct (_, _, vw, _) = ScrollViewer.viewportOf sv
        Assert.True(iv.Bounds.Width <= vw + 0.5, $"{layout}: {iv.Bounds.Width} wide in a {vw} viewport")
        // More than one column.
        Assert.True(iv.Children |> Seq.exists (fun c -> c.Bounds.X > 0.0))
    // Wrap measures its height with the viewport's columns (it used to measure one column: twice as tall).
    let _, sv, iv, _ = buildSideways [| 0..99 |] (fun _ -> 50.0) (ItemsLayout.wrap 100.0 40.0) (Scroller())
    let struct (_, _, vw, _) = ScrollViewer.viewportOf sv
    let columns = int (Math.Floor(vw / 100.0))
    Assert.Equal(float ((100 + columns - 1) / columns) * 40.0, iv.DesiredSize.Height)

/// Scroll steps through a sideways scrolled stack of `n` items: the best step time and what's realized.
let private sidewaysSteps (n: int) =
    let t, sv, iv, _ = buildSideways [| 0 .. n - 1 |] (fun i -> 100.0 + float (i % 13) * 37.0) ItemsLayout.stack (Scroller())
    let mutable offset = 0.0
    let step () =
        offset <- (if offset >= 1500.0 then 500.0 else offset + 37.0)
        ScrollViewer.jumpTo sv offset
        layoutAt t
    for _ in 1..50 do step ()
    let best (f: unit -> unit) = [ for _ in 1..60 -> (let sw = Stopwatch.StartNew() in f (); sw.Elapsed.TotalMilliseconds) ] |> List.min
    best step, realized iv

[<Fact>]
let ``scrolling sideways realizes the same for 100 and 10,000 items`` () =
    let _, smallRealized = sidewaysSteps 100
    let _, bigRealized = sidewaysSteps 10_000
    Assert.Equal(smallRealized, bigRealized)

[<Timing.TimingFact; Trait("Category", "Timing")>]
let ``scrolling sideways keeps a scroll step's cost the same for 100 and 10,000 items`` () =
    let smallLayout, _ = sidewaysSteps 100
    let bigLayout, _ = sidewaysSteps 10_000
    Assert.True(bigLayout <= 1.5 * smallLayout + 0.02, $"layout: 10,000 items {bigLayout:F4} ms, 100 items {smallLayout:F4} ms")

[<Fact>]
let ``scrolling to an item or an offset leaves a sideways scrolled stack where it is`` () =
    for layout in [ ItemsLayout.stack; ItemsLayout.fixedStack 20.0 ] do
        let scroller = Scroller()
        let t, sv, iv, _ = buildSideways [| 0..999 |] (fun _ -> 900.0) layout scroller
        iv.BringRectIntoView(Rect.Create(500.0, 0.0, 1.0, 1.0), ScrollAlign.Start, false)
        layoutAt t
        let struct (x0, _, _, _) = ScrollViewer.viewportOf sv
        Assert.Equal(500.0, x0)
        scroller.ScrollTo(box 600, ScrollAlign.Start, false)
        layoutAt t
        let struct (x, y, _, _) = ScrollViewer.viewportOf sv
        Assert.Equal(500.0, x) // was NaN: the slot of an unlimited-width stack is infinitely wide
        Assert.Equal(12_000.0, y, 3)
        scroller.ScrollToOffset(40.0, false)
        layoutAt t
        let struct (x, y, _, _) = ScrollViewer.viewportOf sv
        Assert.Equal(500.0, x) // was 0
        Assert.Equal(40.0, y, 3)
