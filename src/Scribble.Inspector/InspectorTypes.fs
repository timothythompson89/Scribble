namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// Which side of the window the inspector's panel sits on.
[<RequireQualifiedAccess>]
type DockSide =
    | Left
    | Right

type internal InspectorOptions =
    { Side: DockSide
      ToggleKey: Key * Modifiers
      PickKey: Key * Modifiers
      IsOpen: bool
      /// Where the pop-out window comes from (none: no pop-out).
      Platform: IPlatform voption
      /// Name the pop-out after `WindowTitle.current` and follow it (in an app), else after the
      /// window's title when it opens.
      FollowAppTitle: bool
      /// The app's own formatting, tried before ours (`Inspector.format`).
      Format: obj -> string voption }

    static member Default =
        { Side = DockSide.Right
          ToggleKey = (Key.F 12, Modifiers.None)
          PickKey = (Key.Char 'c', Modifiers.Control ||| Modifiers.Shift)
          IsOpen = false
          Platform = ValueNone
          FollowAppTitle = false
          Format = fun _ -> ValueNone }

/// Where the panel is.
[<RequireQualifiedAccess>]
type internal PanelMode =
    | Docked
    | Floating
    | PoppedOut

/// What a drag on the panel does.
[<RequireQualifiedAccess>]
type internal DragKind =
    | Move
    | ResizeCorner
    | ResizeEdge
    /// The divider between the tree and the properties.
    | Split

[<Struct>]
type internal Drag = { Kind: DragKind; Start: Point; StartRect: Rect }

module internal PanelSize =
    let dockedWidth = 380.0
    let minWidth = 300.0
    let minHeight = 160.0
    /// Around a docked panel and between a floating one and the window's edges when clamped.
    let gap = 8.0
    let minProperties = 60.0
    /// What the header, the footer and a minimal tree take next to the properties.
    let besideProperties = 170.0

/// One inspector setting: `Inspector.side DockSide.Left`.
[<Struct>]
type InspectorSetting = internal InspectorSetting of (InspectorOptions -> InspectorOptions)

/// One row of the tree: a layer's heading or an element at a depth.
[<RequireQualifiedAccess>]
type internal Row =
    | Layer of Layer
    | Element of Element * depth: int

/// Names the inspector gives its own elements, to find them again.
module internal Names =
    /// The selected row's chevron and type name (scrolled into view sideways after a pick).
    [<Literal>]
    let SelectedLabel = "inspectorSelectedLabel"
