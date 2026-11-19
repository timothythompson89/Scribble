/// M9 step 9a: the editing engine's rules (as properties, on any sequence of edits) and the single-line TextBox on
/// the fake platform (DESIGN.md, "9a: the single-line TextBox"): typed text routed to the focused element, every
/// key and mouse gesture, Tab selecting all, controlled text, sideways scrolling, and the caret's blinking ending.
/// The stand-in shaper makes every UTF-16 unit 10 DIPs wide at size 20.
module Scribble.Tests.TextBoxTests

open System
open System.Globalization
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
open Scribble.Tests.AppTests
open Scribble.Tests.TestShaping

// ---- The engine ----

let private at (text: string) (caret: int) = { Text = text; Anchor = caret; Caret = caret }
let private sel (text: string) (anchor: int) (caret: int) = { Text = text; Anchor = anchor; Caret = caret }

/// "a", a grinning face (two UTF-16 units), "e" with a combining acute, "b".
let private mixed = "a\U0001F600éb"

let private isStop (text: string) (index: int) = TextEdit.stopAtOrBefore text index = index

[<Fact>]
let ``the caret moves by what a reader sees as one character`` () =
    let stops = [ 0; 1; 3; 5; 6 ]
    let mutable s = at mixed 0
    for expected in stops do
        Assert.Equal(expected, s.Caret)
        s <- TextEdit.move CaretMove.Right false s
    Assert.Equal(6, s.Caret)
    for expected in List.rev stops do
        Assert.Equal(expected, s.Caret)
        s <- TextEdit.move CaretMove.Left false s
    Assert.Equal(0, s.Caret)
    // An index inside the emoji or between the letter and its accent is its start.
    Assert.Equal(1, TextEdit.stopAtOrBefore mixed 2)
    Assert.Equal(3, TextEdit.stopAtOrBefore mixed 4)
    Assert.Equal(4, TextEdit.graphemes mixed)

[<Fact>]
let ``Backspace and Delete take a whole emoji or an accented letter`` () =
    Assert.Equal("aéb", (TextEdit.backspace (at mixed 3)).Text)
    Assert.Equal("a\U0001F600b", (TextEdit.backspace (at mixed 5)).Text)
    Assert.Equal("aéb", (TextEdit.delete (at mixed 1)).Text)
    Assert.Equal(mixed, (TextEdit.backspace (at mixed 0)).Text)
    Assert.Equal(mixed, (TextEdit.delete (at mixed 6)).Text)

[<Fact>]
let ``word moves stop at the start of each word and each run of punctuation`` () =
    let text = "hello, big  world"
    let rights = [ 0; 5; 7; 12; 17; 17 ]
    let mutable s = at text 0
    for expected in rights do
        Assert.Equal(expected, s.Caret)
        s <- TextEdit.move CaretMove.WordRight false s
    let lefts = [ 17; 12; 7; 5; 0; 0 ]
    for expected in lefts do
        Assert.Equal(expected, s.Caret)
        s <- TextEdit.move CaretMove.WordLeft false s

[<Fact>]
let ``a double-click's word is the run of one kind under it`` () =
    let text = "one two,  three"
    Assert.Equal(struct (0, 3), TextEdit.wordAt text 1)
    Assert.Equal(struct (4, 7), TextEdit.wordAt text 4)
    Assert.Equal(struct (7, 8), TextEdit.wordAt text 7)
    Assert.Equal(struct (8, 10), TextEdit.wordAt text 9)
    Assert.Equal(struct (10, 15), TextEdit.wordAt text 15)
    Assert.Equal(struct (0, 0), TextEdit.wordAt "" 0)

[<Fact>]
let ``Left and Right with a selection go to its ends; Shift keeps the anchor`` () =
    let s = sel "abcdef" 4 2
    Assert.Equal(at "abcdef" 2, TextEdit.move CaretMove.Left false s)
    Assert.Equal(at "abcdef" 4, TextEdit.move CaretMove.Right false s)
    Assert.Equal(sel "abcdef" 4 1, TextEdit.move CaretMove.Left true s)
    Assert.Equal(sel "abcdef" 4 6, TextEdit.move CaretMove.End true s)
    Assert.Equal(sel "abcdef" 0 6, TextEdit.selectAll s)
    Assert.Equal("cd", s.SelectedText)

