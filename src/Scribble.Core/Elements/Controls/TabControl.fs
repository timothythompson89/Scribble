namespace Scribble.Core

open Scribble.Accessibility
open System
open Scribble.Rendering

module TabItemProps =
    /// The header's text.
    let header = Prop.define "Header" "" Affects.Nothing
    /// Header content instead of text (an icon and text). A plain value, so the page doesn't realize it.
    let headerView: Prop<View voption> = Prop.define "HeaderView" ValueNone Affects.Nothing
    /// The page.
    let content = Prop.child "Content"
    /// Keep the page (hidden, with its state) after leaving it; false lets it go.
    let keepAlive = Prop.define "KeepAlive" true Affects.Nothing

/// One tab of a TabControl: its header and its page.
///
///     TabItem.create [ TabItem.header "Settings"; TabItem.content (settingsPage model dispatch) ]
///
/// The page is realized the first time its tab is selected and then kept, hidden, while another
/// tab is shown (its state stays); `keepAlive false` lets it go when it's left. `isEnabled false`
/// disables the header.
[<AbstractClass>]
type TabItem =
    inherit Layoutable
    static member header<'k when 'k :> TabItem>(text: string) : Setting<'k> = Setting.create TabItemProps.header text
    /// Header content instead of text (an icon and text, a badge).
    static member header<'k when 'k :> TabItem>(view: View) : Setting<'k> = Setting.create TabItemProps.headerView (ValueSome view)
    /// The page shown while this tab is selected.
    static member content<'k when 'k :> TabItem>(view: View) : Setting<'k> = Setting.create TabItemProps.content (ValueSome view)
    /// Keep the page (hidden, with its state) after leaving the tab (default); false lets it go,
    /// and it's built again on the next visit.
    static member keepAlive<'k when 'k :> TabItem>(value: bool) : Setting<'k> = Setting.create TabItemProps.keepAlive value

module TabItem =
    /// The page element: its content fills it; it draws nothing.
    let Type =
        Element.define "TabItem" [ Element.defaults [ Semantics.role Role.TabPanel; Semantics.labelFrom TabItemProps.header ] ]
    let create (settings: Setting<TabItem> list) : View = View.Create(Type, settings)
    let style (parts: SelectorPart list) (settings: Setting<TabItem> list) : Style = Style.forType Type parts settings

/// A tab's header in a TabControl's header row (a Button; the TabControl makes them). Styles can
/// change its look: `TabHeader.style [ States.isSelected ] [ TabHeader.foreground c ]`.
[<AbstractClass>]
type TabHeader =
    inherit Button

module TabHeader =
    /// Border's look; the focus ring goes inside the edge, since the header row clips.
    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
        if ctx.IsFocusVisible then
            let size = ctx.Size
            list.StrokeRect(
                Rect.Create(1.0, 1.0, max 0.0 (size.Width - 2.0), max 0.0 (size.Height - 2.0)),
                ctx.Get BorderProps.cornerRadius, FocusRing.thickness, ctx.Get FocusRing.colorProp)

    let Type =
        Element.extend Button.Type "TabHeader" [
            Element.render render
            Element.defaults [
                Border.background Color.Transparent
                Border.borderThickness 0.0
                Setting.create BorderProps.shadow ValueNone
                Border.cornerRadius { TopLeft = 6.0; TopRight = 6.0; BottomRight = 0.0; BottomLeft = 0.0 }
                Border.padding (14.0, 9.0)
                Layoutable.foreground (Color.Hex 0x9A9AB0)
                Semantics.role Role.Tab
                Semantics.selectedFrom TabHeaderProps.isSelected
            ]
        ]

    let create (settings: Setting<TabHeader> list) : View = View.Create(Type, settings)
    let style (parts: SelectorPart list) (settings: Setting<TabHeader> list) : Style = Style.forType Type parts settings

