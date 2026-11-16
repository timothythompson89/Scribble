namespace Scribble.Core

/// Settings type for settings every element has. Element settings types (Border, TextBlock, …)
/// inherit it, so `TextBlock.margin` works and `TextBlock.create [ Border.background … ]` doesn't
/// compile.
[<AbstractClass>]
type Layoutable =
    /// A name for the inspector, tests and logs.
    static member name<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.name value
    /// Style classes, plain names: `Button.classes [ "primary"; if big then "large" ]`.
    static member classes<'k when 'k :> Layoutable>(value: string list) : Setting<'k> = Setting.create LayoutProps.classes value
    /// Styles for this element and everything inside it (nearer scopes win over outer ones and the
    /// app's): `Border.styles cardStyles`. Define the list once (styles compare by reference).
    static member styles<'k when 'k :> Layoutable>(value: Style list) : Setting<'k> = Setting.create StyleProps.styles value
    /// Settings that apply while this element is in `state`, right in the view:
    /// `Button.on States.hover [ Button.background light ]` (one block per state; blocks nest).
    static member on<'k when 'k :> Layoutable> (state: ElementState) (settings: Setting<'k> list) : Setting<'k> = Style.on state settings
    static member margin<'k when 'k :> Layoutable>(value: Thickness) : Setting<'k> = Setting.create LayoutProps.margin value
    static member margin<'k when 'k :> Layoutable>(uniform: float) : Setting<'k> = Setting.create LayoutProps.margin (Thickness.Uniform uniform)
    static member margin<'k when 'k :> Layoutable>(horizontal: float, vertical: float) : Setting<'k> =
        Setting.create LayoutProps.margin (Thickness.Symmetric(horizontal, vertical))
    static member margin<'k when 'k :> Layoutable>(left: float, top: float, right: float, bottom: float) : Setting<'k> =
        Setting.create LayoutProps.margin (Thickness.Create(left, top, right, bottom))
    static member width<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.width (Px value)
    static member width<'k when 'k :> Layoutable>(value: Length) : Setting<'k> = Setting.create LayoutProps.width value
    /// "45px", "45", "45%", "2*", "*" or "auto".
    static member width<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.width (Length.parse value)
    static member height<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.height (Px value)
    static member height<'k when 'k :> Layoutable>(value: Length) : Setting<'k> = Setting.create LayoutProps.height value
    /// "45px", "45", "45%", "2*", "*" or "auto".
    static member height<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.height (Length.parse value)
    static member minWidth<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.minWidth (Px value)
    static member minWidth<'k when 'k :> Layoutable>(value: Length) : Setting<'k> = Setting.create LayoutProps.minWidth value
    /// "45px", "45", "45%", "2*", "*" or "auto".
    static member minWidth<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.minWidth (Length.parse value)
    static member minHeight<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.minHeight (Px value)
    static member minHeight<'k when 'k :> Layoutable>(value: Length) : Setting<'k> = Setting.create LayoutProps.minHeight value
    /// "45px", "45", "45%", "2*", "*" or "auto".
    static member minHeight<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.minHeight (Length.parse value)
    static member maxWidth<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.maxWidth (Px value)
    static member maxWidth<'k when 'k :> Layoutable>(value: Length) : Setting<'k> = Setting.create LayoutProps.maxWidth value
    /// "45px", "45", "45%", "2*", "*" or "auto".
    static member maxWidth<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.maxWidth (Length.parse value)
    static member maxHeight<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.maxHeight (Px value)
    static member maxHeight<'k when 'k :> Layoutable>(value: Length) : Setting<'k> = Setting.create LayoutProps.maxHeight value
    /// "45px", "45", "45%", "2*", "*" or "auto".
    static member maxHeight<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.maxHeight (Length.parse value)
    static member horizontalAlignment<'k when 'k :> Layoutable>(value: HorizontalAlignment) : Setting<'k> =
        Setting.create LayoutProps.horizontalAlignment value
    static member verticalAlignment<'k when 'k :> Layoutable>(value: VerticalAlignment) : Setting<'k> =
        Setting.create LayoutProps.verticalAlignment value
    static member isVisible<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create LayoutProps.isVisible value
    static member clipToBounds<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create LayoutProps.clipToBounds value
    /// False: the pointer goes through this element and everything inside it (a highlight drawn
    /// over other content). Default: true.
    static member isHitTestVisible<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create LayoutProps.isHitTestVisible value
    /// Identifies this element among its siblings across updates. Only needed in lists that insert,
    /// remove or reorder items (see also `Panel.items`); elsewhere position is enough.
    static member key<'k when 'k :> Layoutable>(value: obj) : Setting<'k> = Setting.create LayoutProps.key (ValueSome value)
    /// Runs when the left button is pressed and released on this element (or something inside it),
    /// or when it has focus and Enter or Space activates it. Dragging off before releasing cancels
    /// it. `e.Modifiers` tells shift-clicks apart; `e.Source` says pointer or keyboard.
    static member onClick<'k when 'k :> Layoutable>(handler: ClickEventArgs -> unit) : Setting<'k> = Setting.create LayoutProps.onClick (ValueSome handler)
    /// False turns off input for this element and everything inside it; built-in looks grey out.
    static member isEnabled<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create LayoutProps.isEnabled value
    /// The pointer's look over this element (and inside it, unless something there sets its own).
    static member cursor<'k when 'k :> Layoutable>(value: Cursor) : Setting<'k> = Setting.create LayoutProps.cursor (ValueSome value)
    /// Fades this element and everything inside it (0 to 1). Each shape fades on its own, so where
    /// shapes overlap they show through each other.
    static member opacity<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.opacity value
    /// Draws this element and everything inside it larger or smaller around its center (1 = as is);
    /// layout and hit testing don't change. Button's press is `Button.on States.pressed [ Button.scale 0.97 ]`.
    static member scale<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create LayoutProps.scale value
    /// The theme for this element and everything inside it (instead of the app's): its looks and
    /// base text color, font and size. Switching is the view returning a different theme.
    static member theme<'k when 'k :> Layoutable>(value: Theme) : Setting<'k> = Setting.create LayoutProps.theme (ValueSome(value :> IThemeSource))
    /// Text color for this element and the text inside it (inherited).
    static member foreground<'k when 'k :> Layoutable>(value: Scribble.Rendering.Color) : Setting<'k> = Setting.create InheritedProps.foreground value
    static member foreground<'k when 'k :> Layoutable>(themeValue: ThemeValue<Scribble.Rendering.Color>) : Setting<'k> = Setting.fromTheme InheritedProps.foreground themeValue
    /// Font for the text inside this element (inherited).
    static member font<'k when 'k :> Layoutable>(value: Scribble.Text.Font) : Setting<'k> = Setting.create InheritedProps.font value
    static member font<'k when 'k :> Layoutable>(themeValue: ThemeValue<Scribble.Text.Font>) : Setting<'k> = Setting.fromTheme InheritedProps.font themeValue
    /// Font size in DIPs for the text inside this element (inherited).
    static member fontSize<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create InheritedProps.fontSize value
    static member fontSize<'k when 'k :> Layoutable>(themeValue: ThemeValue<float>) : Setting<'k> = Setting.fromTheme InheritedProps.fontSize themeValue

    /// The pointer came over this element (or something inside it). Not routed.
    static member onPointerEnter<'k when 'k :> Layoutable>(handler: PointerEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPointerEnter (ValueSome handler)
    /// The pointer is no longer over this element or anything inside it. Not routed.
    static member onPointerLeave<'k when 'k :> Layoutable>(handler: PointerEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPointerLeave (ValueSome handler)
    /// The pointer moved over this element, or anywhere while this element holds the press. Bubbles.
    static member onPointerMove<'k when 'k :> Layoutable>(handler: PointerEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPointerMove (ValueSome handler)
    /// A button went down over this element. Bubbles. The element that gets the press keeps getting
    /// the pointer's moves and the release until the last button is up (pointer capture).
    static member onPointerDown<'k when 'k :> Layoutable>(handler: PointerEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPointerDown (ValueSome handler)
    /// A button came up. Goes to the element that got the press (wherever the pointer is now). Bubbles.
    static member onPointerUp<'k when 'k :> Layoutable>(handler: PointerEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPointerUp (ValueSome handler)
    /// Like onPointerDown, but runs from the root down, before the element under the pointer sees it.
    /// Handling it here takes the press: the element under the pointer gets nothing and doesn't click.
    static member onPreviewPointerDown<'k when 'k :> Layoutable>(handler: PointerEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPreviewPointerDown (ValueSome handler)
    /// Like onPointerUp, but runs from the root down first.
    static member onPreviewPointerUp<'k when 'k :> Layoutable>(handler: PointerEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPreviewPointerUp (ValueSome handler)
    /// The wheel turned over this element. Bubbles.
    static member onWheel<'k when 'k :> Layoutable>(handler: WheelEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onWheel (ValueSome handler)
    /// Like onWheel, but runs from the root down first.
    static member onPreviewWheel<'k when 'k :> Layoutable>(handler: WheelEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPreviewWheel (ValueSome handler)

    /// Can take keyboard focus: Tab and Shift+Tab stop here (in tree order), and pressing on it
    /// focuses it. Buttons are focusable by default.
    static member focusable<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create LayoutProps.focusable value
    /// A key went down while this element (or something inside it) has focus. Bubbles. Handling
    /// it stops built-in behavior too (Tab moving focus, Enter/Space clicking, App.onKey).
    static member onKeyDown<'k when 'k :> Layoutable>(handler: KeyEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onKeyDown (ValueSome handler)
    /// A key came up while this element (or something inside it) has focus. Bubbles.
    static member onKeyUp<'k when 'k :> Layoutable>(handler: KeyEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onKeyUp (ValueSome handler)
    /// Like onKeyDown, but runs from the root down, before the focused element sees it.
    static member onPreviewKeyDown<'k when 'k :> Layoutable>(handler: KeyEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPreviewKeyDown (ValueSome handler)
    /// Like onKeyUp, but runs from the root down first.
    static member onPreviewKeyUp<'k when 'k :> Layoutable>(handler: KeyEventArgs -> unit) : Setting<'k> =
        Setting.create LayoutProps.onPreviewKeyUp (ValueSome handler)

module PanelProps =
    let children = Prop.children "Children"

/// Settings type for elements with a list of children.
[<AbstractClass>]
type Panel =
    inherit Layoutable
    static member children<'k when 'k :> Panel>(value: View list) : Setting<'k> = Setting.create PanelProps.children value
    /// Children from a collection, keyed by `key`, so items keep their elements when the
    /// collection inserts, removes or reorders: `StackPanel.items todos _.Id todoView`.
    static member items<'k, 'T, 'Key when 'k :> Panel> (items: 'T seq) (key: 'T -> 'Key) (view: 'T -> View) : Setting<'k> =
        Setting.create PanelProps.children [ for item in items -> (view item).WithKey(box (key item)) ]

[<AutoOpen>]
module StyleExtensions =
    type Style with
        /// A style for elements of any type: `Style.any [ Style.cls compact ] [ Layoutable.margin 2.0 ]`.
        static member any (parts: SelectorPart list) (settings: Setting<Layoutable> list) : Style =
            Style.Create(Selector.build ValueNone parts, [| for s in settings -> struct (s.Prop, s.Value) |])
