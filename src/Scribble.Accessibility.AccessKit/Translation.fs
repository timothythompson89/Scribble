namespace Scribble.Accessibility.AccessKit

open System
open Scribble.Accessibility
open Scribble.Core

/// AccessKit's roles, the ones Scribble uses, by their numbers in accesskit.h (0.23.2: `accesskit_role`).
module internal AkRole =
    let TextRun = 1
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
    let ReplaceSelectedText = 10
    let ScrollDown = 11
    let ScrollUp = 14
    let ScrollIntoView = 15
    let SetTextSelection = 18
    let SetValue = 20

/// A text box's selection as AccessKit takes it: for the anchor and the focus (the caret), a text run's id and a
/// character in it.
[<Struct>]
type internal AkSelection =
    { AnchorRun: uint64
      AnchorCharacter: int
      FocusRun: uint64
      FocusCharacter: int }

/// One node as AccessKit is given it: what the native layer writes out, call by call.
type internal AkNode =
    { Id: uint64
      Role: int
      /// Its name ("": none).
      Label: string
      /// Its text value (a text box's; a text run's text).
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
      Scroll: SemanticScroll voption
      /// A text box: its selection, among its text runs.
      Selection: AkSelection voption
      /// A text run (null for other nodes): each character's length in the bytes of `Value` as UTF-8.
      CharacterLengths: byte[]
      /// A text run: the characters that start a word.
      WordStarts: byte[]
      /// A text run: each character's place from the run's left edge and its width, in physical pixels.
      CharacterPositions: float32[]
      CharacterWidths: float32[]
      /// A text run that is not its line's first or last: the runs beside it on the line (0: none).
      PreviousOnLine: uint64
      NextOnLine: uint64 }

    /// A node with nothing said of it but its id, role and box.
    static member Plain(id: uint64, role: int, x0: float, y0: float, x1: float, y1: float) : AkNode =
        { Id = id
          Role = role
          Label = ""
          Value = ValueNone
          Children = [||]
          X0 = x0
          Y0 = y0
          X1 = x1
          Y1 = y1
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
          Scroll = ValueNone
          Selection = ValueNone
          CharacterLengths = null
          WordStarts = null
          CharacterPositions = null
          CharacterWidths = null
          PreviousOnLine = 0UL
          NextOnLine = 0UL }

/// A whole tree for AccessKit: the window's node first, then every semantic node, a text box's text runs after it.
type internal AkUpdate =
    { Root: uint64
      Focus: uint64
      Nodes: AkNode[] }

