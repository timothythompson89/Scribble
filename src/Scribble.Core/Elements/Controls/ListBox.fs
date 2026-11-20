namespace Scribble.Core

open System
open Scribble.Accessibility
open Scribble.Rendering

/// How many items a ListBox lets the user select (DESIGN.md, M8 step 4b, decision 3).
[<RequireQualifiedAccess>]
type SelectionMode =
    /// Nothing can be selected: a click, tap, Enter or Space invokes the item (a list of actions).
    | None
    /// One item (default): a click selects it, the arrow keys move the selection.
    | Single
    /// Any number: Ctrl+click toggles, Shift+click selects a range, Ctrl+A selects all; the arrow
    /// keys move focus, Space toggles, Shift+arrows extend.
    | Multiple

    // Written out: a union's generated ToString uses %A, which NativeAOT can't run.
    override m.ToString() =
        match m with
        | None -> "None"
        | Single -> "Single"
        | Multiple -> "Multiple"

/// A ListBox's selection after the user changed it: the keys and items selected now, and the keys
/// that were added and removed. In Single mode, `Key` and `Item` are the one selected (or none).
[<Sealed>]
type SelectionChangedEventArgs<'T, 'K> internal (keys: 'K list, items: 'T list, added: 'K list, removed: 'K list) =
    /// The selection now (in the order selected; a range in source order).
    member _.Keys = keys
    /// The selected items, in the same order (those still in the source).
    member _.Items = items
    member _.Added = added
    member _.Removed = removed
    /// The selected key (Single mode): the first, or none.
    member _.Key: 'K voption = match keys with k :: _ -> ValueSome k | [] -> ValueNone
    member _.Item: 'T voption = match items with i :: _ -> ValueSome i | [] -> ValueNone

/// An item the user invoked (double-click, Enter; a click in a list without selection): its key and the item.
[<Sealed>]
type ItemInvokedEventArgs<'T, 'K> internal (key: 'K, item: 'T) =
    member _.Key = key
    member _.Item = item

module ListBoxProps =
    /// The selected keys. Given by the view: controlled; otherwise the control's own value.
    let selected: Prop<obj list> = Prop.appState "Selected" [] Affects.Measure
    let selectionMode = Prop.define "SelectionMode" SelectionMode.Single Affects.Measure
    /// (keys, items, added, removed), boxed; the setting unboxes them for the app's handler.
    let internal onSelectedChanged: Prop<(obj list -> obj list -> obj list -> obj list -> unit) voption> =
        Prop.define "OnSelectedChanged" ValueNone Affects.Nothing
    /// (key, item), boxed.
    let internal onItemInvoked: Prop<(obj -> obj -> unit) voption> = Prop.define "OnItemInvoked" ValueNone Affects.Nothing

module ListBoxItemProps =
    /// The bar at the left edge of a selected item.
    let indicator = Prop.define "Indicator" Color.Transparent Affects.Render
    /// The key of the item it holds (set by the ListBox).
    let internal key: Prop<obj> = Prop.define "ItemKey" null Affects.Nothing

/// What a ListBox keeps between events: where focus is among the items, the anchor for Shift
/// ranges, and the last click (for double-clicks).
[<Sealed; AllowNullLiteral>]
type internal ListBoxState() =
    member val FocusKey: obj = null with get, set
    member val Anchor: obj = null with get, set
    member val LastClickKey: obj = null with get, set
    member val LastClickTime = TimeSpan.MinValue with get, set
    member val Typeahead = TypeaheadState() with get

