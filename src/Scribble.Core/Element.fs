namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Rendering
open Scribble.Text

/// What elements need from the tree they're attached to.
type IElementRoot =
    abstract Text: TextSystem
    /// Physical pixels per DIP; layout rounds to this grid.
    abstract Scale: float
    /// Something changed that needs a new frame.
    abstract Invalidated: unit -> unit
    /// Throws if called from a thread other than the UI thread (debug builds only).
    abstract VerifyAccess: unit -> unit
    /// The current frame's time, for animations.
    abstract Time: TimeSpan
    /// Runs `run` at the start of the next layout, shallowest first (components whose state
    /// changed), and asks for a frame.
    abstract ScheduleComponent: depth: int * run: (unit -> unit) -> unit
    /// Runs `action` after the current frame is drawn (effects; "keep animating" requests).
    abstract AfterFrame: action: (unit -> unit) -> unit

/// What kind of element something is: its name, three functions and its default settings. Measure
/// says how big the content wants to be, Arrange places the children, Render draws the element.
/// Margin, explicit sizes, min/max, alignment, visibility, clipping, pixel rounding and caching are
/// done by the engine around these functions, so they only deal with content.
///
/// Made with `Element.define` or `Element.extend` (see the Element module below).
type ElementType =
    { Name: string
      /// Content size wanted, given the available space (margin and explicit sizes already
      /// applied; may be infinite). Measure children here.
      Measure: MeasureContext -> Size -> Size
      /// Place every child within the element's final size (coordinates relative to the element).
      Arrange: ArrangeContext -> Size -> unit
      /// Draw the element in its own coordinates, (0, 0) at its top-left. Children draw after.
      Render: RenderContext -> DrawList -> unit
      /// Values used where an element of this type has no setting of its own (before the
      /// property's own default).
      Defaults: struct (Prop * obj)[]
      /// A visual-only transform around the element and its children, in the element's own
      /// coordinates (layout and hit testing ignore it).
      Transform: (RenderContext -> Transform) voption }

    override t.ToString() = t.Name

    /// Measures to the largest child, gives every child the whole element, and draws nothing.
    static member internal Default: ElementType =
        { Name = "Element"
          Defaults = [||]
          Transform = ValueNone
          Measure =
            fun ctx available ->
                let mutable w, h = 0.0, 0.0
                for child in ctx.Children do
                    let d = ctx.Measure(child, available)
                    w <- max w d.Width
                    h <- max h d.Height
                Size.Create(w, h)
          Arrange =
            fun ctx size ->
                for child in ctx.Children do
                    ctx.Arrange(child, Rect.Create(0.0, 0.0, size.Width, size.Height))
          Render = fun _ _ -> () }

/// What a Measure function can do: read properties, measure children, use text.
and [<Struct>] MeasureContext internal (element: Element) =
    member _.Get<'T>(prop: Prop<'T>) : 'T = element.Get prop
    member _.Children: IReadOnlyList<Element> = element.Children
    /// Measures a child and returns its desired size (margin included).
    member _.Measure(child: Element, available: Size) : Size =
        child.Measure available
        child.DesiredSize
    member _.Text: TextSystem = element.TextSystem
    member _.Scale = element.Scale
    /// Stores private data (a cache) without invalidating anything; see `Prop.local`.
    member _.SetLocal<'T>(prop: Prop<'T>, value: 'T) = element.SetLocal(prop, value)

/// What an Arrange function can do: read properties and place children.
and [<Struct>] ArrangeContext internal (element: Element) =
    member _.Get<'T>(prop: Prop<'T>) : 'T = element.Get prop
    member _.Children: IReadOnlyList<Element> = element.Children
    /// The size the child asked for in the last measure (margin included).
    member _.DesiredSize(child: Element) : Size = child.DesiredSize
    /// Places a child in `slot` (relative to this element); the child applies its own margin and alignment.
    member _.Arrange(child: Element, slot: Rect) = child.Arrange slot
    member _.Text: TextSystem = element.TextSystem
    member _.SetLocal<'T>(prop: Prop<'T>, value: 'T) = element.SetLocal(prop, value)

