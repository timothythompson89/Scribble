namespace Scribble.Core

open System
open System.Threading.Tasks
open Scribble.Accessibility
open Scribble.Rendering
open Scribble.Text

/// One paragraph of a multi-line TextBox (the text between two line breaks) as it wraps: its lines, whose `Start`
/// and `End` are indices into the paragraph, and each line's caret places, measured when first asked for.
[<Sealed; AllowNullLiteral>]
type internal ParagraphLines(text: string, lines: TextLine[]) =
    member _.Text = text
    member _.Lines = lines
    member val Stops: float[][] = Array.zeroCreate lines.Length

/// A multi-line TextBox's text as it wraps at one width: its paragraphs, and where each starts in the text and
/// among the lines. An edit keeps the paragraphs it didn't touch (DESIGN.md, "9c", decision 44).
[<Sealed; AllowNullLiteral>]
type internal TextLines(paragraphs: ParagraphLines[], width: float, font: Font, size: float) =
    let firstLine = Array.zeroCreate<int> (paragraphs.Length + 1)
    let offset = Array.zeroCreate<int> (paragraphs.Length + 1)
    do
        for i in 0 .. paragraphs.Length - 1 do
            firstLine[i + 1] <- firstLine[i] + paragraphs[i].Lines.Length
            offset[i + 1] <- offset[i] + paragraphs[i].Text.Length + 1

    /// The last paragraph whose entry in `starts` is at or before `value`.
    let find (starts: int[]) (value: int) =
        let mutable lo = 0
        let mutable hi = paragraphs.Length - 1
        while lo < hi do
            let mid = (lo + hi + 1) / 2
            if starts[mid] <= value then lo <- mid else hi <- mid - 1
        lo

    member _.Paragraphs = paragraphs
    member _.Width = width
    member _.Font = font
    member _.Size = size
    member _.LineCount = firstLine[paragraphs.Length]
    /// The number of paragraph `p`'s first line, among all lines.
    member _.FirstLine(p: int) = firstLine[p]
    /// Where paragraph `p` starts in the text.
    member _.Offset(p: int) = offset[p]
    /// The paragraph the text index is in (a line break belongs to the paragraph it ends).
    member _.ParagraphAt(index: int) = find offset index
    member _.ParagraphOfLine(line: int) = find firstLine line

/// Lines and caret places of a multi-line TextBox.
module internal MultiLine =
    /// `value` wrapped at `width`. Paragraphs at the start and the end that `old` has (same font, size and width)
    /// are kept; only those between are laid out. Also how many were laid out.
    let build (text: TextSystem) (old: TextLines) (value: string) (font: Font) (size: float) (width: float) : struct (TextLines * int) =
        let parts = value.Split '\n'
        let n = parts.Length
        let result = Array.zeroCreate<ParagraphLines> n
        let mutable first = 0
        let mutable last = n
        if not (isNull old) && obj.ReferenceEquals(old.Font, font) && old.Size = size && old.Width = width then
            let o = old.Paragraphs
            while first < n && first < o.Length && String.Equals(o[first].Text, parts[first], StringComparison.Ordinal) do
                result[first] <- o[first]
                first <- first + 1
            let mutable k = 1
            while n - k >= first && o.Length - k >= first && String.Equals(o[o.Length - k].Text, parts[n - k], StringComparison.Ordinal) do
                result[n - k] <- o[o.Length - k]
                k <- k + 1
            last <- n - k + 1
        for i in first .. last - 1 do
            result[i] <- ParagraphLines(parts[i], text.Layout(parts[i], font, size, width, Wrap, NoTrimming, 0).Lines)
        struct (TextLines(result, width, font, size), max 0 (last - first))

    /// The caret places of line `l` of paragraph `p`: by index into the line, from its start to its end.
    let stopsOf (text: TextSystem) (lines: TextLines) (p: int) (l: int) =
        let paragraph = lines.Paragraphs[p]
        match paragraph.Stops[l] with
        | null ->
            let line = paragraph.Lines[l]
            let stops = text.CaretStops(paragraph.Text.Substring(line.Start, line.End - line.Start), lines.Font, lines.Size)
            paragraph.Stops[l] <- stops
            stops
        | stops -> stops

    /// Where the caret at text index `index` is: its paragraph, its line in the paragraph, and that line's number
    /// among all lines. At a wrap the index is both the end of one line and the start of the next: the next,
    /// unless `atLineEnd` (decision 41).
    let locate (lines: TextLines) (index: int) (atLineEnd: bool) : struct (int * int * int) =
        let p = lines.ParagraphAt index
        let paragraph = lines.Paragraphs[p]
        let local = max 0 (min paragraph.Text.Length (index - lines.Offset p))
        let ls = paragraph.Lines
        let mutable l = 0
        let mutable go = true
        while go do
            if l = ls.Length - 1 || local < ls[l].End || (local = ls[l].End && atLineEnd && local > ls[l].Start) then go <- false
            else l <- l + 1
        struct (p, l, lines.FirstLine p + l)

    /// The x of the caret at `index` on line `l` of paragraph `p`, from the line's left edge.
    let caretX (text: TextSystem) (lines: TextLines) (p: int) (l: int) (index: int) =
        let stops = stopsOf text lines p l
        let line = lines.Paragraphs[p].Lines[l]
        stops[max 0 (min (stops.Length - 1) (index - lines.Offset p - line.Start))]

    /// The caret place on line `n` (among all lines) nearest to `x`: its text index, and whether it is the end of
    /// a line that wraps on.
    let indexAt (text: TextSystem) (lines: TextLines) (n: int) (x: float) : struct (int * bool) =
        let p = lines.ParagraphOfLine n
        let l = n - lines.FirstLine p
        let paragraph = lines.Paragraphs[p]
        let line = paragraph.Lines[l]
        let stops = stopsOf text lines p l
        let part = paragraph.Text.Substring(line.Start, line.End - line.Start)
        let mutable best = 0
        let mutable i = 0
        let mutable go = true
        while go do
            if abs (stops[i] - x) <= abs (stops[best] - x) then best <- i
            if i >= part.Length || stops[i] > x then go <- false else i <- TextEdit.nextStop part i
        struct (lines.Offset p + line.Start + best, l < paragraph.Lines.Length - 1 && best = part.Length && best > 0)

/// What a TextBox keeps: its text with the selection, where each caret place is, how far it is scrolled, a drag
/// in progress, the last press (for double and triple clicks), the caret's blinking, and its undo history.
[<Sealed; AllowNullLiteral>]
type internal TextBoxState() =
    member val Edit = TextEdit.empty with get, set
    /// The `text` setting's value when it was last looked at: another value since means the view changed it.
    member val Seen: string = "" with get, set
    /// What the caret places below were measured for (one line).
    member val StopsText: string = null with get, set
    member val StopsFont: Font = Unchecked.defaultof<Font> with get, set
    member val StopsSize = 0.0 with get, set
    member val StopsPassword = false with get, set
    /// What is drawn (bullets for a password) and the x of every caret place, by index into the text (one line).
    member val Display = "" with get, set
    member val Stops: float[] = [| 0.0 |] with get, set
    /// How far the text is scrolled to the left, in DIPs (one line).
    member val Scroll = 0.0 with get, set
    member val Dragging = false with get, set
    /// The word a double-click selected: a drag on from it selects by words.
    member val DragWord: struct (int * int) voption = ValueNone with get, set
    member val PressTime = TimeSpan.MinValue with get, set
    member val PressX = 0.0 with get, set
    member val PressY = 0.0 with get, set
    member val PressCount = 0 with get, set
    /// The caret shows (it blinks while focused, and stays on once `BlinkTicks` runs out).
    member val CaretOn = true with get, set
    member val BlinkTicks = 0 with get, set
    /// Counts restarts: a timer from an earlier one does nothing.
    member val BlinkGeneration = 0 with get, set
    /// What can be undone and redone.
    member val History = TextHistory.empty with get, set
    /// A controlled box told its view of new text and the view hasn't had its turn: 1 after an edit, 2 after an
    /// undo or a redo; 0 otherwise.
    member val Pending = 0 with get, set
    /// The browser has been told which keys are this box's (once is enough).
    member val Claimed = false with get, set
    /// Where the pointer was last seen during a drag, and the timer that keeps selecting while it is held outside
    /// the text (DESIGN.md, "9c", decision 37).
    member val LastX = 0.0 with get, set
    member val LastY = 0.0 with get, set
    member val DragTimer = false with get, set
    member val DragGeneration = 0 with get, set
    // ---- Several lines ----
    /// The text as it wraps, and the text that was wrapped.
    member val Lines: TextLines = null with get, set
    member val LinesText: string = null with get, set
    /// How many paragraphs have been laid out so far (tests: an edit lays out one).
    member val LaidOut = 0 with get, set
    /// The text area's width when it was last drawn (0: not yet).
    member val TextWidth = 0.0 with get, set
    /// How many lines high the last measure asked to be.
    member val MeasuredLines = 0 with get, set
    /// How far the text is scrolled up, the most it can be, and the heights of the text and of what shows.
    member val ScrollY = 0.0 with get, set
    member val MaxScrollY = 0.0 with get, set
    member val Extent = 0.0 with get, set
    member val ViewHeight = 0.0 with get, set
    /// The caret moved or the text changed: the next frame scrolls the caret into view.
    member val Reveal = true with get, set
    /// The caret stands at the end of a line that wraps on, not at the start of the next (decision 41).
    member val AtLineEnd = false with get, set
    /// The x that ↑ and ↓ keep to across short lines; NaN: the caret's own.
    member val WantedX = nan with get, set
    /// The scrollbar: the pointer is over it; it is being dragged from `BarDragStart` with the text at `BarDragOffset`.
    member val HoverBar = false with get, set
    member val BarDrag = false with get, set
    member val BarDragStart = 0.0 with get, set
    member val BarDragOffset = 0.0 with get, set
    // ---- Input methods and touch ----
    /// What an input method is composing ("": nothing) and where its caret stands in it. It shows in place of
    /// the selection and isn't part of the text until it is committed (DESIGN.md, "9d", decision 54).
    member val Composition = "" with get, set
    member val CompositionCaret = 0 with get, set
    /// A composition has been shown since the last key: the text typed next is its commit, an undo step of its own.
    member val Composed = false with get, set
    /// A finger went down on the box and hasn't lifted or started to scroll: lifting it places the caret.
    member val TouchDown = false with get, set
    /// A finger is scrolling the text.
    member val Panning = false with get, set
    // ---- Selecting by touch (DESIGN.md, "9g") ----
    /// The selection handles show: a finger used the box last (a key or the mouse puts them away).
    member val Touch = false with get, set
    /// The row of commands shows with them.
    member val Row = false with get, set
    /// The row didn't fit the window: it shows as a menu.
    member val RowVertical = false with get, set
    /// The row is out of sight while the text scrolls; a count of scrolls, so only the last brings it back.
    member val RowHeld = false with get, set
    member val RowGeneration = 0 with get, set
    /// A command of the row that leaves it open has run (Select all).
    member val KeepRow = false with get, set
    /// Nothing is selected and the caret's handle shows (for a few seconds after a tap); a count of restarts.
    member val CaretHandle = false with get, set
    member val CaretHandleGeneration = 0 with get, set
    /// The handle a finger drags (0: none; 1: the selection's start; 2: its end; 3: the caret's), the selection's
    /// other end, from the finger to the place in the text it moves, where the finger went down, and whether it
    /// has moved since.
    member val HandleDrag = 0 with get, set
    member val HandleFixed = 0 with get, set
    member val HandleDX = 0.0 with get, set
    member val HandleDY = 0.0 with get, set
    member val HandleX = 0.0 with get, set
    member val HandleY = 0.0 with get, set
    member val HandleMoved = false with get, set
    /// A long press selected a word (or placed the caret) and its finger, this one, is still down.
    member val LongPressed = false with get, set
    member val TouchId = 0 with get, set
    /// When the finger now down went down, and when and where the last tap did (for a double-tap).
    member val TouchTime = TimeSpan.MinValue with get, set
    member val TapTime = TimeSpan.MinValue with get, set
    member val TapX = 0.0 with get, set
    member val TapY = 0.0 with get, set
    /// Where the box stood in the window at the last frame (NaN: not known yet).
    member val OriginX = nan with get, set
    member val OriginY = nan with get, set
    /// Counts changes to the text, the selection and the row: what the layer shows is made again.
    member val TouchVersion = 0 with get, set
    /// What the layer above shows, and what it was made from.
    member val TouchView: View = Unchecked.defaultof<View> with get, set
    member val TouchKey: obj = null with get, set
    /// Made once: what gives the layer its view, what the handles call, and what the row calls.
    member val TouchContent: unit -> obj = Unchecked.defaultof<unit -> obj> with get, set
    member val HandleDrags: (int -> Point -> unit)[] = [||] with get, set
    member val RowTooWide: unit -> unit = ignore with get, set
    member val RowClosed: unit -> unit = ignore with get, set

    /// What is drawn and measured: the text with what is being composed in place of the selection, the caret
    /// where the input method has it.
    member s.Shown : EditState =
        if s.Composition.Length = 0 then s.Edit
        else
            let edit = s.Edit
            let caret = edit.Start + max 0 (min s.Composition.Length s.CompositionCaret)
            { Text = edit.Text.Substring(0, edit.Start) + s.Composition + edit.Text.Substring edit.End
              Anchor = caret
              Caret = caret }

