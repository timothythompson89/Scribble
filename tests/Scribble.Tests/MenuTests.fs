/// M9 step 9f on the fake platform (DESIGN.md, "9f: the Menu"): a Menu as a flyout and as a context menu, its keys,
/// what opens a context menu (a right-click, the Menu key, Shift+F10, a long press), where it goes, where the focus
/// returns, and the TextBox's own menu.
module Scribble.Tests.MenuTests

open System
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
open Scribble.Tests.AppTests

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

/// Every element in every layer (a menu is in a layer of its own).
let private everything (host: ElementHost) =
    seq {
        for l in host.Layers do
            match l.Tree.Root with
            | ValueSome r -> yield! all r
            | ValueNone -> ()
    }

let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 30 do
        n <- n + 1

let private showApp (settings: AppSetting<'Msg> list) =
    let platform, w, _, app = startAll settings
    w.Size <- { Width = 600; Height = 400 }
    w.Send(Resized w.Size)
    let host = app :?> ElementHost
    settle w
    platform, w, host

let private show (view: View) = showApp [ App.view view ]

let private items (host: ElementHost) = everything host |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, MenuItem.Type)) |> List.ofSeq
let private isOpen (host: ElementHost) = not (items host).IsEmpty
let private item (host: ElementHost) (text: string) = items host |> List.find (fun e -> e.Get TextProps.text = text)
let private named (host: ElementHost) (name: string) = everything host |> Seq.find (fun e -> e.Name = name)
let private focusedText (host: ElementHost) = match host.Focused with ValueSome e -> e.Get TextProps.text | ValueNone -> ""

let private centre (e: Element) : PixelPoint =
    let p = e.PositionInTree
    { X = p.X + e.Bounds.Width / 2.0; Y = p.Y + e.Bounds.Height / 2.0 }

let private clickWith (w: FakeWindow) (button: MouseButton) (at: PixelPoint) =
    w.Time.AdvanceMs 1000.0
    w.Send(PointerPressed(Mouse, 0, at, button, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, button, Modifiers.None))
    settle w

let private click (w: FakeWindow) (e: Element) = clickWith w MouseButton.Left (centre e)
let private rightClick (w: FakeWindow) (e: Element) = clickWith w MouseButton.Right (centre e)

let private keyWith (w: FakeWindow) (mods: Modifiers) (k: Key) =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    settle w

let private key (w: FakeWindow) (k: Key) = keyWith w Modifiers.None k

let private typeText (w: FakeWindow) (text: string) =
    for c in text do
        w.Send(KeyDown(Key.Char(Char.ToLowerInvariant c), Modifiers.None, false))
        w.Send(TextInput(string c))
        w.Send(KeyUp(Key.Char(Char.ToLowerInvariant c), Modifiers.None))
    settle w

/// A menu of five items (one disabled, one checked), and what was chosen.
let private menuOf (chosen: ResizeArray<string>) =
    let pick (text: string) = fun (_: ClickEventArgs) -> chosen.Add text
    Menu.create [
        Menu.children [
            MenuItem.create [ MenuItem.text "Copy"; MenuItem.shortcut "Ctrl+C"; MenuItem.onClick (pick "Copy") ]
            MenuItem.create [ MenuItem.text "Duplicate"; MenuItem.onClick (pick "Duplicate") ]
            MenuItem.create [ MenuItem.text "Rename"; MenuItem.isEnabled false; MenuItem.onClick (pick "Rename") ]
            Menu.separator
            MenuItem.create [ MenuItem.text "Word wrap"; MenuItem.isChecked true; MenuItem.onClick (pick "Word wrap") ]
            MenuItem.create [ MenuItem.text "Delete"; MenuItem.onClick (pick "Delete") ]
        ]
    ]

let private page (chosen: ResizeArray<string>) =
    StackPanel.create [
        StackPanel.horizontalAlignment HorizontalAlignment.Left
        StackPanel.children [
            Button.create [ Button.name "edit"; Button.text "Edit"; Button.flyout (menuOf chosen) ]
            Button.create [ Button.name "other"; Button.text "Other" ]
            Border.create [ Border.name "area"; Border.width 200.0; Border.height 60.0; Border.contextMenu (menuOf chosen) ]
            Border.create [ Border.name "plain"; Border.width 200.0; Border.height 40.0 ]
        ]
    ]

