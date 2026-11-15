namespace Scribble.Core

/// Marker type for settings every element has. Element marker types (Border, TextBlock, …)
/// inherit it, so `TextBlock.margin` works and `TextBlock.create [ Border.background … ]` doesn't
/// compile.
[<AbstractClass>]
type Layoutable =
    /// A name for the inspector, tests and logs.
    static member name<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create LayoutProps.name value
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
    /// Identifies this element among its siblings across updates. Only needed in lists that insert,
    /// remove or reorder items (see also `Panel.items`); elsewhere position is enough.
    static member key<'k when 'k :> Layoutable>(value: obj) : Setting<'k> = Setting.create LayoutProps.key (ValueSome value)
    /// Runs when the pointer is pressed and released on this element (or something inside it).
    static member onClick<'k when 'k :> Layoutable>(handler: unit -> unit) : Setting<'k> = Setting.create LayoutProps.onClick (ValueSome handler)

module PanelProps =
    let children = Prop.children "Children"

/// Marker type for elements with a list of children.
[<AbstractClass>]
type Panel =
    inherit Layoutable
    static member children<'k when 'k :> Panel>(value: View list) : Setting<'k> = Setting.create PanelProps.children value
    /// Children from a collection, keyed by `key`, so items keep their elements when the
    /// collection inserts, removes or reorders: `StackPanel.items todos _.Id todoView`.
    static member items<'k, 'T, 'Key when 'k :> Panel> (items: 'T seq) (key: 'T -> 'Key) (view: 'T -> View) : Setting<'k> =
        Setting.create PanelProps.children [ for item in items -> (view item).WithKey(box (key item)) ]
