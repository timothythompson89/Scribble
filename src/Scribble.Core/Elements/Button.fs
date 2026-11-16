namespace Scribble.Core

open System
open Scribble.Rendering

/// A Border with a default look and something to click: `Button.create [ Button.text "+1";
/// Button.onClick (fun _ -> dispatch Increment) ]`. Every Border setting works on it.
/// It takes keyboard focus (Tab; a ring shows), and Enter or Space clicks it. Its hover, pressed
/// and disabled looks come from the theme (`overlay`, `scale`, `shadow`, `opacity` in state
/// blocks), so app styles can change them.
[<AbstractClass>]
type Button =
    inherit Border

    /// A text label, centered (shorthand for a TextBlock child; its color comes from the Button's
    /// `foreground`).
    static member text<'k when 'k :> Button>(value: string) : Setting<'k> =
        Setting.create BorderProps.child (
            ValueSome(
                TextBlock.create [
                    TextBlock.text value
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        )

    /// Any content.
    static member content<'k when 'k :> Button>(value: View) : Setting<'k> = Setting.create BorderProps.child (ValueSome value)

module Button =
    /// Border's look plus the focus ring.
    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
        if ctx.IsFocusVisible then FocusRing.draw ctx list (ctx.Get BorderProps.cornerRadius)

    let Type =
        Element.extend Border.Type "Button" [
            Element.render render
            // The dark base look (also without a theme); the theme adds the state looks.
            Element.defaults [
                Layoutable.foreground (Color.Hex 0xE6E6F0)
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
