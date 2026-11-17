namespace Scribble.Core

open Scribble.Rendering

module SeparatorProps =
    /// Horizontal (a line across, the default) or Vertical (a line down).
    let orientation = Prop.define "Orientation" Horizontal Affects.Measure
    /// The line's thickness in DIPs.
    let thickness = Prop.define "Thickness" 1.0 Affects.Measure
    /// Solid (default), dashed or dotted.
    let lineStyle = Prop.define "LineStyle" BorderStyle.Solid Affects.Render
    /// The line's color (the theme's `Palette.border`).
    let color = Prop.define "Color" (Color.Hex 0x4A4A68) Affects.Render

/// A thin line between groups of things: across its whole width (Horizontal, the default) or down
/// its whole height (Vertical). For toolbars, menus and forms.
[<AbstractClass>]
type Separator =
    inherit Layoutable
    static member orientation<'k when 'k :> Separator>(value: Orientation) : Setting<'k> = Setting.create SeparatorProps.orientation value
    static member thickness<'k when 'k :> Separator>(value: float) : Setting<'k> = Setting.create SeparatorProps.thickness value
    /// Solid (default), `BorderStyle.Dashed` or `BorderStyle.Dotted`.
    static member lineStyle<'k when 'k :> Separator>(value: BorderStyle) : Setting<'k> = Setting.create SeparatorProps.lineStyle value
    static member color<'k when 'k :> Separator>(value: Color) : Setting<'k> = Setting.create SeparatorProps.color value
    static member color<'k when 'k :> Separator>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme SeparatorProps.color themeValue

module Separator =
    let Type =
        Element.define "Separator" [
            Element.measure (fun ctx _ ->
                let t = ctx.Get SeparatorProps.thickness
                match ctx.Get SeparatorProps.orientation with
                | Horizontal -> Size.Create(0.0, t)
                | Vertical -> Size.Create(t, 0.0))
            Element.render (fun ctx list ->
                let t = ctx.Get SeparatorProps.thickness
                let size = ctx.Size
                let rect =
                    match ctx.Get SeparatorProps.orientation with
                    | Horizontal -> Rect.Create(0.0, (size.Height - t) / 2.0, size.Width, t)
                    | Vertical -> Rect.Create((size.Width - t) / 2.0, 0.0, t, size.Height)
                let color = ctx.Get SeparatorProps.color
                match ctx.Get SeparatorProps.lineStyle with
                | BorderStyle.Solid -> list.FillRect(rect, color)
                | style -> list.StrokeRect(rect, CornerRadius.Zero, t, color, style = Border.strokeStyle style ValueNone t))
        ]

    let create (settings: Setting<Separator> list) : View = View.Create(Type, settings)
    /// A style for Separators: `Separator.style [] [ Separator.color c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Separator> list) : Style = Style.forType Type parts settings
