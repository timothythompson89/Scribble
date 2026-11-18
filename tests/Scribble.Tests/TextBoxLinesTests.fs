/// M9 step 9c: the multi-line TextBox on the fake platform (DESIGN.md, "9c: the multi-line TextBox"): where layout
/// lines come from, line breaks and tabs in the editing engine, wrapping as a TextBlock wraps, growing and
/// scrolling, every key and gesture that moves by lines, the caret at a wrap, the drag timer, `acceptsTab`, and
/// work that doesn't grow with the text. The stand-in shaper makes every UTF-16 unit 10 DIPs wide at size 20; the
/// boxes here are 100 DIPs of text wide: ten characters a line.
module Scribble.Tests.TextBoxLinesTests

open System
open System.Diagnostics
open Xunit
open Scribble.Platform
open Scribble.Text
open Scribble.Core
open Scribble.Tests.AppTests
open Scribble.Tests.TestShaping

// ---- Layout lines and tabs ----

let private font () = Fonts.Regular()
let private lineHeight () = (textSystem ()).LineHeight(font (), 20.0)

[<Fact>]
let ``a layout's lines say where they came from, the spaces at a wrap included`` () =
    let text = textSystem ()
    let layout = text.Layout("hello world foo\nab\n\nlast", font (), 20.0, 100.0, Wrap, NoTrimming, 0)
    let lines = [ for l in layout.Lines -> l.Text, l.Start, l.End ]
    // "hello " keeps its space (undrawn); an empty paragraph is an empty line where it stands.
    Assert.Equal<(string * int * int) list>([ "hello", 0, 6; "world foo", 6, 15; "ab", 16, 18; "", 19, 19; "last", 20, 24 ], lines)
    // A word wider than the line breaks inside it, with no space to keep.
    let long = text.Layout("abcdefghijklmno", font (), 20.0, 100.0, Wrap, NoTrimming, 0)
    Assert.Equal<(string * int * int) list>([ "abcdefghij", 0, 10; "klmno", 10, 15 ], [ for l in long.Lines -> l.Text, l.Start, l.End ])
    // Not wrapped: one line a paragraph.
    let plain = text.Layout("hello world foo\nab", font (), 20.0, Double.PositiveInfinity, NoWrap, NoTrimming, 0)
    Assert.Equal<(int * int) list>([ 0, 15; 16, 18 ], [ for l in plain.Lines -> l.Start, l.End ])

[<Fact>]
let ``a tab is four spaces wide`` () =
    let text = textSystem ()
    Assert.Equal<float[]>([| 0.0; 10.0; 50.0; 60.0 |], text.CaretStops("a\tb", font (), 20.0))
    Assert.Equal(60.0, text.Measure("a\tb", font (), 20.0).Width)

// ---- The engine ----

let private at (text: string) (caret: int) = { Text = text; Anchor = caret; Caret = caret }

[<Fact>]
let ``line breaks and tabs are kept only where the box takes them`` () =
    Assert.Equal("a\nb c", TextEdit.cleanFor true false "a\r\nb\tc")
    Assert.Equal("a\nb\nc", TextEdit.cleanFor true false "a\rb\nc")
    Assert.Equal("a b\tc", TextEdit.cleanFor false true "a\nb\tc")
    Assert.Equal("a\nb\tc", TextEdit.cleanFor true true "a\nb\tc\u0007")
    Assert.Equal("a b c", TextEdit.clean "a\nb\tc")
    Assert.Equal(at "ab\ncd" 3, TextEdit.insertInto true false "\n" 0 (at "abcd" 2))
    // A line break is one character to maxLength.
    Assert.Equal(at "ab\n" 3, TextEdit.insertInto true false "\ncd" 3 (at "ab" 2))
    Assert.Equal(3, TextEdit.graphemes "a\nb")

