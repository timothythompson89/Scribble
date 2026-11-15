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

/// A property key: a name, a default value and what changing it affects. Elements store values
/// for the properties set on them; everything else reads as the default.
///
/// Define properties once, as module-level values, with `Prop.define` (see Element.fs).
[<AbstractClass>]
type Prop internal (name: string, affects: Affects, kind: PropKind, defaultBoxed: obj, same: obj -> obj -> bool) =
    static let mutable lastId = 0
    let id = Interlocked.Increment(&lastId)

    member _.Name = name
    member _.Affects = affects
    member internal _.Id = id
    member internal _.Kind = kind
    member internal _.DefaultBoxed = defaultBoxed
    /// One of LayoutProps: the engine keeps a typed copy for fast layout.
    member val internal IsLayout = false with get, set
    /// Equality of two values of this property's type.
    member internal _.Same(a: obj, b: obj) = same a b
    override _.ToString() = name

    /// A property: name, default value, and what changing it affects.
    static member define (name: string) (defaultValue: 'T) (affects: Affects) : Prop<'T> =
        Prop<'T>(name, defaultValue, affects, PropKind.Value)

    /// Private per-element data (a cache, a counter): written with SetLocal, never invalidates.
    static member local (name: string) (defaultValue: 'T) : Prop<'T> = Prop<'T>(name, defaultValue, Affects.Nothing, PropKind.Value)

/// A property key whose values are of type 'T.
and [<Sealed>] Prop<'T> internal (name: string, defaultValue: 'T, affects: Affects, kind: PropKind) =
    inherit Prop(name, affects, kind, box defaultValue, fun a b -> EqualityComparer<'T>.Default.Equals(unbox<'T> a, unbox<'T> b))
    member _.Default = defaultValue

/// A property value for an element of kind 'k ('k is a marker type such as Border, so a Border
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

    let private keyHandler name = Prop<(KeyEventArgs -> unit) voption>(name, ValueNone, Affects.Nothing, PropKind.Value)
    let onKeyDown = keyHandler "OnKeyDown"
    let onKeyUp = keyHandler "OnKeyUp"
    let onPreviewKeyDown = keyHandler "OnPreviewKeyDown"
    let onPreviewKeyUp = keyHandler "OnPreviewKeyUp"

    do
        for p in [ margin :> Prop; width; height; minWidth; minHeight; maxWidth; maxHeight; horizontalAlignment; verticalAlignment; isVisible; clipToBounds ] do
            p.IsLayout <- true

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
