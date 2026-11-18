/// The Layout page (M8 step 2): a form on a Grid, an app shell on a DockPanel with Separators, boxes to drag on a
/// Canvas (a click brings one to the front with zIndex; the selected one has a dashed outline) and chips in a
/// WrapPanel.
module Scribble.Samples.Gallery.LayoutPage

open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples.Gallery.Common

/// A stand-in text field (TextBox comes with M9).
let private field (placeholder: string) (settings: Setting<Border> list) =
    Border.create [
        yield Border.background Palette.surface
        yield Border.borderBrush Palette.border
        yield Border.borderThickness 1.0
        yield Border.cornerRadius 4.0
        yield Border.padding (8.0, 5.0)
        yield Border.content (TextBlock.create [ TextBlock.text placeholder; TextBlock.foreground Palette.textDim; TextBlock.textTrimming CharacterEllipsis ])
        yield! settings
    ]

let private label (text: string) (row: int) =
    TextBlock.create [ TextBlock.text text; Grid.row row; TextBlock.verticalAlignment VerticalAlignment.Center ]

/// Labels in an Auto column, fields in a star column that keeps at least 120 DIPs, small buttons in
/// another Auto column; the notes span two columns; the last row's buttons sit at the right.
let private form =
    Grid.create [
        Grid.name "form"
        Grid.columns [
            GridColumn.create [ GridColumn.width "auto" ]
            GridColumn.create [ GridColumn.width "*"; GridColumn.minWidth 120.0 ]
            GridColumn.create [ GridColumn.width "auto" ]
        ]
        Grid.rows "auto, auto, auto, auto"
        Grid.columnSpacing 12.0
        Grid.rowSpacing 8.0
        Grid.children [
            label "Name" 0
            field "Ada Lovelace" [ Grid.cell (0, 1) ]
            Button.create [ Button.text "Check"; Grid.cell (0, 2) ]
            label "Email" 1
            field "ada@example.com" [ Grid.cell (1, 1) ]
            Button.create [ Button.text "Verify"; Grid.cell (1, 2) ]
            label "Notes" 2
            field "Spans the field and button columns" [ Grid.cell (2, 1); Grid.columnSpan 2; Border.height 56.0 ]
            StackPanel.create [
                Grid.cell (3, 1)
                Grid.columnSpan 2
                StackPanel.orientation Horizontal
                StackPanel.spacing 8.0
                StackPanel.horizontalAlignment HorizontalAlignment.Right
                StackPanel.children [ Button.create [ Button.text "Cancel" ]; Button.create [ Button.text "Save"; Button.classes [ "primary" ] ] ]
            ]
        ]
    ]

/// Toolbar on top, status bar at the bottom, a sidebar, and the content filling the rest.
let private shell =
    Border.create [
        Border.height 200.0
        Border.borderBrush Palette.border
        Border.borderThickness 1.0
        Border.cornerRadius 6.0
        Border.clipToBounds true
        Border.content (
            DockPanel.create [
                DockPanel.name "shell"
                DockPanel.children [
                    StackPanel.create [
                        DockPanel.dock Dock.Top
                        StackPanel.orientation Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.margin 6.0
                        StackPanel.children [
                            Button.create [ Button.text "New"; Button.padding (10.0, 4.0) ]
                            Button.create [ Button.text "Open"; Button.padding (10.0, 4.0) ]
                            Separator.create [ Separator.orientation Vertical; Separator.margin (4.0, 2.0) ]
                            Button.create [ Button.text "Share"; Button.padding (10.0, 4.0) ]
                        ]
                    ]
                    Separator.create [ DockPanel.dock Dock.Top ]
                    TextBlock.create [
                        DockPanel.dock Dock.Bottom
                        TextBlock.text "Ready · 3 items"
                        TextBlock.fontSize 12.0
                        TextBlock.foreground Palette.textDim
                        TextBlock.margin (8.0, 4.0)
                    ]
                    Separator.create [ DockPanel.dock Dock.Bottom; Separator.lineStyle BorderStyle.Dashed ]
                    StackPanel.create [
                        DockPanel.dock Dock.Left
                        StackPanel.width 110.0
                        StackPanel.margin 8.0
                        StackPanel.spacing 6.0
                        StackPanel.children [
                            for item in [ "Inbox"; "Drafts"; "Archive" ] -> TextBlock.create [ TextBlock.text item ]
                        ]
                    ]
                    Separator.create [ DockPanel.dock Dock.Left; Separator.orientation Vertical ]
                    TextBlock.create [
                        TextBlock.text "The last child fills what's left"
                        TextBlock.foreground Palette.textDim
                        TextBlock.textWrapping Wrap
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.verticalAlignment VerticalAlignment.Center
                        TextBlock.margin 8.0
                    ]
                ]
            ]
        )
    ]

