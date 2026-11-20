/// `ctx.useImage` (DESIGN.md, "A hook for a picture's load state"): a component holds a picture and reads where it
/// stands. Each tree has its own image cache, decoding inline and refusing the network unless a test says otherwise
/// (ImageSetup.fs).
module Scribble.Tests.ImageHookTests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open Scribble.Core
open Scribble.Rendering
open Scribble.Tests.TestShaping

let private pngBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PngSuite", "basn6a08.png"))

let private unique (name: string) = name + "-" + Guid.NewGuid().ToString("N")

/// Answers a request when the test calls `Answer url`; records every request and every cancellation.
type private FakeHttp() =
    inherit HttpMessageHandler()
    let sync = obj ()
    let requests = List<string>()
    let cancelled = List<string>()
    let waiting = Dictionary<string, TaskCompletionSource<HttpResponseMessage>>()

    member _.Requests = lock sync (fun () -> List.ofSeq requests)
    member _.Cancelled = lock sync (fun () -> List.ofSeq cancelled)

    member _.Answer(url: string, response: HttpResponseMessage) =
        let tcs = lock sync (fun () -> waiting[url])
        tcs.TrySetResult response |> ignore

    override _.SendAsync(request: HttpRequestMessage, token: CancellationToken) =
        let url = request.RequestUri.AbsoluteUri
        let tcs = TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously)
        lock sync (fun () ->
            requests.Add url
            waiting[url] <- tcs)
        token.Register(fun () ->
            lock sync (fun () -> cancelled.Add url)
            tcs.TrySetCanceled token |> ignore)
        |> ignore
        tcs.Task

let private ok (bytes: byte[]) = new HttpResponseMessage(HttpStatusCode.OK, Content = new ByteArrayContent(bytes))
let private notFound () = new HttpResponseMessage(HttpStatusCode.NotFound)

/// A tree with a dispatcher (what tells a component that loading ended), its errors collected.
type private Scene(?http: HttpMessageHandler) =
    let size = Size.Create(400.0, 300.0)
    let errors = List<exn>()
    let dispatcher = Dispatcher ignore
    do dispatcher.OnError <- errors.Add
    let tree = ElementTree(textSystem ())
    do
        http |> Option.iter (fun h -> tree.Images.Http.Handler <- h)
        tree.Dispatcher <- ValueSome dispatcher
    member _.Tree = tree
    member _.Images = tree.Images
    member _.Errors = errors
    member _.Update(view: View) = tree.Update view
    /// One frame: what was posted to the UI thread, changed components, layout, a drawing, then effects.
    member _.Frame() : DrawList =
        dispatcher.RunPending()
        tree.UpdateLayout size
        let list = DrawList(1.0)
        tree.Render list
        dispatcher.FrameEnded()
        tree.RunAfterFrame()
        list
    /// Frames (real time: downloads and background decodes end on other threads) until `condition`.
    member this.WaitFor(condition: unit -> bool) =
        let deadline = DateTime.UtcNow.AddSeconds 5.0
        while not (condition ()) && DateTime.UtcNow < deadline do
            this.Frame() |> ignore
            Thread.Yield() |> ignore
        this.Frame() |> ignore
        Assert.True(condition (), "timed out waiting")
    interface IDisposable with
        member this.Dispose() =
            // Taken down, its downloads are cancelled.
            tree.Update(Border.create [])
            this.WaitFor(fun () -> tree.Images.Downloads = struct (0, 0))

let private isReady (status: ImageStatus) =
    match status with
    | ImageStatus.Ready -> true
    | _ -> false

let private isLoading (status: ImageStatus) =
    match status with
    | ImageStatus.Loading -> true
    | _ -> false

let private isFailed (status: ImageStatus) =
    match status with
    | ImageStatus.Failed _ -> true
    | _ -> false

