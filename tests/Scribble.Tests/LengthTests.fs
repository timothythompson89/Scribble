module Scribble.Tests.LengthTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping
open Scribble.Tests.LayoutTests

[<Fact>]
let ``lengths parse from strings`` () =
    Assert.Equal(Px 45.0, Length.parse "45px")
    Assert.Equal(Px 45.0, Length.parse "45")
    Assert.Equal(Px 12.5, Length.parse " 12.5PX ")
    Assert.Equal(Percent 45.0, Length.parse "45%")
    Assert.Equal(Star 2.0, Length.parse "2*")
    Assert.Equal(Star 1.0, Length.parse "*")
    Assert.Equal(Star 0.5, Length.parse "0.5*")
    Assert.Equal(Auto, Length.parse "Auto")

[<Fact>]
let ``bad strings are rejected with a helpful message`` () =
    for bad in [ "wide"; "45em"; "-3px"; "%"; ""; "px" ] do
        Assert.True((Length.tryParse bad).IsNone, bad)
    let e = Assert.Throws<ArgumentException>(fun () -> Length.parse "45em" |> ignore)
    Assert.Contains("isn't a length", e.Message)

[<Fact>]
let ``lengths print back the way they parse`` () =
    for text in [ "45px"; "12.5px"; "45%"; "2*"; "*"; "auto" ] do
        Assert.Equal(text, (Length.parse text).ToString())

let private leftTop<'k when 'k :> Layoutable> : Setting<'k> list =
    [ Layoutable.horizontalAlignment HorizontalAlignment.Left; Layoutable.verticalAlignment VerticalAlignment.Top ]

let private layoutOf view w h = snd (run view w h)

[<Fact>]
let ``numbers, typed lengths and strings mean the same`` () =
    let a = layoutOf (Box.create (leftTop @ [ Box.width 40.0; Box.height (Px 20.0) ])) 100.0 100.0
    let b = layoutOf (Box.create (leftTop @ [ Box.width "40px"; Box.height "20" ])) 100.0 100.0
    Assert.Equal(a.Bounds, b.Bounds)
    Assert.Equal(Rect.Create(0.0, 0.0, 40.0, 20.0), a.Bounds)

[<Fact>]
let ``percent is a share of what the parent offers, margin included`` () =
    let b = layoutOf (Box.create (leftTop @ [ Box.width "50%"; Box.height (Percent 25.0); Box.margin 10.0 ])) 200.0 100.0
    Assert.Equal(Rect.Create(10.0, 10.0, 100.0, 25.0), b.Bounds)

[<Fact>]
let ``percent min and max sizes clamp`` () =
    let b = layoutOf (Box.create [ Box.maxWidth "80%"; Box.minHeight "30%" ]) 200.0 100.0
    Assert.Equal(160.0, b.Bounds.Width)
    // Stretched, so the min doesn't bite in height; a content-sized one does.
    let c = layoutOf (Box.create (leftTop @ [ Box.size (10.0, 10.0); Box.minHeight "30%" ])) 200.0 100.0
    Assert.Equal(30.0, c.Bounds.Height)

[<Fact>]
let ``percent in unlimited space acts like auto`` () =
    let s = layoutOf (StackPanel.create (leftTop @ [ StackPanel.children [ Box.create [ Box.size (10.0, 12.0); Box.height "50%" ] ] ])) 100.0 100.0
    Assert.Equal(12.0, (child s [ 0 ]).Bounds.Height)

[<Fact>]
let ``star children share a stack's leftover space by weight`` () =
    let header = Box.create [ Box.size (10.0, 56.0) ]
    let content = Box.create [ Box.size (10.0, 10.0); Box.height "*" ]
    let footer = Box.create [ Box.size (10.0, 32.0) ]
    let s = layoutOf (StackPanel.create [ StackPanel.children [ header; content; footer ] ]) 300.0 600.0
    Assert.Equal(Rect.Create(0.0, 56.0, 300.0, 512.0), (child s [ 1 ]).Bounds)
    Assert.Equal(568.0, (child s [ 2 ]).Bounds.Y)
    // Weights 1:2 split 300 of leftover as 100 and 200 (after spacing).
    let h = layoutOf (StackPanel.create [ StackPanel.spacing 10.0; StackPanel.children [ Box.create [ Box.height "1*" ]; Box.create [ Box.height (Star 2.0) ] ] ]) 50.0 310.0
    Assert.Equal(100.0, (child h [ 0 ]).Bounds.Height)
    Assert.Equal(200.0, (child h [ 1 ]).Bounds.Height)
    Assert.Equal(110.0, (child h [ 1 ]).Bounds.Y)

[<Fact>]
let ``without star children nothing fills`` () =
    let s = layoutOf (StackPanel.create [ StackPanel.children [ Box.create [ Box.size (10.0, 20.0) ]; Box.create [ Box.size (10.0, 30.0) ] ] ]) 100.0 600.0
    Assert.Equal(20.0, (child s [ 0 ]).Bounds.Height)
    Assert.Equal(30.0, (child s [ 1 ]).Bounds.Height)

[<Fact>]
let ``star children take their content size in unlimited space`` () =
    // The inner stack sits in an outer vertical stack: unlimited height, nothing to share.
    let inner = Box.create [ Box.size (10.0, 15.0); Box.height "*" ]
    let outer = layoutOf (StackPanel.create [ StackPanel.children [ StackPanel.create [ StackPanel.children [ inner ] ] ] ]) 100.0 600.0
    Assert.Equal(15.0, (child outer [ 0; 0 ]).Bounds.Height)

[<Fact>]
let ``a top-aligned stack has no leftover space`` () =
    let star = Box.create [ Box.size (10.0, 15.0); Box.height "*" ]
    let s = layoutOf (StackPanel.create [ StackPanel.verticalAlignment VerticalAlignment.Top; StackPanel.children [ Box.create [ Box.size (10.0, 20.0) ]; star ] ]) 100.0 600.0
    Assert.Equal(15.0, (child s [ 1 ]).Bounds.Height)
    Assert.Equal(35.0, s.Bounds.Height)

[<Fact>]
let ``horizontal stacks share width the same way`` () =
    let s = layoutOf (StackPanel.create [ StackPanel.orientation Horizontal; StackPanel.children [ Box.create [ Box.size (200.0, 10.0) ]; Box.create [ Box.width "*" ] ] ]) 1000.0 100.0
    Assert.Equal(Rect.Create(200.0, 0.0, 800.0, 100.0), (child s [ 1 ]).Bounds)
