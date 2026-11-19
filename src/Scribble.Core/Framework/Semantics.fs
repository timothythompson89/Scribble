namespace Scribble.Accessibility

open System.Collections.Generic
open System.Runtime.CompilerServices
open Scribble.Core

/// What kind of thing an element is to assistive technology, from ARIA's roles (plus ScrollArea,
/// which every platform has and ARIA doesn't). Grows as controls arrive; there is no custom role,
/// since screen readers only know theirs.
[<RequireQualifiedAccess>]
type Role =
    | Button
    | CheckBox
    | Switch
    /// One of a RadioGroup's options (checked when chosen).
    | RadioButton
    /// Options of which one is chosen.
    | RadioGroup
    | Slider
    /// A box showing the item chosen from a list it opens (expanded while the list shows).
    | ComboBox
    /// A box to type text in, named by its label or its placeholder.
    | TextBox
    | Tab
    | TabList
    | TabPanel
    | List
    | ListItem
    /// A list of commands (a Menu), and one of them.
    | Menu
    | MenuItem
    | ScrollArea
    /// Text on its own (a TextBlock), named by its text.
    | Text
    /// A picture; one without a name is decorative and left out.
    | Image
    | Separator
    /// An app's own container that wants a name ("Shipping address").
    | Group
    /// Content over everything else that takes all input until it closes, named by its title.
    | Dialog
    /// A short message that shows by itself (a toast), named by its label.
    | Alert

    // Written out: a union's generated ToString uses %A, which NativeAOT can't run.
    override r.ToString() =
        match r with
        | Button -> "Button"
        | CheckBox -> "CheckBox"
        | Switch -> "Switch"
        | RadioButton -> "RadioButton"
        | RadioGroup -> "RadioGroup"
        | Slider -> "Slider"
        | ComboBox -> "ComboBox"
        | TextBox -> "TextBox"
        | Tab -> "Tab"
        | TabList -> "TabList"
        | TabPanel -> "TabPanel"
        | List -> "List"
        | ListItem -> "ListItem"
        | Menu -> "Menu"
        | MenuItem -> "MenuItem"
        | ScrollArea -> "ScrollArea"
        | Text -> "Text"
        | Image -> "Image"
        | Separator -> "Separator"
        | Group -> "Group"
        | Dialog -> "Dialog"
        | Alert -> "Alert"

/// The checked state of a checkbox, switch or toggle button (pressed, for a button).
[<RequireQualifiedAccess>]
type CheckState =
    | Off
    | On
    | Mixed

    override c.ToString() =
        match c with
        | Off -> "off"
        | On -> "on"
        | Mixed -> "mixed"

/// A value in a range (a slider).
[<Struct>]
type SemanticRange =
    { Value: float
      Minimum: float
      Maximum: float
      Step: float }

/// Where an item is among its set: `Index` counts from 1 ("3 of 10,000"), even when only some
/// of the set has elements.
[<Struct>]
type SemanticPosition = { Index: int; Count: int }

/// What a scroll area shows of what it scrolls over (DIPs).
[<Struct>]
type SemanticScroll =
    { OffsetX: float
      OffsetY: float
      ExtentWidth: float
      ExtentHeight: float
      ViewportWidth: float
      ViewportHeight: float }

/// Something assistive technology can ask a semantic node to do; it takes the same path as the
/// matching input.
[<RequireQualifiedAccess>]
type SemanticAction =
    /// The element's click (`onClick` and its type's click), as a press or Enter would.
    | Click
    /// One step up (a slider's → key).
    | Increment
    /// One step down.
    | Decrement
    | Focus
    | ScrollIntoView
    /// A scroll area moves on by a page.
    | ScrollForward
    | ScrollBackward
    /// Something that opens (a ComboBox's list) shows what it holds.
    | Expand
    | Collapse

    override a.ToString() =
        match a with
        | Click -> "Click"
        | Increment -> "Increment"
        | Decrement -> "Decrement"
        | Focus -> "Focus"
        | ScrollIntoView -> "ScrollIntoView"
        | ScrollForward -> "ScrollForward"
        | ScrollBackward -> "ScrollBackward"
        | Expand -> "Expand"
        | Collapse -> "Collapse"

