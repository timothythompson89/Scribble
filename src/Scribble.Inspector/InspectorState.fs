namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// What the inspector shows and does. Both of its layers draw from this; `Changed` tells them to
/// update. UI thread only.
[<Sealed>]
type internal InspectorState(host: ElementHost, options: InspectorOptions) =
    let changed = Event<unit>()
    /// Bumped by every change except the panel's own geometry (moving, resizing, the divider), so
    /// the tree and properties aren't rebuilt while the panel is dragged.
    let mutable contentVersion = 0
    let notify () =
        contentVersion <- contentVersion + 1
        changed.Trigger()
    /// Only the panel's place or size changed.
    let moved () = changed.Trigger()
    let expanded = HashSet<Element>(HashIdentity.Reference)
    let seenRoots = HashSet<Element>(HashIdentity.Reference)
    let collapsedLayers = HashSet<Layer>(HashIdentity.Reference)
    let clockMs () = host.Elapsed.TotalMilliseconds
    let mutable isOpen = options.IsOpen
    let mutable side = options.Side
    let mutable picking = false
    /// Pick mode: where the pointer is over the app (window DIPs), so what it points at can be
    /// checked again after each frame: the app can move under a pointer that stands still (a list
    /// scrolled by the wheel recycles the row under it to somewhere else).
    let mutable pickPoint: Point voption = ValueNone
    let mutable hovered: Element voption = ValueNone
    let mutable selected: Element voption = ValueNone
    /// The selected element's tree row should be scrolled into view (after a pick, which can
    /// select something whose row is far down); asked once the tree lists it.
    let mutable revealSelected = false
    /// The last `Rows()` had the selected element's row.
    let mutable selectedListed = false
    /// After the tree scrolled to the selected row: bring its name into view sideways once the row exists.
    let mutable revealSideways = false
    /// Scrolls the tree (an ItemsView) by key.
    let treeScroller = Scroller()
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
    let mutable showDefaults = false
    /// Elements with states forced from the panel (cleared when the inspector closes).
    let forcedOn = HashSet<Element>(HashIdentity.Reference)
    /// The properties' share of the panel, in DIPs (the divider changes it).
    let mutable propertiesHeight = 300.0
    // Frame stats. They're shown as of the last refresh only: the inspector's own frames must not
    // update them, or every frame showing new stats would make new stats (frames forever).
    let mutable lastStats: FrameStats voption = ValueNone
    let mutable statsText = ""
    /// Something in the app's layers changed since the last frame.
    let mutable appChanged = false
    /// When the frames the app caused were drawn (the last second's worth), for frames/s.
    let appFrames = Queue<float>()
    let mutable idleCheckScheduled = false

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

    let trimFrames (now: float) =
        while appFrames.Count > 0 && now - appFrames.Peek() > 1000.0 do
            appFrames.Dequeue() |> ignore

    let refresh () =
        dirty <- false
        let now = clockMs ()
        lastRefresh <- now
        hovered <- alive hovered
        selected <- alive selected
        trimFrames now
        let inv = Globalization.CultureInfo.InvariantCulture
        let frames =
            match appFrames.Count with
            | 0 -> "app idle"
            | n -> n.ToString(inv) + " app frames/s"
        statsText <-
            match lastStats with
            | ValueSome s ->
                "layout " + s.LayoutMilliseconds.ToString("0.00", inv) + " · render " + s.RenderMilliseconds.ToString("0.00", inv)
                + " ms · " + s.Shapes.ToString("N0", inv) + " shapes · " + frames
            | ValueNone -> frames
        notify ()

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
    /// Changes with everything but the panel's geometry (see `notify`).
    member _.ContentVersion = contentVersion
    member _.IsOpen = isOpen
    member _.IsPicking = picking
    member _.Hovered = hovered
    member _.Selected = selected
    member _.Options = options

    member _.Own
        with get () = own
        and set v =
            own <- v
            notify () // the tree leaves its own layers out

    /// What the highlight shows: the hovered element (a row or, in pick mode, the app), else the selection.
    member _.Target =
        if not isOpen then ValueNone
        else
            match alive hovered with
            | ValueSome e -> ValueSome e
            | ValueNone -> alive selected

    /// The highlight shows the selection, not something pointed at: then it's only an outline,
    /// so the element's own looks (hover tint, press, forced states) stay visible.
    member this.TargetIsSelection =
        isOpen && (alive hovered).IsNone && this.Target.IsSome

    member _.IsExpanded(e: Element) = expanded.Contains e
    member _.IsCollapsed(l: Layer) = collapsedLayers.Contains l

    /// The rows to show: every layer but the inspector's, and the elements under expanded ones.
    /// Scrolls the tree by key (the tree's ItemsView uses it).
    member _.TreeScroller = treeScroller

    member _.Rows() : Row[] =
        let rows = List<Row>()
        selectedListed <- false
        let rec add (e: Element) depth =
            rows.Add(Row.Element(e, depth))
            match selected with
            | ValueSome s when obj.ReferenceEquals(s, e) -> selectedListed <- true
            | _ -> ()
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
        rows.ToArray()

    member this.Toggle() =
        // Hiding a popped-out inspector closes its window (the panel goes back in for next time).
        if isOpen && mode = PanelMode.PoppedOut then this.PopIn()
        isOpen <- not isOpen
        // Closing it leaves no state forced in the app.
        if not isOpen then this.ClearForced()
        picking <- picking && isOpen
        hovered <- ValueNone
        pickPoint <- ValueNone
        refresh ()

    member _.TogglePick() =
        picking <- not picking
        if picking then isOpen <- true
        hovered <- ValueNone
        pickPoint <- ValueNone
        refresh ()

    member _.StopPicking() =
        picking <- false
        hovered <- ValueNone
        pickPoint <- ValueNone
        notify ()

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
        notify ()

    member this.ToggleCollapsed() =
        collapsed <- not collapsed
        notify ()

    member this.PopOut() =
        if this.CanPopOut && mode <> PanelMode.PoppedOut then
            modeBeforePopOut <- mode
            mode <- PanelMode.PoppedOut
            isOpen <- true
            popOut.Trigger()
            notify ()

    /// Back into the app's window, where it was before.
    member _.PopIn() =
        if mode = PanelMode.PoppedOut then
            mode <- modeBeforePopOut
            popIn.Trigger()
            notify ()

    /// A press on the title area (move), the corner grip or a docked panel's inner edge. Two
    /// quick presses on the title area collapse or expand the panel instead.
    member this.BeginDrag(kind: DragKind, p: Point) =
        let now = clockMs ()
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
                | DragKind.Split, _ -> Rect.Create(0.0, 0.0, 0.0, this.PropertiesHeight)
                | _ -> keepInside floating
            drag <- ValueSome { Kind = kind; Start = p; StartRect = start }

    member this.DragTo(p: Point) =
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
                    moved ()
            | DragKind.Split ->
                // Up makes the properties taller.
                propertiesHeight <- max PanelSize.minProperties (r.Height - dy)
                propertiesHeight <- this.PropertiesHeight
                moved ()
            | DragKind.ResizeCorner ->
                floating <- keepInside (Rect.Create(r.X, r.Y, r.Width + dx, r.Height + dy))
                moved ()
            | DragKind.ResizeEdge ->
                let s = windowSize ()
                let w = r.Width + (if side = DockSide.Right then -dx else dx)
                dockedWidth <- clamp PanelSize.minWidth (s.Width - 2.0 * PanelSize.gap) w
                moved ()
        | ValueNone -> ()

    member _.EndDrag() = drag <- ValueNone

    // --- The properties ------------------------------------------------------------------------

    /// Property defaults are listed too (off by default; type defaults always are).
    member _.ShowDefaults = showDefaults

    member _.ToggleShowDefaults() =
        showDefaults <- not showDefaults
        notify ()

    /// The properties' height, kept small enough to leave the tree some room in the app's window.
    member _.PropertiesHeight =
        let panelHeight =
            match mode with
            | PanelMode.Floating -> (keepInside floating).Height
            | PanelMode.Docked -> (dockedRect ()).Height
            | PanelMode.PoppedOut -> Double.PositiveInfinity
        max PanelSize.minProperties (min propertiesHeight (panelHeight - PanelSize.besideProperties))

    /// Frame stats as of the last refresh, and the app's frames in the second before it.
    member _.StatsText = statsText

    member _.Format(v: obj) = Format.value options.Format v

    member _.ToggleExpanded(e: Element) =
        if not (expanded.Remove e) then expanded.Add e |> ignore
        notify ()

    member _.ToggleLayer(l: Layer) =
        if not (collapsedLayers.Remove l) then collapsedLayers.Add l |> ignore
        notify ()

    /// Selects an element and expands the rows down to it.
    member _.Select(e: Element) =
        selected <- ValueSome e
        revealSelected <- true
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
        notify ()

    // --- Forced states ------------------------------------------------------------------------

    /// Forces `s` on `e` to the opposite of what it is now (on if off, off if on), or stops forcing it
    /// (looks only; see `element.ForceState`).
    member _.ToggleForced(e: Element, s: ElementState) =
        if e.ForcedStates |> List.exists (fun (f, _) -> obj.ReferenceEquals(f, s)) then
            e.UnforceState s
            if e.ForcedStates.IsEmpty then forcedOn.Remove e |> ignore
        else
            let isOn = e.ActiveStates |> List.exists (fun a -> obj.ReferenceEquals(a, s))
            e.ForceState(s, not isOn)
            forcedOn.Add e |> ignore
        notify ()

    /// Some element has forced states.
    member _.HasForced = forcedOn.Count > 0

    /// Stops forcing states everywhere (also when the inspector closes or goes away).
    member _.ClearForced() =
        if forcedOn.Count > 0 then
            for e in forcedOn do
                e.ClearForcedStates()
            forcedOn.Clear()
            notify ()

    /// Clears the selection (the panel shows its hint again).
    member _.Deselect() =
        if selected.IsSome then
            selected <- ValueNone
            revealSelected <- false
            revealSideways <- false
            notify ()

    /// A click on a tree row: selects it, or deselects it when it's already selected.
    member this.ClickRow(e: Element) =
        match selected with
        | ValueSome s when obj.ReferenceEquals(s, e) -> this.Deselect()
        | _ -> this.Select e

    member _.HoverRow(e: Element) =
        if not picking then
            hovered <- ValueSome e
            notify ()

    member _.LeaveRow(e: Element) =
        match hovered with
        | ValueSome h when not picking && obj.ReferenceEquals(h, e) ->
            hovered <- ValueNone
            notify ()
        | _ -> ()

    /// Pick mode: the pointer moved to `p` (window DIPs).
    /// Only while picking: the press that picks ends pick mode, and browsers send a move before its
    /// release (the highlight still has the pointer then); picking again there left an element hovered.
    member _.PickAt(p: Point) =
        if picking then
            pickPoint <- ValueSome p
            let e = elementAt p
            let same =
                match e, hovered with
                | ValueSome a, ValueSome b -> obj.ReferenceEquals(a, b)
                | ValueNone, ValueNone -> true
                | _ -> false
            if not same then
                hovered <- e
                notify ()

    /// Pick mode: a press at `p` selects what's there and ends pick mode.
    member this.PickSelect(p: Point) =
        picking <- false
        hovered <- ValueNone
        pickPoint <- ValueNone
        match elementAt p with
        | ValueSome e -> this.Select e
        | ValueNone -> notify ()

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
    member _.LayerChanged(layer: Layer) =
        if not (isOwn layer) then
            appChanged <- true
            if isOpen then dirty <- true

    /// After each frame: refresh the tree if the app changed, at most about 10 times a second.
    /// Once the app has been still for a second, one more refresh shows it idle (and then nothing).
    /// Pick mode: the pointer went over the panel or out of the window; nothing to check again.
    member _.PickLeft() = pickPoint <- ValueNone

    member this.AfterFrame(stats: FrameStats) =
        // Pick mode: what the still pointer points at now (a change shows in the next frame).
        match pickPoint with
        | ValueSome p when picking -> this.PickAt p
        | _ -> ()
        // The selected row, once the tree lists it (it refreshes a little later): into view, by key
        // (the tree is an ItemsView; the row may never have been realized).
        match selected with
        | ValueSome e when revealSelected && selectedListed ->
            revealSelected <- false
            revealSideways <- true
            treeScroller.ScrollTo(box e, ScrollAlign.Nearest, false) // at once, as devtools do
        | _ when revealSideways ->
            // The row exists now (the frame after the scroll): its chevron and name into view sideways,
            // with a little indent to their left; nothing moves when they already show.
            let rec named (e: Element) : Element voption =
                if e.Name = Names.SelectedLabel then ValueSome e
                else
                    let mutable found = ValueNone
                    let mutable i = 0
                    while found.IsNone && i < e.Children.Count do
                        found <- named e.Children[i]
                        i <- i + 1
                    found
            let label = own |> List.tryPick (fun l -> match l.Tree.Root with ValueSome r -> (match named r with ValueSome x -> Some x | ValueNone -> None) | ValueNone -> None)
            match label with
            | Some l ->
                revealSideways <- false
                l.BringIntoView()
            | None -> if selected.IsNone then revealSideways <- false
        | _ -> ()
        let now = clockMs ()
        lastStats <- ValueSome stats
        if appChanged then
            appChanged <- false
            appFrames.Enqueue now
        trimFrames now
        let later (wait: float) (action: unit -> unit) =
            match host.Dispatcher with
            | ValueSome d ->
                d.PostAfter(TimeSpan.FromMilliseconds wait, action)
                true
            | ValueNone -> false
        let checkIdle () =
            if isOpen && not idleCheckScheduled then
                idleCheckScheduled <-
                    later 1100.0 (fun () ->
                        idleCheckScheduled <- false
                        trimFrames (clockMs ())
                        if isOpen && not dirty && appFrames.Count = 0 then refresh ())
        if dirty && isOpen && not refreshScheduled then
            let wait = 100.0 - (now - lastRefresh)
            if wait > 0.0 then
                refreshScheduled <-
                    later wait (fun () ->
                        refreshScheduled <- false
                        if dirty && isOpen then
                            refresh ()
                            checkIdle ())
                if not refreshScheduled then refresh ()
            else
                refresh ()
                checkIdle ()