[<Fact>]
let ``typing replaces the selection and leaves the caret after what was typed`` () =
    Assert.Equal(at "abXYef" 4, TextEdit.insert "XY" 0 (sel "abcdef" 2 4))
    Assert.Equal(at "abXYef" 4, TextEdit.insert "XY" 0 (sel "abcdef" 4 2))
    Assert.Equal(at "Xabc" 1, TextEdit.insert "X" 0 (at "abc" 0))
    Assert.Equal(at "abef" 2, TextEdit.backspace (sel "abcdef" 2 4))
    Assert.Equal(at "abef" 2, TextEdit.delete (sel "abcdef" 4 2))
    Assert.Equal(at "one " 4, TextEdit.backspaceWord (at "one two" 7))
    Assert.Equal(at "two" 0, TextEdit.deleteWord (at "one two" 0))

[<Fact>]
let ``line breaks and tabs become spaces, other control characters are dropped`` () =
    Assert.Equal("a b c d", (TextEdit.insert "a\r\nb\nc\td" 0 TextEdit.empty).Text)
    Assert.Equal("ab", (TextEdit.insert "a\u0008\u001Bb" 0 TextEdit.empty).Text)
    // Nothing left to type: nothing changes, the selection included.
    let s = sel "abc" 0 3
    Assert.Equal(s, TextEdit.insert "\u0008" 0 s)

[<Fact>]
let ``maxLength counts what a reader sees, and what doesn't fit is left out`` () =
    Assert.Equal("abc", (TextEdit.insert "abcdef" 3 TextEdit.empty).Text)
    Assert.Equal("ab\U0001F600", (TextEdit.insert "\U0001F600\U0001F600" 3 (at "ab" 2)).Text)
    Assert.Equal("abc", (TextEdit.insert "x" 3 (at "abc" 3)).Text)
    // Over a selection there is room for what replaces it.
    Assert.Equal("axc", (TextEdit.insert "xyz" 3 (sel "abc" 1 2)).Text)

[<Fact>]
let ``other text from the view keeps the caret, clamped into it`` () =
    Assert.Equal(sel "ab" 2 2, TextEdit.withText "ab" (sel "abcdef" 3 5))
    Assert.Equal(at "ABCDEF" 4, TextEdit.withText "ABCDEF" (at "abcdef" 4))
    Assert.Equal(at "" 0, TextEdit.withText null (at "abc" 2))
    // Into the middle of an emoji: its start.
    Assert.Equal(at "\U0001F600" 0, TextEdit.withText "\U0001F600" (at "ab" 1))

let private pieces = [| "a"; "b"; " "; "word"; ", "; "\U0001F600"; "é"; "\n"; "\t"; "\u0008"; "x y"; "_"; "9" |]

/// One random edit: the operation's name (for a failure's message) and the next state.
let private randomEdit (r: Random) (maxLength: int) (s: EditState) =
    let extend = r.Next 2 = 0
    match r.Next 14 with
    | 0 | 1 | 2 | 3 ->
        let text = pieces[r.Next pieces.Length]
        "insert", TextEdit.insert text maxLength s
    | 4 -> "backspace", TextEdit.backspace s
    | 5 -> "delete", TextEdit.delete s
    | 6 -> "backspaceWord", TextEdit.backspaceWord s
    | 7 -> "deleteWord", TextEdit.deleteWord s
    | 8 -> "left", TextEdit.move CaretMove.Left extend s
    | 9 -> "right", TextEdit.move CaretMove.Right extend s
    | 10 -> "word", TextEdit.move (if r.Next 2 = 0 then CaretMove.WordLeft else CaretMove.WordRight) extend s
    | 11 -> "ends", TextEdit.move (if r.Next 2 = 0 then CaretMove.Start else CaretMove.End) extend s
    | 12 -> "moveTo", TextEdit.moveTo (r.Next(-2, s.Text.Length + 3)) extend s
    | _ -> "selectWord", TextEdit.selectWordAt (r.Next(0, s.Text.Length + 1)) s

