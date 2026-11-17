module Scribble.Tests.LayoutTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping

// A test element: wants a fixed size and counts how often it's measured (in a local property).
module BoxProps =
    let size = Prop.define "Size" Size.Zero Affects.Measure
    let measures = Prop.local "Measures" 0

[<AbstractClass>]
type Box =
    inherit Layoutable
    static member size<'k when 'k :> Box>(width: float, height: float) : Setting<'k> = Setting.create BoxProps.size (Size.Create(width, height))

module Box =
    let Type =
        Element.define "Box" [
            Element.measure (fun ctx _ ->
                ctx.SetLocal(BoxProps.measures, ctx.Get BoxProps.measures + 1)
                ctx.Get BoxProps.size)
        ]
    let create (settings: Setting<Box> list) = View.Create(Type, settings)

let box w h (settings: Setting<Box> list) = Box.create (Box.size (w, h) :: settings)
let measures (e: Element) = e.Get BoxProps.measures
let topLeft<'k when 'k :> Layoutable> : Setting<'k> list =
    [ Layoutable.horizontalAlignment HorizontalAlignment.Left; Layoutable.verticalAlignment VerticalAlignment.Top ]

/// Builds a view into a tree and lays it out; returns the tree and its root element.
let run (view: View) (w: float) (h: float) =
    let t = ElementTree(textSystem ())
    t.Update view
    t.UpdateLayout(Size.Create(w, h))
    t, t.Root.Value

/// A child by position: `child root [ 0; 2 ]` is the third child of the first child.
let rec child (e: Element) (path: int list) =
    match path with
    | [] -> e
    | i :: rest -> child e.Children[i] rest

let private rect (x, y, w, h) = Rect.Create(x, y, w, h)

[<Fact>]
let ``a stretched element fills its slot minus margin`` () =
    let _, b = run (box 10.0 10.0 [ Box.margin 5.0 ]) 100.0 50.0
    Assert.Equal(rect (5.0, 5.0, 90.0, 40.0), b.Bounds)
    Assert.Equal(Size.Create(20.0, 20.0), b.DesiredSize)

[<Fact>]
let ``alignment positions an element at its desired size`` () =
    let _, b = run (box 20.0 10.0 [ Box.horizontalAlignment HorizontalAlignment.Right; Box.verticalAlignment VerticalAlignment.Center ]) 100.0 50.0
    Assert.Equal(rect (80.0, 20.0, 20.0, 10.0), b.Bounds)

[<Fact>]
let ``explicit size wins over content, min and max clamp it`` () =
    let _, b = run (box 20.0 10.0 (topLeft @ [ Box.width 50.0; Box.height (Px 30.0); Box.maxHeight 25.0 ])) 100.0 100.0
    Assert.Equal(rect (0.0, 0.0, 50.0, 25.0), b.Bounds)
    let _, c = run (box 20.0 10.0 [ Box.minWidth 40.0; Box.horizontalAlignment HorizontalAlignment.Left ]) 100.0 100.0
    Assert.Equal(40.0, c.Bounds.Width)

[<Fact>]
let ``a vertical stack places children one after another with spacing`` () =
    let _, s = run (StackPanel.create (topLeft @ [ StackPanel.spacing 4.0; StackPanel.children [ box 30.0 10.0 []; box 50.0 20.0 [] ] ])) 200.0 200.0
    Assert.Equal(Size.Create(50.0, 34.0), s.DesiredSize)
    Assert.Equal(rect (0.0, 0.0, 50.0, 10.0), (child s [ 0 ]).Bounds)
    Assert.Equal(rect (0.0, 14.0, 50.0, 20.0), (child s [ 1 ]).Bounds)

[<Fact>]
let ``a horizontal stack skips hidden children and their spacing`` () =
    let view =
        StackPanel.create (
            topLeft
            @ [ StackPanel.orientation Horizontal
                StackPanel.spacing 5.0
                StackPanel.children [ box 30.0 10.0 []; box 99.0 99.0 [ Box.isVisible false ]; box 20.0 20.0 [] ] ])
    let _, s = run view 200.0 200.0
    Assert.Equal(Size.Create(55.0, 20.0), s.DesiredSize)
    Assert.Equal(35.0, (child s [ 2 ]).Bounds.X)

[<Fact>]
let ``a border adds padding and border thickness around its child`` () =
    let _, b = run (Border.create (topLeft @ [ Border.child (box 40.0 20.0 []); Border.padding (10.0, 5.0); Border.borderThickness 2.0 ])) 200.0 200.0
    Assert.Equal(Size.Create(64.0, 34.0), b.DesiredSize)
    Assert.Equal(rect (12.0, 7.0, 40.0, 20.0), (child b [ 0 ]).Bounds)

