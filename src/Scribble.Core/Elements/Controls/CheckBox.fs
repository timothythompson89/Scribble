namespace Scribble.Core

open Scribble.Rendering

/// A box that is ticked or not, with an optional label after it; a click anywhere on either
/// flips it (a ToggleButton):
///
///     CheckBox.create [ CheckBox.text "Remember me"; CheckBox.isChecked model.Remember
///                       CheckBox.onCheckedChanged (fun on -> dispatch (SetRemember on)) ]
///
/// `isIndeterminate true` shows a dash instead ("some of these are selected"); it's for display
/// only: a click reports `true`. `background`, `borderBrush` and `cornerRadius` are the box's;
/// `markColor` is the tick's and dash's.
[<AbstractClass>]
type CheckBox =
    inherit ToggleButton

    /// Shows a dash instead of the tick: partly checked (a "select all" over a mixed selection).
    /// Display only: a click reports `true`, and the view decides what shows next.
    static member isIndeterminate<'k when 'k :> CheckBox>(value: bool) : Setting<'k> = Setting.create ToggleButtonProps.isIndeterminate value

module CheckBox =
    /// The box's width and height in DIPs.
    let private boxSize = 18.0

    /// A bold tick, in a 24 × 24 box.
    let private tick = Icon.fromPath "M4.3 12.7 6.4 10.6 9.8 14 17.6 6.2 19.7 8.3 9.8 18.2z" [ Icon.name "tick" ]

    let private render (ctx: RenderContext) (list: DrawList) =
        let box = ToggleButton.leadRect boxSize boxSize ctx
        let radius = ctx.Get BorderProps.cornerRadius
        ToggleButton.drawLead ctx list box radius
        let mark = ctx.Get ToggleButtonProps.markColor
        if ctx.Get ToggleButtonProps.isIndeterminate then
            list.FillRoundedRect(Rect.Create(box.X + 4.0, box.Y + 8.0, boxSize - 8.0, 2.0), CornerRadius.Uniform 1.0, mark)
        elif ctx.Get ToggleButtonProps.isChecked then
            ctx.Text.DrawIcon(list, Icon.geometry tick, tick.ViewBox, 0.0, false, false, box.X, box.Y, boxSize, mark)
        ToggleButton.drawOverlay ctx list box radius
        // Round the box and the label.
        if ctx.IsFocusVisible then FocusRing.draw ctx list (CornerRadius.Uniform 4.0)

    let Type =
        Element.extend ToggleButton.Type "CheckBox" [
            Element.measure (ToggleButton.measureLabeled boxSize boxSize)
            Element.arrange (ToggleButton.arrangeLabeled boxSize)
            Element.render render
            Element.defaults (ToggleButton.labeledDefaults @ [ Border.cornerRadius 3.0 ])
        ]

    let create (settings: Setting<CheckBox> list) : View = View.Create(Type, settings)
    /// A style for CheckBoxes: `CheckBox.style [ States.isChecked ] [ CheckBox.background c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<CheckBox> list) : Style = Style.forType Type parts settings
