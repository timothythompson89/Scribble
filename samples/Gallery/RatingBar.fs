/// A control of the Gallery's own, written as an app or a library would write one: with Scribble's public API
/// only (the samples can't see its internals). The Controls page shows it. The pattern, in the order below: the
/// properties, the settings type, then the element in a module of the same name.
namespace Scribble.Samples.Gallery

open Scribble.Rendering
open Scribble.Core
open Scribble.Accessibility

/// The properties: typed keys, each with a neutral default and what a change to it affects.
module RatingBarProps =
    /// The value is app state: it comes from the app through the view, and a style can't set it.
    let value = Prop.appState "Value" 0.0 Affects.Render
    let maximum = Prop.define "Maximum" 5.0 Affects.Measure
    let onValueChanged: Prop<(float -> unit) voption> = Prop.define "OnValueChanged" ValueNone Affects.Nothing
    /// A dot's colour, empty and filled.
    let empty = Prop.define "Empty" Color.Transparent Affects.Render
    let filled = Prop.define "Filled" Color.Transparent Affects.Render
    /// For a screen reader's range: where it starts and how far one step goes. Never set.
    let minimum = Prop.define "Minimum" 0.0 Affects.Nothing
    let step = Prop.define "Step" 1.0 Affects.Nothing

/// A rating out of `maximum` (5), as a row of dots: a click on a dot gives that many (on the first again, none),
/// and the arrow keys move by one.
///
///     RatingBar.create [ RatingBar.value model.Stars; RatingBar.onValueChanged (fun n -> dispatch (SetStars n)) ]
///
/// With `value` it is controlled: it shows exactly that and reports what the user picks. Without, it keeps its
/// own value.
[<AbstractClass>]
type RatingBar =
    inherit Layoutable
    /// The rating. Given: controlled (it shows exactly this); left out: its own value.
    static member value<'k when 'k :> RatingBar>(value: int) : Setting<'k> = Setting.create RatingBarProps.value (float value)
    static member maximum<'k when 'k :> RatingBar>(value: int) : Setting<'k> = Setting.create RatingBarProps.maximum (float value)
    /// Called with the new rating on every change the user makes.
    static member onValueChanged<'k when 'k :> RatingBar>(handler: int -> unit) : Setting<'k> =
        Setting.create RatingBarProps.onValueChanged (ValueSome(fun v -> handler (int v)))
    // A colour setting takes a fixed colour or a theme value.
    static member empty<'k when 'k :> RatingBar>(value: Color) : Setting<'k> = Setting.create RatingBarProps.empty value
    static member empty<'k when 'k :> RatingBar>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme RatingBarProps.empty themeValue
    static member filled<'k when 'k :> RatingBar>(value: Color) : Setting<'k> = Setting.create RatingBarProps.filled value
    static member filled<'k when 'k :> RatingBar>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme RatingBarProps.filled themeValue

module RatingBar =
    /// A dot's diameter and the space between two, in DIPs.
    let private dot = 18.0
    let private gap = 6.0

    /// A state of the control's own, for apps to style (`Style.onState RatingBar.full [ ... ]`): every dot is
    /// filled. It reads two properties, so it says which and when; it is matched again when either changes.
    let full =
        States.define "full" [
            States.dependsOn RatingBarProps.value
            States.dependsOn RatingBarProps.maximum
            States.where (fun v -> v.Get RatingBarProps.value >= v.Get RatingBarProps.maximum)
        ]

    /// The user picked `v`: the control's own value follows unless the view gives one, then the handler runs.
    let private pick (e: Element) (v: float) =
        let v = max 0.0 (min (e.Get RatingBarProps.maximum) v)
        if v <> e.Get RatingBarProps.value then
            ControlValue.change e RatingBarProps.value v (e.Get RatingBarProps.onValueChanged)

    let private pointerDown (ctx: InputContext) (args: PointerEventArgs) =
        if args.Button = ValueSome MouseButton.Left then
            args.Handle()
            let e = ctx.Element
            let clicked = floor (args.LocalPosition.X / (dot + gap)) + 1.0
            // The first dot again takes the rating away.
            pick e (if clicked = 1.0 && e.Get RatingBarProps.value = 1.0 then 0.0 else clicked)

    let private keyDown (ctx: InputContext) (args: KeyEventArgs) =
        let e = ctx.Element
        let current = e.Get RatingBarProps.value
        match args.Key with
        | Key.Right
        | Key.Up ->
            args.Handle()
            pick e (current + 1.0)
        | Key.Left
        | Key.Down ->
            args.Handle()
            pick e (current - 1.0)
        | _ -> ()

    /// Increment and decrement, from assistive technology: the arrow keys' path.
    let private semanticAction (ctx: InputContext) (action: SemanticAction) =
        let e = ctx.Element
        match action with
        | SemanticAction.Increment ->
            pick e (e.Get RatingBarProps.value + 1.0)
            true
        | SemanticAction.Decrement ->
            pick e (e.Get RatingBarProps.value - 1.0)
            true
        | _ -> false

    let private width (count: float) = if count <= 0.0 then 0.0 else count * dot + (count - 1.0) * gap

    let private render (ctx: RenderContext) (list: DrawList) =
        let value = ctx.Get RatingBarProps.value
        let count = int (ctx.Get RatingBarProps.maximum)
        let round = CornerRadius.Uniform(dot / 2.0)
        for i in 0 .. count - 1 do
            let color = if float i < value then ctx.Get RatingBarProps.filled else ctx.Get RatingBarProps.empty
            list.FillRoundedRect(Rect.Create(float i * (dot + gap), 0.0, dot, dot), round, color)
        if ctx.IsFocusVisible then
            FocusRing.drawAround list (Rect.Create(0.0, 0.0, width (float count), dot)) round (ctx.Get FocusRing.colorProp)

    /// Defined once: the differ compares element types by reference.
    let Type =
        Element.define "RatingBar" [
            Element.measure (fun ctx _ -> Size.Create(width (ctx.Get RatingBarProps.maximum), dot))
            Element.render render
            Element.pointerDown pointerDown
            Element.keyDown keyDown
            Element.defaults [
                // Behaviour, semantics and the look at rest; colours as theme values, never literals.
                Layoutable.focusable true
                Layoutable.cursor Cursor.Hand
                // Its click area is its dots, not the row it sits in.
                Layoutable.horizontalAlignment HorizontalAlignment.Left
                RatingBar.empty Palette.border
                RatingBar.filled Palette.accent
                Semantics.role Role.Slider
                Semantics.rangeFrom (RatingBarProps.value, RatingBarProps.minimum, RatingBarProps.maximum, RatingBarProps.step)
                Semantics.actions [ SemanticAction.Increment; SemanticAction.Decrement ]
                Semantics.onAction semanticAction
            ]
        ]

    let create (settings: Setting<RatingBar> list) : View = View.Create(Type, settings)
    /// A style for RatingBars: `RatingBar.style [] [ Style.onState RatingBar.full [ RatingBar.filled c ] ]`.
    let style (parts: SelectorPart list) (settings: Setting<RatingBar> list) : Style = Style.forType Type parts settings
