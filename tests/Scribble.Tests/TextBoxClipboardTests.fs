/// M9 step 9b: the undo history's rules (as properties, on any sequence of edits) and the TextBox's clipboard and
/// undo keys on the fake platform (DESIGN.md, "9b: clipboard and undo"): copy, cut and paste with their edge rules,
/// what one undo step is, undo with controlled text, the keys claimed from the browser, and `ctx.useClipboard`.
/// The stand-in shaper makes every UTF-16 unit 10 DIPs wide at size 20.
module Scribble.Tests.TextBoxClipboardTests

open System
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests

// ---- The history ----

let private at (text: string) (caret: int) = { Text = text; Anchor = caret; Caret = caret }
let private sel (text: string) (anchor: int) (caret: int) = { Text = text; Anchor = anchor; Caret = caret }
let private ms (n: float) = TimeSpan.FromMilliseconds n

/// Types `text` a character at a time from `state`, `gap` ms apart starting at `start`.
let private typeFrom (start: float) (gap: float) (text: string) (state: EditState, history: EditHistory) =
    let mutable s, h, t = state, history, start
    for c in text do
        let next = TextEdit.insert (string c) 0 s
        h <- TextHistory.record EditKind.Typing (ms t) s next h
        s <- next
        t <- t + gap
    s, h

let private undoAll (history: EditHistory) =
    let mutable h = history
    let mutable last = ValueNone
    let mutable count = 0
    let mutable go = true
    while go do
        match TextHistory.undo h with
        | ValueSome(struct (state, next)) ->
            last <- ValueSome state
            h <- next
            count <- count + 1
        | ValueNone -> go <- false
    last, h, count

[<Fact>]
let ``a run of typing is one step; a pause, another kind of edit or a moved caret starts a new one`` () =
    let s, h = typeFrom 0.0 100.0 "hello" (TextEdit.empty, TextHistory.empty)
    Assert.Equal(1, h.Undo.Length)
    Assert.Equal(TextEdit.empty, h.Undo.Head.Before)
    Assert.Equal(at "hello" 5, h.Undo.Head.After)
    // More than a second later: a new step.
    let s, h = typeFrom 1600.0 100.0 " you" (s, h)
    Assert.Equal(2, h.Undo.Length)
    // A second counts from the step's last edit, not its first.
    let _, long = typeFrom 0.0 900.0 "abcdef" (TextEdit.empty, TextHistory.empty)
    Assert.Equal(1, long.Undo.Length)
    // Backspaces join each other, not the typing before them.
    let b1 = TextEdit.backspace s
    let h = TextHistory.record EditKind.Deleting (ms 2000.0) s b1 h
    let b2 = TextEdit.backspace b1
    let h = TextHistory.record EditKind.Deleting (ms 2100.0) b1 b2 h
    Assert.Equal(3, h.Undo.Length)
    Assert.Equal(at "hello y" 7, h.Undo.Head.After)
    // The caret moved (and came back): the next Backspace is a step of its own.
    let h = TextHistory.close h
    let b3 = TextEdit.backspace b2
    let h = TextHistory.record EditKind.Deleting (ms 2200.0) b2 b3 h
    Assert.Equal(4, h.Undo.Length)
    // Edits that don't start where the last step ended don't join it either.
    let moved = TextEdit.moveTo 0 false b3
    let typed = TextEdit.insert "x" 0 moved
    let h2 = TextHistory.record EditKind.Typing (ms 2300.0) moved typed (snd (typeFrom 2250.0 10.0 "z" (b3, h)))
    Assert.Equal(6, h2.Undo.Length)

