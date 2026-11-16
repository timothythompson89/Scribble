/// Pointer input (M5 step 1): enter/leave, tunnel and bubble routing, capture, clicks, element
/// states, isEnabled, cursors and wheel, against the fake platform from AppTests.
module Scribble.Tests.InputTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests

let private move (w: FakeWindow) x y = w.Send(PointerMoved(Mouse, 0, { X = x; Y = y }, Modifiers.None))
let private press (w: FakeWindow) x y = w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private release (w: FakeWindow) x y = w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))

/// Starts a static view, draws the first frame (so it's laid out) and returns the window, the
/// host and a function that finds an element by name.
let private show (view: View) =
    let w, _, app = start [ App.view view ]
    w.DeliverFrame() |> ignore
    let host = app :?> ElementHost
    let rec find name (e: Element) =
        if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)
    w, host, (fun name -> (find name host.Tree.Root.Value).Value)

/// Two 100×100 boxes side by side, at x 0–100 and 100–200, inside a row named "row".
let private twoBoxes (a: Setting<Border> list) (b: Setting<Border> list) =
    StackPanel.create [
        StackPanel.name "row"
        StackPanel.orientation Horizontal
        StackPanel.verticalAlignment VerticalAlignment.Top
        StackPanel.horizontalAlignment HorizontalAlignment.Left
        StackPanel.children [
            Border.create ([ Border.name "a"; Border.width 100.0; Border.height 100.0 ] @ a)
            Border.create ([ Border.name "b"; Border.width 100.0; Border.height 100.0 ] @ b)
        ]
    ]

[<Fact>]
let ``enter and leave follow the pointer, outermost first in and deepest first out`` () =
    let log = List<string>()
    let track name : Setting<'k> list when 'k :> Layoutable =
        [ Layoutable.onPointerEnter (fun _ -> log.Add("enter " + name)); Layoutable.onPointerLeave (fun _ -> log.Add("leave " + name)) ]
    let view =
        StackPanel.create (
            [ StackPanel.name "row"
              StackPanel.orientation Horizontal
              StackPanel.verticalAlignment VerticalAlignment.Top
              StackPanel.horizontalAlignment HorizontalAlignment.Left
              StackPanel.children [
                  Border.create ([ Border.width 100.0; Border.height 100.0 ] @ track "a")
                  Border.create ([ Border.width 100.0; Border.height 100.0 ] @ track "b")
              ] ]
            @ track "row")
    let w, _, _ = show view
    move w 50 50
    move w 60 50 // still inside a: nothing new
    Assert.Equal<string list>([ "enter row"; "enter a" ], List.ofSeq log)
    log.Clear()
    move w 150 50
    Assert.Equal<string list>([ "leave a"; "enter b" ], List.ofSeq log)
    log.Clear()
    w.Send(PointerExited(Mouse, 0))
    Assert.Equal<string list>([ "leave b"; "leave row" ], List.ofSeq log)

[<Fact>]
let ``pointer down tunnels from the root, then bubbles from the target`` () =
    let log = List<string>()
    let view =
        Border.create [
            Border.name "outer"
            Border.width 200.0
            Border.height 200.0
            Border.padding 50.0
            Border.onPreviewPointerDown (fun _ -> log.Add "preview outer")
            Border.onPointerDown (fun _ -> log.Add "outer")
            Border.child (
                Border.create [
                    Border.name "inner"
                    Border.onPreviewPointerDown (fun _ -> log.Add "preview inner")
                    Border.onPointerDown (fun e ->
                        log.Add "inner"
                        // Positions: window and local (inner starts at 50, 50 in the outer box,
                        // which is centered in the 400×300 window: outer at 100, 50).
                        Assert.Equal(Point(160.0, 120.0), e.Position)
                        Assert.Equal(Point(10.0, 20.0), e.LocalPosition)
                        Assert.Equal(ValueSome MouseButton.Left, e.Button))
                ])
        ]
    let w, _, _ = show view
    press w 160 120
    Assert.Equal<string list>([ "preview outer"; "preview inner"; "inner"; "outer" ], List.ofSeq log)

[<Fact>]
let ``handling stops the route`` () =
    let log = List<string>()
    let view =
        Border.create [
            Border.onPointerDown (fun _ -> log.Add "outer")
            Border.child (Border.create [ Border.onPointerDown (fun e -> log.Add "inner"; e.Handle()) ])
        ]
    let w, _, _ = show view
    press w 10 10
    Assert.Equal<string list>([ "inner" ], List.ofSeq log)

