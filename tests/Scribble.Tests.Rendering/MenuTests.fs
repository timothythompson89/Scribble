/// M9 step 9f on the GPU: a Menu open as a Button's flyout by the keyboard (the first item under the keyboard, one
/// under the pointer, a checked and an unchecked item, a disabled one, shortcut texts and a separator), in the dark
/// and light themes.
module Scribble.Tests.Rendering.MenuTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Tests.Rendering.FlyoutTests

let private view =
    StackPanel.create [
        StackPanel.margin 16.0
        StackPanel.orientation Orientation.Horizontal
        StackPanel.verticalAlignment VerticalAlignment.Top
        StackPanel.children [
            Button.create [
                Button.text "Edit"
                Button.flyout (
                    Menu.create [
                        Menu.children [
                            MenuItem.create [ MenuItem.text "Undo"; MenuItem.shortcut "Ctrl+Z" ]
                            MenuItem.create [ MenuItem.text "Redo"; MenuItem.shortcut "Ctrl+Y"; MenuItem.isEnabled false ]
                            Menu.separator
                            MenuItem.create [ MenuItem.name "copy"; MenuItem.text "Copy"; MenuItem.shortcut "Ctrl+C" ]
                            MenuItem.create [ MenuItem.text "Select all"; MenuItem.shortcut "Ctrl+A" ]
                            Menu.separator
                            MenuItem.create [ MenuItem.text "Word wrap"; MenuItem.isChecked true ]
                            MenuItem.create [ MenuItem.text "Line numbers"; MenuItem.isChecked false ]
                        ]
                    ]
                )
            ]
        ]
    ]

let rec private find (name: string) (e: Element) : Element option =
    if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)

type MenuTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Shoot(theme: Theme, name: string) =
        let pixels: PixelSize = { Width = 300; Height = 300 }
        let dispatcher = Dispatcher(ignore, fun _ _ -> ())
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.theme theme
            ]
        host.Update view
        host.HandleEvent Frame |> ignore
        // By keyboard: Enter on the button opens the menu, and the first item is the one the keyboard is on.
        host.MoveFocus FocusDirection.Next
        host.HandleEvent(KeyDown(Key.Enter, Modifiers.None, false)) |> ignore
        host.HandleEvent(KeyUp(Key.Enter, Modifiers.None)) |> ignore
        host.HandleEvent Frame |> ignore
        Assert.Equal(2, host.Layers.Count)
        (find "copy" (Seq.last host.Layers).Tree.Root.Value).Value.ChangePointersOver 1
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
    member this.``an open menu, dark``() = this.Shoot(Theme.dark [], "menu@1x")

    [<Fact>]
    member this.``an open menu, light``() = this.Shoot(Theme.light [], "menu-light@1x")