[<Fact>]
let ``a cut, a paste or a replaced selection is always a step of its own`` () =
    let s, h = typeFrom 0.0 50.0 "ab" (TextEdit.empty, TextHistory.empty)
    let pasted = TextEdit.insert "cd" 0 s
    let h = TextHistory.record EditKind.Single (ms 120.0) s pasted h
    let again = TextEdit.insert "cd" 0 pasted
    let h = TextHistory.record EditKind.Single (ms 140.0) pasted again h
    Assert.Equal(3, h.Undo.Length)
    // Typing after a paste doesn't join it.
    let _, h = typeFrom 160.0 50.0 "e" (again, h)
    Assert.Equal(4, h.Undo.Length)

[<Fact>]
let ``undo gives each step's text and selection back, redo its result, and a new edit drops what was undone`` () =
    let s, h = typeFrom 0.0 50.0 "hello world" (TextEdit.empty, TextHistory.empty)
    // "world" selected and typed over.
    let selected = sel s.Text 6 11
    let h = TextHistory.close h
    let replaced = TextEdit.insert "x" 0 selected
    let h = TextHistory.record EditKind.Single (ms 5000.0) selected replaced h
    match TextHistory.undo h with
    | ValueSome(struct (state, h1)) ->
        Assert.Equal(selected, state)
        Assert.Equal(1, h1.Redo.Length)
        match TextHistory.redo h1 with
        | ValueSome(struct (again, h2)) ->
            Assert.Equal(replaced, again)
            Assert.Empty h2.Redo
            Assert.Equal(2, h2.Undo.Length)
        | ValueNone -> failwith "nothing to redo"
        // Another edit in place of the redo: the redo is gone.
        let other = TextEdit.insert "y" 0 state
        let h3 = TextHistory.record EditKind.Single (ms 6000.0) state other h1
        Assert.Empty h3.Redo
        Assert.Equal(ValueNone, TextHistory.redo h3)
    | ValueNone -> failwith "nothing to undo"
    Assert.Equal(ValueNone, TextHistory.undo TextHistory.empty)
    Assert.Equal(ValueNone, TextHistory.redo TextHistory.empty)

[<Fact>]
let ``the history keeps the newest 100 steps`` () =
    let mutable s, h = TextEdit.empty, TextHistory.empty
    for i in 0 .. 149 do
        let next = TextEdit.insert "a" 0 s
        h <- TextHistory.record EditKind.Single (ms (float i)) s next h
        s <- next
    Assert.Equal(TextHistory.limit, h.Undo.Length)
    let last, _, count = undoAll h
    Assert.Equal(100, count)
    // The oldest fifty can't be undone.
    Assert.Equal(ValueSome(at (String('a', 50)) 50), last)

[<Fact>]
let ``a view's other answer ends the step in what it showed; its refusal takes the step away`` () =
    let s, h = typeFrom 0.0 50.0 "ab" (TextEdit.empty, TextHistory.empty)
    let shown = at "AB" 2
    let amended = TextHistory.amend shown h
    Assert.Equal(shown, amended.Undo.Head.After)
    Assert.Equal(TextEdit.empty, amended.Undo.Head.Before)
    // The next letter still joins the step.
    let _, more = typeFrom 100.0 50.0 "c" (shown, amended)
    Assert.Equal(1, more.Undo.Length)
    // The view put the old text back: nothing to undo.
    Assert.Empty (TextHistory.amend TextEdit.empty h).Undo
    Assert.Equal(TextHistory.empty, TextHistory.amend s TextHistory.empty)

let private pieces = [| "a"; "b"; " "; "word"; ", "; "\U0001F600"; "é"; "x y"; "_"; "9" |]

