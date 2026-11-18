/// Keyboard and focus (M5 step 2): Tab order, focus from presses, the focus ring state, routed key
/// events, Enter/Space activation and App.onKey seeing only unhandled keys, on the fake platform.
module Scribble.Tests.KeyboardTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests

let private down (w: FakeWindow) key = w.Send(KeyDown(key, Modifiers.None, false))
let private up (w: FakeWindow) key = w.Send(KeyUp(key, Modifiers.None))
let private tab (w: FakeWindow) = down w Key.Tab; up w Key.Tab
let private shiftTab (w: FakeWindow) = w.Send(KeyDown(Key.Tab, Modifiers.Shift, false))

let private find name (host: ElementHost) =
    let rec go (e: Element) = if e.Name = name then Some e else e.Children |> Seq.tryPick go
    (go host.Tree.Root.Value).Value

let private focusedName (host: ElementHost) =
    match host.Focused with
    | ValueSome e -> e.Name
    | ValueNone -> "(none)"

/// Presses and releases the left button in the middle of an element.
let private clickOn (w: FakeWindow) (e: Element) =
    let p = e.PositionInTree
    let at: PixelPoint = { X = p.X + e.Bounds.Width / 2.0; Y = p.Y + e.Bounds.Height / 2.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))

/// A column: button "one", a plain border, button "two", a disabled button "off", button "three".
let private buttons (clicks: List<string>) (extra: Setting<StackPanel> list) =
    let button name enabled =
        Button.create [
            Button.name name
            Button.text name
            Button.isEnabled enabled
            Button.onClick (fun _ -> clicks.Add name)
        ]
    StackPanel.create (
        [ StackPanel.name "column"
          StackPanel.children [
              button "one" true
              Border.create [ Border.name "plain"; Border.height 20.0 ]
              button "two" true
              button "off" false
              button "three" true
          ] ]
        @ extra)

let private show (view: View) settings =
    let w, _, app = start ([ App.view view ] @ settings)
    w.DeliverFrame() |> ignore
    w, (app :?> ElementHost)

[<Fact>]
let ``Tab walks focusable elements in view order and wraps, Shift+Tab goes back`` () =
    let w, host = show (buttons (List()) []) []
    Assert.Equal("(none)", focusedName host)
    let seen = [ for _ in 1..4 -> tab w; focusedName host ]
    Assert.Equal<string list>([ "one"; "two"; "three"; "one" ], seen)
    shiftTab w
    Assert.Equal("three", focusedName host)

[<Fact>]
let ``Tab order is view order, not position: a DockPanel's Fill child comes where it's written`` () =
    let button name (dock: Dock) = Button.create [ Button.name name; Button.text name; DockPanel.dock dock ]
    let w, host =
        show (DockPanel.create [ DockPanel.children [ button "top" Dock.Top; button "content" Dock.Fill; button "bottom" Dock.Bottom ] ]) []
    Assert.Equal<string list>([ "top"; "content"; "bottom"; "top" ], [ for _ in 1..4 -> tab w; focusedName host ])
    // Written the other way (the last child fills), Tab takes the order written, against the one shown.
    let w, host =
        show (DockPanel.create [ DockPanel.children [ button "top" Dock.Top; button "bottom" Dock.Bottom; button "content" Dock.Left ] ]) []
    Assert.True((find "content" host).PositionInTree.Y < (find "bottom" host).PositionInTree.Y)
    Assert.Equal<string list>([ "top"; "bottom"; "content" ], [ for _ in 1..3 -> tab w; focusedName host ])

[<Fact>]
let ``the ring shows after Tab, not after a press, and hides while the window is inactive`` () =
    let w, host = show (buttons (List()) []) []
    let two = find "two" host
    clickOn w two
    Assert.True two.IsFocused
    Assert.False two.IsFocusVisible
    tab w
    let three = find "three" host
    Assert.True three.IsFocusVisible
    Assert.False two.IsFocused
    w.Send(FocusChanged false)
    Assert.False three.IsFocusVisible
    Assert.True three.IsFocused
    w.Send(FocusChanged true)
    Assert.True three.IsFocusVisible

