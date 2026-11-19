namespace Scribble.Core

open Scribble.Accessibility

module LabelProps =
    /// The name of the element this label is for.
    let target = Prop.define "Target" "" Affects.Nothing

/// Text that names another control (DESIGN.md, "9e", decision 68), as HTML's `<label for>`:
///
///     Label.create [ Label.text "Email"; Label.target "email" ]
///     TextBox.create [ TextBox.name "email" ]
///
/// The target is the nearest element with that `name`, looking outward from the label (beside it first, then
/// beside what holds it, and so on), so each row of a repeated form finds its own. A screen reader calls the target
/// by the label's text, unless the target has a `Semantics.label` of its own; a click on the label gives the target
/// the focus. It takes every TextBlock setting.
[<AbstractClass>]
type Label =
    inherit TextBlock
    /// The `name` of the element this label is for (`TextBox.name "email"`).
    static member target<'k when 'k :> Label>(name: string) : Setting<'k> = Setting.create LabelProps.target (if isNull name then "" else name)

module Label =
    /// The element a label is for: the nearest one named as its `target` says, if there is one.
    let internal findTarget (label: Element) : Element voption =
        let name = label.Get LabelProps.target
        if name = "" then ValueNone
        else
            let rec inside (e: Element) : Element voption =
                if e.Name = name && not (obj.ReferenceEquals(e, label)) then ValueSome e
                else
                    let mutable found = ValueNone
                    let children = e.Children
                    let mutable i = 0
                    while found.IsNone && i < children.Count do
                        found <- inside children[i]
                        i <- i + 1
                    found
            // What is beside `from` (not yet looked at), then what is beside its parent, outward.
            let rec around (from: Element) : Element voption =
                match from.Parent with
                | ValueSome parent ->
                    if parent.Name = name then ValueSome parent
                    else
                        let mutable found = ValueNone
                        let children = parent.Children
                        let mutable i = 0
                        while found.IsNone && i < children.Count do
                            if not (obj.ReferenceEquals(children[i], from)) then found <- inside children[i]
                            i <- i + 1
                        match found with
                        | ValueSome _ -> found
                        | ValueNone -> around parent
                | ValueNone -> ValueNone
            around label

    let Type =
        Element.extend TextBlock.Type "Label" [
            // A click on the label is a click on what it names: the target takes the focus.
            Element.click (fun ctx _ ->
                match findTarget ctx.Element with
                | ValueSome target -> target.Focus()
                | ValueNone -> ())
        ]

    let create (settings: Setting<Label> list) : View = View.Create(Type, settings)
    /// A style for Labels (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Label> list) : Style = Style.forType Type parts settings