[<Fact>]
let ``any edits, then as many undos, give the first text and selection back; as many redos the last`` () =
    let r = Random 20261118
    for round in 0 .. 199 do
        let mutable s = TextEdit.empty
        let mutable h = TextHistory.empty
        let mutable time = 0.0
        let mutable first = ValueNone
        // Every state the text went through, by step.
        for _ in 0 .. 59 do
            time <- time + float (r.Next 1500)
            let extend = r.Next 2 = 0
            let kind, next =
                match r.Next 10 with
                | 0 | 1 | 2 | 3 -> EditKind.Typing, TextEdit.insert pieces[r.Next pieces.Length] 0 s
                | 4 -> EditKind.Deleting, TextEdit.backspace s
                | 5 -> EditKind.Deleting, TextEdit.deleteWord s
                | 6 -> EditKind.Single, TextEdit.insert "pasted text" 0 s
                | 7 -> EditKind.Single, TextEdit.move CaretMove.Left extend s
                | 8 -> EditKind.Single, TextEdit.move CaretMove.WordRight extend s
                | _ -> EditKind.Single, TextEdit.selectWordAt (r.Next(0, s.Text.Length + 1)) s
            if next.Text <> s.Text then
                if first.IsNone then first <- ValueSome s
                h <- TextHistory.record (if s.HasSelection then EditKind.Single else kind) (ms time) s next h
            elif next <> s then h <- TextHistory.close h
            s <- next
        let where = "round " + string round
        let steps = h.Undo.Length
        Assert.True(steps <= 60, where)
        let last, undone, count = undoAll h
        Assert.Equal(steps, count)
        Assert.Equal(first, last)
        Assert.Equal(steps, undone.Redo.Length)
        // Back again: the text as it was after the last edit.
        let mutable back = undone
        let mutable state = ValueNone
        for _ in 1 .. steps do
            match TextHistory.redo back with
            | ValueSome(struct (st, next)) ->
                state <- ValueSome st
                back <- next
            | ValueNone -> failwith ("nothing to redo, " + where)
        if steps > 0 then Assert.Equal(s.Text, state.Value.Text)
        Assert.Equal(ValueNone, TextHistory.redo back)
        // Each step starts with the text the one before it ended with.
        for older, newer in List.pairwise (List.rev h.Undo) do
            Assert.Equal(older.After.Text, newer.Before.Text)

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

/// A TextBox (text area from x = 13: character `i` starts at 13 + 10 i) above a button.
let private page (settings: Setting<TextBox> list) =
    StackPanel.create [
        StackPanel.children [
            TextBox.create (
                [ TextBox.fontSize 20.0
                  TextBox.width 300.0
                  TextBox.horizontalAlignment HorizontalAlignment.Left ]
                @ settings
            )
            Button.create [ Button.text "Next"; Button.horizontalAlignment HorizontalAlignment.Left ]
        ]
    ]

let private showApp (settings: AppSetting<'Msg> list) =
    let platform, w, app = startOn settings
    let host = app :?> ElementHost
    settle w
    platform, w, host

let private show (view: View) = showApp [ App.view view ]

let private boxOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TextBox.Type))
let private textOf (host: ElementHost) = (TextBox.editOf (boxOf host)).Text
let private selection (host: ElementHost) = let s = TextBox.editOf (boxOf host) in s.Anchor, s.Caret

let private keyWith (w: FakeWindow) (mods: Modifiers) (k: Key) =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    settle w

let private key (w: FakeWindow) (k: Key) = keyWith w Modifiers.None k
let private ctrl (w: FakeWindow) (c: char) = keyWith w Modifiers.Control (Key.Char c)

/// Types as a keyboard does, with no time between the keys.
let private typeText (w: FakeWindow) (text: string) =
    for c in text do
        let k = if c = ' ' then Key.Space else Key.Char(Char.ToLowerInvariant c)
        w.Send(KeyDown(k, Modifiers.None, false))
        w.Send(TextInput(string c))
        w.Send(KeyUp(k, Modifiers.None))
    settle w