/// What a Render function can do: read properties, its size and its input states.
and [<Struct>] RenderContext internal (element: Element) =
    member _.Get<'T>(prop: Prop<'T>) : 'T = element.Get prop
    /// The element's size; draw within (0, 0)–(Width, Height).
    member _.Size: Size =
        let b: Rect = element.Bounds
        Size.Create(b.Width, b.Height)
    member _.Text: TextSystem = element.TextSystem
    /// The pointer is over the element (or something inside it).
    member _.IsPointerOver = element.IsPointerOver
    /// The element got a left-button press that hasn't been released, and the pointer is over it.
    member _.IsPressed = element.IsPressed
    /// False when the element or one of its ancestors has `isEnabled false`.
    member _.IsEnabled = element.IsEnabled
    /// The element has keyboard focus (however it got it).
    member _.IsFocused = element.IsFocused
    /// The element has focus and the user is using the keyboard: draw a focus ring
    /// (`FocusRing.draw`). False after a pointer press and while the window is inactive.
    member _.IsFocusVisible = element.IsFocusVisible
    /// The current frame's time.
    member _.Time = element.Time

    /// A value that moves to `target` over `duration` whenever the target changes (the first
    /// target is taken at once). Frames keep coming while it moves; nothing else re-runs.
    /// `key` tells this element's animated values apart: `ctx.Animate("press", ...)`.
    member _.Animate(key: string, target: float, duration: TimeSpan, ?easing: float -> float) : float =
        element.Animate(key, target, duration, defaultArg easing Easing.easeOut, Lerp.float)

    /// A color that fades to `target` over `duration` whenever the target changes.
    member _.Animate(key: string, target: Color, duration: TimeSpan, ?easing: float -> float) : Color =
        element.Animate(key, target, duration, defaultArg easing Easing.easeOut, Lerp.color)

/// What a component's input carries (its function and props); equal inputs don't re-run it.
and [<AbstractClass>] internal ComponentInput() =
    /// Runs the component for `element` and returns its view.
    abstract Run: element: Element -> View

/// A component's state on its element: told when the element joins or leaves a tree.
and [<AbstractClass>] internal ComponentState() =
    abstract Attached: root: IElementRoot -> unit
    abstract Detached: unit -> unit

/// A description of an element: its type, its settings and (inside the settings) its children's
/// descriptions. Cheap to make: views are what `Border.create [ ... ]` returns, and what view
/// functions build again on every update. Scribble turns them into elements, and on later updates
/// changes only what differs (see ElementTree.Update).
and View internal (elementType: ElementType, settings: struct (Prop * obj)[]) =
    let key =
        let mutable k = ValueNone
        for struct (p, v) in settings do
            if obj.ReferenceEquals(p, LayoutProps.key) then k <- unbox<obj voption> v
        k

    member _.Type = elementType
    member internal _.Settings = settings
    /// The key setting, if any.
    member _.Key: obj voption = key

    member internal _.Has(prop: Prop) =
        let mutable found = false
        for struct (p, _) in settings do
            if obj.ReferenceEquals(p, prop) then found <- true
        found

    /// The same view with a key (used by `items`).
    member internal _.WithKey(k: obj) = View(elementType, Array.append settings [| struct (LayoutProps.key :> Prop, box (ValueSome k)) |])

    /// A view of an element type with settings. Element authors call this from their `create`.
    static member Create<'k>(elementType: ElementType, settings: Setting<'k> list) : View =
        View(elementType, [| for s in settings -> struct (s.Prop, s.Value) |])

    override _.ToString() = elementType.Name