[<Fact>]
let ``layout rounds edges to physical pixels`` () =
    // Three equal children in 100 DIPs at 150%: edges land on whole pixels with no gaps.
    let t = ElementTree(textSystem ())
    t.Scale <- 1.5
    t.Update(StackPanel.create [ StackPanel.orientation Horizontal; StackPanel.children [ for _ in 1..3 -> box 33.4 10.0 [] ] ])
    t.UpdateLayout(Size.Create(100.0, 10.0))
    let boxes = t.Root.Value.Children
    for b in boxes do
        Assert.Equal(Math.Round(b.Bounds.X * 1.5), b.Bounds.X * 1.5, 9)
        Assert.Equal(Math.Round(b.Bounds.Right * 1.5), b.Bounds.Right * 1.5, 9)
    Assert.Equal(boxes[0].Bounds.Right, boxes[1].Bounds.X, 9)

[<Fact>]
let ``text blocks measure their text and wrap in the available width`` () =
    let _, t = run (TextBlock.create (topLeft @ [ TextBlock.text "hello world"; TextBlock.fontSize 20.0 ])) 1000.0 1000.0
    Assert.Equal(110.0, t.Bounds.Width)
    let _, w = run (TextBlock.create (topLeft @ [ TextBlock.text "hello world"; TextBlock.fontSize 20.0; TextBlock.textWrapping Scribble.Text.Wrap ])) 80.0 1000.0
    Assert.Equal(2, (w.Get TextProps.layout).Value.Lines.Length)
    Assert.Equal(50.0, w.Bounds.Width)

[<Fact>]
let ``changing a property re-measures only the path to the root`` () =
    let leaves = [ for _ in 1..10 -> box 10.0 10.0 [] ]
    let view =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.children [
                StackPanel.create [ StackPanel.children (List.take 5 leaves) ]
                StackPanel.create [ StackPanel.children (List.skip 5 leaves) ]
            ]
        ]
    let t, root = run view 500.0 500.0
    let left, right = child root [ 0 ], child root [ 1 ]
    let leaf i = if i < 5 then child left [ i ] else child right [ i - 5 ]
    for i in 0..9 do Assert.Equal(1, measures (leaf i))
    // Nothing changed: no work at all.
    t.UpdateLayout(Size.Create(500.0, 500.0))
    for i in 0..9 do Assert.Equal(1, measures (leaf i))
    // One leaf changes: it's measured again, its siblings and the other branch are not.
    (leaf 2).Set(Box.margin 1.0)
    Assert.False left.IsMeasureValid
    Assert.True right.IsMeasureValid
    t.UpdateLayout(Size.Create(500.0, 500.0))
    Assert.Equal(2, measures (leaf 2))
    Assert.Equal(1, measures (leaf 3))
    Assert.Equal(1, measures (leaf 7))

[<Fact>]
let ``changes ask the tree for a frame, unchanged values don't`` () =
    let t, b = run (box 10.0 10.0 []) 100.0 100.0
    let mutable requests = 0
    t.OnInvalidated <- fun () -> requests <- requests + 1
    b.Set(Box.margin 2.0)
    Assert.Equal(1, requests)
    b.Set(Box.margin 2.0)
    Assert.Equal(1, requests)
    // A render-only property asks for a frame without invalidating layout.
    t.UpdateLayout(Size.Create(100.0, 100.0))
    b.Set(Box.clipToBounds true)
    Assert.Equal(2, requests)
    Assert.True b.IsMeasureValid

[<Fact>]
let ``hit testing finds the topmost element under a point`` () =
    let view = Border.create [ Border.name "border"; Border.child (box 20.0 20.0 (topLeft @ [ Box.name "inner" ])); Border.padding 10.0 ]
    let t, _ = run view 100.0 100.0
    Assert.Equal("inner", (t.HitTest(15.0, 15.0)).Value.Name)
    Assert.Equal("border", (t.HitTest(50.0, 50.0)).Value.Name)
    Assert.True((t.HitTest(150.0, 50.0)).IsNone)

[<Fact>]
let ``rendering translates children and draws only visible elements`` () =
    let row visible = Border.create [ Border.height 10.0; Border.background Color.White; Border.isVisible visible ]
    let t, _ = run (StackPanel.create [ StackPanel.children [ row true; row false; row true ] ]) 50.0 50.0
    let list = DrawList(1.0)
    t.Render list
    Assert.Equal(2, list.Count)