[<Fact>]
let ``any sequence of edits leaves the caret and anchor on character boundaries, in one line`` () =
    let r = Random 20261118
    for round in 0 .. 199 do
        let maxLength = if round % 4 = 0 then 12 else 0
        let mutable s = TextEdit.empty
        for step in 0 .. 119 do
            let name, next = randomEdit r maxLength s
            let where = name + " at round " + string round + ", step " + string step
            Assert.True(next.Caret >= 0 && next.Caret <= next.Text.Length, where)
            Assert.True(next.Anchor >= 0 && next.Anchor <= next.Text.Length, where)
            Assert.True(isStop next.Text next.Caret, where)
            Assert.True(isStop next.Text next.Anchor, where)
            Assert.False(next.Text |> Seq.exists Char.IsControl, where)
            if maxLength > 0 then Assert.True(TextEdit.graphemes next.Text <= maxLength, where)
            // Moves never change the text.
            match name with
            | "left" | "right" | "word" | "ends" | "moveTo" | "selectWord" -> Assert.Equal(s.Text, next.Text)
            | _ -> ()
            s <- next

[<Fact>]
let ``typing then as many Backspaces as characters gives the text back`` () =
    let r = Random 9
    for _ in 0 .. 199 do
        let mutable s = TextEdit.empty
        for _ in 0 .. r.Next 6 do
            s <- TextEdit.insert pieces[r.Next pieces.Length] 0 s
        // Somewhere in it, with no selection.
        let before = TextEdit.moveTo (r.Next(0, s.Text.Length + 1)) false s
        let typed = "x" + pieces[r.Next 4] + "y"
        let mutable after = TextEdit.insert typed 0 before
        for _ in 1 .. TextEdit.graphemes typed do
            after <- TextEdit.backspace after
        Assert.Equal(before, after)

// ---- Caret places ----

[<Fact>]
let ``caret places follow the glyphs, and an emoji has one place at each side`` () =
    let text = textSystem ()
    let font = Scribble.Text.Fonts.Regular()
    Assert.Equal<float[]>([| 0.0; 10.0; 20.0; 30.0 |], text.CaretStops("abc", font, 20.0))
    Assert.Equal<float[]>([| 0.0 |], text.CaretStops("", font, 20.0))
    // "a", an emoji of two units, "b": the index inside the emoji has its start's place.
    Assert.Equal<float[]>([| 0.0; 10.0; 10.0; 30.0; 40.0 |], text.CaretStops("a\U0001F600b", font, 20.0))

// ---- The element ----

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

let private field (settings: Setting<TextBox> list) =
    TextBox.create (
        [ TextBox.fontSize 20.0
          TextBox.width 200.0
          TextBox.horizontalAlignment HorizontalAlignment.Left ]
        @ settings
    )

/// A TextBox (text area from x = 13, 174 wide: 17 characters) above a button.
let private page (settings: Setting<TextBox> list) =
    StackPanel.create [
        StackPanel.children [
            field settings
            Button.create [ Button.text "Next"; Button.horizontalAlignment HorizontalAlignment.Left ]
        ]
    ]

let private show' (settings: AppSetting<unit> list) =
    let w, _, app = start settings
    let host = app :?> ElementHost
    settle w
    w, host

let private show (view: View) = show' [ App.view view ]

let private boxOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TextBox.Type))
let private edit (host: ElementHost) = TextBox.editOf (boxOf host)
let private textOf (host: ElementHost) = (edit host).Text
let private selection (host: ElementHost) = let s = edit host in s.Anchor, s.Caret

/// The x of the caret place before character `index` (unscrolled), and the middle of the line.
let private xOf (index: int) = 13.0 + 10.0 * float index
let private midY = 15.0

let private pressAt (w: FakeWindow) (mods: Modifiers) (x: float) =
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = midY }, MouseButton.Left, mods))
let private releaseAt (w: FakeWindow) (x: float) =
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = midY }, MouseButton.Left, Modifiers.None))
let private moveTo (w: FakeWindow) (x: float) = w.Send(PointerMoved(Mouse, 0, { X = x; Y = midY }, Modifiers.None))

/// A click on its own: long enough after the last one not to count as a double-click.
let private clickAt (w: FakeWindow) (x: float) =
    w.Time.AdvanceMs 1000.0
    pressAt w Modifiers.None x
    releaseAt w x
    settle w

/// A click soon after the last one (a tenth of a second).
let private clickAgain (w: FakeWindow) (x: float) =
    w.Time.AdvanceMs 100.0
    pressAt w Modifiers.None x
    releaseAt w x
    settle w

