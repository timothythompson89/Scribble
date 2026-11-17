namespace Scribble.Core

open System.Collections.Generic
open Scribble.Platform

/// What the router remembers about one pointer (the mouse, or one finger).
type internal PointerState(kind: PointerKind, id: int) =
    member _.Kind = kind
    member _.Id = id
    /// Where it was last seen (window DIPs), if it's over the window.
    member val Position: Point voption = ValueNone with get, set
    member val Modifiers = Modifiers.None with get, set
    /// The elements it's over: the deepest first, then its ancestors (enabled elements only).
    member val Over: Element list = [] with get, set
    /// The element holding the press: it gets moves and the release (pointer capture).
    member val Captured: Element voption = ValueNone with get, set
    /// How many buttons are down.
    member val Buttons = 0 with get, set
    /// The left-button press route (the pressed element and its ancestors), for IsPressed.
    member val PressRoute: Element list = [] with get, set
    /// The elements currently counted as pressed (PressRoute ∩ Over).
    member val Pressing: Element list = [] with get, set
    /// The element whose onClick runs if the left button comes up over it.
    member val Clickable: Element voption = ValueNone with get, set
    /// Where a touch press started, while a drag from there could still become a pan.
    member val PanStart: Point voption = ValueNone with get, set
    /// The elements under that press whose type pans (deepest first).
    member val PanCandidates: Element list = [] with get, set
    /// The element panning with this finger, and where the finger was at the last pan.
    member val Panning: Element voption = ValueNone with get, set
    member val PanLast = Point.Zero with get, set

/// Where `MoveFocus` goes. Tab is Next, Shift+Tab Previous; arrow directions come later.
[<RequireQualifiedAccess>]
type FocusDirection =
    | Next
    | Previous