[<Fact>]
let ``a press handled in Preview is taken: no click, and the taker gets the moves`` () =
    let mutable clicks = 0
    let moves = List<string>()
    let view =
        Border.create [
            Border.name "outer"
            Border.onPreviewPointerDown (fun e -> e.Handle())
            Border.onPointerMove (fun _ -> moves.Add "outer")
            Border.child (
                Border.create [
                    Border.width 100.0
                    Border.height 100.0
                    Border.onClick (fun _ -> clicks <- clicks + 1)
                    Border.onPointerMove (fun _ -> moves.Add "inner")
                ])
        ]
    let w, _, _ = show view
    press w 200 150
    move w 210 150
    release w 210 150
    Assert.Equal(0, clicks)
    Assert.Equal<string list>([ "outer" ], List.ofSeq moves)

[<Fact>]
let ``the pressed element keeps the pointer: moves and the release go to it`` () =
    let log = List<string>()
    let view =
        twoBoxes
            [ Border.onPointerMove (fun e -> log.Add $"a move {e.LocalPosition.X}"); Border.onPointerUp (fun _ -> log.Add "a up") ]
            [ Border.onPointerEnter (fun _ -> log.Add "b enter"); Border.onPointerUp (fun _ -> log.Add "b up") ]
    let w, _, el = show view
    press w 50 50
    move w 150 50 // over b, but a holds the press
    release w 150 50
    Assert.Equal<string list>([ "a move 150"; "a up"; "b enter" ], List.ofSeq log)
    // Released: b is under the pointer now.
    Assert.True (el "b").IsPointerOver
    Assert.False (el "a").IsPointerOver

[<Fact>]
let ``dragging off cancels the click, dragging back restores it, and IsPressed follows`` () =
    let mutable clicks = 0
    let w, _, el = show (twoBoxes [ Border.onClick (fun _ -> clicks <- clicks + 1) ] [])
    let a = el "a"
    press w 50 50
    Assert.True a.IsPressed
    move w 150 50
    Assert.False a.IsPressed
    Assert.False a.IsPointerOver
    release w 150 50
    Assert.Equal(0, clicks)
    press w 50 50
    move w 150 50
    move w 60 50
    Assert.True a.IsPressed
    release w 60 50
    Assert.Equal(1, clicks)
    Assert.False a.IsPressed

[<Fact>]
let ``clicking inside a child clicks the parent`` () =
    let mutable clicks = 0
    let view =
        Button.create [ Button.width 100.0; Button.height 40.0; Button.text "Go"; Button.onClick (fun _ -> clicks <- clicks + 1) ]
    let w, _, _ = show view
    // Buttons sit on the left (x 0–100) and are centered vertically (y 130–170); the label is in the middle.
    press w 50 150
    release w 50 150
    Assert.Equal(1, clicks)

[<Fact>]
let ``a canceled press doesn't click`` () =
    let mutable clicks = 0
    let w, _, el = show (twoBoxes [ Border.onClick (fun _ -> clicks <- clicks + 1) ] [])
    press w 50 50
    w.Send(PointerCanceled(Mouse, 0))
    Assert.False (el "a").IsPressed
    release w 50 50
    Assert.Equal(0, clicks)

[<Fact>]
let ``disabled elements and everything inside them get no input`` () =
    let log = List<string>()
    let view =
        Border.create [
            Border.name "outer"
            Border.onPointerDown (fun _ -> log.Add "outer down")
            Border.child (
                Border.create [
                    Border.name "disabled"
                    Border.isEnabled false
                    Border.onPointerEnter (fun _ -> log.Add "disabled enter")
                    Border.onClick (fun _ -> log.Add "disabled click")
                    Border.child (Border.create [ Border.name "inside"; Border.onPointerDown (fun _ -> log.Add "inside down") ])
                ])
        ]
    let w, _, el = show view
    Assert.False (el "inside").IsEnabled
    Assert.True (el "outer").IsEnabled
    move w 10 10
    press w 10 10
    release w 10 10
    // The enabled ancestor still hears about it.
    Assert.Equal<string list>([ "outer down" ], List.ofSeq log)
    Assert.False (el "disabled").IsPointerOver