module TabControlProps =
    /// The selected tab's index. Given by the view: controlled; otherwise the control's own value.
    let selected = Prop.define "Selected" 0 Affects.Measure
    let onSelectedChanged: Prop<(int -> unit) voption> = Prop.define "OnSelectedChanged" ValueNone Affects.Nothing
    /// The tabs: TabItem views, kept as plain values (the TabControl realizes pages itself).
    let tabs: Prop<View list> = Prop.define "Tabs" [] Affects.Measure
    /// The line under the header row and the selected tab's underline.
    let line = Prop.define "Line" (Color.Hex 0x4A4A68) Affects.Render
    let underline = Prop.define "Underline" (Color.Hex 0x3D59A1) Affects.Render

/// What a TabControl keeps between layouts: its header row, its pages (realized on first visit),
/// and what they were last built from.
[<Sealed>]
type internal TabState() =
    member val Header: Element voption = ValueNone with get, set
    member val Pages: Element voption[] = [||] with get, set
    member val BuiltFrom: View list = [] with get, set
    member val BuiltSelected = -1 with get, set
    /// The selection changed since the last arrange: bring its header into view.
    member val Reveal = false with get, set

/// Tabs: a row of headers, and below it the selected tab's page.
///
///     TabControl.create [
///         TabControl.selected model.Tab                       // left out: it keeps its own
///         TabControl.onSelectedChanged (fun i -> dispatch (ShowTab i))
///         TabControl.tabs [
///             TabItem.create [ TabItem.header "General"; TabItem.content (general model) ]
///             TabItem.create [ TabItem.header "Advanced"; TabItem.content (advanced model) ]
///         ]
///     ]
///
/// Pages are realized when first shown and then kept, hidden, with their state (`TabItem.keepAlive
/// false` lets one go): tabs never visited cost nothing. ←/→ on a focused header select the
/// previous or next tab (Home/End the first or last); Tab moves into the page. Headers that don't
/// fit scroll sideways, and the selected one is brought into view.
[<AbstractClass>]
type TabControl =
    inherit Layoutable
    /// The selected tab's index. Given: controlled (it shows exactly this); left out: its own
    /// value, starting at 0.
    static member selected<'k when 'k :> TabControl>(index: int) : Setting<'k> = Setting.create TabControlProps.selected index
    /// Called with the new index when the user picks a tab (click, arrow keys).
    static member onSelectedChanged<'k when 'k :> TabControl>(handler: int -> unit) : Setting<'k> =
        Setting.create TabControlProps.onSelectedChanged (ValueSome handler)
    /// The tabs (`TabItem.create [ ... ]`).
    static member tabs<'k when 'k :> TabControl>(items: View list) : Setting<'k> = Setting.create TabControlProps.tabs items
    /// The line under the header row.
    static member line<'k when 'k :> TabControl>(value: Color) : Setting<'k> = Setting.create TabControlProps.line value
    static member line<'k when 'k :> TabControl>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme TabControlProps.line themeValue
    /// The selected tab's underline.
    static member underline<'k when 'k :> TabControl>(value: Color) : Setting<'k> = Setting.create TabControlProps.underline value
    static member underline<'k when 'k :> TabControl>(themeValue: ThemeValue<Color>) : Setting<'k> =
        Setting.fromTheme TabControlProps.underline themeValue

