/// M8 step 2: Grid, DockPanel, Canvas, WrapPanel, Separator, zIndex and attached settings.
module Scribble.Tests.PanelTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping
open Scribble.Tests.LayoutTests

let private bounds (e: Element) = let b = e.Bounds in (b.X, b.Y, b.Width, b.Height)
let private grid (settings: Setting<Grid> list) (children: View list) = Grid.create (Grid.children children :: settings)

// --- Grid ------------------------------------------------------------------------------------

[<Fact>]
let ``fixed columns come first, stars share the rest by weight`` () =
    let _, g = run (grid [ Grid.columns "100, *, 2*" ] [ box 1.0 1.0 []; box 1.0 1.0 [ Grid.column 1 ]; box 1.0 1.0 [ Grid.column 2 ] ]) 400.0 50.0
    Assert.Equal((0.0, 0.0, 100.0, 50.0), bounds g.Children[0])
    Assert.Equal((100.0, 0.0, 100.0, 50.0), bounds g.Children[1])
    Assert.Equal((200.0, 0.0, 200.0, 50.0), bounds g.Children[2])

[<Fact>]
let ``an auto column is as wide as its widest child`` () =
    let view =
        grid [ Grid.columns "auto, *"; Grid.rows "auto, auto" ] [
            box 60.0 20.0 [ Grid.row 0 ]
            box 90.0 20.0 [ Grid.row 1 ]
            box 10.0 10.0 [ Grid.cell (0, 1) ]
        ]
    let _, g = run view 400.0 100.0
    Assert.Equal(90.0, g.Children[1].Bounds.Width)
    Assert.Equal((90.0, 0.0, 310.0, 20.0), bounds g.Children[2])

[<Fact>]
let ``a star column held at its minimum leaves the rest to the other stars`` () =
    let view =
        grid [ Grid.columns [ GridColumn.create [ GridColumn.minWidth 150.0 ]; GridColumn.create [] ] ] [
            box 1.0 1.0 []
            box 1.0 1.0 [ Grid.column 1 ]
        ]
    let _, g = run view 200.0 50.0
    Assert.Equal(150.0, g.Children[0].Bounds.Width)
    Assert.Equal((150.0, 0.0, 50.0, 50.0), bounds g.Children[1])

[<Fact>]
let ``a star column held at its maximum gives the extra to the other stars`` () =
    let view =
        grid [ Grid.columns [ GridColumn.create [ GridColumn.maxWidth 50.0 ]; GridColumn.create [ GridColumn.width "*" ] ] ] [
            box 1.0 1.0 []
            box 1.0 1.0 [ Grid.column 1 ]
        ]
    let _, g = run view 300.0 50.0
    Assert.Equal(50.0, g.Children[0].Bounds.Width)
    Assert.Equal(250.0, g.Children[1].Bounds.Width)

[<Fact>]
let ``limits apply to fixed columns too`` () =
    let view = grid [ Grid.columns [ GridColumn.create [ GridColumn.width 200.0; GridColumn.minWidth 250.0 ]; GridColumn.create [] ] ] [ box 1.0 1.0 [] ]
    let _, g = run view 400.0 50.0
    Assert.Equal(250.0, g.Children[0].Bounds.Width)

[<Fact>]
let ``spans cover their tracks and the gaps between them`` () =
    let view =
        grid [ Grid.columns "100, 100, 100"; Grid.rows "40, 40"; Grid.columnSpacing 10.0; Grid.rowSpacing 6.0 ] [
            box 1.0 1.0 [ Grid.columnSpan 2 ]
            box 1.0 1.0 [ Grid.cell (1, 1); Grid.columnSpan 2 ]
            box 1.0 1.0 [ Grid.column 2; Grid.rowSpan 2 ]
        ]
    let _, g = run view 320.0 86.0
    Assert.Equal((0.0, 0.0, 210.0, 40.0), bounds g.Children[0])
    Assert.Equal((110.0, 46.0, 210.0, 40.0), bounds g.Children[1])
    Assert.Equal((220.0, 0.0, 100.0, 86.0), bounds g.Children[2])

