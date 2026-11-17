namespace Scribble.Core

open Scribble.Accessibility
open Scribble.Rendering

module IconViewProps =
    let icon: Prop<Icon voption> = Prop.define "Icon" ValueNone Affects.Render
    /// Width and height in DIPs.
    let size = Prop.define "Size" 20.0 Affects.Measure

/// Shows an icon, `size` DIPs square, centered in its space, in the text color around it (or its
/// own `foreground`): `IconView.create [ IconView.icon menu; IconView.size 24.0 ]`.
[<AbstractClass>]
type IconView =
    inherit Layoutable
    static member icon<'k when 'k :> IconView>(value: Icon) : Setting<'k> = Setting.create IconViewProps.icon (ValueSome value)
    /// Width and height in DIPs (default 20).
    static member size<'k when 'k :> IconView>(value: float) : Setting<'k> = Setting.create IconViewProps.size value

module IconView =
    /// Draws `icon` in a `size` square centered in `area` (for elements that show an icon inside).
    let internal draw (ctx: RenderContext) (list: DrawList) (icon: Icon) (area: Rect) (size: float) =
        let color = ctx.Get InheritedProps.foreground
        let x = area.X + (area.Width - size) / 2.0
        let y = area.Y + (area.Height - size) / 2.0
        ctx.Text.DrawIcon(list, Icon.geometry icon, icon.ViewBox, icon.Rotation, icon.FlipHorizontal, icon.FlipVertical, x, y, size, color)

    let Type =
        Element.define "IconView" [
            Element.defaults [ Semantics.role Role.Image ]
            Element.measure (fun ctx _ ->
                let s = ctx.Get IconViewProps.size
                Size.Create(s, s))
            Element.render (fun ctx list ->
                match ctx.Get IconViewProps.icon with
                | ValueSome icon ->
                    draw ctx list icon (Rect.Create(0.0, 0.0, ctx.Size.Width, ctx.Size.Height)) (ctx.Get IconViewProps.size)
                | ValueNone -> ())
        ]

    let create (settings: Setting<IconView> list) : View = View.Create(Type, settings)
    /// A style for IconViews: `IconView.style [ States.hover ] [ IconView.foreground c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<IconView> list) : Style = Style.forType Type parts settings
