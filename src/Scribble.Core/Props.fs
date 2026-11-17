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
    /// Holds `Element voption`: one child.
    | Child = 1
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
    static member NextThemeKey() = lock typeof<Ids> (fun () -> Ids.themeKey <- Ids.themeKey + 1; Ids.themeKey)
    static member NextProp() = lock typeof<Ids> (fun () -> Ids.prop <- Ids.prop + 1; Ids.prop)

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

/// A property key: a name, a default value and what changing it affects. Elements store values
/// for the properties set on them; everything else reads as the default.
///
/// Define properties once, as module-level values, with `Prop.define` (see Element.fs).
[<AbstractClass>]
type Prop internal (name: string, affects: Affects, kind: PropKind, defaultBoxed: obj, same: obj -> obj -> bool, lerp: (obj -> obj -> float -> obj) voption) =
    let id = Ids.NextProp()
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
    /// A style's selector tests its value (`Style.prop`, `Style.where`, `States.ofProp`): a change
    /// of the element's own value matches it again.
    member val internal TestedByStyles = false with get, set
    /// ... and an ancestor part does (`Style.inside`/`Style.parent`): a change matches the subtree.
    member val internal TestedByAncestorStyles = false with get, set
    /// Holds an inline state block (`Button.on States.hover [ ... ]`): part of the element's styles.
    member val internal IsStateBlock = false with get, set
    /// The `styles` setting (a list of styles for the element and its subtree).
    member val internal IsStyles = false with get, set
    /// Styles can't set it (children, and what decides matching or identity).
    member val internal NotStylable = false with get, set
    /// Inherited: without a value of its own (or from a style or its type), an element takes its
    /// nearest ancestor's, else its theme's (`foreground`, `font`, `fontSize`).
    member val internal Inherits = false with get, set
    /// The `theme` setting: changing it re-matches styles and inherited values below.
    member val internal IsTheme = false with get, set
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
        Interpolation.forType typeof<'T>)
    member _.Default = defaultValue

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

    do
        for p in [ margin :> Prop; width; height; minWidth; minHeight; maxWidth; maxHeight; horizontalAlignment; verticalAlignment; isVisible; clipToBounds ] do
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
      ClipToBounds: bool }

/// An element's sizes in DIPs, resolved during measure (NaN = automatic).
[<Struct>]
type internal ResolvedSizes =
    { Width: float
      Height: float
      MinWidth: float
      MinHeight: float
      MaxWidth: float
      MaxHeight: float }
