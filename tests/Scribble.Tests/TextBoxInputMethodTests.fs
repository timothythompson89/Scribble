/// M9 step 9d on the fake platform (DESIGN.md, "9d: IME composition and the phone keyboard"): the window told when
/// text is wanted and where the caret is, which keyboard suits a box, what an input method composes (shown, not
/// typed, until it commits), keys left to it meanwhile, a click or lost focus committing it, and a finger on a box.
/// The stand-in shaper makes every UTF-16 unit 10 DIPs wide at size 20.
module Scribble.Tests.TextBoxInputMethodTests

open System
open Xunit
open Scribble.Platform
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

let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 20 do
        n <- n + 1

let private box (settings: Setting<TextBox> list) =
    TextBox.create ([ TextBox.fontSize 20.0; TextBox.width 226.0; TextBox.horizontalAlignment HorizontalAlignment.Left ] @ settings)

/// A TextBox (text area 200 DIPs wide from x = 13) above a button.
let private page (settings: Setting<TextBox> list) =
    StackPanel.create [
        StackPanel.children [ box settings; Button.create [ Button.text "Next"; Button.horizontalAlignment HorizontalAlignment.Left ] ]
    ]

let private show (view: View) =
    let platform, w, _, app = startAll [ App.view view ]
    w.Size <- { Width = 400; Height = 900 }
    w.Send(Resized w.Size)
    let host = app :?> ElementHost
    settle w
    platform, w, host

let private boxOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TextBox.Type))
let private buttonOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Button.Type))
let private stateOf (host: ElementHost) = TextBox.stateOf (boxOf host)
let private textOf (host: ElementHost) = (TextBox.editOf (boxOf host)).Text
let private selection (host: ElementHost) = let s = TextBox.editOf (boxOf host) in s.Anchor, s.Caret
let private lineHeight () = (textSystem ()).LineHeight(Fonts.Regular(), 20.0)

let private xOf (index: int) = 13.0 + 10.0 * float index

let private clickAt (w: FakeWindow) (x: float) (y: float) =
    w.Time.AdvanceMs 1000.0
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    settle w

let private clickOn (w: FakeWindow) (e: Element) =
    let p = e.PositionInTree
    clickAt w (p.X + 5.0) (p.Y + e.Bounds.Height / 2.0)

let private keyWith (w: FakeWindow) (mods: Modifiers) (k: Key) =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    settle w

let private key (w: FakeWindow) (k: Key) = keyWith w Modifiers.None k

let private typeText (w: FakeWindow) (text: string) =
    for c in text do
        let k = if c = ' ' then Key.Space else Key.Char(Char.ToLowerInvariant c)
        w.Send(KeyDown(k, Modifiers.None, false))
        w.Send(TextInput(string c))
        w.Send(KeyUp(k, Modifiers.None))
    settle w

/// What an input method does: composes `steps` one after the other, then commits `result` ("": it was cancelled).
let private compose (w: FakeWindow) (steps: string list) (result: string) =
    for step in steps do
        w.Send(TextComposition(step, step.Length))
    settle w
    w.Send(TextComposition("", 0))
    if result <> "" then w.Send(TextInput result)
    settle w

[<Fact>]
let ``the window is told when text is wanted, and where the caret is as it moves`` () =
    let lh = lineHeight ()
    let _, w, host = show (page [])
    Assert.Equal(ValueNone, w.TextInput)
    Assert.Equal(0, w.TextInputCalls)
    clickAt w (xOf 0) 15.0
    let first = w.TextInput.Value
    Assert.Equal(KeyboardKind.Text, first.Keyboard)
    Assert.False first.Multiline
    // The caret: at the text's start, a line high, inside the box.
    Assert.Equal(13.0, first.CaretX)
    Assert.InRange(first.CaretHeight, lh - 0.5, lh + 0.5)
    Assert.InRange(first.CaretY, 1.0, 12.0)
    typeText w "abc"
    Assert.Equal(43.0, w.TextInput.Value.CaretX)
    key w Key.Left
    Assert.Equal(33.0, w.TextInput.Value.CaretX)
    // Nothing moved: nothing is told again.
    let calls = w.TextInputCalls
    w.Time.AdvanceMs 600.0
    settle w
    Assert.Equal(calls, w.TextInputCalls)
    // The focus goes to a button: text is no longer wanted.
    key w Key.Tab
    Assert.Equal(ValueNone, w.TextInput)
    keyWith w Modifiers.Shift Key.Tab
    Assert.True w.TextInput.IsSome
    // Nothing focused: off again.
    host.Update(TextBlock.create [ TextBlock.text "gone" ])
    settle w
    Assert.Equal(ValueNone, w.TextInput)

