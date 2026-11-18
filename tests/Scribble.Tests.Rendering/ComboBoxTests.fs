/// M8 step 4c on the GPU: ComboBoxes closed (a selection, the placeholder, a view item, disabled) and one open by the
/// keyboard (its list in its flyout, the selected item with the focus ring), in the dark and light themes.
module Scribble.Tests.Rendering.ComboBoxTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Tests.Rendering.FlyoutTests

type Swatch = { Name: string; Color: Color }

let private swatches =
    [| { Name = "Lagoon"; Color = Color.Hex 0x2A9D8F }
       { Name = "Saffron"; Color = Color.Hex 0xE9C46A }
       { Name = "Clay"; Color = Color.Hex 0xE76F51 }
       { Name = "Night"; Color = Color.Hex 0x264653 } |]

let private swatchView (s: Swatch) =
    StackPanel.create [
        StackPanel.orientation Orientation.Horizontal
        StackPanel.spacing 8.0
        StackPanel.children [
            Border.create [ Border.width 14.0; Border.height 14.0; Border.cornerRadius 7.0; Border.background s.Color; Border.verticalAlignment VerticalAlignment.Center ]
            TextBlock.create [ TextBlock.text s.Name; TextBlock.verticalAlignment VerticalAlignment.Center ]
        ]
    ]

let private sizes = [| "Small"; "Medium"; "Large"; "Extra large" |]

/// Two columns: on the left the one that opens, then one under the pointer; on the right the placeholder, a view item
/// and a disabled one.
let private view =
    StackPanel.create [
        StackPanel.margin 20.0
        StackPanel.orientation Orientation.Horizontal
        StackPanel.spacing 20.0
        StackPanel.verticalAlignment VerticalAlignment.Top
        StackPanel.children [
            StackPanel.create [
                StackPanel.width 200.0
                StackPanel.spacing 150.0
                StackPanel.children [
                    ComboBox.create [ ComboBox.name "open"; ComboBox.source sizes; ComboBox.selected (ValueSome "Medium") ]
                    ComboBox.create [ ComboBox.name "hover"; ComboBox.source sizes; ComboBox.selected (ValueSome "Small") ]
                ]
            ]
            StackPanel.create [
                StackPanel.width 200.0
                StackPanel.spacing 12.0
                StackPanel.children [
                    ComboBox.create [ ComboBox.source sizes; ComboBox.placeholder "Pick a size" ]
                    ComboBox.create [
                        ComboBox.source swatches
                        ComboBox.keySelector (fun (s: Swatch) -> s.Name)
                        ComboBox.viewSelector swatchView
                        ComboBox.selected (ValueSome "Clay")
                    ]
                    ComboBox.create [ ComboBox.source sizes; ComboBox.selected (ValueSome "Large"); ComboBox.isEnabled false ]
                ]
            ]
        ]
    ]

let rec private find (name: string) (e: Element) : Element option =
    if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)

type ComboBoxTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Shoot(theme: Theme, name: string) =
        let pixels: PixelSize = { Width = 460; Height = 270 }
        let dispatcher = Dispatcher(ignore, fun _ _ -> ())
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme theme
            ]
        host.Update view
        host.HandleEvent Frame |> ignore
        // By keyboard: the first ComboBox takes focus, Enter opens its list, and focus lands on the selected item.
        host.MoveFocus FocusDirection.Next
        host.HandleEvent(KeyDown(Key.Enter, Modifiers.None, false)) |> ignore
        host.HandleEvent(KeyUp(Key.Enter, Modifiers.None)) |> ignore
        host.HandleEvent Frame |> ignore
        Assert.Equal(2, host.Layers.Count)
        (find "hover" host.Tree.Root.Value).Value.ChangePointersOver 1
        // Past any hover fade.
        for _ in 1..3 do
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 1.0
            host.HandleEvent Frame |> ignore
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear host.ClearColor
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``combo boxes closed and one open, dark``() = this.Shoot(Theme.dark [], "combobox@1x")

    [<Fact>]
    member this.``combo boxes closed and one open, light``() = this.Shoot(Theme.light [], "combobox-light@1x")