let private keyWith (w: FakeWindow) (mods: Modifiers) (k: Key) =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    settle w

let private key (w: FakeWindow) (k: Key) = keyWith w Modifiers.None k

/// Types as a keyboard does: each character's key goes down, its text comes, the key goes up.
let private typeText (w: FakeWindow) (text: string) =
    for c in text do
        let k = if c = ' ' then Key.Space else Key.Char(Char.ToLowerInvariant c)
        w.Send(KeyDown(k, Modifiers.None, false))
        w.Send(TextInput(string c))
        w.Send(KeyUp(k, Modifiers.None))
    settle w

[<Fact>]
let ``typed text goes into the focused TextBox, which keeps its own text`` () =
    let changes = ResizeArray<string>()
    let w, host = show (page [ TextBox.onTextChanged changes.Add ])
    // Nothing has focus: the text goes nowhere.
    Assert.False(host.HandleEvent(TextInput "x"))
    clickAt w (xOf 0)
    Assert.True((boxOf host).IsFocused)
    Assert.True(host.HandleEvent(TextInput "h"))
    typeText w "i there"
    Assert.Equal("hi there", textOf host)
    Assert.Equal("hi there", (boxOf host).Get TextBoxProps.text)
    Assert.Equal<string list>([ "h"; "hi"; "hi "; "hi t"; "hi th"; "hi the"; "hi ther"; "hi there" ], List.ofSeq changes)
    Assert.Equal((8, 8), selection host)

[<Fact>]
let ``typed text bubbles to onTextInput when nothing uses it`` () =
    let heard = ResizeArray<string>()
    let w, host =
        show (
            Border.create [
                Border.onTextInput (fun e -> heard.Add e.Text)
                Border.content (page [ TextBox.isReadOnly true; TextBox.text "fixed" ])
            ]
        )
    key w Key.Tab
    Assert.True((boxOf host).IsFocused)
    w.Send(TextInput "a")
    Assert.Equal<string list>([ "a" ], List.ofSeq heard)
    Assert.Equal("fixed", textOf host)

type private Msg = Typed of string

[<Fact>]
let ``a controlled TextBox shows what its view answers, the caret kept inside`` () =
    // The view answers in capitals and never keeps more than five characters.
    let update (Typed text) (_: string) = text.ToUpperInvariant().Substring(0, min 5 text.Length)
    let view (model: string) dispatch = page [ TextBox.text model; TextBox.onTextChanged (fun t -> dispatch (Typed t)) ]
    let w, _, app = start [ App.program "" update view ]
    let host = app :?> ElementHost
    settle w
    clickAt w (xOf 0)
    typeText w "abc"
    Assert.Equal("ABC", textOf host)
    Assert.Equal((3, 3), selection host)
    typeText w "defg"
    Assert.Equal("ABCDE", textOf host)
    Assert.Equal((5, 5), selection host)

[<Fact>]
let ``a controlled TextBox whose view doesn't follow goes back to what the view says`` () =
    let heard = ResizeArray<string>()
    let w, host = show (page [ TextBox.text "fixed"; TextBox.onTextChanged heard.Add ])
    clickAt w (xOf 5)
    typeText w "!"
    Assert.Equal<string list>([ "fixed!" ], List.ofSeq heard)
    Assert.Equal("fixed", textOf host)
    Assert.Equal((5, 5), selection host)

[<Fact>]
let ``keys typed between two frames all land in a controlled TextBox`` () =
    let update (Typed text) (_: string) = text
    let view (model: string) dispatch = page [ TextBox.text model; TextBox.onTextChanged (fun t -> dispatch (Typed t)) ]
    let w, _, app = start [ App.program "" update view ]
    let host = app :?> ElementHost
    settle w
    clickAt w (xOf 0)
    // No frame between them: the view hasn't answered the first when the second comes.
    w.Send(TextInput "a")
    w.Send(TextInput "b")
    w.Send(TextInput "c")
    settle w
    Assert.Equal("abc", textOf host)
    Assert.Equal((3, 3), selection host)

