/// M8 step 4d on the GPU: an open Dialog (the scrim over the app, the box with its title, focus on its first
/// button) with two toasts above it (one with an action, one an error), in the dark and light themes.
module Scribble.Tests.Rendering.DialogToastTests

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
        StackPanel.spacing 10.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text "The app, under the scrim"; TextBlock.fontSize 18.0 ]
            Button.create [ Button.text "A button below"; Button.horizontalAlignment HorizontalAlignment.Left ]
            Modal.create [
                Modal.isOpen true
                Modal.title "Delete the draft?"
                Modal.content (
                    StackPanel.create [
                        StackPanel.spacing 16.0
                        StackPanel.children [
                            TextBlock.create [ TextBlock.text "It can't be brought back afterwards."; TextBlock.foreground Palette.textDim ]
                            StackPanel.create [
                                StackPanel.orientation Orientation.Horizontal
                                StackPanel.spacing 8.0
                                StackPanel.horizontalAlignment HorizontalAlignment.Right
                                StackPanel.children [ Button.create [ Button.text "Keep" ]; Button.create [ Button.text "Delete" ] ]
                            ]
                        ]
                    ])
            ]
        ]
    ]

type DialogToastTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Shoot(theme: Theme, name: string) =
        let pixels: PixelSize = { Width = 560; Height = 360 }
        let dispatcher = Dispatcher(ignore, fun _ _ -> ())
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme theme
            ]
        let toaster = host.Toaster
        // Opened by keyboard, so focus in the dialog shows its ring.
        host.MoveFocus FocusDirection.Next
        host.Update view
        host.HandleEvent Frame |> ignore
        toaster.Show [ Toast.text "Message deleted"; Toast.action ("Undo", ignore) ]
        toaster.Show [ Toast.severity Severity.Critical; Toast.text "Couldn't reach the server" ]
        // What was posted, then past the fades and slides.
        for _ in 1..4 do
            dispatcher.RunPending()
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 1.0
            host.HandleEvent Frame |> ignore
        Assert.Equal<string list>([ "App"; "Modal"; "Toasts" ], host.Layers |> Seq.map _.Name |> List.ofSeq)
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear host.ClearColor
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    /// A confirmation asked for through `Dialogs`, opened by the keyboard so the focused button shows its ring.
    member private _.Confirm(severity: Severity, name: string) =
        let pixels: PixelSize = { Width = 420; Height = 220 }
        let dispatcher = Dispatcher(ignore, fun _ _ -> ())
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme (Theme.dark [])
            ]
        let dialogs = host.Dialogs
        host.Update(Button.create [ Button.text "The app"; Button.horizontalAlignment HorizontalAlignment.Left; Button.verticalAlignment VerticalAlignment.Top ])
        host.HandleEvent Frame |> ignore
        host.MoveFocus FocusDirection.Next
        dialogs.Confirm [ Confirm.title "Delete the draft?"; Confirm.text "It can't be brought back."; Confirm.accept "Delete"; Confirm.cancel "Keep"; Confirm.severity severity ]
        |> ignore
        for _ in 1..4 do
            dispatcher.RunPending()
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 1.0
            host.HandleEvent Frame |> ignore
        Assert.Contains("Modal", host.Layers |> Seq.map _.Name)
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear host.ClearColor
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        match Snapshot.check name image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``a Critical confirmation: the danger button, focus on cancel``() = this.Confirm(Severity.Critical, "confirm-critical@1x")

    [<Fact>]
    member this.``a Warning confirmation: the warning button, focus on it``() = this.Confirm(Severity.Warning, "confirm-warning@1x")

    [<Fact>]
    member this.``a dialog with toasts above it, dark``() = this.Shoot(Theme.dark [], "dialog-toasts@1x")

    [<Fact>]
    member this.``a dialog with toasts above it, light``() = this.Shoot(Theme.light [], "dialog-toasts-light@1x")
