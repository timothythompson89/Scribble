namespace Scribble.Accessibility.AccessKit

open System
open Scribble.Accessibility
open Scribble.Core

/// AccessKit's roles, the ones Scribble uses, by their numbers in accesskit.h (0.23.2: `accesskit_role`).
module internal AkRole =
    let Label = 3
    let Image = 4
    let ListItem = 7
    let ListBoxOption = 10
    let MenuItem = 11
    let CheckBox = 15
    let RadioButton = 16
    let TextInput = 17
    let Button = 18
    let List = 24
    let Switch = 29
    let Menu = 30
    let MultilineTextInput = 31
    let PasswordInput = 40
    let Alert = 44
    let ComboBox = 56
    let Dialog = 66
    let Group = 78
    let ListBox = 87
    let MenuItemCheckBox = 94
    let RadioGroup = 102
    let ScrollView = 108
    let Slider = 113
    let Splitter = 115
    let Tab = 120
    let TabList = 121
    let TabPanel = 122
    let Window = 133

/// AccessKit's actions (`accesskit_action`).
module internal AkAction =
    let Click = 0
    let Focus = 1
    let Collapse = 3
    let Expand = 4
    let Decrement = 6
    let Increment = 7
    let ScrollDown = 11
    let ScrollUp = 14
    let ScrollIntoView = 15

/// One node as AccessKit is given it: what the native layer writes out, call by call.
type internal AkNode =
    { Id: uint64
      Role: int
      /// Its name ("": none).
      Label: string
      /// Its text value (a text box's).
      Value: string voption
      Children: uint64[]
      /// In the window, in physical pixels: left, top, right, bottom.
      X0: float
      Y0: float
      X1: float
      Y1: float
      Actions: int list
      /// `accesskit_toggled`: 0 false, 1 true, 2 mixed.
      Toggled: int voption
      Selected: bool voption
      Expanded: bool voption
      Disabled: bool
      ReadOnly: bool
      MultiSelectable: bool
      Modal: bool
      ClipsChildren: bool
      Range: SemanticRange voption
      /// `accesskit_orientation`: 0 horizontal, 1 vertical.
      Orientation: int voption
      Position: SemanticPosition voption
      Scroll: SemanticScroll voption }

/// A whole tree for AccessKit: the window's node first, then every semantic node.
type internal AkUpdate =
    { Root: uint64
      Focus: uint64
      Nodes: AkNode[] }