/// An element in the tree: what a View becomes. Layout state lives here, out of sight of element
/// types. Change one with `Set` (the next update from a view wins again).
and [<Sealed>] Element internal (elementType: ElementType) =
    static let empty: Element[] = [||]
    static let clamp (value: float) (low: float) (high: float) = max low (min value high)
    /// A component's input (set by its view) and its rendered content (the view it returned).
    static let componentInput = Prop<obj>("Component", obj (), Affects.Nothing, PropKind.Component)
    static let componentContent = Prop<View voption>("Content", ValueNone, Affects.Measure, PropKind.Child)

    // Property values: a small list of (property, value), searched linearly.
    let mutable keys: Prop[] = Array.zeroCreate 4
    let mutable values: obj[] = Array.zeroCreate 4
    let mutable count = 0

    let mutable parent: Element voption = ValueNone
    /// The tree this element is in (kept up to date for the whole subtree on attach and detach).
    let mutable root: IElementRoot voption = ValueNone
    let mutable lv: LayoutValues =
        { Margin = LayoutProps.margin.Default; Width = LayoutProps.width.Default; Height = LayoutProps.height.Default
          MinWidth = LayoutProps.minWidth.Default; MinHeight = LayoutProps.minHeight.Default
          MaxWidth = LayoutProps.maxWidth.Default; MaxHeight = LayoutProps.maxHeight.Default
          HorizontalAlignment = LayoutProps.horizontalAlignment.Default; VerticalAlignment = LayoutProps.verticalAlignment.Default
          IsVisible = LayoutProps.isVisible.Default; ClipToBounds = LayoutProps.clipToBounds.Default }
    /// Sizes from the last measure, in DIPs (NaN = automatic).
    let mutable resolved: ResolvedSizes =
        { Width = Double.NaN; Height = Double.NaN; MinWidth = 0.0; MinHeight = 0.0
          MaxWidth = Double.PositiveInfinity; MaxHeight = Double.PositiveInfinity }
    let mutable children: Element[] = empty
    /// The realized children of each child property, in the order the properties were set.
    let mutable childSlots: struct (Prop * Element[])[] = [||]
    /// The view this element was last built or patched from.
    let mutable view: View voption = ValueNone

    let mutable measureValid = false
    let mutable arrangeValid = false
    let mutable lastAvailable = Size.Zero
    let mutable lastSlot = Rect.Create(0.0, 0.0, 0.0, 0.0)
    let mutable desiredSize = Size.Zero
    let mutable bounds = Rect.Create(0.0, 0.0, 0.0, 0.0)

    // Input states, kept by the input router: how many pointers are over the element, and how many
    // hold a press on it while over it.
    let mutable pointersOver = 0
    let mutable pointersPressing = 0
    // Kept by the focus manager.
    let mutable focused = false
    let mutable focusVisible = false
    /// Space is held on the focused element (it looks pressed until Space comes up).
    let mutable keyPressed = false
    /// Values animated by the render function (`ctx.Animate`), by key; created on first use.
    let mutable animations: Dictionary<string, obj> voption = ValueNone
    let mutable componentState: ComponentState voption = ValueNone

    let find (id: int) =
        let mutable i = 0
        while i < count && keys[i].Id <> id do
            i <- i + 1
        if i < count then i else -1

    let store (prop: Prop) (value: obj) =
        match find prop.Id with
        | -1 ->
            if count = keys.Length then
                Array.Resize(&keys, count * 2)
                Array.Resize(&values, count * 2)
            keys[count] <- prop
            values[count] <- value
            count <- count + 1
        | i -> values[i] <- value

    static let viewsOf (kind: PropKind) (value: obj) : View[] =
        match kind, value with
        | PropKind.Child, (:? (View voption) as v) -> (match v with ValueSome x -> [| x |] | ValueNone -> [||])
        | PropKind.Children, (:? (View list) as l) -> List.toArray l
        | _ -> [||]

    /// Builds an element (and its children) from a view.
    static member internal Realize(v: View) : Element =
        let e = Element(v.Type)
        // The type's defaults may include layout properties (Button aligns left).
        if v.Type.Defaults.Length > 0 then e.RefreshLayoutValues()
        e.Patch v
        e

    /// Matches new child views against the current child elements: by key where a view has one,
    /// otherwise by position among the unkeyed ones. A match of the same type is patched and kept;
    /// anything else is built new.
    static member private Reconcile(current: Element[], views: View[]) : Element[] =
        if views.Length = 0 then [||]
        else
            let keyed = Dictionary<obj, Element>()
            let unkeyed = Queue<Element>()
            for e in current do
                match e.CurrentView with
                | ValueSome(v: View) when v.Key.IsSome -> keyed[v.Key.Value] <- e
                | _ -> unkeyed.Enqueue e
            [| for v in views ->
                   let candidate =
                       match v.Key with
                       | ValueSome k ->
                           match keyed.TryGetValue k with
                           | true, e ->
                               keyed.Remove k |> ignore
                               ValueSome e
                           | _ -> ValueNone
                       | ValueNone -> if unkeyed.Count > 0 then ValueSome(unkeyed.Dequeue()) else ValueNone
                   match candidate with
                   | ValueSome(e: Element) when obj.ReferenceEquals(e.Type, v.Type) ->
                       e.Patch v
                       e
                   | _ -> Element.Realize v |]

    /// The view this element was last built or patched from.
    member _.CurrentView: View voption = view

    /// Brings the element in line with a new view of the same type: changed settings are set,
    /// settings the new view doesn't have go back to their defaults, children are reconciled.
    /// An identical view object is skipped entirely.
    member internal this.Patch(newView: View) : unit =
        match view with
        | ValueSome old when obj.ReferenceEquals(old, newView) -> ()
        | old ->
            view <- ValueSome newView
            for struct (p, v) in newView.Settings do
                this.SetValue(p, v)
            match old with
            | ValueSome o ->
                for struct (p, _) in o.Settings do
                    if not (newView.Has p) then this.SetValue(p, this.DefaultOf p)
            | ValueNone -> ()

    member _.Type = elementType
    member this.Name = this.Get LayoutProps.name
    member _.Parent = parent
    /// Child elements in drawing order, whichever properties they came from.
    member _.Children: IReadOnlyList<Element> = children
    /// Size wanted after the last measure, margin included.
    member _.DesiredSize: Size = desiredSize
    /// Position and size after the last arrange, relative to the parent (margin excluded).
    member _.Bounds: Rect = bounds
    member _.IsMeasureValid = measureValid
    member _.IsArrangeValid = arrangeValid

    /// The tree this element belongs to, if any.
    member _.Root: IElementRoot voption = root

    /// Sets the tree for this element and everything below it.
    member internal _.SetRoot(r: IElementRoot voption) =
        let before = root
        root <- r
        for child in children do
            child.SetRoot r
        match componentState, before, r with
        | ValueSome c, ValueSome _, ValueNone -> c.Detached()
        | ValueSome c, ValueNone, ValueSome added -> c.Attached added
        | _ -> ()

    // --- Components and animation ----------------------------------------------------------------

    static member internal ComponentInputProp: Prop<obj> = componentInput

    /// The state of the component this element hosts, if it hosts one.
    member internal _.ComponentState
        with get () = componentState
        and set v = componentState <- v

    /// Runs the hosted component and patches what it returned in as this element's child.
    member internal this.RunComponent() =
        match find componentInput.Id with
        | -1 -> ()
        | i ->
            let input = values[i] :?> ComponentInput
            let view = input.Run this
            this.SetValue(componentContent, box (ValueSome view))

    member internal _.Time =
        match root with
        | ValueSome r -> r.Time
        | ValueNone -> TimeSpan.Zero

    member internal this.Animate<'T>(key: string, target: 'T, duration: TimeSpan, easing: float -> float, lerp: 'T -> 'T -> float -> 'T) : 'T =
        let all =
            match animations with
            | ValueSome d -> d
            | ValueNone ->
                let d = Dictionary<string, obj>()
                animations <- ValueSome d
                d
        let tween =
            match all.TryGetValue key with
            | true, (:? Tween<'T> as t) -> t
            | _ ->
                let t = Tween<'T>(lerp, target)
                all[key] <- box t
                t
        let now = this.Time
        let value = tween.Update(target, now, duration, easing)
        if tween.IsMoving now then
            match root with
            | ValueSome r -> r.AfterFrame(fun () -> this.InvalidateVisual())
            | ValueNone -> ()
        value

    member _.IsVisible = lv.IsVisible

    /// False when this element or one of its ancestors has `isEnabled false`.
    member this.IsEnabled =
        this.Get LayoutProps.isEnabled
        && match parent with
           | ValueSome p -> p.IsEnabled
           | ValueNone -> true

    /// The pointer is over this element or something inside it.
    member _.IsPointerOver = pointersOver > 0
    /// A left-button press on this element hasn't been released and the pointer is over it, or
    /// Space is held while it has focus.
    member _.IsPressed = pointersPressing > 0 || keyPressed
    /// Has keyboard focus.
    member _.IsFocused = focused
    /// Has focus and should show it (the keyboard is in use and the window is active).
    member _.IsFocusVisible = focused && focusVisible

    member internal this.SetFocusState(isFocused: bool, isVisible: bool) =
        let before = this.IsFocused, this.IsFocusVisible
        focused <- isFocused
        focusVisible <- isVisible
        if before <> (this.IsFocused, this.IsFocusVisible) then this.InvalidateVisual()

    member internal this.SetKeyPressed(value: bool) =
        let before = this.IsPressed
        keyPressed <- value
        if before <> this.IsPressed then this.InvalidateVisual()

    member internal this.ChangePointersOver(delta: int) =
        let before = pointersOver > 0
        pointersOver <- max 0 (pointersOver + delta)
        if before <> (pointersOver > 0) then this.InvalidateVisual()

    member internal this.ChangePointersPressing(delta: int) =
        let before = pointersPressing > 0
        pointersPressing <- max 0 (pointersPressing + delta)
        if before <> (pointersPressing > 0) then this.InvalidateVisual()

    /// The element's top-left in tree coordinates (DIPs), after the last arrange.
    member _.PositionInTree: Point =
        let mutable x, y = bounds.X, bounds.Y
        let mutable p = parent
        while p.IsSome do
            let b = p.Value.Bounds
            x <- x + b.X
            y <- y + b.Y
            p <- p.Value.Parent
        Point(x, y)

    /// True when `other` is this element or inside it.
    member this.Contains(other: Element) =
        let mutable e = ValueSome other
        let mutable found = false
        while not found && e.IsSome do
            if obj.ReferenceEquals(e.Value, this) then found <- true
            e <- e.Value.Parent
        found

    /// The width and height as set (fast; for panels that look at their children's lengths).
    member _.RequestedWidth: Length = lv.Width
    member _.RequestedHeight: Length = lv.Height

    member internal this.TextSystem =
        match this.Root with
        | ValueSome r -> r.Text
        | ValueNone -> invalidOp $"A {elementType.Name} needs to be in an element tree to lay out text."

    member internal this.Scale =
        match this.Root with
        | ValueSome r -> r.Scale
        | ValueNone -> 1.0

    member this.VerifyAccess() =
        match this.Root with
        | ValueSome r -> r.VerifyAccess()
        | ValueNone -> ()

    // --- Properties ------------------------------------------------------------------------------

    /// The value this element has without a setting of its own: its type's default, else the property's.
    member _.DefaultOf(prop: Prop) : obj =
        let defaults = elementType.Defaults
        let mutable i = 0
        while i < defaults.Length && (let struct (p, _) = defaults[i] in p.Id <> prop.Id) do
            i <- i + 1
        if i < defaults.Length then (let struct (_, v) = defaults[i] in v) else prop.DefaultBoxed

    member this.Get<'T>(prop: Prop<'T>) : 'T =
        match find prop.Id with
        | -1 -> if elementType.Defaults.Length = 0 then prop.Default else unbox<'T> (this.DefaultOf prop)
        | i -> unbox<'T> values[i]

    /// The value as stored (or the default), for tools such as the differ and the inspector.
    member this.GetBoxed(prop: Prop) : obj =
        match find prop.Id with
        | -1 -> this.DefaultOf prop
        | i -> values[i]

    member this.Set<'k>(setting: Setting<'k>) = this.SetValue(setting.Prop, setting.Value)
    member this.Set<'T>(prop: Prop<'T>, value: 'T) = this.SetValue(prop, box value)
    /// Back to the default value (the element type's, else the property's).
    member this.Reset(prop: Prop) = this.SetValue(prop, this.DefaultOf prop)

    member internal _.SetLocal<'T>(prop: Prop<'T>, value: 'T) = store prop (box value)

    member internal this.SetValue(prop: Prop, value: obj) =
        this.VerifyAccess()
        let old =
            match find prop.Id with
            | -1 -> this.DefaultOf prop
            | i -> values[i]
        if not (prop.Same(old, value)) then
            if prop.Kind = PropKind.Component then
                store prop value
                this.RunComponent()
            elif prop.Kind <> PropKind.Value then
                let current =
                    match childSlots |> Array.tryFind (fun struct (p, _) -> obj.ReferenceEquals(p, prop)) with
                    | Some(struct (_, es)) -> es
                    | None -> [||]
                let next = Element.Reconcile(current, viewsOf prop.Kind value)
                let kept = HashSet<Element>(next, HashIdentity.Reference)
                for e in current do
                    if not (kept.Contains e) then
                        e.SetParent ValueNone
                        e.SetRoot ValueNone
                for e in next do
                    if e.Parent.IsNone then
                        e.SetParent(ValueSome this)
                        e.SetRoot root
                        e.InvalidateAll()
                store prop value
                // The same elements in the same order (each patched in place): nothing to re-lay out
                // at this level; patched children invalidated themselves as needed.
                let unchanged = current.Length = next.Length && Array.forall2 (fun a b -> obj.ReferenceEquals(a, b)) current next
                if not unchanged then
                    childSlots <-
                        if childSlots |> Array.exists (fun struct (p, _) -> obj.ReferenceEquals(p, prop)) then
                            childSlots |> Array.map (fun struct (p, es) -> if obj.ReferenceEquals(p, prop) then struct (p, next) else struct (p, es))
                        else
                            Array.append childSlots [| struct (prop, next) |]
                    this.RebuildChildren()
                    match prop.Affects with
                    | Affects.Measure -> this.InvalidateMeasure()
                    | Affects.Arrange -> this.InvalidateArrange()
                    | Affects.Render -> this.InvalidateVisual()
                    | _ -> ()
            else
                store prop value
                if prop.IsLayout then this.RefreshLayoutValues()
                match prop.Affects with
                | Affects.Measure -> this.InvalidateMeasure()
                | Affects.Arrange -> this.InvalidateArrange()
                | Affects.Render -> this.InvalidateVisual()
                | _ -> ()

    member internal _.SetParent(p: Element voption) = parent <- p

    member internal this.RefreshLayoutValues() =
        lv <-
            { Margin = this.Get LayoutProps.margin; Width = this.Get LayoutProps.width; Height = this.Get LayoutProps.height
              MinWidth = this.Get LayoutProps.minWidth; MinHeight = this.Get LayoutProps.minHeight
              MaxWidth = this.Get LayoutProps.maxWidth; MaxHeight = this.Get LayoutProps.maxHeight
              HorizontalAlignment = this.Get LayoutProps.horizontalAlignment; VerticalAlignment = this.Get LayoutProps.verticalAlignment
              IsVisible = this.Get LayoutProps.isVisible; ClipToBounds = this.Get LayoutProps.clipToBounds }

    member private _.RebuildChildren() =
        children <- childSlots |> Array.collect (fun struct (_, es) -> es)

    // --- Invalidation ----------------------------------------------------------------------------

    member private this.NotifyRoot() =
        match root with
        | ValueSome r -> r.Invalidated()
        | ValueNone -> ()

    /// Size may have changed: measure this element and its ancestors again.
    member this.InvalidateMeasure() =
        if measureValid || arrangeValid then
            measureValid <- false
            arrangeValid <- false
            match parent with
            | ValueSome p -> p.InvalidateMeasure()
            | ValueNone -> this.NotifyRoot()
        elif parent.IsNone then
            this.NotifyRoot()

    /// Children may need new positions (no new measure).
    member this.InvalidateArrange() =
        if arrangeValid then
            arrangeValid <- false
            match parent with
            | ValueSome p -> p.InvalidateArrange()
            | ValueNone -> this.NotifyRoot()
        elif parent.IsNone then
            this.NotifyRoot()

    /// Only appearance changed: draw a new frame.
    member this.InvalidateVisual() =
        match this.Root with
        | ValueSome r -> r.Invalidated()
        | ValueNone -> ()

    /// Forgets cached layout in this subtree (after a scale change, or when re-attached).
    member this.InvalidateAll() =
        measureValid <- false
        arrangeValid <- false
        for child in children do
            child.InvalidateAll()

    // --- Layout ----------------------------------------------------------------------------------

    static member private CeilToPixels(v: float, s: float) =
        if Double.IsInfinity v || Double.IsNaN v then v else Math.Ceiling(v * s - 1e-6) / s

    static member private RoundToPixels(v: float, s: float) = Math.Round(v * s, MidpointRounding.AwayFromZero) / s

    /// A width or height in DIPs for a given base size (what the parent offers, margin included),
    /// or NaN for "automatic". Percent of an unlimited base is automatic too.
    static member private Resolve(length: Length, baseSize: float) =
        match length with
        | Px v -> v
        | Percent p when not (Double.IsInfinity baseSize) -> baseSize * p / 100.0
        | _ -> Double.NaN

    static member private OrDefault(value: float, fallback: float) = if Double.IsNaN value then fallback else value

    member this.Measure(available: Size) =
        if not (measureValid && available = lastAvailable) then
            lastAvailable <- available
            if not lv.IsVisible then
                desiredSize <- Size.Zero
            else
                let margin = lv.Margin
                // Percentages resolve against what the parent offers; Arrange reuses these values.
                resolved <-
                    { Width = Element.Resolve(lv.Width, available.Width)
                      Height = Element.Resolve(lv.Height, available.Height)
                      MinWidth = Element.OrDefault(Element.Resolve(lv.MinWidth, available.Width), 0.0)
                      MinHeight = Element.OrDefault(Element.Resolve(lv.MinHeight, available.Height), 0.0)
                      MaxWidth = Element.OrDefault(Element.Resolve(lv.MaxWidth, available.Width), Double.PositiveInfinity)
                      MaxHeight = Element.OrDefault(Element.Resolve(lv.MaxHeight, available.Height), Double.PositiveInfinity) }
                let r = resolved
                let scale = this.Scale
                let constrain (avail: float) (fixedSize: float) low high =
                    if Double.IsNaN fixedSize then clamp avail low high else clamp fixedSize low high
                let inner =
                    Size.Create(
                        constrain (max 0.0 (available.Width - margin.Horizontal)) r.Width r.MinWidth r.MaxWidth,
                        constrain (max 0.0 (available.Height - margin.Vertical)) r.Height r.MinHeight r.MaxHeight
                    )
                let content = elementType.Measure (MeasureContext this) inner
                let size (measured: float) (fixedSize: float) low high =
                    clamp (if Double.IsNaN fixedSize then measured else fixedSize) low high
                desiredSize <-
                    Size.Create(
                        Element.CeilToPixels(size content.Width r.Width r.MinWidth r.MaxWidth + margin.Horizontal, scale),
                        Element.CeilToPixels(size content.Height r.Height r.MinHeight r.MaxHeight + margin.Vertical, scale)
                    )
            measureValid <- true
            arrangeValid <- false

    /// Places the element in `slot` (relative to the parent): margin, alignment and size limits
    /// apply, and the edges are rounded to whole physical pixels. A Star size stretches.
    member this.Arrange(slot: Rect) =
        if not (arrangeValid && slot = lastSlot) then
            lastSlot <- slot
            if not lv.IsVisible then
                bounds <- Rect.Create(slot.X, slot.Y, 0.0, 0.0)
            else
                let margin = lv.Margin
                let r = resolved
                let hAlign, vAlign = lv.HorizontalAlignment, lv.VerticalAlignment
                let starW = match lv.Width with Star _ -> true | _ -> false
                let starH = match lv.Height with Star _ -> true | _ -> false
                let s = this.Scale
                let availW = max 0.0 (slot.Width - margin.Horizontal)
                let availH = max 0.0 (slot.Height - margin.Vertical)
                let wantW = max 0.0 (desiredSize.Width - margin.Horizontal)
                let wantH = max 0.0 (desiredSize.Height - margin.Vertical)
                let w =
                    if not (Double.IsNaN r.Width) then clamp r.Width r.MinWidth r.MaxWidth
                    elif starW || hAlign = HorizontalAlignment.Stretch then clamp availW r.MinWidth r.MaxWidth
                    else clamp wantW r.MinWidth r.MaxWidth
                let h =
                    if not (Double.IsNaN r.Height) then clamp r.Height r.MinHeight r.MaxHeight
                    elif starH || vAlign = VerticalAlignment.Stretch then clamp availH r.MinHeight r.MaxHeight
                    else clamp wantH r.MinHeight r.MaxHeight
                let x =
                    slot.X + margin.Left
                    + match hAlign with
                      | HorizontalAlignment.Left -> 0.0
                      | HorizontalAlignment.Right -> availW - w
                      | HorizontalAlignment.Center
                      | HorizontalAlignment.Stretch -> (availW - w) / 2.0
                let y =
                    slot.Y + margin.Top
                    + match vAlign with
                      | VerticalAlignment.Top -> 0.0
                      | VerticalAlignment.Bottom -> availH - h
                      | VerticalAlignment.Center
                      | VerticalAlignment.Stretch -> (availH - h) / 2.0
                // Round the edges, not the size, so neighbours never overlap or leave gaps.
                bounds <- Rect.FromEdges(Element.RoundToPixels(x, s), Element.RoundToPixels(y, s), Element.RoundToPixels(x + w, s), Element.RoundToPixels(y + h, s))
                elementType.Arrange (ArrangeContext this) (Size.Create(bounds.Width, bounds.Height))
            arrangeValid <- true

    member this.Render(list: DrawList) =
        if not bounds.IsEmpty && lv.IsVisible then
            list.PushTranslate(bounds.X, bounds.Y)
            let transformed =
                match elementType.Transform with
                | ValueSome f ->
                    list.PushTransform(f (RenderContext this))
                    true
                | ValueNone -> false
            elementType.Render (RenderContext this) list
            if children.Length > 0 then
                let clip = lv.ClipToBounds
                if clip then list.PushClip(Rect.Create(0.0, 0.0, bounds.Width, bounds.Height))
                for child in children do
                    child.Render list
                if clip then list.PopClip()
            if transformed then list.PopTransform()
            list.PopTransform()

    override _.ToString() = elementType.Name

