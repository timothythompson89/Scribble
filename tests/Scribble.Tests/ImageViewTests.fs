/// M8 step 3b (d): the Image value, the process-wide cache and its pool, elements holding what they show, ImageView's
/// measure and fitting, failures reported once per image, and decoding only what's realized in a long list
/// (DESIGN.md, "M8 step 3b", (d)).
module Scribble.Tests.ImageViewTests

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Threading
open Xunit
open Scribble.Rendering
open Scribble.Imaging
open Scribble.Core
open Scribble.Tests.TestShaping

/// A 32 × 32 RGBA PNG from PngSuite.
let private pngBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PngSuite", "basn6a08.png"))

/// Keys unique to one test run (the cache is process-wide).
let private unique (name: string) = name + "-" + Guid.NewGuid().ToString("N")

/// Straight RGBA, `w × h`, every pixel `r, g, b, a`.
let private solid (w: int) (h: int) (r: byte, g: byte, b: byte, a: byte) =
    Array.init (w * h * 4) (fun i ->
        match i % 4 with
        | 0 -> r
        | 1 -> g
        | 2 -> b
        | _ -> a)

/// A tree showing `view` at `size`, decoding inline, with a dispatcher whose errors land in `Errors`.
type private Scene(view: View, ?width: float, ?height: float) =
    do ImageCache.DecodeInlineOnThisThread <- true
    let errors = List<exn>()
    let dispatcher = Dispatcher ignore
    do dispatcher.OnError <- errors.Add
    let tree = ElementTree(textSystem ())
    do
        tree.Dispatcher <- ValueSome dispatcher
        tree.Update view
        tree.UpdateLayout(Size.Create(defaultArg width 400.0, defaultArg height 300.0))
    member _.Tree = tree
    member _.Root = tree.Root.Value
    member _.Errors = errors
    member _.Dispatcher = dispatcher
    member _.Update(v: View) =
        tree.Update v
        tree.UpdateLayout(Size.Create(defaultArg width 400.0, defaultArg height 300.0))
    /// Draws a frame and returns its draw list.
    member _.Render() =
        let list = DrawList(1.0)
        tree.Render list
        dispatcher.FrameEnded()
        list

// --- The value ---------------------------------------------------------------------------------

[<Fact>]
let ``images are the same picture by source key; density is part of the value only`` () =
    let a = Image.fromBytes ("k", [| 1uy |]) []
    let b = Image.fromBytes ("k", [| 2uy; 3uy |]) []
    let a2 = Image.fromBytes ("k", [| 1uy |]) [ Image.density 2.0 ]
    Assert.Equal(a, b)
    Assert.Equal(a.Key, a2.Key)
    Assert.NotEqual(a, a2)
    Assert.Equal("bytes:k@2x", string a2)
    let r = Image.fromResource (typeof<Scene>.Assembly, "x.png") []
    Assert.Equal("resource:Scribble.Tests/x.png", r.Key)
    Assert.Throws<ArgumentException>(fun () -> Image.fromPixels ("p", 2, 2, Array.zeroCreate 15) [] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Image.density 0.0 a |> ignore) |> ignore

// --- ImageView ----------------------------------------------------------------------------------

[<Fact>]
let ``an ImageView's own size is its pixels divided by its density`` () =
    let image = Image.fromPixels (unique "px", 40, 20, solid 40 20 (255uy, 0uy, 0uy, 255uy)) [ Image.density 2.0 ]
    let s = Scene(ImageView.create [ ImageView.image image; ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ])
    Assert.Equal(20.0, s.Root.Bounds.Width)
    Assert.Equal(10.0, s.Root.Bounds.Height)
    let list = s.Render()
    Assert.Equal(1, list.Images.Count)
    Assert.Empty s.Errors

[<Fact>]
let ``fromPixels premultiplies straight RGBA`` () =
    let image = Image.fromPixels (unique "half", 1, 1, [| 200uy; 100uy; 50uy; 128uy |]) []
    let s = Scene(ImageView.create [ ImageView.image image ])
    let list = s.Render()
    let d = (list.Images[0]).Image
    Assert.Equal<byte[]>([| 100uy; 50uy; 25uy; 128uy |], d.Pixels)

