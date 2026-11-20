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

/// What a ListBox keeps between layouts: the ScrollViewer it holds (with its list inside) and the scroller it
/// gives that list when the view gives none.
[<Sealed; AllowNullLiteral>]
type internal ListBoxParts() =
    member val Scroll: Element voption = ValueNone with get, set
    member val Own = Scroller() with get

module ListBoxProps =
    /// A ListBox's parts (none on any other element: how the list inside tells its ListBox).
    let internal parts: Prop<ListBoxParts> = Prop.local "ListBoxParts" null
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

/// What a ListBox's list and its item containers share: the selection, focus among the items, invoking. `lb` is
/// the list inside (a `ListBoxItems`): the items are its, the selection and the handlers its ListBox's.
module internal ListBoxLogic =
    let private state: Prop<ListBoxState> = Prop.local "ListBoxState" null

    /// The ListBox a list is inside (list, ScrollViewer, ListBox): the app's settings are on it. A list on its
    /// own is its own owner.
    let owner (lb: Element) : Element =
        match lb.Parent with
        | ValueSome scroll ->
            match scroll.Parent with
            | ValueSome o when not (isNull (o.Get ListBoxProps.parts)) -> o
            | _ -> lb
        | ValueNone -> lb

    let selectedOf (lb: Element) = (owner lb).Get ListBoxProps.selected
    let modeOf (lb: Element) = (owner lb).Get ListBoxProps.selectionMode

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
    let isSelected (lb: Element) (key: obj) = has (selectedOf lb) key

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
        let o = owner lb
        let before = o.Get ListBoxProps.selected
        let added = next |> List.filter (fun k -> not (has before k))
        let removed = before |> List.filter (fun k -> not (has next k))
        if not added.IsEmpty || not removed.IsEmpty then
            if not (ControlValue.isControlled o ListBoxProps.selected) then o.Set(ListBoxProps.selected, next)
            match o.Get ListBoxProps.onSelectedChanged with
            | ValueSome h -> guarded lb (fun () -> h next (itemsOf lb next) added removed)
            | ValueNone -> ()

    let invoke (lb: Element) (key: obj) =
        match (owner lb).Get ListBoxProps.onItemInvoked, ItemsView.indexOf lb key with
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
            match selectedOf lb |> List.tryFind isRealized with
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
        let mode = modeOf lb
        let ctrl = args.Modifiers.HasFlag Modifiers.Control || args.Modifiers.HasFlag Modifiers.Meta
        let shift = args.Modifiers.HasFlag Modifiers.Shift
        let selected = selectedOf lb
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
                if modeOf lb = SelectionMode.Single then
                    select lb [ tk ]
                    s.Anchor <- tk
                focusIndex lb t
            | ValueNone -> ()

    /// Keys nothing inside the item used: moving focus, selecting, invoking.
    let keyDown (lb: Element) (args: KeyEventArgs) =
        let s = stateOf lb
        let n = ItemsView.count lb
        let mode = modeOf lb
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
                            let selected = selectedOf lb
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

