/// M9 step 9g on the fake platform (DESIGN.md, "9g: selection by touch"): a finger's long press and double-tap
/// selecting a word, the selection handles and what dragging them does, the context menu as a row and what its
/// commands leave behind, and what puts them all away. The stand-in shaper makes every UTF-16 unit 10 DIPs wide at
/// size 20.
module Scribble.Tests.TextBoxTouchTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Tests.AppTests
open Scribble.Tests.TestShaping

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

/// Every element in every layer (the handles and the row are in a layer of the box's own).
let private everything (host: ElementHost) =
    seq {
        for l in host.Layers do
            match l.Tree.Root with
            | ValueSome r -> yield! all r
            | ValueNone -> ()
    }

let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 20 do
        n <- n + 1

let private box (settings: Setting<TextBox> list) =
    TextBox.create ([ TextBox.fontSize 20.0; TextBox.width 226.0; TextBox.horizontalAlignment HorizontalAlignment.Left ] @ settings)

/// A TextBox (text area 200 DIPs wide from x = 13, its line from y = 8) above a button.
let private page (settings: Setting<TextBox> list) =
    StackPanel.create [
        StackPanel.children [ box settings; Button.create [ Button.name "next"; Button.text "Next"; Button.horizontalAlignment HorizontalAlignment.Left ] ]
    ]

let private showIn (width: int) (view: View) =
    let platform, w, _, app = startAll [ App.view view ]
    w.Size <- { Width = width; Height = 600 }
    w.Send(Resized w.Size)
    let host = app :?> ElementHost
    settle w
    platform, w, host

let private show (view: View) = showIn 400 view

/// A box that keeps its own text (so what a test types, cuts or pastes stays), holding `text`.
let private showTyped (text: string) =
    let platform, w, host = show (page [])
    w.Send(PointerPressed(Mouse, 0, { X = 20.0; Y = 15.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 20.0; Y = 15.0 }, MouseButton.Left, Modifiers.None))
    w.Send(TextInput text)
    settle w
    platform, w, host

let private boxOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TextBox.Type))
let private stateOf (host: ElementHost) = TextBox.stateOf (boxOf host)
let private textOf (host: ElementHost) = (TextBox.editOf (boxOf host)).Text
let private selection (host: ElementHost) = let s = TextBox.editOf (boxOf host) in s.Start, s.End
let private lineHeight () = (textSystem ()).LineHeight(Fonts.Regular(), 20.0)
let private xOf (index: int) = 13.0 + 10.0 * float index
/// The middle of the first line.
let private midY () = 8.0 + lineHeight () / 2.0

let private handles (host: ElementHost) = everything host |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, SelectionHandle.Type)) |> List.ofSeq
let private handle (host: ElementHost) (kind: SelectionHandleKind) = handles host |> List.tryFind (fun e -> e.Get SelectionHandleProps.kind = kind)
let private kinds (host: ElementHost) = handles host |> List.map (fun e -> e.Get SelectionHandleProps.kind)
/// Where a handle's point is in the window.
let private pointOf (host: ElementHost) (kind: SelectionHandleKind) =
    let e = (handle host kind).Value
    let k = SelectionHandle.pointIn kind
    let p = e.PositionInTree
    p.X + k.X, p.Y + k.Y
/// The middle of a handle: where a finger takes it.
let private grip (host: ElementHost) (kind: SelectionHandleKind) =
    let e = (handle host kind).Value
    let p = e.PositionInTree
    p.X + e.Bounds.Width / 2.0, p.Y + e.Bounds.Height / 2.0

let private rowItems (host: ElementHost) = everything host |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, MenuItem.Type)) |> List.ofSeq
let private row (host: ElementHost) = rowItems host |> List.map (fun e -> e.Get TextProps.text)
let private menuOf (host: ElementHost) = everything host |> Seq.tryFind (fun e -> obj.ReferenceEquals(e.Type, Menu.Type))