/// A component that asks about `image`, shows it, and leaves what it was told (and its handle) where the test reads.
let private watcher (seen: List<ImageStatus>) (handle: ImageLoad voption ref) =
    Component.define "Watcher" (fun ctx (image: Image) ->
        let load = ctx.useImage image
        seen.Add load.Status
        handle.Value <- ValueSome load
        ImageView.create [ ImageView.image image; ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ])

// --- Status ---------------------------------------------------------------------------------------

[<Fact>]
let ``a picture decoded at once is ready on the first run, with its size, and the component runs once`` () =
    use s = new Scene()
    let seen, handle = List<ImageStatus>(), ref ValueNone
    s.Update(watcher seen handle (Image.fromBytes (unique "ready", pngBytes) []))
    let list = s.Frame()
    s.Frame() |> ignore
    Assert.True(isReady (Assert.Single seen))
    Assert.Equal(32, handle.Value.Value.PixelWidth)
    Assert.Equal(32, handle.Value.Value.PixelHeight)
    Assert.Equal(1, list.Images.Count)
    Assert.Empty s.Errors

[<Fact>]
let ``while it decodes the status is loading with the header's size; the component runs again once when it ends`` () =
    use s = new Scene()
    s.Images.DecodeInline <- false
    let seen, handle = List<ImageStatus>(), ref ValueNone
    s.Update(watcher seen handle (Image.fromBytes (unique "background", pngBytes) []))
    // The size came from the header on the first run, whatever the decoding thread has done since.
    Assert.Equal(32, handle.Value.Value.PixelWidth)
    s.WaitFor(fun () -> seen.Count > 0 && isReady seen[seen.Count - 1])
    // Loading then ready, or ready at once when the decoding thread was quicker than the first run's read.
    Assert.True(seen.Count <= 2, $"ran {seen.Count} times")
    let runs = seen.Count
    for _ in 1..3 do
        s.Frame() |> ignore
    Assert.Equal(runs, seen.Count)
    Assert.Empty s.Errors

[<Fact>]
let ``the hook's state shows on the element's hooks, named by the picture's key`` () =
    use s = new Scene()
    let key = unique "hooks"
    let seen, handle = List<ImageStatus>(), ref ValueNone
    s.Update(watcher seen handle (Image.fromBytes (key, pngBytes) []))
    s.Frame() |> ignore
    let hook = Assert.Single s.Tree.Root.Value.Hooks
    Assert.Equal("useImage bytes:" + key, hook.Kind)
    Assert.Equal("ready 32 × 32", hook.Status)

// --- Failure ----------------------------------------------------------------------------------------

[<Fact>]
let ``a failure reaches the hook and not the app's error handler, though an ImageView shows the picture too`` () =
    let broken (name: string) = Image.fromBytes (unique name, [| 1uy; 2uy; 3uy |]) []
    // Without the hook, the ImageView reports it (so the next part of this test means something).
    use plain = new Scene()
    plain.Update(ImageView.create [ ImageView.image (broken "plain") ])
    plain.Frame() |> ignore
    Assert.IsType<ImageLoadException>(Assert.Single plain.Errors) |> ignore
    // With the hook: the component is told, and nothing is reported.
    use s = new Scene()
    let seen, handle = List<ImageStatus>(), ref ValueNone
    s.Update(watcher seen handle (broken "hooked"))
    s.Frame() |> ignore
    s.Frame() |> ignore
    match Assert.Single seen with
    | ImageStatus.Failed e -> Assert.IsType<ImageLoadException> e |> ignore
    | other -> Assert.Fail $"expected failed, was {other}"
    Assert.Empty s.Errors

// --- What it holds ------------------------------------------------------------------------------------