/// A list the user selects from. It scrolls on its own and realizes only what shows, so 10,000 items cost about
/// what 40 do (DESIGN.md, "ListBox scrolls on its own"):
///
///     ListBox.create [
///         ListBox.source model.People
///         ListBox.keySelector (fun (p: Person) -> p.Id)
///         ListBox.viewSelector (fun (p: Person) -> p.Name)         // or a view
///         ListBox.selected [ model.Current ]                        // left out: it keeps its own
///         ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> dispatch (Pick e.Key))
///         ListBox.onItemInvoked (fun (e: ItemInvokedEventArgs<Person, int>) -> dispatch (Open e.Key))
///     ]
///
/// It takes the height it is given and scrolls inside it; where the height is unlimited (a StackPanel), give it
/// `ListBox.maxHeight`, or it is as tall as all its items. Inside it are a ScrollViewer (which
/// `ScrollViewer.style [ Style.inside ListBox.Type [] ] [ ... ]` reaches) and its list.
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

    // A frame of its own, as a Border's (DESIGN.md, "ListBox scrolls on its own", decision 8): none unless asked for.
    /// What is behind the items (default: nothing).
    static member background<'k when 'k :> ListBox>(value: Color) : Setting<'k> = Setting.create BorderProps.background value
    static member background<'k when 'k :> ListBox>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.background themeValue
    /// The colour of the line round the list; it shows with a `borderThickness`.
    static member borderBrush<'k when 'k :> ListBox>(value: Color) : Setting<'k> = Setting.create BorderProps.borderBrush value
    static member borderBrush<'k when 'k :> ListBox>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.borderBrush themeValue
    /// The line's width in DIPs (default 0: no line).
    static member borderThickness<'k when 'k :> ListBox>(value: float) : Setting<'k> = Setting.create BorderProps.borderThickness value
    /// Rounds the frame's corners. The items inside are not cut to them: the default `padding` of 4 keeps an
    /// item's highlight clear of a small radius; a larger one needs more.
    static member cornerRadius<'k when 'k :> ListBox>(value: CornerRadius) : Setting<'k> = Setting.create BorderProps.cornerRadius value
    static member cornerRadius<'k when 'k :> ListBox>(uniform: float) : Setting<'k> = Setting.create BorderProps.cornerRadius (CornerRadius.Uniform uniform)
    static member cornerRadius<'k when 'k :> ListBox>(themeValue: ThemeValue<float>) : Setting<'k> =
        Setting.fromThemeWith BorderProps.cornerRadius themeValue ThemeConvert.uniformCorners
    /// Space between the ListBox's edge and the scrolling items (the scrollbar is inside it too). Default 4, with
    /// or without a frame; `ListBox.padding 0.0` for a list that goes edge to edge.
    static member padding<'k when 'k :> ListBox>(value: Thickness) : Setting<'k> = Setting.create BorderProps.padding value
    static member padding<'k when 'k :> ListBox>(uniform: float) : Setting<'k> = Setting.create BorderProps.padding (Thickness.Uniform uniform)
    static member padding<'k when 'k :> ListBox>(horizontal: float, vertical: float) : Setting<'k> =
        Setting.create BorderProps.padding (Thickness.Symmetric(horizontal, vertical))

/// The list inside a ListBox (an ItemsView whose items are in `ListBoxItem`s): it shows its ListBox's selection
/// and takes the keys. Internal: an app writes `ListBox`, whose settings reach it.
module internal ListBoxItems =
    /// Each item's view in its container (the selection and Tab stop are set after layout).
    let private wrap (key: obj) (_: obj) (view: View) =
        ListBoxItem.create [ Setting.create ListBoxItemProps.key key; ListBoxItem.content view ]

    /// After the items are realized: each container shows whether it's selected, and one is the
    /// Tab stop. Styles for a changed state are matched now, so they draw in this frame.
    let private sync (lb: Element) =
        let selected = ListBoxLogic.selectedOf lb
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
        let selected = ListBoxLogic.selectedOf lb
        if not selected.IsEmpty && not (ControlValue.isControlled (ListBoxLogic.owner lb) ListBoxProps.selected) then
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
        Element.extend ItemsView.Type "ListBoxItems" [
            Element.measure measure
            Element.keyDown (fun ctx args -> ListBoxLogic.keyDown ctx.Element args)
            Element.textInput (fun ctx args -> ListBoxLogic.textInput ctx.Element args)
            Element.defaults [
                Setting.create ItemsViewProps.container (ValueSome wrap)
                // Its ListBox's mode, which the ListBox passes on.
                Semantics.multiSelectableFrom(ListBoxProps.selectionMode, fun m -> m = SelectionMode.Multiple)
            ]
        ]

