/// Rendering to an image (DESIGN.md, "Rendering to an image and saving it"): `ViewImage` draws exactly what the
/// screenshot tests draw, `Capture` draws an element as it is on screen (whole when scrolled half out, hovered),
/// transparency and margins, and the size limit.
module Scribble.Tests.Rendering.ImageOutTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Samples.Gallery

/// An image's straight pixels (rendered images keep them in the Image).
let private straight (image: Image) =
    match image.Source with
    | ImageSource.Pixels(w, h, px) -> w, h, px
    | _ -> failwith "not a rendered image"

/// A rendered image as a Bitmap through its PNG (what a saved file holds).
let private throughPng (image: Image) : Bitmap = Png.decode (Image.toPng image).Result

/// With the other classes that use the process's image cache: run one after another, they can't find a picture another
/// class's thread has only begun to decode (seen once the test run grew: the Images page drawn with empty boxes).
[<Collection("Shared image cache")>]
type ImageOutTests(fixture: TextFixture) =
    // This class is the only one in the rendering tests that renders through ViewImage or Capture, and its tests run
    // one at a time, so the process-wide environment is this fixture's while each runs.
    let services =
        RenderServices.Use
            { Device = fixture.Gpu.Device
              Text = fixture.Text
              Platform = ValueNone
              Window = ValueNone
              Dispatcher = ValueNone
              AppValues = ValueNone
              Images = ValueSome(ImageCache.Create()) }

    interface IClassFixture<TextFixture>

    interface IDisposable with
        member _.Dispose() = services.Dispose()

    /// The same pixels as `GalleryTests`' "the counter" (decision 8): ViewImage lays out and draws as the tests do.
    [<Fact>]
    member _.``a view image of the counter matches its screenshot``() =
        let ignoreMsg (_: Scribble.Samples.Counter.Msg) = ()
        let task =
            ViewImage.render (Scribble.Samples.Counter.view 3 ignoreMsg) [
                ViewImage.size (360.0, 200.0)
                ViewImage.theme Scribble.Samples.GalleryApp.darkTheme
                ViewImage.background (Color.Hex 0x181825)
            ]
        // Headless on the desktop: finished at once.
        Assert.True(task.IsCompleted)
        match Snapshot.check "counter@1x" (throughPng task.Result) with
        | Some error -> Assert.Fail error
        | None -> ()

    /// The light Gallery, with the theme's background (the default).
    [<Fact>]
    member _.``a view image of the light gallery matches its screenshot``() =
        let image =
            (ViewImage.render (Scribble.Samples.GalleryApp.gallery ()) [ ViewImage.size (720.0, 560.0); ViewImage.theme Scribble.Samples.GalleryApp.lightTheme ]).Result
        Assert.Equal(1.0, image.Density)
        match Snapshot.check "gallery-light@1x" (throughPng image) with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``a view image takes the view's own size and the scale``() =
        let view = Border.create [ Border.width 30.0; Border.height 20.0; Border.background Color.White ]
        let image = (ViewImage.render view [ ViewImage.scale 2.0 ]).Result
        let w, h, px = straight image
        Assert.Equal((60, 40), (w, h))
        Assert.Equal(2.0, image.Density)
        Assert.Equal(255uy, px[0])

    [<Fact>]
    member _.``a view image keeps transparency, through its PNG too``() =
        let view =
            Border.create [ Border.width 40.0; Border.height 40.0; Border.cornerRadius 20.0; Border.background (Color.Hex 0x3366CC) ]
        let image = (ViewImage.render view [ ViewImage.background Color.Transparent ]).Result
        let w, _, px = straight image
        let alpha x y = px[(y * w + x) * 4 + 3]
        Assert.Equal(0uy, alpha 0 0) // outside the circle
        Assert.Equal(255uy, alpha 20 20) // its middle
        // Straight colour in the middle, and every pixel the same after a round trip through the file.
        Assert.Equal((0x33uy, 0x66uy, 0xCCuy), (px[(20 * w + 20) * 4], px[(20 * w + 20) * 4 + 1], px[(20 * w + 20) * 4 + 2]))
        Assert.Equal<byte[]>(px, (throughPng image).Pixels)

    [<Fact>]
    member _.``a picture bigger than the GPU takes fails, naming the limit``() =
        let task = ViewImage.render (Border.create []) [ ViewImage.size (9000.0, 10.0) ]
        let e = Assert.ThrowsAny<exn>(fun () -> task.Wait())
        Assert.Contains("8192", e.InnerException.Message)

    /// Decision 5: the element's own box, as on screen (hovered), whole when scrolled half out of its ScrollViewer.
    [<Fact>]
    member _.``a capture draws its element whole and hovered, scrolled or not``() =
        let capture = Capture()
        let scroller = Scroller()
        let card =
            Border.create [
                Border.capture capture
                Border.width 120.0
                Border.height 80.0
                Border.horizontalAlignment HorizontalAlignment.Left
                Border.cornerRadius 12.0
                // Its colour only while hovered (an element's own value would win over the block).
                Border.onState States.hover [ Border.background (Color.Hex 0xCC6633) ]
                Border.content (TextBlock.create [ TextBlock.text "Card"; TextBlock.margin 8.0 ])
            ]
        let view =
            ScrollViewer.create [
                ScrollViewer.scroller scroller
                ScrollViewer.content (
                    StackPanel.create [
                        StackPanel.children [
                            Border.create [ Border.height 40.0 ]
                            Border.create [ Border.margin (20.0, 0.0); Border.content card ]
                            Border.create [ Border.height 400.0 ]
                        ]
                    ]
                )
            ]
        let tree = ElementTree(fixture.Text)
        // No theme: its colour transitions would hold the hover colour at its start (the clock doesn't move here).
        tree.Update view
        let size = Size.Create(300.0, 200.0)
        tree.UpdateLayout size
        let rec find (e: Element) : Element option =
            if obj.ReferenceEquals(e.GetBoxed CaptureProps.capture, capture) then Some e
            else e.Children |> Seq.tryPick find
        let element = (find tree.Root.Value).Value
        element.ChangePointersOver 1
        tree.UpdateLayout size
        // The whole window, transparent where nothing draws, cut to the card.
        let full =
            fixture.Gpu.Device.RenderOffscreen({ Width = 300; Height = 200 }, 1.0, fun f ->
                f.Clear Color.Transparent
                tree.Render f.DrawList)
        let at = element.PositionInTree
        let crop =
            { Size = { Width = 120; Height = 80 }
              Pixels =
                Array.init (120 * 80 * 4) (fun i ->
                    let p = i / 4
                    full.Pixels[((int at.Y + p / 120) * 300 + int at.X + p % 120) * 4 + i % 4]) }
        let _, _, expected = straight (RenderedImage.ofBitmap crop 1.0)
        let shown = (capture.ToImage []).Result
        let w, h, px = straight shown
        Assert.Equal((120, 80), (w, h))
        Assert.Equal<byte[]>(expected, px)
        // Hovered: the hover colour, not the plain one.
        let c = (px[(40 * 120 + 100) * 4], px[(40 * 120 + 100) * 4 + 1], px[(40 * 120 + 100) * 4 + 2])
        Assert.True((c = (0xCCuy, 0x66uy, 0x33uy)), $"hover colour: {c}")
        // Scrolled so the card is half out of view: the capture is still the whole card.
        scroller.ScrollToOffset(80.0, false)
        tree.UpdateLayout size
        Assert.True(element.VisibleBounds.Height < 80.0)
        let _, _, scrolled = straight (capture.ToImage []).Result
        Assert.Equal<byte[]>(expected, scrolled)
        tree.Root <- ValueNone

    [<Fact>]
    member _.``a capture's margin makes room for the shadow, and its scale sharpens it``() =
        let capture = Capture()
        let view =
            Border.create [
                Border.padding 30.0
                Border.content (
                    Border.create [
                        Border.capture capture
                        Border.width 50.0
                        Border.height 30.0
                        Border.background Color.White
                        Border.shadow { Blur = 8.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black }
                    ]
                )
            ]
        let tree = ElementTree(fixture.Text)
        tree.Update view
        tree.UpdateLayout(Size.Create(200.0, 200.0))
        let w, h, px = straight (capture.ToImage [ Capture.margin 16.0 ]).Result
        Assert.Equal((82, 62), (w, h))
        // Shadow below the box, inside the margin.
        Assert.True(px[((16 + 30 + 4) * w + 41) * 4 + 3] > 0uy)
        let tight = (capture.ToImage []).Result
        Assert.Equal((50, 30), (let w, h, _ = straight tight in w, h))
        let sharp = (capture.ToImage [ Capture.scale 2.0 ]).Result
        Assert.Equal(((100, 60), 2.0), ((let w, h, _ = straight sharp in w, h), sharp.Density))
        tree.Root <- ValueNone

    [<Fact>]
    member _.``a capture not on a showing element fails with why``() =
        let capture = Capture()
        let e = Assert.ThrowsAny<exn>(fun () -> (capture.ToImage []).Wait())
        Assert.Contains("isn't on an element", e.InnerException.Message)
        let tree = ElementTree(fixture.Text)
        tree.Update(Border.create [ Border.capture capture; Border.width 10.0; Border.height 10.0 ])
        tree.UpdateLayout(Size.Create(50.0, 50.0))
        Assert.True((capture.ToImage []).Wait(1000))
        tree.Root <- ValueNone
        let e = Assert.ThrowsAny<exn>(fun () -> (capture.ToImage []).Wait())
        Assert.Contains("left the screen", e.InnerException.Message)

    /// The Gallery's "Render the Gallery" in a running app (a host with a dispatcher and the app values, the real
    /// device, pictures decoded off the UI thread): the whole Gallery, rendered fresh at 2×, fills the frame that
    /// was already there, and the page doesn't grow.
    [<Fact>]
    member _.``the Gallery's Render the Gallery shows the Gallery``() =
        let timers = Collections.Generic.List<unit -> unit>()
        let dispatcher = Dispatcher(ignore, fun _ action -> timers.Add action)
        let values = AppValues()
        values.Set(Common.wholeGallery, ValueSome(fun () -> Scribble.Samples.GalleryApp.gallery ()))
        let pixels: PixelSize = { Width = 720; Height = 2400 }
        let host =
            ElementHost.create (new FlyoutTests.StillWindow(pixels)) (new FlyoutTests.OffscreenOnly(fixture.Gpu.Device)) fixture.Text [
                ElementHost.dispatcher dispatcher
                ElementHost.appValues values
            ]
        use _app =
            RenderServices.Use
                { Device = fixture.Gpu.Device
                  Text = fixture.Text
                  Platform = ValueNone
                  Window = ValueNone
                  Dispatcher = ValueSome dispatcher
                  AppValues = ValueSome values
                  Images = ValueNone }
        values.Images.DecodeInline <- false
        host.Update(ImagesPage.view ())
        host.HandleEvent Frame |> ignore
        let rec all (e: Element) = seq { yield e; for c in e.Children do yield! all c }
        let bottom (r: Element) = all r |> Seq.map (fun e -> e.PositionInTree.Y + e.Bounds.Height) |> Seq.max
        let bottomBefore = bottom host.Tree.Root.Value
        let label = all host.Tree.Root.Value |> Seq.find (fun e -> e.GetBoxed TextProps.text = box "Render the Gallery")
        let p = label.PositionInTree
        let at: PixelPoint = { X = p.X + 5.0; Y = p.Y + 5.0 }
        host.HandleEvent(PointerMoved(Mouse, 0, at, Modifiers.None)) |> ignore
        host.HandleEvent(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
        host.HandleEvent(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None)) |> ignore
        let shown () =
            all host.Tree.Root.Value
            |> Seq.tryFind (fun e ->
                match e.GetBoxed ImageViewProps.image with
                | :? (Image voption) as i -> i.IsSome && i.Value.Key.StartsWith "render:" && e.Bounds.Width > 0.0
                | _ -> false)
        let mutable tries = 0
        while tries < 300 && (shown ()).IsNone do
            tries <- tries + 1
            Threading.Thread.Sleep 10
            dispatcher.RunPending()
            host.HandleEvent Frame |> ignore
        let view = (shown ()).Value
        let image = (view.GetBoxed ImageViewProps.image :?> Image voption).Value
        let w, h, px = straight image
        Assert.Equal(((1440, 1120), 2.0), ((w, h), image.Density))
        // The dark theme's background, opaque (a window's picture).
        Assert.Equal(255uy, px[3])
        // Half size in its frame.
        Assert.Equal(360.0, view.Bounds.Width, 3)
        Assert.Equal(bottomBefore, bottom host.Tree.Root.Value)
        // Saved for a look: bin/snapshots/render-the-gallery.png.
        let shot =
            fixture.Gpu.Device.RenderOffscreen({ Width = 720; Height = 560 }, 1.0, fun f ->
                f.Clear host.ClearColor
                f.DrawList.PushTranslate(0.0, -(view.PositionInTree.Y - 200.0))
                for l in host.Layers do
                    l.Tree.Render f.DrawList
                f.DrawList.PopTransform())
        IO.Directory.CreateDirectory(Snapshot.outputDir ()) |> ignore
        IO.File.WriteAllBytes(IO.Path.Combine(Snapshot.outputDir (), "render-the-gallery.png"), Png.encode shot)
        host.Tree.Root <- ValueNone
