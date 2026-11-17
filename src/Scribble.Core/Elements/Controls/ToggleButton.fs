namespace Scribble.Core

open Scribble.Accessibility
open Scribble.Rendering

/// A Button that stays on or off: a click flips it. `CheckBox` and `Switch` are ToggleButtons.
///
///     ToggleButton.create [ ToggleButton.text "Bold"; ToggleButton.isChecked model.Bold
///                           ToggleButton.onCheckedChanged (fun on -> dispatch (SetBold on)) ]
///
/// With `isChecked` it is controlled: it shows exactly that, and a click only reports the new
/// value (the view shows it once the model has it). Without, it keeps its own value. The theme
/// colors it with the accent while on (`States.isChecked` in styles).
[<AbstractClass>]
type ToggleButton =
    inherit Button

    /// On or off. Given: controlled (the control shows exactly this); left out: the control
    /// keeps its own value, starting off.
    static member isChecked<'k when 'k :> ToggleButton>(value: bool) : Setting<'k> = Setting.create ToggleButtonProps.isChecked value

    /// Called with the new value when the user flips it (click, Enter, Space).
    static member onCheckedChanged<'k when 'k :> ToggleButton>(handler: bool -> unit) : Setting<'k> =
        Setting.create ToggleButtonProps.onCheckedChanged (ValueSome handler)

    /// A CheckBox's tick and dash, a Switch's knob (the theme sets it per state).
    static member markColor<'k when 'k :> ToggleButton>(value: Color) : Setting<'k> = Setting.create ToggleButtonProps.markColor value
    static member markColor<'k when 'k :> ToggleButton>(themeValue: ThemeValue<Color>) : Setting<'k> =
        Setting.fromTheme ToggleButtonProps.markColor themeValue

module ToggleButton =
    /// A click turns it on when it's off or showing a CheckBox's dash, else off.
    let internal toggle (ctx: InputContext) (_: ClickEventArgs) =
        let e = ctx.Element
        let next = e.Get ToggleButtonProps.isIndeterminate || not (e.Get ToggleButtonProps.isChecked)
        ControlValue.change e ToggleButtonProps.isChecked next (e.Get ToggleButtonProps.onCheckedChanged)

    // CheckBox and Switch: a mark (the box, the track) `lead` DIPs wide, then the label (the child,
    // from `text` or `child`), inside the padding.

    /// Space between the mark and the label.
    let internal labelGap = 8.0

    let internal measureLabeled (leadWidth: float) (leadHeight: float) (ctx: MeasureContext) (available: Size) =
        let p = ctx.Get BorderProps.padding
        let mutable w, h = leadWidth, leadHeight
        for child in ctx.Children do
            let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - p.Horizontal - leadWidth - labelGap), max 0.0 (available.Height - p.Vertical)))
            w <- leadWidth + labelGap + d.Width
            h <- max h d.Height
        Size.Create(w + p.Horizontal, h + p.Vertical)

    /// The label sits after the mark, as wide as it asked (so a stretched control doesn't center it).
    let internal arrangeLabeled (leadWidth: float) (ctx: ArrangeContext) (size: Size) =
        let p = ctx.Get BorderProps.padding
        let x = p.Left + leadWidth + labelGap
        for child in ctx.Children do
            let d = ctx.DesiredSize child
            ctx.Arrange(child, Rect.Create(x, p.Top, min d.Width (max 0.0 (size.Width - x - p.Right)), max 0.0 (size.Height - p.Vertical)))

    /// Where the mark goes: at the left, centered on the content's height (rounded like layout).
    let internal leadRect (leadWidth: float) (leadHeight: float) (ctx: RenderContext) =
        let p = ctx.Get BorderProps.padding
        let top = System.Math.Floor(p.Top + (ctx.Size.Height - p.Vertical - leadHeight) / 2.0 + 0.5)
        Rect.Create(p.Left, top, leadWidth, leadHeight)

    /// A box or track: background, border, then the hover and press overlay.
    let internal drawLead (ctx: RenderContext) (list: DrawList) (rect: Rect) (radius: CornerRadius) =
        let thickness = ctx.Get BorderProps.borderThickness
        let background, brush = ctx.Get BorderProps.background, ctx.Get BorderProps.borderBrush
        if thickness > 0.0 && brush.A > 0.0f then list.DrawBox(rect, radius, background, thickness, brush)
        elif background.A > 0.0f then list.FillRoundedRect(rect, radius, background)

    let internal drawOverlay (ctx: RenderContext) (list: DrawList) (rect: Rect) (radius: CornerRadius) =
        let overlay = ctx.Get BorderProps.overlay
        if overlay.A > 0.0f then list.FillRoundedRect(rect, radius, overlay)

    /// CheckBox's and Switch's base look without a theme (the theme adds the state looks).
    let internal labeledDefaults: Setting<ToggleButton> list =
        [ Layoutable.foreground (Color.Hex 0xE6E6F0)
          Border.background (Color.Hex 0x3A3A55)
          Border.borderBrush (Color.Hex 0x4A4A68)
          Border.borderThickness 1.0
          Border.padding Thickness.Zero
          Setting.create BorderProps.shadow ValueNone
          ToggleButton.markColor Color.White
          // The box and its label only, so the click area doesn't span a row (Buttons stretch; 2026-11-17).
          Layoutable.horizontalAlignment HorizontalAlignment.Left ]

    let Type =
        Element.extend Button.Type "ToggleButton" [
            Element.click toggle
            Element.defaults [ Semantics.checkedFrom (ToggleButtonProps.isChecked, ToggleButtonProps.isIndeterminate) ]
        ]

    let create (settings: Setting<ToggleButton> list) : View = View.Create(Type, settings)
    /// A style for ToggleButtons (this type exactly): `ToggleButton.style [ States.isChecked ] [ ... ]`.
    let style (parts: SelectorPart list) (settings: Setting<ToggleButton> list) : Style = Style.forType Type parts settings