[<Fact>]
let ``the component holds the picture though its view shows something else, and lets go when it goes`` () =
    let http = new FakeHttp()
    use s = new Scene(http)
    let url = "https://example.test/" + unique "held" + ".png"
    let asker =
        Component.define "Asker" (fun ctx (u: string) ->
            ctx.useImage (Image.fromUrl u []) |> ignore
            // No ImageView: only the hook holds it.
            Border.create [])
    s.Update(asker url)
    s.WaitFor(fun () -> http.Requests.Length = 1)
    Assert.Equal(1, s.Images.Count)
    s.Update(Border.create [])
    // Nothing holds it: its download is cancelled and it leaves the cache.
    s.WaitFor(fun () -> http.Cancelled.Length = 1)
    Assert.Equal(0, s.Images.Count)
    Assert.Empty s.Errors

[<Fact>]
let ``asking for another picture lets go of the first`` () =
    let http = new FakeHttp()
    use s = new Scene(http)
    let run = unique "swap"
    let urlOf (name: string) = "https://example.test/" + run + "/" + name + ".png"
    let asker =
        Component.define "Asker" (fun ctx (u: string) ->
            ctx.useImage (Image.fromUrl u []) |> ignore
            Border.create [])
    s.Update(asker (urlOf "a"))
    s.WaitFor(fun () -> http.Requests.Length = 1)
    s.Update(asker (urlOf "b"))
    s.WaitFor(fun () -> http.Requests.Length = 2 && http.Cancelled.Length = 1)
    Assert.Equal<string list>([ urlOf "a" ], http.Cancelled)
    Assert.Equal(1, s.Images.Count)
    Assert.True(s.Images.Contains("url:" + urlOf "b"))

// --- Retry --------------------------------------------------------------------------------------------

[<Fact>]
let ``retry after a failure asks again; when it succeeds the hook is ready and the ImageView draws it`` () =
    let http = new FakeHttp()
    use s = new Scene(http)
    let url = "https://example.test/" + unique "retry" + ".png"
    let seen, handle = List<ImageStatus>(), ref ValueNone
    s.Update(watcher seen handle (Image.fromUrl url []))
    s.WaitFor(fun () -> http.Requests.Length = 1)
    Assert.True(isLoading handle.Value.Value.Status)
    http.Answer(url, notFound ())
    s.WaitFor(fun () -> isFailed handle.Value.Value.Status && isFailed seen[seen.Count - 1])
    Assert.Equal(0, s.Frame().Images.Count)
    // Retry, as a button's handler would: the same picture is asked for again, in place.
    handle.Value.Value.Retry()
    s.WaitFor(fun () -> http.Requests.Length = 2)
    Assert.True(isLoading handle.Value.Value.Status)
    Assert.Equal(1, s.Images.Count)
    http.Answer(url, ok pngBytes)
    s.WaitFor(fun () -> isReady handle.Value.Value.Status && isReady seen[seen.Count - 1])
    // The ImageView, which had drawn nothing for the failed picture, was told of the retry and draws it now.
    Assert.Equal(1, s.Frame().Images.Count)
    Assert.Equal(32, handle.Value.Value.PixelWidth)
    Assert.Empty s.Errors

[<Fact>]
let ``retry does nothing unless the picture failed`` () =
    use s = new Scene()
    let seen, handle = List<ImageStatus>(), ref ValueNone
    let key = unique "fine"
    s.Update(watcher seen handle (Image.fromBytes (key, pngBytes) []))
    s.Frame() |> ignore
    handle.Value.Value.Retry()
    s.Frame() |> ignore
    Assert.Equal(1, seen.Count)
    Assert.Equal(1, s.Images.Decodes("bytes:" + key))

// --- Proportional to what's shown ---------------------------------------------------------------------