[<Fact>]
let ``words stop at a line's end, and a paragraph is what lies between two line breaks`` () =
    let text = "ab cd\nef  gh\n\nij"
    Assert.Equal(5, TextEdit.nextWord text 3)
    Assert.Equal(6, TextEdit.nextWord text 5)
    Assert.Equal(5, TextEdit.previousWord text 6)
    Assert.Equal(struct (3, 5), TextEdit.wordAt text 4)
    Assert.Equal(struct (5, 6), TextEdit.wordAt text 5)
    Assert.Equal(6, TextEdit.paragraphStart text 9)
    Assert.Equal(12, TextEdit.paragraphEnd text 9)
    Assert.Equal(0, TextEdit.paragraphStart text 5)
    Assert.Equal(5, TextEdit.paragraphEnd text 5)
    Assert.Equal((6, 12), (let s = TextEdit.selectParagraphAt 8 (at text 0) in s.Anchor, s.Caret))
    // An empty paragraph.
    Assert.Equal((13, 13), (let s = TextEdit.selectParagraphAt 13 (at text 0) in s.Anchor, s.Caret))
    Assert.Equal((14, 16), (let s = TextEdit.selectParagraphAt 16 (at text 0) in s.Anchor, s.Caret))

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

/// A multi-line TextBox whose text area is 100 DIPs wide from x = 13 and starts at y = 8, above a button.
let private page (settings: Setting<TextBox> list) =
    StackPanel.create [
        StackPanel.children [
            TextBox.create (
                [ TextBox.fontSize 20.0
                  TextBox.width 126.0
                  TextBox.multiline true
                  TextBox.horizontalAlignment HorizontalAlignment.Left ]
                @ settings
            )
            Button.create [ Button.text "Next"; Button.horizontalAlignment HorizontalAlignment.Left ]
        ]
    ]

let private showApp (settings: AppSetting<'Msg> list) =
    let platform, w, device, app = startAll settings
    // Tall enough for the boxes here to grow.
    w.Size <- { Width = 400; Height = 900 }
    w.Send(Resized w.Size)
    let host = app :?> ElementHost
    settle w
    platform, w, device, host

let private show (view: View) =
    let platform, w, _, host = showApp [ App.view view ]
    platform, w, host

let private boxOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TextBox.Type))
let private stateOf (host: ElementHost) = TextBox.stateOf (boxOf host)
let private textOf (host: ElementHost) = (TextBox.editOf (boxOf host)).Text
let private selection (host: ElementHost) = let s = TextBox.editOf (boxOf host) in s.Anchor, s.Caret
let private caret (host: ElementHost) = (TextBox.editOf (boxOf host)).Caret

/// The x of the caret place before character `index` of a line, and the middle of line `n` as drawn unscrolled.
let private xOf (index: int) = 13.0 + 10.0 * float index
let private yOf (n: int) = 8.0 + (float n + 0.5) * lineHeight ()

let private press (w: FakeWindow) (mods: Modifiers) (x: float) (y: float) =
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, mods))
let private release (w: FakeWindow) (x: float) (y: float) =
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private moveTo (w: FakeWindow) (x: float) (y: float) = w.Send(PointerMoved(Mouse, 0, { X = x; Y = y }, Modifiers.None))

/// A click on its own: long enough after the last one not to count as a double-click.
let private clickAt (w: FakeWindow) (x: float) (y: float) =
    w.Time.AdvanceMs 1000.0
    press w Modifiers.None x y
    release w x y
    settle w

/// A click soon after the last one (a tenth of a second).
let private clickAgain (w: FakeWindow) (x: float) (y: float) =
    w.Time.AdvanceMs 100.0
    press w Modifiers.None x y
    release w x y
    settle w

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

let private numbered (count: int) = String.Join("\n", Seq.init count string)

[<Fact>]
let ``a multi-line TextBox wraps where a TextBlock would`` () =
    let sentence = "The quick brown fox jumps over the lazy dog\n\nand a second paragraph, with punctuation-marks"
    let _, _, host = show (page [ TextBox.text sentence ])
    let s = stateOf host
    let shown = [ for p in s.Lines.Paragraphs do for l in p.Lines -> l.Text ]
    let expected = (textSystem ()).Layout(sentence, font (), 20.0, 100.0, Wrap, NoTrimming, 0)
    Assert.Equal<string list>([ for l in expected.Lines -> l.Text ], shown)
    Assert.Equal(expected.Lines.Length, s.Lines.LineCount)
    Assert.True(s.Lines.LineCount > 6)

