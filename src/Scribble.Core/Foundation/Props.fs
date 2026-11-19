namespace Scribble.Core

open System
open System.Collections.Generic
open System.Threading

/// What a property change makes the engine redo. Each level includes the ones below it:
/// a new size (Measure) also needs new positions (Arrange) and a new frame (Render).
[<Flags>]
type Affects =
    /// Nothing at all: private per-element data such as caches.
    | Nothing = 0
    /// Only the element's appearance.
    | Render = 1
    /// The position of children (and appearance).
    | Arrange = 3
    /// The element's size (and everything else).
    | Measure = 7

type internal PropKind =
    | Value = 0
    /// Holds `View voption`: one view, realized as the element's one child (`content`).
    | Content = 1
    /// Holds `Element list`: any number of children.
    | Children = 2
    /// Holds a component's input (its function and props): a change runs the component.
    | Component = 3

/// How values of the types transitions can animate blend (`a` at 0, `b` at 1), boxed.
module internal Interpolation =
    let private lerp (a: float) (b: float) (t: float) = a + (b - a) * t

    let color (a: Scribble.Rendering.Color) (b: Scribble.Rendering.Color) (t: float) =
        let t = float32 t
        Scribble.Rendering.Color.Rgba(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t)

    let private shadow (a: BoxShadow) (b: BoxShadow) (t: float) =
        { Blur = lerp a.Blur b.Blur t
          OffsetX = lerp a.OffsetX b.OffsetX t
          OffsetY = lerp a.OffsetY b.OffsetY t
          Spread = lerp a.Spread b.Spread t
          Color = color a.Color b.Color t }

    /// The blend for values of type `t`, if transitions can animate it.
    let forType (t: Type) : (obj -> obj -> float -> obj) voption =
        if t = typeof<float> then ValueSome(fun a b x -> box (lerp (unbox a) (unbox b) x))
        elif t = typeof<Scribble.Rendering.Color> then ValueSome(fun a b x -> box (color (unbox a) (unbox b) x))
        elif t = typeof<Scribble.Rendering.CornerRadius> then
            ValueSome(fun a b x ->
                let a, b = unbox<Scribble.Rendering.CornerRadius> a, unbox<Scribble.Rendering.CornerRadius> b
                let r: Scribble.Rendering.CornerRadius =
                    { TopLeft = lerp a.TopLeft b.TopLeft x
                      TopRight = lerp a.TopRight b.TopRight x
                      BottomRight = lerp a.BottomRight b.BottomRight x
                      BottomLeft = lerp a.BottomLeft b.BottomLeft x }
                box r)
        elif t = typeof<BoxShadow voption> then
            // A missing shadow blends as the other one, fully transparent.
            ValueSome(fun a b x ->
                match unbox<BoxShadow voption> a, unbox<BoxShadow voption> b with
                | ValueSome s, ValueSome u -> box (ValueSome(shadow s u x))
                | ValueSome s, ValueNone -> box (ValueSome(shadow s { s with Color = s.Color.WithAlpha 0.0f } x))
                | ValueNone, ValueSome u -> box (ValueSome(shadow { u with Color = u.Color.WithAlpha 0.0f } u x))
                | ValueNone, ValueNone -> box (ValueNone: BoxShadow voption))
        else ValueNone

/// Id counters as plain static fields: a `static let` takes part in F#'s file initialization, which
/// failed ("static data accessed recursively") when several threads first used the type at once.
type internal Ids private () =
    [<DefaultValue>] static val mutable private themeKey: int
    [<DefaultValue>] static val mutable private prop: int
    [<DefaultValue>] static val mutable private appValue: int
    static member NextAppValue() = lock typeof<Ids> (fun () -> Ids.appValue <- Ids.appValue + 1; Ids.appValue)
    static member NextThemeKey() = lock typeof<Ids> (fun () -> Ids.themeKey <- Ids.themeKey + 1; Ids.themeKey)
    static member NextProp() = lock typeof<Ids> (fun () -> Ids.prop <- Ids.prop + 1; Ids.prop)