[<Fact>]
let ``a Menu given as a button's flyout opens on a click, and choosing an item runs it and closes the menu`` () =
    let chosen = ResizeArray<string>()
    let _, w, host = show (page chosen)
    Assert.False(isOpen host)
    click w (named host "edit")
    Assert.True(isOpen host)
    Assert.Equal<string list>([ "Copy"; "Duplicate"; "Rename"; "Word wrap"; "Delete" ], items host |> List.map (fun e -> e.Get TextProps.text))
    click w (item host "Duplicate")
    Assert.Equal<string list>([ "Duplicate" ], List.ofSeq chosen)
    Assert.False(isOpen host)
    // The focus is back on the button.
    Assert.True((named host "edit").IsFocused)
    // A disabled item can't be chosen, and the menu stays.
    click w (named host "edit")
    click w (item host "Rename")
    Assert.Equal<string list>([ "Duplicate" ], List.ofSeq chosen)
    Assert.True(isOpen host)
    // A press elsewhere (clear of the menu) closes it with nothing chosen.
    clickWith w MouseButton.Left { X = 550.0; Y = 350.0 }
    Assert.False(isOpen host)
    Assert.Equal<string list>([ "Duplicate" ], List.ofSeq chosen)

[<Fact>]
let ``arrows move through a menu's items round its ends, skipping what can't be chosen; Enter chooses`` () =
    let chosen = ResizeArray<string>()
    let _, w, host = show (page chosen)
    // Opened with the keyboard (Tab to the button, Space): the first item is the one the keyboard is on.
    key w Key.Tab
    key w Key.Space
    Assert.True(isOpen host)
    Assert.Equal("Copy", focusedText host)
    Assert.True((item host "Copy").IsFocusVisible)
    key w Key.Down
    Assert.Equal("Duplicate", focusedText host)
    // "Rename" is disabled: on to "Word wrap".
    key w Key.Down
    Assert.Equal("Word wrap", focusedText host)
    key w Key.Down
    key w Key.Down
    Assert.Equal("Copy", focusedText host)
    key w Key.Up
    Assert.Equal("Delete", focusedText host)
    key w Key.Home
    Assert.Equal("Copy", focusedText host)
    key w Key.End
    Assert.Equal("Delete", focusedText host)
    // A letter goes to the item that starts with it.
    typeText w "w"
    Assert.Equal("Word wrap", focusedText host)
    key w Key.Enter
    Assert.Equal<string list>([ "Word wrap" ], List.ofSeq chosen)
    Assert.False(isOpen host)
    Assert.True((named host "edit").IsFocused)

[<Fact>]
let ``opened by the mouse no item is shown as current, and the first arrow starts from an end`` () =
    let chosen = ResizeArray<string>()
    let _, w, host = show (page chosen)
    click w (named host "edit")
    Assert.False(items host |> List.exists (fun e -> e.IsFocusVisible))
    key w Key.Down
    Assert.Equal("Copy", focusedText host)
    Assert.True((item host "Copy").IsFocusVisible)
    key w Key.Escape
    Assert.False(isOpen host)
    click w (named host "edit")
    key w Key.Up
    Assert.Equal("Delete", focusedText host)
    // Tab closes it with nothing chosen.
    key w Key.Tab
    Assert.False(isOpen host)
    Assert.Empty chosen

