/// M6 step 1: layers above the app (pointer falls through where a layer has nothing, modal layers
/// block everything below, focus per layer) and the basic ScrollViewer.
module Scribble.Tests.LayerTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.AppTests

let private move (w: FakeWindow) x y = w.Send(PointerMoved(Mouse, 0, { X = x; Y = y }, Modifiers.None))
let private press (w: FakeWindow) x y = w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private release (w: FakeWindow) x y = w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private click w x y = press w x y; release w x y
let private key (w: FakeWindow) k = w.Send(KeyDown(k, Modifiers.None, false)); w.Send(KeyUp(k, Modifiers.None))

let rec private find (name: string) (e: Element) : Element option =
    if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)

/// Finds a named element in any layer.
let private named (host: ElementHost) name =
    host.Layers |> Seq.pick (fun l -> l.Tree.Root |> ValueOption.toOption |> Option.bind (find name))

let private start (settings: AppSetting<unit> list) =
    let w, _, app = start settings
    let host = app :?> ElementHost
    w.DeliverFrame() |> ignore
    w, host

/// The app: a full-window background that counts clicks, with a button at the top left.
let private appView (log: List<string>) =
    Border.create [
        Border.name "background"
        Border.onClick (fun _ -> log.Add "app")
        Border.child (
            Button.create [
                Button.name "appButton"
                Button.width 100.0
                Button.height 40.0
                Button.horizontalAlignment HorizontalAlignment.Left
                Button.verticalAlignment VerticalAlignment.Top
                Button.onClick (fun _ -> log.Add "appButton")
            ]
        )
    ]

/// A 100×100 panel at the bottom right of a 400×300 window (x 300–400, y 200–300) with a button.
let private panel (name: string) (log: List<string>) =
    Border.create [
        Border.name name
        Border.width 100.0
        Border.height 100.0
        Border.horizontalAlignment HorizontalAlignment.Right
        Border.verticalAlignment VerticalAlignment.Bottom
        Border.background (Color.Hex 0x333333)
        Border.child (
            Button.create [
                Button.name (name + "Button")
                Button.width 100.0
                Button.height 100.0
                Button.onClick (fun _ -> log.Add(name + "Button"))
            ]
        )
    ]

[<Fact>]
let ``a layer gets the pointer over its content, and the app gets it everywhere else`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log); App.layer [ Layer.view (panel "panel" log) ] ]
    Assert.Equal(2, host.Layers.Count)
    click w 350.0 250.0
    click w 50.0 250.0
    Assert.Equal<string list>([ "panelButton"; "app" ], List.ofSeq log)

[<Fact>]
let ``moving onto a layer leaves the app's elements, and back`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log); App.layer [ Layer.view (panel "panel" log) ] ]
    let background, panelBorder = named host "background", named host "panel"
    move w 250.0 250.0
    Assert.True background.IsPointerOver
    move w 350.0 250.0
    Assert.False background.IsPointerOver
    Assert.True panelBorder.IsPointerOver
    move w 250.0 250.0
    Assert.True background.IsPointerOver
    Assert.False panelBorder.IsPointerOver

[<Fact>]
let ``a layer appearing under a pointer that stands still takes it over after layout`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log) ]
    move w 350.0 250.0
    let background = named host "background"
    Assert.True background.IsPointerOver
    let layer = host.AddLayer [ Layer.view (panel "panel" log) ]
    w.DeliverFrame() |> ignore
    Assert.False background.IsPointerOver
    Assert.True (named host "panel").IsPointerOver
    host.RemoveLayer layer
    w.DeliverFrame() |> ignore
    Assert.True background.IsPointerOver

[<Fact>]
let ``a press keeps its layer until release, even when dragged over another layer`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log); App.layer [ Layer.view (panel "panel" log) ] ]
    press w 250.0 250.0
    move w 350.0 250.0 // over the panel, but the app holds the press
    Assert.False (named host "panel").IsPointerOver
    release w 350.0 250.0
    Assert.Empty log // released off the background: no click, and the panel didn't get one either
    Assert.True (named host "panel").IsPointerOver

[<Fact>]
let ``a modal layer blocks the app's pointer and keyboard until it goes`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log) ]
    key w Key.Tab
    Assert.Equal("appButton", host.Focused.Value.Name)
    let dialog = host.AddLayer [ Layer.modal true; Layer.view (panel "dialog" log) ]
    w.DeliverFrame() |> ignore
    click w 50.0 20.0 // the app's button, under the modal layer
    click w 200.0 150.0 // the app's background
    key w Key.Enter // the app's focused button
    Assert.Empty log
    Assert.True(obj.ReferenceEquals(dialog, host.KeyboardLayer))
    key w Key.Tab
    Assert.Equal("dialogButton", host.Focused.Value.Name)
    key w Key.Enter
    Assert.Equal<string list>([ "dialogButton" ], List.ofSeq log)
    host.RemoveLayer dialog
    w.DeliverFrame() |> ignore
    key w Key.Enter // focus is still on the app's button
    click w 200.0 150.0
    Assert.Equal<string list>([ "dialogButton"; "appButton"; "app" ], List.ofSeq log)