module ListBox =
    let private partsOf (e: Element) =
        match e.Get ListBoxProps.parts with
        | null ->
            let p = ListBoxParts()
            e.SetLocal(ListBoxProps.parts, p)
            p
        | p -> p

    /// The list inside a ListBox (a `ListBoxItems`), once it has been laid out: what holds the items, takes the
    /// keys and is the list to a screen reader.
    let internal listOf (e: Element) : Element voption =
        match e.Get ListBoxProps.parts with
        | null -> ValueNone
        | p ->
            match p.Scroll with
            | ValueSome scroll -> (match Seq.tryHead scroll.Children with Some list -> ValueSome list | None -> ValueNone)
            | ValueNone -> ValueNone

    let private scrollerOf (e: Element) (p: ListBoxParts) =
        match e.Get ItemsViewProps.scroller with
        | ValueSome s -> s
        | ValueNone -> p.Own

    /// The ScrollViewer and the list inside it, from the ListBox's settings (DESIGN.md, "ListBox scrolls on its
    /// own", decision 7): what is about the items, the selection and the list's name goes to the list; sizes,
    /// styles and handlers stay on the ListBox.
    let private partsView (e: Element) (p: ListBoxParts) =
        let list: Setting<ItemsView> list =
            [ Setting.create ItemsViewProps.source (e.Get ItemsViewProps.source)
              Setting.create ItemsViewProps.keySelector (e.Get ItemsViewProps.keySelector)
              Setting.create ItemsViewProps.viewSelector (e.Get ItemsViewProps.viewSelector)
              Setting.create ItemsViewProps.textSelector (e.Get ItemsViewProps.textSelector)
              Setting.create ItemsViewProps.layout (e.Get ItemsViewProps.layout)
              Setting.create ItemsViewProps.scroller (ValueSome(scrollerOf e p))
              // The selection is read from the ListBox (ListBoxLogic.owner); given here too, a change lays the
              // list out again, and a screen reader hears the mode.
              Setting.create ListBoxProps.selected (e.Get ListBoxProps.selected)
              Setting.create ListBoxProps.selectionMode (e.Get ListBoxProps.selectionMode)
              Setting.create SemanticsProps.label (e.Get SemanticsProps.label) ]
        ScrollViewer.create [ ScrollViewer.content (View.Create(ListBoxItems.Type, list)) ]

    /// Brings the parts in line with the settings: made the first time, patched after.
    let private sync (e: Element) =
        let p = partsOf e
        let view = partsView e p
        match p.Scroll with
        | ValueSome scroll -> scroll.Patch view
        | ValueNone ->
            let scroll = Element.RealizingIn(e.Root, fun () -> Element.Realize view)
            scroll.SetParent(ValueSome e)
            scroll.SetRoot e.Root
            scroll.InvalidateAll()
            // Made during layout, after this layout's restyle pass: its styles are matched now.
            match e.Root with
            | ValueSome(:? ElementTree as tree) -> tree.RunStylesWithin scroll
            | _ -> ()
            p.Scroll <- ValueSome scroll
            e.SetManagedChildren [| scroll |]

#if DEBUG
    let rec private insideScrollViewer (e: Element) =
        match e.Parent with
        | ValueSome parent -> obj.ReferenceEquals(parent.Type, ScrollViewer.Type) || insideScrollViewer parent
        | ValueNone -> false

    /// More than this many items with no height to scroll in is worth a word (decision 4).
    let private manyItems = 200
#endif

    let private measure (ctx: MeasureContext) (available: Size) =
        let e = ctx.Element
        sync e
#if DEBUG
        // Unlimited height and no scroller around it either: every item is realized and nothing scrolls.
        if Double.IsInfinity available.Height && (e.Get ItemsViewProps.source).Count > manyItems && not (insideScrollViewer e) then
            DebugWarnings.Once(
                "listbox-unlimited-height:" + e.Name,
                "A ListBox" + (if e.Name = "" then "" else " (\"" + e.Name + "\")") + " with " + string (e.Get ItemsViewProps.source).Count
                + " items was given unlimited height (in a StackPanel, say), so it is as tall as all of them: every item is realized and it doesn't scroll. Give it ListBox.maxHeight or ListBox.height, or put it where the height is limited.")
#endif
        // As a Border round its one child: the frame's line and padding, then the ScrollViewer.
        let ix, iy = Border.inset ctx
        let mutable w, h = ix, iy
        for child in ctx.Children do
            let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - ix), max 0.0 (available.Height - iy)))
            w <- max w (d.Width + ix)
            h <- max h (d.Height + iy)
        Size.Create(w, h)

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let e = ctx.Element
        let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
        let slot = Rect.Create(p.Left + t, p.Top + t, max 0.0 (size.Width - p.Horizontal - 2.0 * t), max 0.0 (size.Height - p.Vertical - 2.0 * t))
        for child in ctx.Children do
            ctx.Arrange(child, slot)
        // By its name, the scroller is the list's (it scrolls to items).
        match e.Root with
        | ValueSome r when e.Name <> "" -> r.AppValues.RegisterScroller(e.Name, scrollerOf e (partsOf e))
        | _ -> ()

    let Type =
        Element.define "ListBox" [
            Element.measure measure
            Element.arrange arrange
            // Its frame, when it has one (the properties are a Border's; all neutral unless set).
            Element.render Border.draw
            // The gap a flyout keeps round a ComboBox's open list: item highlights stop short of the edge, and
            // clear a rounded corner when it is given a frame.
            Element.defaults [ ListBox.padding 4.0 ]
        ]

    let create (settings: Setting<ListBox> list) : View = View.Create(Type, settings)
    /// A style for ListBoxes (this type exactly). Its items are `ListBoxItem.style`; its scrollbar is
    /// `ScrollViewer.style [ Style.inside ListBox.Type [] ] [ ... ]`.
    let style (parts: SelectorPart list) (settings: Setting<ListBox> list) : Style = Style.forType Type parts settings