[<Fact>]
let ``the window hears which keyboard suits the box as soon as it has the focus`` () =
    let asked (settings: Setting<TextBox> list) =
        let _, w, _ = show (page settings)
        // Before any frame: a phone shows its keyboard from inside the touch that asked.
        w.Send(PointerPressed(Mouse, 0, { X = 20.0; Y = 15.0 }, MouseButton.Left, Modifiers.None))
        let request = w.TextInput.Value
        request.Keyboard, request.Multiline
    Assert.Equal((KeyboardKind.Text, false), asked [])
    Assert.Equal((KeyboardKind.Number, false), asked [ TextBox.keyboard KeyboardKind.Number ])
    Assert.Equal((KeyboardKind.Email, false), asked [ TextBox.keyboard KeyboardKind.Email ])
    Assert.Equal((KeyboardKind.Search, false), asked [ TextBox.keyboard KeyboardKind.Search ])
    // A password has its own, whatever is asked.
    Assert.Equal((KeyboardKind.Password, false), asked [ TextBox.isPassword true; TextBox.keyboard KeyboardKind.Number ])
    Assert.Equal((KeyboardKind.Text, true), asked [ TextBox.multiline true ])

[<Fact>]
let ``an element that listens for typed text wants it too; one that doesn't, doesn't`` () =
    let heard = ResizeArray<string>()
    let view =
        StackPanel.create [
            StackPanel.children [
                Border.create [ Border.name "listens"; Border.focusable true; Border.height 40.0; Border.onTextInput (fun e -> heard.Add e.Text) ]
                Border.create [ Border.name "deaf"; Border.focusable true; Border.height 40.0 ]
            ]
        ]
    let _, w, _ = show view
    key w Key.Tab
    let request = w.TextInput.Value
    // No caret of its own: its whole box.
    Assert.Equal((0.0, 0.0, 40.0), (request.CaretX, request.CaretY, request.CaretHeight))
    w.Send(TextInput "x")
    Assert.Equal<string list>([ "x" ], List.ofSeq heard)
    key w Key.Tab
    Assert.Equal(ValueNone, w.TextInput)

[<Fact>]
let ``what an input method composes shows at the caret and isn't text until it commits`` () =
    let changes = ResizeArray<string>()
    let _, w, host = show (page [ TextBox.onTextChanged changes.Add ])
    let s = stateOf host
    clickAt w (xOf 0) 15.0
    typeText w "ab"
    key w Key.Left
    changes.Clear()
    w.Send(TextComposition("n", 1))
    w.Send(TextComposition("ni", 2))
    settle w
    // Between "a" and "b", with the caret after it; the text itself hasn't changed.
    Assert.Equal("anib", s.Shown.Text)
    Assert.Equal(3, s.Shown.Caret)
    Assert.Equal("ab", textOf host)
    Assert.Empty changes
    // The input method's candidates go beside its caret.
    Assert.Equal(xOf 3, w.TextInput.Value.CaretX)
    w.Send(TextComposition("", 0))
    w.Send(TextInput "に")
    settle w
    Assert.Equal("aにb", textOf host)
    Assert.Equal("aにb", s.Shown.Text)
    Assert.Equal((2, 2), selection host)
    Assert.Equal<string list>([ "aにb" ], List.ofSeq changes)
    // The commit first, then the end (some platforms' order): the same.
    w.Send(TextComposition("ho", 2))
    w.Send(TextInput "ほ")
    w.Send(TextComposition("", 0))
    settle w
    Assert.Equal("aにほb", textOf host)
    // Cancelled: nothing is typed.
    compose w [ "x"; "xy" ] ""
    Assert.Equal("aにほb", textOf host)
    Assert.Equal("aにほb", s.Shown.Text)
    Assert.Equal<string list>([ "aにb"; "aにほb" ], List.ofSeq changes)

[<Fact>]
let ``a composition takes the selection's place, and cancelling it gives the selection back`` () =
    let _, w, host = show (page [ TextBox.text "hello world" ])
    let s = stateOf host
    clickAt w (xOf 0) 15.0
    for _ in 1 .. 5 do
        keyWith w Modifiers.Shift Key.Right
    w.Send(TextComposition("x", 1))
    settle w
    Assert.Equal("x world", s.Shown.Text)
    Assert.False s.Shown.HasSelection
    compose w [ "xy" ] ""
    Assert.Equal("hello world", s.Shown.Text)
    Assert.Equal((0, 5), selection host)

