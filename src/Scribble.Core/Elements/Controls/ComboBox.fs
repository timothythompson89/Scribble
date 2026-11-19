namespace Scribble.Core

open System
open Scribble.Accessibility
open Scribble.Rendering

/// What a ComboBox keeps between layouts: its items' keys, the element showing the selected item,
/// and the scroller of the list in its flyout.
[<Sealed; AllowNullLiteral>]
type internal ComboState() =
    member val Source = ItemsSource.Empty with get, set
    member val Store: KeyStore = TypedKeyStore<obj, obj>() :> KeyStore with get, set
    member val Content: Element voption = ValueNone with get, set
    member val Scroller = Scroller() with get
    member val Typeahead = TypeaheadState() with get

module ComboBoxProps =
    /// The selected key (none: the placeholder shows). Given by the view: controlled; otherwise its own.
    let selected: Prop<obj voption> = Prop.define "Selected" ValueNone Affects.Measure |> AppState.mark
    /// Shown in the box while nothing is selected.
    let placeholder = Prop.define "Placeholder" "" Affects.Measure
    let placeholderForeground = Prop.define "PlaceholderForeground" (Color.Hex 0x9A9AB0) Affects.Measure
    /// The most its list's height can be, in DIPs; more items scroll.
    let maxListHeight = Prop.define "MaxListHeight" 320.0 Affects.Measure
    /// The chevron at the right of the box.
    let chevron = Prop.define "Chevron" (Color.Hex 0x9A9AB0) Affects.Render
    /// (keys, items, added, removed), boxed; the setting unboxes them for the app's handler.
    let internal onSelectedChanged: Prop<(obj list -> obj list -> obj list -> obj list -> unit) voption> =
        Prop.define "OnSelectedChanged" ValueNone Affects.Nothing
    let internal state: Prop<ComboState> = Prop.local "ComboState" null