[<Fact>]
let ``the arrow, Home and End keys move the caret, Shift selects, Ctrl goes by word`` () =
    let w, host = show (page [ TextBox.text "one two three" ])
    clickAt w (xOf 0)
    key w Key.Right
    Assert.Equal((1, 1), selection host)
    keyWith w Modifiers.Control Key.Right
    Assert.Equal((4, 4), selection host)
    keyWith w (Modifiers.Control ||| Modifiers.Shift) Key.Right
    Assert.Equal((4, 8), selection host)
    key w Key.Left
    Assert.Equal((4, 4), selection host)
    key w Key.End
    Assert.Equal((13, 13), selection host)
    keyWith w Modifiers.Shift Key.Home
    Assert.Equal((13, 0), selection host)
    key w Key.Right
    Assert.Equal((13, 13), selection host)
    keyWith w Modifiers.Control Key.Left
    Assert.Equal((8, 8), selection host)
    key w Key.Up
    Assert.Equal((0, 0), selection host)
    keyWith w Modifiers.Shift Key.Down
    Assert.Equal((0, 13), selection host)
    key w Key.Home
    keyWith w Modifiers.Control (Key.Char 'a')
    Assert.Equal((0, 13), selection host)

[<Fact>]
let ``Backspace and Delete take a character, a word with Ctrl, or the selection`` () =
    let changes = ResizeArray<string>()
    let w, host = show (page [ TextBox.onTextChanged changes.Add ])
    clickAt w (xOf 0)
    typeText w "one two three"
    changes.Clear()
    key w Key.Backspace
    Assert.Equal("one two thre", textOf host)
    keyWith w Modifiers.Control Key.Backspace
    Assert.Equal("one two ", textOf host)
    key w Key.Home
    key w Key.Delete
    Assert.Equal("ne two ", textOf host)
    keyWith w Modifiers.Control Key.Delete
    Assert.Equal("two ", textOf host)
    keyWith w Modifiers.Control (Key.Char 'a')
    key w Key.Delete
    Assert.Equal("", textOf host)
    Assert.Equal<string list>([ "one two thre"; "one two "; "ne two "; "two "; "" ], List.ofSeq changes)
    // Nothing left: no change is reported.
    key w Key.Backspace
    Assert.Equal(5, changes.Count)

[<Fact>]
let ``typing replaces the selection`` () =
    let w, host = show (page [])
    clickAt w (xOf 0)
    typeText w "hello world"
    keyWith w (Modifiers.Control ||| Modifiers.Shift) Key.Left
    typeText w "you"
    Assert.Equal("hello you", textOf host)
    Assert.Equal((9, 9), selection host)

[<Fact>]
let ``Enter calls onSubmitted; without it Enter, and always Escape, go on`` () =
    let submitted = ResizeArray<string>()
    let w, host = show (page [ TextBox.text "go"; TextBox.onSubmitted submitted.Add ])
    clickAt w (xOf 0)
    Assert.True(host.HandleEvent(KeyDown(Key.Enter, Modifiers.None, false)))
    Assert.Equal<string list>([ "go" ], List.ofSeq submitted)
    Assert.False(host.HandleEvent(KeyDown(Key.Escape, Modifiers.None, false)))
    // Keys that type are the box's (Space scrolls nothing around it); shortcuts aren't.
    Assert.True(host.HandleEvent(KeyDown(Key.Space, Modifiers.None, false)))
    Assert.True(host.HandleEvent(KeyDown(Key.Char 'q', Modifiers.None, false)))
    Assert.False(host.HandleEvent(KeyDown(Key.Char 's', Modifiers.Control, false)))
    let w2, host2 = show (page [ TextBox.text "go" ])
    clickAt w2 (xOf 0)
    Assert.False(host2.HandleEvent(KeyDown(Key.Enter, Modifiers.None, false)))
    // Tab still moves the focus on.
    key w2 Key.Tab
    Assert.False((boxOf host2).IsFocused)

