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
    let mutable layoutFailed = false
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

    /// What `element.Focus()` does: the input router that serves this tree sets it.
    member val internal FocusHandler: Element -> unit = ignore with get, set
    /// Hears elements whose watched properties changed (`IElementRoot.Watch`): the host's flyouts.
    member val internal WatchHandler: Element -> unit = ignore with get, set
    /// Hears every restyle request (before the tree decides it needs one): the host restyles flyouts whose element
    /// is or is inside the element asking.
    member val internal RestyleHandler: Element -> unit = ignore with get, set
    /// What the host that runs this tree offers the components in it (its toasts and dialogs): a
    /// `HostServices`, kept untyped because those types come later in the compile order.
    member val internal Services: obj = null with get, set
    /// The element this tree belongs to (a flyout's or tooltip's): styled as if inside it.
    member val internal Owner: Element voption = ValueNone with get, set
    /// What an element that takes text last said of its caret (`IElementRoot.TextInputArea`): the element, the
    /// caret's box in its coordinates, the keyboard and whether Enter makes a new line. The host reads it for the
    /// focused element.
    member val internal TextArea: struct (Element * Rect * KeyboardKind * bool) voption = ValueNone with get, set
    /// Hears an element asking for a layer of its own (`IElementRoot.ElementLayer`): the host sets it.
    member val internal ElementLayerHandler: Element -> (unit -> obj) -> unit = (fun _ _ -> ()) with get, set
    /// Drops the window's composition (`IElementRoot.ClearComposition`): the host sets it.
    member val internal ClearCompositionHandler: unit -> unit = ignore with get, set
    /// Where the root goes, from the root (measured: its `DesiredSize`, its settings) and the window's size: a flyout's
    /// or tooltip's tree places its root beside the element it belongs to. ValueNone: the root fills the window.
    member val internal Place: (Element -> Size -> Rect) voption = ValueNone with get, set

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
        let t0 = if ItemsProfile.On then Diagnostics.Stopwatch.GetTimestamp() else 0L
        this.RunScheduledStylesNow()
        if ItemsProfile.On then ItemsProfile.Restyle <- ItemsProfile.Restyle + (Diagnostics.Stopwatch.GetTimestamp() - t0)

    member private this.RunScheduledStylesNow() =
        let mutable rounds = 0
        while restyleQueue.Count > 0 && rounds < 10 do
            rounds <- rounds + 1
            let batch = restyleQueue.ToArray()
            restyleQueue.Clear()
            this.ApplyRestyles batch

    /// Matches styles for `batch` (queued elements), parents before children.
    member private this.ApplyRestyles(batch: Element[]) =
        let depth (e: Element) =
            let mutable d = 0
            let mutable p = e.Parent
            while p.IsSome do
                d <- d + 1
                p <- p.Value.Parent
            d
        for e in Array.sortBy depth batch do
            let isOurs =
                match e.Root with
                | ValueSome r -> obj.ReferenceEquals(r, this)
                | ValueNone -> false
            if isOurs then
                let struct (queued, deep) = e.TakeRestyle()
                if queued then
                    if ItemsProfile.On then ItemsProfile.Restyled <- ItemsProfile.Restyled + 1
                    let scopes = StyleEngine.scopesAbove e
                    let found =
                        if deep then StyleEngine.applyDeep theme appStyles scopes e
                        else StyleEngine.apply theme appStyles scopes e
                    if found then ancestorStates <- true

    /// Matches the styles queued for `e` and anything inside it now; the rest of the queue waits
    /// for the next layout. ItemsView realizes items during layout, after this layout's restyle
    /// pass: it calls this so an item is styled before it's measured (DESIGN.md, "Recycling
    /// measured; styles on realization; new sources", decision 1).
    member internal this.RunStylesWithin(e: Element) =
        let mutable rounds = 0
        while rounds < 10 && restyleQueue.Exists(fun q -> e.Contains q) do
            rounds <- rounds + 1
            let mine = restyleQueue.FindAll(fun q -> e.Contains q).ToArray()
            restyleQueue.RemoveAll(fun q -> e.Contains q) |> ignore
            this.ApplyRestyles mine

    /// An element whose styles are still waiting to be matched (none right after a layout, unless
    /// something asked during the frame: ElementHost warns about those in debug builds).
    member internal _.FirstQueuedRestyle: Element voption =
        let mutable found = ValueNone
        let mutable i = 0
        while found.IsNone && i < restyleQueue.Count do
            if restyleQueue[i].IsRestyleQueued then found <- ValueSome restyleQueue[i]
            i <- i + 1
        found

    /// Runs what asked to run after the frame (effects, animations asking for the next frame).
    /// ElementHost calls it once a frame is drawn.
    member this.RunAfterFrame() =
        if afterFrame.Count > 0 then
            let actions = afterFrame.ToArray()
            afterFrame.Clear()
            for action in actions do
                try
                    action ()
                with e ->
                    (this :> IElementRoot).ReportError e

    /// Lays the tree out in `size` DIPs (after re-running components whose state changed). Only
    /// invalid parts do any work.
    /// True when the last layout threw (the host doesn't draw that frame).
    member internal _.LayoutFailed = layoutFailed

    member this.UpdateLayout(size: Size) =
        this.RunScheduledComponents()
        this.RunScheduledStyles()
        match root with
        | ValueSome r ->
            // Arranging can ask for another pass: a scroller that moved shows other items, which an
            // ItemsView realizes and measures. A few passes settle it (bounded, in case something
            // keeps asking).
            // A custom element's measure or arrange that throws leaves layout half done: the error is
            // reported and the host drops the frame (`LayoutFailed`); what failed stays invalid, so
            // the next frame tries again.
            layoutFailed <- false
            try
                let mutable passes = 0
                while passes = 0 || (passes < 6 && not (r.IsMeasureValid && r.IsArrangeValid)) do
                    passes <- passes + 1
                    r.Measure size
                    match this.Place with
                    | ValueSome place -> r.Arrange(place r size)
                    | ValueNone -> r.Arrange(Rect.Create(0.0, 0.0, size.Width, size.Height))
            with e ->
                layoutFailed <- true
                (this :> IElementRoot).ReportError e
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
                    let children = e.DrawOrder
                    let mutable found = ValueNone
                    let mutable i = children.Count - 1
                    while found.IsNone && i >= 0 do
                        found <- hit children[i] left top
                        i <- i - 1
                    if found.IsSome then found
                    elif e.ComponentState.IsSome || not e.Type.HitsSelf then ValueNone
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
        member _.ReportError(error) =
            match dispatcher with
            | ValueSome d -> d.ReportUiError error
            | ValueNone -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw error
        member _.AppValues = appValues
        member this.Focus(element) = this.FocusHandler(element :?> Element)
        member this.Watch(element) =
            this.WatchHandler(element :?> Element)
            onInvalidated ()
        member this.Owner = (match this.Owner with ValueSome o -> box o | ValueNone -> null)
        member this.TextInputArea(element, caret, keyboard, multiline) =
            this.TextArea <- ValueSome(struct (element :?> Element, caret, keyboard, multiline))
        member this.ClearComposition() = this.ClearCompositionHandler()
        member this.ElementLayer(element, content) = this.ElementLayerHandler (element :?> Element) content
        member _.Theme = theme |> ValueOption.map (fun t -> t :> IThemeSource)
        member this.RequestRestyle(element, deep, stateChange) =
            this.RestyleHandler(element :?> Element)
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