/// Turns platform input (in DIPs) into element events: enter and leave on the chain of elements
/// under the pointer, routed pointer, wheel and key events (Preview from the root down, then
/// normal from the target up), pointer capture from press to release, clicks, keyboard focus,
/// element states (pointer over, pressed, focused) and the cursor.
[<Sealed>]
type internal InputRouter(tree: ElementTree, setCursor: Cursor -> unit) =
    let pointers = Dictionary<int, PointerState>()

    let stateFor (kind: PointerKind) (id: int) =
        match pointers.TryGetValue id with
        | true, s -> s
        | _ ->
            let s = PointerState(kind, id)
            pointers[id] <- s
            s

    let isAttached (e: Element) = e.Root.IsSome

    /// The element and its ancestors, deepest first.
    let rec chain (e: Element voption) =
        match e with
        | ValueSome x -> x :: chain x.Parent
        | ValueNone -> []

    /// Who gets events from `target`: it and its ancestors, minus disabled ones (since disabling is
    /// inherited, that drops a run at the start).
    let routeOf (target: Element voption) = chain target |> List.filter _.IsEnabled

    let contains (list: Element list) (e: Element) = list |> List.exists (fun x -> obj.ReferenceEquals(x, e))

    /// What the pointer is over at `p`. While an element holds the press, only it, what's inside it
    /// and its ancestors count (so dragging off a button doesn't light up its neighbours).
    let mutable isCovered: Point -> bool = fun _ -> false

    let overAt (s: PointerState) (p: Point) =
        // Under another layer (while this one holds a press): over nothing here.
        let route = if isCovered p then [] else routeOf (tree.HitTest(p.X, p.Y))
        match s.Panning, s.Captured with
        // A finger scrolling: what it slides over inside the scroller doesn't react.
        | ValueSome c, _ when isAttached c -> route |> List.filter (fun e -> e.Contains c)
        | _, ValueSome c when isAttached c -> route |> List.filter (fun e -> c.Contains e || e.Contains c)
        | _ -> route

    let invoke (e: Element) (prop: Prop<('a -> unit) voption>) (args: 'a) (origin: RoutedEventArgs) =
        match e.Get prop with
        | ValueSome handler ->
            origin.Origin <- e.PositionInTree
            try
                handler args
            with ex ->
                e.ReportError ex
        | ValueNone -> ()

    let pointerArgs (s: PointerState) (button: MouseButton voption) =
        let p = s.Position |> ValueOption.defaultValue Point.Zero
        PointerEventArgs(s.Kind, s.Id, p, button, s.Modifiers)

    /// Raises a routed event: Preview from the root down to the target, then normal from the target
    /// up, stopping once handled; on the way up each element's type gets it after the element's own
    /// handler (`typeHook`). Returns the elements that handled it in Preview and in a type hook.
    let routedWith
        (route: Element list)
        (preview: Prop<('a -> unit) voption> voption)
        (normal: Prop<('a -> unit) voption>)
        (typeHook: ElementType -> (InputContext -> 'a -> unit) voption)
        (args: 'a)
        =
        let origin = box args :?> RoutedEventArgs
        let mutable previewHandler = ValueNone
        let mutable typeHandler = ValueNone
        match preview with
        | ValueSome previewProp ->
            for e in List.rev route do
                if not origin.IsHandled then
                    invoke e previewProp args origin
                    if origin.IsHandled then previewHandler <- ValueSome e
        | ValueNone -> ()
        for e in route do
            if not origin.IsHandled then invoke e normal args origin
            if not origin.IsHandled then
                match typeHook e.Type with
                | ValueSome f ->
                    origin.Origin <- e.PositionInTree
                    try
                        f (InputContext e) args
                    with ex ->
                        e.ReportError ex
                    if origin.IsHandled then typeHandler <- ValueSome e
                | ValueNone -> ()
        struct (previewHandler, typeHandler)

    let routed (route: Element list) (preview: Prop<('a -> unit) voption> voption) (normal: Prop<('a -> unit) voption>) (args: 'a) =
        let struct (previewHandler, _) = routedWith route preview normal (fun _ -> ValueNone) args
        previewHandler

    let updateCursor (over: Element list) =
        let cursor =
            over |> List.tryPick (fun e -> match e.Get LayoutProps.cursor with ValueSome c -> Some c | ValueNone -> None)
        setCursor (defaultArg cursor Cursor.Default)

    /// Moves the pointer's "over" chain to `next`: leave on elements it left (deepest first), enter on
    /// the ones it entered (outermost first), and the same for the pressed states.
    let setOver (s: PointerState) (next: Element list) =
        let previous = s.Over
        s.Over <- next
        let args = pointerArgs s ValueNone
        for e in previous do
            if not (contains next e) then
                e.ChangePointersOver -1
                invoke e LayoutProps.onPointerLeave args args
        for e in List.rev next do
            if not (contains previous e) then
                e.ChangePointersOver 1
                invoke e LayoutProps.onPointerEnter args args
        let pressing = s.PressRoute |> List.filter (contains next)
        for e in s.Pressing do
            if not (contains pressing e) then e.ChangePointersPressing -1
        for e in pressing do
            if not (contains s.Pressing e) then e.ChangePointersPressing 1
        s.Pressing <- pressing
        // Only while the pointer is over this tree: another layer may own the cursor now.
        if s.Kind = PointerKind.Mouse && s.Position.IsSome then updateCursor next

    /// The element events go to: the one holding the press, else the deepest one under the pointer.
    let target (s: PointerState) =
        match s.Captured with
        | ValueSome c when isAttached c && c.IsEnabled -> ValueSome c
        | _ -> List.tryHead s.Over |> Option.toValueOption

    /// Tells the panning element its finger is gone (`canceled`: no fling).
    let endPan (s: PointerState) (canceled: bool) =
        match s.Panning with
        | ValueSome e when isAttached e ->
            match e.Type.PanEnded with
            | ValueSome f ->
                try
                    f (InputContext e) canceled
                with ex ->
                    e.ReportError ex
            | ValueNone -> ()
        | _ -> ()

    let endPress (s: PointerState) =
        s.Buttons <- 0
        s.Captured <- ValueNone
        s.PressRoute <- []
        s.Clickable <- ValueNone
        s.PanStart <- ValueNone
        s.PanCandidates <- []
        s.Panning <- ValueNone

    /// How far (DIPs) a finger moves before its press becomes a pan.
    let panThreshold = 8.0

    let pan (e: Element) (dx: float) (dy: float) =
        match e.Type.Pan with
        | ValueSome f when isAttached e && e.IsEnabled ->
            try
                f (InputContext e) dx dy
            with ex ->
                e.ReportError ex
                false
        | _ -> false

    /// A touch drag: the panning element follows the finger; before that, once the finger is far
    /// enough from where it went down, the deepest element that takes the drag gets it, and the
    /// press underneath is canceled (no click, no pressed look).
    let trackPan (s: PointerState) (position: Point) =
        match s.Panning, s.PanStart with
        | ValueSome e, _ ->
            let dx, dy = position.X - s.PanLast.X, position.Y - s.PanLast.Y
            s.PanLast <- position
            pan e dx dy |> ignore
        | ValueNone, ValueSome start when s.Buttons > 0 ->
            let dx, dy = position.X - start.X, position.Y - start.Y
            if dx * dx + dy * dy >= panThreshold * panThreshold then
                match s.PanCandidates |> List.tryFind (fun e -> pan e dx dy) with
                | Some e ->
                    s.Panning <- ValueSome e
                    s.PanLast <- position
                    s.Captured <- ValueSome e
                    s.PressRoute <- []
                    s.Clickable <- ValueNone
                | None -> ()
        | _ -> ()

    let refresh (s: PointerState) =
        match s.Position with
        | ValueSome p -> setOver s (overAt s p)
        | ValueNone -> setOver s []

    // --- Focus -----------------------------------------------------------------------------------

    let mutable focused: Element voption = ValueNone
    /// The keyboard is in use (Tab was pressed since the last pointer press): show the focus ring.
    let mutable keyboardMode = false
    let mutable windowActive = true
    /// The element Space went down on: it looks pressed and clicks when Space comes up.
    let mutable spacePressed: Element voption = ValueNone

    /// Shown and enabled all the way up, and in the tree.
    let rec isShown (e: Element) =
        e.IsVisible && (match e.Parent with ValueSome p -> isShown p | ValueNone -> true)

    let canFocus (e: Element) = isAttached e && e.Get LayoutProps.focusable && e.IsEnabled && isShown e

    let releaseSpace () =
        match spacePressed with
        | ValueSome e ->
            spacePressed <- ValueNone
            e.SetKeyPressed false
        | ValueNone -> ()

    let showRing () = keyboardMode && windowActive

    let setFocus (next: Element voption) =
        match focused, next with
        | ValueSome a, ValueSome b when obj.ReferenceEquals(a, b) -> a.SetFocusState(true, showRing ())
        | _ ->
            releaseSpace ()
            focused |> ValueOption.iter (fun e -> e.SetFocusState(false, false))
            focused <- next
            next |> ValueOption.iter (fun e -> e.SetFocusState(true, showRing ()))

    /// Focusable elements in tree order (parents before children, children in order), skipping
    /// hidden and disabled subtrees.
    let focusOrder () =
        let result = ResizeArray<Element>()
        let rec walk (e: Element) =
            if e.IsVisible && e.Get LayoutProps.isEnabled then
                if e.Get LayoutProps.focusable && e.Get LayoutProps.isTabStop then result.Add e
                for child in e.Children do
                    walk child
        tree.Root |> ValueOption.iter walk
        result

    /// The focused element, or ValueNone if it can no longer have focus (and then focus is cleared).
    let currentFocus () =
        match focused with
        | ValueSome e when canFocus e -> focused
        | ValueSome _ ->
            setFocus ValueNone
            ValueNone
        | ValueNone -> ValueNone

    /// The element's own `onClick`, then its type's click behavior (a ToggleButton toggling).
    let click (e: Element) (source: ClickSource) (modifiers: Modifiers) =
        let args = ClickEventArgs(source, modifiers)
        match e.Get LayoutProps.onClick with
        | ValueSome handler ->
            try
                handler args
            with ex ->
                e.ReportError ex
        | ValueNone -> ()
        match e.Type.Click with
        | ValueSome f when isAttached e && e.IsEnabled ->
            try
                f (InputContext e) args
            with ex ->
                e.ReportError ex
        | _ -> ()

    let hasClick (e: Element) = (e.Get LayoutProps.onClick).IsSome || e.Type.Click.IsSome

    // `element.Focus()` from code (a TabControl's arrow keys) comes here.
    do
        tree.FocusHandler <-
            fun e ->
                if canFocus e then
                    setFocus (ValueSome e)
                    e.BringIntoView()

    /// True where another layer has the pointer (set by the host when the tree is a layer).
    member _.IsCovered
        with get () = isCovered
        and set v = isCovered <- v

    member _.PointerMoved(kind: PointerKind, id: int, position: Point, modifiers: Modifiers) =
        let s = stateFor kind id
        s.Position <- ValueSome position
        s.Modifiers <- modifiers
        trackPan s position
        setOver s (overAt s position)
        routedWith (routeOf (target s)) ValueNone LayoutProps.onPointerMove _.PointerMove (pointerArgs s ValueNone) |> ignore

    member this.PointerPressed(kind: PointerKind, id: int, position: Point, button: MouseButton, modifiers: Modifiers) =
        let s = stateFor kind id
        s.Position <- ValueSome position
        s.Modifiers <- modifiers
        // A touch can go down without moving first.
        setOver s (overAt s position)
        let route = routeOf (target s)
        if s.Buttons = 0 then
            // A press moves focus to the nearest focusable element under it (or clears it), and
            // puts the ring away until the keyboard is used again.
            keyboardMode <- false
            setFocus (route |> List.tryFind canFocus |> Option.toValueOption)
        let args = pointerArgs s (ValueSome button)
        let struct (previewHandler, typeHandler) =
            routedWith route (ValueSome LayoutProps.onPreviewPointerDown) LayoutProps.onPointerDown _.PointerDown args
        if s.Buttons = 0 then
            // The element that took the press in Preview, or in its type's own behavior (a
            // scrollbar thumb), gets it; otherwise the deepest one.
            s.Captured <-
                match previewHandler, typeHandler with
                | ValueSome e, _
                | ValueNone, ValueSome e -> ValueSome e
                | ValueNone, ValueNone -> List.tryHead route |> Option.toValueOption
            // A finger may drag to pan, unless a handler took the press (it wants the drag itself).
            if kind = PointerKind.Touch && not args.IsHandled then
                s.PanCandidates <- route |> List.filter (fun e -> e.Type.Pan.IsSome)
                s.PanStart <- if s.PanCandidates.IsEmpty then ValueNone else ValueSome position
        s.Buttons <- s.Buttons + 1
        if button = MouseButton.Left && previewHandler.IsNone && typeHandler.IsNone then
            s.PressRoute <- route
            s.Clickable <- route |> List.tryFind hasClick |> Option.toValueOption
        elif button = MouseButton.Left && previewHandler.IsNone then
            // A type took the press (a Slider's thumb, a scrollbar): it alone looks pressed while
            // the pointer is over it; nothing clicks.
            s.PressRoute <- typeHandler |> ValueOption.toList
        refresh s

    member _.PointerReleased(kind: PointerKind, id: int, position: Point, button: MouseButton, modifiers: Modifiers) =
        let s = stateFor kind id
        s.Position <- ValueSome position
        s.Modifiers <- modifiers
        setOver s (overAt s position)
        routedWith (routeOf (target s)) (ValueSome LayoutProps.onPreviewPointerUp) LayoutProps.onPointerUp _.PointerUp (pointerArgs s (ValueSome button))
        |> ignore
        let clicked =
            if button = MouseButton.Left then
                let c = s.Clickable
                s.PressRoute <- []
                s.Clickable <- ValueNone
                // Only if released over it (or something inside it), and it's still there and enabled.
                match c with
                | ValueSome e when isAttached e && e.IsEnabled && contains s.Over e -> ValueSome e
                | _ -> ValueNone
            else ValueNone
        s.Buttons <- max 0 (s.Buttons - 1)
        if s.Buttons = 0 then
            endPan s false
            endPress s
        refresh s
        clicked |> ValueOption.iter (fun e -> click e (ClickSource.Pointer kind) modifiers)

    /// The pointer is now at `position` over this tree without having moved (it came from another
    /// layer, or the layer above it went away): hover and cursor update, no move event.
    member _.PointerAt(kind: PointerKind, id: int, position: Point, modifiers: Modifiers) =
        let s = stateFor kind id
        s.Position <- ValueSome position
        s.Modifiers <- modifiers
        setOver s (overAt s position)

    /// The pointer left the window (or a touch ended, or it moved to another layer).
    member _.PointerExited(kind: PointerKind, id: int) =
        let s = stateFor kind id
        if s.Buttons = 0 then
            s.Position <- ValueNone
            setOver s []
            if kind <> PointerKind.Mouse then pointers.Remove id |> ignore

    /// The press was taken away: no release, no click.
    member _.PointerCanceled(kind: PointerKind, id: int) =
        let s = stateFor kind id
        endPan s true
        endPress s
        refresh s

    /// Preview from the root down, then up from the element under the pointer: each element's
    /// `onWheel` setting, then its type's own wheel behavior (a ScrollViewer scrolling).
    member _.Wheel(position: Point, deltaX: float, deltaY: float, modifiers: Modifiers) =
        let route = routeOf (tree.HitTest(position.X, position.Y))
        let args = WheelEventArgs(position, deltaX, deltaY, modifiers)
        for e in List.rev route do
            if not args.IsHandled then invoke e LayoutProps.onPreviewWheel args args
        for e in route do
            if not args.IsHandled then invoke e LayoutProps.onWheel args args
            if not args.IsHandled then
                match e.Type.Wheel with
                | ValueSome f ->
                    args.Origin <- e.PositionInTree
                    try
                        f (InputContext e) args
                    with ex ->
                        e.ReportError ex
                | ValueNone -> ()

    /// After layout: things may have moved, appeared or gone under a pointer that stood still.
    member _.Refresh() =
        for s in List.ofSeq pointers.Values do
            match s.Captured with
            | ValueSome c when not (isAttached c) -> endPress s
            | _ -> ()
            refresh s
        // The focused element may have been disabled, hidden or removed.
        currentFocus () |> ignore

    // --- Focus and keys ----------------------------------------------------------------------------

    /// The element with keyboard focus, if any.
    member _.Focused = currentFocus ()

    /// Moves focus to the next or previous focusable element in tree order, wrapping around, and
    /// shows the focus ring. With nothing focused, Next goes to the first and Previous to the last.
    member _.MoveFocus(direction: FocusDirection) =
        keyboardMode <- true
        let order = focusOrder ()
        if order.Count = 0 then setFocus ValueNone
        else
            let index =
                match currentFocus () with
                | ValueSome e -> order.FindIndex(fun x -> obj.ReferenceEquals(x, e))
                | ValueNone -> -1
            let next =
                match direction, index with
                | FocusDirection.Next, -1 -> 0
                | FocusDirection.Previous, -1 -> order.Count - 1
                | FocusDirection.Next, i -> (i + 1) % order.Count
                | FocusDirection.Previous, i -> (i - 1 + order.Count) % order.Count
            setFocus (ValueSome order[next])
            // Into view through every scroller around it.
            order[next].BringIntoView()

    /// Routes a key down from the focused element (else the root), then does what's built in if no
    /// handler took it: Tab moves focus, Enter clicks, Space presses (clicking on release), Escape
    /// cancels a Space press. Returns true when something handled the key.
    member this.KeyDown(key: Key, modifiers: Modifiers, isRepeat: bool) : bool =
        let target = match currentFocus () with ValueSome e -> ValueSome e | ValueNone -> tree.Root
        let args = KeyEventArgs(key, modifiers, isRepeat)
        routedWith (routeOf target) (ValueSome LayoutProps.onPreviewKeyDown) LayoutProps.onKeyDown _.KeyDown args |> ignore
        if args.IsHandled then true
        else
            let plain = modifiers &&& (Modifiers.Control ||| Modifiers.Alt ||| Modifiers.Meta) = Modifiers.None
            match key, currentFocus () with
            | Key.Tab, _ when plain ->
                this.MoveFocus(if modifiers.HasFlag Modifiers.Shift then FocusDirection.Previous else FocusDirection.Next)
                true
            | Key.Escape, _ when spacePressed.IsSome ->
                releaseSpace ()
                true
            | Key.Enter, ValueSome e when plain && hasClick e ->
                if not isRepeat then click e ClickSource.Keyboard modifiers
                true
            | Key.Space, ValueSome e when plain && hasClick e ->
                if not isRepeat && spacePressed.IsNone then
                    spacePressed <- ValueSome e
                    e.SetKeyPressed true
                true
            | _ -> false

    /// Routes a key up the same way; Space coming up clicks the element it went down on.
    member _.KeyUp(key: Key, modifiers: Modifiers) : bool =
        let target = match currentFocus () with ValueSome e -> ValueSome e | ValueNone -> tree.Root
        let args = KeyEventArgs(key, modifiers, false)
        routed (routeOf target) (ValueSome LayoutProps.onPreviewKeyUp) LayoutProps.onKeyUp args |> ignore
        match key, spacePressed with
        | Key.Space, ValueSome e ->
            releaseSpace ()
            if not args.IsHandled && canFocus e && focused = ValueSome e then click e ClickSource.Keyboard modifiers
            true
        | _ -> args.IsHandled

    /// The window became active or inactive: the ring hides while inactive, and a held Space is
    /// forgotten (its release goes to another window).
    member _.WindowFocusChanged(active: bool) =
        windowActive <- active
        if not active then releaseSpace ()
        focused |> ValueOption.iter (fun e -> e.SetFocusState(true, showRing ()))