[<Fact>]
let ``in unlimited space stars act as auto`` () =
    // A vertical StackPanel gives its children unlimited height.
    let view =
        StackPanel.create [
            StackPanel.children [ grid [ Grid.rows "*, *" ] [ box 10.0 30.0 []; box 10.0 70.0 [ Grid.row 1 ] ] ]
        ]
    let _, root = run view 200.0 500.0
    let g = root.Children[0]
    Assert.Equal(100.0, g.Bounds.Height)
    Assert.Equal((0.0, 30.0, 200.0, 70.0), bounds g.Children[1])

[<Fact>]
let ``a percentage is a share of the grid's own size`` () =
    let _, g = run (grid [ Grid.columns "25%, *" ] [ box 1.0 1.0 []; box 1.0 1.0 [ Grid.column 1 ] ]) 400.0 50.0
    Assert.Equal(100.0, g.Children[0].Bounds.Width)
    Assert.Equal(300.0, g.Children[1].Bounds.Width)

[<Fact>]
let ``a grid that isn't stretched is as wide as its content needs`` () =
    let view = grid [ Grid.columns "auto, *"; Grid.horizontalAlignment HorizontalAlignment.Left ] [ box 40.0 10.0 []; box 70.0 10.0 [ Grid.column 1 ] ]
    let _, g = run view 400.0 50.0
    Assert.Equal(110.0, g.Bounds.Width)

[<Fact>]
let ``with no definitions children share one cell`` () =
    let _, g = run (grid [] [ box 10.0 10.0 []; box 20.0 20.0 [] ]) 100.0 60.0
    Assert.Equal((0.0, 0.0, 100.0, 60.0), bounds g.Children[0])
    Assert.Equal((0.0, 0.0, 100.0, 60.0), bounds g.Children[1])

[<Fact>]
let ``a place outside the grid is clamped into it`` () =
    let _, g = run (grid [ Grid.columns "50, 50" ] [ box 1.0 1.0 [ Grid.column 7; Grid.columnSpan 3 ] ]) 100.0 50.0
    Assert.Equal((50.0, 0.0, 50.0, 50.0), bounds g.Children[0])

[<Theory>]
[<InlineData(1.25)>]
[<InlineData(1.5)>]
[<InlineData(1.75)>]
let ``cells tile with no gaps or overlaps at fractional scales`` (scale: float) =
    let t = ElementTree(textSystem (), Scale = scale)
    t.Update(grid [ Grid.columns "*, *, *, *, *, *, *"; Grid.rows "*, *, *" ] [ for r in 0..2 do for c in 0..6 -> box 1.0 1.0 [ Grid.cell (r, c) ] ])
    t.UpdateLayout(Size.Create(401.3, 99.7))
    let g = t.Root.Value
    let cell r c = g.Children[r * 7 + c].Bounds
    let onPixel (v: float) = Assert.True(abs (v * scale - Math.Round(v * scale)) < 1e-6, $"{v} isn't on a pixel at {scale}")
    for r in 0..2 do
        for c in 0..6 do
            let b = cell r c
            onPixel b.Left
            onPixel b.Right
            if c < 6 then Assert.Equal(b.Right, (cell r (c + 1)).Left, 9)
            if r < 2 then Assert.Equal(b.Bottom, (cell (r + 1) c).Top, 9)
    Assert.Equal(g.Bounds.Width, (cell 0 6).Right, 9)
    Assert.Equal(g.Bounds.Height, (cell 2 0).Bottom, 9)

[<Fact>]
let ``column strings are checked`` () =
    let error = Assert.Throws<ArgumentException>(fun () -> Grid.columns<Grid> "auto, wide" |> ignore)
    Assert.Contains("'wide'", error.Message)