[<Fact>]
let ``a click places the caret at the nearest place, Shift+click extends, a drag selects`` () =
    let w, host = show (page [ TextBox.text "abcdefgh" ])
    clickAt w (xOf 3 + 4.0)
    Assert.Equal((3, 3), selection host)
    clickAt w (xOf 3 + 6.0)
    Assert.Equal((4, 4), selection host)
    // Past the end: the end.
    clickAt w 190.0
    Assert.Equal((8, 8), selection host)
    w.Time.AdvanceMs 1000.0
    pressAt w Modifiers.Shift (xOf 2)
    releaseAt w (xOf 2)
    Assert.Equal((8, 2), selection host)
    w.Time.AdvanceMs 1000.0
    pressAt w Modifiers.None (xOf 1)
    moveTo w (xOf 5 + 1.0)
    Assert.Equal((1, 5), selection host)
    // The press holds the pointer: far outside the box still selects.
    moveTo w 390.0
    Assert.Equal((1, 8), selection host)
    moveTo w (xOf 0 - 50.0)
    Assert.Equal((1, 0), selection host)
    releaseAt w (xOf 0)
    moveTo w (xOf 6)
    Assert.Equal((1, 0), selection host)
    Assert.Equal(Cursor.Text, w.Cursor)

[<Fact>]
let ``a double-click selects a word, a triple-click everything, and slow clicks neither`` () =
    let w, host = show (page [ TextBox.text "one two three" ])
    clickAt w (xOf 5)
    Assert.Equal((5, 5), selection host)
    clickAgain w (xOf 5)
    Assert.Equal((4, 7), selection host)
    clickAgain w (xOf 5)
    Assert.Equal((0, 13), selection host)
    // A fourth starts again.
    clickAgain w (xOf 5)
    Assert.Equal((5, 5), selection host)
    // Too slow, or somewhere else: single clicks.
    w.Time.AdvanceMs 900.0
    clickAt w (xOf 5)
    Assert.Equal((5, 5), selection host)
    clickAgain w (xOf 10)
    Assert.Equal((10, 10), selection host)

[<Fact>]
let ``a drag from a double-click selects whole words`` () =
    let w, host = show (page [ TextBox.text "one two three" ])
    clickAt w (xOf 5)
    w.Time.AdvanceMs 100.0
    pressAt w Modifiers.None (xOf 5)
    Assert.Equal((4, 7), selection host)
    moveTo w (xOf 10)
    Assert.Equal((4, 13), selection host)
    moveTo w (xOf 1)
    Assert.Equal((7, 0), selection host)
    releaseAt w (xOf 1)

[<Fact>]
let ``tabbing into a TextBox selects all its text; clicking into it doesn't`` () =
    let w, host = show (page [])
    clickAt w (xOf 0)
    typeText w "hello"
    key w Key.Tab
    Assert.False((boxOf host).IsFocused)
    keyWith w Modifiers.Shift Key.Tab
    Assert.True((boxOf host).IsFocused)
    Assert.Equal((0, 5), selection host)
    typeText w "x"
    Assert.Equal("x", textOf host)
    key w Key.Tab
    Assert.False((boxOf host).IsFocused)
    clickAt w (xOf 1)
    Assert.Equal((1, 1), selection host)
    // And again with Shift+Tab from the button.
    key w Key.Tab
    keyWith w Modifiers.Shift Key.Tab
    Assert.Equal((0, 1), selection host)

[<Fact>]
let ``text wider than the box scrolls with the caret, and shows its start again without focus`` () =
    let w, host = show (page [])
    let state () = TextBox.stateOf (boxOf host)
    clickAt w (xOf 0)
    typeText w "0123456789"
    Assert.Equal(0.0, state().Scroll)
    typeText w "0123456789abcdefghij"
    // 30 characters are 300 wide in 174: the caret (1 wide) is at the right edge.
    Assert.Equal(300.0 + 1.0 - 174.0, state().Scroll)
    key w Key.Home
    Assert.Equal(0.0, state().Scroll)
    // A click lands on what is shown there.
    key w Key.End
    clickAt w (13.0 + 3.0)
    Assert.Equal((13, 13), selection host)
    // Deleting from the end never leaves empty space at the right while there is text to show.
    key w Key.End
    for _ in 1 .. 5 do
        key w Key.Backspace
    Assert.Equal(250.0 + 1.0 - 174.0, state().Scroll)
    key w Key.Tab
    settle w
    Assert.Equal(0.0, state().Scroll)