[<Fact>]
let ``each layer has its own focus, and the keyboard follows the last press`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log); App.layer [ Layer.view (panel "panel" log) ] ]
    key w Key.Tab
    Assert.Equal("appButton", host.Focused.Value.Name)
    let appButton = named host "appButton"
    Assert.True appButton.IsFocusVisible
    click w 350.0 250.0 // press in the panel: its button takes focus there
    Assert.Equal("panelButton", host.Focused.Value.Name)
    // The app keeps its focus, but it no longer looks active.
    Assert.True appButton.IsFocused
    Assert.False appButton.IsFocusVisible
    key w Key.Tab // cycles within the panel's layer only
    Assert.Equal("panelButton", host.Focused.Value.Name)
    click w 200.0 150.0 // back in the app: its background isn't focusable, so the app's focus clears
    key w Key.Tab
    Assert.Equal("appButton", host.Focused.Value.Name)

[<Fact>]
let ``layers stack by order, then by when they were added`` () =
    let w, host = start [ App.view (Border.create []) ]
    let high = host.AddLayer [ Layer.name "high"; Layer.order 50 ]
    let low = host.AddLayer [ Layer.name "low"; Layer.order 10 ]
    let alsoLow = host.AddLayer [ Layer.name "alsoLow"; Layer.order 10 ]
    Assert.Equal<string list>([ "App"; "low"; "alsoLow"; "high" ], [ for l in host.Layers -> l.Name ])
    ignore (w, high, low, alsoLow)

[<Fact>]
let ``the app's layer can't be removed`` () =
    let _, host = start [ App.view (Border.create []) ]
    Assert.Throws<System.InvalidOperationException>(fun () -> host.RemoveLayer host.App) |> ignore

[<Fact>]
let ``a removed layer's components stop`` () =
    let disposed = ref false
    let ticker =
        Component.define "Ticker" (fun ctx () ->
            ctx.useSubscription((), fun () -> { new System.IDisposable with member _.Dispose() = disposed.Value <- true })
            TextBlock.create [ TextBlock.text "tick" ])
    let w, host = start [ App.view (Border.create []) ]
    let layer = host.AddLayer [ Layer.view (ticker ()) ]
    w.DeliverFrame() |> ignore
    host.RemoveLayer layer
    Assert.True disposed.Value

// --- ScrollViewer --------------------------------------------------------------------------------

let private wheel (w: FakeWindow) x y notches = w.Send(PointerWheel({ X = x; Y = y }, 0.0, notches, Modifiers.None))

/// A 200×100 ScrollViewer at the top left holding ten 40-high rows (400 high in all).
let private scroller (outerWheel: List<float>) =
    Border.create [
        Border.onWheel (fun e -> outerWheel.Add e.DeltaY)
        Border.child (
            ScrollViewer.create [
                ScrollViewer.name "scroller"
                ScrollViewer.width 200.0
                ScrollViewer.height 100.0
                ScrollViewer.horizontalAlignment HorizontalAlignment.Left
                ScrollViewer.verticalAlignment VerticalAlignment.Top
                ScrollViewer.content (
                    StackPanel.create [
                        StackPanel.name "content"
                        StackPanel.children [
                            for i in 0..9 -> Border.create [ Border.name $"row{i}"; Border.height 40.0 ]
                        ]
                    ]
                )
            ]
        )
    ]

[<Fact>]
let ``a ScrollViewer shows its content's top, and the wheel scrolls it within its ends`` () =
    let outer = List<float>()
    let w, host = start [ App.view (scroller outer) ]
    let scrollerEl, content = named host "scroller", named host "content"
    Assert.Equal(100.0, scrollerEl.Bounds.Height)
    Assert.Equal(400.0, content.Bounds.Height)
    Assert.Equal(0.0, content.Bounds.Y)
    wheel w 50.0 50.0 -1.0 // towards the user: down by one step
    w.DeliverFrame() |> ignore
    Assert.Equal(-48.0, content.Bounds.Y)
    wheel w 50.0 50.0 -100.0 // far past the end: stops at 300 (400 - 100)
    w.DeliverFrame() |> ignore
    Assert.Equal(-300.0, content.Bounds.Y)
    Assert.Empty outer // the scroller used the wheel both times
    wheel w 50.0 50.0 -1.0 // at the end already: the wheel goes on to the outer element
    Assert.Equal<float list>([ -1.0 ], List.ofSeq outer)
    wheel w 50.0 50.0 1.0
    w.DeliverFrame() |> ignore
    Assert.Equal(-252.0, content.Bounds.Y)

[<Fact>]
let ``scrolled-away content isn't hit outside the ScrollViewer`` () =
    let w, host = start [ App.view (scroller (List<float>())) ]
    wheel w 50.0 50.0 -2.0 // rows 0–2 are now above the viewport
    w.DeliverFrame() |> ignore
    // Row 2 sits at y -16..24 relative to the scroller: only its visible part is hit.
    Assert.Equal("row2", (host.Tree.HitTest(50.0, 10.0)).Value.Name)
    // Row 7 is below the viewport (y 184..224 in the window) and must not catch the pointer there.
    Assert.Equal("", (host.Tree.HitTest(50.0, 200.0)).Value.Name)

[<Fact>]
let ``a ScrollViewer keeps its offset within range when its content shrinks`` () =
    let w, host = start [ App.view (scroller (List<float>())) ]
    wheel w 50.0 50.0 -100.0
    w.DeliverFrame() |> ignore
    let content = named host "content"
    // Make the content short: 5 rows of 40 = 200, so the furthest offset is 100.
    for i in 5..9 do
        (named host $"row{i}").Set(LayoutProps.isVisible, false)
    w.DeliverFrame() |> ignore
    Assert.Equal(-100.0, content.Bounds.Y)


// --- Touch panning -------------------------------------------------------------------------------

let private touchDown (w: FakeWindow) x y = w.Send(PointerPressed(Touch, 1, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private touchMove (w: FakeWindow) x y = w.Send(PointerMoved(Touch, 1, { X = x; Y = y }, Modifiers.None))
let private touchUp (w: FakeWindow) x y =
    w.Send(PointerReleased(Touch, 1, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    w.Send(PointerExited(Touch, 1))

/// The 200×100 scroller again, with ten 40-high buttons that log their clicks, and an optional
/// pointer-down handler on the first one.
let private buttonScroller (clicks: List<string>) (takesPress: bool) =
    ScrollViewer.create [
        ScrollViewer.name "scroller"
        ScrollViewer.width 200.0
        ScrollViewer.height 100.0
        ScrollViewer.horizontalAlignment HorizontalAlignment.Left
        ScrollViewer.verticalAlignment VerticalAlignment.Top
        ScrollViewer.content (
            StackPanel.create [
                StackPanel.name "content"
                StackPanel.children [
                    for i in 0..9 ->
                        Button.create [
                            Button.name $"row{i}"
                            Button.height 40.0
                            Button.horizontalAlignment HorizontalAlignment.Stretch
                            Button.onClick (fun _ -> clicks.Add $"row{i}")
                            if takesPress && i = 0 then Button.onPointerDown (fun e -> e.Handle())
                        ]
                ]
            ]
        )
    ]

[<Fact>]
let ``a finger dragging past the threshold scrolls a ScrollViewer and cancels the press under it`` () =
    let clicks = List<string>()
    let w, host = start [ App.view (buttonScroller clicks false) ]
    let content, row0 = named host "content", named host "row0"
    touchDown w 50.0 30.0
    Assert.True row0.IsPressed
    touchMove w 50.0 25.0 // 5 DIPs: still a press
    w.DeliverFrame() |> ignore
    Assert.Equal(0.0, content.Bounds.Y)
    Assert.True row0.IsPressed
    touchMove w 50.0 10.0 // 20 DIPs: a pan; the content catches up with the finger
    w.DeliverFrame() |> ignore
    Assert.Equal(-20.0, content.Bounds.Y)
    Assert.False row0.IsPressed
    touchMove w 50.0 -40.0 // off the scroller: it keeps the finger
    w.DeliverFrame() |> ignore
    Assert.Equal(-70.0, content.Bounds.Y)
    touchMove w 50.0 -400.0 // past the end: stops there
    w.DeliverFrame() |> ignore
    Assert.Equal(-300.0, content.Bounds.Y)
    touchUp w 50.0 -400.0
    Assert.Empty clicks

[<Fact>]
let ``a short touch drag stays a tap`` () =
    let clicks = List<string>()
    let w, host = start [ App.view (buttonScroller clicks false) ]
    touchDown w 50.0 30.0
    touchMove w 53.0 25.0
    touchUp w 53.0 25.0
    w.DeliverFrame() |> ignore
    Assert.Equal(0.0, (named host "content").Bounds.Y)
    Assert.Equal<string list>([ "row0" ], List.ofSeq clicks)

[<Fact>]
let ``a vertical drag at an end is still a scroll, and what the finger slides over doesn't react`` () =
    let clicks = List<string>()
    let w, host = start [ App.view (buttonScroller clicks false) ]
    touchDown w 50.0 10.0
    touchMove w 50.0 35.0 // down at the top: nothing above to show, but the scroller takes the finger
    touchMove w 50.0 75.0 // over row1 now
    w.DeliverFrame() |> ignore
    Assert.Equal(0.0, (named host "content").Bounds.Y)
    Assert.False (named host "row0").IsPressed
    Assert.False (named host "row1").IsPointerOver
    touchUp w 50.0 75.0
    Assert.Empty clicks

[<Fact>]
let ``a sideways drag stays a press`` () =
    let clicks = List<string>()
    let w, host = start [ App.view (buttonScroller clicks false) ]
    touchDown w 50.0 10.0
    touchMove w 80.0 12.0
    w.DeliverFrame() |> ignore
    Assert.True (named host "row0").IsPressed
    touchUp w 80.0 12.0
    Assert.Equal<string list>([ "row0" ], List.ofSeq clicks)

[<Fact>]
let ``the mouse never pans, and neither does a touch whose press a handler took`` () =
    let clicks = List<string>()
    let w, host = start [ App.view (buttonScroller clicks true) ]
    let content = named host "content"
    press w 50.0 60.0
    move w 50.0 10.0
    release w 50.0 10.0
    w.DeliverFrame() |> ignore
    Assert.Equal(0.0, content.Bounds.Y)
    touchDown w 50.0 30.0 // row0 handles its pointer down
    touchMove w 50.0 0.0
    touchUp w 50.0 0.0
    w.DeliverFrame() |> ignore
    Assert.Equal(0.0, content.Bounds.Y)

// --- Rules found while building the demo ------------------------------------------------------------

[<Fact>]
let ``a modal layer showing nothing blocks nothing`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log) ]
    let dialog = host.AddLayer [ Layer.modal true; Layer.view (Border.create [ Border.isVisible false; Border.child (panel "dialog" log) ]) ]
    w.DeliverFrame() |> ignore
    click w 200.0 150.0
    Assert.Equal<string list>([ "app" ], List.ofSeq log)
    dialog.Update(Border.create [ Border.isVisible true; Border.child (panel "dialog" log) ])
    w.DeliverFrame() |> ignore
    click w 200.0 150.0 // now it blocks
    Assert.Equal<string list>([ "app" ], List.ofSeq log)

