/// Icons: SVG path data, rasterizing paths, the Icon value, IconView and Button.icon.
module Scribble.Tests.IconTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Tests.TestShaping

let private parse (d: string) = (PathGeometry.Parse d).Contours

/// End points of every segment of a contour, rounded to 3 decimals.
let private points (c: PathContour) =
    [ for s in c.Segments ->
        match s with
        | LineTo(x, y) | QuadTo(_, _, x, y) | CubicTo(_, _, _, _, x, y) -> Math.Round(x, 3), Math.Round(y, 3) ]

let private identity (x: float) (y: float) = struct (x, y)

/// Total coverage in pixels (255 = one pixel).
let private area (b: Rasterizer.GlyphBitmap) = (b.Coverage |> Array.sumBy int |> float) / 255.0

[<Fact>]
let ``absolute and relative lines, H and V, closing and implicit lines after M`` () =
    let a = parse "M2 2 L10 2 H10 V10 h-8 z"
    Assert.Equal(1, a.Length)
    Assert.Equal((2.0, 2.0), (a[0].StartX, a[0].StartY))
    Assert.Equal<(float * float) list>([ 10.0, 2.0; 10.0, 2.0; 10.0, 10.0; 2.0, 10.0 ], points a[0])
    // Extra pairs after m are relative lines; numbers may run together ("1-2", ".5.5").
    let b = parse "m1 1 2 0 0 2-2 0z M.5.5 1e1 .5"
    Assert.Equal(2, b.Length)
    Assert.Equal<(float * float) list>([ 3.0, 1.0; 3.0, 3.0; 1.0, 3.0 ], points b[0])
    Assert.Equal((0.5, 0.5), (b[1].StartX, b[1].StartY))
    Assert.Equal<(float * float) list>([ 10.0, 0.5 ], points b[1])

[<Fact>]
let ``after z, a relative move starts from the closed contour's start`` () =
    let c = parse "M10 10h5v5z m2 2h1v1z"
    Assert.Equal((12.0, 12.0), (c[1].StartX, c[1].StartY))

[<Fact>]
let ``S and T reflect the previous control point`` () =
    let c = parse "M0 0 C0 10 10 10 10 0 S20 -10 20 0 M0 0 Q5 10 10 0 T20 0"
    match c[0].Segments[1] with
    | CubicTo(c1x, c1y, _, _, _, _) -> Assert.Equal((10.0, -10.0), (c1x, c1y))
    | _ -> failwith "expected a cubic"
    match c[1].Segments[1] with
    | QuadTo(cx, cy, x, y) -> Assert.Equal((15.0, -10.0, 20.0, 0.0), (cx, cy, x, y))
    | _ -> failwith "expected a quadratic"

[<Fact>]
let ``arcs become cubics that end exactly where the arc ends, flags may run together`` () =
    let c = parse "M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0z M0 0a1 1 0 00 1 1"
    // Each half circle splits into two quarter cubics.
    Assert.Equal(4, c[0].Segments.Length)
    Assert.Equal((22.0, 12.0), points c[0] |> List.item 1)
    Assert.Equal((2.0, 12.0), points c[0] |> List.last)
    Assert.Equal((1.0, 1.0), points c[1] |> List.last)

[<Fact>]
let ``malformed path data is a FormatException`` () =
    Assert.Throws<FormatException>(fun () -> PathGeometry.Parse "M1 1 L2" |> ignore) |> ignore
    Assert.Throws<FormatException>(fun () -> PathGeometry.Parse "12 4" |> ignore) |> ignore
    Assert.Throws<FormatException>(fun () -> PathGeometry.Parse "M0 0 a1 1 0 2 0 1 1" |> ignore) |> ignore
    Assert.Throws<FormatException>(fun () -> PathGeometry.Parse "M0 0 X1 1" |> ignore) |> ignore

[<Fact>]
let ``a rasterized square path covers exactly its pixels`` () =
    let b = Rasterizer.rasterizePath (PathGeometry.Parse "M1 1h4v3H1z") identity
    Assert.Equal((1, 1), (b.Left, b.Top))
    Assert.Equal(12.0, area b, 3)
    Assert.Equal(255uy, b.Coverage[0])