/// Where ItemsView's time goes, for `tools/Scribble.Bench` (Stopwatch ticks and counts). Off unless
/// the benchmark sets `On`; then only one thread may use elements. Costs one static bool read per
/// realized item while off.
type internal ItemsProfile private () =
    [<DefaultValue>] static val mutable private on: bool
    [<DefaultValue>] static val mutable private noPool: bool
    static member On with get () = ItemsProfile.on and set v = ItemsProfile.on <- v
    /// True: recycled elements are dropped instead of pooled (to compare reuse with creating).
    static member NoPool with get () = ItemsProfile.noPool and set v = ItemsProfile.noPool <- v
    [<DefaultValue>] static val mutable private viewTicks: int64
    [<DefaultValue>] static val mutable private attachTicks: int64
    [<DefaultValue>] static val mutable private patchTicks: int64
    [<DefaultValue>] static val mutable private createTicks: int64
    [<DefaultValue>] static val mutable private measureTicks: int64
    [<DefaultValue>] static val mutable private releaseTicks: int64
    [<DefaultValue>] static val mutable private rangeTicks: int64
    [<DefaultValue>] static val mutable private restyleTicks: int64
    [<DefaultValue>] static val mutable private reused: int
    [<DefaultValue>] static val mutable private created: int
    [<DefaultValue>] static val mutable private repatched: int
    [<DefaultValue>] static val mutable private released: int
    [<DefaultValue>] static val mutable private themeWalks: int
    [<DefaultValue>] static val mutable private restyled: int
    /// Elements whose styles were matched again (each queued element; deep ones count once).
    static member Restyled with get () = ItemsProfile.restyled and set v = ItemsProfile.restyled <- v
    static member View with get () = ItemsProfile.viewTicks and set v = ItemsProfile.viewTicks <- v
    static member Attach with get () = ItemsProfile.attachTicks and set v = ItemsProfile.attachTicks <- v
    static member Patch with get () = ItemsProfile.patchTicks and set v = ItemsProfile.patchTicks <- v
    static member Create with get () = ItemsProfile.createTicks and set v = ItemsProfile.createTicks <- v
    static member Measure with get () = ItemsProfile.measureTicks and set v = ItemsProfile.measureTicks <- v
    static member Release with get () = ItemsProfile.releaseTicks and set v = ItemsProfile.releaseTicks <- v
    /// All of `realizeRange` (the parts above plus its own bookkeeping).
    static member Range with get () = ItemsProfile.rangeTicks and set v = ItemsProfile.rangeTicks <- v
    static member Restyle with get () = ItemsProfile.restyleTicks and set v = ItemsProfile.restyleTicks <- v
    static member Reused with get () = ItemsProfile.reused and set v = ItemsProfile.reused <- v
    static member Created with get () = ItemsProfile.created and set v = ItemsProfile.created <- v
    /// Realized items patched again because the items changed (same key, new version).
    static member Repatched with get () = ItemsProfile.repatched and set v = ItemsProfile.repatched <- v
    static member Released with get () = ItemsProfile.released and set v = ItemsProfile.released <- v
    /// Nearest-theme lookups that had to walk up (their remembered one was from an older epoch).
    static member ThemeWalks with get () = ItemsProfile.themeWalks and set v = ItemsProfile.themeWalks <- v
    static member Reset() =
        ItemsProfile.View <- 0L; ItemsProfile.Attach <- 0L; ItemsProfile.Patch <- 0L; ItemsProfile.Create <- 0L
        ItemsProfile.Measure <- 0L; ItemsProfile.Release <- 0L; ItemsProfile.Range <- 0L; ItemsProfile.Restyle <- 0L
        ItemsProfile.Reused <- 0; ItemsProfile.Created <- 0; ItemsProfile.Repatched <- 0; ItemsProfile.Released <- 0
        ItemsProfile.ThemeWalks <- 0; ItemsProfile.Restyled <- 0