/// Properties that hold child elements.
[<AutoOpen>]
module PropChildren =
    type Prop with
        /// Holds one child (a view; the element keeps the realized child).
        static member child(name: string) = Prop<View voption>(name, ValueNone, Affects.Measure, PropKind.Child)
        /// Holds a list of children (views; the element keeps the realized children).
        static member children(name: string) = Prop<View list>(name, [], Affects.Measure, PropKind.Children)

/// One part of an element definition, like a setting is one part of a view: `Element.measure`,
/// `Element.arrange`, `Element.render`, `Element.defaults`.
[<Struct>]
type ElementPart = internal ElementPart of (ElementType -> ElementType)

/// Defines element types, settings-list style:
///
///     let Type =
///         Element.define "ProgressBar" [
///             Element.measure (fun ctx available -> Size.Create(0.0, 8.0))
///             Element.render (fun ctx list -> ...)
///         ]
///
/// Define each type once, as a module-level value: the differ recognizes a type by reference.
module Element =
    let private build (start: ElementType) (name: string) (parts: ElementPart list) =
        let mutable t = { start with Name = name }
        for ElementPart apply in parts do
            t <- apply t
        t

    /// A new element type. What it leaves out works like a plain container: it measures to its
    /// largest child, gives every child the whole element, and draws nothing.
    let define (name: string) (parts: ElementPart list) : ElementType = build ElementType.Default name parts

    /// A new element type that starts as a copy of `baseType` (its measure, arrange, render and
    /// defaults); the parts replace or add to them.
    let extend (baseType: ElementType) (name: string) (parts: ElementPart list) : ElementType = build baseType name parts

    /// How big the content wants to be, given the available space (margin and explicit sizes
    /// already applied; may be infinite). Measure children here with `ctx.Measure`.
    let measure (f: MeasureContext -> Size -> Size) = ElementPart(fun t -> { t with Measure = f })

    /// Places every child within the element's final size, with `ctx.Arrange` (coordinates
    /// relative to the element).
    let arrange (f: ArrangeContext -> Size -> unit) = ElementPart(fun t -> { t with Arrange = f })

    /// Draws the element in its own coordinates, (0, 0) at its top-left. Children draw after.
    let render (f: RenderContext -> DrawList -> unit) = ElementPart(fun t -> { t with Render = f })

    /// A visual-only transform around the element and its children (it changes how they're drawn,
    /// not their layout or where they take input), in the element's own coordinates. For effects
    /// like a pressed button sinking: see `Transform.ScaleAround`.
    let transform (f: RenderContext -> Transform) = ElementPart(fun t -> { t with Transform = ValueSome f })

    /// Values for this element type's properties when a view doesn't set them (a view's own
    /// settings win; removing one from a later view goes back to these). Not for child properties.
    let defaults (settings: Setting<'k> list) =
        for s in settings do
            if s.Prop.Kind <> PropKind.Value then
                invalidArg "settings" $"'{s.Prop.Name}' holds children; element defaults can only be plain values."
        ElementPart(fun t ->
            let replaced = settings |> List.map (fun s -> s.Prop.Id) |> set
            let kept = t.Defaults |> Array.filter (fun struct (p, _) -> not (replaced.Contains p.Id))
            { t with Defaults = Array.append kept [| for s in settings -> struct (s.Prop, s.Value) |] })