[<Fact>]
let ``a right-click opens an element's context menu at the pointer, kept inside the window`` () =
    let chosen = ResizeArray<string>()
    let _, w, host = show (page chosen)
    let area = named host "area"
    let at = centre area
    clickWith w MouseButton.Right at
    Assert.True(isOpen host)
    // Its top left corner is at the pointer (just below it), not beside the element.
    let menu = everything host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Flyout.Type))
    let p = menu.PositionInTree
    Assert.InRange(p.X, at.X - 1.0, at.X + 2.0)
    Assert.InRange(p.Y, at.Y, at.Y + 8.0)
    click w (item host "Delete")
    Assert.Equal<string list>([ "Delete" ], List.ofSeq chosen)
    Assert.False(isOpen host)
    // An element with no menu, and none around it: nothing opens.
    rightClick w (named host "plain")
    Assert.False(isOpen host)
    // A left click doesn't open a context menu.
    click w area
    Assert.False(isOpen host)
    // Near the window's bottom right corner the menu is pushed back in.
    let corner =
        Canvas.create [
            Canvas.children [
                Border.create [ Border.name "corner"; Canvas.left 560.0; Canvas.top 360.0; Border.width 40.0; Border.height 40.0; Border.contextMenu (menuOf chosen) ]
            ]
        ]
    let _, w, host = show corner
    rightClick w (named host "corner")
    let menu = everything host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Flyout.Type))
    let p = menu.PositionInTree
    Assert.True(p.X + menu.Bounds.Width <= 600.0)
    Assert.True(p.Y + menu.Bounds.Height <= 400.0)
    Assert.True(p.X >= 0.0 && p.Y >= 0.0)

[<Fact>]
let ``the nearest element with a context menu shows its own`` () =
    let outer, inner = ResizeArray<string>(), ResizeArray<string>()
    let one (name: string) (log: ResizeArray<string>) = Menu.create [ Menu.children [ MenuItem.create [ MenuItem.text name; MenuItem.onClick (fun _ -> log.Add name) ] ] ]
    let view =
        Border.create [
            Border.name "outer"
            Border.padding 20.0
            Border.horizontalAlignment HorizontalAlignment.Left
            Border.verticalAlignment VerticalAlignment.Top
            Border.contextMenu (one "outer item" outer)
            Border.content (
                StackPanel.create [
                    StackPanel.children [
                        Border.create [ Border.name "inner"; Border.width 100.0; Border.height 40.0; Border.contextMenu (one "inner item" inner) ]
                        Border.create [ Border.name "bare"; Border.width 100.0; Border.height 40.0 ]
                    ]
                ])
        ]
    let _, w, host = show view
    rightClick w (named host "inner")
    Assert.Equal<string list>([ "inner item" ], items host |> List.map (fun e -> e.Get TextProps.text))
    key w Key.Escape
    // Something with none of its own: the one around it.
    rightClick w (named host "bare")
    Assert.Equal<string list>([ "outer item" ], items host |> List.map (fun e -> e.Get TextProps.text))

[<Fact>]
let ``the Menu key and Shift+F10 open the focused element's context menu, and closing it gives the focus back`` () =
    let chosen = ResizeArray<string>()
    let view =
        StackPanel.create [
            StackPanel.horizontalAlignment HorizontalAlignment.Left
            StackPanel.children [
                Button.create [ Button.name "with"; Button.text "With a menu"; Button.contextMenu (menuOf chosen) ]
                Button.create [ Button.name "without"; Button.text "Without" ]
            ]
        ]
    let _, w, host = show view
    key w Key.Tab
    Assert.True((named host "with").IsFocused)
    key w Key.Menu
    Assert.True(isOpen host)
    // By the keyboard: the first item is current.
    Assert.Equal("Copy", focusedText host)
    Assert.True((item host "Copy").IsFocusVisible)
    // It opened at the button, not at the window's corner.
    let button = named host "with"
    let menu = everything host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Flyout.Type))
    Assert.InRange(menu.PositionInTree.Y, button.PositionInTree.Y, button.PositionInTree.Y + button.Bounds.Height + 8.0)
    key w Key.Escape
    Assert.False(isOpen host)
    Assert.True((named host "with").IsFocused)
    keyWith w Modifiers.Shift (Key.F 10)
    Assert.True(isOpen host)
    key w Key.Down
    key w Key.Enter
    Assert.Equal<string list>([ "Duplicate" ], List.ofSeq chosen)
    Assert.True((named host "with").IsFocused)
    // Nothing to show for a button without one.
    key w Key.Tab
    key w Key.Menu
    Assert.False(isOpen host)

