namespace Scribble.Core

open System
open Scribble.Accessibility
open Scribble.Rendering

module RadioGroupProps =
    /// The chosen option's value (null: none). Given by the view: controlled; otherwise its own.
    let selected: Prop<obj> = Prop.define "Selected" null Affects.Measure |> AppState.mark
    /// Called with the chosen value (boxed; the setting unboxes it).
    let internal onSelectedChanged: Prop<(obj -> unit) voption> = Prop.define "OnSelectedChanged" ValueNone Affects.Nothing
    /// Marks a RadioGroup (and types extended from it).
    let internal isGroup = Prop.define "IsRadioGroup" false Affects.Nothing

module RadioButtonProps =
    /// The value the RadioGroup reports when this option is chosen.
    let value: Prop<obj> = Prop.define "Value" null Affects.Measure
    /// Marks a RadioButton (and types extended from it).
    let internal isRadio = Prop.define "IsRadioButton" false Affects.Nothing

/// One option of a RadioGroup: a circle (a dot inside when chosen) and its label. A click chooses
/// it; it can't be unchosen by clicking (choose another). Its group decides `isChecked`.
///
///     RadioButton.create [ RadioButton.value Size.Small; RadioButton.text "Small" ]
[<AbstractClass>]
type RadioButton =
    inherit ToggleButton
    /// The value its group reports when it's chosen (compared with `Equals`).
    static member value<'k, 'T when 'k :> RadioButton>(value: 'T) : Setting<'k> = Setting.create RadioButtonProps.value (box value)