/// What a TextBox's layer of selection handles was made from: where each handle's point is in the window (minus
/// infinity: it doesn't show), the row's place, and counts of what else changes it.
type internal TouchShown =
    { StartX: float
      StartY: float
      EndX: float
      EndY: float
      CaretX: float
      CaretY: float
      Row: bool
      Anchor: Rect
      Middle: bool
      Vertical: bool
      Version: int
      Menu: obj }

/// The words of a TextBox's own menu (DESIGN.md, "9f", decision 76): an app in another language replaces them,
/// `App.value TextBoxMenu.labels { TextBoxMenuLabels.English with Cut = "Couper"; ... }`.
type TextBoxMenuLabels =
    { Undo: string
      Redo: string
      Cut: string
      Copy: string
      Paste: string
      SelectAll: string }

    static member English =
        { Undo = "Undo"
          Redo = "Redo"
          Cut = "Cut"
          Copy = "Copy"
          Paste = "Paste"
          SelectAll = "Select all" }

module internal TextBoxMenuValues =
    /// Made on first use (an app value's definition is shared state: `Theme.current` does the same).
    let labels = lazy (AppValue.define "TextBoxMenu.labels" TextBoxMenuLabels.English)

/// A TextBox's own context menu: Undo, Redo, Cut, Copy, Paste, Select all, each enabled when it applies. A
/// `contextMenu` set on a box replaces it.
[<AbstractClass; Sealed>]
type TextBoxMenu =
    /// The app value holding the menu's words, for every TextBox in the app.
    static member labels: AppValue<TextBoxMenuLabels> = TextBoxMenuValues.labels.Value

module TextBoxProps =
    /// The text. Given by the view: controlled; otherwise the control's own value.
    let text = Prop.define "Text" "" Affects.Measure |> AppState.mark
    let onTextChanged: Prop<(string -> unit) voption> = Prop.define "OnTextChanged" ValueNone Affects.Nothing
    let onSubmitted: Prop<(string -> unit) voption> = Prop.define "OnSubmitted" ValueNone Affects.Nothing
    /// Shown, dim, while the text is empty.
    let placeholder = Prop.define "Placeholder" "" Affects.Render
    let placeholderForeground = Prop.define "PlaceholderForeground" Color.Transparent Affects.Render
    /// The text can be selected and copied but not changed.
    let isReadOnly = Prop.define "IsReadOnly" false Affects.Render
    /// The most grapheme clusters the user can enter; 0: no limit.
    let maxLength = Prop.define "MaxLength" 0 Affects.Nothing
    /// Bullets are shown in place of the characters.
    let isPassword = Prop.define "IsPassword" false Affects.Measure
    /// Behind the selected text.
    let selection = Prop.define "Selection" Color.Transparent Affects.Render
    /// Several lines: the text wraps, and Enter makes a new line.
    let multiline = Prop.define "Multiline" false Affects.Measure
    /// The fewest and the most lines a multi-line box asks to be high (0: no most).
    let minLines = Prop.define "MinLines" 3 Affects.Measure
    let maxLines = Prop.define "MaxLines" 0 Affects.Measure
    /// Tab types a tab character (and pasted tabs are kept).
    let acceptsTab = Prop.define "AcceptsTab" false Affects.Nothing
    /// Which on-screen keyboard suits the text (a phone).
    let keyboard = Prop.define "Keyboard" KeyboardKind.Text Affects.Nothing
    /// Several lines: Enter submits and Shift+Enter makes the new line.
    let submitOnEnter = Prop.define "SubmitOnEnter" false Affects.Nothing
    let internal state: Prop<TextBoxState> = Prop.local "TextBoxState" null

/// Text to type in, one line or several (DESIGN.md, "M9: TextBox"):
///
///     TextBox.create [
///         TextBox.text model.Name                                   // left out: it keeps its own text
///         TextBox.onTextChanged (fun text -> dispatch (SetName text))
///         TextBox.placeholder "Your name"
///         TextBox.onSubmitted (fun text -> dispatch Save)           // Enter
///     ]
///
/// With `text` it is controlled: it shows exactly that, tells `onTextChanged` what the user typed, and shows
/// what the view answers (the same text, or another: a filter; the caret is kept inside it). Without, it keeps
/// its own text. The caret and the selection are its own either way.
///
/// Keys: ←/→ by character and with Ctrl by word, Home/End (and ↑/↓), Shift with any of them to select, Ctrl+A,
/// Backspace/Delete (a word with Ctrl, or the selection), Enter for `onSubmitted`. Ctrl+C, Ctrl+X and Ctrl+V copy,
/// cut and paste (also Ctrl+Insert, Shift+Delete, Shift+Insert); Ctrl+Z undoes, Ctrl+Y or Ctrl+Shift+Z redoes (a
/// run of typing is one step; up to 100 steps). A password is never copied. Escape and Tab are left to whatever is
/// around it (`acceptsTab true`: Tab types a tab). Mouse: a click places the caret, a drag selects (held past the
/// edge it keeps scrolling), Shift+click extends, a double-click selects a word and a triple-click everything.
/// Tabbing into it selects all its text. Text wider than the box scrolls sideways with the caret. The caret moves
/// by what a reader sees as one character (an emoji is never split); it blinks, and stops after ten seconds
/// without input.
///
/// A finger: a tap places the caret, with a selection handle under it for a few seconds; a long press or a
/// double-tap selects the word under it (keep the finger down and drag for more words), with a handle at each end
/// of the selection to drag, and the context menu as a row beside it (Cut, Copy, Paste, Select all: those that
/// apply). A key, typed text or the mouse puts them away (DESIGN.md, "9g: selection by touch").
///
/// `TextBox.multiline true`: the text wraps at the box's width and Enter makes a new line (the text uses `\n`);
/// Ctrl+Enter is `onSubmitted`. ↑/↓ and PageUp/PageDown move by lines, Home/End to the ends of the line as drawn,
/// Ctrl+Home/End to the ends of the text; a triple-click selects the paragraph. The box asks to be as high as its
/// text, between `minLines` (3) and `maxLines` (no limit) lines; text that doesn't fit scrolls, by the wheel, the
/// keys or its scrollbar. Tabbing into it keeps its selection.
[<AbstractClass>]
type TextBox =
    inherit Border
    /// The text. Given: controlled (it shows exactly this); left out: its own text, starting empty.
    static member text<'k when 'k :> TextBox>(value: string) : Setting<'k> = Setting.create TextBoxProps.text (if isNull value then "" else value)
    /// Called with the whole new text on every change the user makes.
    static member onTextChanged<'k when 'k :> TextBox>(handler: string -> unit) : Setting<'k> = Setting.create TextBoxProps.onTextChanged (ValueSome handler)
    /// Called with the text when the user presses Enter (Ctrl+Enter in a multi-line box). Without it, the key goes
    /// on to whatever is around the box.
    static member onSubmitted<'k when 'k :> TextBox>(handler: string -> unit) : Setting<'k> = Setting.create TextBoxProps.onSubmitted (ValueSome handler)
    /// Shown, dim, while the text is empty; also its name for screen readers unless `Semantics.label` gives one.
    static member placeholder<'k when 'k :> TextBox>(text: string) : Setting<'k> = Setting.create TextBoxProps.placeholder (if isNull text then "" else text)
    /// The placeholder's colour (the theme's dim text).
    static member placeholderForeground<'k when 'k :> TextBox>(value: Color) : Setting<'k> = Setting.create TextBoxProps.placeholderForeground value
    static member placeholderForeground<'k when 'k :> TextBox>(themeValue: ThemeValue<Color>) : Setting<'k> =
        Setting.fromTheme TextBoxProps.placeholderForeground themeValue
    /// True: the text can be selected and copied but not changed (it still takes focus). Default: false.
    static member isReadOnly<'k when 'k :> TextBox>(value: bool) : Setting<'k> = Setting.create TextBoxProps.isReadOnly value
    /// The most characters the user can enter, counted as a reader sees them (an emoji is one, a line break is
    /// one); what doesn't fit is left out. 0 (default): no limit. Text the view gives is never cut.
    static member maxLength<'k when 'k :> TextBox>(value: int) : Setting<'k> = Setting.create TextBoxProps.maxLength (max 0 value)
    /// True: a bullet shows for each character, the text counts as one word, and it can't be copied or cut.
    /// A password is always one line. Default: false.
    static member isPassword<'k when 'k :> TextBox>(value: bool) : Setting<'k> = Setting.create TextBoxProps.isPassword value
    /// The colour behind selected text (the theme's accent, tinted).
    static member selection<'k when 'k :> TextBox>(value: Color) : Setting<'k> = Setting.create TextBoxProps.selection value
    static member selection<'k when 'k :> TextBox>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme TextBoxProps.selection themeValue
    /// True: several lines. The text wraps at the box's width, Enter makes a new line and Ctrl+Enter is
    /// `onSubmitted`. Default: false.
    static member multiline<'k when 'k :> TextBox>(value: bool) : Setting<'k> = Setting.create TextBoxProps.multiline value
    /// The fewest lines a multi-line box asks to be high. Default: 3.
    static member minLines<'k when 'k :> TextBox>(value: int) : Setting<'k> = Setting.create TextBoxProps.minLines (max 1 value)
    /// The most lines a multi-line box asks to be high: with more text it scrolls. 0 (default): no limit. A height
    /// from the layout (`TextBox.height`) wins over both.
    static member maxLines<'k when 'k :> TextBox>(value: int) : Setting<'k> = Setting.create TextBoxProps.maxLines (max 0 value)
    /// True: Tab types a tab character (drawn four spaces wide) and pasted tabs are kept; Shift+Tab still moves
    /// the focus back. False (default): Tab moves the focus and a pasted tab becomes a space.
    static member acceptsTab<'k when 'k :> TextBox>(value: bool) : Setting<'k> = Setting.create TextBoxProps.acceptsTab value
    /// Which on-screen keyboard a phone shows for it: `KeyboardKind.Text` (default), `Number`, `Decimal`,
    /// `Email`, `Url`, `Phone` or `Search`. Nothing is checked: a Number box can still be given letters (pasted,
    /// or from a desktop's keyboard). A password box gets the password keyboard by itself.
    static member keyboard<'k when 'k :> TextBox>(value: KeyboardKind) : Setting<'k> = Setting.create TextBoxProps.keyboard value
    /// True, in a multi-line box: Enter calls `onSubmitted` and Shift+Enter makes the new line, as a message box
    /// does (Ctrl+Enter still submits). False (default): Enter makes the new line and Ctrl+Enter submits. The box
    /// keeps its text after a submit: give it `text` and clear that in the handler. On a phone the on-screen
    /// Enter submits too, so a box that should take new lines there needs a Send button and this left off.
    static member submitOnEnter<'k when 'k :> TextBox>(value: bool) : Setting<'k> = Setting.create TextBoxProps.submitOnEnter value