module TabControl =
    let private state: Prop<TabState voption> = Prop.local "TabState" ValueNone
    /// How long the underline takes to slide to a new tab.
    let private slideTime = TimeSpan.FromMilliseconds 150.0
    let private underlineThickness = 2.0

    let private stateOf (e: Element) =
        match e.Get state with
        | ValueSome s -> s
        | ValueNone ->
            let s = TabState()
            e.SetLocal(state, ValueSome s)
            s

    /// A setting's value in a view, if the view has it.
    let private settingOf (v: View) (p: Prop<'T>) : 'T voption =
        let mutable found = ValueNone
        for struct (q, value) in v.Settings do
            if obj.ReferenceEquals(q, p) then found <- ValueSome(unbox<'T> value)
        found

    let private selectedOf (e: Element) (count: int) = if count = 0 then -1 else max 0 (min (count - 1) (e.Get TabControlProps.selected))

    /// The header elements, in tab order (inside the header row's ScrollViewer and StackPanel).
    let private headers (s: TabState) : Element list =
        match s.Header with
        | ValueSome sv when sv.Children.Count > 0 -> List.ofSeq sv.Children[0].Children
        | _ -> []

    /// The user picked tab `i`.
    let private select (e: Element) (i: int) =
        let count = (e.Get TabControlProps.tabs).Length
        if i >= 0 && i < count && i <> selectedOf e count then
            ControlValue.change e TabControlProps.selected i (e.Get TabControlProps.onSelectedChanged)

    /// ←/→ (Home/End) on a focused header: select that tab and move focus to its header.
    let private headerKey (e: Element) (i: int) (args: KeyEventArgs) =
        let count = (e.Get TabControlProps.tabs).Length
        let plain = args.Modifiers &&& (Modifiers.Control ||| Modifiers.Alt ||| Modifiers.Meta ||| Modifiers.Shift) = Modifiers.None
        let target =
            match args.Key with
            | Key.Left -> ValueSome(i - 1)
            | Key.Right -> ValueSome(i + 1)
            | Key.Home -> ValueSome 0
            | Key.End -> ValueSome(count - 1)
            | _ -> ValueNone
        match target with
        | ValueSome t when plain ->
            args.Handle()
            let t = max 0 (min (count - 1) t)
            select e t
            match List.tryItem t (headers (stateOf e)) with
            | Some h -> h.Focus()
            | None -> ()
        | _ -> ()

    let private headerView (e: Element) (selected: int) (i: int) (tab: View) =
        let isSelected = i = selected
        let content =
            match settingOf tab TabItemProps.headerView |> ValueOption.flatten with
            | ValueSome v -> TabHeader.content v
            | ValueNone -> TabHeader.text (settingOf tab TabItemProps.header |> ValueOption.defaultValue "")
        TabHeader.create [
            content
            Setting.create TabHeaderProps.isSelected isSelected
            // Only the selected tab is a Tab stop; the arrow keys move between the others.
            TabHeader.isTabStop isSelected
            TabHeader.isEnabled (settingOf tab LayoutProps.isEnabled |> ValueOption.defaultValue true)
            TabHeader.onClick (fun _ -> select e i)
            TabHeader.onKeyDown (headerKey e i)
            Semantics.position (i + 1, (e.Get TabControlProps.tabs).Length)
        ]

    /// Attaches an element this TabControl realized and matches its styles now (this runs during
    /// layout, after the tree's restyle pass), as ItemsView does.
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

    /// Brings the header row and the pages in line with the tabs and the selection.
    let private sync (e: Element) =
        let s = stateOf e
        let tabs = e.Get TabControlProps.tabs
        let count = tabs.Length
        let selected = selectedOf e count
        if not (obj.ReferenceEquals(tabs, s.BuiltFrom)) || selected <> s.BuiltSelected then
            if s.BuiltSelected <> -1 && selected <> s.BuiltSelected then s.Reveal <- true
            s.BuiltFrom <- tabs
            s.BuiltSelected <- selected
            let row =
                ScrollViewer.create [
                    Semantics.role Role.TabList
                    ScrollViewer.horizontal ScrollBarMode.Hidden
                    ScrollViewer.vertical ScrollBarMode.Disabled
                    ScrollViewer.content (
                        StackPanel.create [
                            StackPanel.orientation Orientation.Horizontal
                            StackPanel.children (tabs |> List.mapi (headerView e selected))
                        ]
                    )
                ]
            match s.Header with
            | ValueSome h -> h.Patch row
            | ValueNone ->
                let h = Element.RealizingIn(e.Root, fun () -> Element.Realize row)
                adopt e h
                s.Header <- ValueSome h
            // Pages: realized on the first visit, kept (hidden) unless keepAlive is false.
            let pages = Array.init count (fun i -> if i < s.Pages.Length then s.Pages[i] else ValueNone)
            for i in count .. s.Pages.Length - 1 do
                s.Pages[i] |> ValueOption.iter drop
            for i, tab in List.indexed tabs do
                let shown = i = selected
                let keep = shown || (pages[i].IsSome && settingOf tab TabItemProps.keepAlive |> ValueOption.defaultValue true)
                match pages[i] with
                | ValueSome p when keep && obj.ReferenceEquals(p.Type, tab.Type) ->
                    p.Patch tab
                    p.Set(LayoutProps.isVisible, shown)
                | existing ->
                    existing |> ValueOption.iter drop
                    pages[i] <-
                        if keep then
                            let p = Element.RealizingIn(e.Root, fun () -> Element.Realize tab)
                            p.Set(LayoutProps.isVisible, shown)
                            adopt e p
                            ValueSome p
                        else ValueNone
            s.Pages <- pages
            e.SetManagedChildren [| yield! s.Header |> ValueOption.toArray; for p in pages do yield! p |> ValueOption.toArray |]

    let private headerHeight (s: TabState) = match s.Header with ValueSome h -> h.DesiredSize.Height | ValueNone -> 0.0

    let private measure (ctx: MeasureContext) (available: Size) =
        let e = ctx.Element
        sync e
        let s = stateOf e
        let mutable w, h = 0.0, 0.0
        match s.Header with
        | ValueSome header ->
            let d = ctx.Measure(header, Size.Create(available.Width, Double.PositiveInfinity))
            w <- d.Width
            h <- d.Height
        | ValueNone -> ()
        let below = Size.Create(available.Width, max 0.0 (available.Height - h))
        let mutable page = 0.0
        for p in s.Pages do
            match p with
            | ValueSome p when p.IsVisible ->
                let d = ctx.Measure(p, below)
                w <- max w d.Width
                page <- max page d.Height
            | _ -> ()
        Size.Create(w, h + page)

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let e = ctx.Element
        let s = stateOf e
        let top = headerHeight s
        match s.Header with
        | ValueSome header -> ctx.Arrange(header, Rect.Create(0.0, 0.0, size.Width, top))
        | ValueNone -> ()
        for p in s.Pages do
            match p with
            | ValueSome p when p.IsVisible -> ctx.Arrange(p, Rect.Create(0.0, top, size.Width, max 0.0 (size.Height - top)))
            | _ -> ()
        // After the frame, outside layout: a scroller asked to move during an arrange would have
        // its request swallowed when its ancestors finish arranging.
        if s.Reveal then
            s.Reveal <- false
            match List.tryItem s.BuiltSelected (headers s), e.Root with
            | Some h, ValueSome r -> r.AfterFrame(fun () -> if h.Root.IsSome then h.BringIntoView())
            | _ -> ()

    /// The line under the header row.
    let private render (ctx: RenderContext) (list: DrawList) =
        let s = stateOf ctx.Element
        let top = headerHeight s
        if top > 0.0 then list.FillRect(Rect.Create(0.0, top - 1.0, ctx.Size.Width, 1.0), ctx.Get TabControlProps.line)

    /// The selected tab's underline, sliding to a new tab; clipped to the header row.
    let private renderAfter (ctx: RenderContext) (list: DrawList) =
        let e = ctx.Element
        let s = stateOf e
        let top = headerHeight s
        match List.tryItem s.BuiltSelected (headers s) with
        | Some h when top > 0.0 ->
            let origin = e.PositionInTree
            let at = h.PositionInTree
            let x = ctx.Animate("underline-x", at.X - origin.X, slideTime)
            let w = ctx.Animate("underline-w", h.Bounds.Width, slideTime)
            list.PushClip(Rect.Create(0.0, 0.0, ctx.Size.Width, top))
            list.FillRect(Rect.Create(x, top - underlineThickness, w, underlineThickness), ctx.Get TabControlProps.underline)
            list.PopClip()
        | _ -> ()

    let Type =
        Element.define "TabControl" [
            Element.measure measure
            Element.arrange arrange
            Element.render render
            Element.renderAfter renderAfter
        ]

    let create (settings: Setting<TabControl> list) : View = View.Create(Type, settings)
    /// A style for TabControls (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<TabControl> list) : Style = Style.forType Type parts settings