/// What ListBox and its item containers share: the selection, focus among the items, invoking.
module internal ListBoxLogic =
    let private state: Prop<ListBoxState> = Prop.local "ListBoxState" null

    let stateOf (lb: Element) =
        match lb.Get state with
        | null ->
            let s = ListBoxState()
            lb.SetLocal(state, s)
            s
        | s -> s

    /// Two clicks on the same item this close together are a double-click.
    let doubleClickTime = TimeSpan.FromMilliseconds 500.0

    let has (keys: obj list) (key: obj) = keys |> List.exists (fun k -> obj.Equals(k, key))
    let isSelected (lb: Element) (key: obj) = has (lb.Get ListBoxProps.selected) key

    let private guarded (lb: Element) (f: unit -> unit) =
        try
            f ()
        with e ->
            lb.ReportError e

    /// The items for `keys` (those still in the source).
    let private itemsOf (lb: Element) (keys: obj list) =
        keys |> List.choose (fun k -> match ItemsView.indexOf lb k with ValueSome i -> Some(ItemsView.itemAt lb i) | ValueNone -> None)

    /// The user changed the selection to `next`: the own value follows unless the view gives it,
    /// then the handler hears of it.
    let select (lb: Element) (next: obj list) =
        let before = lb.Get ListBoxProps.selected
        let added = next |> List.filter (fun k -> not (has before k))
        let removed = before |> List.filter (fun k -> not (has next k))
        if not added.IsEmpty || not removed.IsEmpty then
            if not (ControlValue.isControlled lb ListBoxProps.selected) then lb.Set(ListBoxProps.selected, next)
            match lb.Get ListBoxProps.onSelectedChanged with
            | ValueSome h -> guarded lb (fun () -> h next (itemsOf lb next) added removed)
            | ValueNone -> ()

    let invoke (lb: Element) (key: obj) =
        match lb.Get ListBoxProps.onItemInvoked, ItemsView.indexOf lb key with
        | ValueSome h, ValueSome i -> guarded lb (fun () -> h key (ItemsView.itemAt lb i))
        | _ -> ()

    /// The keys from index `a` to `b` (either order), in source order.
    let range (lb: Element) (a: int) (b: int) = [ for i in min a b .. max a b -> ItemsView.keyAt lb i ]

    /// Where focus goes on Tab: the item last focused, else the first selected one realized, else
    /// the first realized.
    let tabTarget (lb: Element) : obj =
        let s = stateOf lb
        let realized = ItemsView.realized lb
        let isRealized k = realized |> List.exists (fun struct (r, _) -> obj.Equals(r, k))
        if not (isNull s.FocusKey) && isRealized s.FocusKey then s.FocusKey
        else
            match lb.Get ListBoxProps.selected |> List.tryFind isRealized with
            | Some k -> k
            | None ->
                match realized |> List.sortBy (fun struct (k, _) -> ItemsView.indexOf lb k |> ValueOption.defaultValue Int32.MaxValue) with
                | struct (k, _) :: _ -> k
                | [] -> null

    /// Moves focus to the item at `i`: scrolls it into view and focuses its container (now if
    /// it's realized, else once it is).
    let focusIndex (lb: Element) (i: int) =
        let s = stateOf lb
        let key = ItemsView.keyAt lb i
        s.FocusKey <- key
        ItemsView.reveal lb i
        match ItemsView.elementFor lb key with
        | ValueSome el -> el.Focus()
        | ValueNone ->
            match lb.Root with
            | ValueSome r ->
                r.AfterFrame(fun () ->
                    match ItemsView.elementFor lb key with
                    | ValueSome el when obj.ReferenceEquals(s.FocusKey, key) -> el.Focus()
                    | _ -> ())
            | ValueNone -> ()

    /// A click on the item with `key` (from its container).
    let clicked (lb: Element) (key: obj) (args: ClickEventArgs) =
        let s = stateOf lb
        let mode = lb.Get ListBoxProps.selectionMode
        let ctrl = args.Modifiers.HasFlag Modifiers.Control || args.Modifiers.HasFlag Modifiers.Meta
        let shift = args.Modifiers.HasFlag Modifiers.Shift
        let selected = lb.Get ListBoxProps.selected
        let selectClick () =
            match mode with
            | SelectionMode.None -> ()
            | SelectionMode.Single -> select lb (if ctrl && has selected key then [] else [ key ])
            | SelectionMode.Multiple ->
                match ItemsView.indexOf lb key with
                | ValueSome i when shift ->
                    let anchor = if isNull s.Anchor then ValueNone else ItemsView.indexOf lb s.Anchor
                    let r = range lb (anchor |> ValueOption.defaultValue i) i
                    select lb (if ctrl then selected @ (r |> List.filter (fun k -> not (has selected k))) else r)
                | _ ->
                    select lb (if ctrl then (if has selected key then selected |> List.filter (fun k -> not (obj.Equals(k, key))) else selected @ [ key ]) else [ key ])
                    s.Anchor <- key
        s.FocusKey <- key
        if mode <> SelectionMode.Multiple || not shift then s.Anchor <- key
        match args.Source with
        | ClickSource.Assistive ->
            // A screen reader's default action: select (if it can) and invoke.
            if mode <> SelectionMode.None && not (has selected key) then
                select lb (if mode = SelectionMode.Single then [ key ] else selected @ [ key ])
            invoke lb key
        | _ when mode = SelectionMode.None -> invoke lb key
        | _ ->
            let now = match lb.Root with ValueSome r -> r.Time | ValueNone -> TimeSpan.Zero
            let isDouble = obj.Equals(s.LastClickKey, key) && now - s.LastClickTime <= doubleClickTime && not shift && not ctrl
            if isDouble then
                s.LastClickKey <- null
                invoke lb key
            else
                s.LastClickKey <- key
                s.LastClickTime <- now
                selectClick ()

    /// The focused item's key: the realized container holding focus, else the one last focused.
    let private focusedKey (lb: Element) =
        let rec holdsFocus (e: Element) =
            e.IsFocused || (let mutable found = false in (for c in e.Children do if not found && holdsFocus c then found <- true); found)
        match ItemsView.realized lb |> List.tryFind (fun struct (_, el) -> holdsFocus el) with
        | Some(struct (k, _)) -> k
        | None -> (stateOf lb).FocusKey

    let private timeOf (lb: Element) = match lb.Root with ValueSome r -> r.Time | ValueNone -> TimeSpan.Zero

    /// Typed text nothing inside the item used: the list goes to the next item that starts with what has been
    /// typed (DESIGN.md, "9e", decision 64). One selection follows it; with several, or none, only the focus.
    let textInput (lb: Element) (args: TextInputEventArgs) =
        let s = stateOf lb
        let n = ItemsView.count lb
        let now = timeOf lb
        // A space on its own selects (the key); it only searches while letters are building up.
        if n > 0 && (Typeahead.isActive s.Typeahead now || not (String.IsNullOrWhiteSpace args.Text)) then
            args.Handle()
            let key = focusedKey lb
            let current = (if isNull key then ValueNone else ItemsView.indexOf lb key) |> ValueOption.defaultValue -1
            match Typeahead.find s.Typeahead now args.Text n current (fun i -> ItemsView.itemText lb (ItemsView.itemAt lb i)) with
            | ValueSome t ->
                let tk = ItemsView.keyAt lb t
                if lb.Get ListBoxProps.selectionMode = SelectionMode.Single then
                    select lb [ tk ]
                    s.Anchor <- tk
                focusIndex lb t
            | ValueNone -> ()

    /// Keys nothing inside the item used: moving focus, selecting, invoking.
    let keyDown (lb: Element) (args: KeyEventArgs) =
        let s = stateOf lb
        let n = ItemsView.count lb
        let mode = lb.Get ListBoxProps.selectionMode
        let ctrl = args.Modifiers.HasFlag Modifiers.Control || args.Modifiers.HasFlag Modifiers.Meta
        let shift = args.Modifiers.HasFlag Modifiers.Shift
        let alt = args.Modifiers.HasFlag Modifiers.Alt
        let key = focusedKey lb
        let current = if isNull key then ValueNone else ItemsView.indexOf lb key
        if n > 0 && not alt then
            let at = current |> ValueOption.defaultValue -1
            let page = ItemsView.perPage lb
            let target =
                match args.Key with
                | Key.Down -> ValueSome(if at < 0 then 0 else at + 1)
                | Key.Up -> ValueSome(if at < 0 then 0 else at - 1)
                | Key.Home -> ValueSome 0
                | Key.End -> ValueSome(n - 1)
                | Key.PageDown -> ValueSome(if at < 0 then 0 else at + page)
                | Key.PageUp -> ValueSome(if at < 0 then 0 else at - page)
                | _ -> ValueNone
            match target with
            | ValueSome t ->
                args.Handle()
                let t = Math.Clamp(t, 0, n - 1)
                let tk = ItemsView.keyAt lb t
                match mode with
                | SelectionMode.Single when not ctrl ->
                    select lb [ tk ]
                    s.Anchor <- tk
                | SelectionMode.Multiple when shift ->
                    let anchor = if isNull s.Anchor then ValueNone else ItemsView.indexOf lb s.Anchor
                    let anchor = match anchor with ValueSome a -> a | ValueNone -> (if at < 0 then t else at)
                    if isNull s.Anchor then s.Anchor <- ItemsView.keyAt lb anchor
                    select lb (range lb anchor t)
                | _ -> ()
                focusIndex lb t
            | ValueNone ->
                match args.Key with
                | Key.Enter when not ctrl && not shift && current.IsSome ->
                    args.Handle()
                    if not args.IsRepeat then invoke lb key
                // Letters are building up: Space is one of them (it comes as typed text).
                | Key.Space when not ctrl && Typeahead.isActive s.Typeahead (timeOf lb) -> args.Handle()
                | Key.Space when not ctrl && current.IsSome ->
                    args.Handle()
                    if not args.IsRepeat then
                        match mode with
                        | SelectionMode.None -> invoke lb key
                        | SelectionMode.Single -> select lb [ key ]
                        | SelectionMode.Multiple ->
                            let selected = lb.Get ListBoxProps.selected
                            select lb (if has selected key then selected |> List.filter (fun k -> not (obj.Equals(k, key))) else selected @ [ key ])
                        s.Anchor <- key
                | Key.Char c when ctrl && not shift && Char.ToLowerInvariant c = 'a' && mode = SelectionMode.Multiple ->
                    args.Handle()
                    select lb (range lb 0 (n - 1))
                | _ -> ()