[<Fact>]
let ``10,000 rows with a hook each hold only the realized rows, also after jumping to 9,000`` () =
    let http = new FakeHttp()
    use s = new Scene(http)
    let run = unique "rows"
    let urlOf (i: int) = "https://example.test/" + run + "/" + string i + ".png"
    let row =
        Component.define "Row" (fun ctx (i: int) ->
            let load = ctx.useImage (Image.fromUrl (urlOf i) [])
            TextBlock.create [ TextBlock.text (if isReady load.Status then "ready" else "loading"); TextBlock.height 32.0 ])
    let scroller = Scroller()
    s.Update(
        ScrollViewer.create [
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source [| 0..9999 |]
                    ItemsView.keySelector id
                    ItemsView.viewSelector row
                    ItemsView.scroller scroller
                ])
        ])
    s.WaitFor(fun () -> http.Requests.Length = 6)
    let held = s.Images.Count
    Assert.True(held >= 6 && held < 40, $"{held} pictures held for a 300-DIP-high list")
    scroller.ScrollToIndex(9000, ScrollAlign.Start, false)
    // The first rows' components went: their downloads are cancelled and give their places to the new rows.
    // (How many rows are asked for on the way depends on how many frames the list takes to settle, so the counts
    // aren't exact: what is held and what was never asked for are.)
    let firstRows = [ for i in 0..5 -> urlOf i ]
    s.WaitFor(fun () ->
        let cancelled = http.Cancelled
        firstRows |> List.forall (fun u -> List.contains u cancelled) && http.Requests.Length >= 12)
    let struct (running, _) = s.Images.Downloads
    Assert.Equal(6, running)
    Assert.True(s.Images.Count < 40, $"{s.Images.Count} pictures held after the jump")
    let asked = http.Requests
    // (A first-screen row that was still waiting for a place may get one for a moment, as the rows before it let
    // theirs go one by one: so not "everything after the first six is near 9,000", only "nothing between".)
    for i in 100..8900 do
        Assert.DoesNotContain(urlOf i, asked)
    Assert.Empty s.Errors

// --- Hidden ---------------------------------------------------------------------------------------------

[<Fact>]
let ``hidden with what is around it, the component lets go; shown again it takes the picture back`` () =
    let http = new FakeHttp()
    use s = new Scene(http)
    let url = "https://example.test/" + unique "hidden" + ".png"
    let runs = ref 0
    let asker =
        Component.define "Asker" (fun ctx (u: string) ->
            runs.Value <- runs.Value + 1
            ctx.useImage (Image.fromUrl u []) |> ignore
            Border.create [])
    let page (visible: bool) = Border.create [ Border.isVisible visible; Border.content (asker url) ]
    s.Update(page true)
    s.WaitFor(fun () -> http.Requests.Length = 1)
    // Hidden: as an ImageView in it would, the hook lets go, and the download nothing holds is cancelled.
    s.Update(page false)
    s.WaitFor(fun () -> http.Cancelled.Length = 1)
    Assert.Equal(0, s.Images.Count)
    // Shown again, with the same props (so the component doesn't run for that): the hook asks again.
    s.Update(page true)
    s.WaitFor(fun () -> http.Requests.Length = 2)
    Assert.Equal(1, s.Images.Count)
    Assert.Equal(1, runs.Value)
    Assert.Empty s.Errors

// --- The picture itself -----------------------------------------------------------------------------------

[<Fact>]
let ``load.Image is the picture last asked about, so a view can show it without naming it twice`` () =
    use s = new Scene()
    let run = unique "image"
    let shower =
        Component.define "Shower" (fun ctx (name: string) ->
            let load = ctx.useImage (Image.fromBytes (run + name, pngBytes) [ Image.density 2.0 ])
            ImageView.create [ ImageView.image load.Image; ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ])
    s.Update(shower "a")
    Assert.Equal(1, s.Frame().Images.Count)
    // The value given, density and all: 32 pixels at density 2 lay out as 16 DIPs.
    Assert.Equal(16.0, s.Tree.Root.Value.Children[0].Bounds.Width)
    Assert.Equal(1, s.Images.Decodes("bytes:" + run + "a"))
    // Another picture: the handle follows.
    s.Update(shower "b")
    Assert.Equal(1, s.Frame().Images.Count)
    Assert.Equal(1, s.Images.Decodes("bytes:" + run + "b"))
    Assert.Empty s.Errors
