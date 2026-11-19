namespace Scribble.Accessibility

open System
open System.Collections.Generic
open System.Globalization
open System.Text
open Scribble.Core
open Scribble.Rendering

/// Where a semantic node's name came from.
[<RequireQualifiedAccess>]
type NameSource =
    /// It has none.
    | None
    /// `Semantics.label` (or its type's, such as a TextBlock's text).
    | Label
    /// The text inside it (a button's, a checkbox's, a list item's).
    | Content

/// Performing semantic actions: the same paths as input.
module internal SemanticActions =
    let rec private isShown (e: Element) =
        e.IsVisible
        && match e.Parent with
           | ValueSome p -> isShown p
           | ValueNone -> true

    let canFocus (e: Element) = e.Root.IsSome && e.Get LayoutProps.focusable && e.IsEnabled && isShown e

    let isScrollViewer (e: Element) = obj.ReferenceEquals(e.Type, ScrollViewer.Type)

    /// The actions an element supports: what it gets by itself, then its type's or view's own.
    let supported (e: Element) (declared: SemanticAction list) (scrolls: bool) =
        [ if e.IsEnabled && Clicks.hasClick e then SemanticAction.Click
          for a in declared do
              if e.IsEnabled && a <> SemanticAction.Click then a
          if canFocus e then SemanticAction.Focus
          if scrolls then
              SemanticAction.ScrollForward
              SemanticAction.ScrollBackward
          SemanticAction.ScrollIntoView ]
        |> List.distinct

    let perform (e: Element) (supported: SemanticAction list) (action: SemanticAction) =
        e.VerifyAccess()
        if e.Root.IsNone || not (List.contains action supported) then false
        else
            match action with
            | SemanticAction.Click when Clicks.hasClick e ->
                if e.IsEnabled then
                    Clicks.click e ClickSource.Assistive Modifiers.None
                    true
                else false
            | SemanticAction.Focus ->
                if canFocus e then
                    e.Focus()
                    true
                else false
            | SemanticAction.ScrollIntoView ->
                e.BringIntoView()
                true
            | SemanticAction.ScrollForward when isScrollViewer e -> ScrollViewer.page e true
            | SemanticAction.ScrollBackward when isScrollViewer e -> ScrollViewer.page e false
            | _ ->
                match e.Get SemanticsProps.onAction with
                | ValueSome handler when e.IsEnabled ->
                    try
                        handler (InputContext e) action
                    with ex ->
                        e.ReportError ex
                        false
                | _ -> false