module TextBox =
    /// The width it asks for when nothing stretches it (without its padding), in DIPs.
    let private preferredWidth = 160.0
    let private caretWidth = 1.0
    /// The caret is on for this long, then off for as long.
    let internal blinkHalf = TimeSpan.FromMilliseconds 500.0
    /// Half-blinks before the caret stays on: ten seconds (DESIGN.md, "M9", decision 7).
    let internal blinkTicks = 20
    /// Presses this close together, in time and in DIPs, count as one double or triple click.
    let private multiClickTime = TimeSpan.FromMilliseconds 500.0
    let private multiClickDistance = 4.0
    /// What a password shows for each character.
    let private bullet = '•'
    /// While the button is held outside the text during a drag, the selection moves on this often.
    let internal dragStep = TimeSpan.FromMilliseconds 50.0
    /// The strip at the right edge where the scrollbar takes the pointer, and how wide the bar is under it.
    let private barStrip = 12.0
    let private wideBar = 10.0
    /// The caret's handle goes this long after it was last used (DESIGN.md, "9g", decision 88).
    let internal caretHandleTime = TimeSpan.FromSeconds 4.0
    /// The row comes back this long after the text stopped scrolling (decision 93).
    let internal rowReturn = TimeSpan.FromMilliseconds 250.0
    /// Two taps this close (DIPs; and within `multiClickTime`) are a double-tap: a finger is less exact than a mouse.
    let private tapDistance = 24.0

    let internal stateOf (e: Element) =
        match e.Get TextBoxProps.state with
        | null ->
            let s = TextBoxState()
            let text = e.Get TextBoxProps.text
            s.Edit <- { Text = text; Anchor = 0; Caret = 0 }
            s.Seen <- text
            e.SetLocal(TextBoxProps.state, s)
            s
        | s -> s

    /// The text with its selection (for tests and, later, a controlled selection).
    let internal editOf (e: Element) = (stateOf e).Edit

    /// Several lines. A password is always one (DESIGN.md, "9c", decision 45).
    let private isMulti (e: Element) =
        if e.Get TextBoxProps.multiline then
            if e.Get TextBoxProps.isPassword then
                DebugWarnings.Once(
                    "TextBox.passwordMultiline",
                    "A TextBox has both isPassword and multiline: a password is one line, so multiline is ignored."
                )
                false
            else true
        else false

    /// The view gave other text than the box shows. As its answer to an edit (a filter, or a view that didn't
    /// follow), the edit's undo step ends in what the view says; for any other reason (a Clear button, loaded
    /// data, a changed undo) nothing from before can be undone (DESIGN.md, "9b", decision 31).
    let private viewChanged (s: TextBoxState) (text: string) =
        s.Edit <- TextEdit.withText text s.Edit
        s.History <- if s.Pending = 1 then TextHistory.amend s.Edit s.History else TextHistory.empty
        s.AtLineEnd <- false
        s.WantedX <- nan

    /// Before input is handled: text the view has given since the last look replaces what is here (the caret
    /// stays, kept inside it). What the user typed since stays until the view has had its turn (see `settle`).
    let private adopt (e: Element) (s: TextBoxState) =
        let text = e.Get TextBoxProps.text
        if not (String.Equals(text, s.Seen, StringComparison.Ordinal)) then
            s.Seen <- text
            if not (String.Equals(text, s.Edit.Text, StringComparison.Ordinal)) then viewChanged s text

    /// At a frame (views have been applied by then): it shows the `text` setting's value, whatever was typed. A
    /// controlled TextBox whose view didn't follow goes back to what the view says.
    let private settle (e: Element) (s: TextBoxState) =
        let text = e.Get TextBoxProps.text
        s.Seen <- text
        if not (String.Equals(text, s.Edit.Text, StringComparison.Ordinal)) then viewChanged s text
        s.Pending <- 0

    /// What is drawn and where each caret place is, measured once per text, font and size (one line).
    let private measureStops (e: Element) (s: TextBoxState) =
        let text = s.Shown.Text
        let font, size, password = e.Get TextProps.font, e.Get TextProps.fontSize, e.Get TextBoxProps.isPassword
        if not (String.Equals(text, s.StopsText, StringComparison.Ordinal))
           || not (obj.ReferenceEquals(font, s.StopsFont))
           || size <> s.StopsSize
           || password <> s.StopsPassword then
            s.StopsText <- text
            s.StopsFont <- font
            s.StopsSize <- size
            s.StopsPassword <- password
            if password then
                // One bullet for each grapheme cluster; an index inside a cluster has its start's place.
                let count = TextEdit.graphemes text
                let display = String(bullet, count)
                let bullets = e.TextSystem.CaretStops(display, font, size)
                let stops = Array.zeroCreate<float> (text.Length + 1)
                let mutable i = 0
                let mutable n = 0
                while i < text.Length do
                    let next = TextEdit.nextStop text i
                    for k in i .. next - 1 do
                        stops[k] <- bullets[n]
                    i <- next
                    n <- n + 1
                stops[text.Length] <- bullets[count]
                s.Display <- display
                s.Stops <- stops
            else
                // One line: a line break in text the app gave is drawn as a space (what is typed or pasted has
                // none: `TextEdit.clean`). The text itself is left alone, and the caret steps over the break as
                // one character.
                let display = if text.AsSpan().IndexOfAny('\n', '\r') >= 0 then text.Replace('\n', ' ').Replace('\r', ' ') else text
                s.Display <- display
                s.Stops <- e.TextSystem.CaretStops(display, font, size)

    /// The text wrapped at `width`, laid out again only when the text, the font, its size or the width changed
    /// (and then only the paragraphs that did).
    let private linesFor (e: Element) (s: TextBoxState) (width: float) =
        let width = max 1.0 width
        let font, size = e.Get TextProps.font, e.Get TextProps.fontSize
        let text = s.Shown.Text
        let current = s.Lines
        if isNull current
           || not (obj.ReferenceEquals(s.LinesText, text) || String.Equals(s.LinesText, text, StringComparison.Ordinal))
           || not (obj.ReferenceEquals(current.Font, font))
           || current.Size <> size
           || current.Width <> width then
            let struct (lines, laidOut) = MultiLine.build e.TextSystem current text font size width
            s.Lines <- lines
            s.LaidOut <- s.LaidOut + laidOut
        s.LinesText <- text
        s.Lines

    /// The lines at the width the box was last drawn with (before its first frame: the width it asks for).
    let private currentLines (e: Element) (s: TextBoxState) = linesFor e s (if s.TextWidth > 0.0 then s.TextWidth else preferredWidth)

    /// The left edge of the text area and its width, inside the border and the padding.
    let private textArea (e: Element) (size: Size) : struct (float * float) =
        let p, t = e.Get BorderProps.padding, e.Get BorderProps.borderThickness
        struct (p.Left + t, max 0.0 (size.Width - p.Horizontal - 2.0 * t))

    /// The top edge of the text area and its height.
    let private textRows (e: Element) (size: Size) : struct (float * float) =
        let p, t = e.Get BorderProps.padding, e.Get BorderProps.borderThickness
        struct (p.Top + t, max 0.0 (size.Height - p.Vertical - 2.0 * t))

    let private lineHeightOf (e: Element) = e.TextSystem.LineHeight(e.Get TextProps.font, e.Get TextProps.fontSize)

    /// One line, focused: scrolled so the caret is in view, and no further than there is text for. Whole pixels.
    /// (The caret places are measured: `measureStops`.)
    let private scrollLine (s: TextBoxState) (width: float) (scale: float) (focused: bool) =
        if focused then
            let snap (v: float) = Math.Round(v * scale) / scale
            let caretDips = max caretWidth (1.0 / scale)
            let stops = s.Stops
            let caret = stops[s.Shown.Caret]
            let mutable scroll = s.Scroll
            if caret - scroll > width - caretDips then scroll <- caret - width + caretDips
            if caret - scroll < 0.0 then scroll <- caret
            s.Scroll <- snap (max 0.0 (min scroll (stops[stops.Length - 1] + caretDips - width)))

    /// Several lines: how much there is to scroll, and scrolled so the caret's line is in view if it moved.
    let private scrollLines (s: TextBoxState) (count: int) (lineHeight: float) (viewHeight: float) (scale: float) (caretLine: int) =
        let snap (v: float) = Math.Round(v * scale) / scale
        s.Extent <- float count * lineHeight
        s.ViewHeight <- viewHeight
        // Less than a DIP over (layout rounding) is not overflow.
        s.MaxScrollY <- (if s.Extent - viewHeight < 1.0 then 0.0 else s.Extent - viewHeight)
        if s.Reveal then
            s.Reveal <- false
            // The caret's line comes into view by the least scroll.
            let caretTop = float caretLine * lineHeight
            if caretTop < s.ScrollY then s.ScrollY <- caretTop
            if caretTop + lineHeight > s.ScrollY + viewHeight then s.ScrollY <- caretTop + lineHeight - viewHeight
        s.ScrollY <- snap (Math.Clamp(s.ScrollY, 0.0, s.MaxScrollY))

    /// The caret place nearest to `x` (the element's own coordinates), in one line.
    let private indexAt (e: Element) (s: TextBoxState) (size: Size) (x: float) =
        measureStops e s
        let struct (left, _) = textArea e size
        let x = x - left + s.Scroll
        let text = s.Edit.Text
        let stops = s.Stops
        let mutable best = 0
        let mutable i = 0
        let mutable go = true
        while go do
            if abs (stops[i] - x) <= abs (stops[best] - x) then best <- i
            if i >= text.Length || stops[i] > x then go <- false else i <- TextEdit.nextStop text i
        best

    /// The caret place nearest to a point (the element's own coordinates), and whether it is the end of a line
    /// that wraps on. Below the last line: the end of the text.
    let private pointAt (e: Element) (s: TextBoxState) (size: Size) (x: float) (y: float) : struct (int * bool) =
        if isMulti e then
            let lines = currentLines e s
            let struct (left, _) = textArea e size
            let struct (top, _) = textRows e size
            let n = int (Math.Floor((y - top + s.ScrollY) / lineHeightOf e))
            if n >= lines.LineCount then struct (s.Edit.Text.Length, false)
            else MultiLine.indexAt e.TextSystem lines (max 0 n) (x - left)
        else struct (indexAt e s size x, false)

    let rec private blinkLater (e: Element) (s: TextBoxState) (generation: int) =
        match e.Root with
        | ValueSome root ->
            match root.Dispatcher with
            | ValueSome dispatcher ->
                dispatcher.PostAfter(
                    blinkHalf,
                    fun () ->
                        if s.BlinkGeneration = generation && e.IsFocused && e.Root.IsSome then
                            s.BlinkTicks <- s.BlinkTicks - 1
                            // The last one leaves it on: after that no more frames (the power rule).
                            s.CaretOn <- s.BlinkTicks <= 0 || not s.CaretOn
                            e.InvalidateVisual()
                            if s.BlinkTicks > 0 then blinkLater e s generation
                )
            // A bare tree (no app): the caret stays on.
            | ValueNone -> ()
        | ValueNone -> ()

    /// Input came (or focus): the caret shows now and blinks for another ten seconds.
    let private restartBlink (e: Element) (s: TextBoxState) =
        s.BlinkGeneration <- s.BlinkGeneration + 1
        s.CaretOn <- true
        s.BlinkTicks <- blinkTicks
        if e.IsFocused then blinkLater e s s.BlinkGeneration

    /// Every change to the text or the selection comes through here: the new state shows (scrolled into view),
    /// the caret starts blinking again, new text is recorded for undo as a step of `kind` (none: an undo or a
    /// redo, which moved the history itself) and `onTextChanged` hears of it. A change of the caret or the
    /// selection alone ends the undo step being typed.
    let private applyWith (e: Element) (s: TextBoxState) (kind: EditKind voption) (next: EditState) =
        let before = s.Edit
        s.Edit <- next
        s.TouchVersion <- s.TouchVersion + 1
        s.Reveal <- true
        s.AtLineEnd <- false
        s.WantedX <- nan
        restartBlink e s
        e.InvalidateVisual()
        if not (String.Equals(next.Text, before.Text, StringComparison.Ordinal)) then
            match kind with
            | ValueSome kind ->
                // Replacing or removing a selection is always a step of its own.
                let kind = if before.HasSelection then EditKind.Single else kind
                s.History <- TextHistory.record kind e.Time before next s.History
            | ValueNone -> ()
            // Its own text: the setting now holds it. Controlled: the view answers before the next frame.
            if ControlValue.isControlled e TextBoxProps.text then s.Pending <- (if kind.IsSome then 1 else 2)
            else s.Seen <- next.Text
            ControlValue.change e TextBoxProps.text next.Text (e.Get TextBoxProps.onTextChanged)
        else s.History <- TextHistory.close s.History

    /// A move of the caret or the selection.
    let private apply (e: Element) (s: TextBoxState) (next: EditState) = applyWith e s ValueNone next

    /// An edit: `kind` says which edits it joins into one undo step.
    let private applyEdit (e: Element) (s: TextBoxState) (kind: EditKind) (next: EditState) = applyWith e s (ValueSome kind) next

    /// Types or pastes `text` as this box takes it (line breaks only in a multi-line box, tabs only with
    /// `acceptsTab`, no more than `maxLength`).
    let private inserted (e: Element) (text: string) (edit: EditState) =
        TextEdit.insertInto (isMulti e) (e.Get TextBoxProps.acceptsTab) text (e.Get TextBoxProps.maxLength) edit

    /// Which on-screen keyboard suits the box: a password's own, else what `TextBox.keyboard` says.
    let private keyboardOf (e: Element) = if e.Get TextBoxProps.isPassword then KeyboardKind.Password else e.Get TextBoxProps.keyboard

    /// What is being composed stops showing (it was committed as typed text, cancelled, or is taken below).
    let private endComposition (e: Element) (s: TextBoxState) =
        if s.Composition.Length > 0 then
            s.Composition <- ""
            s.CompositionCaret <- 0
            s.Reveal <- true
            e.InvalidateVisual()
            if isMulti e then e.InvalidateMeasure()

    /// A click elsewhere or the focus leaving: what is being composed is typed, and the input method is told to
    /// drop it (DESIGN.md, "9d", decision 56).
    let private commitComposition (e: Element) (s: TextBoxState) =
        if s.Composition.Length > 0 then
            let text = s.Composition
            endComposition e s
            s.Composed <- false
            match e.Root with
            | ValueSome root -> root.ClearComposition()
            | ValueNone -> ()
            if not (e.Get TextBoxProps.isReadOnly) then
                let next = inserted e text s.Edit
                if next <> s.Edit then applyEdit e s EditKind.Single next

    /// Pastes `text` over the selection (a step of its own), if the box can still take it. `whileFocused`: only if
    /// it still has the focus (an answer that came late, after the user moved on, is dropped).
    let private paste (e: Element) (whileFocused: bool) (text: string) =
        if e.Root.IsSome && (e.IsFocused || not whileFocused) && not (e.Get TextBoxProps.isReadOnly) then
            let s = stateOf e
            adopt e s
            let next = inserted e text s.Edit
            if next <> s.Edit then applyEdit e s EditKind.Single next

    // ---- What the keys and the box's own menu both do (DESIGN.md, "9b", decision 26) ----

    /// There is something to copy: a selection, in a box that isn't a password's (which never leaves the box).
    let private canCopy (e: Element) = (stateOf e).Edit.HasSelection && not (e.Get TextBoxProps.isPassword)
    let private canCut (e: Element) = canCopy e && not (e.Get TextBoxProps.isReadOnly)
    let private canChange (e: Element) = not (e.Get TextBoxProps.isReadOnly)

    let private copy (e: Element) =
        if canCopy e then Clipboard().SetText (stateOf e).Edit.SelectedText

    let private cut (e: Element) =
        if canCut e then
            let s = stateOf e
            Clipboard().SetText s.Edit.SelectedText
            applyEdit e s EditKind.Single (TextEdit.deleteSelection s.Edit)

    /// Pastes the clipboard's text. `fromMenu`: asked by the box's menu, while the menu has the focus, so an
    /// answer that is there at once is taken though the box isn't focused.
    let private pasteClipboard (e: Element) (fromMenu: bool) =
        if canChange e then
            // The desktop answers at once; the browser after its paste event (or after asking, from a menu).
            let reading = Clipboard().GetText()
            if reading.IsCompletedSuccessfully then paste e (not fromMenu) reading.Result
            else
                reading.ContinueWith(
                    (fun (t: Task<string>) ->
                        try
                            if t.IsCompletedSuccessfully then paste e true t.Result
                        with error ->
                            match e.Root with
                            | ValueSome root -> root.ReportError error
                            | ValueNone -> ()),
                    TaskContinuationOptions.ExecuteSynchronously
                )
                |> ignore

    /// Undo or redo (`take`): true if there was a step to take.
    let private stepHistory (e: Element) (take: EditHistory -> struct (EditState * EditHistory) voption) =
        let s = stateOf e
        if canChange e then
            match take s.History with
            | ValueSome(struct (state, history)) ->
                s.History <- history
                applyWith e s ValueNone (TextEdit.normalize state)
                true
            | ValueNone -> false
        else false

    let private selectEverything (e: Element) =
        let s = stateOf e
        let next = TextEdit.selectAll s.Edit
        if next <> s.Edit then apply e s next

    /// The box's own menu (DESIGN.md, "9f", decision 75), made when it is asked for: each item is enabled when it
    /// applies now. Its words are an app value (`TextBoxMenu.labels`).
    let private contextMenu (ctx: InputContext) : View voption =
        let e = ctx.Element
        let s = stateOf e
        adopt e s
        // (The menu is in a layer of its own: the box keeps its focus and its selection stays on show.)
        let labels =
            match e.Root with
            | ValueSome root -> root.AppValues.Get TextBoxMenu.labels
            | ValueNone -> TextBoxMenuLabels.English
        let item (text: string) (shortcut: string) (enabled: bool) (run: unit -> unit) =
            MenuItem.create [
                MenuItem.text text
                MenuItem.shortcut shortcut
                MenuItem.isEnabled enabled
                MenuItem.onClick (fun _ -> if e.Root.IsSome then run ())
            ]
        let canChange = canChange e
        ValueSome(
            Menu.create [
                Menu.children [
                    item labels.Undo "Ctrl+Z" (canChange && not s.History.Undo.IsEmpty) (fun () -> stepHistory e TextHistory.undo |> ignore)
                    item labels.Redo "Ctrl+Y" (canChange && not s.History.Redo.IsEmpty) (fun () -> stepHistory e TextHistory.redo |> ignore)
                    Menu.separator
                    item labels.Cut "Ctrl+X" (canCut e) (fun () -> cut e)
                    item labels.Copy "Ctrl+C" (canCopy e) (fun () -> copy e)
                    item labels.Paste "Ctrl+V" canChange (fun () -> pasteClipboard e true)
                    Menu.separator
                    item labels.SelectAll "Ctrl+A" (s.Edit.Text.Length > 0) (fun () -> selectEverything e)
                ]
            ]
        )

    /// A drag reached the point (x, y): the selection runs to the caret place there (by whole words after a
    /// double-click).
    let private dragTo (e: Element) (s: TextBoxState) (size: Size) (x: float) (y: float) =
        adopt e s
        let struct (index, atEnd) = pointAt e s size x y
        let next =
            match s.DragWord with
            // A finger on the caret's handle moves the caret; on a selection handle, that end (the ends may cross).
            | _ when s.HandleDrag = 3 -> TextEdit.moveTo index false s.Edit
            | _ when s.HandleDrag <> 0 -> if index = s.HandleFixed then s.Edit else TextEdit.select s.HandleFixed index s.Edit
            | ValueSome(struct (first, last)) ->
                // From a double-click: whole words, the first one always in.
                let struct (wordFirst, wordLast) = TextEdit.wordAt s.Edit.Text index
                if index < first then TextEdit.select last wordFirst s.Edit
                else TextEdit.select first (max last (if index > last then wordLast else last)) s.Edit
            | ValueNone -> TextEdit.moveTo index true s.Edit
        if next <> s.Edit then
            apply e s next
            s.AtLineEnd <- atEnd && s.DragWord.IsNone

    /// The pointer is past the text's edge in the direction it scrolls: above or below several lines, left or
    /// right of one.
    let private isOutside (e: Element) (size: Size) (x: float) (y: float) =
        if isMulti e then
            let struct (top, height) = textRows e size
            y < top || y > top + height
        else
            let struct (left, width) = textArea e size
            x < left || x > left + width

    /// While the button is held outside the text, the selection moves on a step at a time, which scrolls the
    /// text (DESIGN.md, "9c", decision 37). It ends when the pointer comes back or the button goes up; while
    /// nothing more can be selected, nothing is drawn.
    let rec private dragLater (e: Element) (s: TextBoxState) (generation: int) =
        match e.Root |> ValueOption.bind (fun root -> root.Dispatcher) with
        | ValueSome dispatcher ->
            dispatcher.PostAfter(
                dragStep,
                fun () ->
                    if s.DragGeneration = generation then
                        let b = e.Bounds
                        let size = Size.Create(b.Width, b.Height)
                        if s.Dragging && e.Root.IsSome && isOutside e size s.LastX s.LastY then
                            // One line: a step past the edge, however close to it the pointer is.
                            let x =
                                if isMulti e then s.LastX
                                else
                                    let struct (left, width) = textArea e size
                                    if s.LastX < left then s.LastX - 16.0 else s.LastX + 16.0
                            dragTo e s size x s.LastY
                            dragLater e s generation
                        else s.DragTimer <- false
            )
        | ValueNone -> s.DragTimer <- false

    let private stopDrag (s: TextBoxState) =
        s.Dragging <- false
        s.DragTimer <- false
        s.DragGeneration <- s.DragGeneration + 1

    /// The text scrolled, or the box moved in the window: the row for a finger is out of sight until that has
    /// stopped for a moment (DESIGN.md, "9g", decision 93).
    let private rowScrolled (e: Element) (s: TextBoxState) =
        if s.Touch && s.Row then
            s.RowHeld <- true
            s.RowGeneration <- s.RowGeneration + 1
            let generation = s.RowGeneration
            match e.Root |> ValueOption.bind (fun root -> root.Dispatcher) with
            | ValueSome dispatcher ->
                dispatcher.PostAfter(
                    rowReturn,
                    fun () ->
                        if s.RowGeneration = generation && s.RowHeld then
                            s.RowHeld <- false
                            e.InvalidateVisual()
                )
            | ValueNone -> s.RowHeld <- false

    /// Scrolls several lines to `y` (kept within what there is); true when it moved.
    let private scrollTo (e: Element) (s: TextBoxState) (y: float) =
        let y = Math.Clamp(y, 0.0, s.MaxScrollY)
        if y <> s.ScrollY then
            s.ScrollY <- y
            rowScrolled e s
            e.InvalidateVisual()
            true
        else false

    /// The scrollbar's thumb: (its length, its start along the track), when the text overflows.
    let private thumbOf (s: TextBoxState) (size: Size) =
        if s.MaxScrollY <= 0.0 then ValueNone
        else ScrollViewer.thumbSpan (size.Height - 2.0 * ScrollViewer.inset) s.Extent s.ViewHeight s.ScrollY

    let private overBar (e: Element) (s: TextBoxState) (size: Size) (x: float) = isMulti e && s.MaxScrollY > 0.0 && x >= size.Width - barStrip

    // ---- Selecting by touch (DESIGN.md, "9g: selection by touch") ----
    //
    // A finger's long press or double-tap selects a word; selection handles then stand at the selection's ends (one
    // at the caret after a tap), and the box's context menu shows as a row beside it. Handles and row are in a
    // layer of the box's own above the window's content (`IElementRoot.ElementLayer`), which never takes the
    // keyboard: the box keeps its focus, and a phone its keyboard.

    /// The box of the caret at `index`, in the element's own coordinates, as the text is scrolled now: on whole
    /// pixels, where `render` draws it.
    let private caretBox (e: Element) (s: TextBoxState) (size: Size) (index: int) (atLineEnd: bool) : Rect =
        let scale = e.Scale
        let snap (v: float) = Math.Round(v * scale) / scale
        let lineHeight = lineHeightOf e
        let struct (left, width) = textArea e size
        let struct (top, height) = textRows e size
        if isMulti e then
            let lines = currentLines e s
            let struct (p, l, n) = MultiLine.locate lines index atLineEnd
            let x = max 0.0 (min width (MultiLine.caretX e.TextSystem lines p l index))
            let y = snap (top + float n * lineHeight - s.ScrollY)
            Rect.Create(snap (left + x), y, 0.0, snap (top + float (n + 1) * lineHeight - s.ScrollY) - y)
        else
            measureStops e s
            let stops = s.Stops
            Rect.Create(snap (left + stops[max 0 (min (stops.Length - 1) index)] - s.Scroll), snap (top + (height - lineHeight) / 2.0), 0.0, snap lineHeight)

    /// Where the text shows in the box (what its drawing is clipped to), in the element's own coordinates.
    let private textClip (e: Element) (size: Size) : Rect =
        let t = e.Get BorderProps.borderThickness
        let struct (left, width) = textArea e size
        Rect.Create(left, t, width, max 0.0 (size.Height - 2.0 * t))

    /// Before the layer above is laid out: the text is scrolled as the coming frame will draw it (what `render`
    /// does, a moment earlier).
    let private settleScroll (e: Element) (s: TextBoxState) (size: Size) =
        settle e s
        let scale = e.Scale
        let struct (_, width) = textArea e size
        if isMulti e then
            let struct (_, viewHeight) = textRows e size
            s.TextWidth <- width
            let lines = linesFor e s width
            let struct (_, _, caretLine) = MultiLine.locate lines s.Shown.Caret s.AtLineEnd
            scrollLines s lines.LineCount (lineHeightOf e) viewHeight scale caretLine
        else
            measureStops e s
            scrollLine s width scale e.IsFocused

    /// The character under a finger at (x, y), nearest to which the caret would go at `index`: whether the finger
    /// is on a word (not on blank space, nor past the end of its line), and the index to take the word from. A
    /// finger on the right half of a word's last letter is nearest to the place after the word.
    let private wordUnder (e: Element) (s: TextBoxState) (size: Size) (x: float) (y: float) (index: int) (atEnd: bool) : struct (bool * int) =
        let text = s.Edit.Text
        if text.Length = 0 then struct (false, index)
        else
            let box = caretBox e s size index atEnd
            let onLine = abs (x - box.X) <= 16.0 && y >= box.Y - 4.0 && y <= box.Bottom + 4.0
            let under =
                if x < box.X && index > 0 then
                    let before = TextEdit.previousStop text index
                    // On the same line only (not the line break, nor the line above at a wrap).
                    if text[before] <> '\n' && (caretBox e s size before false).Y = box.Y then before else index
                else index
            let struct (first, last) = TextEdit.wordAt text under
            struct (onLine && last > first && not (Char.IsWhiteSpace text[first]), under)

    /// The caret's handle shows, for a few seconds from now.
    let private caretHandleOn (e: Element) (s: TextBoxState) =
        s.CaretHandle <- true
        s.CaretHandleGeneration <- s.CaretHandleGeneration + 1
        let generation = s.CaretHandleGeneration
        match e.Root |> ValueOption.bind (fun root -> root.Dispatcher) with
        | ValueSome dispatcher ->
            dispatcher.PostAfter(
                caretHandleTime,
                fun () ->
                    if s.CaretHandleGeneration = generation && s.CaretHandle then
                        s.CaretHandle <- false
                        e.InvalidateVisual()
            )
        | ValueNone -> ()

    /// A key, typed text or the mouse: the handles and the row go.
    let private hideTouch (e: Element) (s: TextBoxState) =
        if s.Touch || s.LongPressed || s.HandleDrag <> 0 then
            if s.HandleDrag <> 0 || s.LongPressed then stopDrag s
            s.Touch <- false
            s.Row <- false
            s.RowHeld <- false
            s.CaretHandle <- false
            s.HandleDrag <- 0
            s.LongPressed <- false
            s.DragWord <- ValueNone
            s.CaretHandleGeneration <- s.CaretHandleGeneration + 1
            s.RowGeneration <- s.RowGeneration + 1
            e.InvalidateVisual()

    /// The box's own commands for a finger (decision 85): Cut, Copy, Paste and Select all, those that apply now.
    let private touchCommands (e: Element) (s: TextBoxState) : View list =
        let labels =
            match e.Root with
            | ValueSome root -> root.AppValues.Get TextBoxMenu.labels
            | ValueNone -> TextBoxMenuLabels.English
        let item (text: string) (run: unit -> unit) =
            MenuItem.create [ MenuItem.text text; MenuItem.onClick (fun _ -> if e.Root.IsSome then run ()) ]
        let edit = s.Edit
        [ // Cut and Paste leave a caret: its handle shows (decision 97).
          if canCut e then
              item labels.Cut (fun () ->
                  cut e
                  caretHandleOn e s)
          if canCopy e then item labels.Copy (fun () -> copy e)
          if canChange e then
              item labels.Paste (fun () ->
                  pasteClipboard e true
                  caretHandleOn e s)
          if edit.Text.Length > 0 && not (edit.Start = 0 && edit.End = edit.Text.Length) then
              item labels.SelectAll (fun () ->
                  // The row stays, with what applies to everything.
                  s.KeepRow <- true
                  selectEverything e) ]

    /// Room for a finger on each of the row's items.
    let private rowStyles = [ MenuItem.style [] [ MenuItem.padding (14.0, 11.0) ] ]

    /// The row: the box's context menu for a finger (decisions 80, 94 and 95), in the box a flyout has. An app's
    /// `contextMenu` replaces the box's own commands; a Menu is laid out as a row (unless that didn't fit the
    /// window), any other view is shown as it is.
    let private rowView (e: Element) (s: TextBoxState) (anchor: Rect) (middle: bool) : View voption =
        let asRow (menu: View) =
            if s.RowVertical then menu
            else menu.With(StackProps.orientation, box Horizontal).With(LayoutProps.minWidth, box Auto)
        let content =
            match e.Get FlyoutProps.contextMenu with
            | ValueSome view when obj.ReferenceEquals(view.Type, Menu.Type) -> ValueSome(asRow view)
            | ValueSome view -> ValueSome view
            | ValueNone ->
                match touchCommands e s with
                | [] -> ValueNone
                | items -> ValueSome(asRow (Menu.create [ Menu.children items ]))
        match content with
        | ValueSome content ->
            let row =
                Flyout.create [
                    Flyout.styles rowStyles
                    Setting.create BorderProps.content (ValueSome content)
                    Setting.create SelectionLayer.anchor anchor
                    Setting.create SelectionLayer.middle middle
                    Setting.create SelectionLayer.onOverflow (if s.RowVertical then ValueNone else ValueSome s.RowTooWide)
                    Setting.create Menus.onClose (ValueSome s.RowClosed)
                ]
            ValueSome(row.WithKey(box "row"))
        | ValueNone -> ValueNone

    /// What the layer above the window's content shows now (null: nothing, and the layer goes): asked before every
    /// frame while the handles show, once the box has been laid out.
    let private touchView (e: Element) (s: TextBoxState) : View =
        if not s.Touch || e.Root.IsNone || not e.IsFocused || not e.IsEnabled then
            s.Touch <- false
            Unchecked.defaultof<View>
        else
            let b = e.Bounds
            let size = Size.Create(b.Width, b.Height)
            settleScroll e s size
            let origin = e.PositionInTree
            // Something around the box scrolled it: the row waits until that has stopped.
            if not (Double.IsNaN s.OriginX) && (origin.X <> s.OriginX || origin.Y <> s.OriginY) then rowScrolled e s
            s.OriginX <- origin.X
            s.OriginY <- origin.Y
            // Where text can show, in the window: the box's own clip, cut by every scroller around it.
            let area = e.VisiblePart(textClip e size)
            let edit = s.Edit
            let composing = s.Composition.Length > 0
            let selected = edit.HasSelection && not composing
            let boxOf (index: int) (atEnd: bool) = (caretBox e s size index atEnd).Offset(origin.X, origin.Y)
            // A handle shows while the middle of its caret does (decision 87).
            let shows (box: Rect) =
                let y = box.Y + box.Height / 2.0
                not area.IsEmpty && box.X >= area.X - 1.0 && box.X <= area.Right + 1.0 && y >= area.Y && y <= area.Bottom
            let startBox = boxOf edit.Start false
            let endBox = boxOf edit.End true
            let caret = boxOf edit.Caret s.AtLineEnd
            // The handle a finger holds stays whatever scrolls (and its partner: the ends may have crossed).
            let held = s.HandleDrag = 1 || s.HandleDrag = 2
            let showStart = selected && (shows startBox || held)
            let showEnd = selected && (shows endBox || held)
            let showCaret = not selected && not composing && ((s.CaretHandle && canChange e && shows caret) || s.HandleDrag = 3)
            let wantRow = s.Row && not s.RowHeld && s.HandleDrag = 0 && not s.LongPressed && not composing
            let nowhere = Rect.Create(0.0, 0.0, 0.0, 0.0)
            // On the first line that shows, the row clears the box's own edge too, not only the text.
            let clearOfBox (r: Rect) =
                if r.Y - area.Y < r.Height then
                    let top = min r.Y (e.VisibleBounds.Y)
                    Rect.FromEdges(r.X, top, r.Right, r.Bottom)
                else r
            let struct (hasRow, anchor, middle) =
                if not wantRow then struct (false, nowhere, false)
                elif selected then
                    let whole =
                        if startBox.Y = endBox.Y then Rect.FromEdges(startBox.X, startBox.Y, endBox.X, startBox.Bottom)
                        else
                            let struct (left, width) = textArea e size
                            Rect.FromEdges(origin.X + left, startBox.Y, origin.X + left + width, endBox.Bottom)
                    let part = whole.Intersect area
                    if part.IsEmpty then struct (false, nowhere, false)
                    // Neither end in view: the row goes in the middle of what shows (decision 86).
                    else
                        let middle = not (shows startBox) && not (shows endBox)
                        struct (true, (if middle || startBox.Y <> endBox.Y then part else clearOfBox part), middle)
                elif shows caret then struct (true, clearOfBox caret, false)
                else struct (false, nowhere, false)
            if not selected && not s.CaretHandle && not s.Row && s.HandleDrag = 0 && not s.LongPressed then
                // Nothing left to show (the caret's handle went): the layer goes until a finger comes back.
                s.Touch <- false
                Unchecked.defaultof<View>
            else
                let hidden = Double.NegativeInfinity
                let key =
                    { StartX = (if showStart then startBox.X else hidden)
                      StartY = (if showStart then startBox.Bottom else hidden)
                      EndX = (if showEnd then endBox.X else hidden)
                      EndY = (if showEnd then endBox.Bottom else hidden)
                      CaretX = (if showCaret then caret.X else hidden)
                      CaretY = (if showCaret then caret.Bottom else hidden)
                      Row = hasRow
                      Anchor = anchor
                      Middle = middle
                      Vertical = s.RowVertical
                      Version = s.TouchVersion
                      Menu = (match e.Get FlyoutProps.contextMenu with ValueSome view -> box view | ValueNone -> null) }
                if not (isNull (box s.TouchView)) && not (isNull s.TouchKey) && (unbox<TouchShown> s.TouchKey).Equals key then s.TouchView
                else
                    let handle (kind: SelectionHandleKind) (name: string) (x: float) (y: float) (drag: int -> Point -> unit) =
                        (SelectionHandle.create [
                            Setting.create SelectionHandleProps.kind kind
                            Setting.create SelectionHandleProps.point (Point(x, y))
                            Setting.create SelectionHandleProps.onDrag (ValueSome drag)
                         ]).WithKey(box name)
                    let view =
                        SelectionLayer.create [
                            // The row first: the handles are above it, and take the finger first.
                            if hasRow then
                                match rowView e s anchor middle with
                                | ValueSome row -> row
                                | ValueNone -> ()
                            if showStart then handle SelectionHandleKind.Start "start" startBox.X startBox.Bottom s.HandleDrags[0]
                            if showEnd then handle SelectionHandleKind.End "end" endBox.X endBox.Bottom s.HandleDrags[1]
                            // Under the middle of the caret's line.
                            if showCaret then handle SelectionHandleKind.Caret "caret" (caret.X + caretWidth / 2.0) caret.Bottom s.HandleDrags[2]
                        ]
                    s.TouchKey <- box key
                    s.TouchView <- view
                    view

    /// A finger took a handle (`phase` 0), moves with it (1), let go (2) or was taken away (3), at `at` in the
    /// window: that end of the selection (or the caret) follows, by characters (decision 89).
    let private handleDrag (e: Element) (s: TextBoxState) (kind: int) (phase: int) (at: Point) =
        if e.Root.IsSome then
            let b = e.Bounds
            let size = Size.Create(b.Width, b.Height)
            let origin = e.PositionInTree
            match phase with
            // (While one finger has a handle or extends a long press, another is ignored: decision 102.)
            | 0 when s.HandleDrag = 0 && not s.LongPressed ->
                adopt e s
                let edit = s.Edit
                let struct (index, other, atEnd) =
                    match kind with
                    | 1 -> struct (edit.Start, edit.End, false)
                    | 2 -> struct (edit.End, edit.Start, true)
                    | _ -> struct (edit.Caret, edit.Caret, s.AtLineEnd)
                let box = caretBox e s size index atEnd
                s.HandleDrag <- kind
                s.HandleFixed <- other
                // The place in the text is the middle of the caret's line, above the finger: it stays that far away.
                s.HandleDX <- origin.X + box.X - at.X
                s.HandleDY <- origin.Y + box.Y + box.Height / 2.0 - at.Y
                s.HandleX <- at.X
                s.HandleY <- at.Y
                s.HandleMoved <- false
                s.DragWord <- ValueNone
                s.Dragging <- true
                s.LastX <- box.X
                s.LastY <- box.Y + box.Height / 2.0
                // The caret's handle stays while it is held.
                s.CaretHandleGeneration <- s.CaretHandleGeneration + 1
                s.TouchVersion <- s.TouchVersion + 1
                e.InvalidateVisual()
            | 1 when s.HandleDrag = kind ->
                if abs (at.X - s.HandleX) > 4.0 || abs (at.Y - s.HandleY) > 4.0 then s.HandleMoved <- true
                if s.HandleMoved then
                    let x, y = at.X + s.HandleDX - origin.X, at.Y + s.HandleDY - origin.Y
                    s.LastX <- x
                    s.LastY <- y
                    dragTo e s size x y
                    // Held past the text's edge, it keeps scrolling, as a mouse's drag does.
                    if not s.DragTimer && isOutside e size x y then
                        s.DragTimer <- true
                        s.DragGeneration <- s.DragGeneration + 1
                        dragLater e s s.DragGeneration
            | 2
            | 3 when s.HandleDrag = kind ->
                stopDrag s
                s.HandleDrag <- 0
                if kind = 3 then
                    // A tap on the caret's handle shows the row, or puts it away.
                    if phase = 2 && not s.HandleMoved then
                        s.Row <- not s.Row
                        s.RowVertical <- false
                    caretHandleOn e s
                else
                    // The row comes back when the finger lifts.
                    s.Row <- true
                    s.RowVertical <- false
                s.RowHeld <- false
                s.TouchVersion <- s.TouchVersion + 1
                e.InvalidateVisual()
            | _ -> ()

    /// The handles show (and the row, if `Row` says so): the box asks for its layer.
    let private showTouch (e: Element) (s: TextBoxState) =
        if isNull (box s.TouchContent) then
            s.TouchContent <- fun () -> box (touchView e s)
            s.HandleDrags <- [| handleDrag e s 1; handleDrag e s 2; handleDrag e s 3 |]
            // Wider than the window (long words in another language, an app's long menu): from the next frame it
            // is a menu in the same place (decision 96).
            s.RowTooWide <-
                fun () ->
                    match e.Root with
                    | ValueSome root ->
                        root.AfterFrame(fun () ->
                            s.RowVertical <- true
                            s.TouchVersion <- s.TouchVersion + 1
                            e.InvalidateVisual())
                    | ValueNone -> ()
            // One of the row's items was chosen (its own handler has run).
            s.RowClosed <-
                fun () ->
                    if s.KeepRow then s.KeepRow <- false else s.Row <- false
                    s.TouchVersion <- s.TouchVersion + 1
                    e.InvalidateVisual()
        s.Touch <- true
        s.TouchVersion <- s.TouchVersion + 1
        match e.Root with
        | ValueSome root -> root.ElementLayer(e, s.TouchContent)
        | ValueNone -> ()
        e.InvalidateVisual()

    /// A finger held still on the text (decision 82): on a word, the word is selected, and dragging on selects by
    /// words; elsewhere the caret goes there. The handles show at once, the row when the finger lifts. A password
    /// is selected whole (decision 98).
    let private longPress (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        let s = stateOf e
        if args.PointerKind = PointerKind.Touch && s.TouchDown && e.IsFocused then
            args.Handle()
            s.TouchDown <- false
            adopt e s
            let size = ctx.Size
            let x, y = args.LocalPosition.X, args.LocalPosition.Y
            let struct (index, atEnd) = pointAt e s size x y
            let struct (onWord, under) = wordUnder e s size x y index atEnd
            s.LongPressed <- true
            s.TouchId <- args.PointerId
            s.Row <- false
            s.DragWord <- ValueNone
            if e.Get TextBoxProps.isPassword && s.Edit.Text.Length > 0 then apply e s (TextEdit.selectAll s.Edit)
            elif onWord then
                let next = TextEdit.selectWordAt under s.Edit
                s.DragWord <- ValueSome(struct (next.Start, next.End))
                s.Dragging <- true
                s.LastX <- x
                s.LastY <- y
                apply e s next
            else
                apply e s (TextEdit.moveTo index false s.Edit)
                s.AtLineEnd <- atEnd
            showTouch e s

    /// The press the box held was taken away: a drag ends where it is.
    let private pointerCanceled (ctx: InputContext) =
        let e = ctx.Element
        let s = stateOf e
        stopDrag s
        s.BarDrag <- false
        s.TouchDown <- false
        s.DragWord <- ValueNone
        if s.LongPressed then
            s.LongPressed <- false
            s.Row <- true
            s.TouchVersion <- s.TouchVersion + 1
        e.InvalidateVisual()

    let private pointerDown (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        if args.Button = ValueSome MouseButton.Right && args.PointerKind = PointerKind.Mouse then
            hideTouch e (stateOf e)
            // A right press, before the menu: the box takes the focus, and the caret goes to the press unless that
            // is inside the selection (which the menu's Cut and Copy are then about).
            let s = stateOf e
            adopt e s
            commitComposition e s
            if not e.IsFocused then e.Focus()
            let struct (index, atEnd) = pointAt e s ctx.Size args.LocalPosition.X args.LocalPosition.Y
            if not (s.Edit.HasSelection && index >= s.Edit.Start && index <= s.Edit.End) then
                apply e s (TextEdit.moveTo index false s.Edit)
                s.AtLineEnd <- atEnd
        if args.Button = ValueSome MouseButton.Left then
            let s = stateOf e
            adopt e s
            commitComposition e s
            let x, y = args.LocalPosition.X, args.LocalPosition.Y
            let size = ctx.Size
            if args.PointerKind = PointerKind.Touch then
                // A finger: lifting it places the caret (see pointerUp), unless it scrolls first or stays for a
                // long press. The press isn't taken, so a finger can still scroll what's around the box. Another
                // finger, while one has a handle or extends a long press, does nothing (DESIGN.md, "9g",
                // decision 102).
                if s.HandleDrag = 0 && not s.LongPressed then
                    s.TouchDown <- true
                    s.TouchTime <- ctx.Time
                    s.PressX <- x
                    s.PressY <- y
            elif overBar e s size x then
                // The scrollbar: drag the thumb, or a page towards a press beside it.
                args.Handle()
                match thumbOf s size with
                | ValueSome(struct (length, start)) ->
                    let thumbStart = ScrollViewer.inset + start
                    if y >= thumbStart && y <= thumbStart + length then
                        s.BarDrag <- true
                        s.BarDragStart <- y
                        s.BarDragOffset <- s.ScrollY
                    else
                        let page = max (lineHeightOf e) (s.ViewHeight - lineHeightOf e)
                        scrollTo e s (s.ScrollY + (if y < thumbStart then -page else page)) |> ignore
                    e.InvalidateVisual()
                | ValueNone -> ()
            else
                // The mouse or a pen: no handles (decision 81).
                hideTouch e s
                let struct (index, atEnd) = pointAt e s size x y
                args.Handle()
                let now = ctx.Time
                let again =
                    s.PressCount > 0
                    && now - s.PressTime <= multiClickTime
                    && abs (x - s.PressX) <= multiClickDistance
                    && abs (y - s.PressY) <= multiClickDistance
                s.PressCount <- if again && s.PressCount < 3 then s.PressCount + 1 else 1
                s.PressTime <- now
                s.PressX <- x
                s.PressY <- y
                s.LastX <- x
                s.LastY <- y
                let shift = args.Modifiers.HasFlag Modifiers.Shift
                let password = e.Get TextBoxProps.isPassword
                match s.PressCount with
                | 2 when not password ->
                    let next = TextEdit.selectWordAt index s.Edit
                    s.DragWord <- ValueSome(struct (next.Start, next.End))
                    s.Dragging <- true
                    apply e s next
                | 3 when isMulti e ->
                    s.DragWord <- ValueNone
                    s.Dragging <- false
                    apply e s (TextEdit.selectParagraphAt index s.Edit)
                | 2
                | 3 ->
                    s.DragWord <- ValueNone
                    s.Dragging <- false
                    apply e s (TextEdit.selectAll s.Edit)
                | _ ->
                    s.DragWord <- ValueNone
                    s.Dragging <- true
                    apply e s (TextEdit.moveTo index shift s.Edit)
                    s.AtLineEnd <- atEnd

    let private pointerMove (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        let s = stateOf e
        let x, y = args.LocalPosition.X, args.LocalPosition.Y
        let size = ctx.Size
        if s.BarDrag then
            args.Handle()
            match thumbOf s size with
            | ValueSome(struct (length, _)) ->
                let travel = size.Height - 2.0 * ScrollViewer.inset - length
                if travel > 0.0 then scrollTo e s (s.BarDragOffset + (y - s.BarDragStart) * s.MaxScrollY / travel) |> ignore
            | ValueNone -> ()
        elif s.Dragging && s.HandleDrag = 0 && (not s.LongPressed || args.PointerId = s.TouchId) then
            args.Handle()
            s.LastX <- x
            s.LastY <- y
            dragTo e s size x y
            if not s.DragTimer && isOutside e size x y then
                s.DragTimer <- true
                s.DragGeneration <- s.DragGeneration + 1
                dragLater e s s.DragGeneration
        else
            let over = ctx.IsPointerOver && overBar e s size x
            if over <> s.HoverBar then
                s.HoverBar <- over
                e.InvalidateVisual()

    let private pointerUp (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        let s = stateOf e
        if s.LongPressed && args.PointerKind = PointerKind.Touch && args.PointerId = s.TouchId then
            // The finger of a long press lifted: the row shows (and the caret's handle, with nothing selected).
            args.Handle()
            s.LongPressed <- false
            stopDrag s
            s.DragWord <- ValueNone
            s.Row <- true
            s.RowVertical <- false
            s.RowHeld <- false
            if not s.Edit.HasSelection then caretHandleOn e s
            showTouch e s
        elif s.TouchDown && args.PointerKind = PointerKind.Touch then
            s.TouchDown <- false
            adopt e s
            let x, y = args.LocalPosition.X, args.LocalPosition.Y
            let struct (index, atEnd) = pointAt e s ctx.Size x y
            let struct (onWord, under) = wordUnder e s ctx.Size x y index atEnd
            let password = e.Get TextBoxProps.isPassword
            let again =
                s.TapTime > TimeSpan.MinValue
                && s.TouchTime - s.TapTime <= multiClickTime
                && abs (x - s.TapX) <= tapDistance
                && abs (y - s.TapY) <= tapDistance
            if again && (onWord || (password && s.Edit.Text.Length > 0)) then
                // A double-tap: the word, with its handles and the row (decision 83); a password whole.
                s.TapTime <- TimeSpan.MinValue
                apply e s (if password then TextEdit.selectAll s.Edit else TextEdit.selectWordAt under s.Edit)
                s.Row <- true
                s.RowVertical <- false
                s.RowHeld <- false
                showTouch e s
            else
                s.TapTime <- s.TouchTime
                s.TapX <- x
                s.TapY <- y
                if s.Touch && s.Edit.HasSelection && index >= s.Edit.Start && index <= s.Edit.End then
                    // A tap inside the selection shows the row, or puts it away (decision 91).
                    s.Row <- not s.Row
                    s.RowVertical <- false
                    s.RowHeld <- false
                    s.TouchVersion <- s.TouchVersion + 1
                    e.InvalidateVisual()
                else
                    // A tap: the caret goes where the finger lifted, with its handle (decision 88).
                    apply e s (TextEdit.moveTo index false s.Edit)
                    s.AtLineEnd <- atEnd
                    s.Row <- false
                    caretHandleOn e s
                    showTouch e s
        if s.BarDrag then
            args.Handle()
            s.BarDrag <- false
            ctx.Element.InvalidateVisual()
        if s.Dragging && s.HandleDrag = 0 then
            args.Handle()
            stopDrag s

    /// The wheel scrolls several lines while there is more that way; at the end it is left to what is around the
    /// box.
    let private wheel (ctx: InputContext) (args: WheelEventArgs) =
        let e = ctx.Element
        if isMulti e && args.DeltaY <> 0.0 && args.DeltaX = 0.0 && not (args.Modifiers.HasFlag Modifiers.Shift) then
            let s = stateOf e
            if scrollTo e s (s.ScrollY - args.DeltaY * ScrollViewer.step) then args.Handle()

    /// A finger drags on several lines that overflow: the text follows it. The drag is taken only when there is
    /// more that way at its start, so at an end it scrolls the page (DESIGN.md, "9d", decision 58).
    let private pan (ctx: InputContext) (dx: float) (dy: float) =
        let e = ctx.Element
        let s = stateOf e
        if not s.Panning then
            let canScroll = isMulti e && s.MaxScrollY > 0.0 && abs dy >= abs dx && ((dy > 0.0 && s.ScrollY > 0.0) || (dy < 0.0 && s.ScrollY < s.MaxScrollY))
            if canScroll then
                s.Panning <- true
                s.TouchDown <- false
        if s.Panning then scrollTo e s (s.ScrollY - dy) |> ignore
        s.Panning

    let private panEnded (ctx: InputContext) (_: bool) =
        let s = stateOf ctx.Element
        s.Panning <- false
        s.TouchDown <- false

    let private keyDown (ctx: InputContext) (args: KeyEventArgs) =
        let e = ctx.Element
        let mods = args.Modifiers
        let ctrl, shift = mods.HasFlag Modifiers.Control, mods.HasFlag Modifiers.Shift
        let composing = (stateOf e).Composition.Length > 0
        // While an input method composes, keys are its own: arrows move in what it composes, Enter commits it
        // (DESIGN.md, "9d", decision 55).
        if composing then args.Handle()
        elif mods &&& (Modifiers.Alt ||| Modifiers.Meta) = Modifiers.None then
            let s = stateOf e
            hideTouch e s
            s.Composed <- false
            adopt e s
            let edit = s.Edit
            let readOnly = e.Get TextBoxProps.isReadOnly
            let multi = isMulti e
            // A password is one word: Ctrl+arrows don't show where its words end.
            let byWord = ctrl && not (e.Get TextBoxProps.isPassword)
            let go (next: EditState) =
                args.Handle()
                if next <> edit then apply e s next else restartBlink e s
            let change (f: EditState -> EditState) =
                args.Handle()
                if not readOnly then
                    let next = f edit
                    if next <> edit then applyEdit e s EditKind.Deleting next else restartBlink e s
            let typed (kind: EditKind) (text: string) =
                args.Handle()
                let next = inserted e text edit
                if next <> edit then applyEdit e s kind next else restartBlink e s
            // The keys do what the box's menu does.
            let copy () =
                args.Handle()
                copy e
            let cut () =
                args.Handle()
                cut e
            let pasteNow () =
                args.Handle()
                pasteClipboard e false
            let step (take: EditHistory -> struct (EditState * EditHistory) voption) =
                args.Handle()
                if not (stepHistory e take) then restartBlink e s
            // Several lines: up or down by `delta` lines, keeping to the x the first of a run of such moves
            // started from. Past the first line: the text's start; past the last: its end.
            let vertical (delta: int) =
                args.Handle()
                let lines = currentLines e s
                let struct (p, l, n) = MultiLine.locate lines edit.Caret s.AtLineEnd
                let x = if Double.IsNaN s.WantedX then MultiLine.caretX e.TextSystem lines p l edit.Caret else s.WantedX
                let last = lines.LineCount - 1
                let mutable atEnd = false
                let next =
                    if delta < 0 && n = 0 then TextEdit.move CaretMove.Start shift edit
                    elif delta > 0 && n = last then TextEdit.move CaretMove.End shift edit
                    else
                        let struct (index, wrapEnd) = MultiLine.indexAt e.TextSystem lines (max 0 (min last (n + delta))) x
                        atEnd <- wrapEnd
                        TextEdit.moveTo index shift edit
                if next <> edit then apply e s next else restartBlink e s
                s.AtLineEnd <- atEnd
                s.WantedX <- x
            // Several lines: the start or the end of the line the caret is on, as drawn.
            let lineEdge (toEnd: bool) =
                args.Handle()
                let lines = currentLines e s
                let struct (p, l, _) = MultiLine.locate lines edit.Caret s.AtLineEnd
                let paragraph = lines.Paragraphs[p]
                let line = paragraph.Lines[l]
                let next = TextEdit.moveTo (lines.Offset p + (if toEnd then line.End else line.Start)) shift edit
                if next <> edit then apply e s next else restartBlink e s
                s.AtLineEnd <- toEnd && l < paragraph.Lines.Length - 1
            // A page: the lines that show, less one that stays in view.
            let page () = max 1 (int (Math.Floor(s.ViewHeight / lineHeightOf e + 0.05)) - 1)
            let submit () =
                match e.Get TextBoxProps.onSubmitted with
                | ValueSome handler ->
                    args.Handle()
                    if not args.IsRepeat then handler edit.Text
                | ValueNone -> ()
            match args.Key with
            | Key.Up when multi -> vertical -1
            | Key.Down when multi -> vertical 1
            | Key.PageUp when multi -> vertical (-(page ()))
            | Key.PageDown when multi -> vertical (page ())
            | Key.Home when multi && not ctrl -> lineEdge false
            | Key.End when multi && not ctrl -> lineEdge true
            // Several lines: Enter (and Shift+Enter) is a line break and a step of its own; Ctrl+Enter submits. With
            // `submitOnEnter`, Enter alone submits too (DESIGN.md, "9e", decision 60).
            | Key.Enter when multi && not ctrl && not shift && e.Get TextBoxProps.submitOnEnter -> submit ()
            | Key.Enter when multi && not ctrl -> if not readOnly then typed EditKind.Single "\n"
            | Key.Enter when multi -> submit ()
            | Key.Tab when e.Get TextBoxProps.acceptsTab && not ctrl && not shift && not readOnly -> typed EditKind.Typing "\t"
            | Key.Left -> go (TextEdit.move (if byWord then CaretMove.WordLeft elif ctrl then CaretMove.Start else CaretMove.Left) shift edit)
            | Key.Right -> go (TextEdit.move (if byWord then CaretMove.WordRight elif ctrl then CaretMove.End else CaretMove.Right) shift edit)
            | Key.Home
            | Key.Up -> go (TextEdit.move CaretMove.Start shift edit)
            | Key.End
            | Key.Down -> go (TextEdit.move CaretMove.End shift edit)
            | Key.Char 'a' when ctrl && not shift -> go (TextEdit.selectAll edit)
            | Key.Char 'c' when ctrl && not shift -> copy ()
            | Key.Insert when ctrl && not shift -> copy ()
            | Key.Char 'x' when ctrl && not shift -> cut ()
            | Key.Delete when shift && not ctrl -> cut ()
            | Key.Char 'v' when ctrl && not shift -> pasteNow ()
            | Key.Insert when shift && not ctrl -> pasteNow ()
            | Key.Char 'z' when ctrl && not shift -> step TextHistory.undo
            | Key.Char 'y' when ctrl && not shift -> step TextHistory.redo
            | Key.Char 'z' when ctrl && shift -> step TextHistory.redo
            | Key.Backspace -> change (if byWord then TextEdit.backspaceWord elif ctrl then (fun s -> TextEdit.backspace (TextEdit.moveTo 0 true s)) else TextEdit.backspace)
            | Key.Delete when not shift ->
                change (if byWord then TextEdit.deleteWord elif ctrl then (fun s -> TextEdit.delete (TextEdit.moveTo s.Text.Length true s)) else TextEdit.delete)
            | Key.Enter when not ctrl -> submit ()
            // Keys that type: taken, so they do nothing else on the way up (Space scrolling a page). Their text
            // comes as text input.
            | Key.Char _
            | Key.Space when not ctrl -> args.Handle()
            | _ -> ()

    let private textInput (ctx: InputContext) (args: TextInputEventArgs) =
        let e = ctx.Element
        if not (e.Get TextBoxProps.isReadOnly) then
            args.Handle()
            let s = stateOf e
            hideTouch e s
            adopt e s
            // What an input method composed is committed as typed text: one undo step (decision 54).
            let kind = if s.Composed then EditKind.Single else EditKind.Typing
            s.Composed <- false
            endComposition e s
            let next = inserted e args.Text s.Edit
            if next <> s.Edit then applyEdit e s kind next else restartBlink e s

    /// What an input method is composing: shown at the caret in place of the selection, not typed yet.
    let private textComposition (ctx: InputContext) (args: TextCompositionEventArgs) =
        let e = ctx.Element
        let s = stateOf e
        if e.Get TextBoxProps.isReadOnly then
            // Nothing is composed into a box that can't change.
            if args.Text.Length > 0 then ctx.ClearComposition()
        elif args.Text.Length = 0 then
            // Ended: what was committed, if anything, comes as typed text.
            endComposition e s
        else
            hideTouch e s
            adopt e s
            if s.Composition <> args.Text || s.CompositionCaret <> args.Caret then
                s.Composition <- args.Text
                s.CompositionCaret <- args.Caret
                s.Composed <- true
                s.Reveal <- true
                restartBlink e s
                e.InvalidateVisual()
                if isMulti e then e.InvalidateMeasure()

    let private focusChanged (ctx: InputContext) (isFocused: bool) (byTab: bool) =
        let e = ctx.Element
        let s = stateOf e
        if isFocused then
            adopt e s
            // Tab, then typing, replaces a field's value (DESIGN.md, "9a", decision 15); several lines of notes
            // keep their caret.
            if byTab && not (isMulti e) then s.Edit <- TextEdit.selectAll s.Edit
            s.History <- TextHistory.close s.History
            s.Reveal <- true
            restartBlink e s
            // The window hears at once that text is wanted here and which keyboard suits it (a phone shows its
            // keyboard from inside the touch that asked); the next frame says exactly where the caret is.
            let struct (left, _) = textArea e ctx.Size
            let struct (top, _) = textRows e ctx.Size
            ctx.SetTextInput(Rect.Create(left, top, 1.0, lineHeightOf e), keyboardOf e, isMulti e)
            // In the browser these keys are the box's, not the browser's (DESIGN.md, "9b", decision 32). Ctrl+V is
            // left to it: its paste event is where the text comes from.
            if not s.Claimed then
                s.Claimed <- true
                match RenderServices.TryCurrent with
                | ValueSome { Window = ValueSome window } ->
                    for key in [ 'z'; 'y'; 'c'; 'x' ] do
                        window.ClaimKey(Key.Char key, Modifiers.Control)
                    window.ClaimKey(Key.Char 'z', Modifiers.Control ||| Modifiers.Shift)
                | _ -> ()
        else
            commitComposition e s
            hideTouch e s
            stopDrag s
            s.BarDrag <- false
            s.TouchDown <- false
            s.BlinkGeneration <- s.BlinkGeneration + 1
            s.CaretOn <- true
            // Out of focus one line shows its start again; several stay where they were scrolled to.
            s.Scroll <- 0.0
        e.InvalidateVisual()

    /// How many lines high a multi-line box asks to be for `count` lines of text.
    let private wantedLines (e: Element) (count: int) =
        let least = max 1 (e.Get TextBoxProps.minLines)
        let most = e.Get TextBoxProps.maxLines
        let count = max least count
        if most > 0 then min count (max least most) else count

    let private measure (ctx: MeasureContext) (available: Size) =
        let ix, iy = Border.inset ctx
        let lineHeight = ctx.Text.LineHeight(ctx.Get TextProps.font, ctx.Get TextProps.fontSize)
        let e = ctx.Element
        if isMulti e then
            let s = stateOf e
            settle e s
            // The width it was last drawn with; before that, the width it is offered (or asks for).
            let width =
                if s.TextWidth > 0.0 then s.TextWidth
                elif Double.IsInfinity available.Width then preferredWidth
                else max 1.0 (available.Width - ix)
            let lines = wantedLines e (linesFor e s width).LineCount
            s.MeasuredLines <- lines
            Size.Create(preferredWidth + ix, float lines * lineHeight + iy)
        else Size.Create(preferredWidth + ix, lineHeight + iy)

    /// One line.
    let private renderLine (ctx: RenderContext) (list: DrawList) =
        let e = ctx.Element
        let s = stateOf e
        settle e s
        measureStops e s
        let size = ctx.Size
        let struct (left, width) = textArea e size
        let thickness = ctx.Get BorderProps.borderThickness
        let padding = ctx.Get BorderProps.padding
        let font, fontSize = ctx.Get TextProps.font, ctx.Get TextProps.fontSize
        let lineHeight = ctx.Text.LineHeight(font, fontSize)
        // Whole pixels for everything that moves, so the text, the selection and the caret stay sharp.
        let scale = list.Scale
        let snap (v: float) = Math.Round(v * scale) / scale
        let innerHeight = max 0.0 (size.Height - padding.Vertical - 2.0 * thickness)
        let top = snap (padding.Top + thickness + (innerHeight - lineHeight) / 2.0)
        // With what an input method is composing in place of the selection.
        let edit = s.Shown
        let stops = s.Stops
        let focused = ctx.IsFocused
        let caretDips = max caretWidth (1.0 / scale)
        // The caret stays in view; no more is scrolled than there is text for.
        scrollLine s width scale focused
        let scroll = s.Scroll
        list.PushClip(Rect.Create(left, thickness, width, max 0.0 (size.Height - 2.0 * thickness)))
        if s.Display.Length = 0 then
            let placeholder = ctx.Get TextBoxProps.placeholder
            if placeholder.Length > 0 then
                ctx.Text.Draw(list, placeholder, font, fontSize, left, top, ctx.Get TextBoxProps.placeholderForeground)
        else
            if focused && edit.HasSelection then
                let x0, x1 = snap (left + stops[edit.Start] - scroll), snap (left + stops[edit.End] - scroll)
                list.FillRect(Rect.Create(x0, top, x1 - x0, snap lineHeight), ctx.Get TextBoxProps.selection)
            ctx.Text.Draw(list, s.Display, font, fontSize, left - scroll, top, ctx.Get TextProps.foreground)
            if s.Composition.Length > 0 then
                // What is being composed is underlined: it isn't typed yet.
                let from = s.Edit.Start
                let x0, x1 = snap (left + stops[from] - scroll), snap (left + stops[from + s.Composition.Length] - scroll)
                list.FillRect(Rect.Create(x0, snap (top + lineHeight) - snap caretDips, x1 - x0, snap caretDips), ctx.Get TextProps.foreground)
        let caretX = snap (left + stops[edit.Caret] - scroll)
        if focused && s.CaretOn && ctx.IsEnabled && not (ctx.Get TextBoxProps.isReadOnly) then
            list.FillRect(Rect.Create(caretX, top, snap caretDips, snap lineHeight), ctx.Get TextProps.foreground)
        list.PopClip()
        // Where an input method shows its candidates, and a phone's keyboard knows what is typed.
        if focused then ctx.SetTextInput(Rect.Create(caretX, top, caretDips, lineHeight), keyboardOf e, false)

    /// Several lines: the lines that show, the selection behind them, the caret and the scrollbar.
    let private renderLines (ctx: RenderContext) (list: DrawList) =
        let e = ctx.Element
        let s = stateOf e
        settle e s
        let size = ctx.Size
        let struct (left, width) = textArea e size
        let struct (top, viewHeight) = textRows e size
        let thickness = ctx.Get BorderProps.borderThickness
        let font, fontSize = ctx.Get TextProps.font, ctx.Get TextProps.fontSize
        let text = ctx.Text
        s.TextWidth <- width
        let lines = linesFor e s width
        let count = lines.LineCount
        // The width differs from what the measure wrapped at (a resize): measure again with this one.
        if wantedLines e count <> s.MeasuredLines then e.InvalidateMeasure()
        let scale = list.Scale
        let snap (v: float) = Math.Round(v * scale) / scale
        // Lines are as far apart as a TextBlock's; each lands on a whole pixel.
        let lineHeight = text.LineHeight(font, fontSize)
        // With what an input method is composing in place of the selection.
        let edit = s.Shown
        let composedFrom = s.Edit.Start
        let composedTo = composedFrom + s.Composition.Length
        let focused = ctx.IsFocused
        let caretDips = max caretWidth (1.0 / scale)
        let struct (caretP, caretL, caretLine) = MultiLine.locate lines edit.Caret s.AtLineEnd
        scrollLines s count lineHeight viewHeight scale caretLine
        let scroll = s.ScrollY
        // Text scrolls under the padding and stops at the border.
        let clipTop, clipHeight = thickness, max 0.0 (size.Height - 2.0 * thickness)
        list.PushClip(Rect.Create(left, clipTop, width, clipHeight))
        if edit.Text.Length = 0 then
            let placeholder = ctx.Get TextBoxProps.placeholder
            if placeholder.Length > 0 then
                let layout = text.Layout(placeholder, font, fontSize, max 1.0 width, Wrap, NoTrimming, 0)
                text.DrawLayout(list, layout, left, snap top, width, Start, ctx.Get TextBoxProps.placeholderForeground)
        else
            // Only the lines that show (DESIGN.md, "9c", decision 44).
            let first = max 0 (int (Math.Floor((scroll - (top - clipTop)) / lineHeight)))
            let last = min (count - 1) (int (Math.Floor((scroll + clipHeight - (top - clipTop)) / lineHeight)))
            let selected = focused && edit.HasSelection
            let selection = ctx.Get TextBoxProps.selection
            let foreground = ctx.Get TextProps.foreground
            // A selected line break shows as a little width after its line.
            let breakWidth = snap (fontSize * 0.3)
            let mutable p = if first <= last then lines.ParagraphOfLine first else 0
            for n in first .. last do
                while n >= lines.FirstLine(p + 1) do
                    p <- p + 1
                let l = n - lines.FirstLine p
                let paragraph = lines.Paragraphs[p]
                let line = paragraph.Lines[l]
                let y = snap (top + float n * lineHeight - scroll)
                if selected then
                    let lineStart, lineEnd = lines.Offset p + line.Start, lines.Offset p + line.End
                    let from, upTo = max edit.Start lineStart, min edit.End lineEnd
                    // The selection holds the line break after this line (the last of its paragraph).
                    let past = l = paragraph.Lines.Length - 1 && edit.Start <= lineEnd && edit.End > lineEnd
                    if from < upTo || past then
                        let stops = MultiLine.stopsOf text lines p l
                        let x0 = min width stops[from - lineStart]
                        let x1 = min width (stops[max from upTo - lineStart] + (if past then breakWidth else 0.0))
                        // Down to where the next line starts, so the rows of a selection meet.
                        let bottom = snap (top + float (n + 1) * lineHeight - scroll)
                        if x1 > x0 then list.FillRect(Rect.Create(snap (left + x0), y, snap (x1 - x0), bottom - y), selection)
                if line.Text.Length > 0 then text.Draw(list, line.Text, font, fontSize, left, y, foreground)
                if composedTo > composedFrom then
                    // What is being composed is underlined: it isn't typed yet.
                    let lineStart, lineEnd = lines.Offset p + line.Start, lines.Offset p + line.End
                    let from, upTo = max composedFrom lineStart, min composedTo lineEnd
                    if from < upTo then
                        let stops = MultiLine.stopsOf text lines p l
                        let x0, x1 = min width stops[from - lineStart], min width stops[upTo - lineStart]
                        let bottom = snap (top + float (n + 1) * lineHeight - scroll)
                        list.FillRect(Rect.Create(snap (left + x0), bottom - snap caretDips, snap (x1 - x0), snap caretDips), foreground)
        // Spaces at a wrap hang past the edge: the caret after them stays at it.
        let caretX = snap (left + max 0.0 (min (width - caretDips) (MultiLine.caretX text lines caretP caretL edit.Caret)))
        let caretY = snap (top + float caretLine * lineHeight - scroll)
        if focused && s.CaretOn && ctx.IsEnabled && not (ctx.Get TextBoxProps.isReadOnly) then
            list.FillRect(Rect.Create(caretX, caretY, snap caretDips, snap lineHeight), ctx.Get TextProps.foreground)
        list.PopClip()
        // Where an input method shows its candidates, and a phone's keyboard knows what is typed.
        if focused then ctx.SetTextInput(Rect.Create(caretX, caretY, caretDips, lineHeight), keyboardOf e, true)
        // The scrollbar, as a ScrollViewer draws its own: thin, wider under the pointer or while dragged.
        match thumbOf s size with
        | ValueSome(struct (length, start)) ->
            let wide = s.BarDrag || (s.HoverBar && ctx.IsPointerOver)
            let bar = ctx.Animate("bar", (if wide then wideBar else ScrollViewer.thinBar), TimeSpan.FromMilliseconds 100.0)
            let r = Rect.Create(size.Width - ScrollViewer.inset - bar, ScrollViewer.inset + start, bar, length)
            list.FillRoundedRect(r, CornerRadius.Uniform(bar / 2.0), ctx.Theme Palette.scrollThumb)
        | ValueNone -> ()

    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
        if isMulti ctx.Element then renderLines ctx list else renderLine ctx list
        if ctx.IsFocusVisible then FocusRing.draw ctx list (ctx.Get BorderProps.cornerRadius)

    // ---- For assistive technology (DESIGN.md, "A2: a TextBox's text") ----

    /// The most lines a screen reader is given at once: beyond it, those around the caret (decision 18).
    let internal semanticLineCap = 1000

    /// One line for a screen reader: its characters (what the caret steps over as one), where each stands (`xOf`
    /// a place in `text`), and the characters that start a word, as the caret's word moves see words.
    let private semanticLine (number: int) (text: string) (endsWithBreak: bool) (xOf: int -> float) (bounds: Rect) : SemanticTextLine =
        // (White space at the line's start is a word of its own to AccessKit; so the first character always starts one.)
        let struct (stops, words) = TextEdit.characters text
        { Number = number
          Text = text
          EndsWithBreak = endsWithBreak
          Stops = stops
          Positions = Array.map xOf stops
          WordStarts = words
          Bounds = bounds }

    /// How many characters of `text` come before the place `index`.
    let private charactersBefore (text: string) (index: int) =
        let mutable i = 0
        let mutable n = 0
        while i < index && i < text.Length do
            i <- TextEdit.nextStop text i
            n <- n + 1
        n

    /// The place in `text` after `count` characters (its end, if it has fewer).
    let private placeAfter (text: string) (count: int) =
        let mutable i = 0
        let mutable n = 0
        while n < count && i < text.Length do
            i <- TextEdit.nextStop text i
            n <- n + 1
        i

    /// The lines a screen reader is given of several lines: all of them, or those around the caret's.
    let private semanticRange (count: int) (caretLine: int) : struct (int * int) =
        if count <= semanticLineCap then struct (0, count - 1)
        else
            let first = max 0 (min (count - semanticLineCap) (caretLine - semanticLineCap / 2))
            struct (first, first + semanticLineCap - 1)

    /// The box's text for a screen reader: its lines as drawn (what an input method composes included, bullets
    /// for a password) and its selection.
    let private semanticText (e: Element) : SemanticTextInfo voption =
        let s = stateOf e
        let b = e.Bounds
        let size = Size.Create(b.Width, b.Height)
        let shown = s.Shown
        let lineHeight = lineHeightOf e
        let struct (left, width) = textArea e size
        let struct (top, height) = textRows e size
        if isMulti e then
            let lines = currentLines e s
            let count = lines.LineCount
            let place (index: int) (atEnd: bool) (first: int) (last: int) : SemanticTextPosition =
                let struct (p, l, n) = MultiLine.locate lines index atEnd
                if n < first then { Line = first; Character = 0 }
                elif n > last then
                    let lastP = lines.ParagraphOfLine last
                    let line = lines.Paragraphs[lastP].Lines[last - lines.FirstLine lastP]
                    { Line = last; Character = charactersBefore (lines.Paragraphs[lastP].Text.Substring(line.Start, line.End - line.Start)) Int32.MaxValue }
                else
                    let paragraph = lines.Paragraphs[p]
                    let line = paragraph.Lines[l]
                    let text = paragraph.Text.Substring(line.Start, line.End - line.Start)
                    { Line = n; Character = charactersBefore text (index - lines.Offset p - line.Start) }
            let struct (_, _, caretLine) = MultiLine.locate lines shown.Caret s.AtLineEnd
            let struct (first, last) = semanticRange count caretLine
            let made =
                [| for n in first .. last do
                       let p = lines.ParagraphOfLine n
                       let l = n - lines.FirstLine p
                       let paragraph = lines.Paragraphs[p]
                       let line = paragraph.Lines[l]
                       let text = paragraph.Text.Substring(line.Start, line.End - line.Start)
                       let stops = MultiLine.stopsOf e.TextSystem lines p l
                       // The last line of a paragraph that another follows ends with the break between them.
                       let endsWithBreak = l = paragraph.Lines.Length - 1 && p < lines.Paragraphs.Length - 1
                       let bounds = Rect.Create(left, top + float n * lineHeight - s.ScrollY, width, lineHeight)
                       semanticLine n text endsWithBreak (fun i -> stops[max 0 (min (stops.Length - 1) i)]) bounds |]
            ValueSome
                { Lines = made
                  Anchor = place shown.Anchor false first last
                  Caret = place shown.Caret s.AtLineEnd first last }
        else
            measureStops e s
            let text = shown.Text
            let stops = s.Stops
            let password = e.Get TextBoxProps.isPassword
            let bounds = Rect.Create(left - s.Scroll, top + (height - lineHeight) / 2.0, max width stops[stops.Length - 1], lineHeight)
            let line =
                if password then
                    // A bullet for each character: where each stands, and nothing of what they are.
                    let places = ResizeArray<float>()
                    let mutable i = 0
                    places.Add stops[0]
                    while i < text.Length do
                        i <- TextEdit.nextStop text i
                        places.Add stops[i]
                    { Number = 0
                      Text = s.Display
                      EndsWithBreak = false
                      Stops = [| 0 .. places.Count - 1 |]
                      Positions = places.ToArray()
                      WordStarts = (if places.Count > 1 then [| 0 |] else [||])
                      Bounds = bounds }
                else semanticLine 0 s.Display false (fun i -> stops[max 0 (min (stops.Length - 1) i)]) bounds
            ValueSome
                { Lines = [| line |]
                  Anchor = { Line = 0; Character = charactersBefore text shown.Anchor }
                  Caret = { Line = 0; Character = charactersBefore text shown.Caret } }

    /// Where in the text a place a screen reader names is, and whether it is the end of a line that wraps on.
    let private indexOfPlace (e: Element) (s: TextBoxState) (place: SemanticTextPosition) : struct (int * bool) =
        if isMulti e then
            let lines = currentLines e s
            let n = max 0 (min (lines.LineCount - 1) place.Line)
            let p = lines.ParagraphOfLine n
            let l = n - lines.FirstLine p
            let paragraph = lines.Paragraphs[p]
            let line = paragraph.Lines[l]
            let text = paragraph.Text.Substring(line.Start, line.End - line.Start)
            let offset = placeAfter text (max 0 place.Character)
            struct (lines.Offset p + line.Start + offset, offset = text.Length && offset > 0 && l < paragraph.Lines.Length - 1)
        else struct (placeAfter s.Edit.Text (max 0 place.Character), false)

    /// What a screen reader asks of the text (decision 22), each by the path the matching input takes: a selection
    /// as a drag makes one, a replacement as pasted text, a new value as everything selected and pasted over.
    let private semanticActionWith (ctx: InputContext) (action: SemanticAction) (argument: SemanticArgument) : bool =
        let e = ctx.Element
        let s = stateOf e
        adopt e s
        match action, argument with
        | SemanticAction.SetTextSelection, SemanticArgument.TextSelection(anchor, caret) ->
            commitComposition e s
            hideTouch e s
            let struct (from, _) = indexOfPlace e s anchor
            let struct (upTo, atEnd) = indexOfPlace e s caret
            let next = TextEdit.select from upTo s.Edit
            if next <> s.Edit then apply e s next
            s.AtLineEnd <- atEnd
            true
        | SemanticAction.ReplaceSelectedText, SemanticArgument.Text text when canChange e ->
            commitComposition e s
            hideTouch e s
            let next = inserted e text s.Edit
            if next <> s.Edit then applyEdit e s EditKind.Single next
            true
        | SemanticAction.SetValue, SemanticArgument.Text text when canChange e ->
            commitComposition e s
            hideTouch e s
            let next = inserted e text (TextEdit.selectAll s.Edit)
            if next <> s.Edit then applyEdit e s EditKind.Single next
            true
        | _ -> false

    let Type =
        Element.extend Border.Type "TextBox" [
            Element.measure measure
            Element.render render
            Element.pointerDown pointerDown
            Element.pointerMove pointerMove
            Element.pointerUp pointerUp
            Element.pointerCanceled pointerCanceled
            Element.longPress longPress
            Element.wheel wheel
            Element.pan pan
            Element.panEnded panEnded
            Element.keyDown keyDown
            Element.textInput textInput
            Element.textComposition textComposition
            Element.contextMenu contextMenu
            Element.focusChanged focusChanged
            // The dark base look (also without a theme); the theme adds the state looks.
            Element.defaults [
                Semantics.role Role.TextBox
                Semantics.labelFrom TextBoxProps.placeholder
                // Its text (a password's is not told), and whether it can be changed.
                Setting.create SemanticsProps.value (
                    Read([| TextBoxProps.text; TextBoxProps.isPassword |], fun e -> if e.Get TextBoxProps.isPassword then ValueNone else ValueSome(e.Get TextBoxProps.text)))
                Setting.create SemanticsProps.isProtected (Read([| TextBoxProps.isPassword |], fun e -> e.Get TextBoxProps.isPassword))
                Semantics.readOnlyFrom TextBoxProps.isReadOnly
                // Its lines and selection, and what a screen reader can ask of them.
                Semantics.textFrom semanticText
                Semantics.actions [ SemanticAction.SetTextSelection; SemanticAction.ReplaceSelectedText; SemanticAction.SetValue ]
                Semantics.onActionWith semanticActionWith
                // (A password is one line whatever `multiline` says.)
                Setting.create SemanticsProps.multiline (
                    Read([| TextBoxProps.multiline; TextBoxProps.isPassword |], fun e -> e.Get TextBoxProps.multiline && not (e.Get TextBoxProps.isPassword)))
                // Its look at rest, by theme value; the selection is the accent, tinted.
                Layoutable.foreground Palette.text
                Border.background Palette.surface
                Border.borderBrush Palette.border
                Border.borderThickness 1.0
                Border.cornerRadius Palette.cornerRadius
                TextBox.placeholderForeground Palette.textDim
                Setting.fromThemeWith TextBoxProps.selection Palette.accent ThemeConvert.textSelectionTint
                Border.padding (12.0, 7.0)
                Layoutable.cursor Cursor.Text
                Layoutable.focusable true
            ]
        ]

    let create (settings: Setting<TextBox> list) : View = View.Create(Type, settings)
    /// A style for TextBoxes: `TextBox.style [ States.focused ] [ TextBox.borderBrush c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<TextBox> list) : Style = Style.forType Type parts settings
