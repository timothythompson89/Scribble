/// M8 step 3a on the GPU: CheckBox, Switch, ToggleButton, Slider and TabControl in each of their
/// states (off, on, indeterminate, hover, pressed, focused, disabled), in the dark and light themes.
module Scribble.Tests.Rendering.ControlTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let private width, height = 560.0, 420.0

/// One row per control: its states side by side, each named so the test can put it in that state.
let private view =
    let row (children: View list) =
        StackPanel.create [ StackPanel.orientation Orientation.Horizontal; StackPanel.spacing 18.0; StackPanel.children children ]
    StackPanel.create [
        StackPanel.margin 20.0
        StackPanel.spacing 18.0
        StackPanel.children [
            row [
                CheckBox.create [ CheckBox.text "Off" ]
                CheckBox.create [ CheckBox.text "On"; CheckBox.isChecked true ]
                CheckBox.create [ CheckBox.text "Some"; CheckBox.isIndeterminate true ]
                CheckBox.create [ CheckBox.name "hover"; CheckBox.text "Hover" ]
                CheckBox.create [ CheckBox.name "focus"; CheckBox.text "Focus"; CheckBox.isChecked true ]
                CheckBox.create [ CheckBox.text "Off"; CheckBox.isEnabled false ]
            ]
            row [
                Switch.create [ Switch.text "Off" ]
                Switch.create [ Switch.text "On"; Switch.isChecked true ]
                Switch.create [ Switch.name "hover"; Switch.text "Hover" ]
                Switch.create [ Switch.name "focus"; Switch.text "Focus"; Switch.isChecked true ]
                Switch.create [ Switch.text "Off"; Switch.isEnabled false ]
            ]
            row [
                ToggleButton.create [ ToggleButton.text "Off" ]
                ToggleButton.create [ ToggleButton.text "On"; ToggleButton.isChecked true ]
                ToggleButton.create [ ToggleButton.name "focus"; ToggleButton.text "Focus"; ToggleButton.isChecked true ]
                ToggleButton.create [ ToggleButton.text "Off"; ToggleButton.isEnabled false ]
            ]
            Slider.create [ Slider.value 40.0 ]
            row [
                Slider.create [ Slider.name "hover"; Slider.width 120.0; Slider.value 0.0 ]
                Slider.create [ Slider.name "pressed"; Slider.width 120.0; Slider.value 100.0 ]
                Slider.create [ Slider.name "focus"; Slider.width 120.0; Slider.value 70.0 ]
                Slider.create [ Slider.width 120.0; Slider.value 30.0; Slider.isEnabled false ]
            ]
            row [
                Slider.create [ Slider.orientation Orientation.Vertical; Slider.height 60.0; Slider.value 25.0 ]
                TabControl.create [
                    TabControl.width 380.0
                    TabControl.selected 1
                    TabControl.tabs [
                        TabItem.create [ TabItem.header "First"; TabItem.content (TextBlock.create [ TextBlock.text "Page one" ]) ]
                        TabItem.create [ TabItem.header "Second"; TabItem.content (TextBlock.create [ TextBlock.margin 8.0; TextBlock.text "Page two" ]) ]
                        TabItem.create [ TabItem.header "Third" ]
                        TabItem.create [ TabItem.header "Off"; TabItem.isEnabled false ]
                    ]
                ]
            ]
        ]
    ]

type ControlTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Shoot(theme: Theme, name: string) =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome theme
        tree.Update view
        let size = Size.Create(width, height)
        tree.UpdateLayout size
        // The states, as the input router would set them.
        for e in all tree.Root.Value do
            match e.Name with
            | "hover" -> e.ChangePointersOver 1
            | "pressed" ->
                e.ChangePointersOver 1
                e.ChangePointersPressing 1
            | "focus" -> e.SetFocusState(true, true)
            | _ -> ()
        // Past every transition (hover tints, colors).
        for ms in [ 0.0; 1000.0 ] do
            tree.Time <- TimeSpan.FromMilliseconds ms
            tree.UpdateLayout size
            tree.RunAfterFrame()
        let pixels: PixelSize = { Width = int width; Height = int height }
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(theme.Get Palette.background)
                tree.Render f.DrawList)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``the controls in each state, dark``() = this.Shoot(Theme.dark [], "controls@1x")

    [<Fact>]
    member this.``the controls in each state, light``() = this.Shoot(Theme.light [], "controls-light@1x")
