/// M8 step 4a on the GPU: an open flyout (the theme's Flyout look: surface, line, corners, floating shadow) and a
/// tooltip, each in its own layer over the app.
module Scribble.Tests.Rendering.FlyoutTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests

/// A window that is never shown: events are sent to the host by hand.
type StillWindow(size: PixelSize) =
    interface IWindow with
        member _.ClientSize = size
        member _.Scale = 1.0
        member _.Title with get () = "" and set _ = ()
        member _.Surface = BrowserCanvas "test"
        member _.IsClosed = false
        member _.Show() = ()
        member _.Close() = ()
        member _.RequestFrame() = ()
        member _.SetCursor _ = ()
        member _.ClaimKey(_, _) = ()
        member _.Dispose() = ()

/// The real device for text and offscreen pictures, with a surface that never draws (the test renders the layers).
type OffscreenOnly(device: IRenderDevice) =
    interface IRenderDevice with
        member _.Description = device.Description
        member _.GlyphAtlas = device.GlyphAtlas
        member _.ColorGlyphAtlas = device.ColorGlyphAtlas
        member _.MaxTextureSize = device.MaxTextureSize
        member _.CreateSurface(_, size) =
            { new IRenderSurface with
                member _.Size = size
                member _.Resize _ = ()
                member _.TryBeginFrame _ = None
                member _.Dispose() = () }
        member _.RenderOffscreenAsync(size, scale, draw) = device.RenderOffscreenAsync(size, scale, draw)
        member _.Dispose() = ()

let private menuItem (text: string) =
    Button.create [
        Button.background Color.Transparent
        Button.borderThickness 0.0
        Setting.create BorderProps.shadow ValueNone
        Button.padding (10.0, 6.0)
        Button.content (TextBlock.create [ TextBlock.text text; TextBlock.horizontalAlignment HorizontalAlignment.Left ])
    ]

let private appView =
    StackPanel.create [
        StackPanel.margin 16.0
        StackPanel.spacing 8.0
        StackPanel.orientation Horizontal
        StackPanel.verticalAlignment VerticalAlignment.Top
        StackPanel.children [
            Button.create [
                Button.name "actions"
                Button.text "Actions"
                Button.flyout (
                    StackPanel.create [
                        StackPanel.width 160.0
                        StackPanel.children [
                            menuItem "Copy"
                            menuItem "Duplicate"
                            Separator.create [ Separator.margin (0.0, 4.0) ]
                            menuItem "Delete"
                        ]
                    ]
                )
            ]
            Button.create [ Button.name "save"; Button.text "Save"; Button.tooltip "Save the document (Ctrl+S)" ]
        ]
    ]

type FlyoutTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Host(pixels: PixelSize, timers: List<unit -> unit>) =
        let dispatcher = Dispatcher(ignore, fun _ action -> timers.Add action)
        let host =
            ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text [ ElementHost.dispatcher dispatcher ]
        host.Update appView
        host.HandleEvent Frame |> ignore
        host

    member private _.Render(host: ElementHost, pixels: PixelSize) =
        // Past any hover or press fade.
        for _ in 1..3 do
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromSeconds 1.0
            host.HandleEvent Frame |> ignore
        fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
            f.Clear host.ClearColor
            for l in host.Layers do
                l.Tree.Render f.DrawList)

    [<Fact>]
    member this.``an open flyout``() =
        let pixels: PixelSize = { Width = 320; Height = 220 }
        let host = this.Host(pixels, List())
        let actions = host.Tree.Root.Value.Children[0]
        let p = actions.PositionInTree
        // By keyboard: the first item gets focus (and the ring).
        host.MoveFocus FocusDirection.Next
        host.HandleEvent(KeyDown(Key.Enter, Modifiers.None, false)) |> ignore
        host.HandleEvent Frame |> ignore
        Assert.Equal(2, host.Layers.Count)
        Assert.True(p.Y >= 0.0)
        let image = this.Render(host, pixels)
        match Snapshot.check "flyout-open@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``a tooltip``() =
        let pixels: PixelSize = { Width = 320; Height = 120 }
        let timers = List<unit -> unit>()
        let host = this.Host(pixels, timers)
        let save = host.Tree.Root.Value.Children[1]
        let p = save.PositionInTree
        let at: PixelPoint = { X = p.X + 20.0; Y = p.Y + 12.0 }
        host.HandleEvent(PointerMoved(Mouse, 0, at, Modifiers.None)) |> ignore
        for t in timers.ToArray() do
            t ()
        Assert.Equal("Tooltip", (Seq.last host.Layers).Name)
        let image = this.Render(host, pixels)
        match Snapshot.check "tooltip@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