/// Focus by a click at the text's start (a second after anything before, so it is one click).
let private focus (w: FakeWindow) =
    w.Time.AdvanceMs 1000.0
    let at: PixelPoint = { X = 13.0; Y = 15.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    settle w

/// Shift+Right `count` times from the caret.
let private selectRight (w: FakeWindow) (count: int) =
    for _ in 1 .. count do
        keyWith w Modifiers.Shift Key.Right

[<Fact>]
let ``Ctrl+C and Ctrl+Insert copy the selection; with none the clipboard keeps what it had`` () =
    let platform, w, host = show (page [ TextBox.text "hello world" ])
    platform.Clipboard <- "before"
    focus w
    ctrl w 'c'
    Assert.Equal("before", platform.Clipboard)
    selectRight w 5
    ctrl w 'c'
    Assert.Equal("hello", platform.Clipboard)
    Assert.Equal("hello world", textOf host)
    Assert.Equal((0, 5), selection host)
    key w Key.End
    keyWith w Modifiers.Shift Key.Left
    keyWith w Modifiers.Control Key.Insert
    Assert.Equal("d", platform.Clipboard)

[<Fact>]
let ``Ctrl+X and Shift+Delete cut the selection; with none nothing happens`` () =
    let changes = ResizeArray<string>()
    let platform, w, host = show (page [ TextBox.onTextChanged changes.Add ])
    focus w
    typeText w "hello world"
    changes.Clear()
    platform.Clipboard <- "before"
    ctrl w 'x'
    keyWith w Modifiers.Shift Key.Delete
    Assert.Equal("before", platform.Clipboard)
    Assert.Equal("hello world", textOf host)
    key w Key.Home
    selectRight w 6
    ctrl w 'x'
    Assert.Equal("hello ", platform.Clipboard)
    Assert.Equal("world", textOf host)
    Assert.Equal((0, 0), selection host)
    selectRight w 1
    keyWith w Modifiers.Shift Key.Delete
    Assert.Equal("w", platform.Clipboard)
    Assert.Equal<string list>([ "world"; "orld" ], List.ofSeq changes)

[<Fact>]
let ``Ctrl+V and Shift+Insert paste over the selection, in one line, within maxLength`` () =
    let platform, w, host = show (page [ TextBox.maxLength 12 ])
    focus w
    typeText w "abcd"
    platform.Clipboard <- "one\r\ntwo\tx"
    keyWith w Modifiers.Shift Key.Left
    keyWith w Modifiers.Shift Key.Left
    ctrl w 'v'
    // "cd" replaced; the line break and the tab are spaces; the caret after what was pasted.
    Assert.Equal("abone two x", textOf host)
    Assert.Equal((11, 11), selection host)
    // One more fits.
    platform.Clipboard <- "12345"
    keyWith w Modifiers.Shift Key.Insert
    Assert.Equal("abone two x1", textOf host)
    // An empty clipboard pastes nothing, and keeps the selection.
    platform.Clipboard <- ""
    ctrl w 'a'
    ctrl w 'v'
    Assert.Equal("abone two x1", textOf host)
    Assert.Equal((0, 12), selection host)

[<Fact>]
let ``a password pastes but is never copied or cut; a read-only box copies and does nothing else`` () =
    let platform, w, host = show (page [ TextBox.isPassword true ])
    focus w
    platform.Clipboard <- "secret"
    ctrl w 'v'
    Assert.Equal("secret", textOf host)
    platform.Clipboard <- "kept"
    ctrl w 'a'
    ctrl w 'c'
    ctrl w 'x'
    keyWith w Modifiers.Control Key.Insert
    keyWith w Modifiers.Shift Key.Delete
    Assert.Equal("kept", platform.Clipboard)
    Assert.Equal("secret", textOf host)
    let platform, w, host = show (page [ TextBox.text "fixed"; TextBox.isReadOnly true ])
    focus w
    ctrl w 'a'
    ctrl w 'c'
    Assert.Equal("fixed", platform.Clipboard)
    platform.Clipboard <- "other"
    ctrl w 'x'
    ctrl w 'v'
    ctrl w 'z'
    Assert.Equal("other", platform.Clipboard)
    Assert.Equal("fixed", textOf host)

[<Fact>]
let ``a clipboard that answers later pastes when it does, unless the box lost focus`` () =
    let platform, w, host = show (page [])
    platform.ClipboardIsSlow <- true
    platform.Clipboard <- "late"
    focus w
    typeText w "ab"
    ctrl w 'v'
    Assert.Equal("ab", textOf host)
    Assert.Equal(1, platform.ClipboardReads.Count)
    platform.FinishClipboardReads()
    platform.RunWake()
    settle w
    Assert.Equal("ablate", textOf host)
    // Asked, then Tab moves on before the answer: nothing is pasted.
    ctrl w 'v'
    key w Key.Tab
    platform.FinishClipboardReads()
    platform.RunWake()
    settle w
    Assert.Equal("ablate", textOf host)

[<Fact>]
let ``Ctrl+Z undoes a run of typing at once; Ctrl+Y and Ctrl+Shift+Z redo it`` () =
    let changes = ResizeArray<string>()
    let _, w, host = show (page [ TextBox.onTextChanged changes.Add ])
    focus w
    typeText w "hello"
    changes.Clear()
    ctrl w 'z'
    Assert.Equal("", textOf host)
    ctrl w 'z'
    Assert.Equal("", textOf host)
    ctrl w 'y'
    Assert.Equal("hello", textOf host)
    Assert.Equal((5, 5), selection host)
    ctrl w 'y'
    ctrl w 'z'
    keyWith w (Modifiers.Control ||| Modifiers.Shift) (Key.Char 'z')
    Assert.Equal("hello", textOf host)
    Assert.Equal<string list>([ ""; "hello"; ""; "hello" ], List.ofSeq changes)

[<Fact>]
let ``a pause, a moved caret and another kind of edit each start a new undo step`` () =
    let _, w, host = show (page [])
    focus w
    typeText w "one"
    w.Time.AdvanceMs 1500.0
    typeText w " two"
    // The caret moved away and back: a new step.
    key w Key.Left
    key w Key.Right
    typeText w "!"
    key w Key.Backspace
    key w Key.Backspace
    Assert.Equal("one tw", textOf host)
    let texts = ResizeArray<string>()
    for _ in 1 .. 5 do
        ctrl w 'z'
        texts.Add(textOf host)
    Assert.Equal<string list>([ "one two!"; "one two"; "one"; ""; "" ], List.ofSeq texts)

[<Fact>]
let ``undo gives the selection back, and a paste or a cut is a step of its own`` () =
    let platform, w, host = show (page [])
    focus w
    typeText w "hello world"
    key w Key.Home
    selectRight w 5
    typeText w "x"
    Assert.Equal("x world", textOf host)
    ctrl w 'z'
    Assert.Equal("hello world", textOf host)
    Assert.Equal((0, 5), selection host)
    // Cut, then paste twice at the end.
    ctrl w 'x'
    key w Key.End
    ctrl w 'v'
    ctrl w 'v'
    Assert.Equal(" worldhellohello", textOf host)
    Assert.Equal("hello", platform.Clipboard)
    ctrl w 'z'
    Assert.Equal(" worldhello", textOf host)
    ctrl w 'z'
    Assert.Equal(" world", textOf host)
    ctrl w 'z'
    Assert.Equal("hello world", textOf host)
    Assert.Equal((0, 5), selection host)
    // Typing now drops what was undone.
    typeText w "y"
    ctrl w 'y'
    Assert.Equal("y world", textOf host)

type private Msg =
    | Typed of string
    | Clear

[<Fact>]
let ``a controlled TextBox undoes through its view, and a filter's answer is what is undone to`` () =
    // The view shows capitals.
    let heard = ResizeArray<string>()
    let update msg (_: string) =
        match msg with
        | Typed text ->
            heard.Add text
            text.ToUpperInvariant()
        | Clear -> ""
    let view (model: string) dispatch = page [ TextBox.text model; TextBox.onTextChanged (fun t -> dispatch (Typed t)) ]
    let _, w, host = showApp [ App.program "" update view ]
    focus w
    typeText w "abc"
    Assert.Equal("ABC", textOf host)
    w.Time.AdvanceMs 1500.0
    typeText w "d"
    Assert.Equal("ABCD", textOf host)
    heard.Clear()
    ctrl w 'z'
    Assert.Equal("ABC", textOf host)
    Assert.Equal((3, 3), selection host)
    ctrl w 'z'
    Assert.Equal("", textOf host)
    ctrl w 'y'
    Assert.Equal("ABC", textOf host)
    Assert.Equal<string list>([ "ABC"; ""; "ABC" ], List.ofSeq heard)

[<Fact>]
let ``text the view sets for another reason clears the history; a view that refuses an edit leaves nothing to undo`` () =
    let dispatchRef: (Msg -> unit) ref = ref ignore
    let update msg (_: string) =
        match msg with
        | Typed text -> text
        | Clear -> "new"
    let view (model: string) dispatch =
        dispatchRef.Value <- dispatch
        page [ TextBox.text model; TextBox.onTextChanged (fun t -> dispatch (Typed t)) ]
    let _, w, host = showApp [ App.program "" update view ]
    focus w
    typeText w "abc"
    dispatchRef.Value Clear
    settle w
    Assert.Equal("new", textOf host)
    ctrl w 'z'
    Assert.Equal("new", textOf host)
    // From here on there is a history again.
    typeText w "!"
    ctrl w 'z'
    Assert.Equal("new", textOf host)
    // A view that never follows: the typed "!" is put back, and there is nothing to undo or redo.
    let heard = ResizeArray<string>()
    let _, w, host = show (page [ TextBox.text "fixed"; TextBox.onTextChanged heard.Add ])
    focus w
    key w Key.End
    typeText w "!"
    Assert.Equal("fixed", textOf host)
    heard.Clear()
    ctrl w 'z'
    ctrl w 'y'
    Assert.Empty heard
    Assert.Equal("fixed", textOf host)

[<Fact>]
let ``a focused TextBox claims the undo, redo, copy and cut keys from the browser, and leaves Ctrl+V to it`` () =
    let _, w, _ = show (page [])
    Assert.Empty w.Claimed
    focus w
    let control = Modifiers.Control
    for c in [ 'z'; 'y'; 'c'; 'x' ] do
        Assert.Contains((Key.Char c, control), w.Claimed)
    Assert.Contains((Key.Char 'z', control ||| Modifiers.Shift), w.Claimed)
    Assert.DoesNotContain((Key.Char 'v', control), w.Claimed)
    // Once: focus coming back claims nothing more.
    let count = w.Claimed.Length
    key w Key.Tab
    keyWith w Modifiers.Shift Key.Tab
    Assert.Equal(count, w.Claimed.Length)

[<Fact>]
let ``ctx.useClipboard copies from a handler and reads what is there`` () =
    let read = ResizeArray<string>()
    let view =
        Component.define "Copier" (fun ctx () ->
            let clipboard = ctx.useClipboard ()
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.text "Copy"; Button.onClick (fun _ -> clipboard.SetText "a link") ]
                    Button.create [
                        Button.text "Read"
                        // The desktop (and this fake) answers at once.
                        Button.onClick (fun _ ->
                            let reading = clipboard.GetText()
                            if reading.IsCompletedSuccessfully then read.Add reading.Result)
                    ]
                ]
            ])
    let platform, w, host = show (view ())
    let buttons = all host.Tree.Root.Value |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, Button.Type)) |> List.ofSeq
    let click (e: Element) =
        let p = e.PositionInTree
        let at: PixelPoint = { X = p.X + 5.0; Y = p.Y + 5.0 }
        w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        settle w
    click buttons[0]
    Assert.Equal("a link", platform.Clipboard)
    platform.Clipboard <- "from elsewhere"
    click buttons[1]
    Assert.Equal<string list>([ "from elsewhere" ], List.ofSeq read)