/// A StackPanel of options, one of them chosen: RadioButtons anywhere inside it (in view order).
/// One Tab stop (the chosen option, else the first); the arrow keys choose the next or previous
/// option, wrapping and skipping disabled ones.
///
///     RadioGroup.create [
///         RadioGroup.selected model.Size                          // left out: it keeps its own
///         RadioGroup.onSelectedChanged (fun (s: Size) -> dispatch (SetSize s))
///         RadioGroup.children [
///             RadioButton.create [ RadioButton.value Size.Small; RadioButton.text "Small" ]
///             RadioButton.create [ RadioButton.value Size.Large; RadioButton.text "Large" ]
///         ]
///     ]
[<AbstractClass>]
type RadioGroup =
    inherit StackPanel
    /// The chosen option's value. Given: controlled (the view decides what shows); left out: its
    /// own choice, starting with none.
    static member selected<'k, 'T when 'k :> RadioGroup>(value: 'T) : Setting<'k> = Setting.create RadioGroupProps.selected (box value)
    /// Called with the chosen option's value when the user chooses one.
    static member onSelectedChanged<'k, 'T when 'k :> RadioGroup>(handler: 'T -> unit) : Setting<'k> =
        Setting.create RadioGroupProps.onSelectedChanged (ValueSome(fun (v: obj) -> handler (unbox<'T> v)))

module internal RadioLogic =
    let isRadio (e: Element) = e.Get RadioButtonProps.isRadio
    let isGroup (e: Element) = e.Get RadioGroupProps.isGroup

    /// The RadioButtons in a group, in view order (not those of a group inside it).
    let radios (group: Element) =
        let found = ResizeArray<Element>()
        let rec walk (e: Element) =
            for c in e.Children do
                if isRadio c then found.Add c
                elif not (isGroup c) then walk c
        walk group
        List.ofSeq found

    /// The group around a radio, if any.
    let rec groupOf (e: Element) =
        match e.Parent with
        | ValueSome p when isGroup p -> ValueSome p
        | ValueSome p -> groupOf p
        | ValueNone -> ValueNone

    /// The user chose `radio`.
    let choose (radio: Element) =
        match groupOf radio with
        | ValueSome g ->
            let v = radio.Get RadioButtonProps.value
            if not (obj.Equals(g.Get RadioGroupProps.selected, v)) then
                ControlValue.change g RadioGroupProps.selected v (g.Get RadioGroupProps.onSelectedChanged)
        | ValueNone ->
            // On its own: it turns on (a lone radio can't be turned off by clicking).
            if not (radio.Get ToggleButtonProps.isChecked) then
                ControlValue.change radio ToggleButtonProps.isChecked true (radio.Get ToggleButtonProps.onCheckedChanged)

module RadioButton =
    /// The circle's width and height in DIPs, and the dot's.
    let private circle = 18.0
    let private dot = 8.0

    let private render (ctx: RenderContext) (list: DrawList) =
        let box = ToggleButton.leadRect circle circle ctx
        let round = CornerRadius.Uniform(circle / 2.0)
        ToggleButton.drawLead ctx list box round
        if ctx.Get ToggleButtonProps.isChecked then
            let inset = (circle - dot) / 2.0
            list.FillRoundedRect(Rect.Create(box.X + inset, box.Y + inset, dot, dot), CornerRadius.Uniform(dot / 2.0), ctx.Get ToggleButtonProps.markColor)
        ToggleButton.drawOverlay ctx list box round
        if ctx.IsFocusVisible then FocusRing.draw ctx list (CornerRadius.Uniform 4.0)

    let Type =
        Element.extend ToggleButton.Type "RadioButton" [
            Element.measure (ToggleButton.measureLabeled circle circle)
            Element.arrange (ToggleButton.arrangeLabeled circle)
            Element.render render
            Element.click (fun ctx _ -> RadioLogic.choose ctx.Element)
            Element.defaults (
                ToggleButton.labeledDefaults
                // The ring and the dot are the shared ones, as a CheckBox's box and tick (the dot reads on the accent a
                // chosen radio fills with).
                @ [ Setting.create RadioButtonProps.isRadio true
                    Semantics.role Role.RadioButton
                    Semantics.checkedFrom ToggleButtonProps.isChecked ]
            )
        ]

    let create (settings: Setting<RadioButton> list) : View = View.Create(Type, settings)
    /// A style for RadioButtons: `RadioButton.style [ States.isChecked ] [ RadioButton.borderBrush c ]`.
    let style (parts: SelectorPart list) (settings: Setting<RadioButton> list) : Style = Style.forType Type parts settings

module RadioGroup =
    /// Each radio shows whether it's chosen; the chosen one (else the first enabled) is the Tab stop.
    let private sync (g: Element) =
        let selected = g.Get RadioGroupProps.selected
        let radios = RadioLogic.radios g
        let chosen = radios |> List.tryFind (fun r -> obj.Equals(r.Get RadioButtonProps.value, selected))
        let stop =
            match chosen with
            | Some r when r.IsEnabled -> Some r
            | _ -> radios |> List.tryFind _.IsEnabled
        for r in radios do
            let isChecked = chosen |> Option.exists (fun c -> obj.ReferenceEquals(c, r))
            let isStop = stop |> Option.exists (fun c -> obj.ReferenceEquals(c, r))
            if r.Get ToggleButtonProps.isChecked <> isChecked || r.Get LayoutProps.isTabStop <> isStop then
                r.Set(ToggleButtonProps.isChecked, isChecked)
                r.Set(LayoutProps.isTabStop, isStop)
                match g.Root with
                | ValueSome(:? ElementTree as tree) -> tree.RunStylesWithin r
                | _ -> ()

    let private measure (ctx: MeasureContext) (available: Size) =
        sync ctx.Element
        StackPanel.Type.Measure ctx available

    /// The arrow keys choose the next or previous enabled option, wrapping.
    let private keyDown (ctx: InputContext) (args: KeyEventArgs) =
        let g = ctx.Element
        let step =
            match args.Key with
            | Key.Down
            | Key.Right -> 1
            | Key.Up
            | Key.Left -> -1
            | _ -> 0
        let plain = args.Modifiers &&& (Modifiers.Control ||| Modifiers.Alt ||| Modifiers.Meta ||| Modifiers.Shift) = Modifiers.None
        if step <> 0 && plain then
            let radios = RadioLogic.radios g |> List.filter _.IsEnabled |> Array.ofList
            let rec holdsFocus (e: Element) = e.IsFocused || (e.Children |> Seq.exists holdsFocus)
            match radios |> Array.tryFindIndex holdsFocus with
            | Some i when radios.Length > 0 ->
                args.Handle()
                let next = radios[(i + step + radios.Length) % radios.Length]
                RadioLogic.choose next
                next.Focus()
            | _ -> ()

    let Type =
        Element.extend StackPanel.Type "RadioGroup" [
            Element.measure measure
            Element.keyDown keyDown
            Element.defaults [ StackPanel.spacing 4.0; Setting.create RadioGroupProps.isGroup true; Semantics.role Role.RadioGroup ]
        ]

    let create (settings: Setting<RadioGroup> list) : View = View.Create(Type, settings)
    let style (parts: SelectorPart list) (settings: Setting<RadioGroup> list) : Style = Style.forType Type parts settings
