/// M9 step 9a on the GPU: TextBoxes in the dark and light themes. One has focus from a click: its text is wider
/// than the box, scrolled to its end, with the last two words selected and the caret at the selection's start.
/// The others show the placeholder, text, a password's bullets, a read-only and a disabled one.
module Scribble.Tests.Rendering.TextBoxTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Tests.Rendering.FlyoutTests

let private view =
    StackPanel.create [
        StackPanel.margin 20.0
        StackPanel.orientation Orientation.Horizontal
        StackPanel.spacing 20.0
        StackPanel.verticalAlignment VerticalAlignment.Top
        StackPanel.children [
            StackPanel.create [
                StackPanel.width 200.0
                StackPanel.spacing 12.0
                StackPanel.children [
                    TextBox.create [ TextBox.name "typed"; TextBox.placeholder "Type here" ]
                    TextBox.create [ TextBox.placeholder "Your name" ]
                    TextBox.create [ TextBox.text "open sesame"; TextBox.isPassword true ]
                ]
            ]
            StackPanel.create [
                StackPanel.width 200.0
                StackPanel.spacing 12.0
                StackPanel.children [
                    TextBox.create [ TextBox.text "Ada Lovelace" ]
                    TextBox.create [ TextBox.text "Read-only"; TextBox.isReadOnly true ]
                    TextBox.create [ TextBox.text "Disabled"; TextBox.isEnabled false ]
                ]
            ]
        ]
    ]

let rec private find (name: string) (e: Element) : Element option =
    if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)

type TextBoxTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Shoot(theme: Theme, name: string) =
        let pixels: PixelSize = { Width = 460; Height = 160 }
        // Delayed work never runs: the caret stays on.
        let dispatcher = Dispatcher(ignore, fun _ _ -> ())
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme theme
            ]
        host.Update view
        host.HandleEvent Frame |> ignore
        // A click into the first box, a sentence wider than it, then Ctrl+Shift+Left twice.
        let at: PixelPoint = { X = 60.0; Y = 36.0 }
        host.HandleEvent(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
        host.HandleEvent(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
        let typed = (find "typed" host.Tree.Root.Value).Value
        Assert.True(typed.IsFocused)
        host.HandleEvent(TextInput "The quick brown fox jumps over the lazy dog") |> ignore
        let draw () =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear host.ClearColor
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        // Drawn with the caret at the end, as an app's frame would: the text scrolls to its end.
        draw () |> ignore
        for _ in 1..2 do
            host.HandleEvent(KeyDown(Key.Left, Modifiers.Control ||| Modifiers.Shift, false)) |> ignore
            host.HandleEvent(KeyUp(Key.Left, Modifiers.Control ||| Modifiers.Shift)) |> ignore
        Assert.Equal("lazy dog", (TextBox.editOf typed).SelectedText)
        // Past the border's fade to the accent.
        for _ in 1..3 do
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 1.0
            host.HandleEvent Frame |> ignore
        let image = draw ()
        // Drawn: the text is scrolled so the caret shows.
        Assert.True((TextBox.stateOf typed).Scroll > 0.0)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``text boxes, one focused with a selection, dark``() = this.Shoot(Theme.dark [], "textbox@1x")

    [<Fact>]
    member this.``text boxes, one focused with a selection, light``() = this.Shoot(Theme.light [], "textbox-light@1x")