/// A box showing one item chosen from a list it opens in its flyout (DESIGN.md, "4c: ComboBox"):
///
///     ComboBox.create [
///         ComboBox.source model.Countries
///         ComboBox.keySelector (fun (c: Country) -> c.Code)
///         ComboBox.viewSelector (fun (c: Country) -> c.Name)             // or a view
///         ComboBox.selected (ValueSome model.Country)                    // left out: it keeps its own
///         ComboBox.placeholder "Pick a country"
///         ComboBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Country, string>) -> dispatch (Pick e.Key))
///     ]
///
/// `source`, `keySelector` and `viewSelector` are a ListBox's: the list inside is one, so 10,000
/// items cost what a screenful does. A click, Space, Enter, Alt+↓ or F4 opens the list; there the
/// arrows move through the items, Enter or a click picks one and closes it, Escape or Tab closes it
/// with nothing changed. While it's closed, ↑/↓ and Home/End pick the next item straight away.
/// Its list is its flyout: `Flyout.style [ Style.inside ComboBox.Type [] ] [ ... ]` restyles it.
[<AbstractClass>]
type ComboBox =
    inherit Border

    /// The items, as a ListBox's `source` (an array or `IReadOnlyList` is used as it is; compared by reference).
    static member source<'k, 'T when 'k :> ComboBox>(items: seq<'T>) : Setting<'k> =
        let s: Setting<ItemsView> = ItemsView.source items
        Setting.create ItemsViewProps.source (unbox<ItemsSource> s.Value)
    /// How to get the key that identifies an item (default: the item itself). The selection is a key.
    static member keySelector<'k, 'T, 'K when 'k :> ComboBox and 'K: equality>(f: 'T -> 'K) : Setting<'k> =
        Setting.create ItemsViewProps.keySelector (TypedKeyFunc<'T, 'K>(f) :> KeyFunc)
    /// The view for one item, in the list and in the box.
    static member viewSelector<'k, 'T when 'k :> ComboBox>(f: 'T -> View) : Setting<'k> =
        Setting.create ItemsViewProps.viewSelector (ItemsFunc<View voption>(fun item -> ValueSome(f (unbox<'T> item))))
    /// The text for one item, in the list and in the box (and its name for screen readers).
    static member viewSelector<'k, 'T when 'k :> ComboBox>(f: 'T -> string) : Setting<'k> =
        Setting.create ItemsViewProps.viewSelector (ItemsFunc<View voption>((fun item -> ValueSome(TextBlock.create [ TextBlock.text (f (unbox<'T> item)) ])), Text = ValueSome(fun item -> f (unbox<'T> item))))
    /// An item's text when a view draws it, as a ListBox's `textSelector`: typing picks the item whose text
    /// starts with what is typed, and a screen reader calls the item by it.
    static member textSelector<'k, 'T when 'k :> ComboBox>(f: 'T -> string) : Setting<'k> =
        Setting.create ItemsViewProps.textSelector (ValueSome(ItemsFunc<string>(fun item -> f (unbox<'T> item))))
    /// The selected key, or none (the placeholder shows). Given: controlled (it shows exactly this);
    /// left out: its own selection, starting with none. A key not in the source shows the placeholder.
    static member selected<'k, 'K when 'k :> ComboBox>(key: 'K voption) : Setting<'k> =
        Setting.create ComboBoxProps.selected (key |> ValueOption.map box)
    /// Called with the new selection when the user picks another item (`e.Key`, `e.Item`).
    static member onSelectedChanged<'k, 'T, 'K when 'k :> ComboBox>(handler: SelectionChangedEventArgs<'T, 'K> -> unit) : Setting<'k> =
        let boxed (keys: obj list) (items: obj list) (added: obj list) (removed: obj list) =
            handler (
                SelectionChangedEventArgs<'T, 'K>(
                    keys |> List.map unbox<'K>, items |> List.map unbox<'T>, added |> List.map unbox<'K>, removed |> List.map unbox<'K>)
            )
        Setting.create ComboBoxProps.onSelectedChanged (ValueSome boxed)
    /// Shown in the box while nothing is selected.
    static member placeholder<'k when 'k :> ComboBox>(text: string) : Setting<'k> = Setting.create ComboBoxProps.placeholder text
    /// The placeholder's colour (the theme's dim text).
    static member placeholderForeground<'k when 'k :> ComboBox>(value: Color) : Setting<'k> = Setting.create ComboBoxProps.placeholderForeground value
    static member placeholderForeground<'k when 'k :> ComboBox>(themeValue: ThemeValue<Color>) : Setting<'k> =
        Setting.fromTheme ComboBoxProps.placeholderForeground themeValue
    /// The most its list's height can be, in DIPs (default 320, about eight items); more items scroll.
    static member maxListHeight<'k when 'k :> ComboBox>(value: float) : Setting<'k> = Setting.create ComboBoxProps.maxListHeight value
    /// The chevron's colour (the theme's dim text).
    static member chevron<'k when 'k :> ComboBox>(value: Color) : Setting<'k> = Setting.create ComboBoxProps.chevron value
    static member chevron<'k when 'k :> ComboBox>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme ComboBoxProps.chevron themeValue

module ComboBox =
    /// The chevron's width and height, and the space before it.
    let private chevronSize = 16.0
    let private gap = 8.0

    /// A chevron pointing down, in a 24 × 24 box.
    let private chevron = Icon.fromPath "M6 10 7.4 8.6 12 13.2 16.6 8.6 18 10 12 16z" [ Icon.name "chevron" ]

    let private stateOf (e: Element) =
        match e.Get ComboBoxProps.state with
        | null ->
            let s = ComboState()
            e.SetLocal(ComboBoxProps.state, s)
            s
        | s -> s

    let private same (a: obj voption) (b: obj voption) =
        match a, b with
        | ValueSome x, ValueSome y -> obj.Equals(x, y)
        | ValueNone, ValueNone -> true
        | _ -> false

    /// Where the selected key is in the source, if it's there.
    let private selectedIndex (e: Element) (s: ComboState) =
        match e.Get ComboBoxProps.selected with
        | ValueSome k -> s.Store.IndexOf k
        | ValueNone -> ValueNone

    /// The user picked `key` (or nothing): the own value follows unless the view gives it, then the
    /// handler hears of it.
    let private select (e: Element) (key: obj voption) =
        let before = e.Get ComboBoxProps.selected
        if not (same before key) then
            let s = stateOf e
            if not (ControlValue.isControlled e ComboBoxProps.selected) then e.Set(ComboBoxProps.selected, key)
            match e.Get ComboBoxProps.onSelectedChanged with
            | ValueSome h ->
                let keys = ValueOption.toList key
                let items =
                    keys |> List.choose (fun k -> match s.Store.IndexOf k with ValueSome i -> Some(s.Source.Item i) | ValueNone -> None)
                try
                    h keys items keys (ValueOption.toList before)
                with ex ->
                    e.ReportError ex
            | ValueNone -> ()

    /// The flyout asked to open or close (a click, Escape, a press elsewhere, a pick): opening,
    /// the list scrolls to the selected item first, so focus can land on it.
    let private setOpen (e: Element) (value: bool) =
        if e.Get ComboBoxOpenProps.isOpen <> value then
            if value then
                let s = stateOf e
                match selectedIndex e s with
                | ValueSome i -> s.Scroller.ScrollToIndex(i, ScrollAlign.Nearest, false)
                | ValueNone -> s.Scroller.ScrollToTop false
            e.Set(ComboBoxOpenProps.isOpen, value)

    /// An item in the open list was invoked (a click, Enter): it's the selection, and the list closes.
    let private commit (e: Element) (key: obj) =
        select e (ValueSome key)
        Flyouts.request e false

    /// Attaches an element this ComboBox realized and matches its styles now (during layout), as
    /// TabControl does.
    let private adopt (e: Element) (child: Element) =
        child.SetParent(ValueSome e)
        child.SetRoot e.Root
        child.InvalidateAll()
        match e.Root with
        | ValueSome(:? ElementTree as tree) -> tree.RunStylesWithin child
        | _ -> ()

    let private drop (child: Element) =
        child.SetParent ValueNone
        child.SetRoot ValueNone

    /// The flyout: a ListBox of the same items in a ScrollViewer. Nothing can be selected in it
    /// (the arrows only move focus); it shows the box's selection, and invoking an item picks it.
    let private flyoutView (e: Element) (s: ComboState) =
        let list =
            ListBox.create [
                Setting.create ItemsViewProps.source s.Source
                Setting.create ItemsViewProps.keySelector (e.Get ItemsViewProps.keySelector)
                Setting.create ItemsViewProps.viewSelector (e.Get ItemsViewProps.viewSelector)
                Setting.create ItemsViewProps.textSelector (e.Get ItemsViewProps.textSelector)
                ListBox.selectionMode SelectionMode.None
                Setting.create ListBoxProps.selected (e.Get ComboBoxProps.selected |> ValueOption.toList)
                Setting.create ListBoxProps.onItemInvoked (ValueSome(fun (key: obj) (_: obj) -> commit e key))
                ListBox.scroller s.Scroller
                // Tab leaves the list with nothing changed (it has one Tab stop, so Tab would go nowhere).
                ListBox.onKeyDown (fun args ->
                    if args.Key = Key.Tab then
                        args.Handle()
                        Flyouts.request e false)
            ]
        Flyout.create [
            // As wide as the box (a list takes all the width it's offered, which would be the window's).
            if e.Bounds.Width > 0.0 then Flyout.width e.Bounds.Width
            Flyout.matchAnchorWidth true
            Flyout.isOpen (e.Get ComboBoxOpenProps.isOpen)
            Flyout.onOpenChanged (setOpen e)
            Flyout.content (ScrollViewer.create [ ScrollViewer.maxHeight (e.Get ComboBoxProps.maxListHeight); ScrollViewer.content list ])
        ]

    /// Brings the keys, the box's content and the flyout in line with the settings.
    let private sync (e: Element) =
        let s = stateOf e
        let source = e.Get ItemsViewProps.source
        if not (source.Equals s.Source) then
            s.Source <- source
            s.Store <- s.Store.Update(source, e.Get ItemsViewProps.keySelector)
        // The box shows the selected item as the list does, else the placeholder. Clicks on it are the box's.
        let content =
            let view =
                match selectedIndex e s with
                | ValueSome i -> (e.Get ItemsViewProps.viewSelector).Invoke(source.Item i)
                | ValueNone -> ValueNone
            match view with
            | ValueSome v -> v
            | ValueNone ->
                let text = e.Get ComboBoxProps.placeholder
                // A space keeps the box a line high when there's nothing to say.
                TextBlock.create [ TextBlock.text (if text = "" then " " else text); TextBlock.foreground (e.Get ComboBoxProps.placeholderForeground) ]
        let content = content.With(LayoutProps.isHitTestVisible, box false)
        match s.Content with
        | ValueSome c when obj.ReferenceEquals(c.Type, content.Type) -> c.Patch content
        | existing ->
            existing |> ValueOption.iter drop
            let c = Element.RealizingIn(e.Root, fun () -> Element.Realize content)
            adopt e c
            s.Content <- ValueSome c
        e.SetManagedChildren(s.Content |> ValueOption.toArray)
        e.Set(FlyoutProps.flyout, ValueSome(flyoutView e s))
        // Its own selection: a key that left the source is dropped, and the app hears of it after the frame.
        match e.Get ComboBoxProps.selected, e.Root with
        | ValueSome k, ValueSome r when (s.Store.IndexOf k).IsNone && not (ControlValue.isControlled e ComboBoxProps.selected) ->
            r.AfterFrame(fun () -> if (s.Store.IndexOf k).IsNone then select e ValueNone)
        | _ -> ()

    /// Border's measure, with room for the chevron after the content.
    let private measure (ctx: MeasureContext) (available: Size) =
        let e = ctx.Element
        sync e
        let ix, iy = Border.inset ctx
        let extra = chevronSize + gap
        let mutable w, h = ix + extra, iy + chevronSize
        match (stateOf e).Content with
        | ValueSome child ->
            let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - ix - extra), max 0.0 (available.Height - iy)))
            w <- d.Width + ix + extra
            h <- max h (d.Height + iy)
        | ValueNone -> ()
        Size.Create(w, h)

    /// The content at the left, in the middle of the box's height; the chevron is drawn after it.
    let private arrange (ctx: ArrangeContext) (size: Size) =
        let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
        let width = max 0.0 (size.Width - p.Horizontal - 2.0 * t - chevronSize - gap)
        let height = max 0.0 (size.Height - p.Vertical - 2.0 * t)
        match (stateOf ctx.Element).Content with
        | ValueSome child ->
            let h = min height (ctx.DesiredSize child).Height
            ctx.Arrange(child, Rect.Create(p.Left + t, Math.Floor(p.Top + t + (height - h) / 2.0 + 0.5), width, h))
        | ValueNone -> ()

    /// Border's look, the chevron, and the focus ring.
    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
        let size = ctx.Size
        let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
        let x = Math.Floor(size.Width - p.Right - t - chevronSize + 0.5)
        let y = Math.Floor((size.Height - chevronSize) / 2.0 + 0.5)
        ctx.Text.DrawIcon(list, Icon.geometry chevron, chevron.ViewBox, 0.0, false, false, x, y, chevronSize, ctx.Get ComboBoxProps.chevron)
        if ctx.IsFocusVisible then FocusRing.draw ctx list (ctx.Get BorderProps.cornerRadius)

    /// Closed (keys in the open list go to it, in its own layer): Alt+↓ or F4 opens; ↑/↓ and
    /// Home/End pick the next item straight away. Space and Enter click it, which opens it.
    let private keyDown (ctx: InputContext) (args: KeyEventArgs) =
        let e = ctx.Element
        let s = stateOf e
        let held = args.Modifiers &&& (Modifiers.Control ||| Modifiers.Alt ||| Modifiers.Meta ||| Modifiers.Shift)
        let n = s.Store.Count
        let at = selectedIndex e s |> ValueOption.defaultValue -1
        let pick (i: int) =
            args.Handle()
            let i = max 0 (min (n - 1) i)
            if i <> at then select e (ValueSome(s.Store.KeyAt i))
        match args.Key with
        | Key.Down when held = Modifiers.Alt ->
            args.Handle()
            Flyouts.request e true
        | Key.F 4 when held = Modifiers.None ->
            args.Handle()
            Flyouts.request e true
        | Key.Down when held = Modifiers.None && n > 0 -> pick (at + 1)
        | Key.Up when held = Modifiers.None && n > 0 -> pick (if at < 0 then 0 else at - 1)
        | Key.Home when held = Modifiers.None && n > 0 -> pick 0
        | Key.End when held = Modifiers.None && n > 0 -> pick (n - 1)
        // Letters are building up: Space is one of them (it comes as typed text), and doesn't open the list.
        | Key.Space when held = Modifiers.None && Typeahead.isActive s.Typeahead ctx.Time -> args.Handle()
        | _ -> ()

    /// Closed: typing picks the next item whose text starts with what has been typed (DESIGN.md, "9e",
    /// decision 65). Open, the keys go to its list, which does the same with its highlight.
    let private textInput (ctx: InputContext) (args: TextInputEventArgs) =
        let e = ctx.Element
        let s = stateOf e
        let n = s.Store.Count
        let now = ctx.Time
        if n > 0 && (Typeahead.isActive s.Typeahead now || not (String.IsNullOrWhiteSpace args.Text)) then
            args.Handle()
            let at = selectedIndex e s |> ValueOption.defaultValue -1
            match Typeahead.find s.Typeahead now args.Text n at (fun i -> ItemsView.itemText e (s.Source.Item i)) with
            | ValueSome i when i <> at -> select e (ValueSome(s.Store.KeyAt i))
            | _ -> ()

    /// A screen reader opening or closing the list.
    let private semanticAction (ctx: InputContext) (action: SemanticAction) =
        match action with
        | SemanticAction.Expand ->
            Flyouts.request ctx.Element true
            true
        | SemanticAction.Collapse ->
            Flyouts.request ctx.Element false
            true
        | _ -> false

    let Type =
        Element.extend Border.Type "ComboBox" [
            Element.measure measure
            Element.arrange arrange
            Element.render render
            Element.keyDown keyDown
            Element.textInput textInput
            // The dark base look (also without a theme); the theme adds the state looks.
            Element.defaults [
                Semantics.role Role.ComboBox
                Semantics.expandedFrom ComboBoxOpenProps.isOpen
                Semantics.actions [ SemanticAction.Expand; SemanticAction.Collapse ]
                Semantics.onAction semanticAction
                Layoutable.foreground (Color.Hex 0xE6E6F0)
                Border.background (Color.Hex 0x24243A)
                Border.borderBrush (Color.Hex 0x4A4A68)
                Border.borderThickness 1.0
                Border.cornerRadius 6.0
                Border.padding (12.0, 7.0)
                Layoutable.cursor Cursor.Hand
                Layoutable.focusable true
            ]
        ]

    let create (settings: Setting<ComboBox> list) : View = View.Create(Type, settings)
    /// A style for ComboBoxes: `ComboBox.style [ States.isOpen ] [ ComboBox.borderBrush c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<ComboBox> list) : Style = Style.forType Type parts settings