[<Fact>]
let ``a commit is one undo step, and maxLength applies to it`` () =
    let _, w, host = show (page [ TextBox.maxLength 6 ])
    clickAt w (xOf 0) 15.0
    typeText w "ab"
    compose w [ "k"; "ka"; "kan" ] "漢字"
    typeText w "c"
    Assert.Equal("ab漢字c", textOf host)
    let undo () = keyWith w Modifiers.Control (Key.Char 'z')
    undo ()
    Assert.Equal("ab漢字", textOf host)
    undo ()
    Assert.Equal("ab", textOf host)
    undo ()
    Assert.Equal("", textOf host)
    keyWith w Modifiers.Control (Key.Char 'y')
    keyWith w Modifiers.Control (Key.Char 'y')
    keyWith w Modifiers.Control (Key.Char 'y')
    // Six at most: of three more, one fits.
    compose w [ "x" ] "日本語"
    Assert.Equal("ab漢字c日", textOf host)

[<Fact>]
let ``keys are the input method's while it composes`` () =
    let submitted = ResizeArray<string>()
    let _, w, host = show (page [ TextBox.onSubmitted submitted.Add ])
    clickAt w (xOf 0) 15.0
    typeText w "abc"
    w.Send(TextComposition("ni", 2))
    settle w
    for k in [ Key.Enter; Key.Backspace; Key.Left; Key.Home; Key.Delete; Key.Escape; Key.Tab ] do
        Assert.True(host.HandleEvent(KeyDown(k, Modifiers.None, false)), string k)
        host.HandleEvent(KeyUp(k, Modifiers.None)) |> ignore
    keyWith w Modifiers.Control (Key.Char 'a')
    settle w
    Assert.Empty submitted
    Assert.Equal("abc", textOf host)
    Assert.Equal((3, 3), selection host)
    Assert.True((boxOf host).IsFocused)
    Assert.Equal("abcni", (stateOf host).Shown.Text)
    // Committed: keys are the box's again.
    w.Send(TextComposition("", 0))
    w.Send(TextInput "に")
    key w Key.Enter
    Assert.Equal<string list>([ "abcに" ], List.ofSeq submitted)
    key w Key.Backspace
    Assert.Equal("abc", textOf host)

[<Fact>]
let ``a click in the box or the focus leaving types what was being composed`` () =
    let changes = ResizeArray<string>()
    let _, w, host = show (page [ TextBox.onTextChanged changes.Add ])
    clickAt w (xOf 0) 15.0
    typeText w "abc"
    changes.Clear()
    w.Send(TextComposition("xy", 2))
    settle w
    // A click at the text's start: "xy" stays where it was composed, and the caret goes to the click.
    clickAt w (xOf 0) 15.0
    Assert.Equal("abcxy", textOf host)
    Assert.Equal((0, 0), selection host)
    Assert.Equal(1, w.CompositionsCleared)
    Assert.Equal("", (stateOf host).Composition)
    // The input method's own end, if it sends one after being dropped, changes nothing.
    w.Send(TextComposition("", 0))
    settle w
    Assert.Equal("abcxy", textOf host)
    // The focus goes to the button.
    w.Send(TextComposition("q", 1))
    settle w
    clickOn w (buttonOf host)
    Assert.False((boxOf host).IsFocused)
    Assert.Equal("qabcxy", textOf host)
    Assert.Equal(2, w.CompositionsCleared)
    Assert.Equal(ValueNone, w.TextInput)
    Assert.Equal<string list>([ "abcxy"; "qabcxy" ], List.ofSeq changes)
    // Undo takes each back as one step.
    clickAt w (xOf 0) 15.0
    keyWith w Modifiers.Control (Key.Char 'z')
    Assert.Equal("abcxy", textOf host)

[<Fact>]
let ``the focus moving from a box that is composing leaves both looking as they should`` () =
    let view =
        StackPanel.create [
            StackPanel.children [ box [ TextBox.name "first" ]; box [ TextBox.name "second" ] ]
        ]
    let _, w, host = show view
    let boxes = all host.Tree.Root.Value |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, TextBox.Type)) |> List.ofSeq
    let first, second = boxes[0], boxes[1]
    let plain = first.Get BorderProps.borderBrush
    clickOn w second
    w.Time.AdvanceMs 1000.0
    settle w
    let focusedBrush = second.Get BorderProps.borderBrush
    Assert.NotEqual(plain, focusedBrush)
    w.Send(TextComposition("abc", 3))
    settle w
    clickOn w first
    w.Time.AdvanceMs 1000.0
    settle w
    Assert.True first.IsFocused
    Assert.Equal("abc", (TextBox.editOf second).Text)
    Assert.Equal(focusedBrush, first.Get BorderProps.borderBrush)
    Assert.Equal(plain, second.Get BorderProps.borderBrush)