[<Fact>]
let ``10,000 elements lay out quickly, and a single change is cheap`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                for _ in 1..100 ->
                    StackPanel.create [ StackPanel.orientation Horizontal; StackPanel.children [ for _ in 1..99 -> box 8.0 8.0 [] ] ]
            ]
        ]
    let t = ElementTree(textSystem ())
    t.Update view
    let rows = t.Root.Value
    let cell i = child rows [ i / 99; i % 99 ]
    let size = Size.Create(1000.0, 1000.0)
    // Steady state: .NET first runs quickly-compiled code and swaps in optimized code after a
    // few calls, so warm up, then take the best of several runs.
    let best (f: unit -> unit) =
        let mutable b = Double.MaxValue
        for _ in 1..15 do
            let sw = Diagnostics.Stopwatch.StartNew()
            f ()
            b <- min b sw.Elapsed.TotalMilliseconds
        b
    for _ in 1..30 do
        rows.InvalidateAll()
        t.UpdateLayout size
    let before = measures (cell 0)
    let full = best (fun () -> rows.InvalidateAll(); t.UpdateLayout size)
    let mutable w = 9.0
    let single = best (fun () -> w <- w + 1.0; (cell 5000).Set(Box.width w); t.UpdateLayout size)
    // Generous bounds (debug builds, slow CI machines); the README reports typical numbers.
    Assert.True(full < 25.0, $"full layout took {full:F2} ms")
    Assert.True(single < 1.0, $"incremental layout took {single:F3} ms")
    // Each full pass measures every cell once; the single changes measured only the changed cell.
    Assert.Equal(before + 15, measures (cell 0))
    Assert.Equal(before + 30, measures (cell 5000))
    Console.WriteLine($"10k elements: full {full:F2} ms, one change {single:F3} ms")

/// Counts its arrange and render stages.
let mutable private arranged = 0
let mutable private rendered = 0
let private countingType =
    Element.define "Counting" [
        Element.arrange (fun _ _ -> arranged <- arranged + 1)
        Element.render (fun _ _ -> rendered <- rendered + 1)
    ]
let private counting (settings: Setting<Layoutable> list) = View.Create(countingType, settings)

[<Fact>]
let ``an element that only moves isn't arranged again; one that changes size is`` () =
    let tree = ElementTree(textSystem ())
    let view (above: float) (width: float) =
        StackPanel.create [
            StackPanel.children [
                Border.create [ Border.height above ]
                counting [ Layoutable.width width; Layoutable.height 20.0; Layoutable.horizontalAlignment HorizontalAlignment.Left ]
            ]
        ]
    tree.Update(view 10.0 50.0)
    tree.UpdateLayout(Size.Create(200.0, 200.0))
    let element = tree.Root.Value.Children[1]
    arranged <- 0
    // Moved down by 30: new bounds, but its arrange stage (and so its children) isn't run again.
    tree.Update(view 40.0 50.0)
    tree.UpdateLayout(Size.Create(200.0, 200.0))
    Assert.Equal(40.0, element.Bounds.Y)
    Assert.Equal(0, arranged)
    // Wider: arranged again.
    tree.Update(view 40.0 80.0)
    tree.UpdateLayout(Size.Create(200.0, 200.0))
    Assert.Equal(1, arranged)

[<Fact>]
let ``an element off screen is skipped unless it's scaled up past its bounds`` () =
    let tree = ElementTree(textSystem ())
    let view scale =
        ScrollViewer.create [
            ScrollViewer.height 100.0
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [
                        Border.create [ Border.height 200.0 ]
                        // 100 DIPs below the clip: further than the 64 DIPs elements may draw outside themselves.
                        counting [ Layoutable.height 100.0; Layoutable.scale scale ]
                    ]
                ]
            )
        ]
    for scale, drawn in [ 1.0, 0; 0.97, 0; 4.0, 1 ] do
        tree.Update(view scale)
        tree.UpdateLayout(Size.Create(200.0, 300.0))
        rendered <- 0
        tree.Render(DrawList 1.0)
        Assert.Equal(drawn, rendered)

// --- VisibleBounds: the part of an element its ancestors' clips let show ---------------------------

/// A ScrollViewer `height` tall over rows of 80 (named "0", "1", ...), scrolled to `offset`.
let private scrolledRows (height: float) (rows: int) (offset: float) (above: View list) =
    let scroller = Scroller()
    let view =
        StackPanel.create [
            StackPanel.children [
                yield! above
                ScrollViewer.create [
                    ScrollViewer.height height
                    ScrollViewer.scroller scroller
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.children [ for i in 0 .. rows - 1 -> Border.create [ Border.name (string i); Border.height 80.0 ] ]
                        ]
                    )
                ]
            ]
        ]
    let tree, root = run view 200.0 400.0
    scroller.ScrollToOffset(offset, false)
    tree.UpdateLayout(Size.Create(200.0, 400.0))
    tree, root

let rec private find (name: string) (e: Element) : Element voption =
    if e.Name = name then ValueSome e
    else e.Children |> Seq.tryPick (fun c -> match find name c with ValueSome f -> Some f | ValueNone -> None) |> Option.toValueOption

[<Fact>]
let ``VisibleBounds is the whole element when nothing clips it`` () =
    let _, b = run (box 10.0 10.0 [ Box.margin 5.0 ]) 100.0 50.0
    Assert.Equal(rect (5.0, 5.0, 90.0, 40.0), b.VisibleBounds)

[<Fact>]
let ``VisibleBounds cuts an element partly scrolled out and is empty for one scrolled out entirely`` () =
    let _, root = scrolledRows 100.0 5 50.0 []
    let row n = (find (string n) root).Value
    // Row 0 is at -50..30: only its bottom 30 shows.
    let v0 = row 0
    Assert.Equal(-50.0, v0.PositionInTree.Y)
    Assert.Equal(0.0, v0.VisibleBounds.Y)
    Assert.Equal(30.0, v0.VisibleBounds.Height)
    // Row 1 at 30..110: cut at the viewport's bottom (100).
    Assert.Equal(30.0, (row 1).VisibleBounds.Y)
    Assert.Equal(70.0, (row 1).VisibleBounds.Height)
    // Row 2 at 110..190: below the viewport.
    Assert.True((row 2).VisibleBounds.IsEmpty)
    Assert.True((row 2).VisibleBounds.Width = 0.0 && (row 2).VisibleBounds.Height = 0.0)

[<Fact>]
let ``VisibleBounds applies every clipping ancestor (nested ScrollViewers)`` () =
    // An outer ScrollViewer 100 tall holds a 40-tall header, then an inner one 100 tall (40..140):
    // the outer viewport cuts the inner one's bottom at 100.
    let inner = Scroller()
    let view =
        ScrollViewer.create [
            ScrollViewer.height 100.0
            ScrollViewer.verticalAlignment VerticalAlignment.Top   // else it's centred in the 400-tall window
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [
                        Border.create [ Border.height 40.0 ]
                        ScrollViewer.create [
                            ScrollViewer.height 100.0
                            ScrollViewer.scroller inner
                            ScrollViewer.content (
                                StackPanel.create [
                                    StackPanel.children [ for i in 0 .. 3 -> Border.create [ Border.name (string i); Border.height 80.0 ] ]
                                ]
                            )
                        ]
                    ]
                ]
            )
        ]
    let _, root = run view 200.0 400.0
    let row0 = (find "0" root).Value
    // Row 0 at 40..120: the outer viewport ends at 100.
    Assert.Equal(40.0, row0.VisibleBounds.Y)
    Assert.Equal(60.0, row0.VisibleBounds.Height)
    // Row 1 at 120..200: inside the inner viewport (to 140) but below the outer one.
    Assert.True((find "1" root).Value.VisibleBounds.IsEmpty)

[<Fact>]
let ``VisibleBounds is empty under a hidden ancestor`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                Border.create [ Border.isVisible false; Border.child (Border.create [ Border.name "inside"; Border.height 20.0 ]) ]
            ]
        ]
    let _, root = run view 200.0 100.0
    Assert.True((find "inside" root).Value.VisibleBounds.IsEmpty)

[<Fact>]
let ``VisiblePart cuts any rect in an element's own coordinates`` () =
    let _, root = scrolledRows 100.0 5 50.0 [ Border.create [ Border.height 20.0 ] ]
    // The viewport is 20..120; row 1 is at 50..130 (80 down, scrolled up 50, under the 20-tall header).
    let row1 = (find "1" root).Value
    Assert.Equal(50.0, row1.PositionInTree.Y)
    // A band 10 above it (its margin, say) shows whole; one past its bottom is cut at 120.
    let above = row1.VisiblePart(Rect.Create(0.0, -10.0, 50.0, 10.0))
    Assert.Equal(40.0, above.Y)
    Assert.Equal(10.0, above.Height)
    let below = row1.VisiblePart(Rect.Create(0.0, 60.0, 50.0, 30.0))
    Assert.Equal(110.0, below.Y)
    Assert.Equal(10.0, below.Height)