[<Fact>]
let ``it is as high as its text, between minLines and maxLines, and scrolls beyond`` () =
    let lh = lineHeight ()
    let high (lines: int) = float lines * lh + 16.0
    let near (expected: float) (actual: float) = Assert.InRange(actual, expected - 1.0, expected + 1.0)
    let _, w, host = show (page [ TextBox.maxLines 5 ])
    let box = boxOf host
    // Empty: three lines, and nothing to scroll.
    near (high 3) box.Bounds.Height
    Assert.Equal(0.0, (stateOf host).MaxScrollY)
    clickAt w (xOf 0) (yOf 0)
    typeText w "a"
    for _ in 1 .. 3 do
        key w Key.Enter
    // Four lines: it grew by one.
    Assert.Equal("a\n\n\n", textOf host)
    near (high 4) box.Bounds.Height
    for _ in 1 .. 4 do
        key w Key.Enter
    // Eight lines in a box of five: scrolled so the caret's line (the last) shows.
    near (high 5) box.Bounds.Height
    let s = stateOf host
    near (3.0 * lh) s.ScrollY
    near (3.0 * lh) s.MaxScrollY
    // minLines alone, and a height from the layout over both.
    let _, _, host = show (page [ TextBox.minLines 2 ])
    near (high 2) (boxOf host).Bounds.Height
    let _, _, host = show (page [ TextBox.height 200.0; TextBox.maxLines 2 ])
    Assert.Equal(200.0, (boxOf host).Bounds.Height)

type private Msg = SetText of string

[<Fact>]
let ``text from the view makes the box higher in the same frame`` () =
    let lh = lineHeight ()
    let dispatchRef: (Msg -> unit) ref = ref ignore
    let view (model: string) dispatch =
        dispatchRef.Value <- dispatch
        page [ TextBox.text model ]
    let _, w, _, host = showApp [ App.program "one" (fun (SetText t) _ -> t) view ]
    let box = boxOf host
    Assert.InRange(box.Bounds.Height, 3.0 * lh + 15.0, 3.0 * lh + 17.0)
    dispatchRef.Value(SetText(numbered 6))
    Assert.True(w.DeliverFrame())
    Assert.InRange(box.Bounds.Height, 6.0 * lh + 15.0, 6.0 * lh + 17.0)
    Assert.Equal(6, (stateOf host).Lines.LineCount)

[<Fact>]
let ``Enter and Shift+Enter make a new line; Ctrl+Enter submits`` () =
    let submitted = ResizeArray<string>()
    let _, w, host = show (page [ TextBox.onSubmitted submitted.Add ])
    clickAt w (xOf 0) (yOf 0)
    typeText w "ab"
    key w Key.Enter
    typeText w "cd"
    keyWith w Modifiers.Shift Key.Enter
    Assert.Equal("ab\ncd\n", textOf host)
    Assert.Equal((6, 6), selection host)
    Assert.Empty submitted
    keyWith w Modifiers.Control Key.Enter
    Assert.Equal<string list>([ "ab\ncd\n" ], List.ofSeq submitted)
    Assert.Equal("ab\ncd\n", textOf host)
    // A read-only box takes no line break.
    let _, w, host = show (page [ TextBox.text "fixed"; TextBox.isReadOnly true ])
    clickAt w (xOf 0) (yOf 0)
    key w Key.Enter
    Assert.Equal("fixed", textOf host)

[<Fact>]
let ``Up and Down move a line at a time and keep to the x they started from`` () =
    let _, w, host = show (page [ TextBox.text "abcdefgh\nab\nabcdefgh" ])
    clickAt w (xOf 6) (yOf 0)
    Assert.Equal((6, 6), selection host)
    key w Key.Down
    // The short line: its end.
    Assert.Equal((11, 11), selection host)
    key w Key.Down
    Assert.Equal((18, 18), selection host)
    // On the last line Down goes to the end; on the first Up goes to the start.
    key w Key.Down
    Assert.Equal((20, 20), selection host)
    clickAt w (xOf 6) (yOf 2)
    key w Key.Up
    key w Key.Up
    Assert.Equal((6, 6), selection host)
    key w Key.Up
    Assert.Equal((0, 0), selection host)
    // Shift selects by lines; a sideways move ends the run, and the next Down starts from the new x.
    clickAt w (xOf 1) (yOf 0)
    keyWith w Modifiers.Shift Key.Down
    Assert.Equal((1, 10), selection host)
    key w Key.Right
    key w Key.Up
    Assert.Equal((1, 1), selection host)

