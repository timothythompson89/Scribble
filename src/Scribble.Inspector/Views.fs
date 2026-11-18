namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Accessibility

/// The panel component's props: the shared state, and whether it fills the pop-out window.
type internal PanelProps = { State: InspectorState; IsPoppedOut: bool }

/// The header's props: what its buttons depend on (equal props, no re-run).
type internal HeaderProps =
    { State: InspectorState
      IsPoppedOut: bool
      IsPicking: bool
      IsFloating: bool
      Side: DockSide
      IsCollapsed: bool
      CanPopOut: bool }

/// Props for the parts that only change with the content (`InspectorState.ContentVersion`), not
/// with the panel's place or size: equal props, no re-run.
type internal SectionProps = { State: InspectorState; Version: int }

/// The toolbar's icons: Google's Material Symbols (outlined, weight 400), © Google, Apache License 2.0
/// (https://www.apache.org/licenses/LICENSE-2.0). Path data in Google's box (0, -960, 960, 960).
module internal InspectorIcons =
    let private material name data =
        Icon.fromPath data [ Icon.name name; Icon.viewBox (0.0, -960.0, 960.0, 960.0) ]

    let pick = material "arrow_selector_tool" "M320 -410 399 -520H569L320 -716ZM551 -80 406 -392 240 -160V-880L800 -440H516L660 -131ZM399 -520Z"
    /// A window with a narrow pane on the left (Material calls it dock_to_right: the content docks right).
    let dockLeft =
        material
            "dock_left"
            "M200 -120Q167 -120 144 -144Q120 -167 120 -200V-760Q120 -793 144 -816Q167 -840 200 -840H760Q793 -840 816 -816Q840 -793 840 -760V-200Q840 -167 816 -144Q793 -120 760 -120ZM320 -200V-760H200Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200ZM400 -200H760Q760 -200 760 -200Q760 -200 760 -200V-760Q760 -760 760 -760Q760 -760 760 -760H400ZM320 -200H200Q200 -200 200 -200Q200 -200 200 -200Q200 -200 200 -200Q200 -200 200 -200H320Z"
    /// The same, mirrored: the pane on the right.
    let dockRight = dockLeft |> Icon.flipHorizontal
    let popOut =
        material
            "open_in_new"
            "M200 -120Q167 -120 144 -144Q120 -167 120 -200V-760Q120 -793 144 -816Q167 -840 200 -840H480V-760H200Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200H760Q760 -200 760 -200Q760 -200 760 -200V-480H840V-200Q840 -167 816 -144Q793 -120 760 -120ZM388 -332 332 -388 704 -760H560V-840H840V-560H760V-704Z"
    let popIn = material "close_fullscreen" "M136 -80 80 -136 344 -400H160V-480H480V-160H400V-344ZM480 -480V-800H560V-616L824 -880L880 -824L616 -560H800V-480Z"
    /// Folding the panel to its title bar, like a window's minimize; restoring it, like a window's square.
    let fold = material "minimize" "M240 -120V-200H720V-120Z"
    let unfold =
        material
            "crop_square"
            "M200 -120Q167 -120 144 -144Q120 -167 120 -200V-760Q120 -793 144 -816Q167 -840 200 -840H760Q793 -840 816 -816Q840 -793 840 -760V-200Q840 -167 816 -144Q793 -120 760 -120ZM200 -200H760Q760 -200 760 -200Q760 -200 760 -200V-760Q760 -760 760 -760Q760 -760 760 -760H200Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200ZM200 -200Q200 -200 200 -200Q200 -200 200 -200V-760Q200 -760 200 -760Q200 -760 200 -760Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200Z"
    let close = material "close" "M256 -200 200 -256 424 -480 200 -704 256 -760 480 -536 704 -760 760 -704 536 -480 760 -256 704 -200 480 -424Z"

// --- The views ---------------------------------------------------------------------------------------