[<Fact>]
let ``fit places the image by stretch and direction, centred`` () =
    let natural = Size.Create(100.0, 50.0)
    let area = Rect.Create(0.0, 0.0, 400.0, 400.0)
    let fit = ImageView.fit natural area
    Assert.Equal(Rect.Create(150.0, 175.0, 100.0, 50.0), fit ImageStretch.Uniform StretchDirection.DownOnly)
    Assert.Equal(Rect.Create(0.0, 100.0, 400.0, 200.0), fit ImageStretch.Uniform StretchDirection.Both)
    Assert.Equal(Rect.Create(-200.0, 0.0, 800.0, 400.0), fit ImageStretch.UniformToFill StretchDirection.UpOnly)
    Assert.Equal(Rect.Create(0.0, 0.0, 400.0, 400.0), fit ImageStretch.Fill StretchDirection.Both)
    Assert.Equal(Rect.Create(150.0, 175.0, 100.0, 50.0), fit ImageStretch.None StretchDirection.Both)
    let small = Rect.Create(10.0, 10.0, 50.0, 50.0)
    Assert.Equal(Rect.Create(10.0, 22.5, 50.0, 25.0), ImageView.fit natural small ImageStretch.Uniform StretchDirection.DownOnly)
    Assert.Equal(Rect.Create(-15.0, 10.0, 100.0, 50.0), ImageView.fit natural small ImageStretch.UniformToFill StretchDirection.UpOnly)
    Assert.Equal(Rect.Create(-15.0, -15.0, 100.0, 50.0), ImageView.fit natural (Rect.Create(10.0, 0.0, 50.0, 20.0)) ImageStretch.None StretchDirection.Both)

[<Fact>]
let ``UniformToFill draws only the part that shows, with the corner radius`` () =
    let image = Image.fromPixels (unique "wide", 100, 50, solid 100 50 (0uy, 0uy, 255uy, 255uy)) []
    let s =
        Scene(
            ImageView.create [
                ImageView.image image; ImageView.width 50.0; ImageView.height 50.0
                ImageView.stretch ImageStretch.UniformToFill; ImageView.cornerRadius 25.0
                ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top
            ])
    let list = s.Render()
    let draw = list.Images[0]
    Assert.Equal(Rect.Create(25.0, 0.0, 50.0, 50.0), draw.Source)
    Assert.Equal(Rect.Create(0.0, 0.0, 50.0, 50.0), list.InstanceRect draw.Instance)

[<Fact>]
let ``nothing is drawn without an image, and it measures 0 × 0`` () =
    let s = Scene(ImageView.create [ ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ])
    Assert.Equal(0.0, s.Root.Bounds.Width)
    Assert.Equal(0, s.Render().Images.Count)

// --- Holding and the pool -----------------------------------------------------------------------

[<Fact>]
let ``many ImageViews showing one picture decode it once`` () =
    let key = unique "shared"
    let image () = Image.fromBytes (key, pngBytes) []
    let s = Scene(StackPanel.create [ StackPanel.children [ for _ in 1..20 -> ImageView.create [ ImageView.image (image ()) ] ] ])
    let list = s.Render()
    Assert.Equal(20, list.Images.Count)
    Assert.Equal(1, ImageCache.Shared.Decodes("bytes:" + key))
    Assert.Empty s.Errors

[<Fact>]
let ``an element lets go of a picture it no longer shows, and of all of them when it leaves its tree`` () =
    let a = Image.fromPixels (unique "a", 2, 2, solid 2 2 (1uy, 2uy, 3uy, 255uy)) []
    let b = Image.fromPixels (unique "b", 2, 2, solid 2 2 (4uy, 5uy, 6uy, 255uy)) []
    let view (i: Image) = Border.create [ Border.content (ImageView.create [ ImageView.image i ]) ]
    let s = Scene(view a)
    s.Render() |> ignore
    let imageView = s.Root.Children[0]
    Assert.Equal(1, imageView.HeldImageCount)
    s.Update(view b)
    s.Render() |> ignore
    Assert.Equal(1, imageView.HeldImageCount)
    // `a` is unheld and pooled: shown again it isn't made again.
    Assert.True(ImageCache.Shared.Contains a.Key)
    s.Update(view a)
    s.Render() |> ignore
    Assert.Equal(1, ImageCache.Shared.Decodes a.Key)
    s.Update(Border.create [])
    Assert.Equal(0, imageView.HeldImageCount)