[<Fact>]
let ``Home and End go to the ends of the line as drawn, and End stays on its line at a wrap`` () =
    // Wraps after "hello ": the place at 6 is the end of the first line and the start of the second.
    let _, w, host = show (page [ TextBox.text "hello world foo" ])
    let s = stateOf host
    let lineOfCaret () = let struct (_, _, n) = MultiLine.locate s.Lines (caret host) s.AtLineEnd in n
    clickAt w (xOf 2) (yOf 0)
    key w Key.End
    Assert.Equal((6, 6), selection host)
    Assert.Equal(0, lineOfCaret ())
    key w Key.Home
    Assert.Equal((0, 0), selection host)
    key w Key.End
    // Right, then Left: the same place, now as the start of the second line.
    key w Key.Right
    key w Key.Left
    Assert.Equal((6, 6), selection host)
    Assert.Equal(1, lineOfCaret ())
    key w Key.Home
    Assert.Equal((6, 6), selection host)
    key w Key.End
    Assert.Equal((15, 15), selection host)
    // A click past the end of the first line: its end, not the second line's start.
    clickAt w (xOf 9) (yOf 0)
    Assert.Equal((6, 6), selection host)
    Assert.Equal(0, lineOfCaret ())
    // With Shift it selects; with Ctrl it is the text's ends.
    key w Key.Home
    keyWith w Modifiers.Shift Key.End
    Assert.Equal((0, 6), selection host)
    keyWith w Modifiers.Control Key.End
    Assert.Equal((15, 15), selection host)
    keyWith w Modifiers.Control Key.Home
    Assert.Equal((0, 0), selection host)

[<Fact>]
let ``PageUp and PageDown move by the box's height and scroll with the caret`` () =
    let lh = lineHeight ()
    // Twenty lines "0" to "19" in a box of three: line n starts at index 2n up to line 10.
    let _, w, host = show (page [ TextBox.text (numbered 20); TextBox.maxLines 3 ])
    clickAt w (xOf 0) (yOf 0)
    key w Key.PageDown
    Assert.Equal((4, 4), selection host)
    Assert.Equal(0.0, (stateOf host).ScrollY)
    key w Key.PageDown
    key w Key.PageDown
    Assert.Equal((12, 12), selection host)
    // Line 6 is the last that shows.
    Assert.InRange((stateOf host).ScrollY, 4.0 * lh - 1.0, 4.0 * lh + 1.0)
    keyWith w Modifiers.Shift Key.PageUp
    Assert.Equal((12, 8), selection host)
    key w Key.PageUp
    key w Key.PageUp
    key w Key.PageUp
    Assert.Equal((0, 0), selection host)
    Assert.Equal(0.0, (stateOf host).ScrollY)

[<Fact>]
let ``a click goes to the line under it, a drag selects across lines, a triple-click selects the paragraph`` () =
    let _, w, host = show (page [ TextBox.text "abcdefgh\nab\nabcdefgh" ])
    clickAt w (xOf 3) (yOf 2)
    Assert.Equal((15, 15), selection host)
    // Past a short line's end: its end.
    clickAt w (xOf 8) (yOf 1)
    Assert.Equal((11, 11), selection host)
    // A drag from the first line to the third.
    w.Time.AdvanceMs 1000.0
    press w Modifiers.None (xOf 1) (yOf 0)
    moveTo w (xOf 2) (yOf 2)
    release w (xOf 2) (yOf 2)
    settle w
    Assert.Equal((1, 14), selection host)
    // Shift+click extends from the anchor.
    w.Time.AdvanceMs 1000.0
    press w Modifiers.Shift (xOf 1) (yOf 1)
    release w (xOf 1) (yOf 1)
    Assert.Equal((1, 10), selection host)
    // Three clicks on the first line: its paragraph, not everything.
    clickAt w (xOf 2) (yOf 0)
    clickAgain w (xOf 2) (yOf 0)
    Assert.Equal((0, 8), selection host)
    clickAgain w (xOf 2) (yOf 0)
    Assert.Equal((0, 8), selection host)
    clickAt w (xOf 1) (yOf 1)
    clickAgain w (xOf 1) (yOf 1)
    clickAgain w (xOf 1) (yOf 1)
    Assert.Equal((9, 11), selection host)
    // Below the last line: the end of the text.
    let _, w, host = show (page [ TextBox.text "ab\ncd" ])
    clickAt w (xOf 0) (yOf 2)
    Assert.Equal((5, 5), selection host)