type private CanvasBox = { Id: int; Label: string; X: float; Y: float; Z: int; Color: Color }

let private initialBoxes =
    [ { Id = 1; Label = "Drag me"; X = 16.0; Y = 16.0; Z = 1; Color = Color.Hex 0x3B5BA5 }
      { Id = 2; Label = "Or me"; X = 96.0; Y = 56.0; Z = 2; Color = Color.Hex 0x7A4FA0 }
      { Id = 3; Label = "Me too"; X = 176.0; Y = 26.0; Z = 3; Color = Color.Hex 0x2E7D6B } ]

/// Boxes at positions; pressing one selects it and brings it to the front, dragging moves it.
let private canvasDemo =
    Component.define "CanvasDemo" (fun ctx () ->
        let boxes = ctx.useState initialBoxes
        let selected = ctx.useState 3
        // The drag in progress: which box, where the pointer went down, where the box was.
        let drag = ctx.useMemo((), fun () -> ref (ValueNone: struct (int * Point * float * float) voption))
        let view (b: CanvasBox) =
            Border.create [
                Border.key b.Id
                Canvas.left b.X
                Canvas.top b.Y
                Border.zIndex b.Z
                Border.width 110.0
                Border.height 64.0
                Border.cornerRadius 8.0
                Border.background b.Color
                Border.borderThickness 2.0
                // The theme's text color: it shows against the page in both themes (white vanished in light).
                if selected.Current = b.Id then Border.borderBrush Palette.text else Border.borderBrush Color.Transparent
                Border.borderStyle BorderStyle.Dashed
                Border.cursor Cursor.Move
                Border.onPointerDown (fun e ->
                    let top = (boxes.Current |> List.map _.Z |> List.max) + 1
                    boxes.Set(boxes.Current |> List.map (fun x -> if x.Id = b.Id then { x with Z = top } else x))
                    selected.Set b.Id
                    drag.Value <- ValueSome(struct (b.Id, e.Position, b.X, b.Y))
                    e.Handle())
                Border.onPointerMove (fun e ->
                    match drag.Value with
                    | ValueSome(struct (id, start, x0, y0)) when id = b.Id ->
                        let x, y = max 0.0 (x0 + e.Position.X - start.X), max 0.0 (y0 + e.Position.Y - start.Y)
                        boxes.Set(boxes.Current |> List.map (fun x' -> if x'.Id = id then { x' with X = x; Y = y } else x'))
                    | _ -> ())
                Border.onPointerUp (fun _ -> drag.Value <- ValueNone)
                Border.content (
                    TextBlock.create [
                        TextBlock.text (b.Label + "\nz " + string b.Z)
                        TextBlock.foreground Color.White
                        TextBlock.fontSize 12.0
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                        TextBlock.verticalAlignment VerticalAlignment.Center
                    ]
                )
            ]
        Border.create [
            Border.height 180.0
            Border.background Palette.surface
            Border.cornerRadius 6.0
            Border.clipToBounds true
            Border.content (Canvas.create [ Canvas.name "canvas"; Canvas.children [ for b in boxes.Current -> view b ] ])
        ])

/// A chip; `settings` add to these (setting one of them again would warn in Debug builds).
let private chipWith (thickness: float) (text: string) (settings: Setting<Border> list) =
    Border.create [
        yield Border.padding (10.0, 4.0)
        yield Border.cornerRadius 12.0
        yield Border.borderThickness thickness
        yield Border.borderBrush line
        yield Border.content (TextBlock.create [ TextBlock.text text; TextBlock.fontSize 12.0 ])
        yield! settings
    ]

let private chip = chipWith 1.0

let private chips =
    WrapPanel.create [
        WrapPanel.spacing 6.0
        WrapPanel.lineSpacing 6.0
        WrapPanel.children [
            chip "F#" []
            chip "OCaml" []
            chip "Elm" [ WrapPanel.breakAfter true ]
            chip "WebGPU" []
            chip "HarfBuzz" []
            chip "wgpu-native" []
            chip "NativeAOT" []
            chip "WebAssembly" []
            chipWith 2.0 "dotted" [ Border.borderStyle BorderStyle.Dotted ]
            chip "dashed" [ Border.borderStyle BorderStyle.Dashed ]
        ]
    ]

let view () =
    page "Layout" "The panels: rows and columns, docking, positions of your own and wrapping." [
        section "Grid" "A form: labels in an Auto column, fields in a star column (at least 120), a span." form
        section "DockPanel" "An app shell: toolbar, status bar, sidebar, Separators; the last child fills." shell
        section "Canvas" "Drag the boxes; a press brings one to the front (zIndex) and outlines it." (canvasDemo ())
        section "WrapPanel" "Chips that wrap; breakAfter after Elm starts a new row." chips
    ]
