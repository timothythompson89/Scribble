namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// Which side of the window the inspector's panel sits on.
[<RequireQualifiedAccess>]
type DockSide =
    | Left
    | Right

type internal InspectorOptions =
    { Side: DockSide
      ToggleKey: Key * Modifiers
      PickKey: Key * Modifiers
      IsOpen: bool
      /// Where the pop-out window comes from (none: no pop-out).
      Platform: IPlatform voption
      AppTitle: string voption }

    static member Default =
        { Side = DockSide.Right
          ToggleKey = (Key.F 12, Modifiers.None)
          PickKey = (Key.Char 'c', Modifiers.Control ||| Modifiers.Shift)
          IsOpen = false
          Platform = ValueNone
          AppTitle = ValueNone }

/// Where the panel is.
[<RequireQualifiedAccess>]
type internal PanelMode =
    | Docked
    | Floating
    | PoppedOut

/// What a drag on the panel does.
[<RequireQualifiedAccess>]
type internal DragKind =
    | Move
    | ResizeCorner
    | ResizeEdge

[<Struct>]
type internal Drag = { Kind: DragKind; Start: Point; StartRect: Rect }

module internal PanelSize =
    let dockedWidth = 380.0
    let minWidth = 300.0
    let minHeight = 160.0
    /// Around a docked panel and between a floating one and the window's edges when clamped.
    let gap = 8.0

/// One inspector setting: `Inspector.side DockSide.Left`.
[<Struct>]
type InspectorSetting = internal InspectorSetting of (InspectorOptions -> InspectorOptions)

/// One row of the tree: a layer's heading or an element at a depth.
[<RequireQualifiedAccess>]
type internal Row =
    | Layer of Layer
    | Element of Element * depth: int

/// Text for keys and values, by our own code (never `string` on unions: it isn't AOT-safe).
module internal Format =
    let key (key: Key, mods: Modifiers) =
        let name =
            match key with
            | Key.F n -> "F" + n.ToString()
            | Key.Char c -> Char.ToUpperInvariant(c).ToString()
            | Key.Escape -> "Esc"
            | Key.Enter -> "Enter"
            | Key.Space -> "Space"
            | Key.Tab -> "Tab"
            | Key.Other n -> n
            | _ -> "key"
        let has (m: Modifiers) = mods &&& m = m
        (if has Modifiers.Control then "Ctrl+" else "")
        + (if has Modifiers.Alt then "Alt+" else "")
        + (if has Modifiers.Shift then "Shift+" else "")
        + (if has Modifiers.Meta then "Meta+" else "")
        + name

    let clip (max: int) (text: string) =
        let oneLine = text.Replace('\n', ' ').Replace('\r', ' ')
        if oneLine.Length <= max then oneLine else oneLine.Substring(0, max - 1) + "…"

    /// The type's name, or the component's for a component's host element.
    let label (e: Element) =
        match e.ComponentName with
        | ValueSome n -> n
        | ValueNone -> e.Type.Name

    /// A hint after the name: a TextBlock's text, or the text of a TextBlock directly inside.
    let hint (e: Element) =
        let textOf (t: Element) = "\"" + clip 28 (t.Get TextProps.text) + "\""
        if e.Type.Name = "TextBlock" then textOf e
        else
            match e.Children |> Seq.tryFind (fun c -> c.Type.Name = "TextBlock") with
            | Some t when e.Children.Count = 1 -> textOf t
            | _ -> ""

    let size (width: float) (height: float) = $"{width:F0} × {height:F0}"