[<Fact>]
let ``a finger held still opens the context menu and its lift is no click; moved or lifted early, it doesn't`` () =
    let chosen = ResizeArray<string>()
    let clicks = ResizeArray<string>()
    let view =
        StackPanel.create [
            StackPanel.horizontalAlignment HorizontalAlignment.Left
            StackPanel.children [
                Button.create [ Button.name "target"; Button.text "Hold me"; Button.onClick (fun _ -> clicks.Add "click"); Button.contextMenu (menuOf chosen) ]
            ]
        ]
    let _, w, host = show view
    let at = centre (named host "target")
    let down () = w.Send(PointerPressed(Touch, 1, at, MouseButton.Left, Modifiers.None))
    let up (p: PixelPoint) =
        w.Send(PointerReleased(Touch, 1, p, MouseButton.Left, Modifiers.None))
        w.Send(PointerExited(Touch, 1))
        settle w
    // A tap: a click, no menu.
    down ()
    w.Time.AdvanceMs 100.0
    up at
    Assert.Equal(1, clicks.Count)
    Assert.False(isOpen host)
    // Held for half a second: the menu, and lifting the finger doesn't click.
    w.Time.AdvanceMs 1000.0
    down ()
    let pressedAt = w.Time.Now
    settle w
    Assert.False(isOpen host)
    // (Frames move the fake clock too, so the time is read from it.)
    let mutable steps = 0
    while not (isOpen host) && steps < 200 do
        w.Time.AdvanceMs 10.0
        settle w
        steps <- steps + 1
    Assert.True(isOpen host)
    // Half a second, then the frames in which the menu's layer is made and laid out.
    Assert.InRange((w.Time.Now - pressedAt).TotalMilliseconds, 500.0, 800.0)
    up at
    Assert.Equal(1, clicks.Count)
    Assert.True(isOpen host)
    click w (item host "Copy")
    Assert.Equal<string list>([ "Copy" ], List.ofSeq chosen)
    // Moved while held: no menu.
    w.Time.AdvanceMs 1000.0
    down ()
    w.Send(PointerMoved(Touch, 1, { X = at.X + 30.0; Y = at.Y }, Modifiers.None))
    w.Time.AdvanceMs 600.0
    settle w
    Assert.False(isOpen host)
    up { X = at.X + 30.0; Y = at.Y }

[<Fact>]
let ``a menu and its items are a Menu and MenuItems to a screen reader, with what is checked and disabled`` () =
    let chosen = ResizeArray<string>()
    let _, w, host = show (page chosen)
    click w (named host "edit")
    let tree = host.SemanticTree().ToString()
    Assert.Contains("Menu", tree)
    Assert.Contains("MenuItem \"Copy\"", tree)
    Assert.Contains("MenuItem \"Rename\" disabled", tree)
    Assert.Contains("MenuItem \"Word wrap\" checked", tree)
    // A screen reader's click chooses.
    let node = (host.SemanticTree().Find "MenuItem \"Delete\"").Value
    Assert.True(node.Perform SemanticAction.Click)
    settle w
    Assert.Equal<string list>([ "Delete" ], List.ofSeq chosen)
    Assert.False(isOpen host)

// ---- The TextBox's own menu ----

let private boxPage (settings: Setting<TextBox> list) =
    StackPanel.create [
        StackPanel.horizontalAlignment HorizontalAlignment.Left
        StackPanel.children [ TextBox.create ([ TextBox.name "box"; TextBox.fontSize 20.0; TextBox.width 326.0 ] @ settings) ]
    ]

let private textOf (host: ElementHost) = (TextBox.editOf (named host "box")).Text
let private selection (host: ElementHost) = let s = TextBox.editOf (named host "box") in s.Anchor, s.Caret
let private enabled (host: ElementHost) = items host |> List.filter (fun e -> e.IsEnabled) |> List.map (fun e -> e.Get TextProps.text)

/// A right-click on character `index` of the box (10 DIPs a character from x = 13).
let private rightClickAt (w: FakeWindow) (index: int) = clickWith w MouseButton.Right { X = 13.0 + 10.0 * float index + 2.0; Y = 15.0 }

