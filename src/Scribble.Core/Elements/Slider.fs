namespace Scribble.Core

open System
open Scribble.Rendering

module SliderProps =
    /// The value. Given by the view: controlled; otherwise the control's own value.
    let value = Prop.define "Value" 0.0 Affects.Render
    let minimum = Prop.define "Minimum" 0.0 Affects.Render
    let maximum = Prop.define "Maximum" 100.0 Affects.Render
    /// Values snap to multiples of this from the minimum; 0 means continuous.
    let step = Prop.define "Step" 1.0 Affects.Render
    let orientation = Prop.define "Orientation" Orientation.Horizontal Affects.Measure
    let onValueChanged: Prop<(float -> unit) voption> = Prop.define "OnValueChanged" ValueNone Affects.Nothing
    let onValueCommitted: Prop<(float -> unit) voption> = Prop.define "OnValueCommitted" ValueNone Affects.Nothing
    /// The track's empty part.
    let track = Prop.define "Track" (Color.Hex 0x3A3A55) Affects.Render
    /// The track from the minimum up to the value.
    let fill = Prop.define "Fill" (Color.Hex 0x3D59A1) Affects.Render
    let thumb = Prop.define "Thumb" Color.White Affects.Render
    /// The thumb's edge.
    let thumbBorder = Prop.define "ThumbBorder" (Color.Hex 0x4A4A68) Affects.Render
    let thumbShadow: Prop<BoxShadow voption> = Prop.define "ThumbShadow" ValueNone Affects.Render
    /// Laid over the thumb: the theme tints it on hover and while pressed.
    let overlay = Prop.define "Overlay" Color.Transparent Affects.Render
    /// During a drag: the last value reported (NaN: none yet), and where on the thumb it was taken.
    let internal reported = Prop.local "Reported" Double.NaN
    let internal grab = Prop.local "Grab" 0.0
    let internal dragging = Prop.local "Dragging" false

/// A value picked by sliding a thumb along a track (or with the arrow keys):
///
///     Slider.create [ Slider.value model.Volume; Slider.onValueChanged (fun v -> dispatch (SetVolume v)) ]
///
/// From `minimum` (0) to `maximum` (100) in `step`s (1; 0 for continuous). With `value` it is
/// controlled: it shows exactly that and reports where the user moves it; without, it keeps its own
/// value. `onValueChanged` runs on every change (each drag move, each key step), `onValueCommitted`
/// once the user lets go (a drag ends, a key step lands). ←/↓ and →/↑ move one step (1% when
/// continuous), PageDown/PageUp 10%, Home/End to the ends; a press on the track jumps there and
/// drags on. `orientation Vertical` puts the minimum at the bottom.
[<AbstractClass>]
type Slider =
    inherit Layoutable
    /// The value. Given: controlled (the slider shows exactly this); left out: its own value.
    static member value<'k when 'k :> Slider>(value: float) : Setting<'k> = Setting.create SliderProps.value value
    static member minimum<'k when 'k :> Slider>(value: float) : Setting<'k> = Setting.create SliderProps.minimum value
    static member maximum<'k when 'k :> Slider>(value: float) : Setting<'k> = Setting.create SliderProps.maximum value
    /// Values snap to multiples of this from the minimum (default 1); 0 means continuous.
    static member step<'k when 'k :> Slider>(value: float) : Setting<'k> = Setting.create SliderProps.step value
    /// Horizontal (default; minimum at the left) or Vertical (minimum at the bottom).
    static member orientation<'k when 'k :> Slider>(value: Orientation) : Setting<'k> = Setting.create SliderProps.orientation value
    /// Called with the new value on every change the user makes (each drag move, each key step).
    static member onValueChanged<'k when 'k :> Slider>(handler: float -> unit) : Setting<'k> = Setting.create SliderProps.onValueChanged (ValueSome handler)
    /// Called with the final value when the user lets go: a drag ends, or a key step lands.
    static member onValueCommitted<'k when 'k :> Slider>(handler: float -> unit) : Setting<'k> = Setting.create SliderProps.onValueCommitted (ValueSome handler)
    static member track<'k when 'k :> Slider>(value: Color) : Setting<'k> = Setting.create SliderProps.track value
    static member track<'k when 'k :> Slider>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme SliderProps.track themeValue
    static member fill<'k when 'k :> Slider>(value: Color) : Setting<'k> = Setting.create SliderProps.fill value
    static member fill<'k when 'k :> Slider>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme SliderProps.fill themeValue
    static member thumb<'k when 'k :> Slider>(value: Color) : Setting<'k> = Setting.create SliderProps.thumb value
    static member thumb<'k when 'k :> Slider>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme SliderProps.thumb themeValue
    static member thumbBorder<'k when 'k :> Slider>(value: Color) : Setting<'k> = Setting.create SliderProps.thumbBorder value
    static member thumbBorder<'k when 'k :> Slider>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme SliderProps.thumbBorder themeValue
    static member thumbShadow<'k when 'k :> Slider>(value: BoxShadow) : Setting<'k> = Setting.create SliderProps.thumbShadow (ValueSome value)
    static member thumbShadow<'k when 'k :> Slider>(themeValue: ThemeValue<BoxShadow>) : Setting<'k> =
        Setting.fromThemeWith SliderProps.thumbShadow themeValue ThemeConvert.someShadow
    /// A translucent color over the thumb (the theme tints it on hover and while pressed).
    static member overlay<'k when 'k :> Slider>(value: Color) : Setting<'k> = Setting.create SliderProps.overlay value
    static member overlay<'k when 'k :> Slider>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme SliderProps.overlay themeValue

