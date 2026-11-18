/// M6 step 2 on the GPU: the inspector open over the Gallery, after picking its first "+" button.
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
        member _.MaxTextureSize = device.MaxTextureSize
        member _.CreateSurface(_, size) =
            { new IRenderSurface with
                member _.Size = size
                member _.Resize _ = ()
                member _.TryBeginFrame _ = None
                member _.Dispose() = () }
        member _.RenderOffscreenAsync(size, scale, draw) = device.RenderOffscreenAsync(size, scale, draw)
        member _.Dispose() = ()

/// With the other classes that use the process's image cache: run one after another, they can't find a picture another
/// class's thread has only begun to decode (seen once the test run grew: the Images page drawn with empty boxes).
[<Collection("Shared image cache")>]
type InspectorTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    [<Fact>]
    member _.``the inspector after picking a button``() =
        let pixels: PixelSize = { Width = 900; Height = 520 }
        let size = Size.Create(900.0, 520.0)
        let host = ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text []
        host.Update(Scribble.Samples.GalleryApp.gallery ())
        Inspector.attach (host, []) |> ignore
        let layout () =
            for _ in 1..3 do
                for l in host.Layers do
                    l.Tree.UpdateLayout size
                    l.Tree.RunAfterFrame() // effects and subscriptions start after a frame
        let send e = host.HandleEvent e |> ignore
        layout ()
        // Pick the first "+" button (the Components page's Elmish counter, second button in its row).
        Assert.Equal(3, host.Layers.Count)
        Assert.True(host.HandleEvent(KeyDown(Key.Char 'c', Modifiers.Control ||| Modifiers.Shift, false)), "key")
        layout ()
        Assert.True(host.Layers[2].Tree.HasVisibleContent, "panel")
        Assert.True(host.Layers[1].Tree.HasVisibleContent, "highlight")
        let rec all (e: Element) : Element seq = seq { yield e; for c in e.Children do yield! all c }
        let plus =
            all host.Tree.Root.Value
            |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Button.Type) && all e |> Seq.exists (fun t -> obj.ReferenceEquals(t.Type, TextBlock.Type) && t.Get TextProps.text = "+"))
        let p = plus.PositionInTree
        let at: PixelPoint = { X = p.X + plus.Bounds.Width / 2.0; Y = p.Y + plus.Bounds.Height / 2.0 }
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

    /// M8 step 3b (f) 7: an ImageView picked, its IMAGES section with the thumbnail.
    [<Fact>]
    member _.``the inspector's IMAGES section``() =
        ImageCache.DecodeInlineOnThisThread <- true
        // Custom states are process-wide; the Gallery's ("chosen") show in STATES whichever test ran first.
        Scribble.Samples.Gallery.StylesPage.view () |> ignore
        let pixels: PixelSize = { Width = 900; Height = 720 }
        let size = Size.Create(900.0, 720.0)
        let host = ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text []
        // A 96 × 64 gradient with a ring, at density 2 (48 × 32 DIPs).
        let w, h = 96, 64
        let rgba = Array.zeroCreate<byte> (w * h * 4)
        for y in 0 .. h - 1 do
            for x in 0 .. w - 1 do
                let i = (y * w + x) * 4
                let d = sqrt ((float x - 48.0) ** 2.0 + (float y - 32.0) ** 2.0)
                let ring = d > 18.0 && d < 24.0
                rgba[i] <- if ring then 250uy else byte (60 + x * 2)
                rgba[i + 1] <- if ring then 240uy else byte (40 + y * 2)
                rgba[i + 2] <- if ring then 200uy else 170uy
                rgba[i + 3] <- 255uy
        let image = Image.fromPixels ("inspector-images", w, h, rgba) [ Image.density 2.0 ]
        host.Update(
            Border.create [
                Border.padding 40.0
                Border.content (
                    ImageView.create [
                        ImageView.image image
                        ImageView.horizontalAlignment HorizontalAlignment.Left
                        ImageView.verticalAlignment VerticalAlignment.Top
                    ]
                )
            ]
        )
        Inspector.attach (host, []) |> ignore
        let layout () =
            for _ in 1..3 do
                for l in host.Layers do
                    l.Tree.UpdateLayout size
                    l.Tree.RunAfterFrame()
        let send e = host.HandleEvent e |> ignore
        layout ()
        Assert.True(host.HandleEvent(KeyDown(Key.Char 'c', Modifiers.Control ||| Modifiers.Shift, false)), "key")
        layout ()
        let at: PixelPoint = { X = 60.0; Y = 50.0 }
        send (PointerMoved(Mouse, 0, at, Modifiers.None))
        send (PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        send (PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        layout ()
        // The properties scrolled to their end, where IMAGES is (then layout again: scrolling animates).
        send (PointerMoved(Mouse, 0, { X = 700.0; Y = 620.0 }, Modifiers.None))
        send (PointerWheel({ X = 700.0; Y = 620.0 }, 0.0, -20.0, Modifiers.None))
        for _ in 1..40 do
            for l in host.Layers do
                l.Tree.Time <- l.Tree.Time + TimeSpan.FromMilliseconds 16.0
            layout ()
        let shot =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                for l in host.Layers do
                    l.Tree.Render f.DrawList)
        match Snapshot.check "inspector-images@1x" shot with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``a floating inspector, dragged over the app``() =
        let pixels: PixelSize = { Width = 900; Height = 520 }
        let size = Size.Create(900.0, 520.0)
        let host = ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text []
        host.Update(Scribble.Samples.GalleryApp.gallery ())
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

    /// The highlight on an element partly scrolled out of a ScrollViewer: only the part that shows
    /// (its margin above the viewport isn't drawn over the header), a dashed line on the cut edge,
    /// the label inside; then the same element selected (corner marks only at the corners that show).
    [<Fact>]
    member _.``the highlight on an element partly scrolled out``() =
        let pixels: PixelSize = { Width = 900; Height = 520 }
        let size = Size.Create(900.0, 520.0)
        let host = ElementHost.create (new StillWindow(pixels)) (new OffscreenOnly(fixture.Gpu.Device)) fixture.Text []
        let scroller = Scroller()
        let rowColors = [| 0x3B4261; 0x2F3549; 0x3B4261; 0x2F3549; 0x3B4261; 0x2F3549 |]
        host.Update(
            StackPanel.create [
                StackPanel.children [
                    Border.create [ Border.height 60.0; Border.background (Color.Hex 0x7AA2F7) ]
                    ScrollViewer.create [
                        ScrollViewer.height 240.0
                        ScrollViewer.scroller scroller
                        ScrollViewer.content (
                            StackPanel.create [
                                StackPanel.children [
                                    for c in rowColors ->
                                        Border.create [ Border.height 80.0; Border.margin 8.0; Border.background (Color.Hex c) ]
                                ]
                            ]
                        )
                    ]
                    Border.create [ Border.height 60.0; Border.background (Color.Hex 0x9ECE6A) ]
                ]
            ])
        Inspector.attach (host, []) |> ignore
        let layout () =
            for _ in 1..3 do
                for l in host.Layers do
                    l.Tree.UpdateLayout size
                    l.Tree.RunAfterFrame()
        let send e = host.HandleEvent e |> ignore
        // The app and the highlight only: the panel lists custom states, which other tests in the
        // run register (process-wide), so it would depend on test order.
        let render () =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                for l in host.Layers do
                    if l.Name <> "Inspector" then l.Tree.Render f.DrawList)
        layout ()
        // Row 0 (8..88 in the content) scrolled up 50: 18..98 in the window, under the header to 60.
        scroller.ScrollToOffset(50.0, false)
        layout ()
        Assert.True(host.HandleEvent(KeyDown(Key.Char 'c', Modifiers.Control ||| Modifiers.Shift, false)), "key")
        layout ()
        let at: PixelPoint = { X = 200.0; Y = 80.0 }
        send (PointerMoved(Mouse, 0, at, Modifiers.None))
        layout ()
        match Snapshot.check "inspector-scrolled-hover@1x" (render ()) with
        | Some error -> Assert.Fail error
        | None -> ()
        send (PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        send (PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
        // Out over the panel, so nothing in the app is pointed at: the selection's outline shows.
        send (PointerMoved(Mouse, 0, { X = 880.0; Y = 500.0 }, Modifiers.None))
        layout ()
        match Snapshot.check "inspector-scrolled-selected@1x" (render ()) with
        | Some error -> Assert.Fail error
        | None -> ()