/// One element as assistive technology sees it: a snapshot taken when the semantic tree was asked
/// for. `Perform` acts on the element (UI thread only).
[<Sealed>]
type SemanticNode
    internal
    (
        element: Element,
        role: Role,
        name: string,
        nameSource: NameSource,
        isChecked: CheckState voption,
        isSelected: bool voption,
        isExpanded: bool voption,
        isMultiSelectable: bool,
        range: SemanticRange voption,
        orientation: Orientation voption,
        value: string voption,
        isReadOnly: bool,
        isProtected: bool,
        scroll: SemanticScroll voption,
        position: SemanticPosition voption,
        isDisabled: bool,
        isFocusable: bool,
        isFocused: bool,
        actions: SemanticAction list,
        bounds: Rect,
        children: SemanticNode list
    ) =
    let childList = List<SemanticNode>(children) :> IReadOnlyList<SemanticNode>
    member _.Role = role
    /// What a screen reader calls it ("" when it has no name).
    member _.Name = name
    member _.NameSource = nameSource
    member _.IsChecked = isChecked
    member _.IsSelected = isSelected
    member _.IsExpanded = isExpanded
    /// A list in which more than one item can be selected.
    member _.IsMultiSelectable = isMultiSelectable
    member _.Range = range
    member _.Orientation = orientation
    /// Its text value (what a text box holds); none for other elements, and for a password.
    member _.Value = value
    /// Its value can be read but not changed.
    member _.IsReadOnly = isReadOnly
    /// What it holds is hidden (a password): `Value` is none.
    member _.IsProtected = isProtected
    /// What a scroll area shows of what it scrolls over.
    member _.Scroll = scroll
    /// Its place in its set (a tab among the tabs, an item among all of a list's items).
    member _.Position = position
    /// It or something around it has `isEnabled false`.
    member _.IsDisabled = isDisabled
    /// It can take keyboard focus now.
    member _.IsFocusable = isFocusable
    member _.IsFocused = isFocused
    member _.Actions = actions
    /// Its layout bounds in window coordinates (DIPs; visual transforms aren't applied).
    member _.Bounds = bounds
    member _.Children = childList
    member internal _.Element = element

    /// Asks the element to do `action`, by the path the matching input takes; false when it has
    /// left its tree, is disabled, or doesn't support the action.
    member _.Perform(action: SemanticAction) : bool = SemanticActions.perform element actions action

    /// One line: role, name and states (`CheckBox "Remember me" checked`).
    member this.Describe() =
        let sb = StringBuilder()
        let n (v: float) = v.ToString("0.###", CultureInfo.InvariantCulture)
        sb.Append(string role) |> ignore
        if name <> "" then sb.Append(" \"").Append(name).Append('"') |> ignore
        match value with
        | ValueSome v ->
            // A line: long text is cut, line breaks shown as "\n".
            let shown = (if v.Length > 40 then v.Substring(0, 40) + "…" else v).Replace("\n", "\\n")
            sb.Append(" value \"").Append(shown).Append('"') |> ignore
        | ValueNone -> ()
        if isProtected then sb.Append(" password") |> ignore
        if isReadOnly then sb.Append(" read-only") |> ignore
        match isChecked with
        | ValueSome CheckState.On -> sb.Append(" checked") |> ignore
        | ValueSome CheckState.Mixed -> sb.Append(" mixed") |> ignore
        | ValueSome CheckState.Off -> sb.Append(" unchecked") |> ignore
        | ValueNone -> ()
        match isSelected with
        | ValueSome true -> sb.Append(" selected") |> ignore
        | _ -> ()
        match isExpanded with
        | ValueSome e -> sb.Append(if e then " expanded" else " collapsed") |> ignore
        | ValueNone -> ()
        if isMultiSelectable then sb.Append(" multi-select") |> ignore
        match range with
        | ValueSome r -> sb.Append(" ").Append(n r.Value).Append(" (").Append(n r.Minimum).Append(" to ").Append(n r.Maximum).Append(")") |> ignore
        | ValueNone -> ()
        match orientation with
        | ValueSome Orientation.Vertical -> sb.Append(" vertical") |> ignore
        | _ -> ()
        match position with
        | ValueSome p -> sb.Append(" ").Append(p.Index).Append(" of ").Append(p.Count) |> ignore
        | ValueNone -> ()
        if scroll.IsSome then sb.Append(" scrolls") |> ignore
        if isDisabled then sb.Append(" disabled") |> ignore
        if isFocused then sb.Append(" focused") |> ignore
        sb.ToString()

    override this.ToString() = this.Describe()

/// The semantic nodes of a window's layers (or of one element tree), built when asked for.
[<Sealed>]
type SemanticTree internal (nodes: SemanticNode list) =
    let childList = List<SemanticNode>(nodes) :> IReadOnlyList<SemanticNode>
    /// The top-level nodes, in order (bottom layer first).
    member _.Children = childList

    /// Every node, depth first.
    member _.Nodes: SemanticNode seq =
        let rec walk (n: SemanticNode) =
            seq {
                yield n
                for c in n.Children do
                    yield! walk c
            }
        seq {
            for n in nodes do
                yield! walk n
        }

    /// The first node whose `Describe()` contains `text` (tests).
    member this.Find(text: string) =
        this.Nodes |> Seq.tryFind (fun n -> n.Describe().Contains(text, StringComparison.Ordinal)) |> Option.toValueOption

    /// The tree, one node per line, indented by depth.
    override _.ToString() =
        let sb = StringBuilder()
        let rec write (depth: int) (n: SemanticNode) =
            sb.Append(' ', depth * 2).Append(n.Describe()).Append('\n') |> ignore
            for c in n.Children do
                write (depth + 1) c
        for n in nodes do
            write 0 n
        sb.ToString()