[<Fact>]
let ``the wheel scrolls it while there is more that way, and is left to the page at the ends`` () =
    // The box in a page that scrolls too.
    let view =
        ScrollViewer.create [
            ScrollViewer.height 200.0
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [
                        page [ TextBox.text (numbered 20); TextBox.maxLines 3 ]
                        Border.create [ Border.height 1000.0 ]
                    ]
                ])
        ]
    let _, w, host = show view
    let s = stateOf host
    let outer = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, ScrollViewer.Type)) |> ScrollViewer.stateOf
    let pageMoved () = outer.Y > 0.0 || (outer.Animating && outer.ToY > 0.0)
    // Over the box, wherever the page stands in the window.
    let wheel (notches: float) =
        let p = (boxOf host).PositionInTree
        host.HandleEvent(PointerWheel({ X = p.X + 50.0; Y = p.Y + 30.0 }, 0.0, notches, Modifiers.None)) |> ignore
    Assert.True(s.MaxScrollY > 0.0)
    wheel -1.0
    Assert.Equal(48.0, s.ScrollY)
    Assert.False(pageMoved ())
    settle w
    // The caret stayed where it was (out of view now): scrolling doesn't bring it back.
    Assert.Equal(48.0, s.ScrollY)
    wheel 1.0
    Assert.Equal(0.0, s.ScrollY)
    let mutable notches = 0
    while s.ScrollY < s.MaxScrollY && notches < 100 do
        wheel -1.0
        notches <- notches + 1
    Assert.Equal(s.MaxScrollY, s.ScrollY)
    Assert.False(pageMoved ())
    // At its end: the page takes the wheel.
    wheel -1.0
    Assert.True(pageMoved ())
    // Text that fits takes no wheel at all.
    let _, _, host = show (page [ TextBox.text "one" ])
    host.HandleEvent(PointerWheel({ X = 50.0; Y = 30.0 }, 0.0, -1.0, Modifiers.None)) |> ignore
    Assert.Equal(0.0, (stateOf host).ScrollY)

[<Fact>]
let ``the scrollbar's thumb can be dragged, and a press beside it pages`` () =
    let _, w, host = show (page [ TextBox.text (numbered 20); TextBox.maxLines 3 ])
    let s = stateOf host
    let box = boxOf host
    let x = box.Bounds.Width - 5.0
    clickAt w (xOf 0) (yOf 0)
    // The thumb starts at the top.
    press w Modifiers.None x 6.0
    moveTo w x 16.0
    Assert.True(s.ScrollY > 0.0)
    let after = s.ScrollY
    moveTo w x 500.0
    Assert.Equal(s.MaxScrollY, s.ScrollY)
    release w x 500.0
    settle w
    Assert.True(after < s.ScrollY)
    // The caret and the selection didn't move.
    Assert.Equal((0, 0), selection host)
    // A press above the thumb: a page back.
    w.Time.AdvanceMs 1000.0
    let before = s.ScrollY
    press w Modifiers.None x 6.0
    release w x 6.0
    settle w
    Assert.True(s.ScrollY < before)
    Assert.Equal((0, 0), selection host)

[<Fact>]
let ``a drag held below the box keeps selecting and scrolling, and stops with the button`` () =
    let text = numbered 20
    let _, w, host = show (page [ TextBox.text text; TextBox.maxLines 3 ])
    let s = stateOf host
    let below = (boxOf host).Bounds.Height + 30.0
    w.Time.AdvanceMs 1000.0
    press w Modifiers.None (xOf 0) (yOf 0)
    moveTo w (xOf 0) below
    settle w
    let first = caret host
    Assert.True(first > 0)
    // Nothing moves the pointer: the timer goes on, a step at a time.
    let mutable last = first
    for _ in 1 .. 3 do
        w.Time.AdvanceMs 50.0
        settle w
        Assert.True(caret host > last)
        last <- caret host
    Assert.True(s.ScrollY > 0.0)
    Assert.Equal(0, (TextBox.editOf (boxOf host)).Anchor)
    // To the end of the text: then nothing more is drawn while the button stays down.
    let mutable n = 0
    while caret host < text.Length && n < 100 do
        w.Time.AdvanceMs 50.0
        settle w
        n <- n + 1
    Assert.Equal(text.Length, caret host)
    w.Time.AdvanceMs 200.0
    Assert.False w.FrameRequested
    // Back inside: the timer ends, and the selection follows the pointer again.
    moveTo w (xOf 0) 20.0
    settle w
    let inside = caret host
    Assert.True(inside < text.Length)
    w.Time.AdvanceMs 200.0
    settle w
    Assert.Equal(inside, caret host)
    Assert.False s.DragTimer
    // Out again, then the button goes up: nothing more happens.
    moveTo w (xOf 0) below
    settle w
    release w (xOf 0) below
    settle w
    let released = selection host
    w.Time.AdvanceMs 300.0
    settle w
    Assert.Equal(released, selection host)
    Assert.False s.DragTimer