module Slider =
    /// The thumb's diameter and the track's thickness, in DIPs.
    let private thumbSize = 18.0
    let private trackThickness = 4.0
    /// The length it asks for when nothing stretches it.
    let private preferredLength = 120.0

    let private isVertical (e: Element) = e.Get SliderProps.orientation = Orientation.Vertical

    /// `v` inside the range, on a step.
    let internal snap (e: Element) (v: float) =
        let low, high = e.Get SliderProps.minimum, e.Get SliderProps.maximum
        let high = max low high
        let step = e.Get SliderProps.step
        let v = max low (min high v)
        if step > 0.0 then max low (min high (low + Math.Round((v - low) / step) * step)) else v

    /// Where the value is along the range, 0 to 1.
    let private fraction (e: Element) (v: float) =
        let low, high = e.Get SliderProps.minimum, e.Get SliderProps.maximum
        if high <= low then 0.0 else max 0.0 (min 1.0 ((v - low) / (high - low)))

    /// The thumb's center along the slider (DIPs from the left, or from the top when vertical).
    let private thumbCenter (e: Element) (size: Size) =
        let f = fraction e (e.Get SliderProps.value)
        if isVertical e then size.Height - thumbSize / 2.0 - f * (size.Height - thumbSize)
        else thumbSize / 2.0 + f * (size.Width - thumbSize)

    /// The value under a point along the slider (the thumb's center there).
    let private valueAt (e: Element) (size: Size) (along: float) =
        let usable = (if isVertical e then size.Height else size.Width) - thumbSize
        let f =
            if usable <= 0.0 then 0.0
            elif isVertical e then (size.Height - thumbSize / 2.0 - along) / usable
            else (along - thumbSize / 2.0) / usable
        let low, high = e.Get SliderProps.minimum, e.Get SliderProps.maximum
        snap e (low + max 0.0 (min 1.0 f) * (max 0.0 (high - low)))

    /// The user moved it to `v`: reported once per new value (a controlled slider that the view
    /// doesn't follow keeps showing the old value, but isn't told the same value twice).
    let private report (e: Element) (v: float) =
        let last = e.Get SliderProps.reported
        let previous = if Double.IsNaN last then e.Get SliderProps.value else last
        if v <> previous then
            e.SetLocal(SliderProps.reported, v)
            ControlValue.change e SliderProps.value v (e.Get SliderProps.onValueChanged)

    let private commit (e: Element) =
        let last = e.Get SliderProps.reported
        e.SetLocal(SliderProps.reported, Double.NaN)
        if not (Double.IsNaN last) then
            match e.Get SliderProps.onValueCommitted with
            | ValueSome h -> h last
            | ValueNone -> ()

    let private along (e: Element) (p: Point) = if isVertical e then p.Y else p.X

    let private pointerDown (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        if args.Button = ValueSome MouseButton.Left then
            args.Handle()
            let at = along e args.LocalPosition
            let center = thumbCenter e ctx.Size
            // On the thumb: drag it from where it was taken; on the track: jump there.
            let grab = if abs (at - center) <= thumbSize / 2.0 then at - center else 0.0
            e.SetLocal(SliderProps.grab, grab)
            e.SetLocal(SliderProps.dragging, true)
            e.SetLocal(SliderProps.reported, Double.NaN)
            report e (valueAt e ctx.Size (at - grab))
            ctx.InvalidateVisual()

    let private pointerMove (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        if e.Get SliderProps.dragging then
            args.Handle()
            report e (valueAt e ctx.Size (along e args.LocalPosition - e.Get SliderProps.grab))

    let private pointerUp (ctx: InputContext) (args: PointerEventArgs) =
        let e = ctx.Element
        if e.Get SliderProps.dragging then
            args.Handle()
            e.SetLocal(SliderProps.dragging, false)
            commit e
            ctx.InvalidateVisual()

    let private keyDown (ctx: InputContext) (args: KeyEventArgs) =
        let e = ctx.Element
        let low, high = e.Get SliderProps.minimum, e.Get SliderProps.maximum
        let range = max 0.0 (high - low)
        let step = e.Get SliderProps.step
        let small = if step > 0.0 then step else range / 100.0
        let large = max small (range / 10.0)
        let current = e.Get SliderProps.value
        let target =
            match args.Key with
            | Key.Left | Key.Down -> ValueSome(current - small)
            | Key.Right | Key.Up -> ValueSome(current + small)
            | Key.PageDown -> ValueSome(current - large)
            | Key.PageUp -> ValueSome(current + large)
            | Key.Home -> ValueSome low
            | Key.End -> ValueSome high
            | _ -> ValueNone
        match target with
        | ValueSome t when args.Modifiers &&& (Modifiers.Control ||| Modifiers.Alt ||| Modifiers.Meta) = Modifiers.None ->
            args.Handle()
            e.SetLocal(SliderProps.reported, Double.NaN)
            report e (snap e t)
            commit e
        | _ -> ()

    let private render (ctx: RenderContext) (list: DrawList) =
        let e = ctx.Element
        let size = ctx.Size
        let vertical = isVertical e
        let center = thumbCenter e size
        let half = thumbSize / 2.0
        let radius = CornerRadius.Uniform(trackThickness / 2.0)
        let track, fill =
            if vertical then
                let x = Math.Floor((size.Width - trackThickness) / 2.0 + 0.5)
                Rect.Create(x, half, trackThickness, max 0.0 (size.Height - thumbSize)),
                Rect.Create(x, center, trackThickness, max 0.0 (size.Height - half - center))
            else
                let y = Math.Floor((size.Height - trackThickness) / 2.0 + 0.5)
                Rect.Create(half, y, max 0.0 (size.Width - thumbSize), trackThickness),
                Rect.Create(half, y, max 0.0 (center - half), trackThickness)
        list.FillRoundedRect(track, radius, ctx.Get SliderProps.track)
        if fill.Width > 0.0 && fill.Height > 0.0 then list.FillRoundedRect(fill, radius, ctx.Get SliderProps.fill)
        let thumb =
            if vertical then Rect.Create((size.Width - thumbSize) / 2.0, center - half, thumbSize, thumbSize)
            else Rect.Create(center - half, (size.Height - thumbSize) / 2.0, thumbSize, thumbSize)
        let round = CornerRadius.Uniform half
        match ctx.Get SliderProps.thumbShadow with
        | ValueSome s -> list.DrawShadow(thumb, round, s.Blur, s.Color, s.OffsetX, s.OffsetY, s.Spread)
        | ValueNone -> ()
        list.DrawBox(thumb, round, ctx.Get SliderProps.thumb, 1.0, ctx.Get SliderProps.thumbBorder)
        let overlay = ctx.Get SliderProps.overlay
        if overlay.A > 0.0f then list.FillRoundedRect(thumb, round, overlay)
        // The ring goes round the thumb, where the keys act.
        if ctx.IsFocusVisible then FocusRing.drawAround list thumb round (ctx.Get FocusRing.colorProp)

    let Type =
        Element.define "Slider" [
            Element.measure (fun ctx _ ->
                if ctx.Get SliderProps.orientation = Orientation.Vertical then Size.Create(thumbSize + 6.0, preferredLength)
                else Size.Create(preferredLength, thumbSize + 6.0))
            Element.render render
            Element.pointerDown pointerDown
            Element.pointerMove pointerMove
            Element.pointerUp pointerUp
            Element.keyDown keyDown
            Element.defaults [ Layoutable.focusable true; Layoutable.cursor Cursor.Hand ]
        ]

    let create (settings: Setting<Slider> list) : View = View.Create(Type, settings)
    /// A style for Sliders: `Slider.style [ States.hover ] [ Slider.thumb c ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Slider> list) : Style = Style.forType Type parts settings
