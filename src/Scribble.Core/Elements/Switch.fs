namespace Scribble.Core

open System
open Scribble.Rendering

/// An on/off switch: a track with a knob that slides across, with an optional label after it; a
/// click anywhere on either flips it (a ToggleButton):
///
///     Switch.create [ Switch.text "Dark theme"; Switch.isChecked model.Dark
///                     Switch.onCheckedChanged (fun on -> dispatch (SetDark on)) ]
///
/// `background` and `borderBrush` are the track's, `markColor` the knob's.
[<AbstractClass>]
type Switch =
    inherit ToggleButton

module Switch =
    let private trackWidth = 36.0
    let private trackHeight = 20.0
    let private knob = 14.0
    /// How long the knob takes to slide across.
    let private slideTime = TimeSpan.FromMilliseconds 120.0

    let private render (ctx: RenderContext) (list: DrawList) =
        let track = ToggleButton.leadRect trackWidth trackHeight ctx
        let pill = CornerRadius.Uniform(trackHeight / 2.0)
        ToggleButton.drawLead ctx list track pill
        // 0 at the left (off), 1 at the right (on); the first value is taken at once.
        let at = ctx.Animate("knob", (if ctx.Get ToggleButtonProps.isChecked then 1.0 else 0.0), slideTime)
        let inset = (trackHeight - knob) / 2.0
        let x = track.X + inset + at * (trackWidth - trackHeight)
        list.FillRoundedRect(Rect.Create(x, track.Y + inset, knob, knob), CornerRadius.Uniform(knob / 2.0), ctx.Get ToggleButtonProps.markColor)
        ToggleButton.drawOverlay ctx list track pill
        if ctx.IsFocusVisible then FocusRing.draw ctx list (CornerRadius.Uniform(trackHeight / 2.0))

    let Type =
        Element.extend ToggleButton.Type "Switch" [
            Element.measure (ToggleButton.measureLabeled trackWidth trackHeight)
            Element.arrange (ToggleButton.arrangeLabeled trackWidth)
            Element.render render
            Element.defaults (ToggleButton.labeledDefaults @ [ ToggleButton.markColor (Color.Hex 0x9A9AB0) ])
        ]

    let create (settings: Setting<Switch> list) : View = View.Create(Type, settings)
    /// A style for Switches: `Switch.style [ States.isChecked ] [ Switch.background c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Switch> list) : Style = Style.forType Type parts settings
