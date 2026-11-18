namespace Scribble.Core

open System
open System.Threading.Tasks
open Scribble.Accessibility
open Scribble.Rendering
open Scribble.Text

/// What a TextBox keeps: its text with the selection, where each caret place is, how far it is scrolled, a drag
/// in progress, the last press (for double and triple clicks), and the caret's blinking.
[<Sealed; AllowNullLiteral>]
type internal TextBoxState() =
    member val Edit = TextEdit.empty with get, set
    /// The `text` setting's value when it was last looked at: another value since means the view changed it.
    member val Seen: string = "" with get, set
    /// What the caret places below were measured for.
    member val StopsText: string = null with get, set
    member val StopsFont: Font = Unchecked.defaultof<Font> with get, set
    member val StopsSize = 0.0 with get, set
    member val StopsPassword = false with get, set
    /// What is drawn (bullets for a password) and the x of every caret place, by index into the text.
    member val Display = "" with get, set
    member val Stops: float[] = [| 0.0 |] with get, set
    /// How far the text is scrolled to the left, in DIPs.
    member val Scroll = 0.0 with get, set
    member val Dragging = false with get, set
    /// The word a double-click selected: a drag on from it selects by words.
    member val DragWord: struct (int * int) voption = ValueNone with get, set
    member val PressTime = TimeSpan.MinValue with get, set
    member val PressX = 0.0 with get, set
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

module TextBoxProps =
    /// The text. Given by the view: controlled; otherwise the control's own value.
    let text = Prop.define "Text" "" Affects.Render |> AppState.mark
    let onTextChanged: Prop<(string -> unit) voption> = Prop.define "OnTextChanged" ValueNone Affects.Nothing
    let onSubmitted: Prop<(string -> unit) voption> = Prop.define "OnSubmitted" ValueNone Affects.Nothing
    /// Shown, dim, while the text is empty.
    let placeholder = Prop.define "Placeholder" "" Affects.Render
    let placeholderForeground = Prop.define "PlaceholderForeground" (Color.Hex 0x9A9AB0) Affects.Render
    /// The text can be selected and copied but not changed.
    let isReadOnly = Prop.define "IsReadOnly" false Affects.Render
    /// The most grapheme clusters the user can enter; 0: no limit.
    let maxLength = Prop.define "MaxLength" 0 Affects.Nothing
    /// Bullets are shown in place of the characters.
    let isPassword = Prop.define "IsPassword" false Affects.Render
    /// Behind the selected text.
    let selection = Prop.define "Selection" ((Color.Hex 0x3D59A1).WithAlpha 0.5f) Affects.Render
    let internal state: Prop<TextBoxState> = Prop.local "TextBoxState" null