[<Fact>]
let ``the pool keeps unheld pictures up to its limit, oldest out first, releasing them`` () =
    ImageCache.DecodeInlineOnThisThread <- true
    // 16 × 16 counts 16·16·16/3 = 1365 bytes; the limit fits two.
    let cache = ImageCache(3000L)
    let make n = Image.fromPixels (unique ("pool" + string n), 16, 16, solid 16 16 (9uy, 9uy, 9uy, 255uy)) []
    let images = [ for n in 1..3 -> make n ]
    let entries = [ for i in images -> cache.Acquire i ]
    let decoded = [ for e in entries -> match cache.StateOf e with ImageState.Ready d -> d | _ -> failwith "not ready" ]
    for e in entries do
        cache.Release e
    Assert.True decoded[0].IsReleased
    Assert.False decoded[1].IsReleased
    Assert.False decoded[2].IsReleased
    Assert.Equal(2, cache.Count)
    // Held again, it leaves the pool and is kept whatever else comes and goes.
    let again = cache.Acquire images[1]
    for n in 4..6 do
        cache.Release(cache.Acquire(make n))
    Assert.False decoded[1].IsReleased
    Assert.True decoded[2].IsReleased
    cache.Release again
    Assert.True(cache.PooledBytes <= cache.PoolLimit)

// --- Failures -----------------------------------------------------------------------------------

[<Fact>]
let ``a broken picture is reported once, however many elements show it and however often`` () =
    let key = unique "broken"
    let broken () = Image.fromBytes (key, [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 1uy; 2uy |]) []
    let s = Scene(StackPanel.create [ StackPanel.children [ for _ in 1..50 -> ImageView.create [ ImageView.image (broken ()) ] ] ])
    for _ in 1..3 do
        Assert.Equal(0, s.Render().Images.Count)
    let e = Assert.Single s.Errors
    let load = Assert.IsType<ImageLoadException>(e)
    Assert.Equal("bytes:" + key, load.Key)

[<Fact>]
let ``a missing resource names what the assembly has`` () =
    let image = Image.fromResource (typeof<Scene>.Assembly, unique "missing.png") []
    let s = Scene(ImageView.create [ ImageView.image image ])
    s.Render() |> ignore
    let e = Assert.Single s.Errors
    Assert.Contains("No resource named", e.Message)
    Assert.Contains("EmbeddedResource", e.Message)

// --- Proportional to what's realized ------------------------------------------------------------

/// A ScrollViewer over 10,000 rows, each showing its own picture (`row-<run>-<i>`).
let private longList (run: string) (scroller: Scroller) =
    ScrollViewer.create [
        ScrollViewer.content (
            ItemsView.create [
                ItemsView.source [| 0..9999 |]
                ItemsView.key id
                ItemsView.view (fun (i: int) ->
                    ImageView.create [
                        ImageView.image (Image.fromBytes (run + "-" + string i, pngBytes) [])
                        ImageView.height 32.0
                    ])
                ItemsView.scroller scroller
            ])
    ]