/// Semantics values as text, written out (records' and options' generated ToString use %A,
/// which NativeAOT can't run): for the inspector and node descriptions.
module SemanticText =
    let private n (v: float) = v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)

    let range (r: SemanticRange) = n r.Value + " (" + n r.Minimum + " to " + n r.Maximum + ", step " + n r.Step + ")"

    let orientation (o: Orientation) =
        match o with
        | Orientation.Vertical -> "vertical"
        | Orientation.Horizontal -> "horizontal"

    /// A semantics setting's value ("none" for an absent one).
    let value (v: obj) : string =
        match v with
        | null -> "none"
        | :? string as s -> if s = "" then "none" else "\"" + s + "\""
        | :? bool as b -> if b then "true" else "false"
        | :? (Role voption) as r -> (match r with ValueSome x -> x.ToString() | ValueNone -> "none")
        | :? (CheckState voption) as c -> (match c with ValueSome x -> x.ToString() | ValueNone -> "none")
        | :? (bool voption) as b -> (match b with ValueSome true -> "true" | ValueSome false -> "false" | ValueNone -> "none")
        | :? (string voption) as s -> (match s with ValueSome x -> "\"" + x + "\"" | ValueNone -> "none")
        | :? (SemanticRange voption) as r -> (match r with ValueSome x -> range x | ValueNone -> "none")
        | :? (Orientation voption) as o -> (match o with ValueSome x -> orientation x | ValueNone -> "none")
        | :? (SemanticPosition voption) as p ->
            (match p with
             | ValueSome x -> string x.Index + " of " + string x.Count
             | ValueNone -> "none")
        | :? (SemanticAction list) as l -> if l.IsEmpty then "none" else System.String.Join(", ", l |> List.map _.ToString())
        // Handlers (onAction, computed): a value option of a function.
        | v when v.GetType().IsGenericType && v.GetType().GetGenericTypeDefinition() = typedefof<int voption> -> "handler"
        | v -> v.ToString()