[<Fact>]
let ``a press on something not focusable clears focus`` () =
    let w, host = show (buttons (List()) []) []
    tab w
    Assert.Equal("one", focusedName host)
    clickOn w (find "plain" host)
    Assert.Equal("(none)", focusedName host)

[<Fact>]
let ``Enter clicks the focused button, repeats don't`` () =
    let clicks = List<string>()
    let w, _ = show (buttons clicks []) []
    tab w
    down w Key.Enter
    w.Send(KeyDown(Key.Enter, Modifiers.None, true))
    Assert.Equal<string list>([ "one" ], List.ofSeq clicks)

[<Fact>]
let ``Space looks pressed while held and clicks on release, Escape cancels`` () =
    let clicks = List<string>()
    let w, host = show (buttons clicks []) []
    tab w
    let one = find "one" host
    down w Key.Space
    w.Send(KeyDown(Key.Space, Modifiers.None, true))
    Assert.True one.IsPressed
    Assert.Empty clicks
    up w Key.Space
    Assert.False one.IsPressed
    Assert.Equal<string list>([ "one" ], List.ofSeq clicks)
    down w Key.Space
    down w Key.Escape
    Assert.False one.IsPressed
    up w Key.Space
    Assert.Equal<string list>([ "one" ], List.ofSeq clicks)

[<Fact>]
let ``keys tunnel from the root and bubble from the focused element, handling stops built-ins`` () =
    let log = List<string>()
    let clicks = List<string>()
    let view =
        buttons clicks [
            StackPanel.onPreviewKeyDown (fun e -> log.Add "preview column")
            StackPanel.onKeyDown (fun e ->
                log.Add "column"
                if e.Key = Key.Enter then e.Handle())
        ]
    let w, _ = show view []
    down w (Key.Char 'x') // nothing focused: goes to the root
    tab w
    log.Clear()
    down w Key.Enter
    Assert.Equal<string list>([ "preview column"; "column" ], List.ofSeq log)
    Assert.Empty clicks // the column handled Enter, so the button didn't click

[<Fact>]
let ``App.onKey gets only keys nothing handled`` () =
    let keys = List<Key>()
    let clicks = List<string>()
    let w, _ = show (buttons clicks []) [ App.onKey (fun key _ -> keys.Add key; None) ]
    down w Key.Space // nothing focused: the app's
    tab w // Tab moved focus: not the app's
    down w Key.Space // the focused button's
    up w Key.Space
    down w (Key.Char 'a') // nobody wants it: the app's
    Assert.Equal<Key list>([ Key.Space; Key.Char 'a' ], List.ofSeq keys)
    Assert.Equal<string list>([ "one" ], List.ofSeq clicks)

[<Fact>]
let ``disabling, hiding or removing the focused element clears focus`` () =
    let w, host = show (buttons (List()) []) []
    tab w
    let one = find "one" host
    one.Set(LayoutProps.isEnabled, false)
    w.DeliverFrame() |> ignore
    Assert.Equal("(none)", focusedName host)
    Assert.False one.IsFocused
    tab w
    Assert.Equal("two", focusedName host)
    (find "two" host).Set(LayoutProps.isVisible, false)
    w.DeliverFrame() |> ignore
    Assert.Equal("(none)", focusedName host)
    tab w
    Assert.Equal("three", focusedName host)
    host.Update(StackPanel.create [ StackPanel.name "column" ])
    w.DeliverFrame() |> ignore
    Assert.Equal("(none)", focusedName host)

[<Fact>]
let ``Shift+Enter and Shift+Space click with Shift in the args, Ctrl+Enter doesn't click`` () =
    let clicks = List<ClickEventArgs>()
    let w, _ =
        show (Button.create [ Button.text "Go"; Button.onClick clicks.Add ]) []
    tab w
    w.Send(KeyDown(Key.Enter, Modifiers.Shift, false))
    w.Send(KeyDown(Key.Space, Modifiers.Shift, false))
    w.Send(KeyUp(Key.Space, Modifiers.Shift))
    w.Send(KeyDown(Key.Enter, Modifiers.Control, false))
    Assert.Equal(2, clicks.Count)
    for e in clicks do
        Assert.Equal(ClickSource.Keyboard, e.Source)
        Assert.True(e.Modifiers.HasFlag Modifiers.Shift)