[<Fact>]
let ``changing a child's attached setting lays the grid out again`` () =
    let t, g = run (grid [ Grid.columns "50, 50" ] [ box 1.0 1.0 [] ]) 100.0 50.0
    t.Update(grid [ Grid.columns "50, 50" ] [ box 1.0 1.0 [ Grid.column 1 ] ])
    t.UpdateLayout(Size.Create(100.0, 50.0))
    Assert.Equal(50.0, g.Children[0].Bounds.X)

// --- DockPanel -------------------------------------------------------------------------------

[<Fact>]
let ``docked children take strips in order and the last fills`` () =
    let view =
        DockPanel.create [
            DockPanel.children [
                box 1.0 20.0 [ DockPanel.dock Dock.Top ]
                box 1.0 10.0 [ DockPanel.dock Dock.Bottom ]
                box 30.0 1.0 [ DockPanel.dock Dock.Left ]
                box 25.0 1.0 [ DockPanel.dock Dock.Right ]
                box 1.0 1.0 []
            ]
        ]
    let _, d = run view 200.0 100.0
    Assert.Equal((0.0, 0.0, 200.0, 20.0), bounds d.Children[0])
    Assert.Equal((0.0, 90.0, 200.0, 10.0), bounds d.Children[1])
    Assert.Equal((0.0, 20.0, 30.0, 70.0), bounds d.Children[2])
    Assert.Equal((175.0, 20.0, 25.0, 70.0), bounds d.Children[3])
    Assert.Equal((30.0, 20.0, 145.0, 70.0), bounds d.Children[4])

[<Fact>]
let ``without lastChildFill the last child docks too, and spacing goes between`` () =
    let view =
        DockPanel.create [
            DockPanel.lastChildFill false
            DockPanel.spacing 5.0
            DockPanel.children [ box 30.0 1.0 []; box 40.0 1.0 [] ]
        ]
    let _, d = run view 200.0 50.0
    Assert.Equal((0.0, 0.0, 30.0, 50.0), bounds d.Children[0])
    Assert.Equal((35.0, 0.0, 40.0, 50.0), bounds d.Children[1])

[<Fact>]
let ``a dock panel's natural size adds its strips`` () =
    let view =
        DockPanel.create [
            DockPanel.horizontalAlignment HorizontalAlignment.Left
            DockPanel.verticalAlignment VerticalAlignment.Top
            DockPanel.children [ box 1.0 20.0 [ DockPanel.dock Dock.Top ]; box 30.0 40.0 [ DockPanel.dock Dock.Left ]; box 50.0 10.0 [] ]
        ]
    let _, d = run view 400.0 400.0
    Assert.Equal((0.0, 0.0, 80.0, 60.0), bounds d)

// --- Canvas ----------------------------------------------------------------------------------

[<Fact>]
let ``canvas children sit at their positions, from either edge`` () =
    let view =
        Canvas.create [
            Canvas.children [
                box 20.0 10.0 [ Canvas.left 40.0; Canvas.top 15.0 ]
                box 20.0 10.0 [ Canvas.right 5.0; Canvas.bottom 5.0 ]
                box 20.0 10.0 []
            ]
        ]
    let _, c = run view 200.0 100.0
    Assert.Equal((40.0, 15.0, 20.0, 10.0), bounds c.Children[0])
    Assert.Equal((175.0, 85.0, 20.0, 10.0), bounds c.Children[1])
    Assert.Equal((0.0, 0.0, 20.0, 10.0), bounds c.Children[2])

[<Fact>]
let ``a canvas is as big as its children reach`` () =
    let view =
        Canvas.create [
            Canvas.horizontalAlignment HorizontalAlignment.Left
            Canvas.verticalAlignment VerticalAlignment.Top
            Canvas.children [ box 20.0 10.0 [ Canvas.left 300.0; Canvas.top 15.0 ]; box 50.0 80.0 [] ]
        ]
    let _, c = run view 1000.0 1000.0
    Assert.Equal(320.0, c.Bounds.Width)
    Assert.Equal(80.0, c.Bounds.Height)