/// What the inspector shows and does. Both of its layers draw from this; `Changed` tells them to
/// update. UI thread only.
[<Sealed>]
type internal InspectorState(host: ElementHost, options: InspectorOptions) =
    let changed = Event<unit>()
    let expanded = HashSet<Element>(HashIdentity.Reference)
    let seenRoots = HashSet<Element>(HashIdentity.Reference)
    let collapsedLayers = HashSet<Layer>(HashIdentity.Reference)
    let clock = Stopwatch.StartNew()
    let mutable isOpen = options.IsOpen
    let mutable side = options.Side
    let mutable picking = false
    let mutable hovered: Element voption = ValueNone
    let mutable selected: Element voption = ValueNone
    let mutable own: Layer list = []
    let mutable dirty = false
    let mutable lastRefresh = -1000.0
    let mutable refreshScheduled = false
    let mutable mode = PanelMode.Docked
    /// The in-window mode to go back to when the pop-out window closes.
    let mutable modeBeforePopOut = PanelMode.Docked
    let mutable dockedWidth = PanelSize.dockedWidth
    let mutable floating = Rect.Create(0.0, 0.0, PanelSize.dockedWidth, 400.0)
    let mutable collapsed = false
    let mutable drag: Drag voption = ValueNone
    let mutable lastTitlePress = -1000.0
    let mutable lastTitlePoint = Point(0.0, 0.0)
    let popOut = Event<unit>()
    let popIn = Event<unit>()

    /// The app window's size in DIPs.
    let windowSize () =
        let s = host.Window.ClientSize
        let scale = host.Window.Scale
        Size.Create(float s.Width / scale, float s.Height / scale)

    let clamp (lo: float) (hi: float) (v: float) = max lo (min (max lo hi) v)

    /// A floating rectangle kept inside the window (at least its size allows).
    let keepInside (r: Rect) =
        let s = windowSize ()
        let w = clamp PanelSize.minWidth s.Width r.Width
        let h = clamp PanelSize.minHeight s.Height r.Height
        Rect.Create(clamp 0.0 (s.Width - w) r.X, clamp 0.0 (s.Height - (if collapsed then 40.0 else h)) r.Y, w, h)

    let dockedRect () =
        let s = windowSize ()
        let w = clamp PanelSize.minWidth (s.Width - 2.0 * PanelSize.gap) dockedWidth
        let x = if side = DockSide.Right then s.Width - PanelSize.gap - w else PanelSize.gap
        Rect.Create(x, PanelSize.gap, w, max 0.0 (s.Height - 2.0 * PanelSize.gap))

    let isOwn (layer: Layer) = own |> List.exists (fun l -> obj.ReferenceEquals(l, layer))
    let alive (e: Element voption) =
        match e with
        | ValueSome x when x.Root.IsSome -> e
        | _ -> ValueNone

    let refresh () =
        dirty <- false
        lastRefresh <- clock.Elapsed.TotalMilliseconds
        hovered <- alive hovered
        selected <- alive selected
        changed.Trigger()

    /// The topmost element under a point in any layer but the inspector's own.
    let elementAt (p: Point) =
        let mutable found = ValueNone
        let mutable i = host.Layers.Count - 1
        while found.IsNone && i >= 0 do
            let l = host.Layers[i]
            if not (isOwn l) then found <- l.Tree.HitTest(p.X, p.Y)
            i <- i - 1
        found

    member _.Changed = changed.Publish
    member _.IsOpen = isOpen
    member _.IsPicking = picking
    member _.Hovered = hovered
    member _.Selected = selected
    member _.Options = options

    member _.Own
        with get () = own
        and set v = own <- v

    /// What the highlight shows: the hovered element (a row or, in pick mode, the app), else the selection.
    member _.Target =
        if not isOpen then ValueNone
        else
            match alive hovered with
            | ValueSome e -> ValueSome e
            | ValueNone -> alive selected

    member _.IsExpanded(e: Element) = expanded.Contains e
    member _.IsCollapsed(l: Layer) = collapsedLayers.Contains l

    /// The rows to show: every layer but the inspector's, and the elements under expanded ones.
    member _.Rows() : Row list =
        let rows = List<Row>()
        let rec add (e: Element) depth =
            rows.Add(Row.Element(e, depth))
            if expanded.Contains e then
                for c in e.Children do
                    add c (depth + 1)
        for layer in host.Layers do
            if not (isOwn layer) then
                rows.Add(Row.Layer layer)
                match layer.Tree.Root with
                | ValueSome root ->
                    // Roots start expanded; after that the user decides.
                    if seenRoots.Add root then expanded.Add root |> ignore
                    if not (collapsedLayers.Contains layer) then add root 1
                | ValueNone -> ()
        List.ofSeq rows

    member this.Toggle() =
        // Hiding a popped-out inspector closes its window (the panel goes back in for next time).
        if isOpen && mode = PanelMode.PoppedOut then this.PopIn()
        isOpen <- not isOpen
        picking <- picking && isOpen
        hovered <- ValueNone
        refresh ()

    member _.TogglePick() =
        picking <- not picking
        if picking then isOpen <- true
        hovered <- ValueNone
        refresh ()

    member _.StopPicking() =
        picking <- false
        hovered <- ValueNone
        changed.Trigger()

    // --- Where the panel is ------------------------------------------------------------------

    member _.Mode = mode
    member _.Side = side
    member _.IsPanelCollapsed = collapsed
    member _.DockedRect = dockedRect ()
    member _.FloatingRect = keepInside floating

    /// True when there's a platform that can open the pop-out window.
    member _.CanPopOut =
        match options.Platform with
        | ValueSome p -> p.SupportsMultipleWindows
        | ValueNone -> false

    /// The pop-out window should open (the inspector's attach code listens) or close.
    member _.PopOutRequested = popOut.Publish
    member _.PopInRequested = popIn.Publish

    member _.Dock(value: DockSide) =
        side <- value
        mode <- PanelMode.Docked
        changed.Trigger()

    member this.ToggleCollapsed() =
        collapsed <- not collapsed
        changed.Trigger()

    member this.PopOut() =
        if this.CanPopOut && mode <> PanelMode.PoppedOut then
            modeBeforePopOut <- mode
            mode <- PanelMode.PoppedOut
            isOpen <- true
            popOut.Trigger()
            changed.Trigger()

    /// Back into the app's window, where it was before.
    member _.PopIn() =
        if mode = PanelMode.PoppedOut then
            mode <- modeBeforePopOut
            popIn.Trigger()
            changed.Trigger()

    /// A press on the title area (move), the corner grip or a docked panel's inner edge. Two
    /// quick presses on the title area collapse or expand the panel instead.
    member this.BeginDrag(kind: DragKind, p: Point) =
        let now = clock.Elapsed.TotalMilliseconds
        let isDouble =
            kind = DragKind.Move && now - lastTitlePress < 400.0
            && abs (p.X - lastTitlePoint.X) < 4.0 && abs (p.Y - lastTitlePoint.Y) < 4.0
        if kind = DragKind.Move then
            lastTitlePress <- (if isDouble then -1000.0 else now)
            lastTitlePoint <- p
        if isDouble then
            drag <- ValueNone
            this.ToggleCollapsed()
        else
            let start =
                match kind, mode with
                | DragKind.Move, PanelMode.Docked ->
                    // Undocked, the panel keeps its width and gets a height that leaves room to move.
                    let d = dockedRect ()
                    Rect.Create(d.X, d.Y, d.Width, max PanelSize.minHeight (min d.Height ((windowSize ()).Height * 0.6)))
                | DragKind.ResizeEdge, _ -> dockedRect ()
                | _ -> keepInside floating
            drag <- ValueSome { Kind = kind; Start = p; StartRect = start }

    member _.DragTo(p: Point) =
        match drag with
        | ValueSome d ->
            let dx, dy = p.X - d.Start.X, p.Y - d.Start.Y
            let r = d.StartRect
            match d.Kind with
            | DragKind.Move ->
                // A docked panel comes off once the pointer has really moved (not on a click).
                if mode = PanelMode.Docked && abs dx + abs dy > 4.0 then mode <- PanelMode.Floating
                if mode = PanelMode.Floating then
                    floating <- keepInside (Rect.Create(r.X + dx, r.Y + dy, r.Width, r.Height))
                    changed.Trigger()
            | DragKind.ResizeCorner ->
                floating <- keepInside (Rect.Create(r.X, r.Y, r.Width + dx, r.Height + dy))
                changed.Trigger()
            | DragKind.ResizeEdge ->
                let s = windowSize ()
                let w = r.Width + (if side = DockSide.Right then -dx else dx)
                dockedWidth <- clamp PanelSize.minWidth (s.Width - 2.0 * PanelSize.gap) w
                changed.Trigger()
        | ValueNone -> ()

    member _.EndDrag() = drag <- ValueNone

    member _.ToggleExpanded(e: Element) =
        if not (expanded.Remove e) then expanded.Add e |> ignore
        changed.Trigger()

    member _.ToggleLayer(l: Layer) =
        if not (collapsedLayers.Remove l) then collapsedLayers.Add l |> ignore
        changed.Trigger()

    /// Selects an element and expands the rows down to it.
    member _.Select(e: Element) =
        selected <- ValueSome e
        let mutable p = e.Parent
        let mutable top = e
        while p.IsSome do
            expanded.Add p.Value |> ignore
            top <- p.Value
            p <- p.Value.Parent
        for l in host.Layers do
            match l.Tree.Root with
            | ValueSome r when obj.ReferenceEquals(r, top) -> collapsedLayers.Remove l |> ignore
            | _ -> ()
        changed.Trigger()

    member _.HoverRow(e: Element) =
        if not picking then
            hovered <- ValueSome e
            changed.Trigger()

    member _.LeaveRow(e: Element) =
        match hovered with
        | ValueSome h when not picking && obj.ReferenceEquals(h, e) ->
            hovered <- ValueNone
            changed.Trigger()
        | _ -> ()

    /// Pick mode: the pointer moved to `p` (window DIPs).
    member _.PickAt(p: Point) =
        let e = elementAt p
        let same =
            match e, hovered with
            | ValueSome a, ValueSome b -> obj.ReferenceEquals(a, b)
            | ValueNone, ValueNone -> true
            | _ -> false
        if not same then
            hovered <- e
            changed.Trigger()

    /// Pick mode: a press at `p` selects what's there and ends pick mode.
    member this.PickSelect(p: Point) =
        picking <- false
        hovered <- ValueNone
        match elementAt p with
        | ValueSome e -> this.Select e
        | ValueNone -> changed.Trigger()

    /// Shortcuts, seen before any layer gets the key.
    member this.OnKey (key: Key) (mods: Modifiers) =
        if (key, mods) = options.ToggleKey then
            this.Toggle()
            true
        elif (key, mods) = options.PickKey then
            this.TogglePick()
            true
        elif picking && key = Key.Escape then
            this.StopPicking()
            true
        else false

    /// A layer changed: if it's one of the app's, the tree may need refreshing.
    member _.LayerChanged(layer: Layer) = if isOpen && not (isOwn layer) then dirty <- true

    /// After each frame: refresh the tree if the app changed, at most about 10 times a second.
    member _.AfterFrame() =
        if dirty && isOpen && not refreshScheduled then
            let wait = 100.0 - (clock.Elapsed.TotalMilliseconds - lastRefresh)
            match host.Dispatcher with
            | ValueSome d when wait > 0.0 ->
                refreshScheduled <- true
                Task
                    .Delay(TimeSpan.FromMilliseconds wait)
                    .ContinueWith(fun (_: Task) ->
                        d.Post(fun () ->
                            refreshScheduled <- false
                            if dirty && isOpen then refresh ()))
                |> ignore
            | _ -> refresh ()

