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
    /// The UI thread's dispatcher, when the tree runs in an app (bare trees in tests have none).
    abstract Dispatcher: Dispatcher voption
    /// An element (an `Element`) asks for style matching: `deep` for its subtree too, `stateChange`
    /// when one of its states changed. The tree queues it for the start of the next layout (and
    /// ignores it while nothing in the tree uses styles).
    abstract RequestRestyle: element: obj * deep: bool * stateChange: bool -> unit
    /// Something in the tree uses styles (a `styles` setting or an inline state block).
    abstract StylesUsed: unit -> unit
    /// The tree's theme (`App.theme`), for inherited base values; elements can set their own.
    abstract Theme: IThemeSource voption
    /// The app's app values (`AppValue`): the host's, shared by its layers; a bare tree has its own.
    abstract AppValues: AppValues

[<AutoOpen>]
module internal StyleMarks =
    /// Put before a theme style's selector text by the style engine, so `Values` can say `Theme`.
    [<Literal>]
    let ThemeMark = "theme "

/// Where an element's property value comes from (`Element.Values`).
[<RequireQualifiedAccess>]
type ValueSource =
    /// A setting in the view the element was last built or patched from.
    | View
    /// Set from code (`element.Set`), not by the view.
    | Code
    /// A matching style of the app's or an element's (`PropertyValue.Selector` says which).
    | Style
    /// One of the theme's styles (`PropertyValue.Selector` says which: `Button:hover`).
    | Theme
    /// The element type's default (`Element.defaults`), e.g. Button's background.
    | TypeDefault
    /// Passed down from an ancestor or the theme (`foreground`, `font`, `fontSize`).
    | Inherited
    /// The property's own default.
    | Default

/// One property of an element: its key, its value and where the value comes from.
[<Struct>]
type PropertyValue =
    { Prop: Prop
      Value: obj
      Source: ValueSource
      /// For values from a style: its selector written out (`Button.primary:hover`); otherwise empty.
      Selector: string
      /// Set by a theme value (`Border.background Palette.surface`): its name; otherwise empty.
      ThemeValue: string }

/// One hook of a component (`Element.Hooks`), in the order the component calls them.
[<Struct>]
type HookValue =
    { /// "useState", "useElmish", "useMemo", "useEffect", "useSubscription", "useAnimated", "useAsync".
      Kind: string
      /// The current value: a state, a model, a memo, an animated value, a loaded result, an
      /// error. None for hooks that hold no value (effects, subscriptions) or don't have one yet.
      Value: obj voption
      /// For useAsync: "loading", "loaded" or "failed"; otherwise empty.
      Status: string }

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
      Transform: (RenderContext -> Transform) voption
      /// Draws on top of the children (a scrollbar), in the element's own coordinates.
      RenderAfter: (RenderContext -> DrawList -> unit) voption
      /// The type's own wheel behavior (scrolling): runs on each element of the route after that
      /// element's `onWheel` setting, if nothing handled the wheel yet.
      Wheel: (InputContext -> WheelEventArgs -> unit) voption
      /// The type's own touch-drag behavior (scrolling by finger): given how far the finger moved
      /// (x, y in DIPs), returns true if the element takes the drag. See `Element.pan`.
      Pan: (InputContext -> float -> float -> bool) voption
      /// The finger that was panning lifted (`false`) or the pan was canceled (`true`).
      PanEnded: (InputContext -> bool -> unit) voption
      /// The type's own pointer behavior (a scrollbar's thumb): runs as the event bubbles, after
      /// each element's own handler, if nothing handled it yet. Handling a press in it captures
      /// the pointer to this element (moves and the release come here).
      PointerDown: (InputContext -> PointerEventArgs -> unit) voption
      PointerMove: (InputContext -> PointerEventArgs -> unit) voption
      PointerUp: (InputContext -> PointerEventArgs -> unit) voption
      /// The type's own key behavior (a ScrollViewer's arrow keys): runs as a key bubbles from the
      /// focused element, after each element's own handler, if nothing handled it yet.
      KeyDown: (InputContext -> KeyEventArgs -> unit) voption
      /// The clip for the children, in the element's own coordinates (a ScrollViewer's viewport,
      /// without its scrollbars); by default its bounds when `clipToBounds` is set.
      ClipChildren: (RenderContext -> Rect) voption
      /// How it takes part in scrolling (ScrollViewer, ItemsView).
      Scrolling: ScrollHooks voption }

    override t.ToString() = t.Name

    /// Measures to the largest child, gives every child the whole element, and draws nothing.
    static member internal Default: ElementType =
        { Name = "Element"
          Defaults = [||]
          Transform = ValueNone
          RenderAfter = ValueNone
          Wheel = ValueNone
          Pan = ValueNone
          PanEnded = ValueNone
          PointerDown = ValueNone
          PointerMove = ValueNone
          PointerUp = ValueNone
          KeyDown = ValueNone
          ClipChildren = ValueNone
          Scrolling = ValueNone
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

/// How an element type takes part in scrolling. ScrollViewer and ItemsView set it.
and ScrollHooks =
    { /// Scrolls so that `rect` (the element's own coordinates) shows, as `align` says; returns
      /// how far that rect moved (x, y; the change in scroll offset).
      MakeVisible: Element -> Rect -> ScrollAlign -> bool -> Point
      /// What a scroller above shows of it changed (scrolled, resized), or it was arranged.
      ViewportChanged: Element -> unit
      /// Scrolls to its item or element with `key`; false if it has none.
      ScrollToKey: Element -> obj -> ScrollAlign -> bool -> bool }

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
    member internal _.Element = element

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
    /// The current frame's time.
    member _.Time = element.Time
    member internal _.Element = element

/// What a Render function can do: read properties, its size and its input states.
and [<Struct>] RenderContext internal (element: Element) =
    member _.Get<'T>(prop: Prop<'T>) : 'T = element.Get prop
    /// A theme value for drawing (`ctx.Theme Palette.accent`), from the nearest theme.
    member _.Theme<'T>(themeValue: ThemeValue<'T>) : 'T = unbox<'T> (element.ThemeValueOf themeValue)
    /// The element's size; draw within (0, 0)–(Width, Height).
    member _.Size: Size =
        let b: Rect = element.Bounds
        Size.Create(b.Width, b.Height)
    member _.Text: TextSystem = element.TextSystem
    /// The pointer is over the element (or something inside it).
    member _.IsPointerOver = element.IsPointerOver || element.HasForcedHoverInside
    /// The element got a left-button press that hasn't been released, and the pointer is over it.
    member _.IsPressed = element.IsPressed || element.LooksForced 2
    /// False when the element or one of its ancestors has `isEnabled false`.
    member _.IsEnabled = element.IsEnabled && not element.HasForcedDisabledAbove
    /// The element has keyboard focus (however it got it).
    member _.IsFocused = element.IsFocused || element.LooksForced 4
    /// The element has focus and the user is using the keyboard: draw a focus ring
    /// (`FocusRing.draw`). False after a pointer press and while the window is inactive.
    member _.IsFocusVisible = element.IsFocusVisible || element.LooksForced 8
    /// The current frame's time.
    member _.Time = element.Time
    member internal _.Element = element

    /// A value that moves to `target` over `duration` whenever the target changes (the first
    /// target is taken at once). Frames keep coming while it moves; nothing else re-runs.
    /// `key` tells this element's animated values apart: `ctx.Animate("press", ...)`.
    member _.Animate(key: string, target: float, duration: TimeSpan, ?easing: float -> float) : float =
        element.Animate(key, target, duration, defaultArg easing Easing.easeOut, Lerp.float)

    /// A color that fades to `target` over `duration` whenever the target changes.
    member _.Animate(key: string, target: Color, duration: TimeSpan, ?easing: float -> float) : Color =
        element.Animate(key, target, duration, defaultArg easing Easing.easeOut, Lerp.color)

