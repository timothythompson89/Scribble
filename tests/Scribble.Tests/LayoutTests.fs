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
