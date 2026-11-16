namespace Scribble.Core

open System
open Scribble.Rendering
open Scribble.Text

/// A tree of elements with what it needs to lay itself out and draw: the text system, the pixel
/// scale, and a callback for "something changed, please give me a frame". Works without a window,
/// which is how layout tests run.
[<Sealed>]
type ElementTree(text: TextSystem) =
    let ownerThread = Environment.CurrentManagedThreadId
    let mutable root: Element voption = ValueNone
    let mutable scale = 1.0
    let mutable onInvalidated: unit -> unit = ignore
    let mutable time = TimeSpan.Zero
    let mutable dispatcher: Dispatcher voption = ValueNone
    let mutable appValues = AppValues()
    /// Components whose state changed, with their depth: run at the start of the next layout.
    let scheduled = Collections.Generic.List<struct (int * (unit -> unit))>()
    let afterFrame = Collections.Generic.List<unit -> unit>()
    // Styles: the app's (every element), whether anything in the tree uses styles (until then
    // restyle requests are ignored), whether a style tests an ancestor's state, and the queue.
    let mutable appStyles: Style list = []
    let mutable theme: Theme voption = ValueNone
    let mutable stylesInUse = false
    let mutable ancestorStates = false
    let restyleQueue = Collections.Generic.List<Element>()

    member _.Text = text

    /// Called when the tree needs a new frame. ElementHost sets this to the window's RequestFrame.
    member _.OnInvalidated
        with get () = onInvalidated
        and set v = onInvalidated <- v

    member this.Root
        with get () = root
        and set (value: Element voption) =
            match root with
            | ValueSome old -> old.SetRoot ValueNone
            | ValueNone -> ()
            root <- value
            match value with
            | ValueSome e ->
                if e.Parent.IsSome then invalidOp "The root element can't have a parent."
                e.SetRoot(ValueSome(this :> IElementRoot))
                e.InvalidateAll()
            | ValueNone -> ()
            onInvalidated ()

    /// Shows a view: the first time it builds the elements, after that it patches them, so only
    /// what changed between the previous view and this one is redone.
    member this.Update(view: View) =
        match root with
        | ValueSome r when obj.ReferenceEquals(r.Type, view.Type) -> r.Patch view
        | _ -> this.Root <- ValueSome(Element.RealizingIn(ValueSome(this :> IElementRoot), fun () -> Element.Realize view))

    /// Physical pixels per DIP. Changing it re-lays out everything (rounding and text sizes change).
    member _.Scale
        with get () = scale
        and set v =
            if v <> scale then
                scale <- v
                root |> ValueOption.iter _.InvalidateAll()
                onInvalidated ()

    /// The current frame's time, for animations (ElementHost sets it at the start of every frame).
    member _.Time
        with get () = time
        and set v = time <- v

    /// The UI thread's dispatcher (App.start sets it): state set from other threads goes through it.
    /// The app values its components use (`ctx.useAppValue`): its own until a host gives it the app's.
    /// Set it before the tree shows components that use them.
    member _.AppValues
        with get () = appValues
        and set (v: AppValues) = appValues <- v

    member _.Dispatcher
        with get () = dispatcher
        and set (v: Dispatcher voption) = dispatcher <- v

    /// Re-runs components whose state changed, parents before children (a parent that re-runs a
    /// child with new props takes care of it). Part of UpdateLayout.
    member _.RunScheduledComponents() =
        let mutable rounds = 0
        while scheduled.Count > 0 && rounds < 100 do
            rounds <- rounds + 1
            let batch = scheduled.ToArray() |> Array.sortBy (fun struct (depth, _) -> depth)
            scheduled.Clear()
            for struct (_, run) in batch do
                run ()

    /// Styles for every element in the tree (`App.styles`; the host sets them on its layers).
    member this.Styles
        with get () = appStyles
        and set (value: Style list) =
            if not (obj.ReferenceEquals(value, appStyles)) then
                appStyles <- value
                stylesInUse <- stylesInUse || not value.IsEmpty
                match root with
                | ValueSome r -> (this :> IElementRoot).RequestRestyle(box r, true, false)
                | ValueNone -> ()

    /// The theme for the whole tree (`App.theme`; the host sets it on its layers): its styles sit
    /// below every other style, and it gives the base text color, font and size. Elements can
    /// set their own (`theme`) for what's inside them.
    member this.Theme
        with get () = theme
        and set (value: Theme voption) =
            let same =
                match value, theme with
                | ValueSome a, ValueSome b -> obj.ReferenceEquals(a, b)
                | ValueNone, ValueNone -> true
                | _ -> false
            if not same then
                theme <- value
                stylesInUse <- stylesInUse || value.IsSome
                match root with
                | ValueSome r ->
                    // Base text values change everywhere; styles are matched again.
                    r.InvalidateAll()
                    r.ThemeChanged()
                    (this :> IElementRoot).RequestRestyle(box r, true, false)
                | ValueNone -> ()
                onInvalidated ()

    /// Matches styles for elements that asked (joined the tree, changed classes or states, ...),
    /// parents before children. Part of UpdateLayout, after components run.
    member this.RunScheduledStyles() =
        let mutable rounds = 0
        while restyleQueue.Count > 0 && rounds < 10 do
            rounds <- rounds + 1
            let depth (e: Element) =
                let mutable d = 0
                let mutable p = e.Parent
                while p.IsSome do
                    d <- d + 1
                    p <- p.Value.Parent
                d
            let batch = restyleQueue.ToArray() |> Array.sortBy depth
            restyleQueue.Clear()
            for e in batch do
                let isOurs =
                    match e.Root with
                    | ValueSome r -> obj.ReferenceEquals(r, this)
                    | ValueNone -> false
                if isOurs then
                    let struct (queued, deep) = e.TakeRestyle()
                    if queued then
                        let scopes = StyleEngine.scopesAbove e
                        let found =
                            if deep then StyleEngine.applyDeep theme appStyles scopes e
                            else StyleEngine.apply theme appStyles scopes e
                        if found then ancestorStates <- true

    /// Runs what asked to run after the frame (effects, animations asking for the next frame).
    /// ElementHost calls it once a frame is drawn.
    member _.RunAfterFrame() =
        if afterFrame.Count > 0 then
            let actions = afterFrame.ToArray()
            afterFrame.Clear()
            for action in actions do
                action ()

    /// Lays the tree out in `size` DIPs (after re-running components whose state changed). Only
    /// invalid parts do any work.
    member this.UpdateLayout(size: Size) =
        this.RunScheduledComponents()
        this.RunScheduledStyles()
        match root with
        | ValueSome r ->
            r.Measure size
            r.Arrange(Rect.Create(0.0, 0.0, size.Width, size.Height))
        | ValueNone -> ()

    member _.Render(list: DrawList) = root |> ValueOption.iter (fun r -> r.Render list)

    /// True when the tree shows anything: a visible element that isn't just a component's host
    /// (a modal layer showing nothing blocks nothing).
    member _.HasVisibleContent =
        let rec shows (e: Element) =
            e.IsVisible && (e.ComponentState.IsNone || e.Children |> Seq.exists shows)
        match root with
        | ValueSome r -> shows r
        | ValueNone -> false

    /// The deepest visible element under a point (DIPs, tree coordinates). A component's host
    /// element is only hit through what the component shows (it fills its slot, but draws nothing).
    member _.HitTest(x: float, y: float) : Element voption =
        let rec hit (e: Element) (ox: float) (oy: float) =
            if not e.IsVisible || not (e.Get LayoutProps.isHitTestVisible) then ValueNone
            else
                let b = e.Bounds
                let left, top = ox + b.X, oy + b.Y
                if x >= left && x < left + b.Width && y >= top && y < top + b.Height then
                    // Later children are drawn on top, so test them first.
                    let children = e.Children
                    let mutable found = ValueNone
                    let mutable i = children.Count - 1
                    while found.IsNone && i >= 0 do
                        found <- hit children[i] left top
                        i <- i - 1
                    if found.IsSome then found
                    elif e.ComponentState.IsSome then ValueNone
                    else ValueSome e
                else ValueNone
        match root with
        | ValueSome r -> hit r 0.0 0.0
        | ValueNone -> ValueNone

    interface IElementRoot with
        member _.Text = text
        member _.Scale = scale
        member _.Invalidated() = onInvalidated ()
        member _.Time = time
        member _.ScheduleComponent(depth, run) =
            scheduled.Add(struct (depth, run))
            onInvalidated ()
        member _.AfterFrame action = afterFrame.Add action
        member _.Dispatcher = dispatcher
        member _.AppValues = appValues
        member _.Theme = theme |> ValueOption.map (fun t -> t :> IThemeSource)
        member this.RequestRestyle(element, deep, stateChange) =
            if stylesInUse then
                let e = element :?> Element
                if e.MarkRestyle(deep || (stateChange && ancestorStates)) then
                    restyleQueue.Add e
                    onInvalidated ()
        member this.StylesUsed() =
            if not stylesInUse then
                stylesInUse <- true
                match root with
                | ValueSome r -> (this :> IElementRoot).RequestRestyle(box r, true, false)
                | ValueNone -> ()
        member _.VerifyAccess() =
#if DEBUG
            if Environment.CurrentManagedThreadId <> ownerThread then
                invalidOp "UI elements can only be used on the UI thread (the thread that created the tree)."
#else
            ignore ownerThread
#endif