/// Counts shared by every element tree on every thread (plain static fields, for the same reason as
/// `Ids`; changed under a lock, read without one: an int read is atomic, and a stale read only costs
/// a walk or a theme lookup).
type internal ElementCounts private () =
    /// Elements anywhere with a forced hover / disabled (`ForceState`): while none do, the walks
    /// that spread them are skipped.
    [<DefaultValue>] static val mutable private forcedHovers: int
    [<DefaultValue>] static val mutable private forcedDisableds: int
    /// Bumped by whatever can change an element's nearest theme; a theme an element remembered
    /// from an older epoch is looked up again.
    [<DefaultValue>] static val mutable private themeEpoch: int
    static member ForcedHovers = ElementCounts.forcedHovers
    static member ForcedDisableds = ElementCounts.forcedDisableds
    static member ThemeEpoch = ElementCounts.themeEpoch
    static member AddForced(hovers: int, disableds: int) =
        if hovers <> 0 || disableds <> 0 then
            lock typeof<ElementCounts> (fun () ->
                ElementCounts.forcedHovers <- ElementCounts.forcedHovers + hovers
                ElementCounts.forcedDisableds <- ElementCounts.forcedDisableds + disableds)
    static member BumpThemeEpoch() =
        lock typeof<ElementCounts> (fun () -> ElementCounts.themeEpoch <- ElementCounts.themeEpoch + 1)

/// Warnings for app authors in Debug builds (written to the debugger), each said once per process:
/// a mistake that repeats every update would otherwise flood the output. Fields as in `Ids`.
type internal DebugWarnings private () =
    [<DefaultValue>] static val mutable private said: System.Collections.Generic.HashSet<string>
    /// Tests listen here (warnings are said once per process, so a test uses its own keys).
    [<DefaultValue>] static val mutable private listener: Action<string>
    static member Listener
        with get () = DebugWarnings.listener
        and set (v: Action<string>) = lock typeof<DebugWarnings> (fun () -> DebugWarnings.listener <- v)
    /// Writes `message` once for `key` (callers check `#if DEBUG`).
    static member Once(key: string, message: string) =
        lock typeof<DebugWarnings> (fun () ->
            if isNull DebugWarnings.said then DebugWarnings.said <- System.Collections.Generic.HashSet()
            if DebugWarnings.said.Add key then
                Diagnostics.Debug.WriteLine("Scribble: " + message)
                match DebugWarnings.listener with
                | null -> ()
                | l -> l.Invoke message)

/// A named value a theme gives (`ThemeValue<'T>`; the built-in ones are in `Palette`).
[<AbstractClass>]
type ThemeKey internal (name: string, defaultBoxed: obj) =
    let id = Ids.NextThemeKey()
    member _.Name = name
    member internal _.Id = id
    member internal _.DefaultBoxed = defaultBoxed
    override _.ToString() = name

/// A value a theme gives, by name: `Palette.surface`, or an app's own made with
/// `ThemeValue.define "CardLine" (Color.Hex 0x34345A)`. Settings take one in place of a value
/// (`Border.background Palette.surface`) and read it from the nearest theme; a theme sets it with
/// `Theme.set`. Define theme values once, as module-level values.
[<Sealed>]
type ThemeValue<'T> internal (name: string, defaultValue: 'T) =
    inherit ThemeKey(name, box defaultValue)
    /// Used when the theme doesn't set it, and when there is no theme.
    member _.Default = defaultValue