/// A window's semantic tree as AccessKit wants it (DESIGN.md, "AccessKit: the screen-reader bridge", decisions 11
/// to 13). No native calls: tested on the fake platform.
module internal Translation =
    /// The window's own node. Semantic nodes' ids are never 0 or 1.
    let rootId = 1UL

    /// AccessKit's role for a semantic node.
    let roleOf (n: SemanticNode) : int =
        match n.Role with
        | Role.Button -> AkRole.Button
        | Role.CheckBox -> AkRole.CheckBox
        | Role.Switch -> AkRole.Switch
        | Role.RadioButton -> AkRole.RadioButton
        | Role.RadioGroup -> AkRole.RadioGroup
        | Role.Slider -> AkRole.Slider
        | Role.ComboBox -> AkRole.ComboBox
        // By what it holds: a password, several lines, or one.
        | Role.TextBox -> if n.IsProtected then AkRole.PasswordInput elif n.IsMultiline then AkRole.MultilineTextInput else AkRole.TextInput
        | Role.Tab -> AkRole.Tab
        | Role.TabList -> AkRole.TabList
        | Role.TabPanel -> AkRole.TabPanel
        // A list whose items can be selected is a list box of options; otherwise a plain list of items.
        | Role.List -> if n.IsMultiSelectable || n.Children |> Seq.exists (fun c -> c.IsSelected.IsSome) then AkRole.ListBox else AkRole.List
        | Role.ListItem -> if n.IsSelected.IsSome then AkRole.ListBoxOption else AkRole.ListItem
        | Role.Menu -> AkRole.Menu
        | Role.MenuItem -> if n.IsChecked.IsSome then AkRole.MenuItemCheckBox else AkRole.MenuItem
        | Role.ScrollArea -> AkRole.ScrollView
        | Role.Text -> AkRole.Label
        | Role.Image -> AkRole.Image
        // AccessKit has no separator of its own: its splitter is what platforms show as one.
        | Role.Separator -> AkRole.Splitter
        | Role.Group -> AkRole.Group
        | Role.Dialog -> AkRole.Dialog
        | Role.Alert -> AkRole.Alert

    /// AccessKit's action for one of ours (decision 11: our nine).
    let actionOf (a: SemanticAction) : int =
        match a with
        | SemanticAction.Click -> AkAction.Click
        | SemanticAction.Focus -> AkAction.Focus
        | SemanticAction.Increment -> AkAction.Increment
        | SemanticAction.Decrement -> AkAction.Decrement
        | SemanticAction.ScrollIntoView -> AkAction.ScrollIntoView
        | SemanticAction.ScrollForward -> AkAction.ScrollDown
        | SemanticAction.ScrollBackward -> AkAction.ScrollUp
        | SemanticAction.Expand -> AkAction.Expand
        | SemanticAction.Collapse -> AkAction.Collapse

    /// Ours for one AccessKit asks for, among those `node` supports (none: not one of ours, or not supported).
    let semanticAction (node: SemanticNode) (action: int) : SemanticAction voption =
        node.Actions |> List.tryFind (fun a -> actionOf a = action) |> Option.toValueOption

    let private nodeOf (scale: float) (n: SemanticNode) : AkNode =
        let b = n.Bounds
        // AccessKit reads a label's text from its value, not its label (seen through UI Automation: a label
        // given only a label has no name).
        let isText = n.Role = Role.Text
        { Id = n.Id
          Role = roleOf n
          Label = (if isText then "" else n.Name)
          Value = (if isText then ValueSome n.Name else n.Value)
          Children = [| for c in n.Children -> c.Id |]
          X0 = b.X * scale
          Y0 = b.Y * scale
          X1 = (b.X + b.Width) * scale
          Y1 = (b.Y + b.Height) * scale
          Actions = n.Actions |> List.map actionOf
          Toggled =
            (match n.IsChecked with
             | ValueSome CheckState.Off -> ValueSome 0
             | ValueSome CheckState.On -> ValueSome 1
             | ValueSome CheckState.Mixed -> ValueSome 2
             | ValueNone -> ValueNone)
          Selected = n.IsSelected
          Expanded = n.IsExpanded
          Disabled = n.IsDisabled
          ReadOnly = n.IsReadOnly
          MultiSelectable = n.IsMultiSelectable
          Modal = (n.Role = Role.Dialog)
          // What a scroll area scrolls over shows only inside it.
          ClipsChildren = n.Scroll.IsSome
          Range = n.Range
          Orientation =
            (match n.Orientation with
             | ValueSome Orientation.Horizontal -> ValueSome 0
             | ValueSome Orientation.Vertical -> ValueSome 1
             | ValueNone -> ValueNone)
          Position = n.Position
          Scroll = n.Scroll }

    /// The whole tree: a window node named `title` of `width` × `height` pixels, holding the layers' nodes in
    /// layer order. `focus`: the element with the keyboard's focus; its node has AccessKit's (or the nearest
    /// around it that has one; else the window: decision 12).
    let update (title: string) (width: float) (height: float) (scale: float) (tree: SemanticTree) (focus: Element voption) : AkUpdate =
        let nodes = ResizeArray<AkNode>()
        nodes.Add
            { Id = rootId
              Role = AkRole.Window
              Label = title
              Value = ValueNone
              Children = [| for c in tree.Children -> c.Id |]
              X0 = 0.0
              Y0 = 0.0
              X1 = width
              Y1 = height
              Actions = []
              Toggled = ValueNone
              Selected = ValueNone
              Expanded = ValueNone
              Disabled = false
              ReadOnly = false
              MultiSelectable = false
              Modal = false
              ClipsChildren = false
              Range = ValueNone
              Orientation = ValueNone
              Position = ValueNone
              Scroll = ValueNone }
        for n in tree.Nodes do
            nodes.Add(nodeOf scale n)
        let focused =
            match focus |> ValueOption.bind tree.NodeAround with
            | ValueSome n -> n.Id
            | ValueNone -> rootId
        { Root = rootId; Focus = focused; Nodes = nodes.ToArray() }

/// What to do about a change to the tree.
[<RequireQualifiedAccess>]
type internal PacerStep =
    /// Send it now.
    | Send
    /// Ask again (`Due`) after this long: the first change of a run that has to wait.
    | WaitFor of TimeSpan
    /// A wait is under way already: nothing to do.
    | Waiting

/// When a changed tree goes to AccessKit (decision 10): the first change after a quiet spell at once; while
/// changes keep coming, at most one in `interval`, and a last one when they stop. A change of focus goes at once.
[<Sealed>]
type internal UpdatePacer(interval: TimeSpan) =
    let mutable lastSent = TimeSpan.MinValue
    let mutable pending = false

    /// A change seen at `now`.
    member _.Changed(now: TimeSpan, focusMoved: bool) : PacerStep =
        if focusMoved || lastSent = TimeSpan.MinValue || now - lastSent >= interval then
            lastSent <- now
            pending <- false
            PacerStep.Send
        elif pending then PacerStep.Waiting
        else
            pending <- true
            PacerStep.WaitFor(lastSent + interval - now)

    /// The wait is over at `now`: true if a change is still waiting (send it).
    member _.Due(now: TimeSpan) : bool =
        if pending then
            pending <- false
            lastSent <- now
            true
        else false