[<Fact>]
let ``a component's host element doesn't catch the pointer, only what the component shows`` () =
    let log = List<string>()
    let floating = Component.define "Floating" (fun _ () -> panel "panel" log)
    let w, _ = start [ App.view (appView log); App.layer [ Layer.view (floating ()) ] ]
    click w 200.0 150.0 // outside the panel: the component's host fills the window but isn't hit
    click w 350.0 250.0
    Assert.Equal<string list>([ "app"; "panelButton" ], List.ofSeq log)

[<Fact>]
let ``when a dialog closes, the keyboard goes back to the app and its focus looks active again`` () =
    let log = List<string>()
    let w, host = start [ App.view (appView log) ]
    key w Key.Tab
    let appButton = named host "appButton"
    let dialog = host.AddLayer [ Layer.modal true; Layer.view (panel "dialog" log) ]
    w.DeliverFrame() |> ignore
    Assert.False appButton.IsFocusVisible
    click w 350.0 250.0 // press in the dialog
    dialog.Update(Border.create [ Border.isVisible false ]) // the dialog closes
    w.DeliverFrame() |> ignore
    Assert.True(obj.ReferenceEquals(host.App, host.KeyboardLayer))
    Assert.True appButton.IsFocused
    key w Key.Enter
    Assert.Equal<string list>([ "dialogButton"; "appButton" ], List.ofSeq log)

[<Fact>]
let ``rows scrolled out of a ScrollViewer aren't drawn`` () =
    let tree = ElementTree(Scribble.Tests.TestShaping.textSystem ())
    let mutable drawn = 0
    // Counts its renders (one call per frame it's drawn in).
    let counted =
        Element.define "Counted" [
            Element.measure (fun _ _ -> Size.Create(100.0, 20.0))
            Element.render (fun _ list ->
                drawn <- drawn + 1
                list.FillRect(Rect.Create(0.0, 0.0, 100.0, 20.0), Color.White))
        ]
    tree.Update(
        ScrollViewer.create [
            ScrollViewer.height 100.0
            ScrollViewer.content (StackPanel.create [ StackPanel.children [ for _ in 1 .. 50 -> View.Create(counted, ([]: Setting<Layoutable> list)) ] ])
        ]
    )
    tree.UpdateLayout(Size.Create(200.0, 300.0))
    tree.Render(DrawList(1.0))
    // 5 rows show; rows within 64 DIPs below (shadows and rings may reach in) are drawn too.
    Assert.InRange(drawn, 5, 9)