/// A semantics value given as a constant, or read from the element when the semantic tree is
/// asked for (from the properties named, so the inspector can say where it came from).
[<CustomEquality; NoComparison>]
type internal SemanticSource<'T> =
    | Fixed of 'T
    | Read of props: Prop[] * read: (Element -> 'T)

    member s.Of(e: Element) =
        match s with
        | Fixed v -> v
        | Read(_, f) -> f e

    override s.Equals(other: obj) =
        match s, other with
        | Fixed a, (:? SemanticSource<'T> as o) ->
            (match o with
             | Fixed b -> EqualityComparer<'T>.Default.Equals(a, b)
             | _ -> false)
        | Read(a, _), (:? SemanticSource<'T> as o) ->
            (match o with
             | Read(b, _) -> a.Length = b.Length && Array.forall2 (fun (x: Prop) y -> obj.ReferenceEquals(x, y)) a b
             | _ -> false)
        | _ -> false

    override s.GetHashCode() =
        match s with
        | Fixed v -> EqualityComparer<'T>.Default.GetHashCode v
        | Read(props, _) -> if props.Length = 0 then 0 else RuntimeHelpers.GetHashCode props[0]

    override s.ToString() =
        match s with
        | Fixed v -> SemanticText.value (box v)
        | Read(props, _) -> "from " + (props |> Array.map _.Name |> String.concat ", ")

/// The semantics properties (every element has them; they affect no layout or drawing).
module internal SemanticsProps =
    let private define name (value: 'T) = Prop.define ("Semantics." + name) value Affects.Nothing
    let role: Prop<Role voption> = define "role" ValueNone
    let label: Prop<SemanticSource<string>> = define "label" (Fixed "")
    let hidden = define "hidden" false
    let isChecked: Prop<SemanticSource<CheckState voption>> = define "checked" (Fixed ValueNone)
    let selected: Prop<SemanticSource<bool voption>> = define "selected" (Fixed ValueNone)
    let expanded: Prop<SemanticSource<bool voption>> = define "expanded" (Fixed ValueNone)
    let multiSelectable: Prop<SemanticSource<bool>> = define "multiSelectable" (Fixed false)
    let range: Prop<SemanticSource<SemanticRange voption>> = define "range" (Fixed ValueNone)
    let orientation: Prop<SemanticSource<Orientation voption>> = define "orientation" (Fixed ValueNone)
    /// A text value (what a text box holds); none for a password.
    let value: Prop<SemanticSource<string voption>> = define "value" (Fixed ValueNone)
    let readOnly: Prop<SemanticSource<bool>> = define "readOnly" (Fixed false)
    /// What it holds is hidden (a password).
    let isProtected: Prop<SemanticSource<bool>> = define "protected" (Fixed false)
    /// Its text value may hold several lines.
    let multiline: Prop<SemanticSource<bool>> = define "multiline" (Fixed false)
    let position: Prop<SemanticPosition voption> = define "position" ValueNone
    let actions: Prop<SemanticAction list> = define "actions" []
    let onAction: Prop<(InputContext -> SemanticAction -> bool) voption> = define "onAction" ValueNone
    let computed: Prop<(Element -> Setting<Layoutable> list) voption> = define "computed" ValueNone
    /// For a list (ItemsView): the position of one of its children among all its items.
    let itemPosition: Prop<(Element -> Element -> SemanticPosition voption) voption> = define "itemPosition" ValueNone

/// Semantics settings, for any element: what it is to assistive technology. Apps use them like
/// attached settings, after `open Scribble.Accessibility`:
///
///     Button.create [ Button.icon Material.close; Semantics.label "Close" ]
///
/// Element types give theirs in `Element.defaults` (`Semantics.role Role.CheckBox`), pointing at
/// the properties that hold what changes (`Semantics.checkedFrom (ToggleButtonProps.isChecked, ...)`).
[<AbstractClass; Sealed>]
type Semantics =
    /// What it is. Elements without a role (layout panels, plain Borders) are left out of the
    /// semantic tree, and what's inside them takes their place.
    static member role<'k when 'k :> Layoutable>(value: Role) : Setting<'k> = Setting.create SemanticsProps.role (ValueSome value)
    /// What a screen reader calls it, in place of its text ("Close" for an icon-only button).
    static member label<'k when 'k :> Layoutable>(value: string) : Setting<'k> = Setting.create SemanticsProps.label (Fixed value)
    /// Its name, read from one of its properties when asked (a TextBlock's text).
    static member labelFrom<'k when 'k :> Layoutable>(prop: Prop<string>) : Setting<'k> =
        Setting.create SemanticsProps.label (Read([| prop |], fun e -> e.Get prop))
    /// True leaves it and everything inside it out (decorations, text said elsewhere).
    static member hidden<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create SemanticsProps.hidden value
    /// Checked (a checkbox, switch, or a pressed toggle button).
    static member isChecked<'k when 'k :> Layoutable>(value: bool) : Setting<'k> =
        Setting.create SemanticsProps.isChecked (Fixed(ValueSome(if value then CheckState.On else CheckState.Off)))
    static member isChecked<'k when 'k :> Layoutable>(value: CheckState) : Setting<'k> =
        Setting.create SemanticsProps.isChecked (Fixed(ValueSome value))
    /// Checked as two properties say: `isChecked`, and `isIndeterminate` for mixed.
    static member checkedFrom<'k when 'k :> Layoutable>(isChecked: Prop<bool>, isIndeterminate: Prop<bool>) : Setting<'k> =
        Setting.create SemanticsProps.isChecked (
            Read([| isChecked; isIndeterminate |], fun e ->
                ValueSome(if e.Get isIndeterminate then CheckState.Mixed elif e.Get isChecked then CheckState.On else CheckState.Off)))
    /// Checked as one property says (on or off).
    static member checkedFrom<'k when 'k :> Layoutable>(isChecked: Prop<bool>) : Setting<'k> =
        Setting.create SemanticsProps.isChecked (Read([| isChecked |], fun e -> ValueSome(if e.Get isChecked then CheckState.On else CheckState.Off)))
    /// Selected (a tab, a list item).
    static member isSelected<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create SemanticsProps.selected (Fixed(ValueSome value))
    static member selectedFrom<'k when 'k :> Layoutable>(prop: Prop<bool>) : Setting<'k> =
        Setting.create SemanticsProps.selected (Read([| prop |], fun e -> ValueSome(e.Get prop)))
    /// A list in which more than one item can be selected.
    static member isMultiSelectable<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create SemanticsProps.multiSelectable (Fixed value)
    /// Read from a property of the element's own (a ListBox's selection mode).
    static member internal multiSelectableFrom<'k, 'T when 'k :> Layoutable>(prop: Prop<'T>, isMulti: 'T -> bool) : Setting<'k> =
        Setting.create SemanticsProps.multiSelectable (Read([| prop :> Prop |], fun e -> isMulti (e.Get prop)))
    /// Expanded or collapsed (something that opens).
    static member isExpanded<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create SemanticsProps.expanded (Fixed(ValueSome value))
    /// Read from a property of the element's own (a ComboBox's open list).
    static member internal expandedFrom<'k when 'k :> Layoutable>(prop: Prop<bool>) : Setting<'k> =
        Setting.create SemanticsProps.expanded (Read([| prop |], fun e -> ValueSome(e.Get prop)))
    /// A value in a range (a slider of the app's own).
    static member range<'k when 'k :> Layoutable>(value: float, minimum: float, maximum: float, step: float) : Setting<'k> =
        Setting.create SemanticsProps.range (Fixed(ValueSome { Value = value; Minimum = minimum; Maximum = maximum; Step = step }))
    /// A range read from four properties.
    static member rangeFrom<'k when 'k :> Layoutable>(value: Prop<float>, minimum: Prop<float>, maximum: Prop<float>, step: Prop<float>) : Setting<'k> =
        Setting.create SemanticsProps.range (
            Read([| value; minimum; maximum; step |], fun e ->
                ValueSome { Value = e.Get value; Minimum = e.Get minimum; Maximum = e.Get maximum; Step = e.Get step }))
    static member orientation<'k when 'k :> Layoutable>(value: Orientation) : Setting<'k> =
        Setting.create SemanticsProps.orientation (Fixed(ValueSome value))
    static member orientationFrom<'k when 'k :> Layoutable>(prop: Prop<Orientation>) : Setting<'k> =
        Setting.create SemanticsProps.orientation (Read([| prop |], fun e -> ValueSome(e.Get prop)))
    /// Its place in its set: `index` from 1, of `count` ("tab 2 of 4").
    /// Its text value (what a text box holds), for an element type of your own that edits text.
    static member value<'k when 'k :> Layoutable>(text: string) : Setting<'k> = Setting.create SemanticsProps.value (Fixed(ValueSome text))
    /// Its text value, read from one of its properties when asked.
    static member valueFrom<'k when 'k :> Layoutable>(prop: Prop<string>) : Setting<'k> =
        Setting.create SemanticsProps.value (Read([| prop |], fun e -> ValueSome(e.Get prop)))
    /// Its value can be read but not changed.
    static member isReadOnly<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create SemanticsProps.readOnly (Fixed value)
    static member readOnlyFrom<'k when 'k :> Layoutable>(prop: Prop<bool>) : Setting<'k> =
        Setting.create SemanticsProps.readOnly (Read([| prop |], fun e -> e.Get prop))
    /// Its text value may hold several lines (a text box of your own that takes Enter).
    static member isMultiline<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create SemanticsProps.multiline (Fixed value)
    static member multilineFrom<'k when 'k :> Layoutable>(prop: Prop<bool>) : Setting<'k> =
        Setting.create SemanticsProps.multiline (Read([| prop |], fun e -> e.Get prop))
    static member position<'k when 'k :> Layoutable>(index: int, count: int) : Setting<'k> =
        Setting.create SemanticsProps.position (ValueSome { Index = index; Count = count })
    /// Actions it supports beyond the ones it gets by itself (click when it has one, focus when
    /// focusable, scroll into view; scrolling for a scroll area): performed by `onAction`.
    static member actions<'k when 'k :> Layoutable>(value: SemanticAction list) : Setting<'k> = Setting.create SemanticsProps.actions value
    /// Performs an action of `actions` (returns whether it did).
    static member onAction<'k when 'k :> Layoutable>(handler: InputContext -> SemanticAction -> bool) : Setting<'k> =
        Setting.create SemanticsProps.onAction (ValueSome handler)
    /// Semantics worked out when asked (when they can't be read from one property each): its
    /// settings take the place of the element's own.
    static member computed<'k when 'k :> Layoutable>(f: Element -> Setting<Layoutable> list) : Setting<'k> =
        Setting.create SemanticsProps.computed (ValueSome f)
    /// For a list's element type: where a child is among all the items.
    static member internal itemPosition<'k when 'k :> Layoutable>(f: Element -> Element -> SemanticPosition voption) : Setting<'k> =
        Setting.create SemanticsProps.itemPosition (ValueSome f)