// --- The highlight -----------------------------------------------------------------------------------

module internal HighlightProps =
    let target = Prop.define "Target" (ValueNone: Element voption) Affects.Render

/// Draws the devtools-style highlight over an element (bounds, margin, a label with the size).
/// Reads the target's position while drawing, so it's exact in every frame.
[<AbstractClass>]
type internal Highlight =
    inherit Layoutable
    static member target<'k when 'k :> Highlight>(value: Element voption) : Setting<'k> = Setting.create HighlightProps.target value

module internal Highlight =
    let private boxColor = Color.Hex 0x6FA8DC
    let private marginColor = (Color.Hex 0xF6B26B).WithAlpha 0.35f

    let private render (ctx: RenderContext) (list: DrawList) =
        match ctx.Get HighlightProps.target with
        | ValueSome t when t.Root.IsSome && t.Bounds.Width >= 0.0 && t.Bounds.Height >= 0.0 ->
            let p = t.PositionInTree
            let b = t.Bounds
            let r = Rect.Create(p.X, p.Y, b.Width, b.Height)
            let m = t.Get LayoutProps.margin
            let outerWidth = r.Width + m.Left + m.Right
            if m.Top > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y - m.Top, outerWidth, m.Top), marginColor)
            if m.Bottom > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y + r.Height, outerWidth, m.Bottom), marginColor)
            if m.Left > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y, m.Left, r.Height), marginColor)
            if m.Right > 0.0 then list.FillRect(Rect.Create(r.X + r.Width, r.Y, m.Right, r.Height), marginColor)
            list.FillRect(r, boxColor.WithAlpha 0.3f)
            list.StrokeRect(r, CornerRadius.Zero, 1.0, boxColor)
            // The label: above the element (and its margin), or below when there's no room.
            let text = Format.label t + "  " + Format.size b.Width b.Height
            let layout = ctx.Text.Layout(text, ctx.Get TextProps.font, 12.0, Double.PositiveInfinity, NoWrap, NoTrimming, 0)
            let w, h = layout.Width + 12.0, layout.Height + 6.0
            let above = r.Y - m.Top - h - 4.0
            let y = if above >= 0.0 then above else min (r.Y + r.Height + m.Bottom + 4.0) (ctx.Size.Height - h)
            let x = max 0.0 (min (r.X - m.Left) (ctx.Size.Width - w))
            list.FillRoundedRect(Rect.Create(x, y, w, h), CornerRadius.Uniform 4.0, Color.Hex 0x11111B)
            ctx.Text.DrawLayout(list, layout, x + 6.0, y + 3.0, layout.Width, TextAlignment.Start, Color.Hex 0xE6E6F0)
        | _ -> ()

    let Type = Element.define "InspectorHighlight" [ Element.render render ]
    let create (settings: Setting<Highlight> list) = View.Create(Type, settings)

