/// The Controls page (M8 step 3a): CheckBox (with a "select all" that shows a dash), Switch, ToggleButton,
/// Slider, ComboBox (M8 step 4c) and TextBox (M9 step 9a), each controlled by the page's state next to one that keeps its own value.
module Scribble.Samples.Gallery.ControlsPage

open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Accessibility
open Scribble.Samples.Gallery.Common

/// Controls one under another.
let private column (children: View list) =
    StackPanel.create [ StackPanel.spacing 10.0; StackPanel.children children ]

let private row (children: View list) =
    StackPanel.create [
        StackPanel.orientation Orientation.Horizontal
        StackPanel.spacing 12.0
        StackPanel.children children
    ]

/// Side by side while there is room, then one under another (a phone).
let private wrap (children: View list) =
    WrapPanel.create [ WrapPanel.spacing 12.0; WrapPanel.lineSpacing 10.0; WrapPanel.children children ]

let private toppings = [ "Cheese"; "Olives"; "Mushrooms" ]

let private sizes = [| "Small"; "Medium"; "Large"; "Extra large" |]

let private numbers = lazy (Array.init 10_000 (fun i -> "Item " + string (i + 1)))

type private Swatch = { Name: string; Color: Color }

let private swatches =
    [| { Name = "Lagoon"; Color = Color.Hex 0x2A9D8F }
       { Name = "Saffron"; Color = Color.Hex 0xE9C46A }
       { Name = "Clay"; Color = Color.Hex 0xE76F51 }
       { Name = "Night"; Color = Color.Hex 0x264653 }
       { Name = "Orchid"; Color = Color.Hex 0xB56CC9 }
       { Name = "Moss"; Color = Color.Hex 0x6A994E } |]

/// A swatch's view: a round chip of the colour and its name.
let private swatchView (s: Swatch) =
    StackPanel.create [
        StackPanel.orientation Orientation.Horizontal
        StackPanel.spacing 8.0
        StackPanel.children [
            Border.create [ Border.width 14.0; Border.height 14.0; Border.cornerRadius 7.0; Border.background s.Color; Border.verticalAlignment VerticalAlignment.Center ]
            TextBlock.create [ TextBlock.text s.Name; TextBlock.verticalAlignment VerticalAlignment.Center ]
        ]
    ]

