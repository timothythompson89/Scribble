/// The Controls page (M8 step 3a): CheckBox (with a "select all" that shows a dash), Switch, ToggleButton and
/// Slider, each controlled by the page's state next to one that keeps its own value.
module Scribble.Samples.Gallery.ControlsPage

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

let private toppings = [ "Cheese"; "Olives"; "Mushrooms" ]

let private controls =
    Component.define "Controls" (fun ctx () ->
        let picked = ctx.useState (Set.ofList [ "Cheese" ])
        let wifi = ctx.useState true
        let bold = ctx.useState false
        let volume = ctx.useState 40.0
        let committed = ctx.useState 40.0
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
            ]
        ])

let view () =
    page "Controls" "Each control is controlled when the view gives its value, and keeps its own otherwise." [ controls () ]
