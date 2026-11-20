/// The Lists page (M8 step 4b): a ListBox of 10,000 items whose selection mode a RadioGroup picks (the page keeps
/// the selection, by key, so reversing the order keeps it), and a small ListBox of colours with a view of its own
/// per item that keeps its own selection. The page fills the window: the long list takes the height left.
module Scribble.Samples.Gallery.ListsPage

open System
open System.Globalization
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples.Gallery.Common

type Item = { Id: int; Name: string }

let private words = [| "amber"; "birch"; "cobalt"; "delta"; "ember"; "fjord"; "garnet"; "harbor"; "indigo"; "juniper"; "kestrel"; "lumen" |]

let private number (n: int) = n.ToString("N0", CultureInfo.InvariantCulture)

let private items = lazy (Array.init 10_000 (fun i -> { Id = i + 1; Name = "#" + number (i + 1) + "  " + words[i % words.Length] + " " + words[(i * 7 + 3) % words.Length] }))

/// The selection mode's name on its radio button.
let private modeName (m: SelectionMode) =
    match m with
    | SelectionMode.Single -> "Single"
    | SelectionMode.Multiple -> "Multiple"
    | SelectionMode.None -> "None"

/// What the status line says about the selection.
let private describe (keys: int list) =
    match keys with
    | [] -> "Nothing selected"
    | [ k ] -> "Selected #" + number k
    | ks when ks.Length <= 4 -> "Selected " + String.Join(", ", ks |> List.map (fun k -> "#" + number k))
    | ks -> "Selected " + number ks.Length + " items"

type private Swatch = { Name: string; Color: Color; Note: string }

let private swatches =
    [| { Name = "Lagoon"; Color = Color.Hex 0x2A9D8F; Note = "teal, for calm" }
       { Name = "Saffron"; Color = Color.Hex 0xE9C46A; Note = "yellow, for warmth" }
       { Name = "Clay"; Color = Color.Hex 0xE76F51; Note = "orange, for energy" }
       { Name = "Night"; Color = Color.Hex 0x264653; Note = "deep blue-green" }
       { Name = "Orchid"; Color = Color.Hex 0xB56CC9; Note = "violet, for play" }
       { Name = "Moss"; Color = Color.Hex 0x6A994E; Note = "green, for growth" } |]

/// A swatch's view: a round chip of the colour, its name and a dim note.
let private swatchView (s: Swatch) =
    StackPanel.create [
        StackPanel.orientation Orientation.Horizontal
        StackPanel.spacing 10.0
        StackPanel.children [
            Border.create [
                Border.width 16.0
                Border.height 16.0
                Border.cornerRadius 8.0
                Border.background s.Color
                Border.verticalAlignment VerticalAlignment.Center
            ]
            TextBlock.create [ TextBlock.text s.Name; TextBlock.verticalAlignment VerticalAlignment.Center ]
            TextBlock.create [ TextBlock.text s.Note; TextBlock.foreground Palette.textDim; TextBlock.fontSize 12.0; TextBlock.verticalAlignment VerticalAlignment.Center ]
        ]
    ]

let private lists =
    Component.define "Lists" (fun ctx () ->
        let mode = ctx.useState SelectionMode.Single
        let selected = ctx.useState ([]: int list)
        let invoked = ctx.useState ""
        let reversed = ctx.useState false
        let source = ctx.useMemo(reversed.Current, fun r -> if r then Array.rev items.Value else items.Value)
        let colours = ctx.useState ""
        // Written in the order shown (view order is the order Tab takes): the long list says it fills.
        DockPanel.create [
            DockPanel.children [
                StackPanel.create [
                    DockPanel.dock Dock.Top
                    StackPanel.spacing 8.0
                    StackPanel.margin (0.0, 0.0, 0.0, 8.0)
                    StackPanel.children [
                        RadioGroup.create [
                            RadioGroup.orientation Orientation.Horizontal
                            RadioGroup.spacing 16.0
                            RadioGroup.selected mode.Current
                            RadioGroup.onSelectedChanged (fun (m: SelectionMode) ->
                                // A Single list can't show several: the selection starts again.
                                mode.Set m
                                selected.Set [])
                            RadioGroup.children [
                                for m in [ SelectionMode.Single; SelectionMode.Multiple; SelectionMode.None ] ->
                                    RadioButton.create [ RadioButton.value m; RadioButton.text (modeName m) ]
                            ]
                        ]
                        WrapPanel.create [
                            WrapPanel.spacing 12.0
                            WrapPanel.lineSpacing 6.0
                            WrapPanel.children [
                                Button.create [
                                    Button.text (if reversed.Current then "Original order" else "Reverse order")
                                    Button.onClick (fun _ -> reversed.Set(not reversed.Current))
                                ]
                                TextBlock.create [
                                    TextBlock.text (describe selected.Current + (if invoked.Current = "" then "" else " · invoked " + invoked.Current))
                                    TextBlock.foreground Palette.textDim
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                ]
                            ]
                        ]
                    ]
                ]
                ListBox.create [
                    DockPanel.dock Dock.Fill
                    ListBox.name "items"
                    ListBox.source source
                    ListBox.keySelector (fun (i: Item) -> i.Id)
                    ListBox.viewSelector (fun (i: Item) -> i.Name)
                    ListBox.selectionMode mode.Current
                    ListBox.selected selected.Current
                    ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Item, int>) -> selected.Set e.Keys)
                    ListBox.onItemInvoked (fun (e: ItemInvokedEventArgs<Item, int>) -> invoked.Set("#" + number e.Key))
                ]
                // The colours: a view of their own per item; the list keeps its own selection.
                StackPanel.create [
                    DockPanel.dock Dock.Bottom
                    StackPanel.margin (0.0, 16.0, 0.0, 0.0)
                    StackPanel.spacing 6.0
                    StackPanel.children [
                        TextBlock.create [ TextBlock.text "A view per item"; TextBlock.fontSize 15.0 ]
                        TextBlock.create [
                            TextBlock.text (if colours.Current = "" then "Swatches with a chip and a note; this list keeps its own selection." else "Picked " + colours.Current)
                            TextBlock.foreground Palette.textDim
                            TextBlock.fontSize 12.0
                            TextBlock.textWrapping Wrap
                        ]
                        ListBox.create [
                            ListBox.height 150.0
                            ListBox.name "swatches"
                            ListBox.source swatches
                            ListBox.keySelector (fun (s: Swatch) -> s.Name)
                            ListBox.viewSelector swatchView
                            ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Swatch, string>) ->
                                colours.Set(e.Key |> ValueOption.defaultValue ""))
                        ]
                    ]
                ]
            ]
        ])

let view () =
    fillingPage "Lists" "ListBox and RadioButton: 10,000 items, only those on screen exist; the selection is kept by key." (
        fillingSection
            "ListBox"
            "Click to select; Ctrl+click and Shift+click in Multiple; arrows, Home/End and PageUp/Down after Tab; double-click or Enter invokes. Ctrl+A selects all in Multiple."
            (lists ()))