let private controls =
    Component.define "Controls" (fun ctx () ->
        let picked = ctx.useState (Set.ofList [ "Cheese" ])
        let wifi = ctx.useState true
        let bold = ctx.useState false
        let volume = ctx.useState 40.0
        let committed = ctx.useState 40.0
        let size = ctx.useState "Medium"
        let name = ctx.useState "Ada"
        let submitted = ctx.useState ""
        let clipboard = ctx.useClipboard ()
        let greeting = if name.Current = "" then "Hello." else "Hello, " + name.Current + "."
        let some = picked.Current
        StackPanel.create [
            StackPanel.spacing 22.0
            StackPanel.children [
                section "CheckBox" "Controlled by the page's state; \"All toppings\" shows a dash while only some are picked." (
                    column [
                        CheckBox.create [
                            CheckBox.text "All toppings"
                            CheckBox.isChecked (some.Count = toppings.Length)
                            CheckBox.isIndeterminate (some.Count > 0 && some.Count < toppings.Length)
                            CheckBox.onCheckedChanged (fun on -> picked.Set(if on then Set.ofList toppings else Set.empty))
                        ]
                        for t in toppings do
                            CheckBox.create [
                                CheckBox.margin (26.0, 0.0, 0.0, 0.0)
                                CheckBox.text t
                                CheckBox.isChecked (some.Contains t)
                                CheckBox.onCheckedChanged (fun on -> picked.Set(if on then some.Add t else some.Remove t))
                            ]
                        CheckBox.create [ CheckBox.text "No isChecked: it keeps its own value" ]
                        CheckBox.create [ CheckBox.text "Disabled"; CheckBox.isChecked true; CheckBox.isEnabled false ]
                    ])
                section "Switch and ToggleButton" "The first switch is controlled; the next keeps its own value." (
                    column [
                        Switch.create [
                            Switch.text (if wifi.Current then "Wi-Fi is on" else "Wi-Fi is off")
                            Switch.isChecked wifi.Current
                            Switch.onCheckedChanged wifi.Set
                        ]
                        Switch.create [ Switch.text "No isChecked: it keeps its own value" ]
                        Switch.create [ Switch.text "Disabled"; Switch.isEnabled false ]
                        row [
                            ToggleButton.create [ ToggleButton.text "Bold"; ToggleButton.isChecked bold.Current; ToggleButton.onCheckedChanged bold.Set ]
                            TextBlock.create [
                                TextBlock.text "Some text"
                                TextBlock.verticalAlignment VerticalAlignment.Center
                                TextBlock.font (if bold.Current then Fonts.SemiBold() else Fonts.Regular())
                            ]
                        ]
                    ])
                section "Slider" "The first is controlled: its value below follows every change, \"let go\" only when you let go." (
                    column [
                        Slider.create [
                            Semantics.label "Volume"
                            Slider.value volume.Current
                            Slider.onValueChanged volume.Set
                            Slider.onValueCommitted committed.Set
                        ]
                        TextBlock.create [ TextBlock.text $"Value {volume.Current:F0}, let go at {committed.Current:F0}" ]
                        Slider.create [ Slider.step 0.0; Semantics.label "Smooth" ]
                        Slider.create [ Slider.value 30.0; Slider.isEnabled false; Semantics.label "Disabled" ]
                        // Vertical, in steps of 10, each keeping its own value (minimum at the bottom).
                        row [
                            for i in 1..3 ->
                                Slider.create [
                                    Slider.orientation Orientation.Vertical
                                    Slider.height 120.0
                                    Slider.step 10.0
                                    Semantics.label ("Level " + string i)
                                ]
                        ]
                    ])
                section "ComboBox" "A click, Space or Enter opens the list: there the arrows move, Enter or a click picks, Escape closes. Closed, ↑ and ↓ pick straight away." (
                    column [
                        wrap [
                            // Controlled: the line below follows it.
                            ComboBox.create [
                                ComboBox.width 220.0
                                ComboBox.source sizes
                                ComboBox.selected (ValueSome size.Current)
                                ComboBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<string, string>) ->
                                    e.Key |> ValueOption.iter size.Set)
                            ]
                            // A view per item; it keeps its own selection.
                            ComboBox.create [
                                ComboBox.width 220.0
                                ComboBox.source swatches
                                ComboBox.keySelector (fun (s: Swatch) -> s.Name)
                                ComboBox.viewSelector swatchView
                                ComboBox.placeholder "Pick a colour"
                            ]
                        ]
                        TextBlock.create [ TextBlock.text ("Size: " + size.Current) ]
                        wrap [
                            // 10,000 items: the list realizes only what shows.
                            ComboBox.create [
                                ComboBox.width 220.0
                                ComboBox.source numbers.Value
                                ComboBox.placeholder "One of 10,000"
                            ]
                            ComboBox.create [ ComboBox.width 220.0; ComboBox.source sizes; ComboBox.selected (ValueSome "Large"); ComboBox.isEnabled false ]
                        ]
                    ])
                section "TextBox" "Click and type. ←/→ move (Ctrl by word), Shift selects, Ctrl+A selects all; a double-click selects a word, a triple-click everything; Tab into one selects its text." (
                    column [
                        wrap [
                            // Controlled: the line below follows every key.
                            TextBox.create [
                                TextBox.width 220.0
                                TextBox.text name.Current
                                TextBox.placeholder "Your name"
                                TextBox.onTextChanged name.Set
                            ]
                            // It keeps its own text; Enter submits it.
                            TextBox.create [
                                TextBox.width 220.0
                                TextBox.placeholder "Type, then press Enter"
                                TextBox.onSubmitted submitted.Set
                            ]
                        ]
                        wrap [
                            TextBlock.create [
                                TextBlock.text (greeting + (if submitted.Current = "" then "" else "  Submitted: " + submitted.Current))
                                TextBlock.verticalAlignment VerticalAlignment.Center
                            ]
                            // The clipboard from app code: paste the greeting into a box (Ctrl+V), or anywhere else.
                            Button.create [
                                Button.text "Copy"
                                Button.padding (12.0, 4.0)
                                Semantics.label "Copy the greeting"
                                Button.onClick (fun _ -> clipboard.SetText greeting)
                            ]
                        ]
                        wrap [
                            TextBox.create [ TextBox.width 220.0; TextBox.placeholder "Password"; TextBox.isPassword true ]
                            TextBox.create [ TextBox.width 220.0; TextBox.placeholder "Up to 8 characters"; TextBox.maxLength 8 ]
                        ]
                        wrap [
                            TextBox.create [
                                TextBox.width 220.0
                                TextBox.text "Read-only: select, not change"
                                TextBox.isReadOnly true
                                Semantics.label "Read-only"
                            ]
                            TextBox.create [ TextBox.width 220.0; TextBox.text "Disabled"; TextBox.isEnabled false; Semantics.label "Disabled" ]
                        ]
                    ])
            ]
        ])

let view () =
    page "Controls" "Each control is controlled when the view gives its value, and keeps its own otherwise." [ controls () ]
