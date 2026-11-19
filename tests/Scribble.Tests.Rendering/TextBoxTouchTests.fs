/// M9 step 9g on the GPU: a TextBox after a finger's long press on a word (the word selected, a selection handle at
/// each end, the context menu as a row above it), in the dark and light themes and at 2×; and after a tap (the
/// caret with its handle).
module Scribble.Tests.Rendering.TextBoxTouchTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Tests.Rendering.FlyoutTests

/// A window that is never shown, at a scale.
type private ScaledWindow(size: PixelSize, scale: float) =
    interface IWindow with
        member _.ClientSize = size
        member _.Scale = scale
        member _.Title with get () = "" and set _ = ()
        member _.Surface = BrowserCanvas "test"
        member _.IsClosed = false
        member _.Show() = ()
        member _.Close() = ()
        member _.RequestFrame() = ()
        member _.SetCursor _ = ()
        member _.ClaimKey(_, _) = ()
        member _.SetBackground(_, _, _) = ()
        member _.SetTextInput _ = ()
        member _.ClearComposition() = ()
        member _.Dispose() = ()

let private view =
    StackPanel.create [
        StackPanel.margin (20.0, 70.0, 20.0, 20.0)
        StackPanel.spacing 12.0
        StackPanel.children [
            TextBox.create [ TextBox.name "box"; TextBox.text "The quick brown fox jumps" ]
            TextBlock.create [ TextBlock.text "Hold a word to select it." ]
        ]
    ]

let rec private find (name: string) (e: Element) : Element option =
    if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)

type TextBoxTouchTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    /// `hold`: a long press on "quick"; otherwise a tap inside "brown".
    member private _.Shoot(theme: Theme, scale: float, hold: bool, name: string) =
        let pixels: PixelSize = { Width = int (300.0 * scale); Height = int (190.0 * scale) }
        // Delayed work is kept, and run by hand: the long press's.
        let timers = ResizeArray<unit -> unit>()
        let dispatcher = Dispatcher(ignore, fun _ action -> timers.Add action)
        let host =
            ElementHost.create (new ScaledWindow(pixels, scale)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme theme
            ]
        host.Update view
        host.HandleEvent Frame |> ignore
        let box = (find "box" host.Tree.Root.Value).Value
        let origin = box.PositionInTree
        let stops = fixture.Text.CaretStops("The quick brown fox jumps", box.Get TextProps.font, box.Get TextProps.fontSize)
        // In window pixels: the middle of the box's line, at a place in its text (13 DIPs in: border and padding).
        let at (index: int) : PixelPoint =
            { X = (origin.X + 13.0 + stops[index] + 2.0) * scale; Y = (origin.Y + box.Bounds.Height / 2.0) * scale }
        if hold then
            host.HandleEvent(PointerPressed(Touch, 1, at 6, MouseButton.Left, Modifiers.None)) |> ignore
            // The host's long-press timer is the last thing the press asked for.
            timers[timers.Count - 1] ()
            host.HandleEvent(PointerReleased(Touch, 1, at 6, MouseButton.Left, Modifiers.None)) |> ignore
            Assert.Equal("quick", (TextBox.editOf box).SelectedText)
        else
            host.HandleEvent(PointerPressed(Touch, 1, at 13, MouseButton.Left, Modifiers.None)) |> ignore
            host.HandleEvent(PointerReleased(Touch, 1, at 13, MouseButton.Left, Modifiers.None)) |> ignore
            Assert.Equal(13, (TextBox.editOf box).Caret)
        // Past the border's fade to the accent.
        for _ in 1..3 do
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 1.0
            host.HandleEvent Frame |> ignore
        // The app and the box's own layer of handles.
        Assert.Equal(2, host.Layers.Count)
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, scale, fun f ->
                f.Clear host.ClearColor
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``a word selected by a long press, dark``() = this.Shoot(Theme.dark [], 1.0, true, "textbox-touch@1x")

    [<Fact>]
    member this.``a word selected by a long press, light``() = this.Shoot(Theme.light [], 1.0, true, "textbox-touch-light@1x")

    [<Fact>]
    member this.``a word selected by a long press, at 2x``() = this.Shoot(Theme.dark [], 2.0, true, "textbox-touch@2x")

    [<Fact>]
    member this.``the caret's handle after a tap``() = this.Shoot(Theme.dark [], 1.0, false, "textbox-touch-caret@1x")