[<Fact>]
let ``a drag held past the side of a one-line box keeps selecting and scrolling`` () =
    let view =
        TextBox.create [
            TextBox.fontSize 20.0
            TextBox.width 126.0
            TextBox.horizontalAlignment HorizontalAlignment.Left
            TextBox.text (String.replicate 8 "abcdefghijklmnopqrstuvwxyz")
        ]
    let _, w, host = show view
    w.Time.AdvanceMs 1000.0
    press w Modifiers.None (xOf 2) 15.0
    moveTo w 200.0 15.0
    settle w
    let first = caret host
    let mutable last = first
    for _ in 1 .. 3 do
        w.Time.AdvanceMs 50.0
        settle w
        Assert.True(caret host > last)
        last <- caret host
    Assert.True((stateOf host).Scroll > 0.0)
    release w 200.0 15.0
    settle w
    w.Time.AdvanceMs 300.0
    settle w
    Assert.Equal(last, caret host)

[<Fact>]
let ``Tab moves the focus unless acceptsTab, which types a tab and keeps pasted ones`` () =
    let platform, w, host = show (page [])
    clickAt w (xOf 0) (yOf 0)
    key w Key.Tab
    Assert.False((boxOf host).IsFocused)
    Assert.Equal("", textOf host)
    keyWith w Modifiers.Shift Key.Tab
    Assert.True((boxOf host).IsFocused)
    platform.Clipboard <- "a\tb\r\nc"
    keyWith w Modifiers.Control (Key.Char 'v')
    // The tab became a space; the line break is kept as "\n".
    Assert.Equal("a b\nc", textOf host)
    let platform, w, host = show (page [ TextBox.acceptsTab true ])
    clickAt w (xOf 0) (yOf 0)
    typeText w "a"
    key w Key.Tab
    typeText w "b"
    Assert.True((boxOf host).IsFocused)
    Assert.Equal("a\tb", textOf host)
    platform.Clipboard <- "\tc"
    keyWith w Modifiers.Control (Key.Char 'v')
    Assert.Equal("a\tb\tc", textOf host)
    // Shift+Tab still leaves.
    keyWith w Modifiers.Shift Key.Tab
    Assert.False((boxOf host).IsFocused)
    // One line with acceptsTab: tabs but no line breaks.
    let view = TextBox.create [ TextBox.fontSize 20.0; TextBox.acceptsTab true ]
    let platform, w, host = show view
    clickAt w 20.0 15.0
    platform.Clipboard <- "a\tb\nc"
    keyWith w Modifiers.Control (Key.Char 'v')
    key w Key.Tab
    Assert.Equal("a\tb c\t", textOf host)

[<Fact>]
let ``tabbing into several lines keeps the caret; a password is one line whatever multiline says`` () =
    let _, w, host = show (page [ TextBox.text "some notes" ])
    key w Key.Tab
    Assert.True((boxOf host).IsFocused)
    Assert.Equal((0, 0), selection host)
    let _, w, host = show (page [ TextBox.isPassword true; TextBox.minLines 4 ])
    let lh = lineHeight ()
    Assert.InRange((boxOf host).Bounds.Height, lh + 15.0, lh + 17.0)
    clickAt w (xOf 0) 15.0
    typeText w "ab"
    key w Key.Enter
    Assert.Equal("ab", textOf host)

[<Fact>]
let ``a line break is an undo step of its own, and one character to maxLength`` () =
    let _, w, host = show (page [])
    clickAt w (xOf 0) (yOf 0)
    typeText w "ab"
    key w Key.Enter
    typeText w "cd"
    let undo () = keyWith w Modifiers.Control (Key.Char 'z')
    undo ()
    Assert.Equal("ab\n", textOf host)
    undo ()
    Assert.Equal("ab", textOf host)
    undo ()
    Assert.Equal("", textOf host)
    keyWith w Modifiers.Control (Key.Char 'y')
    keyWith w Modifiers.Control (Key.Char 'y')
    Assert.Equal("ab\n", textOf host)
    Assert.Equal((3, 3), selection host)
    let _, w, host = show (page [ TextBox.maxLength 3 ])
    clickAt w (xOf 0) (yOf 0)
    typeText w "ab"
    key w Key.Enter
    typeText w "c"
    key w Key.Enter
    Assert.Equal("ab\n", textOf host)