[<Fact>]
let ``the window is told the colour the app clears to, once, and again when it changes`` () =
    // A browser paints the page behind its canvas with it (a phone can show a strip of that page).
    let platform, w, _, _ = startAll [ App.background (Scribble.Rendering.Color.Hex 0x102030); App.view (page []) ]
    ignore platform
    Assert.Equal(ValueNone, w.Background)
    settle w
    Assert.Equal(ValueSome(struct (0x10uy, 0x20uy, 0x30uy)), w.Background)
    // The theme's background when the app gives none.
    let told (theme: Theme) =
        let _, w, _, _ = startAll [ App.theme theme; App.view (page []) ]
        settle w
        w.Background.Value
    Assert.NotEqual(told (Theme.dark []), told (Theme.light []))

[<Fact>]
let ``nothing is composed into a read-only box`` () =
    let _, w, host = show (page [ TextBox.text "fixed"; TextBox.isReadOnly true ])
    clickAt w (xOf 0) 15.0
    w.Send(TextComposition("x", 1))
    settle w
    Assert.Equal("fixed", (stateOf host).Shown.Text)
    Assert.Equal(1, w.CompositionsCleared)
    // Its keys still work: nothing is being composed.
    key w Key.End
    Assert.Equal((5, 5), selection host)

[<Fact>]
let ``composing in several lines wraps with the text and says where its caret is`` () =
    let lh = lineHeight ()
    // 100 DIPs of text: ten characters a line.
    let view = box [ TextBox.multiline true; TextBox.width 126.0 ]
    let _, w, host = show view
    let s = stateOf host
    clickAt w (xOf 0) 15.0
    typeText w "abcdefgh"
    w.Send(TextComposition("ijklm", 5))
    settle w
    Assert.Equal("abcdefgh", textOf host)
    Assert.Equal(2, s.Lines.LineCount)
    // The caret is on the second line, after "klm".
    let request = w.TextInput.Value
    Assert.True request.Multiline
    Assert.Equal(xOf 3, request.CaretX)
    Assert.InRange(request.CaretY, 8.0 + lh - 1.0, 8.0 + lh + 1.0)
    compose w [] ""
    Assert.Equal(1, s.Lines.LineCount)

[<Fact>]
let ``a finger's tap places the caret when it lifts, and a finger drag scrolls several lines`` () =
    let text = String.Join("\n", Seq.init 20 string)
    let _, w, host = show (page [ TextBox.multiline true; TextBox.width 126.0; TextBox.maxLines 3; TextBox.text text ])
    let s = stateOf host
    let lh = lineHeight ()
    let y (line: int) = 8.0 + (float line + 0.5) * lh
    let touch (e: PlatformEvent) = w.Send e
    // A tap on the second line.
    touch (PointerPressed(Touch, 1, { X = xOf 1; Y = y 1 }, MouseButton.Left, Modifiers.None))
    Assert.Equal((0, 0), selection host)
    touch (PointerReleased(Touch, 1, { X = xOf 1; Y = y 1 }, MouseButton.Left, Modifiers.None))
    settle w
    Assert.True((boxOf host).IsFocused)
    Assert.Equal((3, 3), selection host)
    Assert.Equal(0.0, s.ScrollY)
    // A finger moves up over the text: the text follows it, and the caret stays.
    touch (PointerPressed(Touch, 1, { X = xOf 1; Y = y 2 }, MouseButton.Left, Modifiers.None))
    for step in 1 .. 6 do
        touch (PointerMoved(Touch, 1, { X = xOf 1; Y = y 2 - 10.0 * float step }, Modifiers.None))
    touch (PointerReleased(Touch, 1, { X = xOf 1; Y = y 2 - 60.0 }, MouseButton.Left, Modifiers.None))
    settle w
    Assert.True(s.ScrollY > 20.0)
    Assert.Equal((3, 3), selection host)
    Assert.False s.Panning
    // A finger moving down brings the text back to its top.
    touch (PointerPressed(Touch, 1, { X = xOf 1; Y = y 0 }, MouseButton.Left, Modifiers.None))
    for step in 1 .. 20 do
        touch (PointerMoved(Touch, 1, { X = xOf 1; Y = y 0 + 10.0 * float step }, Modifiers.None))
    touch (PointerReleased(Touch, 1, { X = xOf 1; Y = y 0 + 200.0 }, MouseButton.Left, Modifiers.None))
    settle w
    Assert.Equal(0.0, s.ScrollY)
    // At the top, a finger moving down has nothing more to show: the drag isn't the box's (the page's, if it scrolls).
    touch (PointerPressed(Touch, 1, { X = xOf 1; Y = y 0 }, MouseButton.Left, Modifiers.None))
    for step in 1 .. 3 do
        touch (PointerMoved(Touch, 1, { X = xOf 1; Y = y 0 + 10.0 * float step }, Modifiers.None))
    Assert.False s.Panning
    Assert.Equal(0.0, s.ScrollY)
    touch (PointerReleased(Touch, 1, { X = xOf 1; Y = y 0 + 30.0 }, MouseButton.Left, Modifiers.None))
