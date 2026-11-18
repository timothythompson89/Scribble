namespace Scribble.Core

open Scribble.Rendering

/// How a Border's border is drawn along its length.
[<RequireQualifiedAccess>]
type BorderStyle =
    | Solid
    /// Dashes 3 × the thickness long with 2 × the thickness between them, unless `Border.borderDash` says otherwise.
    | Dashed
    /// Round dots as wide as the border, about one width apart.
    | Dotted

module BorderProps =
    let background = Prop.define "Background" Color.Transparent Affects.Render
    let borderBrush = Prop.define "BorderBrush" Color.Transparent Affects.Render
    /// Border width in DIPs, the same on all sides (per-side borders come later).
    let borderThickness = Prop.define "BorderThickness" 0.0 Affects.Measure
    let cornerRadius = Prop.define "CornerRadius" CornerRadius.Zero Affects.Render
    let borderStyle = Prop.define "BorderStyle" BorderStyle.Solid Affects.Render
    /// Dash and gap lengths in DIPs for `BorderStyle.Dashed` (ValueNone: from the thickness).
    let borderDash: Prop<struct (float * float) voption> = Prop.define "BorderDash" ValueNone Affects.Render
    let padding = Prop.define "Padding" Thickness.Zero Affects.Measure
    let shadow: Prop<BoxShadow voption> = Prop.define "Shadow" ValueNone Affects.Render
    /// A translucent color laid over the whole box, background and border alike, used for hover and
    /// press: the theme lightens on hover and darkens on press this way, so the look works whatever
    /// the background color is (a class color like `.primary` keeps its state looks).
    let overlay = Prop.define "Overlay" Color.Transparent Affects.Render
    let content = Prop.content "Content"

/// Draws a background, border and optional shadow around one child, with padding inside the border.
[<AbstractClass>]
type Border =
    inherit Layoutable
    static member background<'k when 'k :> Border>(value: Color) : Setting<'k> = Setting.create BorderProps.background value
    static member background<'k when 'k :> Border>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.background themeValue
    static member borderBrush<'k when 'k :> Border>(value: Color) : Setting<'k> = Setting.create BorderProps.borderBrush value
    static member borderBrush<'k when 'k :> Border>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.borderBrush themeValue
    static member borderThickness<'k when 'k :> Border>(value: float) : Setting<'k> = Setting.create BorderProps.borderThickness value
    /// Solid (default), dashed or dotted. Dashes follow rounded corners and are evened out so the
    /// pattern meets itself.
    static member borderStyle<'k when 'k :> Border>(value: BorderStyle) : Setting<'k> = Setting.create BorderProps.borderStyle value
    /// Dash and gap lengths in DIPs for a dashed border (default: 3 × and 2 × the thickness).
    static member borderDash<'k when 'k :> Border>(dash: float, gap: float) : Setting<'k> = Setting.create BorderProps.borderDash (ValueSome(struct (dash, gap)))
    static member cornerRadius<'k when 'k :> Border>(value: CornerRadius) : Setting<'k> = Setting.create BorderProps.cornerRadius value
    static member cornerRadius<'k when 'k :> Border>(uniform: float) : Setting<'k> = Setting.create BorderProps.cornerRadius (CornerRadius.Uniform uniform)
    /// A theme's corner radius (`Palette.cornerRadius`), the same on every corner.
    static member cornerRadius<'k when 'k :> Border>(themeValue: ThemeValue<float>) : Setting<'k> =
        Setting.fromThemeWith BorderProps.cornerRadius themeValue ThemeConvert.uniformCorners
    static member padding<'k when 'k :> Border>(value: Thickness) : Setting<'k> = Setting.create BorderProps.padding value
    static member padding<'k when 'k :> Border>(uniform: float) : Setting<'k> = Setting.create BorderProps.padding (Thickness.Uniform uniform)
    static member padding<'k when 'k :> Border>(horizontal: float, vertical: float) : Setting<'k> =
        Setting.create BorderProps.padding (Thickness.Symmetric(horizontal, vertical))
    static member shadow<'k when 'k :> Border>(value: BoxShadow) : Setting<'k> = Setting.create BorderProps.shadow (ValueSome value)
    static member shadow<'k when 'k :> Border>(themeValue: ThemeValue<BoxShadow>) : Setting<'k> = Setting.fromThemeWith BorderProps.shadow themeValue ThemeConvert.someShadow
    /// A translucent color laid over the whole box (background and border), used for hover and
    /// press, so it works whatever the background color is. Transparent by default; the theme sets
    /// it per state, and a style can change it:
    /// `Button.style [ States.hover ] [ Button.overlay (Color.White.WithAlpha 0.15f) ]`.
    static member overlay<'k when 'k :> Border>(value: Color) : Setting<'k> = Setting.create BorderProps.overlay value
    static member overlay<'k when 'k :> Border>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme BorderProps.overlay themeValue
    /// What it shows (one view; Button, Flyout and other Borders inherit it).
    static member content<'k when 'k :> Border>(value: View) : Setting<'k> = Setting.create BorderProps.content (ValueSome value)

module Border =
    let internal inset (ctx: MeasureContext) =
        let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
        p.Horizontal + 2.0 * t, p.Vertical + 2.0 * t

    /// The drawing style for a border style, dash lengths and thickness.
    let internal strokeStyle (style: BorderStyle) (dash: struct (float * float) voption) (thickness: float) =
        match style with
        | BorderStyle.Solid -> StrokeStyle.Solid
        | BorderStyle.Dotted -> StrokeStyle.Dotted
        | BorderStyle.Dashed ->
            match dash with
            | ValueSome(struct (d, g)) -> StrokeStyle.Dashed(d, g)
            | ValueNone -> StrokeStyle.Dashed(3.0 * thickness, 2.0 * thickness)

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
        if thickness > 0.0 && brush.A > 0.0f then
            let style = strokeStyle (ctx.Get BorderProps.borderStyle) (ctx.Get BorderProps.borderDash) thickness
            list.DrawBox(rect, radius, background, thickness, brush, style = style)
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