[<AutoOpen>]
module ApplicableStateExtensions =
    type Element with
        /// The states this element can be in to some effect, for tools such as the inspector: the built-in ones,
        /// and each custom one (`States.define`, `States.ofProp`) that something ties to it: its view switches
        /// it here (`Border.state chosen false` counts), a style reaching it gives it a look (its theme, the app,
        /// the scopes above it, its own styles and blocks: a CheckBox that isn't checked still has "checked"), or
        /// a tool has forced it. Having a property the state reads is not a tie: a state read from a Slider's
        /// value belongs to the sliders a style for it reaches, not to every Slider. Walks those styles, so not
        /// for every frame.
        member this.ApplicableStates: ElementState list =
            let custom = States.customStates ()
            if custom.Length = 0 then
                List.ofArray States.builtIn
            else
                let styled = System.Collections.Generic.HashSet<ElementState>(HashIdentity.Reference)
                // A rule that would match the element but for its states (and property tests) gives those states a look.
                let note (r: Rule) =
                    if not r.Selector.States.IsEmpty && Selector.matches { r.Selector with States = []; Props = [] } this then
                        for s in r.Selector.States do
                            styled.Add s |> ignore
                let noteAll (styles: Style list) =
                    for s in styles do
                        for r in s.Rules do
                            note r
                noteAll (StyleEngine.themeStyles (StyleEngine.treeTheme this) this)
                match this.Root with
                | ValueSome(:? ElementTree as tree) -> noteAll tree.Styles
                | _ -> ()
                for scope in StyleEngine.scopesAbove this do
                    noteAll scope
                noteAll (this.Get StyleProps.styles)
                for struct (p, v) in this.StyleSettings do
                    if p.IsStateBlock then
                        let rules = System.Collections.Generic.List<Rule>()
                        Rule.flatten Selector.Any "inline " [| struct (p, v) |] rules
                        for r in rules do
                            note r
                let forced = this.ForcedStates
                [ yield! States.builtIn
                  for s in custom do
                      // The view switches it on this element (a hidden property that is that state's alone).
                      let switchedHere =
                          match s.SwitchProp with
                          | ValueSome p -> this.HasOwnValue p
                          | ValueNone -> false
                      if switchedHere
                         || styled.Contains s
                         || forced |> List.exists (fun (f, _) -> obj.ReferenceEquals(f, s)) then
                          s ]