[<Fact>]
let ``a selection across lines is deleted and replaced as one`` () =
    let platform, w, host = show (page [])
    clickAt w (xOf 0) (yOf 0)
    platform.Clipboard <- "abcd\nefgh\nijkl"
    keyWith w Modifiers.Control (Key.Char 'v')
    Assert.Equal("abcd\nefgh\nijkl", textOf host)
    // From the middle of the first line to the middle of the third.
    w.Time.AdvanceMs 1000.0
    press w Modifiers.None (xOf 2) (yOf 0)
    moveTo w (xOf 2) (yOf 2)
    release w (xOf 2) (yOf 2)
    settle w
    Assert.Equal((2, 12), selection host)
    keyWith w Modifiers.Control (Key.Char 'c')
    Assert.Equal("cd\nefgh\nij", platform.Clipboard)
    typeText w "X"
    Assert.Equal("abXkl", textOf host)
    Assert.Equal(1, (stateOf host).Lines.LineCount)
    keyWith w Modifiers.Control (Key.Char 'z')
    Assert.Equal("abcd\nefgh\nijkl", textOf host)
    Assert.Equal((2, 12), selection host)
    // Backspace at a line's start joins it to the line before.
    clickAt w (xOf 0) (yOf 1)
    key w Key.Backspace
    Assert.Equal("abcdefgh\nijkl", textOf host)

// ---- Work that doesn't grow with the text ----

/// A box of three lines holding `count` lines of text (pasted, so it keeps its own text), the caret at the end.
let private filled (count: int) =
    let platform, w, device, host = showApp [ App.view (page [ TextBox.maxLines 3 ]) ]
    clickAt w (xOf 0) (yOf 0)
    // Every line the same, so what shows is the same however many there are.
    platform.Clipboard <- String.Join("\n", Seq.replicate count "some text")
    keyWith w Modifiers.Control (Key.Char 'v')
    w, device, host

[<Fact>]
let ``an edit lays out its own paragraph only, and only the lines that show are drawn`` () =
    let small, smallDevice, smallHost = filled 100
    let big, bigDevice, bigHost = filled 10_000
    Assert.Equal(10_000, (stateOf bigHost).Lines.LineCount)
    // The same drawing for 100 lines and 10,000.
    Assert.Equal(smallDevice.LastShapes, bigDevice.LastShapes)
    for w, host in [ small, smallHost; big, bigHost ] do
        let s = stateOf host
        let before = s.LaidOut
        typeText w "x"
        Assert.Equal(1, s.LaidOut - before)
        // A new line at the text's end: the new, empty paragraph.
        let before = s.LaidOut
        key w Key.Enter
        Assert.Equal(1, s.LaidOut - before)
        // A new line inside a paragraph: its two halves.
        key w Key.Up
        key w Key.Right
        let before = s.LaidOut
        key w Key.Enter
        Assert.Equal(2, s.LaidOut - before)
        // Moving and selecting lay out nothing.
        let before = s.LaidOut
        key w Key.Up
        keyWith w Modifiers.Shift Key.PageUp
        keyWith w Modifiers.Control Key.Home
        Assert.Equal(0, s.LaidOut - before)
    Assert.Equal(smallDevice.LastShapes, bigDevice.LastShapes)

[<Timing.TimingFact; Trait("Category", "Timing")>]
let ``typing into 10,000 lines is quick`` () =
    let w, _, host = filled 10_000
    // In the middle of the text.
    keyWith w Modifiers.Control Key.Home
    for _ in 1 .. 20 do
        key w Key.PageDown
    let typeOne () =
        let sw = Stopwatch.StartNew()
        typeText w "x"
        sw.Elapsed.TotalMilliseconds
    for _ in 1 .. 20 do
        typeOne () |> ignore
    let best = Seq.init 50 (fun _ -> typeOne ()) |> Seq.min
    Console.WriteLine($"TextBox, 10,000 lines: a typed character and its frame {best:F3} ms")
    Assert.Equal(10_000, (stateOf host).Lines.Paragraphs.Length)
    // Generous: measured well under a millisecond in Release.
    Assert.True(best < 5.0, $"a typed character took {best:F2} ms")
