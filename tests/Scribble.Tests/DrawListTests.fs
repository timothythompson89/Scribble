module Scribble.Tests.DrawListTests

open Xunit
open Scribble.Rendering

/// Reads shape `index` back as (rect, radii, fill, border, params, clip) float arrays.
let private shape (list: DrawList) index =
    let data = list.InstanceData
    let at k = float data[index * 24 + k]
    {| Rect = [ at 0; at 1; at 2; at 3 ]
       Radii = [ at 4; at 5; at 6; at 7 ]
       Fill = [ at 8; at 9; at 10; at 11 ]
       BorderWidth = at 16
       Sigma = at 17
       Clip = [ at 20; at 21; at 22; at 23 ] |}

[<Fact>]
let ``opacity multiplies the alpha of fills and borders, nested, until popped`` () =
    let list = DrawList(1.0)
    let square = Rect.Create(0.0, 0.0, 10.0, 10.0)
    list.PushOpacity 0.5
    list.FillRect(square, Color.White)
    list.PushOpacity 0.5
    list.FillRect(square, Color.White.WithAlpha 0.8f)
    list.PopOpacity()
    list.FillRect(square, Color.White)
    list.PopOpacity()
    list.FillRect(square, Color.White)
    let alpha i = (shape list i).Fill[3]
    Assert.Equal(0.5, alpha 0, 5)
    Assert.Equal(0.2, alpha 1, 5)
    Assert.Equal(0.5, alpha 2, 5)
    Assert.Equal(1.0, alpha 3, 5)
    Assert.Equal(1.0, list.Opacity)

[<Fact>]
let ``rects are scaled to pixels and snapped to whole pixels`` () =
    let list = DrawList(1.5)
    list.FillRect(Rect.Create(10.0, 10.0, 21.0, 7.0), Color.White)
    // 10 × 1.5 = 15, (10 + 21) × 1.5 = 46.5 → 47 (round half to even → 46; either way whole pixels)
    let s = shape list 0
    for v in s.Rect do
        Assert.Equal(System.Math.Round v, v)
    Assert.Equal(15.0, s.Rect[0])

[<Fact>]
let ``precise mode keeps fractional edges`` () =
    let list = DrawList(1.5)
    list.FillRect(Rect.Create(10.1, 0.0, 10.0, 10.0), Color.White, Precise)
    Assert.Equal(15.15, (shape list 0).Rect[0], 3)

[<Fact>]
let ``border widths snap to at least one pixel`` () =
    let list = DrawList(1.0)
    list.StrokeRect(Rect.Create(0.0, 0.0, 10.0, 10.0), CornerRadius.Zero, 0.3, Color.White)
    Assert.Equal(1.0, (shape list 0).BorderWidth)

[<Fact>]
let ``radii scale with the frame`` () =
    let list = DrawList(2.0)
    list.FillRoundedRect(Rect.Create(0.0, 0.0, 50.0, 50.0), CornerRadius.Uniform 8.0, Color.White)
    Assert.Equal<float list>([ 16.0; 16.0; 16.0; 16.0 ], (shape list 0).Radii)

[<Fact>]
let ``transforms nest`` () =
    let list = DrawList(2.0)
    list.PushTranslate(10.0, 20.0)
    list.PushTranslate(1.0, 1.0)
    list.FillRect(Rect.Create(0.0, 0.0, 5.0, 5.0), Color.White)
    list.PopTransform()
    list.FillRect(Rect.Create(0.0, 0.0, 5.0, 5.0), Color.White)
    list.PopTransform()
    Assert.Equal<float list>([ 22.0; 42.0; 10.0; 10.0 ], (shape list 0).Rect)
    Assert.Equal<float list>([ 20.0; 40.0; 10.0; 10.0 ], (shape list 1).Rect)

[<Fact>]
let ``clips intersect and are recorded per shape`` () =
    let list = DrawList(1.0)
    list.PushClip(Rect.Create(0.0, 0.0, 100.0, 100.0))
    list.PushClip(Rect.Create(50.0, 50.0, 100.0, 100.0))
    list.FillRect(Rect.Create(60.0, 60.0, 10.0, 10.0), Color.White)
    list.PopClip()
    list.PopClip()
    Assert.Equal<float list>([ 50.0; 50.0; 100.0; 100.0 ], (shape list 0).Clip)

[<Fact>]
let ``fully clipped and invisible shapes are skipped`` () =
    let list = DrawList(1.0)
    list.PushClip(Rect.Create(0.0, 0.0, 10.0, 10.0))
    list.FillRect(Rect.Create(50.0, 50.0, 10.0, 10.0), Color.White)
    list.PopClip()
    list.FillRect(Rect.Create(0.0, 0.0, 10.0, 10.0), Color.Transparent)
    Assert.Equal(0, list.Count)

[<Fact>]
let ``shadows carry a blur sigma and are not snapped`` () =
    let list = DrawList(1.0)
    list.DrawShadow(Rect.Create(10.0, 10.0, 50.0, 50.0), CornerRadius.Uniform 4.0, 8.0, Color.Black, offsetY = 2.5)
    let s = shape list 0
    Assert.Equal(4.0, s.Sigma)
    Assert.Equal(12.5, s.Rect[1])

[<Fact>]
let ``unbalanced pops throw`` () =
    let list = DrawList(1.0)
    Assert.Throws<System.InvalidOperationException>(fun () -> list.PopClip()) |> ignore
    Assert.Throws<System.InvalidOperationException>(fun () -> list.PopTransform()) |> ignore

[<Fact>]
let ``the list grows past its initial capacity`` () =
    let list = DrawList(1.0)
    for i in 0..999 do
        list.FillRect(Rect.Create(float i, 0.0, 1.0, 1.0), Color.White)
    Assert.Equal(1000, list.Count)
    Assert.Equal(999.0, (shape list 999).Rect[0])
