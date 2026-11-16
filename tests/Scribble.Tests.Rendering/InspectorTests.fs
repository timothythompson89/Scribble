/// M6 step 2 on the GPU: the inspector open over the Counter demo, after picking its "+" button.
module Scribble.Tests.Rendering.InspectorTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Inspector
open Scribble.Tests.Rendering.TextRenderingTests

/// A window that is never shown: events are sent to the host by hand.
type private StillWindow(size: PixelSize) =
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

/// The real device for text and offscreen pictures, with a surface that never draws (the test
/// renders the layers itself).
type private OffscreenOnly(device: IRenderDevice) =
    interface IRenderDevice with
        member _.Description = device.Description
        member _.GlyphAtlas = device.GlyphAtlas
        member _.ColorGlyphAtlas = device.ColorGlyphAtlas
        member _.CreateSurface(_, size) =
            { new IRenderSurface with
                member _.Size = size
                member _.Resize _ = ()
                member _.TryBeginFrame _ = None
                member _.Dispose() = () }
        member _.RenderOffscreen(size, scale, draw) = device.RenderOffscreen(size, scale, draw)
        member _.Dispose() = ()

type InspectorTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    [<Fact>]
    member _.``the inspector after picking a button``() =
        let pixels: PixelSize = { Width = 900; Height = 520 }
        let size = Size.Create(900.0, 520.0)
        let host = ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text []
        host.Update(Scribble.Samples.Counter.demo ())
        Inspector.attach (host, []) |> ignore
        let layout () =
            for _ in 1..3 do
                for l in host.Layers do
                    l.Tree.UpdateLayout size
                    l.Tree.RunAfterFrame() // effects and subscriptions start after a frame
        let send e = host.HandleEvent e |> ignore
        layout ()
        // Pick the "+" button (Elmish tab, second button in the row).
        Assert.Equal(3, host.Layers.Count)
        Assert.True(host.HandleEvent(KeyDown(Key.Char 'c', Modifiers.Control ||| Modifiers.Shift, false)), "key")
        layout ()
        Assert.True(host.Layers[2].Tree.HasVisibleContent, "panel")
        Assert.True(host.Layers[1].Tree.HasVisibleContent, "highlight")
        let at: PixelPoint = { X = 92.0; Y = 172.0 }
        send (PointerMoved(Mouse, 0, at, Modifiers.None))
        send (PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        send (PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        layout ()
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        match Snapshot.check "inspector-pick@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``a floating inspector, dragged over the app``() =
        let pixels: PixelSize = { Width = 900; Height = 520 }
        let size = Size.Create(900.0, 520.0)
        let host = ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text []
        host.Update(Scribble.Samples.Counter.demo ())
        Inspector.attach (host, [ Inspector.isOpen true ]) |> ignore
        let layout () =
            for _ in 1..3 do
                for l in host.Layers do
                    l.Tree.UpdateLayout size
                    l.Tree.RunAfterFrame()
        let send e = host.HandleEvent e |> ignore
        let at x y : PixelPoint = { X = x; Y = y }
        layout ()
        // Drag the title from the docked panel to the middle of the window.
        send (PointerMoved(Mouse, 0, at 540.0 25.0, Modifiers.None))
        send (PointerPressed(Mouse, 0, at 540.0 25.0, MouseButton.Left, Modifiers.None))
        layout ()
        send (PointerMoved(Mouse, 0, at 400.0 120.0, Modifiers.None))
        send (PointerMoved(Mouse, 0, at 300.0 160.0, Modifiers.None))
        send (PointerReleased(Mouse, 0, at 300.0 160.0, MouseButton.Left, Modifiers.None))
        layout ()
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        match Snapshot.check "inspector-floating@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