/// What an element type's own input handling can do: read properties, keep private state, and
/// ask for a new arrange or a new frame.
and [<Struct>] InputContext internal (element: Element) =
    member _.Get<'T>(prop: Prop<'T>) : 'T = element.Get prop
    /// The element's size.
    member _.Size: Size =
        let b: Rect = element.Bounds
        Size.Create(b.Width, b.Height)
    /// Stores private data (a scroll offset) without invalidating anything; see `Prop.local`.
    member _.SetLocal<'T>(prop: Prop<'T>, value: 'T) = element.SetLocal(prop, value)
    /// Children need new positions (the scroll offset changed).
    member _.InvalidateArrange() = element.InvalidateArrange()
    /// Only appearance changed.
    member _.InvalidateVisual() = element.InvalidateVisual()
    /// Its size may change: measure again.
    member _.InvalidateMeasure() = element.InvalidateMeasure()
    /// The pointer is over the element (or something inside it).
    member _.IsPointerOver = element.IsPointerOver
    /// The current frame's time.
    member _.Time = element.Time
    /// Scrolls every scroller around the element so all of it shows (the least scroll).
    member _.BringIntoView() = element.BringIntoView()
    member internal _.Element = element

/// What a component's input carries (its function and props); equal inputs don't re-run it.
and [<AbstractClass>] internal ComponentInput() =
    /// Runs the component for `element` and returns its view.
    abstract Run: element: Element -> View