[<Fact>]
let ``a circle made of arcs has the area of a circle, and a hole wound the other way stays empty`` () =
    let b = Rasterizer.rasterizePath (PathGeometry.Parse "M0 20a20 20 0 1 0 40 0a20 20 0 1 0-40 0z") identity
    // Within about 0.1 px around the edge (the flattening tolerance; lines run inside the curve).
    Assert.InRange(area b, Math.PI * 400.0 - 10.0, Math.PI * 400.0 + 1.0)
    // A ring: the inner circle runs the other way (nonzero leaves it empty).
    let ring = Rasterizer.rasterizePath (PathGeometry.Parse "M0 20a20 20 0 1 0 40 0a20 20 0 1 0-40 0zM10 20a10 10 0 1 1 20 0a10 10 0 1 1-20 0z") identity
    Assert.InRange(area ring, Math.PI * 300.0 - 10.0, Math.PI * 300.0 + 10.0)
    Assert.Equal(0uy, ring.Coverage[20 * ring.Width + 20 - ring.Left])

[<Fact>]
let ``icons compare by shape, box, rotation, flips and name, and describe themselves`` () =
    let a = Icon.fromPath "M2 2h20v20H2z" []
    let b = Icon.fromPath "M2 2h20v20H2z" []
    Assert.Equal(a, b)
    Assert.Equal(a.GetHashCode(), b.GetHashCode())
    Assert.NotEqual(a, a.Rotated 90.0)
    Assert.NotEqual(a, a.FlippedHorizontally)
    Assert.Equal(a, a.FlippedHorizontally.FlippedHorizontally)
    Assert.NotEqual(a, Icon.fromPath "M2 2h20v20H2z" [ Icon.viewBox (0.0, 0.0, 48.0, 48.0) ])
    Assert.Equal("path", a.Description)
    let named = Icon.fromPath "M2 2h20v20H2z" [ Icon.name "box"; Icon.rotate 90.0; Icon.flipVertical ]
    Assert.Equal("box-rotate90-flipv", named.Description)
    Assert.Equal("box-rotate90-fliph-flipv", named.FlippedHorizontally.Description)
    Assert.Throws<ArgumentException>(fun () -> Icon.fromPath " " [] |> ignore) |> ignore

[<Fact>]
let ``bad path data is found when the icon is first drawn`` () =
    let icon = Icon.fromPath "M2 2 L" []
    Assert.Throws<FormatException>(fun () -> icon.Path |> ignore) |> ignore

let private square = Icon.fromPath "M0 0h24v24H0z" [ Icon.name "square" ]

/// The glyph shapes in a draw list: (x, y, width, height) in pixels.
let private glyphRects (list: DrawList) =
    let data = list.InstanceData
    [ for i in 0 .. list.Count - 1 do
        let at k = float data[i * 24 + k]
        yield (at 0, at 1, at 2, at 3) ]

[<Fact>]
let ``DrawIcon scales the viewBox to the size, snaps to pixels and caches the bitmap`` () =
    let text = textSystem ()
    let list = DrawList(2.0)
    text.DrawIcon(list, square.Path, square.ViewBox, 0.0, false, false, 10.2, 5.0, 12.0, Color.White)
    // 12 DIPs at 2x = 24 px, at (20.4 → 20, 10); one more column, where the rasterizer's
    // accumulation spills (empty).
    Assert.Equal<(float * float * float * float) list>([ 20.0, 10.0, 25.0, 24.0 ], glyphRects list)
    // Drawing the same icon again uses the bitmap already in the atlas.
    let atlas = GlyphAtlas(256)
    let t2 = TextSystem(atlas, new FixedShaper())
    let l2 = DrawList(1.0)
    t2.DrawIcon(l2, square.Path, square.ViewBox, 0.0, false, false, 0.0, 0.0, 10.0, Color.White)
    let dirty = atlas.TakeDirtyRegion()
    t2.DrawIcon(l2, square.Path, square.ViewBox, 0.0, false, false, 30.0, 0.0, 10.0, Color.White)
    Assert.False dirty.IsEmpty
    Assert.True((atlas.TakeDirtyRegion()).IsEmpty)