/// The element a ListBox wraps each item in (a Border): it shows the item's states (`selected`
/// with hover, pressed and focused) and takes its clicks. Styles reach it as
/// `ListBoxItem.style [] [ Style.onState States.isSelected [ ListBoxItem.background c ] ]`.
[<AbstractClass>]
type ListBoxItem =
    inherit Border
    /// The bar at the left edge of a selected item (the theme's accent).
    static member indicator<'k when 'k :> ListBoxItem>(value: Color) : Setting<'k> = Setting.create ListBoxItemProps.indicator value
    static member indicator<'k when 'k :> ListBoxItem>(themeValue: ThemeValue<Color>) : Setting<'k> =
        Setting.fromTheme ListBoxItemProps.indicator themeValue

module ListBoxItem =
    let private indicatorWidth = 3.0

    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
        let size = ctx.Size
        if ctx.Get TabHeaderProps.isSelected then
            let inset = Math.Round(min 6.0 (size.Height / 4.0))
            list.FillRoundedRect(
                Rect.Create(0.0, inset, indicatorWidth, max 0.0 (size.Height - 2.0 * inset)),
                CornerRadius.Uniform(indicatorWidth / 2.0), ctx.Get ListBoxItemProps.indicator)
        // Inside the edge: the list's ScrollViewer clips what's outside.
        if ctx.IsFocusVisible then
            list.StrokeRect(
                Rect.Create(1.0, 1.0, max 0.0 (size.Width - 2.0), max 0.0 (size.Height - 2.0)),
                ctx.Get BorderProps.cornerRadius, FocusRing.thickness, ctx.Get FocusRing.colorProp)

    let private click (ctx: InputContext) (args: ClickEventArgs) =
        let e = ctx.Element
        match e.Parent with
        | ValueSome lb -> ListBoxLogic.clicked lb (e.Get ListBoxItemProps.key) args
        | ValueNone -> ()

    let Type =
        Element.extend Border.Type "ListBoxItem" [
            Element.render render
            Element.click click
            Element.defaults [
                Layoutable.focusable true
                Layoutable.isTabStop false
                Border.padding (12.0, 8.0)
                ListBoxItem.indicator Palette.accent
                Semantics.role Role.ListItem
                Semantics.selectedFrom TabHeaderProps.isSelected
            ]
        ]

    let create (settings: Setting<ListBoxItem> list) : View = View.Create(Type, settings)
    /// A style for ListBoxItems: `ListBoxItem.style [] [ Style.onState States.isSelected [ ... ] ]`.
    let style (parts: SelectorPart list) (settings: Setting<ListBoxItem> list) : Style = Style.forType Type parts settings