let private down (w: FakeWindow) (id: int) (x: float, y: float) = w.Send(PointerPressed(Touch, id, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private move (w: FakeWindow) (id: int) (x: float, y: float) =
    w.Send(PointerMoved(Touch, id, { X = x; Y = y }, Modifiers.None))
    settle w
let private up (w: FakeWindow) (id: int) (x: float, y: float) =
    w.Send(PointerReleased(Touch, id, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    w.Send(PointerExited(Touch, id))
    settle w

/// A tap, long after the one before (so never a double-tap).
let private tap (w: FakeWindow) (at: float * float) =
    w.Time.AdvanceMs 1000.0
    down w 1 at
    up w 1 at

/// A finger goes down and stays for longer than a long press takes.
let private press (w: FakeWindow) (at: float * float) =
    w.Time.AdvanceMs 1000.0
    down w 1 at
    settle w
    w.Time.AdvanceMs 600.0
    settle w

let private longPress (w: FakeWindow) (at: float * float) =
    press w at
    up w 1 at

/// A finger chooses one of the row's items.
let private choose (w: FakeWindow) (host: ElementHost) (text: string) =
    let e = rowItems host |> List.find (fun e -> e.Get TextProps.text = text)
    let p = e.PositionInTree
    tap w (p.X + e.Bounds.Width / 2.0, p.Y + e.Bounds.Height / 2.0)

[<Fact>]
let ``a long press on a word selects it with a handle at each end, and the row shows when the finger lifts`` () =
    let lh = lineHeight ()
    let _, w, host = show (page [ TextBox.text "hello brave world" ])
    // On the "a" of "brave".
    let at = xOf 8 + 5.0, midY ()
    press w at
    Assert.True((boxOf host).IsFocused)
    Assert.Equal((6, 11), selection host)
    Assert.Equal<SelectionHandleKind list>([ SelectionHandleKind.Start; SelectionHandleKind.End ], kinds host)
    // Each handle's point is the foot of its end of the selection (on whole pixels, as it is drawn).
    Assert.Equal((xOf 6, 8.0 + Math.Round lh), pointOf host SelectionHandleKind.Start)
    Assert.Equal((xOf 11, 8.0 + Math.Round lh), pointOf host SelectionHandleKind.End)
    // No row while the finger is down; and nothing asks for frames while it rests (the power rule).
    Assert.False w.FrameRequested
    Assert.Empty(row host)
    up w 1 at
    // Lifting it is no tap: the word stays selected, and the row offers what applies.
    Assert.Equal((6, 11), selection host)
    Assert.Equal<string list>([ "Cut"; "Copy"; "Paste"; "Select all" ], row host)
    // The row is a Menu laid out sideways, above the text when there is room (here there is none: below the handles).
    let menu = (menuOf host).Value
    Assert.Equal(Horizontal, menu.Get StackProps.orientation)
    let items = rowItems host
    Assert.True(items[1].PositionInTree.X > items[0].PositionInTree.X)
    Assert.Equal(items[0].PositionInTree.Y, items[1].PositionInTree.Y)
    Assert.True(menu.PositionInTree.Y > 8.0 + lh)
    // The box kept the keyboard: the handles' layer never takes it.
    Assert.True(obj.ReferenceEquals(host.Focused.Value, boxOf host))

[<Fact>]
let ``a finger that stays down after a long press extends the selection by words`` () =
    let _, w, host = show (page [ TextBox.text "hello brave world" ])
    press w (xOf 8, midY ())
    Assert.Equal((6, 11), selection host)
    // Into "world": the whole word.
    move w 1 (xOf 14, midY ())
    Assert.Equal((6, 17), selection host)
    // Back before "brave": "hello" comes in, and the first word stays.
    move w 1 (xOf 2, midY ())
    Assert.Equal((0, 11), selection host)
    up w 1 (xOf 2, midY ())
    Assert.Equal((0, 11), selection host)
    Assert.False((stateOf host).Dragging)
    Assert.NotEmpty(row host)

[<Fact>]
let ``a long press on blank space places the caret and offers what applies there`` () =
    let _, w, host = show (page [ TextBox.text "hello" ])
    // Far past the text's end.
    longPress w (xOf 15, midY ())
    Assert.Equal((5, 5), selection host)
    Assert.Equal<string list>([ "Paste"; "Select all" ], row host)
    Assert.Equal<SelectionHandleKind list>([ SelectionHandleKind.Caret ], kinds host)
    // An empty box: only Paste.
    let _, w, host = show (page [])
    longPress w (xOf 3, midY ())
    Assert.Equal<string list>([ "Paste" ], row host)

[<Fact>]
let ``a double-tap selects the word; a tap inside the selection toggles the row; a tap elsewhere leaves a caret with its handle`` () =
    let lh = lineHeight ()
    let _, w, host = show (page [ TextBox.text "hello brave world" ])
    let s = stateOf host
    tap w (xOf 2 + 1.0, midY ())
    Assert.Equal((2, 2), selection host)
    // Soon after, close by: the word.
    w.Time.AdvanceMs 100.0
    down w 1 (xOf 3, midY ())
    up w 1 (xOf 3, midY ())
    Assert.Equal((0, 5), selection host)
    Assert.Equal<SelectionHandleKind list>([ SelectionHandleKind.Start; SelectionHandleKind.End ], kinds host)
    Assert.Equal<string list>([ "Cut"; "Copy"; "Paste"; "Select all" ], row host)
    // A tap inside the selection puts the row away, and another brings it back.
    tap w (xOf 2, midY ())
    Assert.Equal((0, 5), selection host)
    Assert.Empty(row host)
    Assert.Equal(2, (handles host).Length)
    tap w (xOf 2, midY ())
    Assert.NotEmpty(row host)
    // A tap elsewhere in the box: the caret there, its handle under it, no row.
    tap w (xOf 8 + 1.0, midY ())
    Assert.Equal((8, 8), selection host)
    Assert.Empty(row host)
    Assert.Equal<SelectionHandleKind list>([ SelectionHandleKind.Caret ], kinds host)
    let x, y = pointOf host SelectionHandleKind.Caret
    Assert.InRange(x, xOf 8, xOf 8 + 1.0)
    Assert.Equal(8.0 + Math.Round lh, y)
    // Left alone for four seconds, the caret's handle goes, and the layer with it.
    let layers = host.Layers.Count
    w.Time.Advance TextBox.caretHandleTime
    settle w
    Assert.Empty(handles host)
    Assert.False s.Touch
    Assert.Equal(layers - 1, host.Layers.Count)

[<Fact>]
let ``dragging a selection handle moves its end by characters, the ends may cross, and the row waits for the lift`` () =
    let _, w, host = show (page [ TextBox.text "hello brave world" ])
    longPress w (xOf 8, midY ())
    Assert.Equal((6, 11), selection host)
    Assert.NotEmpty(row host)
    // The end's handle, three characters to the right.
    let gx, gy = grip host SelectionHandleKind.End
    w.Time.AdvanceMs 1000.0
    down w 1 (gx, gy)
    settle w
    Assert.Empty(row host)
    move w 1 (gx + 30.0, gy)
    Assert.Equal((6, 14), selection host)
    Assert.Equal(xOf 14, fst (pointOf host SelectionHandleKind.End))
    // Back over the start and past it: the ends swap, and the selection's other end stays at 6.
    move w 1 (gx - 80.0, gy)
    Assert.Equal((3, 6), selection host)
    Assert.Empty(row host)
    up w 1 (gx - 80.0, gy)
    Assert.Equal((3, 6), selection host)
    Assert.NotEmpty(row host)
    Assert.False((stateOf host).Dragging)
    // The start's handle, to the text's start.
    let sx, sy = grip host SelectionHandleKind.Start
    w.Time.AdvanceMs 1000.0
    down w 1 (sx, sy)
    move w 1 (sx - 30.0, sy)
    up w 1 (sx - 30.0, sy)
    Assert.Equal((0, 6), selection host)

[<Fact>]
let ``the caret's handle moves the caret, and a tap on it toggles the row`` () =
    let _, w, host = show (page [ TextBox.text "hello brave world" ])
    tap w (xOf 3 + 1.0, midY ())
    Assert.Equal((3, 3), selection host)
    let gx, gy = grip host SelectionHandleKind.Caret
    w.Time.AdvanceMs 1000.0
    down w 1 (gx, gy)
    move w 1 (gx + 50.0, gy)
    Assert.Equal((8, 8), selection host)
    up w 1 (gx + 50.0, gy)
    Assert.Empty(row host)
    // A tap on the handle: the row (nothing selected: Paste and Select all).
    tap w (grip host SelectionHandleKind.Caret)
    Assert.Equal((8, 8), selection host)
    Assert.Equal<string list>([ "Paste"; "Select all" ], row host)
    tap w (grip host SelectionHandleKind.Caret)
    Assert.Empty(row host)

[<Fact>]
let ``the row's commands: Copy keeps the selection, Select all keeps the row, Cut and Paste leave a caret with its handle`` () =
    let platform, w, host = showTyped "hello brave world"
    longPress w (xOf 8, midY ())
    // Copy: the row goes, the selection and its handles stay.
    choose w host "Copy"
    Assert.Equal("brave", platform.Clipboard)
    Assert.Equal((6, 11), selection host)
    Assert.Empty(row host)
    Assert.Equal(2, (handles host).Length)
    // The row again (a tap inside the selection), then Select all: everything, and the row stays without it.
    tap w (xOf 8, midY ())
    choose w host "Select all"
    Assert.Equal((0, 17), selection host)
    Assert.Equal<string list>([ "Cut"; "Copy"; "Paste" ], row host)
    // Cut: a caret, with its handle; no row.
    choose w host "Cut"
    Assert.Equal("", textOf host)
    Assert.Equal("hello brave world", platform.Clipboard)
    Assert.Empty(row host)
    Assert.Equal<SelectionHandleKind list>([ SelectionHandleKind.Caret ], kinds host)
    // Paste, from the row a tap on the caret's handle brings.
    platform.Clipboard <- "again"
    tap w (grip host SelectionHandleKind.Caret)
    choose w host "Paste"
    Assert.Equal("again", textOf host)
    Assert.Empty(row host)
    Assert.Equal<SelectionHandleKind list>([ SelectionHandleKind.Caret ], kinds host)
    // The box had the keyboard throughout.
    Assert.True(obj.ReferenceEquals(host.Focused.Value, boxOf host))

[<Fact>]
let ``a password is selected whole and never copied; a read-only box offers Copy and Select all`` () =
    let platform, w, host = show (page [ TextBox.text "open sesame"; TextBox.isPassword true ])
    platform.Clipboard <- "kept"
    longPress w (xOf 2, midY ())
    Assert.Equal((0, 11), selection host)
    Assert.Equal<string list>([ "Paste" ], row host)
    // A double-tap too.
    tap w (xOf 30, 200.0)
    tap w (xOf 2, midY ())
    w.Time.AdvanceMs 100.0
    down w 1 (xOf 2, midY ())
    up w 1 (xOf 2, midY ())
    Assert.Equal((0, 11), selection host)
    Assert.Equal("kept", platform.Clipboard)
    let _, w, host = show (page [ TextBox.text "hello brave world"; TextBox.isReadOnly true ])
    longPress w (xOf 8, midY ())
    Assert.Equal((6, 11), selection host)
    Assert.Equal<string list>([ "Copy"; "Select all" ], row host)
    // No caret in a box that can't change, so no handle for one.
    tap w (xOf 14, midY ())
    Assert.Empty(handles host)

[<Fact>]
let ``a key, typed text, the mouse or the focus leaving puts the handles and the row away`` () =
    let _, w, host = showTyped "hello brave world"
    let s = stateOf host
    let select () =
        longPress w (xOf 8, midY ())
        Assert.Equal(2, (handles host).Length)
        Assert.NotEmpty(row host)
    let gone () =
        settle w
        Assert.Empty(handles host)
        Assert.Empty(row host)
        Assert.False s.Touch
    // Typing replaces the selection, as from any keyboard.
    select ()
    w.Send(TextInput "X")
    gone ()
    Assert.Equal("hello X world", textOf host)
    select ()
    w.Send(KeyDown(Key.Left, Modifiers.None, false))
    gone ()
    select ()
    w.Time.AdvanceMs 1000.0
    w.Send(PointerPressed(Mouse, 0, { X = xOf 2; Y = midY () }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = xOf 2; Y = midY () }, MouseButton.Left, Modifiers.None))
    gone ()
    // A tap outside the box: the focus goes, and they with it; the box keeps its selection.
    select ()
    let next = everything host |> Seq.find (fun e -> e.Name = "next")
    let p = next.PositionInTree
    tap w (p.X + 5.0, p.Y + 5.0)
    gone ()
    Assert.False((boxOf host).IsFocused)
    Assert.Equal((8, 13), selection host)

[<Fact>]
let ``a second finger does nothing while one drags a handle or extends a long press`` () =
    let _, w, host = show (page [ TextBox.text "hello brave world" ])
    press w (xOf 8, midY ())
    // Another finger taps in the box while the first is down.
    down w 2 (xOf 1, midY ())
    up w 2 (xOf 1, midY ())
    Assert.Equal((6, 11), selection host)
    Assert.True((stateOf host).LongPressed)
    up w 1 (xOf 8, midY ())
    // One finger on the end's handle; another on the start's.
    let ex, ey = grip host SelectionHandleKind.End
    let sx, sy = grip host SelectionHandleKind.Start
    w.Time.AdvanceMs 1000.0
    down w 1 (ex, ey)
    move w 1 (ex + 30.0, ey)
    down w 2 (sx, sy)
    move w 2 (sx - 40.0, sy)
    up w 2 (sx - 40.0, sy)
    Assert.Equal((6, 14), selection host)
    Assert.Equal(2, (stateOf host).HandleDrag)
    up w 1 (ex + 30.0, ey)
    Assert.Equal((6, 14), selection host)

[<Fact>]
let ``an app's context menu is the row for a finger: a Menu sideways with all its items, and choosing closes it`` () =
    let chosen = ResizeArray<string>()
    let menu =
        Menu.create [
            Menu.children [
                MenuItem.create [ MenuItem.text "Define"; MenuItem.shortcut "F1"; MenuItem.onClick (fun _ -> chosen.Add "Define") ]
                Menu.separator
                MenuItem.create [ MenuItem.text "Share"; MenuItem.onClick (fun _ -> chosen.Add "Share") ]
            ]
        ]
    let _, w, host = show (page [ TextBox.text "hello brave world"; TextBox.contextMenu menu ])
    longPress w (xOf 8, midY ())
    Assert.Equal((6, 11), selection host)
    Assert.Equal<string list>([ "Define"; "Share" ], row host)
    Assert.Equal(Horizontal, (menuOf host).Value.Get StackProps.orientation)
    choose w host "Share"
    Assert.Equal<string list>([ "Share" ], List.ofSeq chosen)
    Assert.Empty(row host)
    Assert.Equal((6, 11), selection host)
    // Any other view is shown as it is.
    let _, w, host = show (page [ TextBox.text "hello brave world"; TextBox.contextMenu (TextBlock.create [ TextBlock.name "own"; TextBlock.text "Mine" ]) ])
    longPress w (xOf 8, midY ())
    Assert.Contains(everything host, fun e -> e.Name = "own")
    Assert.True((menuOf host).IsNone)

[<Fact>]
let ``a row wider than the window shows as a menu in the same place`` () =
    let labels = { TextBoxMenuLabels.English with Cut = "Ausschneiden"; Copy = "Kopieren"; Paste = "Einfügen"; SelectAll = "Alles auswählen" }
    let platform, w, _, app = startAll [ App.view (page [ TextBox.text "hello brave world" ]); App.value TextBoxMenu.labels labels ]
    ignore platform
    w.Size <- { Width = 240; Height = 600 }
    w.Send(Resized w.Size)
    let host = app :?> ElementHost
    settle w
    longPress w (xOf 8, midY ())
    Assert.Equal<string list>([ "Ausschneiden"; "Kopieren"; "Einfügen"; "Alles auswählen" ], row host)
    let menu = (menuOf host).Value
    Assert.Equal(Vertical, menu.Get StackProps.orientation)
    // Inside the window.
    let p = menu.PositionInTree
    Assert.True(p.X >= 0.0 && p.X + menu.Bounds.Width <= 240.0)
    Assert.True(p.Y >= 0.0)

[<Fact>]
let ``scrolling the text takes the row away until it stops, and a handle scrolled out of view hides`` () =
    let lh = lineHeight ()
    let text = String.Join("\n", Seq.init 20 (fun i -> "line" + string i))
    let _, w, host = show (page [ TextBox.multiline true; TextBox.maxLines 3; TextBox.text text ])
    let s = stateOf host
    // The word on the second line.
    longPress w (xOf 2, 8.0 + 1.5 * lh)
    Assert.Equal((6, 11), selection host)
    Assert.Equal(2, (handles host).Length)
    Assert.NotEmpty(row host)
    let startY = snd (pointOf host SelectionHandleKind.Start)
    // The wheel scrolls one line: the handles follow in the same frame, and the row is out of sight.
    w.Send(PointerWheel({ X = xOf 12; Y = 8.0 + 1.5 * lh }, 0.0, -lh / ScrollViewer.step, Modifiers.None))
    w.DeliverFrame() |> ignore
    Assert.Equal(Math.Round lh, s.ScrollY)
    Assert.Equal(startY - s.ScrollY, snd (pointOf host SelectionHandleKind.Start))
    Assert.Empty(row host)
    // A quarter of a second after the scrolling stopped, it is back.
    w.Time.Advance TextBox.rowReturn
    settle w
    Assert.NotEmpty(row host)
    // Scrolled until the selected line is out of the box: no handles, no row; back in view, they return.
    w.Send(PointerWheel({ X = xOf 12; Y = 8.0 + 1.5 * lh }, 0.0, -5.0 * lh / ScrollViewer.step, Modifiers.None))
    settle w
    w.Time.Advance TextBox.rowReturn
    settle w
    Assert.Empty(handles host)
    Assert.Empty(row host)
    Assert.True s.Touch
    w.Send(PointerWheel({ X = xOf 12; Y = 8.0 + 1.5 * lh }, 0.0, 100.0, Modifiers.None))
    settle w
    w.Time.Advance TextBox.rowReturn
    settle w
    Assert.Equal(2, (handles host).Length)
    Assert.NotEmpty(row host)

[<Fact>]
let ``handles follow a box scrolled by what is around it, and hide where it is clipped`` () =
    let view =
        ScrollViewer.create [
            ScrollViewer.height 100.0
            ScrollViewer.verticalAlignment VerticalAlignment.Top
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [ box [ TextBox.text "hello brave world" ]; Border.create [ Border.height 600.0 ] ]
                ]
            )
        ]
    let _, w, host = show view
    longPress w (xOf 8, midY ())
    Assert.Equal(2, (handles host).Length)
    let y0 = snd (pointOf host SelectionHandleKind.Start)
    // Ten DIPs up: the handles too, in the same frame.
    w.Send(PointerWheel({ X = 300.0; Y = 50.0 }, 0.0, -10.0 / ScrollViewer.step, Modifiers.None))
    settle w
    let moved = (boxOf host).PositionInTree.Y
    Assert.True(moved < 0.0)
    Assert.Equal(y0 + moved, snd (pointOf host SelectionHandleKind.Start))
    // The box out of the viewer: nothing shows.
    w.Send(PointerWheel({ X = 300.0; Y = 50.0 }, 0.0, -100.0, Modifiers.None))
    settle w
    Assert.Empty(handles host)
    Assert.Empty(row host)

[<Fact>]
let ``a pen's long press opens the box's menu as a menu, with no handles`` () =
    let _, w, host = show (page [ TextBox.text "hello brave world" ])
    w.Time.AdvanceMs 1000.0
    w.Send(PointerPressed(Pen, 1, { X = xOf 8; Y = midY () }, MouseButton.Left, Modifiers.None))
    settle w
    w.Time.AdvanceMs 600.0
    settle w
    Assert.Empty(handles host)
    Assert.Equal(Vertical, (menuOf host).Value.Get StackProps.orientation)
    Assert.Contains("Undo", row host)

[<Fact>]
let ``the row goes above the selection, below the handles when there is no room, and in the middle when neither end shows`` () =
    let window = Size.Create(400.0, 600.0)
    // Room above: centred over the selection, a gap away.
    let r = SelectionLayer.place (Rect.Create(100.0, 200.0, 60.0, 20.0)) false 120.0 40.0 window
    Assert.Equal(Rect.Create(70.0, 200.0 - SelectionLayer.gap - 40.0, 120.0, 40.0), r)
    // None above: under the handles.
    let r = SelectionLayer.place (Rect.Create(100.0, 10.0, 60.0, 20.0)) false 120.0 40.0 window
    Assert.Equal(30.0 + SelectionHandle.depth + SelectionLayer.gap, r.Y)
    // Neither end in view: the middle of what shows.
    let r = SelectionLayer.place (Rect.Create(0.0, 100.0, 400.0, 300.0)) true 120.0 40.0 window
    Assert.Equal(Rect.Create(140.0, 230.0, 120.0, 40.0), r)
    // Kept inside the window.
    let r = SelectionLayer.place (Rect.Create(380.0, 300.0, 10.0, 20.0)) false 120.0 40.0 window
    Assert.Equal(400.0 - 120.0 - SelectionLayer.edge, r.X)

[<Fact>]
let ``a Menu as a row: text alone, upright separators, and the arrow keys go sideways`` () =
    let view =
        Button.create [
            Button.name "open"
            Button.text "Open"
            Button.horizontalAlignment HorizontalAlignment.Left
            Button.flyout (
                Menu.create [
                    Menu.orientation Horizontal
                    Menu.minWidth 0.0
                    Menu.children [
                        MenuItem.create [ MenuItem.text "One"; MenuItem.shortcut "Ctrl+1" ]
                        Menu.separator
                        MenuItem.create [ MenuItem.text "Two" ]
                        MenuItem.create [ MenuItem.text "Three"; MenuItem.isChecked true ]
                    ]
                ]
            )
        ]
    let _, w, host = show view
    let button = everything host |> Seq.find (fun e -> e.Name = "open")
    let p = button.PositionInTree
    w.Send(KeyDown(Key.Tab, Modifiers.None, false))
    w.Send(KeyUp(Key.Tab, Modifiers.None))
    w.Send(KeyDown(Key.Enter, Modifiers.None, false))
    w.Send(KeyUp(Key.Enter, Modifiers.None))
    settle w
    ignore p
    let items = rowItems host
    Assert.Equal(3, items.Length)
    // Side by side; the first is no wider than its text and padding (no shortcut, no room for a mark).
    Assert.True(items[1].PositionInTree.X > items[0].PositionInTree.X)
    Assert.Equal(items[0].PositionInTree.Y, items[2].PositionInTree.Y)
    Assert.Equal(Math.Ceiling(20.0 + (textSystem ()).Measure("One", Fonts.Regular(), 14.0).Width), items[0].Bounds.Width)
    // The one that can be checked keeps room for its mark.
    Assert.True(items[2].Bounds.Width > 20.0 + (textSystem ()).Measure("Three", Fonts.Regular(), 14.0).Width + 16.0)
    // The separator stands upright between the first two.
    let separator = everything host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Separator.Type))
    Assert.True(separator.Bounds.Height > separator.Bounds.Width)
    // Right and Left move through the items, round the ends.
    let focused () = host.Focused.Value.Get TextProps.text
    Assert.Equal("One", focused ())
    let key (k: Key) =
        w.Send(KeyDown(k, Modifiers.None, false))
        w.Send(KeyUp(k, Modifiers.None))
        settle w
    key Key.Right
    Assert.Equal("Two", focused ())
    key Key.Right
    key Key.Right
    Assert.Equal("One", focused ())
    key Key.Left
    Assert.Equal("Three", focused ())
    // Up and Down are not its keys.
    key Key.Down
    Assert.Equal("Three", focused ())
