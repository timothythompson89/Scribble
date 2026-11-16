namespace Scribble.Core

open System
open Scribble.Rendering

/// A Border with a default look and something to click: `Button.create [ Button.text "+1";
/// Button.onClick (fun _ -> dispatch Increment) ]`. Every Border setting works on it.
/// It lightens under the pointer, darkens while pressed and fades when disabled. It takes keyboard
/// focus (Tab; a ring shows), and Enter or Space clicks it. A themeable look comes with styles in
/// Milestone 7.
[<AbstractClass>]
type Button =
    inherit Border

    /// A text label, centered (shorthand for a TextBlock child).
    static member text<'k when 'k :> Button>(value: string) : Setting<'k> =
        Setting.create BorderProps.child (
            ValueSome(
                TextBlock.create [
                    TextBlock.text value
                    TextBlock.foreground (Color.Hex 0xE6E6F0)
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        )

    /// Any content.
    static member content<'k when 'k :> Button>(value: View) : Setting<'k> = Setting.create BorderProps.child (ValueSome value)

module Button =
    /// How long the press takes to sink in and spring back.
    let private pressTime = TimeSpan.FromMilliseconds 120.0

    /// `c` moved a fraction `t` of the way towards `target` (alpha kept).
    let private mix (c: Color) (target: Color) (t: float32) =
        Color.Rgba(c.R + (target.R - c.R) * t, c.G + (target.G - c.G) * t, c.B + (target.B - c.B) * t, c.A)

    /// Border's look adjusted for the input state: lighter under the pointer, darker while
    /// pressed, faded when disabled (whatever colors are set); the shadow flattens while pressed
    /// or disabled.
    let private render (ctx: RenderContext) (list: DrawList) =
        let background, brush = ctx.Get BorderProps.background, ctx.Get BorderProps.borderBrush
        let background, brush =
            if not ctx.IsEnabled then background.WithAlpha(background.A * 0.5f), brush.WithAlpha(brush.A * 0.5f)
            elif ctx.IsPressed then mix background Color.Black 0.2f, mix brush Color.Black 0.1f
            elif ctx.IsPointerOver then mix background Color.White 0.08f, mix brush Color.White 0.12f
            else background, brush
        // 1 = raised, 0 = flat.
        let height = ctx.Animate("height", (if ctx.IsPressed || not ctx.IsEnabled then 0.0 else 1.0), pressTime)
        let shadow =
            ctx.Get BorderProps.shadow
            |> ValueOption.map (fun s -> { s with OffsetY = s.OffsetY * height; Blur = s.Blur * (0.4 + 0.6 * height) })
        Border.draw ctx list background brush shadow
        if ctx.IsFocusVisible then FocusRing.draw ctx list (ctx.Get BorderProps.cornerRadius)

    /// Sinks to 97% around the center while pressed (drawing only: layout and input don't move).
    let private transform (ctx: RenderContext) =
        let scale = ctx.Animate("press", (if ctx.IsPressed then 0.97 else 1.0), pressTime)
        let size = ctx.Size
        Transform.ScaleAround(scale, size.Width / 2.0, size.Height / 2.0)

    let Type =
        Element.extend Border.Type "Button" [
            Element.render render
            Element.transform transform
            Element.defaults [
                Border.background (Color.Hex 0x3A3A55)
                Border.borderBrush (Color.Hex 0x4A4A68)
                Border.borderThickness 1.0
                Border.cornerRadius 6.0
                Border.padding (14.0, 7.0)
                Border.shadow { Blur = 3.0; OffsetX = 0.0; OffsetY = 1.0; Spread = 0.0; Color = Color.Rgba(0.0f, 0.0f, 0.0f, 0.35f) }
                Layoutable.horizontalAlignment HorizontalAlignment.Left
                Layoutable.cursor Cursor.Hand
                Layoutable.focusable true
            ]
        ]

    let create (settings: Setting<Button> list) : View = View.Create(Type, settings)
    /// A style for Buttons: `Button.style [ Style.cls "c"; States.hover ] [ Button.margin 4.0 ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Button> list) : Style = Style.forType Type parts settings