module internal Views =
    // Shown in their own words on the states line ("hovered", "focused (visible)"), not by name.
    let private builtInStates = [ States.hover; States.pressed; States.focused; States.focusVisible; States.disabled ]
    let private panelBackground = (Color.Hex 0x1B1B2A).WithAlpha 0.97f
    let private dim = Color.Hex 0x8A8AA0
    let private typeColor = Color.Hex 0x8AB4F8
    let private componentColor = Color.Hex 0xC792EA
    let private nameColor = Color.Hex 0xF2C27B
    /// Forced states: their toggles (background) and the tree's marker (text). Teal, apart from the
    /// selection's blue, components' purple and names' yellow.
    let private forcedColor = Color.Hex 0x1F7A70
    let private forcedTextColor = Color.Hex 0x5CC8B8
    let private hintColor = Color.Hex 0xB8B8C8
    let private errorColor = Color.Hex 0xF28B82
    let private indentStep = 12.0

    /// Re-runs the component whenever the state says it changed.
    let private follow (ctx: ComponentContext) (state: InspectorState) =
        let version = ctx.useState 0
        ctx.useSubscription(state, fun (s: InspectorState) -> s.Changed.Subscribe(fun () -> version.Update((+) 1)))

    let private text (value: string) (color: Color) =
        TextBlock.create [
            TextBlock.text value
            TextBlock.fontSize 12.0
            TextBlock.foreground color
            TextBlock.verticalAlignment VerticalAlignment.Center
        ]

    let private chevron (shown: bool) (open': bool) =
        Chevron.create [
            Chevron.isOpen open'
            Chevron.color (if shown then dim else Color.Transparent)
            Chevron.verticalAlignment VerticalAlignment.Center
        ]

    let private row (state: InspectorState) (r: Row) =
        match r with
        | Row.Layer l ->
            let details = "order " + l.Order.ToString() + (if l.IsModal then " · modal" else "")
            Border.create [
                Border.height 22.0 // every row is 22 DIPs (the tree's ItemsView has fixed heights)
                Border.padding (Thickness.Create(6.0, 0.0, 6.0, 0.0))
                Border.background (Color.Hex 0x26263A)
                Border.onPointerDown (fun _ -> state.ToggleLayer l)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.children [
                            chevron true (not (state.IsCollapsed l))
                            text ("Layer " + l.Name) Color.White
                            text details dim
                        ]
                    ]
                )
            ]
        | Row.Element(e, depth) ->
            let isSelected = state.Selected |> ValueOption.exists (fun s -> obj.ReferenceEquals(s, e))
            let isHovered = state.Hovered |> ValueOption.exists (fun s -> obj.ReferenceEquals(s, e))
            let hasChildren = e.Children.Count > 0
            let isOpen = hasChildren && state.IsExpanded e
            let left = 6.0 + float depth * indentStep
            // Hidden elements (and everything inside them) are dimmed.
            let fade (c: Color) = if e.IsVisible then c else c.WithAlpha 0.4f
            // The last part of the indent sits inside the label, so bringing the label into view sideways
            // keeps a little of it to the left (M8 step 3a++).
            let inset = min left 24.0
            Border.create [
                Border.height 22.0
                Border.padding (Thickness.Create(left - inset, 0.0, 6.0, 0.0))
                Border.background (
                    if isSelected then (Color.Hex 0x3D59A1).WithAlpha 0.6f
                    elif isHovered then Color.White.WithAlpha 0.07f
                    else Color.Transparent
                )
                // The chevron toggles; anywhere else selects.
                Border.onPointerDown (fun a ->
                    if hasChildren && a.LocalPosition.X < left + 14.0 then state.ToggleExpanded e else state.ClickRow e)
                Border.onPointerEnter (fun _ -> state.HoverRow e)
                Border.onPointerLeave (fun _ -> state.LeaveRow e)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.spacing 4.0
                        StackPanel.children [
                            Border.create [
                                if isSelected then Border.name Names.SelectedLabel
                                Border.padding (Thickness.Create(inset, 0.0, 0.0, 0.0))
                                Border.child (
                                    StackPanel.create [
                                        StackPanel.orientation Horizontal
                                        StackPanel.spacing 4.0
                                        StackPanel.children [
                                            chevron hasChildren isOpen
                                            text (Format.label e) (fade (if e.ComponentName.IsSome then componentColor else typeColor))
                                        ]
                                    ]
                                )
                            ]
                            if e.Name.Length > 0 then text ("#" + e.Name) (fade nameColor)
                            if not e.ForcedStates.IsEmpty then text "• forced" forcedTextColor
                            // An ItemsView's count shows open or not; text hints only on collapsed rows.
                            let realized = Format.realized e
                            if realized.Length > 0 then text realized (fade hintColor)
                            elif not isOpen then
                                let hint = Format.hint e
                                if hint.Length > 0 then text hint (fade hintColor)
                        ]
                    ]
                )
            ]

    let private rowKey (r: Row) : obj =
        match r with
        | Row.Layer l -> box l
        | Row.Element(e, _) -> box e

    let private smallButton (label: string) (isActive: bool) (onClick: unit -> unit) =
        Button.create [
            Button.padding (7.0, 2.0)
            Button.verticalAlignment VerticalAlignment.Center
            Button.onClick (fun _ -> onClick ())
            if isActive then Button.background (Color.Hex 0x3D59A1)
            Button.child (
                TextBlock.create [
                    TextBlock.text label
                    TextBlock.fontSize 12.0
                    TextBlock.foreground (Color.Hex 0xE6E6F0)
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        ]

    /// Press, move and release on an element drive one kind of drag (moves keep coming to the
    /// element that holds the press, wherever the pointer goes).
    let private dragHandlers<'k when 'k :> Layoutable> (state: InspectorState) (kind: DragKind) : Setting<'k> list =
        [ Layoutable.onPointerDown (fun a -> state.BeginDrag(kind, a.Position))
          Layoutable.onPointerMove (fun a -> state.DragTo a.Position)
          Layoutable.onPointerUp (fun _ -> state.EndDrag()) ]

    /// The title, and the area to drag the panel by (double-click collapses). While the pointer is
    /// over a toolbar button, it says what the button does instead.
    let private title (state: InspectorState) (draggable: bool) (hint: string) =
        Border.create (
            [ Border.width "1*"
              Border.padding (Thickness.Create(12.0, 0.0, 4.0, 0.0))
              Border.child (
                  TextBlock.create [
                      if hint.Length > 0 then
                          TextBlock.text hint
                          TextBlock.foreground (Color.Hex 0xB8B8CC)
                          TextBlock.textTrimming CharacterEllipsis
                      else
                          TextBlock.text "Inspector"
                          TextBlock.font (Fonts.SemiBold())
                          TextBlock.foreground Color.White
                      TextBlock.verticalAlignment VerticalAlignment.Center
                  ]
              ) ]
            @ (if draggable then dragHandlers state DragKind.Move else [])
        )

    /// A toolbar button: an icon, transparent until hovered (accent-filled when `isActive`);
    /// hovering shows `hint` in the title.
    let private toolButton (name: string) (icon: Icon) (hint: string) (isActive: bool) (showHint: string -> unit) (onClick: unit -> unit) =
        Button.create [
            Button.name name
            Button.icon icon
            Button.iconSize 18.0
            Button.width 28.0
            Button.height 28.0
            Button.padding 0.0
            Button.borderThickness 0.0
            Button.cornerRadius 5.0
            Setting.create BorderProps.shadow ValueNone
            Button.background (if isActive then Color.Hex 0x3D59A1 else Color.Transparent)
            Button.foreground (if isActive then Color.White else Color.Hex 0xC8C8D8)
            Button.verticalAlignment VerticalAlignment.Center
            Button.onPointerEnter (fun _ -> showHint name)
            Button.onPointerLeave (fun _ -> showHint "")
            Button.onClick (fun _ -> onClick ())
        ]

    /// A thin line between groups of toolbar buttons.
    let private toolSeparator () =
        Border.create [
            Border.width 1.0
            Border.height 16.0
            Border.margin (Thickness.Create(3.0, 0.0, 3.0, 0.0))
            Border.verticalAlignment VerticalAlignment.Center
            Border.background (Color.White.WithAlpha 0.12f)
        ]

    /// The title and the toolbar. Its own component, so hovering a button (which changes the
    /// title) re-runs only this.
    let private header =
        Component.define "InspectorHeader" (fun ctx (p: HeaderProps) ->
            let state = p.State
            // The hovered button's name; its hint shows only while that button is still there
            // (a click can remove it before the pointer leaves).
            let hovered = ctx.useState ""
            let show = hovered.Set
            let buttons = [
                yield "pick", InspectorIcons.pick, (if p.IsPicking then "Stop picking (Esc)" else "Pick (Ctrl+Shift+C)"), p.IsPicking, state.TogglePick
                if p.IsPoppedOut then
                    yield "popIn", InspectorIcons.popIn, "Pop back in", false, state.PopIn
                else
                    if p.IsFloating || p.Side = DockSide.Right then
                        yield "dockLeft", InspectorIcons.dockLeft, "Dock left", false, (fun () -> state.Dock DockSide.Left)
                    if p.IsFloating || p.Side = DockSide.Left then
                        yield "dockRight", InspectorIcons.dockRight, "Dock right", false, (fun () -> state.Dock DockSide.Right)
                    if p.CanPopOut then yield "popOut", InspectorIcons.popOut, "Pop out", false, state.PopOut
                    if p.IsCollapsed then yield "fold", InspectorIcons.unfold, "Restore", false, state.ToggleCollapsed
                    else yield "fold", InspectorIcons.fold, "Minimize", false, state.ToggleCollapsed
                    yield "close", InspectorIcons.close, "Close (F12)", false, state.Toggle
            ]
            // A hovered button's hint, else pick mode's status (the pointer is out in the app while
            // picking), else the title.
            let hint =
                buttons
                |> List.tryPick (fun (name, _, hint, _, _) -> if name = hovered.Current then Some hint else None)
                |> Option.defaultValue (if p.IsPicking then "Picking… (Esc stops)" else "")
            // Groups: pick | where the panel goes | fold and close.
            let groupOf name =
                match name with
                | "pick" -> 0
                | "fold" | "close" -> 2
                | _ -> 1
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 2.0
                StackPanel.height 36.0
                StackPanel.margin (Thickness.Create(0.0, 0.0, 6.0, 0.0))
                StackPanel.children [
                    yield title state (not p.IsPoppedOut) hint
                    let mutable group = 0
                    for (name, icon, text, isActive, onClick) in buttons do
                        if groupOf name <> group then
                            group <- groupOf name
                            yield toolSeparator ()
                        yield toolButton name icon text isActive show onClick
                ]
            ])

    let private headerFor (state: InspectorState) (isPoppedOut: bool) =
        header
            { State = state
              IsPoppedOut = isPoppedOut
              IsPicking = state.IsPicking
              IsFloating = state.Mode = PanelMode.Floating
              Side = state.Side
              IsCollapsed = state.IsPanelCollapsed
              CanPopOut = state.CanPopOut }

    /// Frame stats (as of the last refresh) and, when floating, the corner grip.
    let private footer (state: InspectorState) (showGrip: bool) =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.children [
                yield
                    TextBlock.create [
                        TextBlock.width "1*"
                        TextBlock.margin (Thickness.Create(10.0, 6.0, 10.0, 8.0))
                        TextBlock.text state.StatsText
                        TextBlock.fontSize 11.0
                        TextBlock.foreground dim
                        TextBlock.textTrimming CharacterEllipsis
                    ]
                if showGrip then
                    yield
                        Grip.create (
                            [ Grip.verticalAlignment VerticalAlignment.Bottom; Grip.cursor Cursor.ResizeDiagonal ]
                            @ dragHandlers state DragKind.ResizeCorner
                        )
            ]
        ]

    /// Between the tree and the properties: drag it up or down.
    let private divider (state: InspectorState) =
        Border.create (
            [ Border.height 7.0
              Border.cursor Cursor.ResizeUpDown
              Border.background Color.Transparent
              Border.child (Border.create [ Border.height 1.0; Border.background (Color.Hex 0x3A3A55); Border.verticalAlignment VerticalAlignment.Center ]) ]
            @ dragHandlers state DragKind.Split
        )

    let private cell (value: string) (color: Color) (width: Length) =
        TextBlock.create [
            TextBlock.width width
            TextBlock.text value
            TextBlock.fontSize 12.0
            TextBlock.foreground color
            TextBlock.textTrimming CharacterEllipsis
            TextBlock.verticalAlignment VerticalAlignment.Center
        ]

    /// One line of the properties: a name, a value and (for properties) where the value comes from.
    let private line (name: string) (value: string) (valueColor: Color) (source: string) =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.height 20.0
            StackPanel.margin (Thickness.Create(10.0, 0.0, 10.0, 0.0))
            StackPanel.children [
                yield cell name dim (Px 128.0)
                yield cell value valueColor (Star 1.0)
                if source.Length > 0 then yield cell source dim (Px 52.0)
            ]
        ]

    /// A line whose value wraps instead of ending in "…" (the states, which can be many). One line
    /// tall, it looks exactly like `line`.
    let private wrappingLine (name: string) (value: string) (valueColor: Color) =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.minHeight 20.0
            StackPanel.margin (Thickness.Create(10.0, 0.0, 10.0, 0.0))
            StackPanel.children [
                cell name dim (Px 128.0)
                TextBlock.create [
                    TextBlock.width (Star 1.0)
                    TextBlock.text value
                    TextBlock.fontSize 12.0
                    TextBlock.foreground valueColor
                    TextBlock.textWrapping Wrap
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            ]
        ]

    let private heading (value: string) =
        TextBlock.create [
            TextBlock.margin (Thickness.Create(10.0, 8.0, 10.0, 2.0))
            TextBlock.text value
            TextBlock.fontSize 11.0
            TextBlock.font (Fonts.SemiBold())
            TextBlock.foreground dim
        ]

    /// Settings are written in lower camel case (`horizontalAlignment`); properties are named in Pascal case.
    let private settingName (p: Prop) =
        let camel (n: string) = if n.Length = 0 then n else Char.ToLowerInvariant(n[0]).ToString() + n.Substring 1
        // Attached settings keep their panel's name: "Grid.Row" shows as Grid.row, as written.
        if p.AttachedTo <> "" && p.Name.StartsWith(p.AttachedTo + ".") then
            p.AttachedTo + "." + camel (p.Name.Substring(p.AttachedTo.Length + 1))
        else camel p.Name

    let private sourceText (s: ValueSource) =
        match s with
        | ValueSource.View -> "view"
        | ValueSource.Code -> "code"
        | ValueSource.Style -> "style"
        | ValueSource.Theme -> "theme"
        | ValueSource.TypeDefault -> "type"
        | ValueSource.Inherited -> "inherited"
        | ValueSource.Default -> "default"

    /// A CheckBox, controlled by the inspector's state. The inspector takes no app styles or
    /// theme, so its colors are set here (the accent while on).
    let private checkBox (label: string) (isChecked: bool) (onToggle: unit -> unit) =
        CheckBox.create [
            CheckBox.margin (Thickness.Create(10.0, 4.0, 0.0, 4.0))
            CheckBox.horizontalAlignment HorizontalAlignment.Left
            CheckBox.isChecked isChecked
            CheckBox.onCheckedChanged (fun _ -> onToggle ())
            CheckBox.background Color.Transparent
            CheckBox.borderBrush (Color.Hex 0x8A8AA0)
            CheckBox.on States.isChecked [ CheckBox.background (Color.Hex 0x3D59A1); CheckBox.borderBrush (Color.Hex 0x3D59A1) ]
            CheckBox.child (
                TextBlock.create [
                    TextBlock.text label
                    TextBlock.fontSize 12.0
                    TextBlock.foreground hintColor
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        ]

    /// What the selected element is: states, layout numbers, properties and a component's hooks.
    /// The selected element's name and the Deselect button, above the scrolling properties (so
    /// they stay in view).
    let private selectionHeader (state: InspectorState) =
        match state.Selected with
        | ValueNone -> None
        | ValueSome e ->
            Some(
                StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 6.0
                    StackPanel.margin (Thickness.Create(10.0, 6.0, 10.0, 2.0))
                    StackPanel.children [
                        yield
                            TextBlock.create [
                                TextBlock.text (Format.label e)
                                TextBlock.font (Fonts.SemiBold())
                                TextBlock.foreground (if e.ComponentName.IsSome then componentColor else typeColor)
                            ]
                        if e.Name.Length > 0 then yield text ("#" + e.Name) nameColor
                        // Pushes the deselect button to the right edge.
                        yield Border.create [ Border.width "1*" ]
                        // Not "×": the title bar's × closes the whole inspector.
                        if state.HasForced then yield smallButton "Clear forced" false state.ClearForced
                        yield smallButton "Deselect" false state.Deselect
                    ]
                ]
            )

    /// One state as a toggle: filled teal while forced on, a teal outline with the name struck through
    /// while forced off, bright while on for real, dim while off. A click forces the opposite of what
    /// it shows, or stops forcing it.
    let private stateChip (label: string) (isOn: bool) (forced: bool voption) (onClick: unit -> unit) =
        let forcedOn, forcedOff = forced = ValueSome true, forced = ValueSome false
        let bright = forcedOn || forcedOff || isOn
        Border.create [
            Border.padding (6.0, 1.0)
            Border.cornerRadius 4.0
            Border.borderThickness 1.0
            Border.borderBrush (
                if forcedOff then forcedColor
                elif forcedOn || isOn then Color.Transparent
                else Color.White.WithAlpha 0.14f
            )
            Border.background (
                if forcedOn then forcedColor
                elif isOn then Color.White.WithAlpha 0.12f
                else Color.Transparent
            )
            Border.cursor Cursor.Hand
            Border.on States.hover [ Border.overlay (Color.White.WithAlpha 0.08f) ]
            Border.onClick (fun _ -> onClick ())
            Border.child (
                // One cell: the name, and over it a line through its middle when forced off.
                Grid.create [
                    Grid.children [
                        TextBlock.create [
                            TextBlock.text label
                            TextBlock.fontSize 12.0
                            TextBlock.foreground (if bright then Color.White else dim)
                        ]
                        if forcedOff then
                            Border.create [
                                Border.height 1.0
                                Border.verticalAlignment VerticalAlignment.Center
                                Border.background (Color.White.WithAlpha 0.85f)
                            ]
                    ]
                ]
            )
        ]

    /// The STATES section: every state, built-in and custom (`States.define`, `States.ofProp`), as
    /// toggles that force them, so one that is off can be forced too.
    let private statesLine (state: InspectorState) (e: Element) =
        let active = e.ActiveStates
        let forced = e.ForcedStates
        let shown = builtInStates @ List.ofArray (States.customStates ())
        // Its own section (under the STATES heading), across the whole width.
        WrapPanel.create [
            WrapPanel.margin (Thickness.Create(10.0, 2.0, 10.0, 2.0))
            WrapPanel.spacing 4.0
            WrapPanel.lineSpacing 4.0
            WrapPanel.children [
                for s in shown do
                    // Forced states test as forced: "on" here means on for real (only asked when not forced).
                    let isForced =
                        match forced |> List.tryFind (fun (f, _) -> obj.ReferenceEquals(f, s)) with
                        | Some(_, on) -> ValueSome on
                        | None -> ValueNone
                    let isOn = isForced.IsNone && active |> List.exists (fun x -> obj.ReferenceEquals(x, s))
                    stateChip s.Name isOn isForced (fun () -> state.ToggleForced(e, s))
                if not e.IsVisible then text "hidden" hintColor
            ]
        ]

    let private isSemantics (p: Prop) = p.Name.StartsWith("Semantics.", StringComparison.Ordinal)

    /// SEMANTICS: what the element is to assistive technology (its semantic node), or why it has
    /// none; then the semantics settings it has and where they came from.
    let private semanticsLines (state: InspectorState) (e: Element) (values: PropertyValue list) =
        let n = Format.number
        [ yield heading "SEMANTICS"
          match e.SemanticNode() with
          | Error why -> yield line "not exposed" why hintColor ""
          | Ok node ->
              yield line "role" (string node.Role) Color.White ""
              let source =
                  match node.NameSource with
                  | NameSource.Label -> "label"
                  | NameSource.Content -> "content"
                  | NameSource.None -> ""
              yield line "name" (if node.Name = "" then "none" else node.Name) (if node.Name = "" then dim else Color.White) source
              match node.IsChecked with
              | ValueSome c -> yield line "checked" (string c) Color.White ""
              | ValueNone -> ()
              match node.IsSelected with
              | ValueSome s -> yield line "selected" (if s then "yes" else "no") Color.White ""
              | ValueNone -> ()
              match node.IsExpanded with
              | ValueSome x -> yield line "expanded" (if x then "yes" else "no") Color.White ""
              | ValueNone -> ()
              match node.Range with
              | ValueSome r -> yield line "range" (SemanticText.range r) Color.White ""
              | ValueNone -> ()
              match node.Orientation with
              | ValueSome o -> yield line "orientation" (SemanticText.orientation o) Color.White ""
              | ValueNone -> ()
              match node.Position with
              | ValueSome p -> yield line "position" (string p.Index + " of " + string p.Count) Color.White ""
              | ValueNone -> ()
              match node.Scroll with
              | ValueSome s ->
                  yield line "scroll" (n s.OffsetX + ", " + n s.OffsetY + " in " + Format.size s.ExtentWidth s.ExtentHeight) Color.White ""
              | ValueNone -> ()
              let states =
                  [ if node.IsDisabled then "disabled"
                    if node.IsFocusable then "focusable"
                    if node.IsFocused then "focused" ]
              if not states.IsEmpty then yield line "states" (String.Join(", ", states)) Color.White ""
              yield line "actions" (String.Join(", ", node.Actions |> List.map string)) hintColor ""
              yield line "children" (string node.Children.Count) hintColor ""
          for v in values do
              if isSemantics v.Prop && v.Source <> ValueSource.Default then
                  yield line v.Prop.Name (SemanticText.value v.Value) Color.White (sourceText v.Source) ]

    /// IMAGES: what the element holds (an ImageView's picture, an image cursor's, a custom element's `ctx.Image`):
    /// its source, sizes, state, how many elements hold it, and a thumbnail (DESIGN.md, "M8 step 3b", (f) 7).
    let private imageLines (e: Element) =
        let n = Format.number
        match e.HeldImages with
        | [] -> []
        | images ->
            [ yield heading "IMAGES"
              for h in images do
                  yield wrappingLine "source" (Format.image h.Image) Color.White
                  if h.PixelWidth > 0 then
                      let density = h.Image.Density
                      let pixels = string h.PixelWidth + " × " + string h.PixelHeight + " px"
                      let dips = n (float h.PixelWidth / density) + " × " + n (float h.PixelHeight / density) + " DIPs"
                      yield line "size" (pixels + ", " + dips) Color.White ""
                  match h.Status with
                  | ImageStatus.Loading -> yield line "state" "loading" hintColor ""
                  | ImageStatus.Ready -> yield line "state" "ready" Color.White ""
                  | ImageStatus.Failed ex -> yield wrappingLine "state" ("failed: " + ex.Message) errorColor
                  match h.Decoded with
                  | ValueSome d when d.Reduced ->
                      yield line "reduced to" (string d.Width + " × " + string d.Height + " px") hintColor ""
                  | _ -> ()
                  yield line "held by" (if h.Holders = 1 then "1 element" else string h.Holders + " elements") hintColor ""
                  match h.Decoded with
                  | ValueSome d ->
                      // Its own size in DIPs, shrunk to fit 96.
                      let w = float h.PixelWidth / h.Image.Density
                      let ht = float h.PixelHeight / h.Image.Density
                      let k = min 1.0 (96.0 / max w ht)
                      yield
                          Thumbnail.create [
                              Thumbnail.decoded d
                              Thumbnail.width (w * k)
                              Thumbnail.height (ht * k)
                              Thumbnail.horizontalAlignment HorizontalAlignment.Left
                              Thumbnail.margin (Thickness.Create(138.0, 2.0, 10.0, 6.0))
                          ]
                  | ValueNone -> () ]

    let private properties (state: InspectorState) =
        match state.Selected with
        | ValueNone ->
            [ TextBlock.create [
                  TextBlock.margin 10.0
                  TextBlock.text ("Click a row, or Pick (" + Format.key state.Options.PickKey + ") and click in the app.")
                  TextBlock.fontSize 12.0
                  TextBlock.foreground dim
                  TextBlock.textWrapping Wrap
              ] ]
        | ValueSome e ->
            let p = e.PositionInTree
            let b = e.Bounds
            // What the theme can set here (in any state) always shows, even at its default; a "from
            // theme ..." line appears under it once a theme style applies.
            let themed = e.ThemedProperties
            let themedRules (p: Prop) =
                themed |> List.tryPick (fun t -> if obj.ReferenceEquals(t.Prop, p) then Some t.Selectors else None)
            let all = e.Values
            let missing =
                [ for t in themed do
                      if not (all |> List.exists (fun v -> obj.ReferenceEquals(v.Prop, t.Prop))) then
                          { Prop = t.Prop; Value = e.GetBoxed t.Prop; Source = ValueSource.Default; Selector = ""; ThemeValue = "" } ]
            let values =
                all @ missing
                |> List.filter (fun v -> not (isSemantics v.Prop))
                |> List.filter (fun v -> state.ShowDefaults || v.Source <> ValueSource.Default || (themedRules v.Prop).IsSome)
                |> List.sortBy (fun v ->
                    if v.Source <> ValueSource.Default then 0
                    elif (themedRules v.Prop).IsSome then 1
                    else 2)
            let hooks = e.Hooks
            [ yield heading "LAYOUT"
              yield line "offered" (Format.sizeOf e.OfferedSize) hintColor ""
              yield line "desired" (Format.sizeOf e.DesiredSize) hintColor ""
              yield line "bounds" (Format.number b.X + ", " + Format.number b.Y + "  " + Format.size b.Width b.Height) hintColor "parent"
              yield line "position" (Format.number p.X + ", " + Format.number p.Y) hintColor "window"
              let realized = Format.realized e
              if realized.Length > 0 then yield line "items" realized hintColor ""
              yield heading "PROPERTIES"
              yield checkBox "Show defaults" state.ShowDefaults state.ToggleShowDefaults
              for v in values do
                  let isDefault = v.Source = ValueSource.Default
                  let shown =
                      match v.Value with
                      | :? (string list) as classes when obj.ReferenceEquals(v.Prop, LayoutProps.classes) ->
                          if classes.IsEmpty then "none" else String.Join(" ", classes |> List.map (fun c -> "." + c))
                      | value -> state.Format value
                  yield line (settingName v.Prop) shown (if isDefault then dim else Color.White) (sourceText v.Source)
                  // Under the value: the theme value it reads (`Palette.surface`), then which style set
                  // it (`from Button:hover`; the source says style or theme).
                  let unders =
                      [ // An attached setting only its parent reads: say so when the parent isn't that panel.
                        if v.Prop.AttachedTo <> "" then
                            match e.Parent with
                            | ValueSome p when p.Type.Name = v.Prop.AttachedTo -> ()
                            | _ -> "ignored: parent isn't a " + v.Prop.AttachedTo
                        if v.ThemeValue.Length > 0 then v.ThemeValue
                        if v.Source = ValueSource.Style || v.Source = ValueSource.Theme then "from " + v.Selector ]
                  for under in unders do
                      yield
                          TextBlock.create [
                              // Under the value column only (the source column stays clear); wraps.
                              TextBlock.margin (Thickness.Create(138.0, -3.0, 62.0, 3.0))
                              TextBlock.text under
                              TextBlock.fontSize 11.0
                              TextBlock.foreground dim
                              TextBlock.textWrapping Wrap
                          ]
              yield! imageLines e
              yield! semanticsLines state e all
              if not hooks.IsEmpty then
                  yield heading "HOOKS"
                  for h in hooks do
                      let value =
                          match h.Value with
                          | ValueSome v -> (if h.Status.Length > 0 then h.Status + ": " else "") + state.Format v
                          | ValueNone -> h.Status
                      yield line h.Kind value Color.White ""
              yield Border.create [ Border.height 8.0 ] ]

    // The tree, the selection's name row and the properties are components re-run only when the
    // content changes, so dragging or resizing the panel only moves it (in the browser, rebuilding
    // them on every pointer move made dragging lag).
    let private treeSection =
        Component.define "InspectorTree" (fun _ (p: SectionProps) ->
            // Only the rows in view exist (M8 step 3a+): every row is 22 DIPs, so positions are exact.
            ItemsView.create [
                ItemsView.source (p.State.Rows())
                ItemsView.key rowKey
                ItemsView.view (row p.State)
                ItemsView.layout (ItemsLayout.fixedStack 22.0)
                ItemsView.scroller p.State.TreeScroller
            ])

    /// The selection's name, Deselect and its STATES: kept above the scrolling properties, so the
    /// state toggles stay in view while scrolling.
    let private selectionSection =
        Component.define "InspectorSelection" (fun _ (p: SectionProps) ->
            match selectionHeader p.State, p.State.Selected with
            | Some h, ValueSome e ->
                StackPanel.create [
                    StackPanel.children [
                        h
                        heading "STATES"
                        statesLine p.State e
                        // Where the scrolling part starts.
                        Border.create [ Border.margin (Thickness.Create(0.0, 6.0, 0.0, 0.0)); Border.height 1.0; Border.background (Color.White.WithAlpha 0.06f) ]
                    ]
                ]
            | _ -> Border.create [])

    let private propertiesSection =
        Component.define "InspectorProperties" (fun _ (p: SectionProps) ->
            StackPanel.create [ StackPanel.children (properties p.State) ])

    /// The header, the tree, the selected element's properties and the frame stats (only the
    /// header when collapsed).
    let private content (state: InspectorState) (isPoppedOut: bool) (fillWidth: bool) =
        let section: SectionProps = { State = state; Version = state.ContentVersion }
        StackPanel.create [
            if fillWidth then StackPanel.width "1*"
            StackPanel.children [
                yield headerFor state isPoppedOut
                if isPoppedOut || not state.IsPanelCollapsed then
                    yield
                        ScrollViewer.create [
                            ScrollViewer.height "1*"
                            ScrollViewer.horizontal ScrollBarMode.Auto // deep rows scroll sideways (M8 step 3a++)
                            ScrollViewer.scrollBarSpace ScrollBarSpace.Overlay // no gutter: the bar sits over the rows (2026-11-17)
                            ScrollViewer.content (treeSection section)
                        ]
                    yield divider state
                    // The divider sets the height of the whole area: the selection's name stays put
                    // at the top, the rest scrolls.
                    yield
                        StackPanel.create [
                            StackPanel.height state.PropertiesHeight
                            StackPanel.children [
                                selectionSection section
                                ScrollViewer.create [
                                    ScrollViewer.height "1*"
                                    ScrollViewer.content (propertiesSection section)
                                ]
                            ]
                        ]
                    yield footer state (not isPoppedOut && state.Mode = PanelMode.Floating)
            ]
        ]

    /// A docked panel's inner edge: drag it to change the width. Hidden where it isn't the inner edge.
    let private edge (state: InspectorState) (isShown: bool) =
        Border.create (
            [ Border.isVisible isShown
              Border.width 6.0
              Border.cursor Cursor.ResizeLeftRight
              Border.background Color.Transparent ]
            @ dragHandlers state DragKind.ResizeEdge
        )

    /// The panel: in the app's window (docked or floating), or filling the pop-out window.
    let panel =
        Component.define "Inspector" (fun ctx (props: PanelProps) ->
            let state = props.State
            follow ctx state
            if props.IsPoppedOut then
                Border.create [ Semantics.hidden true; Border.background panelBackground; Border.child (content state true false) ]
            else
                let looks =
                    [ Semantics.hidden true
                      Border.isVisible (state.IsOpen && state.Mode <> PanelMode.PoppedOut)
                      Border.cornerRadius 8.0
                      Border.background panelBackground
                      Border.borderThickness 1.0
                      Border.borderBrush (Color.Hex 0x3A3A55)
                      Border.clipToBounds true
                      Border.shadow { Blur = 16.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.5f } ]
                let placement =
                    match state.Mode with
                    | PanelMode.Floating ->
                        let r = state.FloatingRect
                        [ Border.horizontalAlignment HorizontalAlignment.Left
                          Border.verticalAlignment VerticalAlignment.Top
                          Border.margin (r.X, r.Y, 0.0, 0.0)
                          Border.width r.Width
                          if not state.IsPanelCollapsed then Border.height r.Height ]
                    | _ ->
                        let right = state.Side = DockSide.Right
                        [ Border.horizontalAlignment (if right then HorizontalAlignment.Right else HorizontalAlignment.Left)
                          Border.verticalAlignment (if state.IsPanelCollapsed then VerticalAlignment.Top else VerticalAlignment.Stretch)
                          Border.margin PanelSize.gap
                          Border.width state.DockedRect.Width ]
                // The same elements in every mode (only settings change), so whatever holds the
                // pointer keeps it: undocking mid-drag must not rebuild the title being dragged.
                let docked = state.Mode = PanelMode.Docked
                let body =
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.children [
                            edge state (docked && state.Side = DockSide.Right)
                            content state false true
                            edge state (docked && state.Side = DockSide.Left)
                        ]
                    ]
                Border.create (looks @ placement @ [ Border.child body ]))

    /// The highlight, over everything but the panel. In pick mode it also takes the pointer, so
    /// the app doesn't see it.
    let highlight =
        Component.define "InspectorHighlight" (fun ctx (state: InspectorState) ->
            follow ctx state
            let target = state.Target
            Border.create [
                Semantics.hidden true
                Border.isVisible (state.IsPicking || target.IsSome)
                Border.isHitTestVisible state.IsPicking
                Border.cursor Cursor.Crosshair
                Border.onPointerMove (fun a -> state.PickAt a.Position)
                Border.onPointerLeave (fun _ -> state.PickLeft())
                Border.onPointerDown (fun a ->
                    state.PickSelect a.Position
                    a.Handle())
                Border.child (Highlight.create [ Highlight.target target; Highlight.outlineOnly state.TargetIsSelection ])
            ])
