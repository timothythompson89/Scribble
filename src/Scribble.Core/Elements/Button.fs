namespace Scribble.Core

open Scribble.Rendering

module internal ButtonType =
    let value = { BorderType.value with Name = "Button" }

/// A Border with a default look and something to click: `Button.create [ Button.text "+1";
/// Button.onClick (fun () -> dispatch Increment) ]`. Every Border setting works on it.
/// Hover and pressed looks arrive with input states in Milestone 5, and a themeable look with
/// styles in Milestone 7.
[<AbstractClass>]
type Button =
    inherit Border
    static member Type: ElementType = ButtonType.value

    /// The defaults come first, so settings you pass win.
    static member create(settings: Setting<Button> list) : View =
        View.Create(
            ButtonType.value,
            [ Border.background (Color.Hex 0x3A3A55)
              Border.borderBrush (Color.Hex 0x4A4A68)
              Border.borderThickness 1.0
              Border.cornerRadius 6.0
              Border.padding (14.0, 7.0)
              Button.horizontalAlignment HorizontalAlignment.Left ]
            @ settings
        )

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