/// Builds semantic nodes from elements.
module internal SemanticBuilder =
    /// Roles named by the text inside them; that text (and labelled pictures) joins the name.
    let private namedByContent (r: Role) =
        match r with
        | Role.Button
        | Role.CheckBox
        | Role.Switch
        | Role.RadioButton
        | Role.Tab
        | Role.ComboBox
        | Role.ListItem -> true
        | _ -> false

    /// Roles that are only content: left out without a name (decorative), absorbed into a
    /// control's name.
    let private isContent (r: Role) =
        match r with
        | Role.Text
        | Role.Image -> true
        | _ -> false

    let private computedOf (e: Element) =
        match e.Get SemanticsProps.computed with
        | ValueSome f ->
            try
                f e
            with ex ->
                e.ReportError ex
                []
        | ValueNone -> []

    /// A semantics value: `computed`'s when it gives one, else the element's own.
    let private valueOf (computed: Setting<Layoutable> list) (e: Element) (prop: Prop<'T>) : 'T =
        let mutable found = ValueNone
        for s in computed do
            if obj.ReferenceEquals(s.Key, prop) then found <- ValueSome(unbox<'T> s.BoxedValue)
        match found with
        | ValueSome v -> v
        | ValueNone -> e.Get prop

    let private read (computed: Setting<Layoutable> list) (e: Element) (prop: Prop<SemanticSource<'T>>) : 'T =
        try
            (valueOf computed e prop).Of e
        with ex ->
            e.ReportError ex
            prop.Default.Of e

    /// The role an element ends up with (ValueNone: left out, what's inside takes its place).
    let roleOf (computed: Setting<Layoutable> list) (e: Element) (position: SemanticPosition voption) =
        match valueOf computed e SemanticsProps.role, position with
        | ValueSome Role.ScrollArea, _ when SemanticActions.isScrollViewer e && (ScrollViewer.scrollInfo e).IsNone -> ValueNone
        | ValueSome r, ValueSome _ when isContent r -> ValueSome Role.ListItem
        | ValueSome r, _ -> ValueSome r
        | ValueNone, ValueSome _ -> ValueSome Role.ListItem
        | ValueNone, ValueNone -> ValueNone

    /// The text of every Label in the tree under `root`, by the element it is for (the first label wins).
    let labelsIn (root: Element) =
        let labels = Dictionary<Element, string>(HashIdentity.Reference)
        let rec walk (e: Element) =
            if e.IsVisible then
                if obj.ReferenceEquals(e.Type, Label.Type) then
                    match Label.findTarget e with
                    | ValueSome target when not (labels.ContainsKey target) -> labels[target] <- e.Get TextProps.text
                    | _ -> ()
                for c in e.Children do
                    walk c
        walk root
        labels

    /// The nodes `e` gives: its own, or (without a role) those of what's inside it. `labels`: `labelsIn` its tree.
    let rec build (labels: Dictionary<Element, string>) (e: Element) (position: SemanticPosition voption) : SemanticNode list =
        if not e.IsVisible then []
        else
            let computed = computedOf e
            if valueOf computed e SemanticsProps.hidden then []
            else
                let position =
                    match valueOf computed e SemanticsProps.position with
                    | ValueSome p -> ValueSome p
                    | ValueNone -> position
                let itemPosition = valueOf computed e SemanticsProps.itemPosition
                let kids =
                    [ for c in e.Children do
                          let p =
                              match itemPosition with
                              | ValueSome f ->
                                  try
                                      f e c
                                  with ex ->
                                      e.ReportError ex
                                      ValueNone
                              | ValueNone -> ValueNone
                          yield! build labels c p ]
                let kids =
                    if itemPosition.IsSome then
                        kids |> List.sortBy (fun n -> match n.Position with ValueSome p -> p.Index | ValueNone -> Int32.MaxValue)
                    else kids
                match roleOf computed e position with
                | ValueNone ->
#if DEBUG
                    match valueOf computed e SemanticsProps.label with
                    | Fixed l when l <> "" ->
                        DebugWarnings.Once(
                            "semantics-label-without-role:" + e.Type.Name,
                            "A " + e.Type.Name + " has Semantics.label \"" + l + "\" but no role, so it isn't in the semantic tree: give it Semantics.role (Role.Group for a container).")
                    | _ -> ()
#endif
                    kids
                | ValueSome role ->
                    // Its own `Semantics.label`, else the Label that is for it, else what its type reads its name
                    // from (a TextBox's placeholder).
                    let label =
                        match valueOf computed e SemanticsProps.label, labels.TryGetValue e with
                        | Fixed own, _ when own <> "" -> own
                        | _, (true, text) when text <> "" -> text
                        | _ -> read computed e SemanticsProps.label
                    let kids, content =
                        if namedByContent role then
                            let names = kids |> List.choose (fun k -> if k.Name <> "" then Some k.Name else None)
                            (kids |> List.filter (fun k -> not (isContent k.Role))), String.Join(" ", names)
                        else kids, ""
                    let name, source =
                        if label <> "" then label, NameSource.Label
                        elif content <> "" then content, NameSource.Content
                        else "", NameSource.None
                    if isContent role && name = "" then kids
                    else
                        let scroll = if SemanticActions.isScrollViewer e then ScrollViewer.scrollInfo e else ValueNone
                        let at = e.PositionInTree
                        [ SemanticNode(
                              e,
                              role,
                              name,
                              source,
                              read computed e SemanticsProps.isChecked,
                              read computed e SemanticsProps.selected,
                              read computed e SemanticsProps.expanded,
                              read computed e SemanticsProps.multiSelectable,
                              read computed e SemanticsProps.range,
                              read computed e SemanticsProps.orientation,
                              read computed e SemanticsProps.value,
                              read computed e SemanticsProps.readOnly,
                              read computed e SemanticsProps.isProtected,
                              scroll,
                              position,
                              not e.IsEnabled,
                              SemanticActions.canFocus e,
                              e.IsFocused,
                              SemanticActions.supported e (valueOf computed e SemanticsProps.actions) scroll.IsSome,
                              Rect.Create(at.X, at.Y, e.Bounds.Width, e.Bounds.Height),
                              kids
                          ) ]

    let ofTree (tree: ElementTree) =
        match tree.Root with
        | ValueSome root -> build (labelsIn root) root ValueNone
        | ValueNone -> []

    /// The layers a reader can reach: from the top modal one showing something up (as input).
    let ofHost (host: ElementHost) =
        let layers = host.Layers
        let mutable from = 0
        for i in 0 .. layers.Count - 1 do
            if layers[i].IsModal && layers[i].Tree.HasVisibleContent then from <- i
        [ for i in from .. layers.Count - 1 do
              yield! ofTree layers[i].Tree ]

    let rec private find (e: Element) (nodes: SemanticNode list) =
        nodes
        |> List.tryPick (fun n -> if obj.ReferenceEquals(n.Element, e) then Some n else find e (List.ofSeq n.Children))

    /// The element's node, or why it has none (for the inspector).
    let explain (e: Element) : Result<SemanticNode, string> =
        if e.Root.IsNone then Error "not in an element tree"
        else
            let rec top (x: Element) =
                match x.Parent with
                | ValueSome p -> top p
                | ValueNone -> x
            let rec why (x: Element) =
                if not x.IsVisible then ValueSome(if obj.ReferenceEquals(x, e) then "hidden (isVisible false)" else "inside a hidden element")
                elif valueOf (computedOf x) x SemanticsProps.hidden then
                    ValueSome(if obj.ReferenceEquals(x, e) then "left out (Semantics.hidden)" else "inside an element left out (Semantics.hidden)")
                else
                    match x.Parent with
                    | ValueSome p -> why p
                    | ValueNone -> ValueNone
            match why e with
            | ValueSome reason -> Error reason
            | ValueNone ->
                match find e (build (labelsIn (top e)) (top e) ValueNone) with
                | Some n -> Ok n
                | None ->
                    let computed = computedOf e
                    match valueOf computed e SemanticsProps.role with
                    | ValueSome Role.ScrollArea when SemanticActions.isScrollViewer e -> Error "a ScrollViewer whose content fits"
                    | ValueSome r when isContent r ->
                        let rec absorbed (x: Element) =
                            match x.Parent with
                            | ValueSome p ->
                                match valueOf (computedOf p) p SemanticsProps.role with
                                | ValueSome pr when namedByContent pr -> true
                                | _ -> absorbed p
                            | ValueNone -> false
                        if absorbed e then Error "part of its control's name" else Error "decorative (no name)"
                    | ValueSome _ -> Error "not exposed"
                    | ValueNone -> Error "no role (what's inside it takes its place)"

/// Reading the semantic tree (after `open Scribble.Accessibility`).
[<AutoOpen>]
module SemanticExtensions =
    type ElementTree with
        /// This tree's semantic nodes, built now (UI thread only).
        member this.Semantics() = SemanticTree(SemanticBuilder.ofTree this)

    type ElementHost with
        /// The window's semantic tree, built now: its layers' nodes in order, from the top modal
        /// layer up (UI thread only).
        member this.SemanticTree() = SemanticTree(SemanticBuilder.ofHost this)

    type Element with
        /// The element's semantic node, or why it has none ("no role", "decorative (no name)").
        member this.SemanticNode() : Result<SemanticNode, string> = SemanticBuilder.explain this