module internal ChevronProps =
    let isOpen = Prop.define "IsOpen" false Affects.Render
    let color = Prop.define "Color" Color.White Affects.Render

/// A small triangle: pointing right (closed) or down (open). Inter has no triangle glyphs, so it's
/// drawn as thin strips.
[<AbstractClass>]
type internal Chevron =
    inherit Layoutable
    static member isOpen<'k when 'k :> Chevron>(value: bool) : Setting<'k> = Setting.create ChevronProps.isOpen value
    static member color<'k when 'k :> Chevron>(value: Color) : Setting<'k> = Setting.create ChevronProps.color value

module internal Chevron =
    let private render (ctx: RenderContext) (list: DrawList) =
        let color = ctx.Get ChevronProps.color
        let cx, cy = ctx.Size.Width / 2.0, ctx.Size.Height / 2.0
        let strips = 8
        let step = 0.5
        for i in 0 .. strips - 1 do
            // From the triangle's base (full length) to its tip.
            let along = float i * step
            let length = 8.0 * (1.0 - float i / float strips)
            let r =
                if ctx.Get ChevronProps.isOpen then Rect.Create(cx - length / 2.0, cy - 2.0 + along, length, step)
                else Rect.Create(cx - 2.0 + along, cy - length / 2.0, step, length)
            list.FillRect(r, color, EdgeMode.Precise)

    let Type =
        Element.define "InspectorChevron" [
            Element.measure (fun _ _ -> Size.Create(12.0, 12.0))
            Element.render render
        ]

    let create (settings: Setting<Chevron> list) = View.Create(Type, settings)