[<Fact>]
let ``the caret blinks for ten seconds after the last input, then the window is idle`` () =
    let w, host = show (page [])
    let state () = TextBox.stateOf (boxOf host)
    clickAt w (xOf 0)
    typeText w "a"
    Assert.False(w.FrameRequested)
    Assert.True(state().CaretOn)
    let mutable frames = 0
    let mutable offSeen = false
    for _ in 1 .. 40 do
        w.Time.AdvanceMs 500.0
        if not (state().CaretOn) then offSeen <- true
        while w.DeliverFrame() do
            frames <- frames + 1
    // One frame for each half blink, twenty of them; then nothing, with the caret left showing.
    Assert.Equal(TextBox.blinkTicks, frames)
    Assert.True(offSeen)
    Assert.True(state().CaretOn)
    w.Time.AdvanceMs 60000.0
    Assert.False(w.FrameRequested)
    // Input starts it again; losing focus stops it at once.
    key w Key.Left
    w.Time.AdvanceMs 500.0
    Assert.True(w.FrameRequested)
    settle w
    key w Key.Tab
    w.Time.AdvanceMs 5000.0
    Assert.False(w.FrameRequested)

[<Fact>]
let ``typing keeps the caret on: each key starts the ten seconds again`` () =
    let w, host = show (page [])
    let state () = TextBox.stateOf (boxOf host)
    clickAt w (xOf 0)
    for _ in 1 .. 30 do
        w.Time.AdvanceMs 400.0
        typeText w "a"
        Assert.True(state().CaretOn)
    Assert.Equal(TextBox.blinkTicks, state().BlinkTicks)

[<Fact>]
let ``a read-only TextBox selects but doesn't change`` () =
    let heard = ResizeArray<string>()
    let w, host = show (page [ TextBox.text "fixed text"; TextBox.isReadOnly true; TextBox.onTextChanged heard.Add ])
    clickAt w (xOf 2)
    typeText w "x"
    key w Key.Backspace
    key w Key.Delete
    keyWith w Modifiers.Shift Key.End
    Assert.Equal("fixed text", textOf host)
    Assert.Equal((2, 10), selection host)
    Assert.Empty heard

[<Fact>]
let ``maxLength stops typing, and a password shows a bullet for each character`` () =
    let w, host = show (page [ TextBox.maxLength 4; TextBox.isPassword true ])
    clickAt w (xOf 0)
    typeText w "secrets"
    Assert.Equal("secr", textOf host)
    Assert.Equal("••••", (TextBox.stateOf (boxOf host)).Display)
    // One word: Ctrl+Left goes to the start, a double-click selects everything.
    keyWith w Modifiers.Control Key.Left
    Assert.Equal((0, 0), selection host)
    w.Time.AdvanceMs 1000.0
    clickAt w (xOf 2)
    clickAgain w (xOf 2)
    Assert.Equal((0, 4), selection host)

[<Fact>]
let ``a disabled TextBox takes no focus and no text`` () =
    let w, host = show (page [ TextBox.text "off"; TextBox.isEnabled false ])
    clickAt w (xOf 1)
    Assert.False((boxOf host).IsFocused)
    typeText w "x"
    Assert.Equal("off", textOf host)
    key w Key.Tab
    Assert.False((boxOf host).IsFocused)

[<Fact>]
let ``text set by the view while the box has focus keeps the caret where it fits`` () =
    let setText: (string -> unit) ref = ref ignore
    let view =
        Component.define "Host" (fun ctx () ->
            let text = ctx.useState "hello world"
            setText.Value <- text.Set
            page [ TextBox.text text.Current ])
    let w, host = show (view ())
    clickAt w (xOf 8)
    setText.Value "hi"
    settle w
    Assert.Equal("hi", textOf host)
    Assert.Equal((2, 2), selection host)

[<Fact>]
let ``a TextBox is a text box to assistive technology, named by its placeholder or its label`` () =
    let _, host = show (field [ TextBox.placeholder "Search" ])
    Assert.Contains("TextBox \"Search\"", host.SemanticTree().ToString())
    let _, host2 = show (field [ TextBox.placeholder "Search"; Semantics.label "Find an icon" ])
    Assert.Contains("TextBox \"Find an icon\"", host2.SemanticTree().ToString())

[<Fact>]
let ``it asks for one line's height and takes the width it is given`` () =
    let _, host = show (page [])
    let box = boxOf host
    Assert.Equal(200.0, box.Bounds.Width)
    // One line at size 20 plus the padding (7 + 7) and the border (1 + 1).
    let lineHeight = (textSystem ()).LineHeight(Scribble.Text.Fonts.Regular(), 20.0)
    Assert.InRange(box.Bounds.Height, lineHeight + 15.0, lineHeight + 17.0)