/// A window's semantic tree as AccessKit wants it (DESIGN.md, "AccessKit: the screen-reader bridge", decisions 11
/// to 13 and 17 to 22). No native calls: tested on the fake platform.
module internal Translation =
    /// The window's own node. Semantic nodes' ids are never 0 or 1.
    let rootId = 1UL

    /// A text run holds at most this many characters: AccessKit keeps a run's word starts in bytes. A longer line
    /// is several runs, linked as being on one line.
    let runCharacters = 255

    /// A text run's id: its text box's element, its line's number, and which part of the line it is. (The top bit
    /// keeps them apart from semantic nodes' ids.)
    let runId (textBox: uint64) (line: int) (part: int) : uint64 =
        (1UL <<< 63) ||| (((textBox >>> 20) &&& 0x7FFFFFFFFUL) <<< 28) ||| (uint64 (line &&& 0xFFFFF) <<< 8) ||| uint64 (part &&& 0xFF)

    /// The place in a text box's text that a character of one of its runs is.
    let placeOf (run: uint64) (character: int) : SemanticTextPosition =
        { Line = int ((run >>> 8) &&& 0xFFFFFUL)
          Character = int (run &&& 0xFFUL) * runCharacters + character }

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

    /// AccessKit's action for one of ours.
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
        | SemanticAction.SetTextSelection -> AkAction.SetTextSelection
        | SemanticAction.ReplaceSelectedText -> AkAction.ReplaceSelectedText
        | SemanticAction.SetValue -> AkAction.SetValue

    /// The actions AccessKit is told a node supports: its own, less changing the text of one that is read-only.
    let private actionsOf (n: SemanticNode) =
        n.Actions
        |> List.filter (fun a -> not (n.IsReadOnly && (a = SemanticAction.ReplaceSelectedText || a = SemanticAction.SetValue)))

    /// Ours for one AccessKit asks for, among those `node` supports (none: not one of ours, or not supported).
    let semanticAction (node: SemanticNode) (action: int) : SemanticAction voption =
        actionsOf node |> List.tryFind (fun a -> actionOf a = action) |> Option.toValueOption

    /// The text runs of one line of the text box `n` (its top left at `originX`, `originY` in the window, DIPs).
    let private runsOf (scale: float) (n: SemanticNode) (line: SemanticTextLine) : AkNode[] =
        let origin = n.Bounds
        // The line's characters, and its break as one more.
        let own = line.Stops.Length - 1
        let total = own + (if line.EndsWithBreak then 1 else 0)
        let parts = max 1 ((total + runCharacters - 1) / runCharacters)
        let xOf (character: int) = line.Positions[min character own]
        let top = (origin.Y + line.Bounds.Y) * scale
        let bottom = (origin.Y + line.Bounds.Y + line.Bounds.Height) * scale
        [| for part in 0 .. parts - 1 do
               let first = part * runCharacters
               let last = min total (first + runCharacters)
               let text = System.Text.StringBuilder()
               let lengths = Array.zeroCreate<byte> (last - first)
               let positions = Array.zeroCreate<float32> (last - first)
               let widths = Array.zeroCreate<float32> (last - first)
               let left = xOf first
               for c in first .. last - 1 do
                   if c < own then
                       let piece = line.Text.Substring(line.Stops[c], line.Stops[c + 1] - line.Stops[c])
                       text.Append piece |> ignore
                       lengths[c - first] <- byte (min 255 (System.Text.Encoding.UTF8.GetByteCount piece))
                       widths[c - first] <- float32 ((xOf (c + 1) - xOf c) * scale)
                   else
                       // The line break: one character, with no width.
                       text.Append '\n' |> ignore
                       lengths[c - first] <- 1uy
                   positions[c - first] <- float32 ((xOf c - left) * scale)
               let x0 = (origin.X + line.Bounds.X + left) * scale
               let x1 = (origin.X + line.Bounds.X + xOf last) * scale
               { AkNode.Plain(runId n.Id line.Number part, AkRole.TextRun, x0, top, x1, bottom) with
                   Value = ValueSome(text.ToString())
                   CharacterLengths = lengths
                   WordStarts = [| for w in line.WordStarts do if w >= first && w < last then byte (w - first) |]
                   CharacterPositions = positions
                   CharacterWidths = widths
                   PreviousOnLine = (if part > 0 then runId n.Id line.Number (part - 1) else 0UL)
                   NextOnLine = (if part < parts - 1 then runId n.Id line.Number (part + 1) else 0UL) } |]

    /// A place in the text as a run and a character in it. A place on a line that isn't among `lines` (a very long
    /// text: decision 18) goes to the nearest end of those that are.
    let private runPlace (n: SemanticNode) (lines: SemanticTextLine[]) (place: SemanticTextPosition) : struct (uint64 * int) =
        let line =
            match lines |> Array.tryFind (fun l -> l.Number = place.Line) with
            | Some l -> l
            | None -> if place.Line < lines[0].Number then lines[0] else lines[lines.Length - 1]
        let own = line.Stops.Length - 1
        let total = own + (if line.EndsWithBreak then 1 else 0)
        let character =
            if line.Number = place.Line then max 0 (min own place.Character)
            elif place.Line < line.Number then 0
            else own
        let parts = max 1 ((total + runCharacters - 1) / runCharacters)
        let part = min (parts - 1) (character / runCharacters)
        struct (runId n.Id line.Number part, character - part * runCharacters)

    let private nodeOf (scale: float) (n: SemanticNode) : AkNode[] =
        let b = n.Bounds
        // AccessKit reads a label's text from its value, not its label (seen through UI Automation: a label
        // given only a label has no name).
        let isText = n.Role = Role.Text
        let runs, selection =
            match n.Text with
            | ValueSome text when text.Lines.Length > 0 ->
                let struct (anchorRun, anchorCharacter) = runPlace n text.Lines text.Anchor
                let struct (focusRun, focusCharacter) = runPlace n text.Lines text.Caret
                text.Lines |> Array.collect (runsOf scale n),
                ValueSome { AnchorRun = anchorRun; AnchorCharacter = anchorCharacter; FocusRun = focusRun; FocusCharacter = focusCharacter }
            | _ -> [||], ValueNone
        let node =
            { AkNode.Plain(n.Id, roleOf n, b.X * scale, b.Y * scale, (b.X + b.Width) * scale, (b.Y + b.Height) * scale) with
                Label = (if isText then "" else n.Name)
                Value = (if isText then ValueSome n.Name else n.Value)
                Children = [| for c in n.Children do c.Id
                              for r in runs do r.Id |]
                Actions = actionsOf n |> List.map actionOf
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
                // What a scroll area scrolls over shows only inside it; so does a text box's text.
                ClipsChildren = (n.Scroll.IsSome || runs.Length > 0)
                Range = n.Range
                Orientation =
                    (match n.Orientation with
                     | ValueSome Orientation.Horizontal -> ValueSome 0
                     | ValueSome Orientation.Vertical -> ValueSome 1
                     | ValueNone -> ValueNone)
                Position = n.Position
                Scroll = n.Scroll
                Selection = selection }
        Array.append [| node |] runs

    /// The whole tree: a window node named `title` of `width` × `height` pixels, holding the layers' nodes in
    /// layer order. `focus`: the element with the keyboard's focus; its node has AccessKit's (or the nearest
    /// around it that has one; else the window: decision 12).
    let update (title: string) (width: float) (height: float) (scale: float) (tree: SemanticTree) (focus: Element voption) : AkUpdate =
        let nodes = ResizeArray<AkNode>()
        nodes.Add
            { AkNode.Plain(rootId, AkRole.Window, 0.0, 0.0, width, height) with
                Label = title
                Children = [| for c in tree.Children -> c.Id |] }
        for n in tree.Nodes do
            nodes.AddRange(nodeOf scale n)
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