/// A component's state on its element: told when the element joins or leaves a tree.
and [<AbstractClass>] internal ComponentState() =
    abstract Name: string
    abstract Attached: root: IElementRoot -> unit
    abstract Detached: unit -> unit
    /// The hooks' current values (`now` for animated ones).
    abstract Hooks: now: TimeSpan -> HookValue list

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
    // Counts shared by all trees (forced states, the theme epoch) live in `ElementCounts`: a
    // `static let mutable` here would be shared across threads (see CLAUDE.md).
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
    // States a tool forced on (the inspector): ElementState values (declared after Element), and
    // the built-in ones as flags for RenderContext (1 hover, 2 pressed, 4 focus, 8 focus-visible,
    // 16 disabled). Looks only: input reads the real states.
    let mutable forced: obj list = []
    let mutable forcedLooks = 0
    /// Values animated by the render function (`ctx.Animate`), by key; created on first use.
    let mutable animations: Dictionary<string, obj> voption = ValueNone
    /// Running or finished transitions (`Style.transition`), by property id; created on first use.
    let mutable transitions: Dictionary<int, obj> voption = ValueNone
    /// Its nearest theme (`NearestTheme`) and the theme epoch it was found in (-1: not yet).
    let mutable nearestTheme: IThemeSource voption = ValueNone
    let mutable nearestThemeEpoch = -1
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

    let remove (i: int) =
        for j in i .. count - 2 do
            keys[j] <- keys[j + 1]
            values[j] <- values[j + 1]
        count <- count - 1
        keys[count] <- Unchecked.defaultof<Prop>
        values[count] <- null

    // Values from matching styles (between the element's own values and the defaults), with the
    // selector that set each one (for the inspector). Replaced as a whole by ApplyStyleValues.
    let mutable styleKeys: Prop[] = [||]
    let mutable styleValues: obj[] = [||]
    let mutable styleSelectors: string[] = [||]
    /// Queued for style matching (by the tree), and whether its subtree is too.
    let mutable restyleQueued = false
    let mutable restyleDeep = false

    let findStyle (id: int) =
        let mutable i = 0
        while i < styleKeys.Length && styleKeys[i].Id <> id do
            i <- i + 1
        if i < styleKeys.Length then i else -1

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
                    if not (newView.Has p) then this.ClearValue p
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

    /// Sets the tree for this element and everything below it. A subtree that joins a tree is
    /// queued for style matching.
    member internal this.SetRoot(r: IElementRoot voption) =
        Element.ThemesMoved()
        this.SetRootInSubtree r
        match r with
        | ValueSome added -> added.RequestRestyle(box this, true, false)
        | ValueNone -> ()

    member private this.SetRootInSubtree(r: IElementRoot voption) =
        let before = root
        root <- r
        restyleQueued <- false
        restyleDeep <- false
        match r with
        | ValueSome added when this.UsesStyles -> added.StylesUsed()
        | _ -> ()
        for child in children do
            child.SetRootInSubtree r
        match componentState, before, r with
        | ValueSome c, ValueSome _, ValueNone -> c.Detached()
        | ValueSome c, ValueNone, ValueSome added -> c.Attached added
        | _ -> ()

    /// Has a `styles` setting or an inline state block of its own.
    member private _.UsesStyles =
        let mutable uses = false
        for i in 0 .. count - 1 do
            if keys[i].IsStateBlock || keys[i].IsStyles then
                uses <- true
        uses

    // --- Components and animation ----------------------------------------------------------------

    static member internal ComponentInputProp: Prop<obj> = componentInput

    /// The state of the component this element hosts, if it hosts one.
    member internal _.ComponentState
        with get () = componentState
        and set v = componentState <- v

    /// The name of the component this element hosts (`Component.define "Name"`), if it hosts one.
    member _.ComponentName: string voption = componentState |> ValueOption.map _.Name

    // --- For tools (the inspector) ------------------------------------------------------------

    /// The element's property values and where each comes from: everything the view, code, styles or the
    /// element type set, then the common layout and input properties at their defaults. Private
    /// data (`Prop.local`) is left out. Children properties hold views (`View voption`, `View list`).
    member this.Values: PropertyValue list =
        let result = List<PropertyValue>()
        let seen = HashSet<int>()
        let typeHas (p: Prop) = elementType.Defaults |> Array.exists (fun struct (q, _) -> q.Id = p.Id)
        let shown (p: Prop) = not p.IsLocal && not p.IsStateBlock && p.Kind <> PropKind.Component && seen.Add p.Id
        let add (p: Prop) =
            let own = find p.Id
            let fromStyle = findStyle p.Id
            let value, source, selector =
                if own >= 0 then
                    let source =
                        match view with
                        | ValueSome vw when vw.Has p -> ValueSource.View
                        | _ -> ValueSource.Code
                    values[own], source, ""
                elif fromStyle >= 0 then
                    // The style engine marks the theme's selectors with "theme " (see StyleEngine.apply).
                    let s = styleSelectors[fromStyle]
                    if s.StartsWith(ThemeMark, StringComparison.Ordinal) then styleValues[fromStyle], ValueSource.Theme, s.Substring ThemeMark.Length
                    else styleValues[fromStyle], ValueSource.Style, s
                elif typeHas p then this.DefaultOf p, ValueSource.TypeDefault, ""
                elif p.Inherits then
                    let v = this.FromAbove p
                    v, (if p.Same(v, p.DefaultBoxed) then ValueSource.Default else ValueSource.Inherited), ""
                else p.DefaultBoxed, ValueSource.Default, ""
            let themeValue = match value with :? ThemeRef as r -> r.Key.Name | _ -> ""
            result.Add { Prop = p; Value = this.Resolve value; Source = source; Selector = selector; ThemeValue = themeValue }
        for i in 0 .. count - 1 do
            if shown keys[i] then add keys[i]
        for p in styleKeys do
            if shown p then add p
        for struct (p, _) in elementType.Defaults do
            if shown p then add p
        // Text settings passed down from above (only when something above sets them).
        for p in [ InheritedProps.foreground :> Prop; InheritedProps.font; InheritedProps.fontSize ] do
            if not (p.Same(this.FromAbove p, p.DefaultBoxed)) && shown p then add p
        for p in LayoutProps.common do
            if shown p then add p
        List.ofSeq result

    /// Its states changed in a way only it knows about (a custom state from `States.define`, such
    /// as M8's checked): styles that test states are matched again.
    member this.RefreshStates() = this.RequestRestyle(false, true)

    /// The space the last measure offered the element (margin included; may be infinite).
    member _.OfferedSize: Size = lastAvailable

    /// The hooks of the component this element hosts, in call order (empty for other elements).
    member this.Hooks: HookValue list =
        match componentState with
        | ValueSome c -> c.Hooks this.Time
        | ValueNone -> []

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

    /// The states forced on by a tool (`element.ForceState`), as ElementState values.
    member internal _.Forced = forced

    /// Replaces the forced states (and the built-in ones' flags); styles match again.
    member internal this.SetForced(states: obj list, looks: int) =
        let changed flag = (forcedLooks &&& flag) <> (looks &&& flag)
        // Elements are UI-thread-only, so plain counts do (the walks only run while one is forced).
        let delta flag = if not (changed flag) then 0 elif looks &&& flag <> 0 then 1 else -1
        ElementCounts.AddForced(delta 1, delta 16)
        let hoverChanged, disabledChanged = changed 1, changed 16
        forced <- states
        forcedLooks <- looks
        this.InvalidateVisual()
        // A forced disabled reaches everything inside (as `isEnabled false` does); a forced hover
        // reaches the ancestors (as the pointer over a child does): those match again too.
        this.RequestRestyle(disabledChanged, true)
        if hoverChanged then
            let mutable p = parent
            while p.IsSome do
                p.Value.RequestRestyle(false, true)
                p <- p.Value.Parent

    /// A built-in state forced on (see `forcedLooks`), for RenderContext.
    member internal _.LooksForced(flag: int) = forcedLooks &&& flag <> 0

    /// Looks hovered because of a forced hover on it or something inside it (the pointer over a
    /// child is over its ancestors too).
    member internal this.HasForcedHoverInside =
        ElementCounts.ForcedHovers > 0
        && (let rec any (e: Element) = e.LooksForced 1 || e.Children |> Seq.exists any
            any this)

    /// Looks disabled because of a forced disabled on it or an ancestor (as `isEnabled false`).
    member internal this.HasForcedDisabledAbove =
        ElementCounts.ForcedDisableds > 0
        && (let mutable e = ValueSome this
            let mutable found = false
            while not found && e.IsSome do
                found <- e.Value.LooksForced 16
                e <- e.Value.Parent
            found)

    member internal this.SetFocusState(isFocused: bool, isVisible: bool) =
        let before = this.IsFocused, this.IsFocusVisible
        focused <- isFocused
        focusVisible <- isVisible
        if before <> (this.IsFocused, this.IsFocusVisible) then
            this.InvalidateVisual()
            this.RequestRestyle(false, true)

    member internal this.SetKeyPressed(value: bool) =
        let before = this.IsPressed
        keyPressed <- value
        if before <> this.IsPressed then
            this.InvalidateVisual()
            this.RequestRestyle(false, true)

    member internal this.ChangePointersOver(delta: int) =
        let before = pointersOver > 0
        pointersOver <- max 0 (pointersOver + delta)
        if before <> (pointersOver > 0) then
            this.InvalidateVisual()
            this.RequestRestyle(false, true)

    member internal this.ChangePointersPressing(delta: int) =
        let before = pointersPressing > 0
        pointersPressing <- max 0 (pointersPressing + delta)
        if before <> (pointersPressing > 0) then
            this.InvalidateVisual()
            this.RequestRestyle(false, true)

    /// Replaces the children with elements the element type manages itself (an ItemsView's
    /// realized items), already attached; nothing is realized or patched here.
    member internal _.SetManagedChildren(next: Element[]) = children <- next

    /// Scrolls every scroller around this element so that `rect` (its own coordinates) shows,
    /// the innermost first.
    member internal this.BringRectIntoView(rect: Rect, align: ScrollAlign, animate: bool) =
        let mutable r = rect
        let mutable e = this
        let mutable p = parent
        while p.IsSome do
            let b = e.Bounds
            r <- r.Offset(b.X, b.Y)
            let a = p.Value
            match a.Type.Scrolling with
            | ValueSome s ->
                let moved = s.MakeVisible a r align animate
                r <- r.Offset(-moved.X, -moved.Y)
            | ValueNone -> ()
            e <- a
            p <- a.Parent

    /// Scrolls every scroller around this element so all of it shows (the least scroll, animated).
    member this.BringIntoView() =
        this.BringRectIntoView(Rect.Create(0.0, 0.0, bounds.Width, bounds.Height), ScrollAlign.Nearest, true)

    /// What scrollers above show of the elements inside changed: they're told.
    member internal _.ViewportChangedBelow() =
        for c in children do
            match c.Type.Scrolling with
            | ValueSome s -> s.ViewportChanged c
            | ValueNone -> ()
            c.ViewportChangedBelow()

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

    /// Its type's default for `prop`, if the type has one.
    member private _.TryTypeDefault(prop: Prop) : obj voption =
        let defaults = elementType.Defaults
        let mutable i = 0
        while i < defaults.Length && (let struct (p, _) = defaults[i] in p.Id <> prop.Id) do
            i <- i + 1
        if i < defaults.Length then (let struct (_, v) = defaults[i] in ValueSome v) else ValueNone

    /// The base value its own `theme` setting gives an inherited property, if any.
    member private _.OwnThemeBase(prop: Prop) : obj voption =
        match find LayoutProps.theme.Id with
        | -1 -> ValueNone
        | i ->
            match values[i] with
            | :? (IThemeSource voption) as t when t.IsSome -> t.Value.TryBase prop
            | _ -> ValueNone

    /// A theme value's value here: from the nearest theme (this element's or an ancestor's `theme`, else
    /// the tree's), else the theme value's default.
    member internal this.ThemeValueOf(themeValue: ThemeKey) : obj =
        match this.NearestTheme with
        | ValueSome t -> t.Resolve themeValue
        | ValueNone -> themeValue.DefaultBoxed

    /// The theme that decides its theme values: its own `theme`, else its parent's nearest, else the
    /// tree's. Remembered until something bumps the theme epoch (then found again, through the
    /// parent's remembered one, so siblings share the work).
    member internal this.NearestTheme: IThemeSource voption =
        let epoch = ElementCounts.ThemeEpoch
        if nearestThemeEpoch = epoch then nearestTheme
        else
            let found =
                match this.OwnTheme with
                | ValueSome t -> ValueSome t
                | ValueNone ->
                    match parent with
                    | ValueSome p -> p.NearestTheme
                    | ValueNone ->
                        match root with
                        | ValueSome r -> r.Theme
                        | ValueNone -> ValueNone
            nearestTheme <- found
            nearestThemeEpoch <- epoch
            found

    /// Something changed which theme is nearest for some elements: they all look again.
    static member internal ThemesMoved() = ElementCounts.BumpThemeEpoch()

    /// The tree that new elements are being made for, on this thread: components run the first
    /// time before their element joins the tree, and need it for app values.
    [<ThreadStatic; DefaultValue>]
    static val mutable private realizing: IElementRoot voption

    static member internal Realizing = Element.realizing

    /// Runs `make` with `root` as the tree being built for (when there is one; else the outer one stays).
    static member internal RealizingIn<'T>(root: IElementRoot voption, make: unit -> 'T) : 'T =
        if root.IsNone then make ()
        else
            let before = Element.realizing
            Element.realizing <- root
            try
                make ()
            finally
                Element.realizing <- before

    /// Its own `theme` setting, if any.
    member internal _.OwnTheme: IThemeSource voption =
        match find LayoutProps.theme.Id with
        | -1 -> ValueNone
        | i ->
            match values[i] with
            | :? (IThemeSource voption) as t -> t
            | _ -> ValueNone

    /// A stored value as read: a theme value reference becomes the nearest theme's value.
    member private this.Resolve(value: obj) : obj =
        match value with
        | :? ThemeRef as r -> r.Apply(this.ThemeValueOf r.Key)
        | v -> v

    /// An inherited property's value from above: its own theme's base value, else its parent's
    /// value, else the tree's theme's, else the property's default.
    member private this.FromAbove(prop: Prop) : obj =
        match this.OwnThemeBase prop with
        | ValueSome v -> v
        | ValueNone ->
            match parent with
            | ValueSome p -> p.Resolved prop
            | ValueNone ->
                match root with
                | ValueSome r when r.Theme.IsSome ->
                    match r.Theme.Value.TryBase prop with
                    | ValueSome v -> v
                    | ValueNone -> prop.DefaultBoxed
                | _ -> prop.DefaultBoxed

    /// An inherited property's value here: own, else a style's, else the type's default, else from above.
    member internal this.Resolved(prop: Prop) : obj =
        match find prop.Id with
        | -1 -> this.Resolve(this.BaseOf prop)
        | i -> this.Resolve values[i]

    /// The value without a setting of its own: a matching style's, else the type's default, else
    /// (inherited properties) the value from above, else the property's default.
    member private this.BaseOf(prop: Prop) : obj =
        match findStyle prop.Id with
        | -1 ->
            if prop.Inherits then
                match this.TryTypeDefault prop with
                | ValueSome v -> v
                | ValueNone -> this.FromAbove prop
            else this.DefaultOf prop
        | i -> styleValues[i]

    /// True when the element decides `prop` itself (own value, a style's or its type's default), so
    /// a change above doesn't reach it or anything inside it.
    member private this.HoldsInherited(prop: Prop) =
        find prop.Id >= 0 || findStyle prop.Id >= 0 || (this.TryTypeDefault prop).IsSome || (this.OwnThemeBase prop).IsSome

    /// An inherited property's value changed here: redo what it affects for every element inside
    /// that takes it from here.
    member private this.InheritedChanged(prop: Prop) =
        for c in children do
            if not (c.HoldsInherited prop) then
                c.Invalidate prop.Affects
                c.InheritedChanged prop

    /// Redoes what changing a value with this effect needs.
    member private this.Invalidate(affects: Affects) =
        match affects with
        | Affects.Measure -> this.InvalidateMeasure()
        | Affects.Arrange -> this.InvalidateArrange()
        | Affects.Render -> this.InvalidateVisual()
        | _ -> ()

    /// The theme here changed (its own `theme` setting, or the tree's): values given by theme values may
    /// differ everywhere below, fonts and sizes included. Transitions start over at the new values
    /// (switching themes doesn't fade).
    member internal this.ThemeChanged() =
        Element.ThemesMoved()
        transitions <- ValueNone
        this.InvalidateMeasure()
        this.InvalidateVisual()
        for c in children do
            c.ThemeChanged()

    /// Every inherited property may have changed here and below (a theme came or went).
    member private this.AllInheritedChanged() =
        for p in [ InheritedProps.font :> Prop; InheritedProps.fontSize; InheritedProps.foreground ] do
            this.Invalidate p.Affects
            this.InheritedChanged p

    /// The value shown now: while a transition (`Style.transition`) runs, between the old value and `value`.
    member private this.Transitioned<'T>(prop: Prop<'T>, value: 'T) : 'T =
        match this.Get prop.TransitionProp with
        | ValueNone -> value
        | ValueSome duration ->
            let all =
                match transitions with
                | ValueSome d -> d
                | ValueNone ->
                    let d = Dictionary<int, obj>()
                    transitions <- ValueSome d
                    d
            let tween =
                match all.TryGetValue prop.Id with
                | true, (:? Tween<'T> as t) -> t
                | _ ->
                    let lerp = prop.Lerp.Value
                    let t = Tween<'T>((fun a b x -> unbox<'T> (lerp (box a) (box b) x)), value)
                    all[prop.Id] <- box t
                    t
            let now = this.Time
            let shown = tween.Update(value, now, duration, Easing.easeOut)
            if tween.IsMoving now then
                match root with
                | ValueSome r -> r.AfterFrame(fun () -> this.InvalidateVisual())
                | ValueNone -> ()
            shown

    /// The value without styles: its own, else the default. What property selectors test, so a
    /// style can't change whether it (or another style) matches.
    member internal this.UnstyledValue(prop: Prop) : obj =
        match find prop.Id with
        | -1 -> this.DefaultOf prop
        | i -> values[i]

    /// A style selector tests `prop` and its unstyled value went from `before` to `after`.
    member private this.TestedValueChanged(prop: Prop, before: obj, after: obj) =
        if prop.TestedByStyles && not (prop.Same(before, after)) then
            this.RequestRestyle(prop.TestedByAncestorStyles, false)

    /// The value without any transition easing it (the value it's heading to).
    member private this.Untransitioned<'T>(prop: Prop<'T>) : 'T =
        match find prop.Id with
        | -1 ->
            if styleKeys.Length > 0 || prop.Inherits then unbox<'T> (this.Resolve(this.BaseOf prop))
            elif elementType.Defaults.Length = 0 then prop.Default
            else unbox<'T> (this.Resolve(this.DefaultOf prop))
        | i -> unbox<'T> (this.Resolve values[i])

    member this.Get<'T>(prop: Prop<'T>) : 'T =
        let value = this.Untransitioned prop
        if prop.Transition.IsSome then this.Transitioned(prop, value) else value

    /// The current value, boxed: the element's own, else a style's, else the default (a theme's
    /// value for a theme value).
    member this.GetBoxed(prop: Prop) : obj =
        match find prop.Id with
        | -1 -> this.Resolve(this.BaseOf prop)
        | i -> this.Resolve values[i]

    member this.Set<'k>(setting: Setting<'k>) = this.SetValue(setting.Prop, setting.Value)
    member this.Set<'T>(prop: Prop<'T>, value: 'T) = this.SetValue(prop, box value)
    /// Takes away the element's own value: a matching style's shows, else the default (the element
    /// type's, else the property's).
    member this.Reset(prop: Prop) = this.ClearValue prop

    member internal _.SetLocal<'T>(prop: Prop<'T>, value: 'T) = store prop (box value)

    /// Redoes what a value change affects (layout copy, invalidation, style matching).
    member private this.ValueChanged(prop: Prop) =
        if prop.IsLayout then this.RefreshLayoutValues()
        this.Invalidate prop.Affects
        if prop.Inherits then this.InheritedChanged prop
        if prop.IsTheme then
            this.AllInheritedChanged()
            this.ThemeChanged()
        if prop.IsStyles || prop.IsStateBlock || prop.IsTheme then
            match root with
            | ValueSome r -> r.StylesUsed()
            | ValueNone -> ()
        if prop.Restyles || prop.IsStyles || prop.IsTheme then this.RequestRestyle(true, false)
        elif prop.IsStateBlock then this.RequestRestyle(false, false)

    /// Removes the element's own value (a setting the view dropped, or Reset).
    member internal this.ClearValue(prop: Prop) =
        if prop.Kind <> PropKind.Value then this.SetValue(prop, this.DefaultOf prop)
        else
            match find prop.Id with
            | -1 -> ()
            | i ->
                this.VerifyAccess()
                let old = values[i]
                remove i
                this.TestedValueChanged(prop, old, this.DefaultOf prop)
                if not (prop.Same(this.Resolve old, this.Resolve(this.BaseOf prop))) then this.ValueChanged prop
                elif prop.IsStateBlock then this.RequestRestyle(false, false)

    // --- Styles ----------------------------------------------------------------------------------

    /// Asks the tree to match styles again for this element (and its subtree when `deep`).
    member internal this.RequestRestyle(deep: bool, stateChange: bool) =
        match root with
        | ValueSome r -> r.RequestRestyle(box this, deep, stateChange)
        | ValueNone -> ()

    /// The tree accepted a restyle request: remember it. Returns true when the element wasn't
    /// queued yet (the tree then adds it to its queue).
    member internal _.MarkRestyle(deep: bool) =
        let wasQueued = restyleQueued
        restyleQueued <- true
        restyleDeep <- restyleDeep || deep
        not wasQueued

    /// Takes the queued request: (queued, deep).
    member internal _.TakeRestyle() =
        let r = struct (restyleQueued, restyleDeep)
        restyleQueued <- false
        restyleDeep <- false
        r

    /// The element's own values that are part of its styles: its `styles` list and inline blocks.
    member internal _.StyleSettings: struct (Prop * obj)[] =
        [| for i in 0 .. count - 1 do
               if keys[i].IsStyles || keys[i].IsStateBlock then struct (keys[i], values[i]) |]

    /// Replaces the values from matching styles; whatever changes for this element is redone.
    member internal this.ApplyStyleValues(newKeys: Prop[], newValues: obj[], newSelectors: string[]) =
        let oldKeys, oldValues = styleKeys, styleValues
        let changed = List<Prop>()
        // What shows without a style value.
        let fallback (p: Prop) =
            if p.Inherits then
                match this.TryTypeDefault p with
                | ValueSome v -> v
                | ValueNone -> this.FromAbove p
            else this.DefaultOf p
        let check (p: Prop) =
            // The element's own value hides any style value.
            if find p.Id = -1 && not (changed.Contains p) then
                let before =
                    match Array.tryFindIndex (fun (k: Prop) -> k.Id = p.Id) oldKeys with
                    | Some i -> oldValues[i]
                    | None -> fallback p
                let after =
                    match Array.tryFindIndex (fun (k: Prop) -> k.Id = p.Id) newKeys with
                    | Some i -> newValues[i]
                    | None -> fallback p
                if not (p.Same(before, after)) then changed.Add p
        for p in oldKeys do check p
        for p in newKeys do check p
        styleKeys <- newKeys
        styleValues <- newValues
        styleSelectors <- newSelectors
        if changed.Count > 0 then
            if changed |> Seq.exists _.IsLayout then this.RefreshLayoutValues()
            let mutable affects = Affects.Nothing
            for p in changed do
                affects <- affects ||| p.Affects
            if affects.HasFlag Affects.Measure then this.InvalidateMeasure()
            elif affects.HasFlag Affects.Arrange then this.InvalidateArrange()
            elif affects.HasFlag Affects.Render then this.InvalidateVisual()
            for p in changed do
                if p.Inherits then this.InheritedChanged p

    member internal this.SetValue(prop: Prop, value: obj) =
        this.VerifyAccess()
        if prop.Kind = PropKind.Value then
            // Always stored (even when equal to a style's value): the element's own value wins over styles.
            let old = this.GetBoxed prop
            if prop.TestedByStyles then this.TestedValueChanged(prop, this.UnstyledValue prop, value)
            store prop value
            // Compared as read (a theme value by its value here), so setting the same again is no change.
            if not (prop.Same(old, this.Resolve value)) then this.ValueChanged prop
        else
            let old =
                match find prop.Id with
                | -1 -> this.DefaultOf prop
                | i -> values[i]
            if not (prop.Same(old, value)) then
                if prop.Kind = PropKind.Component then
                    store prop value
                    this.RunComponent()
                else
                    let current =
                        match childSlots |> Array.tryFind (fun struct (p, _) -> obj.ReferenceEquals(p, prop)) with
                        | Some(struct (_, es)) -> es
                        | None -> [||]
                    let next = Element.RealizingIn(root, fun () -> Element.Reconcile(current, viewsOf prop.Kind value))
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

    member internal _.SetParent(p: Element voption) =
        parent <- p
        Element.ThemesMoved()

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
                let before = bounds
                bounds <- Rect.FromEdges(Element.RoundToPixels(x, s), Element.RoundToPixels(y, s), Element.RoundToPixels(x + w, s), Element.RoundToPixels(y + h, s))
                // Children sit relative to this element, and its arrange stage sees only its size:
                // moved but not resized (scrolled, say), with nothing else changed, its children
                // stay where they are and the stage isn't run again.
                if not (arrangeValid && bounds.Width = before.Width && bounds.Height = before.Height) then
                    elementType.Arrange (ArrangeContext this) (Size.Create(bounds.Width, bounds.Height))
            arrangeValid <- true

    member this.Render(list: DrawList) =
        // Skipped when it can't show (scrolled out of a ScrollViewer, outside a clip): its bounds,
        // grown by what elements draw outside themselves (shadows, focus rings), lie outside the
        // clip. Always drawn when it grows (scale above 1, or easing back from it) or has its own
        // transform. The clip test comes first and the scale is read plainly after it (no
        // transition lookup unless it has a running one): most elements in a long scrolled list
        // are off screen, and this is all they cost.
        let canShow () =
            list.CanShow(bounds.Inflate Element.RenderOverflow)
            || elementType.Transform.IsSome
            || this.Untransitioned LayoutProps.scale > 1.0
            || (match transitions with
                | ValueSome d when d.ContainsKey LayoutProps.scale.Id -> this.Get LayoutProps.scale > 1.0
                | _ -> false)
        if not bounds.IsEmpty && lv.IsVisible && canShow () then
            let scale = this.Get LayoutProps.scale
            list.PushTranslate(bounds.X, bounds.Y)
            let opacity = this.Get LayoutProps.opacity
            if opacity < 1.0 then list.PushOpacity opacity
            if scale <> 1.0 then list.PushTransform(Transform.ScaleAround(scale, bounds.Width / 2.0, bounds.Height / 2.0))
            let transformed =
                match elementType.Transform with
                | ValueSome f ->
                    list.PushTransform(f (RenderContext this))
                    true
                | ValueNone -> false
            elementType.Render (RenderContext this) list
            if children.Length > 0 then
                let clip = lv.ClipToBounds
                if clip then
                    match elementType.ClipChildren with
                    | ValueSome f -> list.PushClip(f (RenderContext this))
                    | ValueNone -> list.PushClip(Rect.Create(0.0, 0.0, bounds.Width, bounds.Height))
                for child in children do
                    child.Render list
                if clip then list.PopClip()
            match elementType.RenderAfter with
            | ValueSome f -> f (RenderContext this) list
            | ValueNone -> ()
            if transformed then list.PopTransform()
            if scale <> 1.0 then list.PopTransform()
            if opacity < 1.0 then list.PopOpacity()
            list.PopTransform()

    /// How far outside its bounds an element may draw (DIPs): shadows and focus rings. One whose
    /// bounds, grown by this, lie outside the clip isn't drawn.
    static member val RenderOverflow = 64.0 with get, set

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

    /// Draws on top of the children (a scrollbar, an overlay), in the element's own coordinates.
    let renderAfter (f: RenderContext -> DrawList -> unit) = ElementPart(fun t -> { t with RenderAfter = ValueSome f })

    /// The type's own wheel behavior, such as scrolling. It runs as the wheel event bubbles, after
    /// each element's own `onWheel` setting, so a view's handler can take the wheel first; call
    /// `e.Handle()` when the element used it (an outer scroller then stays put).
    let wheel (f: InputContext -> WheelEventArgs -> unit) = ElementPart(fun t -> { t with Wheel = ValueSome f })

    /// The type's own touch-drag behavior, such as scrolling. Once a finger pressed on (or inside)
    /// the element moves past a small threshold, this gets the movement so far and then every
    /// further move (x, y in DIPs; positive is right and down). Return true when the element takes
    /// the drag (a scroller: it has something to scroll that way, even if it's already at the end):
    /// it then keeps the finger until it lifts, the press underneath is canceled (no click), and
    /// what the finger slides over inside it doesn't react. Return false to leave the drag alone
    /// (to an outer element, or to stay a press). Later calls' results don't matter.
    /// Not used when a view's `onPointerDown` handled the press, and never for the mouse.
    let pan (f: InputContext -> float -> float -> bool) = ElementPart(fun t -> { t with Pan = ValueSome f })

    /// Runs when the finger that was panning (see `pan`) lifts (`false`) or the pan is canceled
    /// (`true`): a scroller flings on.
    let panEnded (f: InputContext -> bool -> unit) = ElementPart(fun t -> { t with PanEnded = ValueSome f })

    /// The type's own press behavior (a scrollbar thumb): runs as the press bubbles, after each
    /// element's own `onPointerDown`, if nothing handled it yet. Call `e.Handle()` to take the
    /// press: the pointer is then captured to this element (`pointerMove` and `pointerUp` get the
    /// rest of the drag) and nothing clicks.
    let pointerDown (f: InputContext -> PointerEventArgs -> unit) = ElementPart(fun t -> { t with PointerDown = ValueSome f })

    /// The type's own pointer-move behavior (after each element's `onPointerMove`).
    let pointerMove (f: InputContext -> PointerEventArgs -> unit) = ElementPart(fun t -> { t with PointerMove = ValueSome f })

    /// The type's own release behavior (after each element's `onPointerUp`).
    let pointerUp (f: InputContext -> PointerEventArgs -> unit) = ElementPart(fun t -> { t with PointerUp = ValueSome f })

    /// The type's own key behavior: keys nothing handled bubble from the focused element up, and
    /// reach this after each element's own `onKeyDown`. Call `e.Handle()` when it used the key.
    let keyDown (f: InputContext -> KeyEventArgs -> unit) = ElementPart(fun t -> { t with KeyDown = ValueSome f })

    /// The clip for the children (own coordinates) when `clipToBounds` is set, instead of the
    /// element's bounds.
    let clipChildren (f: RenderContext -> Rect) = ElementPart(fun t -> { t with ClipChildren = ValueSome f })

    /// Scrolling hooks (ScrollViewer, ItemsView).
    let internal scrolling (hooks: ScrollHooks) = ElementPart(fun t -> { t with Scrolling = ValueSome hooks })

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