/// The corner grip of a floating panel: a small triangle of dots.
[<AbstractClass>]
type internal Grip =
    inherit Layoutable

module internal Grip =
    let Type =
        Element.define "InspectorGrip" [
            Element.measure (fun _ _ -> Size.Create(16.0, 16.0))
            Element.render (fun ctx list ->
                let color = (Color.Hex 0x8A8AA0).WithAlpha 0.8f
                let s = ctx.Size
                for i, j in [ 0, 0; 1, 0; 0, 1; 2, 0; 1, 1; 0, 2 ] do
                    list.FillRect(Rect.Create(s.Width - 5.0 - float i * 4.0, s.Height - 5.0 - float j * 4.0, 2.0, 2.0), color))
        ]

    let create (settings: Setting<Grip> list) = View.Create(Type, settings)

/// The panel component's props: the shared state, and whether it fills the pop-out window.
type internal PanelProps = { State: InspectorState; IsPoppedOut: bool }

// --- The views ---------------------------------------------------------------------------------------

module internal Views =
    let private panelBackground = (Color.Hex 0x1B1B2A).WithAlpha 0.97f
    let private dim = Color.Hex 0x8A8AA0
    let private typeColor = Color.Hex 0x8AB4F8
    let private componentColor = Color.Hex 0xC792EA
    let private nameColor = Color.Hex 0xF2C27B
    let private hintColor = Color.Hex 0xB8B8C8
    let private indentStep = 12.0

    /// Re-runs the component whenever the state says it changed.
    let private follow (ctx: ComponentContext) (state: InspectorState) =
        let version = ctx.useState 0
        ctx.useSubscription(state, fun (s: InspectorState) -> s.Changed.Subscribe(fun () -> version.Update((+) 1)))

    let private text (value: string) (color: Color) =
        TextBlock.create [
            TextBlock.text value
            TextBlock.fontSize 12.0
            TextBlock.foreground color
            TextBlock.verticalAlignment VerticalAlignment.Center
        ]

    let private chevron (shown: bool) (open': bool) =
        Chevron.create [
            Chevron.isOpen open'
            Chevron.color (if shown then dim else Color.Transparent)
            Chevron.verticalAlignment VerticalAlignment.Center
        ]

    let private row (state: InspectorState) (r: Row) =
        match r with
        | Row.Layer l ->
            let details = "order " + l.Order.ToString() + (if l.IsModal then " · modal" else "")
            Border.create [
                Border.height 24.0
                Border.padding (Thickness.Create(6.0, 0.0, 6.0, 0.0))
                Border.background (Color.Hex 0x26263A)
                Border.onPointerDown (fun _ -> state.ToggleLayer l)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.children [
                            chevron true (not (state.IsCollapsed l))
                            text ("Layer " + l.Name) Color.White
                            text details dim
                        ]
                    ]
                )
            ]
        | Row.Element(e, depth) ->
            let isSelected = state.Selected |> ValueOption.exists (fun s -> obj.ReferenceEquals(s, e))
            let isHovered = state.Hovered |> ValueOption.exists (fun s -> obj.ReferenceEquals(s, e))
            let hasChildren = e.Children.Count > 0
            let isOpen = hasChildren && state.IsExpanded e
            let left = 6.0 + float depth * indentStep
            // Hidden elements (and everything inside them) are dimmed.
            let fade (c: Color) = if e.IsVisible then c else c.WithAlpha 0.4f
            Border.create [
                Border.height 22.0
                Border.padding (Thickness.Create(left, 0.0, 6.0, 0.0))
                Border.background (
                    if isSelected then (Color.Hex 0x3D59A1).WithAlpha 0.6f
                    elif isHovered then Color.White.WithAlpha 0.07f
                    else Color.Transparent
                )
                // The chevron toggles; anywhere else selects.
                Border.onPointerDown (fun a ->
                    if hasChildren && a.LocalPosition.X < left + 14.0 then state.ToggleExpanded e else state.Select e)
                Border.onPointerEnter (fun _ -> state.HoverRow e)
                Border.onPointerLeave (fun _ -> state.LeaveRow e)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.spacing 4.0
                        StackPanel.children [
                            chevron hasChildren isOpen
                            text (Format.label e) (fade (if e.ComponentName.IsSome then componentColor else typeColor))
                            if e.Name.Length > 0 then text ("#" + e.Name) (fade nameColor)
                            if not isOpen then
                                let hint = Format.hint e
                                if hint.Length > 0 then text hint (fade hintColor)
                        ]
                    ]
                )
            ]

    let private rowKey (r: Row) : obj =
        match r with
        | Row.Layer l -> box l
        | Row.Element(e, _) -> box e

    let private smallButton (label: string) (isActive: bool) (onClick: unit -> unit) =
        Button.create [
            Button.padding (7.0, 2.0)
            Button.verticalAlignment VerticalAlignment.Center
            Button.onClick (fun _ -> onClick ())
            if isActive then Button.background (Color.Hex 0x3D59A1)
            Button.child (
                TextBlock.create [
                    TextBlock.text label
                    TextBlock.fontSize 12.0
                    TextBlock.foreground (Color.Hex 0xE6E6F0)
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        ]

    /// Press, move and release on an element drive one kind of drag (moves keep coming to the
    /// element that holds the press, wherever the pointer goes).
    let private dragHandlers<'k when 'k :> Layoutable> (state: InspectorState) (kind: DragKind) : Setting<'k> list =
        [ Layoutable.onPointerDown (fun a -> state.BeginDrag(kind, a.Position))
          Layoutable.onPointerMove (fun a -> state.DragTo a.Position)
          Layoutable.onPointerUp (fun _ -> state.EndDrag()) ]

    /// The title, and the area to drag the panel by (double-click collapses).
    let private title (state: InspectorState) (draggable: bool) =
        Border.create (
            [ Border.width "1*"
              Border.padding (Thickness.Create(12.0, 0.0, 4.0, 0.0))
              Border.child (
                  TextBlock.create [
                      TextBlock.text "Inspector"
                      TextBlock.font (Fonts.SemiBold())
                      TextBlock.foreground Color.White
                      TextBlock.verticalAlignment VerticalAlignment.Center
                  ]
              ) ]
            @ (if draggable then dragHandlers state DragKind.Move else [])
        )

    let private header (state: InspectorState) (isPoppedOut: bool) =
        let floating = state.Mode = PanelMode.Floating
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.spacing 4.0
            StackPanel.height 36.0
            StackPanel.margin (Thickness.Create(0.0, 0.0, 6.0, 0.0))
            StackPanel.children [
                yield title state (not isPoppedOut)
                yield smallButton (if state.IsPicking then "Picking…" else "Pick") state.IsPicking state.TogglePick
                if isPoppedOut then
                    yield smallButton "Pop in" false state.PopIn
                else
                    if floating || state.Side = DockSide.Right then
                        yield smallButton "Left" false (fun () -> state.Dock DockSide.Left)
                    if floating || state.Side = DockSide.Left then
                        yield smallButton "Right" false (fun () -> state.Dock DockSide.Right)
                    if state.CanPopOut then yield smallButton "Pop out" false state.PopOut
                    yield smallButton (if state.IsPanelCollapsed then "+" else "–") false state.ToggleCollapsed
                    yield smallButton "×" false state.Toggle
            ]
        ]

    let private footer (state: InspectorState) (showGrip: bool) =
        let line =
            match state.Selected with
            | ValueSome e ->
                let p = e.PositionInTree
                Format.label e + "   at " + $"{p.X:F0}, {p.Y:F0}" + "   " + Format.size e.Bounds.Width e.Bounds.Height
            | ValueNone ->
                "Click a row, or Pick (" + Format.key state.Options.PickKey + ") and click in the app."
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.children [
                yield
                    TextBlock.create [
                        TextBlock.width "1*"
                        TextBlock.margin (Thickness.Create(10.0, 6.0, 10.0, 8.0))
                        TextBlock.text line
                        TextBlock.fontSize 12.0
                        TextBlock.foreground dim
                        TextBlock.textTrimming CharacterEllipsis
                    ]
                if showGrip then
                    yield
                        Grip.create (
                            [ Grip.verticalAlignment VerticalAlignment.Bottom; Grip.cursor Cursor.ResizeDiagonal ]
                            @ dragHandlers state DragKind.ResizeCorner
                        )
            ]
        ]

    /// The header, the tree and the line about the selection (only the header when collapsed).
    let private content (state: InspectorState) (isPoppedOut: bool) (fillWidth: bool) =
        StackPanel.create [
            if fillWidth then StackPanel.width "1*"
            StackPanel.children [
                yield header state isPoppedOut
                if isPoppedOut || not state.IsPanelCollapsed then
                    yield
                        ScrollViewer.create [
                            ScrollViewer.height "1*"
                            ScrollViewer.content (StackPanel.create [ StackPanel.items (state.Rows()) rowKey (row state) ])
                        ]
                    yield footer state (not isPoppedOut && state.Mode = PanelMode.Floating)
            ]
        ]

    /// A docked panel's inner edge: drag it to change the width. Hidden where it isn't the inner edge.
    let private edge (state: InspectorState) (isShown: bool) =
        Border.create (
            [ Border.isVisible isShown
              Border.width 6.0
              Border.cursor Cursor.ResizeLeftRight
              Border.background Color.Transparent ]
            @ dragHandlers state DragKind.ResizeEdge
        )

    /// The panel: in the app's window (docked or floating), or filling the pop-out window.
    let panel =
        Component.define "Inspector" (fun ctx (props: PanelProps) ->
            let state = props.State
            follow ctx state
            if props.IsPoppedOut then
                Border.create [ Border.background panelBackground; Border.child (content state true false) ]
            else
                let looks =
                    [ Border.isVisible (state.IsOpen && state.Mode <> PanelMode.PoppedOut)
                      Border.cornerRadius 8.0
                      Border.background panelBackground
                      Border.borderThickness 1.0
                      Border.borderBrush (Color.Hex 0x3A3A55)
                      Border.clipToBounds true
                      Border.shadow { Blur = 16.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.5f } ]
                let placement =
                    match state.Mode with
                    | PanelMode.Floating ->
                        let r = state.FloatingRect
                        [ Border.horizontalAlignment HorizontalAlignment.Left
                          Border.verticalAlignment VerticalAlignment.Top
                          Border.margin (r.X, r.Y, 0.0, 0.0)
                          Border.width r.Width
                          if not state.IsPanelCollapsed then Border.height r.Height ]
                    | _ ->
                        let right = state.Side = DockSide.Right
                        [ Border.horizontalAlignment (if right then HorizontalAlignment.Right else HorizontalAlignment.Left)
                          Border.verticalAlignment (if state.IsPanelCollapsed then VerticalAlignment.Top else VerticalAlignment.Stretch)
                          Border.margin PanelSize.gap
                          Border.width state.DockedRect.Width ]
                // The same elements in every mode (only settings change), so whatever holds the
                // pointer keeps it: undocking mid-drag must not rebuild the title being dragged.
                let docked = state.Mode = PanelMode.Docked
                let body =
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.children [
                            edge state (docked && state.Side = DockSide.Right)
                            content state false true
                            edge state (docked && state.Side = DockSide.Left)
                        ]
                    ]
                Border.create (looks @ placement @ [ Border.child body ]))

    /// The highlight, over everything but the panel. In pick mode it also takes the pointer, so
    /// the app doesn't see it.
    let highlight =
        Component.define "InspectorHighlight" (fun ctx (state: InspectorState) ->
            follow ctx state
            let target = state.Target
            Border.create [
                Border.isVisible (state.IsPicking || target.IsSome)
                Border.isHitTestVisible state.IsPicking
                Border.cursor Cursor.Crosshair
                Border.onPointerMove (fun a -> state.PickAt a.Position)
                Border.onPointerDown (fun a ->
                    state.PickSelect a.Position
                    a.Handle())
                Border.child (Highlight.create [ Highlight.target target ])
            ])