/// A list the user selects from (an ItemsView: it realizes only what shows, so 10,000 items cost
/// about what 40 do). Put it inside a ScrollViewer, as an ItemsView.
///
///     ScrollViewer.create [
///         ScrollViewer.content (
///             ListBox.create [
///                 ListBox.source model.People
///                 ListBox.keySelector (fun (p: Person) -> p.Id)
///                 ListBox.viewSelector (fun (p: Person) -> p.Name)         // or a view
///                 ListBox.selected [ model.Current ]                        // left out: it keeps its own
///                 ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> dispatch (Pick e.Key))
///                 ListBox.onItemInvoked (fun (e: ItemInvokedEventArgs<Person, int>) -> dispatch (Open e.Key))
///             ])
///     ]
///
/// The selection is keys (`keySelector`; without one, the items themselves), so it survives
/// inserts, sorting and filtering. `selectionMode`: Single (default), Multiple or None. One Tab
/// stop; the arrow keys, Home/End and PageUp/PageDown move through the items (realizing only the
/// one reached). Selecting picks an item; invoking acts on it: a double-click or Enter, or a click
/// when nothing can be selected.
[<AbstractClass>]
type ListBox =
    inherit ItemsView
    /// The selected keys (empty: none). Given: controlled (it shows exactly these); left out: its
    /// own selection, starting empty. A key not in the source shows nothing.
    static member selected<'k, 'K when 'k :> ListBox>(keys: 'K list) : Setting<'k> =
        Setting.create ListBoxProps.selected (keys |> List.map box)
    /// Single (default), Multiple or None.
    static member selectionMode<'k when 'k :> ListBox>(mode: SelectionMode) : Setting<'k> = Setting.create ListBoxProps.selectionMode mode
    /// Called with the new selection when the user changes it (or, when it keeps its own, when a
    /// selected item leaves the source).
    static member onSelectedChanged<'k, 'T, 'K when 'k :> ListBox>(handler: SelectionChangedEventArgs<'T, 'K> -> unit) : Setting<'k> =
        let boxed (keys: obj list) (items: obj list) (added: obj list) (removed: obj list) =
            handler (
                SelectionChangedEventArgs<'T, 'K>(
                    keys |> List.map unbox<'K>, items |> List.map unbox<'T>, added |> List.map unbox<'K>, removed |> List.map unbox<'K>)
            )
        Setting.create ListBoxProps.onSelectedChanged (ValueSome boxed)
    /// Called when the user invokes an item (double-click, Enter; a click or Space when
    /// `selectionMode` is None).
    static member onItemInvoked<'k, 'T, 'K when 'k :> ListBox>(handler: ItemInvokedEventArgs<'T, 'K> -> unit) : Setting<'k> =
        Setting.create ListBoxProps.onItemInvoked (ValueSome(fun (key: obj) (item: obj) -> handler (ItemInvokedEventArgs<'T, 'K>(unbox<'K> key, unbox<'T> item))))