[<Fact>]
let ``10,000 rows with a picture each decode only the realized rows, also after jumping to 9,000`` () =
    let run = unique "row"
    let scroller = Scroller()
    let s = Scene(longList run scroller, 300.0, 200.0)
    s.Render() |> ignore
    let decoded () = [ for i in 0..9999 -> ImageCache.Shared.Decodes("bytes:" + run + "-" + string i) ] |> List.sum
    let first = decoded ()
    Assert.InRange(first, 6, 40)
    scroller.ScrollToIndex(9000, ScrollAlign.Start, false)
    s.Tree.UpdateLayout(Size.Create(300.0, 200.0))
    s.Render() |> ignore
    Assert.InRange(decoded () - first, 6, 40)
    let between = [ for i in 100..8900 -> ImageCache.Shared.Decodes("bytes:" + run + "-" + string i) ] |> List.sum
    Assert.Equal(0, between)
    Assert.True(ImageCache.Shared.Decodes("bytes:" + run + "-9000") = 1)
    // Rows scrolled away let go: what they showed is pooled, not held.
    Assert.Empty s.Errors

// --- Off the UI thread --------------------------------------------------------------------------

[<Fact>]
let ``decoding runs off the UI thread and the element is drawn when it ends`` () =
    let s = Scene(ImageView.create [ ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ])
    ImageCache.DecodeInlineOnThisThread <- false
    try
        let key = unique "async"
        let invalidated = ref 0
        s.Tree.OnInvalidated <- (fun () -> invalidated.Value <- invalidated.Value + 1)
        s.Update(ImageView.create [ ImageView.image (Image.fromBytes (key, pngBytes) []); ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ])
        // Its size is read from the header at once.
        Assert.Equal(32.0, s.Root.Bounds.Width)
        let first = s.Render()
        let watch = Stopwatch.StartNew()
        let mutable drawn = first.Images.Count
        while drawn = 0 && watch.ElapsedMilliseconds < 5000L do
            Thread.Sleep 5
            s.Dispatcher.RunPending()
            if invalidated.Value > 0 then drawn <- s.Render().Images.Count
        Assert.Equal(1, drawn)
        Assert.True(invalidated.Value >= 1)
        Assert.Empty s.Errors
    finally
        ImageCache.DecodeInlineOnThisThread <- true

// --- Debug warning ------------------------------------------------------------------------------

#if DEBUG
[<Fact>]
let ``a picture over the GPU's limit is reduced, with a debug warning`` () =
    let heard = List<string>()
    let key = unique "huge"
    let listener = Action<string>(fun m -> if m.Contains key then lock heard (fun () -> heard.Add m))
    DebugWarnings.Listener <- Delegate.Combine(DebugWarnings.Listener, listener) :?> Action<string>
    try
        let image = Image.fromPixels (key, 8200, 1, solid 8200 1 (1uy, 1uy, 1uy, 255uy)) []
        let s = Scene(ImageView.create [ ImageView.image image; ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ])
        Assert.Equal(8200.0, s.Root.Bounds.Width)
        s.Render() |> ignore
        Assert.Contains("halved", Assert.Single heard)
    finally
        DebugWarnings.Listener <- Delegate.Remove(DebugWarnings.Listener, listener) :?> Action<string>
#endif

// --- The Gallery --------------------------------------------------------------------------------

[<Fact>]
let ``the Gallery's Images page finds its embedded pictures and draws every one`` () =
    let s = Scene(Scribble.Samples.Gallery.ImagesPage.view (), 720.0, 1600.0)
    let list = s.Render()
    Assert.Empty s.Errors
    // 4 stretch modes, 3 directions, smooth and pixelated, two avatars, the pattern; the exported card's potion.
    Assert.Equal(13, list.Images.Count)

/// Finding 3 (review of M8 step 3b): pictures were let go only while drawing, so a hidden element kept its own.
[<Fact>]
let ``an ImageView that is hidden lets go of its picture`` () =
    let image = Image.fromPixels (unique "hidden", 4, 4, solid 4 4 (255uy, 0uy, 0uy, 255uy)) []
    let view visible = StackPanel.create [ StackPanel.children [ ImageView.create [ ImageView.image image; ImageView.isVisible visible ] ] ]
    let scene = Scene(view true)
    scene.Render() |> ignore
    let imageView = scene.Root.Children[0]
    Assert.Equal(1, imageView.HeldImageCount)
    scene.Update(view false)
    scene.Render() |> ignore
    scene.Render() |> ignore
    Assert.Equal(0, imageView.HeldImageCount)
