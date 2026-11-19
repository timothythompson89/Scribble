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

    /// M9 step 9c: several lines. The first box has focus: a sentence that wraps and a second paragraph, selected
    /// from the middle of one line to the middle of another two lines up (a line break among it), the caret at
    /// the selection's start. The second holds eight lines in a box of three, scrolled by the wheel, with its
    /// scrollbar. The third shows a placeholder that wraps.
    member private _.ShootLines(theme: Theme, name: string) =
        let pixels: PixelSize = { Width = 500; Height = 200 }
        let dispatcher = Dispatcher(ignore, fun _ _ -> ())
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme theme
            ]
        let view =
            StackPanel.create [
                StackPanel.margin 20.0
                StackPanel.orientation Orientation.Horizontal
                StackPanel.spacing 20.0
                StackPanel.verticalAlignment VerticalAlignment.Top
                StackPanel.children [
                    TextBox.create [ TextBox.name "notes"; TextBox.width 220.0; TextBox.multiline true; TextBox.verticalAlignment VerticalAlignment.Top ]
                    StackPanel.create [
                        StackPanel.width 220.0
                        StackPanel.spacing 12.0
                        StackPanel.children [
                            TextBox.create [
                                TextBox.name "scrolled"
                                TextBox.multiline true
                                TextBox.maxLines 3
                                TextBox.text "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight"
                            ]
                            TextBox.create [
                                TextBox.multiline true
                                TextBox.minLines 2
                                TextBox.placeholder "A placeholder long enough to wrap onto a second line"
                            ]
                        ]
                    ]
                ]
            ]
        host.Update view
        host.HandleEvent Frame |> ignore
        let draw () =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear host.ClearColor
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        let at: PixelPoint = { X = 60.0; Y = 40.0 }
        host.HandleEvent(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
        host.HandleEvent(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
        let notes = (find "notes" host.Tree.Root.Value).Value
        Assert.True(notes.IsFocused)
        host.HandleEvent(TextInput "The quick brown fox jumps over the lazy dog.") |> ignore
        host.HandleEvent(KeyDown(Key.Enter, Modifiers.None, false)) |> ignore
        host.HandleEvent(TextInput "A second paragraph") |> ignore
        // A frame: the box grows to its text and wraps at its width.
        host.HandleEvent Frame |> ignore
        draw () |> ignore
        for _ in 1..8 do
            host.HandleEvent(KeyDown(Key.Left, Modifiers.None, false)) |> ignore
        for _ in 1..2 do
            host.HandleEvent(KeyDown(Key.Up, Modifiers.Shift, false)) |> ignore
        let selected = (TextBox.editOf notes).SelectedText
        Assert.Contains("\n", selected)
        Assert.True((TextBox.stateOf notes).Lines.LineCount >= 3)
        // The wheel over the second box: two notches.
        let scrolled = (find "scrolled" host.Tree.Root.Value).Value
        let over: PixelPoint = { X = 300.0; Y = 50.0 }
        host.HandleEvent(PointerWheel(over, 0.0, -1.0, Modifiers.None)) |> ignore
        Assert.Equal(48.0, (TextBox.stateOf scrolled).ScrollY)
        // Past the border's fade to the accent.
        for _ in 1..3 do
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 1.0
            host.HandleEvent Frame |> ignore
        let image = draw ()
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    /// M9 step 9d: what an input method is composing, underlined, in one line (after typed text) and wrapped over
    /// two lines.
    [<Fact>]
    member _.``composing text is underlined``() =
        let pixels: PixelSize = { Width = 500; Height = 120 }
        let dispatcher = Dispatcher(ignore, fun _ _ -> ())
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme (Theme.dark [])
            ]
        let view =
            StackPanel.create [
                StackPanel.margin 20.0
                StackPanel.orientation Orientation.Horizontal
                StackPanel.spacing 20.0
                StackPanel.verticalAlignment VerticalAlignment.Top
                StackPanel.children [
                    TextBox.create [ TextBox.name "one"; TextBox.width 220.0; TextBox.verticalAlignment VerticalAlignment.Top ]
                    TextBox.create [ TextBox.name "several"; TextBox.width 220.0; TextBox.multiline true; TextBox.minLines 2 ]
                ]
            ]
        host.Update view
        host.HandleEvent Frame |> ignore
        let draw () =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear host.ClearColor
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        let click (x: float) =
            let at: PixelPoint = { X = x; Y = 40.0 }
            host.HandleEvent(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
            host.HandleEvent(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
        // The second box first: composing there, then the focus leaves, which types it. Then the first, left composing.
        click 300.0
        host.HandleEvent(TextInput "Typed, then ") |> ignore
        host.HandleEvent(TextComposition("composing over the wrap", 9)) |> ignore
        host.HandleEvent Frame |> ignore
        let several = draw ()
        click 60.0
        host.HandleEvent(TextInput "Tokyo ") |> ignore
        host.HandleEvent(TextComposition("nihongo", 4)) |> ignore
        let one = (find "one" host.Tree.Root.Value).Value
        Assert.Equal("Tokyo ", (TextBox.editOf one).Text)
        Assert.Equal("Tokyo nihongo", (TextBox.stateOf one).Shown.Text)
        // Drawn once so the borders start their fade (to the accent, and back from it), then drawn again well
        // past it (a frame would put the trees' time back to the clock's).
        host.HandleEvent Frame |> ignore
        draw () |> ignore
        for l in host.Layers do
            l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 10.0
        for name, image in [ "textbox-composing-lines@1x", several; "textbox-composing@1x", draw () ] do
            match Snapshot.check name image with
            | Some error -> Assert.Fail error
            | None -> ()

    [<Fact>]
    member this.``text boxes of several lines: a selection across lines, one scrolled, dark``() = this.ShootLines(Theme.dark [], "textbox-lines@1x")

    [<Fact>]
    member this.``text boxes of several lines: a selection across lines, one scrolled, light``() =
        this.ShootLines(Theme.light [], "textbox-lines-light@1x")

    [<Fact>]
    member this.``text boxes, one focused with a selection, dark``() = this.Shoot(Theme.dark [], "textbox@1x")

    [<Fact>]
    member this.``text boxes, one focused with a selection, light``() = this.Shoot(Theme.light [], "textbox-light@1x")