module ListBox =
    /// Each item's view in its container (the selection and Tab stop are set after layout).
    let private wrap (key: obj) (_: obj) (view: View) =
        ListBoxItem.create [ Setting.create ListBoxItemProps.key key; ListBoxItem.content view ]

    /// After the items are realized: each container shows whether it's selected, and one is the
    /// Tab stop. Styles for a changed state are matched now, so they draw in this frame.
    let private sync (lb: Element) =
        let selected = lb.Get ListBoxProps.selected
        let target = ListBoxLogic.tabTarget lb
        // With a text selector, an item's name for screen readers is that text (its view may have none).
        let named = (lb.Get ItemsViewProps.textSelector).IsSome
        for struct (key, el) in ItemsView.realized lb do
            if named then
                match ItemsView.indexOf lb key with
                | ValueSome i ->
                    let label = Fixed(ItemsView.itemText lb (ItemsView.itemAt lb i))
                    if el.Get SemanticsProps.label <> label then el.Set(SemanticsProps.label, label)
                | ValueNone -> ()
            let isSelected = ListBoxLogic.has selected key
            let isTarget = obj.Equals(key, target)
            let changed = el.Get TabHeaderProps.isSelected <> isSelected || el.Get LayoutProps.isTabStop <> isTarget
            if changed then
                el.Set(TabHeaderProps.isSelected, isSelected)
                el.Set(LayoutProps.isTabStop, isTarget)
                match lb.Root with
                | ValueSome(:? ElementTree as tree) -> tree.RunStylesWithin el
                | _ -> ()

    /// Keys that left the source (own selection only): dropped, and the app hears of it after the frame.
    let private dropMissing (lb: Element) =
        let selected = lb.Get ListBoxProps.selected
        if not selected.IsEmpty && not (ControlValue.isControlled lb ListBoxProps.selected) then
            let kept = selected |> List.filter (fun k -> (ItemsView.indexOf lb k).IsSome)
            if kept.Length <> selected.Length then
                match lb.Root with
                | ValueSome r -> r.AfterFrame(fun () -> ListBoxLogic.select lb kept)
                | ValueNone -> ()

    let private measure (ctx: MeasureContext) (available: Size) =
        let size = ItemsView.Type.Measure ctx available
        let lb = ctx.Element
        sync lb
        dropMissing lb
        size

    let Type =
        Element.extend ItemsView.Type "ListBox" [
            Element.measure measure
            Element.keyDown (fun ctx args -> ListBoxLogic.keyDown ctx.Element args)
            Element.textInput (fun ctx args -> ListBoxLogic.textInput ctx.Element args)
            Element.defaults [
                Setting.create ItemsViewProps.container (ValueSome wrap)
                Semantics.multiSelectableFrom(ListBoxProps.selectionMode, fun m -> m = SelectionMode.Multiple)
            ]
        ]

    let create (settings: Setting<ListBox> list) : View = View.Create(Type, settings)
    /// A style for ListBoxes (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<ListBox> list) : Style = Style.forType Type parts settings