[<Fact>]
let ``buttons show a hand, and the cursor goes back when the pointer leaves`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                // Only generic settings: this used to be ambiguous with Border.create (now a module function).
                Button.create [ Button.name "button"; Button.width 100.0; Button.height 40.0 ]
                Border.create [ Border.name "text"; Border.height 40.0; Border.cursor Cursor.Text; Border.child (Border.create []) ]
            ]
        ]
    let w, _, _ = show view
    move w 50 20
    Assert.Equal(Cursor.Hand, w.Cursor)
    // Inside a child that doesn't set one: the ancestor's cursor applies.
    move w 200 60
    Assert.Equal(Cursor.Text, w.Cursor)
    move w 200 200
    Assert.Equal(Cursor.Default, w.Cursor)

[<Fact>]
let ``the wheel tunnels and bubbles too`` () =
    let log = List<string>()
    let view =
        Border.create [
            Border.onPreviewWheel (fun _ -> log.Add "preview outer")
            Border.onWheel (fun e -> log.Add $"outer {e.DeltaY}")
            Border.child (Border.create [ Border.onWheel (fun _ -> log.Add "inner") ])
        ]
    let w, _, _ = show view
    w.Send(PointerWheel({ X = 10.0; Y = 10.0 }, 0.0, -1.0, Modifiers.Control))
    Assert.Equal<string list>([ "preview outer"; "inner"; "outer -1" ], List.ofSeq log)

[<Fact>]
let ``when layout moves things under a still pointer, hover follows`` () =
    let mutable dispatch = ignore
    let view (showA: bool) d =
        dispatch <- d
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.verticalAlignment VerticalAlignment.Top
            StackPanel.horizontalAlignment HorizontalAlignment.Left
            StackPanel.children [
                if showA then Border.create [ Border.key "a"; Border.name "a"; Border.width 100.0; Border.height 100.0 ]
                Border.create [ Border.key "b"; Border.name "b"; Border.width 100.0; Border.height 100.0 ]
            ]
        ]
    let w, _, app = start [ App.program true (fun () _ -> false) view ]
    w.DeliverFrame() |> ignore
    let host = app :?> ElementHost
    let b = host.Tree.Root.Value.Children[1]
    move w 50 50
    Assert.False b.IsPointerOver
    dispatch ()
    w.DeliverFrame() |> ignore
    // a is gone and b slid under the pointer.
    Assert.Same(b, host.Tree.Root.Value.Children[0])
    Assert.True b.IsPointerOver

[<Fact>]
let ``states change the look: a frame is requested for hover, none for moves within`` () =
    let w, _, el = show (twoBoxes [] [])
    Assert.False(w.DeliverFrame())
    move w 50 50
    Assert.True (el "a").IsPointerOver
    Assert.True(w.DeliverFrame())
    move w 60 50
    Assert.False(w.DeliverFrame())

[<Fact>]
let ``a click carries its source and the modifiers held at release`` () =
    let clicks = List<ClickEventArgs>()
    let w, _, _ = show (twoBoxes [ Border.onClick clicks.Add ] [])
    let at: PixelPoint = { X = 50.0; Y = 50.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.Shift))
    let e = Assert.Single clicks
    Assert.Equal(ClickSource.Pointer Mouse, e.Source)
    Assert.True(e.Modifiers.HasFlag Modifiers.Shift)

[<Fact>]
let ``an icon cursor is the window's cursor over its element, and stays the same value as the pointer moves`` () =
    let pointer = Icon.fromPath "M0 0h24v24H0z" [ Icon.name "square" ]
    let view =
        StackPanel.create [
            StackPanel.children [
                Border.create [ Border.height 40.0; Border.cursor (Cursor.fromIcon pointer [ Cursor.hotspot (0.0, 0.0) ]) ]
                Border.create [ Border.height 40.0 ]
            ]
        ]
    let w, _, _ = show view
    move w 50 20
    let first = w.Cursor
    Assert.Equal(Cursor.fromIcon pointer [ Cursor.hotspot (0.0, 0.0) ], first)
    move w 60 25
    // The same value: a platform compares and keeps what it made.
    Assert.Equal(first, w.Cursor)
    move w 50 60
    Assert.Equal(Cursor.Default, w.Cursor)