/// One line of text to type in (DESIGN.md, "M9: TextBox"):
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
/// around it. Mouse: a click places the caret, a drag selects, Shift+click extends, a double-click
/// selects a word and a triple-click everything. Tabbing into it selects all its text. Text wider than the box
/// scrolls sideways with the caret. The caret moves by what a reader sees as one character (an emoji is never
/// split); it blinks, and stops after ten seconds without input.
[<AbstractClass>]
type TextBox =
    inherit Border
    /// The text. Given: controlled (it shows exactly this); left out: its own text, starting empty.
    static member text<'k when 'k :> TextBox>(value: string) : Setting<'k> = Setting.create TextBoxProps.text (if isNull value then "" else value)
    /// Called with the whole new text on every change the user makes.
    static member onTextChanged<'k when 'k :> TextBox>(handler: string -> unit) : Setting<'k> = Setting.create TextBoxProps.onTextChanged (ValueSome handler)
    /// Called with the text when the user presses Enter. Without it, Enter goes on to whatever is around the box.
    static member onSubmitted<'k when 'k :> TextBox>(handler: string -> unit) : Setting<'k> = Setting.create TextBoxProps.onSubmitted (ValueSome handler)
    /// Shown, dim, while the text is empty; also its name for screen readers unless `Semantics.label` gives one.
    static member placeholder<'k when 'k :> TextBox>(text: string) : Setting<'k> = Setting.create TextBoxProps.placeholder (if isNull text then "" else text)
    /// The placeholder's colour (the theme's dim text).
    static member placeholderForeground<'k when 'k :> TextBox>(value: Color) : Setting<'k> = Setting.create TextBoxProps.placeholderForeground value
    static member placeholderForeground<'k when 'k :> TextBox>(themeValue: ThemeValue<Color>) : Setting<'k> =
        Setting.fromTheme TextBoxProps.placeholderForeground themeValue
    /// True: the text can be selected and copied but not changed (it still takes focus). Default: false.
    static member isReadOnly<'k when 'k :> TextBox>(value: bool) : Setting<'k> = Setting.create TextBoxProps.isReadOnly value
    /// The most characters the user can enter, counted as a reader sees them (an emoji is one); what doesn't fit
    /// is left out. 0 (default): no limit. Text the view gives is never cut.
    static member maxLength<'k when 'k :> TextBox>(value: int) : Setting<'k> = Setting.create TextBoxProps.maxLength (max 0 value)
    /// True: a bullet shows for each character, the text counts as one word, and it can't be copied or cut.
    /// Default: false.
    static member isPassword<'k when 'k :> TextBox>(value: bool) : Setting<'k> = Setting.create TextBoxProps.isPassword value
    /// The colour behind selected text (the theme's accent, tinted).
    static member selection<'k when 'k :> TextBox>(value: Color) : Setting<'k> = Setting.create TextBoxProps.selection value
    static member selection<'k when 'k :> TextBox>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme TextBoxProps.selection themeValue

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

    /// The view gave other text than the box shows. As its answer to an edit (a filter, or a view that didn't
    /// follow), the edit's undo step ends in what the view says; for any other reason (a Clear button, loaded
    /// data, a changed undo) nothing from before can be undone (DESIGN.md, "9b", decision 31).
    let private viewChanged (s: TextBoxState) (text: string) =
        s.Edit <- TextEdit.withText text s.Edit
        s.History <- if s.Pending = 1 then TextHistory.amend s.Edit s.History else TextHistory.empty

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

    /// What is drawn and where each caret place is, measured once per text, font and size.
    let private measureStops (e: Element) (s: TextBoxState) =
        let text = s.Edit.Text
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
                s.Display <- text
                s.Stops <- e.TextSystem.CaretStops(text, font, size)

    /// The left edge of the text area and its width, inside the border and the padding.
    let private textArea (e: Element) (size: Size) : struct (float * float) =
        let p, t = e.Get BorderProps.padding, e.Get BorderProps.borderThickness
        struct (p.Left + t, max 0.0 (size.Width - p.Horizontal - 2.0 * t))

    /// The caret place nearest to `x` (the element's own coordinates).
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

    /// Every change to the text or the selection comes through here: the new state shows, the caret starts
    /// blinking again, new text is recorded for undo as a step of `kind` (none: an undo or a redo, which moved the
    /// history itself) and `onTextChanged` hears of it. A change of the caret or the selection alone ends the
    /// undo step being typed.
    let private applyWith (e: Element) (s: TextBoxState) (kind: EditKind voption) (next: EditState) =
        let before = s.Edit
        s.Edit <- next
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

    /// Pastes `text` over the selection (a step of its own), if the box can still take it.
    let private paste (e: Element) (text: string) =
        if e.Root.IsSome && e.IsFocused && not (e.Get TextBoxProps.isReadOnly) then
            let s = stateOf e
            adopt e s
            let next = TextEdit.insert text (e.Get TextBoxProps.maxLength) s.Edit
            if next <> s.Edit then applyEdit e s EditKind.Single next

    let private pointerDown (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        if args.Button = ValueSome MouseButton.Left then
            let s = stateOf e
            adopt e s
            let x = args.LocalPosition.X
            let index = indexAt e s ctx.Size x
            if args.PointerKind = PointerKind.Touch then
                // A tap places the caret; the press isn't taken, so a finger can still scroll what's around the
                // box. Selecting by touch comes with the handles (9d).
                apply e s (TextEdit.moveTo index false s.Edit)
            else
                args.Handle()
                let now = ctx.Time
                let again = s.PressCount > 0 && now - s.PressTime <= multiClickTime && abs (x - s.PressX) <= multiClickDistance
                s.PressCount <- if again && s.PressCount < 3 then s.PressCount + 1 else 1
                s.PressTime <- now
                s.PressX <- x
                let shift = args.Modifiers.HasFlag Modifiers.Shift
                let password = e.Get TextBoxProps.isPassword
                match s.PressCount with
                | 2 when not password ->
                    let next = TextEdit.selectWordAt index s.Edit
                    s.DragWord <- ValueSome(struct (next.Start, next.End))
                    s.Dragging <- true
                    apply e s next
                | 2
                | 3 ->
                    s.DragWord <- ValueNone
                    s.Dragging <- false
                    apply e s (TextEdit.selectAll s.Edit)
                | _ ->
                    s.DragWord <- ValueNone
                    s.Dragging <- true
                    apply e s (TextEdit.moveTo index shift s.Edit)

    let private pointerMove (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        let s = stateOf e
        if s.Dragging then
            args.Handle()
            adopt e s
            let index = indexAt e s ctx.Size args.LocalPosition.X
            let next =
                match s.DragWord with
                | ValueSome(struct (first, last)) ->
                    // From a double-click: whole words, the first one always in.
                    let struct (wordFirst, wordLast) = TextEdit.wordAt s.Edit.Text index
                    if index < first then TextEdit.select last wordFirst s.Edit
                    else TextEdit.select first (max last (if index > last then wordLast else last)) s.Edit
                | ValueNone -> TextEdit.moveTo index true s.Edit
            if next <> s.Edit then apply e s next

    let private pointerUp (ctx: InputContext) (args: PointerEventArgs) =
        let s = stateOf ctx.Element
        if s.Dragging then
            args.Handle()
            s.Dragging <- false

    let private keyDown (ctx: InputContext) (args: KeyEventArgs) =
        let e = ctx.Element
        let mods = args.Modifiers
        let ctrl, shift = mods.HasFlag Modifiers.Control, mods.HasFlag Modifiers.Shift
        if mods &&& (Modifiers.Alt ||| Modifiers.Meta) = Modifiers.None then
            let s = stateOf e
            adopt e s
            let edit = s.Edit
            let readOnly = e.Get TextBoxProps.isReadOnly
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
            let password = e.Get TextBoxProps.isPassword
            // A password never leaves the box (DESIGN.md, "9b", decision 26).
            let copy () =
                args.Handle()
                if edit.HasSelection && not password then ctx.Clipboard.SetText edit.SelectedText
            let cut () =
                args.Handle()
                if edit.HasSelection && not password && not readOnly then
                    ctx.Clipboard.SetText edit.SelectedText
                    applyEdit e s EditKind.Single (TextEdit.deleteSelection edit)
            let pasteNow () =
                args.Handle()
                if not readOnly then
                    // The desktop answers at once; the browser after its paste event.
                    let reading = ctx.Clipboard.GetText()
                    if reading.IsCompletedSuccessfully then paste e reading.Result
                    else
                        reading.ContinueWith(
                            (fun (t: Task<string>) ->
                                try
                                    if t.IsCompletedSuccessfully then paste e t.Result
                                with error ->
                                    match e.Root with
                                    | ValueSome root -> root.ReportError error
                                    | ValueNone -> ()),
                            TaskContinuationOptions.ExecuteSynchronously
                        )
                        |> ignore
            let step (take: EditHistory -> struct (EditState * EditHistory) voption) =
                args.Handle()
                if not readOnly then
                    match take s.History with
                    | ValueSome(struct (state, history)) ->
                        s.History <- history
                        applyWith e s ValueNone (TextEdit.normalize state)
                    | ValueNone -> restartBlink e s
            match args.Key with
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
            | Key.Enter when not ctrl ->
                match e.Get TextBoxProps.onSubmitted with
                | ValueSome handler ->
                    args.Handle()
                    if not args.IsRepeat then handler edit.Text
                | ValueNone -> ()
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
            adopt e s
            let next = TextEdit.insert args.Text (e.Get TextBoxProps.maxLength) s.Edit
            if next <> s.Edit then applyEdit e s EditKind.Typing next else restartBlink e s

    let private focusChanged (ctx: InputContext) (isFocused: bool) (byTab: bool) =
        let e = ctx.Element
        let s = stateOf e
        if isFocused then
            adopt e s
            // Tab, then typing, replaces a field's value (DESIGN.md, "9a", decision 15).
            if byTab then s.Edit <- TextEdit.selectAll s.Edit
            s.History <- TextHistory.close s.History
            restartBlink e s
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
            s.Dragging <- false
            s.BlinkGeneration <- s.BlinkGeneration + 1
            s.CaretOn <- true
            // Out of focus it shows its start again.
            s.Scroll <- 0.0
        e.InvalidateVisual()

    let private measure (ctx: MeasureContext) (_: Size) =
        let ix, iy = Border.inset ctx
        Size.Create(preferredWidth + ix, ctx.Text.LineHeight(ctx.Get TextProps.font, ctx.Get TextProps.fontSize) + iy)

    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
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
        let edit = s.Edit
        let stops = s.Stops
        let focused = ctx.IsFocused
        let caretDips = max caretWidth (1.0 / scale)
        if focused then
            // The caret stays in view; no more is scrolled than there is text for.
            let caret = stops[edit.Caret]
            let mutable scroll = s.Scroll
            if caret - scroll > width - caretDips then scroll <- caret - width + caretDips
            if caret - scroll < 0.0 then scroll <- caret
            s.Scroll <- snap (max 0.0 (min scroll (stops[stops.Length - 1] + caretDips - width)))
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
        if focused && s.CaretOn && ctx.IsEnabled && not (ctx.Get TextBoxProps.isReadOnly) then
            let x = snap (left + stops[edit.Caret] - scroll)
            list.FillRect(Rect.Create(x, top, snap caretDips, snap lineHeight), ctx.Get TextProps.foreground)
        list.PopClip()
        if ctx.IsFocusVisible then FocusRing.draw ctx list (ctx.Get BorderProps.cornerRadius)

    let Type =
        Element.extend Border.Type "TextBox" [
            Element.measure measure
            Element.render render
            Element.pointerDown pointerDown
            Element.pointerMove pointerMove
            Element.pointerUp pointerUp
            Element.keyDown keyDown
            Element.textInput textInput
            Element.focusChanged focusChanged
            // The dark base look (also without a theme); the theme adds the state looks.
            Element.defaults [
                Semantics.role Role.TextBox
                Semantics.labelFrom TextBoxProps.placeholder
                Layoutable.foreground (Color.Hex 0xE6E6F0)
                Border.background (Color.Hex 0x24243A)
                Border.borderBrush (Color.Hex 0x4A4A68)
                Border.borderThickness 1.0
                Border.cornerRadius 6.0
                Border.padding (12.0, 7.0)
                Layoutable.cursor Cursor.Text
                Layoutable.focusable true
            ]
        ]

    let create (settings: Setting<TextBox> list) : View = View.Create(Type, settings)
    /// A style for TextBoxes: `TextBox.style [ States.focused ] [ TextBox.borderBrush c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<TextBox> list) : Style = Style.forType Type parts settings
