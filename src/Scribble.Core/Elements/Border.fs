namespace Scribble.Core

open Scribble.Rendering

module BorderProps =
    let background = Prop.define "Background" Color.Transparent Affects.Render
    let borderBrush = Prop.define "BorderBrush" Color.Transparent Affects.Render
    /// Border width in DIPs, the same on all sides (per-side borders come later).
    let borderThickness = Prop.define "BorderThickness" 0.0 Affects.Measure
    let cornerRadius = Prop.define "CornerRadius" CornerRadius.Zero Affects.Render
    let padding = Prop.define "Padding" Thickness.Zero Affects.Measure
    let shadow: Prop<BoxShadow voption> = Prop.define "Shadow" ValueNone Affects.Render
    /// A translucent color laid over the whole box, background and border alike, used for hover and
    /// press: the theme lightens on hover and darkens on press this way, so the look works whatever
    /// the background color is (a class color like `.primary` keeps its state looks).
    let overlay = Prop.define "Overlay" Color.Transparent Affects.Render
    let child = Prop.child "Child"

/// Draws a background, border and optional shadow around one child, with padding inside the border.
[<AbstractClass>]
type Border =
    inherit Layoutable
    static member background<'k when 'k :> Border>(value: Color) : Setting<'k> = Setting.create BorderProps.background value
    static member background<'k when 'k :> Border>(token: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.background token
    static member borderBrush<'k when 'k :> Border>(value: Color) : Setting<'k> = Setting.create BorderProps.borderBrush value
    static member borderBrush<'k when 'k :> Border>(token: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.borderBrush token
    static member borderThickness<'k when 'k :> Border>(value: float) : Setting<'k> = Setting.create BorderProps.borderThickness value
    static member cornerRadius<'k when 'k :> Border>(value: CornerRadius) : Setting<'k> = Setting.create BorderProps.cornerRadius value
    static member cornerRadius<'k when 'k :> Border>(uniform: float) : Setting<'k> = Setting.create BorderProps.cornerRadius (CornerRadius.Uniform uniform)
    /// A theme's corner radius (`Palette.cornerRadius`), the same on every corner.
    static member cornerRadius<'k when 'k :> Border>(token: ThemeValue<float>) : Setting<'k> =
        Setting.fromThemeWith BorderProps.cornerRadius token ThemeConvert.uniformCorners
    static member padding<'k when 'k :> Border>(value: Thickness) : Setting<'k> = Setting.create BorderProps.padding value
    static member padding<'k when 'k :> Border>(uniform: float) : Setting<'k> = Setting.create BorderProps.padding (Thickness.Uniform uniform)
    static member padding<'k when 'k :> Border>(horizontal: float, vertical: float) : Setting<'k> =
        Setting.create BorderProps.padding (Thickness.Symmetric(horizontal, vertical))
    static member shadow<'k when 'k :> Border>(value: BoxShadow) : Setting<'k> = Setting.create BorderProps.shadow (ValueSome value)
    static member shadow<'k when 'k :> Border>(token: ThemeValue<BoxShadow>) : Setting<'k> = Setting.fromThemeWith BorderProps.shadow token ThemeConvert.someShadow
    /// A translucent color laid over the whole box (background and border), used for hover and
    /// press, so it works whatever the background color is. Transparent by default; the theme sets
    /// it per state, and a style can change it:
    /// `Button.style [ States.hover ] [ Button.overlay (Color.White.WithAlpha 0.15f) ]`.
    static member overlay<'k when 'k :> Border>(value: Color) : Setting<'k> = Setting.create BorderProps.overlay value
    static member overlay<'k when 'k :> Border>(token: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.overlay token
    static member child<'k when 'k :> Border>(value: View) : Setting<'k> = Setting.create BorderProps.child (ValueSome value)

module Border =
    let private inset (ctx: MeasureContext) =
        let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
        p.Horizontal + 2.0 * t, p.Vertical + 2.0 * t

    /// Draws the shadow, background, border and overlay.
    let internal draw (ctx: RenderContext) (list: DrawList) =
        let size = ctx.Size
        let rect = Rect.Create(0.0, 0.0, size.Width, size.Height)
        let radius = ctx.Get BorderProps.cornerRadius
        let thickness = ctx.Get BorderProps.borderThickness
        let background, brush = ctx.Get BorderProps.background, ctx.Get BorderProps.borderBrush
        match ctx.Get BorderProps.shadow with
        | ValueSome s -> list.DrawShadow(rect, radius, s.Blur, s.Color, s.OffsetX, s.OffsetY, s.Spread)
        | ValueNone -> ()
        if thickness > 0.0 && brush.A > 0.0f then list.DrawBox(rect, radius, background, thickness, brush)
        elif background.A > 0.0f then
            if radius.IsZero then list.FillRect(rect, background) else list.FillRoundedRect(rect, radius, background)
        let overlay = ctx.Get BorderProps.overlay
        if overlay.A > 0.0f then
            if radius.IsZero then list.FillRect(rect, overlay) else list.FillRoundedRect(rect, radius, overlay)

    let Type =
        Element.define "Border" [
            Element.measure (fun ctx available ->
                let ix, iy = inset ctx
                let mutable w, h = ix, iy
                for child in ctx.Children do
                    let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - ix), max 0.0 (available.Height - iy)))
                    w <- max w (d.Width + ix)
                    h <- max h (d.Height + iy)
                Size.Create(w, h))
            Element.arrange (fun ctx size ->
                let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
                let slot =
                    Rect.Create(p.Left + t, p.Top + t, max 0.0 (size.Width - p.Horizontal - 2.0 * t), max 0.0 (size.Height - p.Vertical - 2.0 * t))
                for child in ctx.Children do
                    ctx.Arrange(child, slot))
            Element.render draw
        ]

    let create (settings: Setting<Border> list) : View = View.Create(Type, settings)
    /// A style for Borders: `Border.style [ Style.cls "c"; States.hover ] [ Border.margin 4.0 ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Border> list) : Style = Style.forType Type parts settings