module ThemeValue =
    /// A value themes can set (`Theme.set theme value v`), with the value used when one doesn't.
    let define (name: string) (defaultValue: 'T) = ThemeValue<'T>(name, defaultValue)

/// What a setting stores for a theme value: the theme value and, when the property's type differs from the
/// theme value's (a shadow stored as `BoxShadow voption`), a fixed conversion. Equal when both are the
/// same, so a view that sets the same theme value again changes nothing.
[<Sealed; AllowNullLiteral>]
type internal ThemeRef(themeValue: ThemeKey, convert: (obj -> obj) voption) =
    member _.Key = themeValue
    member _.Convert = convert
    member _.Apply(value: obj) = match convert with ValueSome f -> f value | ValueNone -> value
    override _.Equals(other: obj) =
        match other with
        | :? ThemeRef as r ->
            obj.ReferenceEquals(r.Key, themeValue)
            && (match r.Convert, convert with
                | ValueSome a, ValueSome b -> obj.ReferenceEquals(a, b)
                | ValueNone, ValueNone -> true
                | _ -> false)
        | _ -> false
    override _.GetHashCode() = themeValue.Id

/// A value an element tells when it takes it (`Prop.Tracked`): a `Capture` learns its element this way.
type internal ITrackedValue =
    abstract Taken: element: obj -> unit

/// A property key: a name, a default value and what changing it affects. Elements store values
/// for the properties set on them; everything else reads as the default.
///
/// Define properties once, as module-level values, with `Prop.define` (see Element.fs).
[<AbstractClass>]
type Prop internal (name: string, affects: Affects, kind: PropKind, defaultBoxed: obj, same: obj -> obj -> bool, lerp: (obj -> obj -> float -> obj) voption, valueType: Type) =
    let id = Ids.NextProp()
    // A handler: its value is a function, or an optional one (`onClick`). Styles can't set handlers (they have no model).
    let isHandler =
        let isFunction (t: Type) = t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<obj -> obj>
        isFunction valueType
        || (valueType.IsGenericType
            && (valueType.GetGenericTypeDefinition() = typedefof<obj voption> || valueType.GetGenericTypeDefinition() = typedefof<obj option>)
            && isFunction (valueType.GetGenericArguments()[0]))
    // This property's transition duration, as a property of its own (`Style.transition`), made on first use.
    let mutable transition: Prop<TimeSpan voption> voption = ValueNone

    member _.Name = name
    member _.Affects = affects
    member internal _.Id = id
    member internal _.Kind = kind
    member internal _.DefaultBoxed = defaultBoxed
    /// One of LayoutProps: the engine keeps a typed copy for fast layout.
    member val internal IsLayout = false with get, set
    /// Made with `Prop.local`: private per-element data, left out of `Element.Values`.
    member val internal IsLocal = false with get, set
    /// Changing it can change which styles match the element (and everything inside it).
    member val internal Restyles = false with get, set
    /// Its tree hears when an element's own value changes or an element with one joins (`IElementRoot.Watch`): a
    /// flyout (`flyout`).
    member val internal Watched = false with get, set
    /// Its value is told which element took it (`ITrackedValue`): when an element's own value changes, or an element
    /// with one joins a tree. `capture`.
    member val internal Tracked = false with get, set
    /// A style's selector tests its value (`Style.prop`, `Style.where`, `States.ofProp`): a change
    /// of the element's own value matches it again.
    member val internal TestedByStyles = false with get, set
    /// ... and an ancestor part does (`Style.inside`/`Style.parent`): a change matches the subtree.
    member val internal TestedByAncestorStyles = false with get, set
    /// Holds an inline state block (`Button.onState States.hover [ ... ]`): part of the element's styles.
    member val internal IsStateBlock = false with get, set
    /// The `styles` setting (a list of styles for the element and its subtree).
    member val internal IsStyles = false with get, set
    /// Styles can't set it (children, and what decides matching or identity).
    member val internal NotStylable = false with get, set
    /// App state: a controlled value (`isChecked`, `value`, `selected`, `isOpen`) or a flyout. Styles can't set it, nor a
    /// handler or content: a style has no model, no `dispatch`, no idea which element it's on (DESIGN.md, "M8 step 4",
    /// round 1f).
    member val internal IsAppState = false with get, set
    /// Its value is a function (`onClick`, `onValueChanged`): a handler.
    member internal _.IsHandler = isHandler
    /// Inherited: without a value of its own (or from a style or its type), an element takes its
    /// nearest ancestor's, else its theme's (`foreground`, `font`, `fontSize`).
    member val internal Inherits = false with get, set
    /// The `theme` setting: changing it re-matches styles and inherited values below.
    member val internal IsTheme = false with get, set
    /// For an attached property (`Prop.attached`): the name of the element type whose elements read it
    /// from their children ("Grid"); empty otherwise.
    member val AttachedTo = "" with get, set
    /// Equality of two values of this property's type (a theme value reference equals only itself).
    member internal _.Same(a: obj, b: obj) =
        match a, b with
        | (:? ThemeRef as r), _ -> r.Equals b
        | _, (:? ThemeRef) -> false
        | _ -> same a b
    /// How two values blend, when transitions can animate this property's type.
    member internal _.Lerp = lerp
    /// Its transition duration property, once a style has used `Style.transition` on it.
    member internal _.Transition = transition

    /// Its transition duration property (made on first use; properties are shared across threads).
    member internal this.TransitionProp: Prop<TimeSpan voption> =
        match transition with
        | ValueSome p -> p
        | ValueNone ->
            lock this (fun () ->
                match transition with
                | ValueSome p -> p
                | ValueNone ->
                    let p = Prop<TimeSpan voption>("Transition " + name, ValueNone, Affects.Nothing, PropKind.Value)
                    transition <- ValueSome p
                    p)

    override _.ToString() = name

    /// A property: name, default value, and what changing it affects.
    static member define (name: string) (defaultValue: 'T) (affects: Affects) : Prop<'T> =
        Prop<'T>(name, defaultValue, affects, PropKind.Value)

    /// An attached property: one a parent of type `owner` reads from its children (`Grid.row`).
    /// Any element accepts it; under another parent it does nothing (the inspector says so). Its
    /// name shows as "owner.name". A change re-measures the child and so its parent (Affects.Measure).
    static member attached (owner: string) (name: string) (defaultValue: 'T) (affects: Affects) : Prop<'T> =
        let p = Prop<'T>(owner + "." + name, defaultValue, affects, PropKind.Value)
        p.AttachedTo <- owner
        p

    /// Private per-element data (a cache, a counter): written with SetLocal, never invalidates.
    static member local (name: string) (defaultValue: 'T) : Prop<'T> =
        let p = Prop<'T>(name, defaultValue, Affects.Nothing, PropKind.Value)
        p.IsLocal <- true
        p

/// A property key whose values are of type 'T.
and [<Sealed>] Prop<'T> internal (name: string, defaultValue: 'T, affects: Affects, kind: PropKind) =
    inherit Prop(
        name, affects, kind, box defaultValue,
        (fun a b -> EqualityComparer<'T>.Default.Equals(unbox<'T> a, unbox<'T> b)),
        Interpolation.forType typeof<'T>, typeof<'T>)
    member _.Default = defaultValue

/// Marks a property as app state: styles can't set it (`Prop.IsAppState`).
module internal AppState =
    let mark (p: 'P when 'P :> Prop) =
        p.IsAppState <- true
        p

/// Where elements find base values for inherited properties: a theme (`Layoutable.theme`,
/// `App.theme`). Implemented by `Theme`.
type IThemeSource =
    /// The base value of an inherited property (text color, font, size), if the theme has one.
    abstract TryBase: prop: Prop -> obj voption
    /// A theme value's value in this theme (its default when the theme doesn't set it).
    abstract Resolve: themeValue: ThemeKey -> obj

/// A property value for an element of kind 'k ('k is a settings type such as Border, so a Border
/// setting can't be put on a TextBlock). Made by the setting functions, e.g. `Border.background`.
[<Struct>]
type Setting<'k> =
    internal
        { Prop: Prop
          Value: obj }

    /// The property this setting sets.
    member s.Key = s.Prop
    member s.BoxedValue = s.Value

module Setting =
    /// A setting of `prop` to `value`. Element authors use this to write setting functions.
    let create (prop: Prop<'T>) (value: 'T) : Setting<'k> = { Prop = prop; Value = box value }

    /// A setting of `prop` to a theme's value (`Palette.surface`, or the app's own theme value), read from
    /// the nearest theme. Element authors use this for a setting's theme value overload.
    let fromTheme (prop: Prop<'T>) (themeValue: ThemeValue<'T>) : Setting<'k> = { Prop = prop; Value = ThemeRef(themeValue, ValueNone) }

    /// The same for a property whose type differs from the theme value's: `convert` (a module-level
    /// function, so equal settings compare equal) turns the theme value's value into the property's.
    let internal fromThemeWith (prop: Prop<'T>) (themeValue: ThemeValue<'U>) (convert: obj -> obj) : Setting<'k> =
        { Prop = prop; Value = ThemeRef(themeValue, ValueSome convert) }

/// Properties every element has.
module LayoutProps =
    let name = Prop<string>("Name", "", Affects.Nothing, PropKind.Value)
    let margin = Prop<Thickness>("Margin", Thickness.Zero, Affects.Measure, PropKind.Value)
    /// Drawing and hit-testing order among siblings: higher on top; equal values keep view order.
    let zIndex = Prop<int>("ZIndex", 0, Affects.Render, PropKind.Value)
    /// Auto (the default) means "size to content / stretch, per alignment".
    let width = Prop<Length>("Width", Auto, Affects.Measure, PropKind.Value)
    let height = Prop<Length>("Height", Auto, Affects.Measure, PropKind.Value)
    /// Auto (the default) means no minimum; Star acts like Auto here.
    let minWidth = Prop<Length>("MinWidth", Auto, Affects.Measure, PropKind.Value)
    let minHeight = Prop<Length>("MinHeight", Auto, Affects.Measure, PropKind.Value)
    /// Auto (the default) means no maximum; Star acts like Auto here.
    let maxWidth = Prop<Length>("MaxWidth", Auto, Affects.Measure, PropKind.Value)
    let maxHeight = Prop<Length>("MaxHeight", Auto, Affects.Measure, PropKind.Value)
    let horizontalAlignment = Prop<HorizontalAlignment>("HorizontalAlignment", HorizontalAlignment.Stretch, Affects.Arrange, PropKind.Value)
    let verticalAlignment = Prop<VerticalAlignment>("VerticalAlignment", VerticalAlignment.Stretch, Affects.Arrange, PropKind.Value)
    /// Hidden elements take no space and draw nothing.
    let isVisible = Prop<bool>("IsVisible", true, Affects.Measure, PropKind.Value)
    /// Clip children to the element's bounds (rectangular for now).
    let clipToBounds = Prop<bool>("ClipToBounds", false, Affects.Render, PropKind.Value)
    /// False: the pointer goes through the element and everything inside it, to whatever is
    /// behind (highlights, decorations over other content).
    let isHitTestVisible = Prop<bool>("IsHitTestVisible", true, Affects.Nothing, PropKind.Value)
    /// Identifies a child among its siblings across updates (for lists that insert, remove or reorder).
    let key = Prop<obj voption>("Key", ValueNone, Affects.Nothing, PropKind.Value)
    /// Called when the left button is pressed and released on the element (or something inside it).
    let onClick = Prop<(ClickEventArgs -> unit) voption>("OnClick", ValueNone, Affects.Nothing, PropKind.Value)
    /// False turns off input for the element and everything inside it (and greys out built-in looks).
    let isEnabled = Prop<bool>("IsEnabled", true, Affects.Render, PropKind.Value)
    /// The pointer's look over the element (ValueNone: whatever its parent uses).
    let cursor = Prop<Cursor voption>("Cursor", ValueNone, Affects.Nothing, PropKind.Value)

    let private pointerHandler name = Prop<(PointerEventArgs -> unit) voption>(name, ValueNone, Affects.Nothing, PropKind.Value)
    let onPointerEnter = pointerHandler "OnPointerEnter"
    let onPointerLeave = pointerHandler "OnPointerLeave"
    let onPointerMove = pointerHandler "OnPointerMove"
    let onPointerDown = pointerHandler "OnPointerDown"
    let onPointerUp = pointerHandler "OnPointerUp"
    let onPreviewPointerDown = pointerHandler "OnPreviewPointerDown"
    let onPreviewPointerUp = pointerHandler "OnPreviewPointerUp"
    let onWheel = Prop<(WheelEventArgs -> unit) voption>("OnWheel", ValueNone, Affects.Nothing, PropKind.Value)
    let onPreviewWheel = Prop<(WheelEventArgs -> unit) voption>("OnPreviewWheel", ValueNone, Affects.Nothing, PropKind.Value)
    /// Can take keyboard focus (Tab stops here; a press focuses it). Render, so turning it off
    /// gets a frame, after which the focus manager lets go.
    let focusable = Prop<bool>("Focusable", false, Affects.Render, PropKind.Value)
    /// Tab stops here (when focusable). False: focusable by a press only (a ScrollViewer).
    let isTabStop = Prop<bool>("IsTabStop", true, Affects.Nothing, PropKind.Value)
    /// The element's style classes: plain names, matched by `Style.cls "name"`.
    let classes = Prop<string list>("Classes", [], Affects.Nothing, PropKind.Value)
    /// Fades the element and everything inside it (0 to 1); each shape fades on its own.
    let opacity = Prop<float>("Opacity", 1.0, Affects.Render, PropKind.Value)
    /// Draws the element and everything inside it larger or smaller around its center; layout and
    /// hit testing don't change.
    let scale = Prop<float>("Scale", 1.0, Affects.Render, PropKind.Value)
    /// The theme for the element and everything inside it (its styles and base text values).
    let theme = Prop<IThemeSource voption>("Theme", ValueNone, Affects.Measure, PropKind.Value)

    let private keyHandler name = Prop<(KeyEventArgs -> unit) voption>(name, ValueNone, Affects.Nothing, PropKind.Value)
    let onKeyDown = keyHandler "OnKeyDown"
    let onKeyUp = keyHandler "OnKeyUp"
    let onPreviewKeyDown = keyHandler "OnPreviewKeyDown"
    let onPreviewKeyUp = keyHandler "OnPreviewKeyUp"
    let onTextInput = Prop<(TextInputEventArgs -> unit) voption>("OnTextInput", ValueNone, Affects.Nothing, PropKind.Value)

    do
        for p in [ margin :> Prop; width; height; minWidth; minHeight; maxWidth; maxHeight; horizontalAlignment; verticalAlignment; isVisible; clipToBounds; zIndex ] do
            p.IsLayout <- true
        for p in [ name :> Prop; classes; isEnabled ] do
            p.Restyles <- true
        for p in [ name :> Prop; classes; key; theme ] do
            p.NotStylable <- true
        theme.IsTheme <- true

    /// What `Element.Values` lists even at the property's default: the properties every element has
    /// that say something about its layout or input (not the handlers or the key).
    let internal common: Prop list =
        [ name; margin; width; height; minWidth; minHeight; maxWidth; maxHeight; horizontalAlignment
          verticalAlignment; isVisible; clipToBounds; isEnabled; isHitTestVisible; focusable; cursor; classes ]

/// Text settings every element has and passes down to the text inside it (TextBlock reads them).
module InheritedProps =
    let font = Prop<Scribble.Text.Font>("Font", Scribble.Text.Fonts.Regular(), Affects.Measure, PropKind.Value)
    let fontSize = Prop<float>("FontSize", 14.0, Affects.Measure, PropKind.Value)
    let foreground = Prop<Scribble.Rendering.Color>("Foreground", Scribble.Rendering.Color.Black, Affects.Render, PropKind.Value)

    do
        for p in [ font :> Prop; fontSize; foreground ] do
            p.Inherits <- true

/// ToggleButton's value (CheckBox and Switch are ToggleButtons). Declared here, before the
/// styles, so `States.isChecked` can read it.
module ToggleButtonProps =
    /// On or off. Given by the view: controlled; otherwise the control's own value, kept across
    /// rebuilds (a click sets it, and the view never had it, so a patch leaves it).
    let isChecked = Prop<bool>("IsChecked", false, Affects.Render, PropKind.Value) |> AppState.mark
    /// A CheckBox's "partly checked" dash (display only).
    let isIndeterminate = Prop<bool>("IsIndeterminate", false, Affects.Render, PropKind.Value) |> AppState.mark
    /// Called with the new value when the user changes it.
    let onCheckedChanged = Prop<(bool -> unit) voption>("OnCheckedChanged", ValueNone, Affects.Nothing, PropKind.Value)
    /// A CheckBox's tick and dash, a Switch's knob.
    let markColor = Prop<Scribble.Rendering.Color>("MarkColor", Scribble.Rendering.Color.Transparent, Affects.Render, PropKind.Value)

/// Whether a TabControl's header or a ListBox's item is selected. Declared here so `States.isSelected` can read it.
module TabHeaderProps =
    let isSelected = Prop<bool>("IsSelected", false, Affects.Render, PropKind.Value) |> AppState.mark

/// Whether a ComboBox's list is open. Declared here so `States.isOpen` can read it.
module ComboBoxOpenProps =
    let isOpen = Prop<bool>("IsListOpen", false, Affects.Measure, PropKind.Value) |> AppState.mark

/// The engine's typed copy of an element's layout properties (read on every measure and arrange).
[<Struct>]
type internal LayoutValues =
    { Margin: Thickness
      Width: Length
      Height: Length
      MinWidth: Length
      MinHeight: Length
      MaxWidth: Length
      MaxHeight: Length
      HorizontalAlignment: HorizontalAlignment
      VerticalAlignment: VerticalAlignment
      IsVisible: bool
      ClipToBounds: bool
      ZIndex: int }

/// An element's sizes in DIPs, resolved during measure (NaN = automatic).
[<Struct>]
type internal ResolvedSizes =
    { Width: float
      Height: float
      MinWidth: float
      MinHeight: float
      MaxWidth: float
      MaxHeight: float }