[<Fact>]
let ``a TextBox has a menu of its own: each item is enabled when it applies, and does what its key does`` () =
    let platform, w, host = show (boxPage [])
    // Empty and unfocused: only Paste can do anything.
    platform.Clipboard <- "hello world"
    rightClickAt w 0
    Assert.Equal<string list>([ "Undo"; "Redo"; "Cut"; "Copy"; "Paste"; "Select all" ], items host |> List.map (fun e -> e.Get TextProps.text))
    Assert.Equal<string list>([ "Paste" ], enabled host)
    Assert.Equal("Ctrl+V", (item host "Paste").Get MenuItemProps.shortcut)
    click w (item host "Paste")
    Assert.Equal("hello world", textOf host)
    Assert.False(isOpen host)
    // The box has the focus back.
    Assert.True((named host "box").IsFocused)
    // With text but no selection: Undo, Paste, Select all.
    rightClickAt w 3
    Assert.Equal((3, 3), selection host)
    Assert.Equal<string list>([ "Undo"; "Paste"; "Select all" ], enabled host)
    click w (item host "Select all")
    Assert.Equal((0, 11), selection host)
    // A right-click inside the selection keeps it: Cut and Copy are about it.
    rightClickAt w 4
    Assert.Equal((0, 11), selection host)
    Assert.Equal<string list>([ "Undo"; "Cut"; "Copy"; "Paste"; "Select all" ], enabled host)
    // The menu is in a layer of its own: the box keeps its focus, so its selection stays on show.
    Assert.True((named host "box").IsFocused)
    platform.Clipboard <- ""
    click w (item host "Cut")
    Assert.Equal("", textOf host)
    Assert.Equal("hello world", platform.Clipboard)
    // Undo and Redo.
    rightClickAt w 0
    Assert.Equal<string list>([ "Undo"; "Paste" ], enabled host)
    click w (item host "Undo")
    Assert.Equal("hello world", textOf host)
    rightClickAt w 0
    Assert.Contains("Redo", enabled host)
    click w (item host "Redo")
    Assert.Equal("", textOf host)

[<Fact>]
let ``a password's menu never copies or cuts, a read-only box's only copies, and the Menu key opens it at the caret`` () =
    let _, w, host = show (boxPage [ TextBox.text "secret"; TextBox.isPassword true ])
    key w Key.Tab
    Assert.Equal((0, 6), selection host)
    key w Key.Menu
    Assert.True(isOpen host)
    Assert.Equal<string list>([ "Paste"; "Select all" ], enabled host)
    key w Key.Escape
    Assert.True((named host "box").IsFocused)
    let platform, w, host = show (boxPage [ TextBox.text "fixed text"; TextBox.isReadOnly true ])
    key w Key.Tab
    keyWith w Modifiers.Shift (Key.F 10)
    Assert.Equal<string list>([ "Copy"; "Select all" ], enabled host)
    // Opened by the keyboard: at the caret (the text's end here), not at the box's corner.
    let menu = everything host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Flyout.Type))
    Assert.InRange(menu.PositionInTree.X, 13.0 + 100.0 - 2.0, 13.0 + 100.0 + 3.0)
    // The first item that can be chosen is current.
    Assert.Equal("Copy", focusedText host)
    key w Key.Enter
    Assert.Equal("fixed text", platform.Clipboard)

[<Fact>]
let ``an app's contextMenu replaces a TextBox's own, and its words can be translated`` () =
    let chosen = ResizeArray<string>()
    let _, w, host = show (boxPage [ TextBox.contextMenu (menuOf chosen) ])
    rightClickAt w 0
    Assert.Equal("Copy", (items host).Head.Get TextProps.text)
    Assert.Equal(5, (items host).Length)
    key w Key.Escape
    let french = { TextBoxMenuLabels.English with Cut = "Couper"; Copy = "Copier"; Paste = "Coller"; SelectAll = "Tout sélectionner" }
    let _, w, host = showApp [ App.value TextBoxMenu.labels french; App.view (boxPage []) ]
    rightClickAt w 0
    Assert.Equal<string list>([ "Undo"; "Redo"; "Couper"; "Copier"; "Coller"; "Tout sélectionner" ], items host |> List.map (fun e -> e.Get TextProps.text))