// ---- The Icons sample's search: a controlled TextBox whose view answers with other text ----

[<Fact>]
let ``the Icons sample's search shows names its way, scrolls to the first match, and Escape clears it`` () =
    let names = Array.ofSeq (Scribble.Icons.Material.names ())
    // An icon far down the grid that is the first match of its own name.
    let target = names[3000..] |> Array.find (fun n -> Scribble.Samples.IconGallery.firstMatch n = Some n)
    let w, host = show' Scribble.Samples.IconGallery.app
    let box = boxOf host
    let tileShown (name: string) =
        all host.Tree.Root.Value
        |> Seq.exists (fun e -> obj.ReferenceEquals(e.Type, TextBlock.Type) && e.Get TextProps.text = name)
    Assert.False(tileShown target)
    let p = box.PositionInTree
    let at: PixelPoint = { X = p.X + 20.0; Y = p.Y + box.Bounds.Height / 2.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    settle w
    Assert.True(box.IsFocused)
    // The border of a focused box (the accent).
    let plainBorder = box.Get BorderProps.borderBrush
    // Typed with capitals and spaces: shown in lower case with underscores, the caret after what was typed.
    typeText w (target.ToUpperInvariant().Replace('_', ' '))
    Assert.Equal(target, textOf host)
    Assert.Equal((target.Length, target.Length), selection host)
    Assert.True(tileShown target)
    Assert.Equal(plainBorder, box.Get BorderProps.borderBrush)
    // Nothing is called this: the border says so.
    typeText w "qqq"
    Assert.Equal(target + "qqq", textOf host)
    Assert.NotEqual(plainBorder, box.Get BorderProps.borderBrush)
    key w Key.Escape
    Assert.Equal("", textOf host)
    Assert.Equal(plainBorder, box.Get BorderProps.borderBrush)
    Assert.True(box.IsFocused)

[<Fact>]
let ``a one-line box draws a line break the app gave as a space and leaves the text alone`` () =
    let changes = ResizeArray<string>()
    let view =
        Component.define "Host" (fun ctx () ->
            let text = ctx.useState "ab\ncd\r\nef"
            page [
                TextBox.text text.Current
                TextBox.onTextChanged (fun t ->
                    changes.Add t
                    text.Set t)
            ])
    let w, host = show (view ())
    let s = TextBox.stateOf (boxOf host)
    // Drawn on one line, a space for each character of a break; nothing is drawn below and clipped away.
    Assert.Equal("ab cd  ef", s.Display)
    Assert.Equal("ab\ncd\r\nef", textOf host)
    Assert.Equal<float[]>([| 0.0; 10.0; 20.0; 30.0; 40.0; 50.0; 60.0; 70.0; 80.0; 90.0 |], s.Stops)
    // The caret steps over a break as one character ("\r\n" is one), and a click lands by what is drawn.
    clickAt w (xOf 2)
    Assert.Equal((2, 2), selection host)
    key w Key.Right
    Assert.Equal((3, 3), selection host)
    key w Key.Right
    key w Key.Right
    key w Key.Right
    Assert.Equal((7, 7), selection host)
    clickAt w (xOf 8)
    Assert.Equal((8, 8), selection host)
    // Typing changes only what is typed: the breaks the app gave are still there.
    typeText w "x"
    Assert.Equal("ab\ncd\r\nexf", textOf host)
    Assert.Equal<string list>([ "ab\ncd\r\nexf" ], List.ofSeq changes)
    Assert.Equal("ab cd  exf", s.Display)
    // Backspace over a break takes it whole.
    key w Key.Home
    key w Key.Right
    key w Key.Right
    key w Key.Right
    key w Key.Backspace
    Assert.Equal("abcd\r\nexf", textOf host)
    // A screen reader hears the text as it is.
    let node = host.SemanticTree().Nodes |> Seq.find (fun n -> n.Role = Role.TextBox)
    Assert.Equal(ValueSome "abcd\r\nexf", node.Value)
    Assert.Contains("value \"abcd\\r\\nexf\"", node.Describe())