/// An inspector for Scribble apps: a panel over the app (it never changes the app's layout) with
/// the element tree of every layer, pick mode and a devtools-style highlight. F12 shows and hides
/// it; Ctrl+Shift+C picks an element (Escape cancels). The panel docks left or right, floats
/// (drag its title), collapses, and on the desktop pops out into a window of its own.
[<AbstractClass; Sealed>]
type Inspector =
    /// Which side the panel docks to (default: right). It can be switched from the panel.
    static member side(value: DockSide) = InspectorSetting(fun o -> { o with Side = value })

    /// The key that shows and hides the inspector (default: F12).
    static member toggleKey(key: Key, modifiers: Modifiers) = InspectorSetting(fun o -> { o with ToggleKey = (key, modifiers) })

    /// The key that starts pick mode (default: Ctrl+Shift+C).
    static member pickKey(key: Key, modifiers: Modifiers) = InspectorSetting(fun o -> { o with PickKey = (key, modifiers) })

    /// Shown from the start (default: hidden until the toggle key).
    static member isOpen(value: bool) = InspectorSetting(fun o -> { o with IsOpen = value })

    /// The platform to open the pop-out window on (`Inspector.enable` passes the app's; custom
    /// hosts pass theirs to get the "Pop out" button). Only platforms with several windows.
    static member platform(value: IPlatform) = InspectorSetting(fun o -> { o with Platform = ValueSome value })

    /// Adds the inspector to a running host (custom hosts; apps use `Inspector.enable`). Dispose
    /// the result to take it away again.
    static member attach(host: ElementHost, settings: InspectorSetting list) : IDisposable =
        let options = settings |> List.fold (fun o (InspectorSetting apply) -> apply o) InspectorOptions.Default
        let state = InspectorState(host, options)
        let highlightLayer =
            host.AddLayer [ Layer.name "Inspector highlight"; Layer.order (Int32.MaxValue - 1); Layer.view (Views.highlight state) ]
        let panelLayer =
            host.AddLayer [ Layer.name "Inspector"; Layer.order Int32.MaxValue; Layer.view (Views.panel { State = state; IsPoppedOut = false }) ]
        state.Own <- [ highlightLayer; panelLayer ]
        let toggleKey, toggleMods = options.ToggleKey
        let pickKey, pickMods = options.PickKey
        host.Window.ClaimKey(toggleKey, toggleMods)
        host.Window.ClaimKey(pickKey, pickMods)

        // The pop-out window: its own host on the same device and text system, showing the panel.
        let popped: (ElementHost * IDisposable) voption ref = ref ValueNone
        let closeWindow () =
            match popped.Value with
            | ValueSome(h, keys) ->
                popped.Value <- ValueNone
                keys.Dispose()
                h.Close()
                (h :> IDisposable).Dispose()
            | ValueNone -> ()
        let openWindow () =
            match options.Platform with
            | ValueSome platform when popped.Value.IsNone ->
                let popHost: ElementHost voption ref = ref ValueNone
                let onEvent (e: PlatformEvent) =
                    match popHost.Value with
                    | ValueSome h ->
                        match e with
                        | CloseRequested -> state.PopIn() // closes the window
                        | _ -> h.HandleEvent e |> ignore
                    | ValueNone -> ()
                let title = "Inspector – " + (options.AppTitle |> ValueOption.defaultValue host.Window.Title)
                let size: DipSize = { Width = 440.0; Height = 720.0 }
                // Owned by the app's window: it stays above it (also after clicks in the app), minimizes
                // with it, and has no taskbar button of its own.
                let windowOptions = { WindowOptions.Default with Title = title; Size = size; Owner = ValueSome host.Window }
                let window = platform.CreateWindow(windowOptions, onEvent)
                let hostSettings =
                    [ yield ElementHost.background (Color.Hex 0x1B1B2A)
                      match host.Dispatcher with
                      | ValueSome d -> yield ElementHost.dispatcher d
                      | ValueNone -> () ]
                let h = ElementHost.create window host.Device host.Text hostSettings
                popHost.Value <- ValueSome h
                h.Update(Views.panel { State = state; IsPoppedOut = true })
                popped.Value <- ValueSome(h, h.AddKeyPreview state.OnKey)
                window.Show()
                window.RequestFrame()
            | _ -> ()

        let subscriptions =
            [ host.AddKeyPreview state.OnKey
              host.LayerChanged.Subscribe state.LayerChanged
              host.FrameDrawn.Subscribe(fun _ -> state.AfterFrame())
              state.PopOutRequested.Subscribe(fun () -> openWindow ())
              state.PopInRequested.Subscribe(fun () -> closeWindow ())
              // Closing the app closes the pop-out too (otherwise its window keeps the app running).
              host.Closed.Subscribe(fun () -> closeWindow ()) ]
        { new IDisposable with
            member _.Dispose() =
                for s in subscriptions do
                    s.Dispose()
                closeWindow ()
                host.RemoveLayer highlightLayer
                host.RemoveLayer panelLayer }

    /// Turns the inspector on for an app: `Inspector.enable [ Inspector.side DockSide.Left ]` in
    /// its settings. Leave it out of release builds with `#if DEBUG` if you like.
    static member enable<'Msg>(settings: InspectorSetting list) : AppSetting<'Msg> =
        App.onStart (fun ctx ->
            let fromApp = InspectorSetting(fun o -> { o with Platform = ValueSome ctx.Platform; AppTitle = ValueSome ctx.Title })
            Inspector.attach (ctx.Host, fromApp :: settings) |> ignore)