[<Fact>]
let ``rotation and flips turn the shape around its center`` () =
    // A right triangle in the top-left half of its box.
    let tri = Icon.fromPath "M0 0h24L0 24z" []
    let coverageAt (icon: Icon) =
        let atlas = GlyphAtlas(256)
        let text = TextSystem(atlas, new FixedShaper())
        let list = DrawList(1.0)
        text.DrawIcon(list, icon.Path, icon.ViewBox, icon.Rotation, icon.IsFlippedHorizontally, icon.IsFlippedVertically, 0.0, 0.0, 24.0, Color.White)
        let data = list.InstanceData
        let ax, ay = int data[4], int data[5]
        let x0, y0 = int data[0], int data[1]
        // Coverage near the middle of each side of the box: left, top, right, bottom (away from
        // both diagonals, where the triangle's long edge can run).
        let pixel x y = atlas.Pixels[(ay + y - y0) * atlas.Size + ax + x - x0]
        [ pixel 4 12 > 200uy; pixel 12 4 > 200uy; pixel 20 12 > 200uy; pixel 12 20 > 200uy ]
    // The triangle fills the top-left half: left and top.
    Assert.Equal<bool list>([ true; true; false; false ], coverageAt tri)
    // Turned a quarter clockwise, it fills the top-right half: top and right.
    Assert.Equal<bool list>([ false; true; true; false ], coverageAt (tri.Rotated 90.0))
    Assert.Equal<bool list>([ false; true; true; false ], coverageAt tri.FlippedHorizontally)
    Assert.Equal<bool list>([ true; false; false; true ], coverageAt tri.FlippedVertically)

let private frame (t: ElementTree) =
    t.UpdateLayout(Size.Create(400.0, 300.0))
    t.RunAfterFrame()

[<Fact>]
let ``IconView is its size, and an equal icon in a rebuilt view changes nothing`` () =
    let tree = ElementTree(textSystem ())
    let view () = IconView.create [ IconView.icon (Icon.fromPath "M0 0h24v24H0z" []); IconView.size 24.0 ]
    tree.Update(StackPanel.create [ StackPanel.children [ view () ] ])
    frame tree
    let e = tree.Root.Value.Children[0]
    Assert.Equal(Size.Create(24.0, 24.0), e.DesiredSize)
    let mutable invalidated = false
    tree.OnInvalidated <- fun () -> invalidated <- true
    tree.Update(StackPanel.create [ StackPanel.children [ view () ] ])
    Assert.False invalidated

[<Fact>]
let ``a Button with an icon makes room for it and centers icon and text together`` () =
    let tree = ElementTree(textSystem ())
    let plain = Button.create [ Button.text "Add" ]
    let withIcon = Button.create [ Button.icon square; Button.text "Add" ]
    let iconOnly = Button.create [ Button.icon square ]
    let wide = Button.create [ Button.icon square; Button.text "Add"; Button.width 200.0 ]
    tree.Update(StackPanel.create [ StackPanel.children [ plain; withIcon; iconOnly; wide ] ])
    frame tree
    let b = tree.Root.Value.Children
    // Icon 18 + gap 8 more than the plain button; the icon alone: 18 + padding 28 + border 2.
    Assert.Equal(b[0].DesiredSize.Width + 26.0, b[1].DesiredSize.Width, 3)
    Assert.Equal(48.0, b[2].DesiredSize.Width, 3)
    // In the wide button, icon + gap + text sit in the middle: as much room left of the icon as
    // right of the text.
    let iconRect = b[3].Get ButtonProps.iconRect
    let textBounds = b[3].Children[0].Bounds
    let leftRoom = iconRect.X
    let rightRoom = 200.0 - (textBounds.X + textBounds.Width)
    Assert.InRange(abs (leftRoom - rightRoom), 0.0, 1.0) // within layout rounding
    Assert.Equal(iconRect.X + 18.0 + 8.0, textBounds.X, 3)
