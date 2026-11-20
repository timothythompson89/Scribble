/// M8 step 4b on the GPU: ListBox items (plain, selected, hover, pressed, focused, disabled) and RadioButtons
/// (off, chosen, hover, focused, disabled), in the dark and light themes.
module Scribble.Tests.Rendering.ListTests

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

let private width, height = 460.0, 330.0

let private names = [| "Plain"; "Selected"; "Hover"; "Pressed"; "Selected, focused"; "Selected, hover"; "Plain" |]

/// A Multiple list (items 1, 4 and 5 selected) beside a RadioGroup (Pear chosen).
let private view =
    StackPanel.create [
        StackPanel.margin 20.0
        StackPanel.orientation Orientation.Horizontal
        StackPanel.spacing 24.0
        StackPanel.children [
            ListBox.create [
                ListBox.width 220.0
                ListBox.height 290.0
                ListBox.source [ 0 .. names.Length - 1 ]
                ListBox.viewSelector (fun (i: int) -> names[i])
                ListBox.selectionMode SelectionMode.Multiple
                ListBox.selected [ 1; 4; 5 ]
            ]
            RadioGroup.create [
                RadioGroup.selected "Pear"
                RadioGroup.children [
                    RadioButton.create [ RadioButton.value "Apple"; RadioButton.text "Off" ]
                    RadioButton.create [ RadioButton.value "Pear"; RadioButton.text "Chosen" ]
                    RadioButton.create [ RadioButton.name "hover"; RadioButton.value "Plum"; RadioButton.text "Hover" ]
                    RadioButton.create [ RadioButton.name "focus"; RadioButton.value "Fig"; RadioButton.text "Focus" ]
                    RadioButton.create [ RadioButton.value "Kiwi"; RadioButton.text "Disabled"; RadioButton.isEnabled false ]
                ]
            ]
        ]
    ]

type ListTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Shoot(theme: Theme, name: string) =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome theme
        tree.Update view
        let size = Size.Create(width, height)
        tree.UpdateLayout size
        tree.UpdateLayout size
        // The states, as the input router would set them: items by key, radios by name.
        for e in all tree.Root.Value do
            let item = if obj.ReferenceEquals(e.Type, ListBoxItem.Type) then unbox<int> (e.Get ListBoxItemProps.key) else -1
            match e.Name, item with
            | "hover", _
            | _, 2
            | _, 5 -> e.ChangePointersOver 1
            | _, 3 ->
                e.ChangePointersOver 1
                e.ChangePointersPressing 1
            | "focus", _
            | _, 4 -> e.SetFocusState(true, true)
            | _ -> ()
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
    member this.``list items and radio buttons in each state, dark``() = this.Shoot(Theme.dark [], "lists@1x")

    [<Fact>]
    member this.``list items and radio buttons in each state, light``() = this.Shoot(Theme.light [], "lists-light@1x")