// --- zIndex ----------------------------------------------------------------------------------

[<Fact>]
let ``zIndex orders drawing and hit testing; equal values keep view order`` () =
    let view (z: int) =
        grid [] [
            box 1.0 1.0 [ Box.name "a"; Box.zIndex z ]
            box 1.0 1.0 [ Box.name "b" ]
            box 1.0 1.0 [ Box.name "c" ]
        ]
    let t, g = run (view 0) 100.0 100.0
    Assert.Equal("c", t.HitTest(10.0, 10.0).Value.Name)
    Assert.True(obj.ReferenceEquals(g.Children, g.DrawOrder))
    t.Update(view 1)
    t.UpdateLayout(Size.Create(100.0, 100.0))
    Assert.Equal("a", t.HitTest(10.0, 10.0).Value.Name)
    Assert.Equal<string list>([ "b"; "c"; "a" ], [ for e in g.DrawOrder -> e.Name ])
    // Focus and Children keep the view's order.
    Assert.Equal<string list>([ "a"; "b"; "c" ], [ for e in g.Children -> e.Name ])
    t.Update(view -1)
    t.UpdateLayout(Size.Create(100.0, 100.0))
    Assert.Equal<string list>([ "a"; "b"; "c" ], [ for e in g.DrawOrder -> e.Name ])
    Assert.Equal("c", t.HitTest(10.0, 10.0).Value.Name)

// --- WrapPanel -------------------------------------------------------------------------------

[<Fact>]
let ``breakAfter starts a new row, and hidden children take no room`` () =
    let view =
        WrapPanel.create [
            WrapPanel.spacing 10.0
            WrapPanel.verticalAlignment VerticalAlignment.Top
            WrapPanel.lineSpacing 4.0
            WrapPanel.children [
                box 50.0 20.0 [ WrapPanel.breakAfter true ]
                box 50.0 20.0 [ Box.isVisible false ]
                box 50.0 20.0 []
                box 50.0 30.0 []
            ]
        ]
    let _, w = run view 400.0 200.0
    Assert.Equal((0.0, 0.0, 50.0, 20.0), bounds w.Children[0])
    Assert.Equal((0.0, 24.0, 50.0, 30.0), bounds w.Children[2])
    Assert.Equal((60.0, 24.0, 50.0, 30.0), bounds w.Children[3])

// --- Separator and attached settings ---------------------------------------------------------

[<Fact>]
let ``a separator is a line across, or down`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                Separator.create [ Separator.thickness 2.0 ]
                Separator.create [ Separator.orientation Vertical; Separator.height 30.0; Separator.horizontalAlignment HorizontalAlignment.Left ]
            ]
        ]
    let _, s = run view 200.0 100.0
    Assert.Equal((0.0, 0.0, 200.0, 2.0), bounds s.Children[0])
    Assert.Equal((0.0, 2.0, 1.0, 30.0), bounds s.Children[1])

[<Fact>]
let ``attached settings say which panel reads them`` () =
    Assert.Equal("Grid", GridProps.row.AttachedTo)
    Assert.Equal("Grid.Row", GridProps.row.Name)
    Assert.Equal("DockPanel", DockProps.dock.AttachedTo)
    Assert.Equal("Canvas", CanvasProps.left.AttachedTo)
    Assert.Equal("", LayoutProps.margin.AttachedTo)

[<Fact>]
let ``a child spanning a star column doesn't widen an auto column it also spans`` () =
    let view =
        grid [ Grid.columns "auto, *, auto"; Grid.rows "auto, auto" ] [
            box 40.0 10.0 []
            box 30.0 10.0 [ Grid.column 2 ]
            box 500.0 10.0 [ Grid.cell (1, 1); Grid.columnSpan 2 ]
        ]
    let _, g = run view 300.0 100.0
    Assert.Equal((270.0, 0.0, 30.0, 10.0), bounds g.Children[1])
    Assert.Equal((40.0, 10.0, 260.0, 10.0), bounds g.Children[2])
