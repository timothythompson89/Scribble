/// M8 step 3b (e): `Image.fromFile` and `Image.fromUrl`. A fake HTTP handler answers every request (no real network):
/// at most 6 downloads at once, cancelled when nothing holds them, failures (404, too big, timeout, relative URL on
/// the desktop) reported once, a size that arrives with the pixels, and a 10,000-row list downloading only what's
/// shown (DESIGN.md, "M8 step 3b", (e)).
module Scribble.Tests.ImageLoadingTests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping

/// A 32 × 32 RGBA PNG from PngSuite.
let private pngBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "PngSuite", "basn6a08.png"))

let private unique (name: string) = name + "-" + Guid.NewGuid().ToString("N")

/// Answers requests: at once with `answer url` when it gives one, else when the test calls `Answer url`. Records
/// every request and every cancellation.
type private FakeHttp(answer: string -> HttpResponseMessage voption) =
    inherit HttpMessageHandler()
    let sync = obj ()
    let requests = List<string>()
    let cancelled = List<string>()
    let agents = List<string>()
    let waiting = Dictionary<string, TaskCompletionSource<HttpResponseMessage>>()

    member _.Requests = lock sync (fun () -> List.ofSeq requests)
    member _.Cancelled = lock sync (fun () -> List.ofSeq cancelled)
    member _.UserAgents = lock sync (fun () -> List.ofSeq agents)

    member _.Answer(url: string, response: HttpResponseMessage) =
        let tcs = lock sync (fun () -> waiting[url])
        tcs.TrySetResult response |> ignore

    override _.SendAsync(request: HttpRequestMessage, token: CancellationToken) =
        let url = request.RequestUri.AbsoluteUri
        lock sync (fun () ->
            requests.Add url
            agents.Add(request.Headers.UserAgent.ToString()))
        match answer url with
        | ValueSome r -> Task.FromResult r
        | ValueNone ->
            let tcs = TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously)
            lock sync (fun () -> waiting[url] <- tcs)
            token.Register(fun () ->
                lock sync (fun () -> cancelled.Add url)
                tcs.TrySetCanceled token |> ignore)
            |> ignore
            tcs.Task

let private ok (bytes: byte[]) = new HttpResponseMessage(HttpStatusCode.OK, Content = new ByteArrayContent(bytes))

/// Waits (real time: downloads finish on other threads) until `condition`, running what they post to the UI thread.
let private waitFor (cache: ImageCache) (dispatcher: Dispatcher) (condition: unit -> bool) =
    let deadline = DateTime.UtcNow.AddSeconds 5.0
    while not (condition ()) && DateTime.UtcNow < deadline do
        dispatcher.RunPending()
        Thread.Yield() |> ignore
    dispatcher.RunPending()
    let struct (running, waiting) = cache.Downloads
    Assert.True(condition (), "timed out waiting; downloads running " + string running + ", waiting " + string waiting + "
" + Environment.StackTrace)

/// A tree showing `view`, its errors collected; `http` answers the downloads of its own image cache (null: the tests'
/// default, which refuses every request).
type private Scene(view: View, http: HttpMessageHandler, ?width: float, ?height: float, ?timeout: TimeSpan) =
    let size = Size.Create(defaultArg width 400.0, defaultArg height 300.0)
    let errors = List<exn>()
    let dispatcher = Dispatcher ignore
    do dispatcher.OnError <- errors.Add
    let tree = ElementTree(textSystem ())
    do
        if not (isNull http) then tree.Images.Http.Handler <- http
        timeout |> Option.iter (fun t -> tree.Images.Http.Timeout <- t)
        tree.Dispatcher <- ValueSome dispatcher
        tree.Update view
        tree.UpdateLayout size
    member _.Tree = tree
    member _.Root = tree.Root.Value
    member _.Errors = errors
    member _.Dispatcher = dispatcher
    member _.Update(v: View) =
        tree.Update v
        tree.UpdateLayout size
    member _.Layout() = tree.UpdateLayout size
    member _.Render() =
        let list = DrawList(1.0)
        tree.Render list
        dispatcher.FrameEnded()
        list
    member _.WaitFor(condition: unit -> bool) = waitFor tree.Images dispatcher condition
    interface IDisposable with
        member _.Dispose() =
            // Taken down, its downloads are cancelled: the next test gets all 6 places.
            tree.Update(Border.create [])
            waitFor tree.Images dispatcher (fun () -> tree.Images.Downloads = struct (0, 0))

let private topLeft (image: Image) =
    ImageView.create [ ImageView.image image; ImageView.horizontalAlignment Left; ImageView.verticalAlignment Top ]

// --- The values -------------------------------------------------------------------------------------

[<Fact>]
let ``URLs and files are keyed by their absolute address and full path`` () =
    Assert.Equal("url:https://example.test/a%20b.png", (Image.fromUrl "https://example.test/a b.png" []).Key)
    Assert.Equal("url:images/logo.png", (Image.fromUrl "images/logo.png" []).Key)
    Assert.Equal("url:/images/logo.png", (Image.fromUrl "/images/logo.png" []).Key)
    let relative = Image.fromFile "pictures/x.png" []
    Assert.Equal("file:" + Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "pictures/x.png")), relative.Key)
    let full = Path.Combine(Path.GetTempPath(), "x.png")
    Assert.Equal("file:" + full, (Image.fromUrl (Uri(full).AbsoluteUri) []).Key)

// --- URLs ---------------------------------------------------------------------------------------

[<Fact>]
let ``a downloaded picture measures 0 × 0 until it arrives, then lays out again and draws`` () =
    let url = "https://example.test/" + unique "a" + ".png"
    let http = new FakeHttp(fun _ -> ValueNone)
    use s = new Scene(topLeft (Image.fromUrl url []), http)
    Assert.Equal(0.0, s.Root.Bounds.Width)
    Assert.Equal(0, s.Render().Images.Count)
    Assert.Empty s.Errors
    s.WaitFor(fun () -> http.Requests = [ url ])
    // Some servers refuse a request without a User-Agent (Wikimedia gave Tim 403).
    Assert.Equal<string list>([ "Scribble/1.0" ], http.UserAgents)
    let measured = ref 0
    s.Tree.OnInvalidated <- (fun () -> measured.Value <- measured.Value + 1)
    http.Answer(url, ok pngBytes)
    s.WaitFor(fun () -> measured.Value > 0)
    s.Layout()
    Assert.Equal(32.0, s.Root.Bounds.Width)
    Assert.Equal(1, s.Render().Images.Count)
    Assert.Empty s.Errors

[<Fact>]
let ``a 404 is reported once, with its status`` () =
    let url = "https://example.test/" + unique "missing" + ".png"
    let http = new FakeHttp(fun _ -> ValueSome(new HttpResponseMessage(HttpStatusCode.NotFound)))
    use s = new Scene(StackPanel.create [ StackPanel.children [ for _ in 1..5 -> topLeft (Image.fromUrl url []) ] ], http)
    s.Render() |> ignore
    s.WaitFor(fun () -> s.Layout(); s.Render() |> ignore; s.Errors.Count > 0)
    s.Render() |> ignore
    let e = Assert.Single s.Errors
    Assert.Contains("404", e.Message)
    Assert.Equal(1, http.Requests.Length)

[<Fact>]
let ``a response over 64 MB fails without being read`` () =
    let url = "https://example.test/" + unique "huge" + ".png"
    let http =
        new FakeHttp(fun _ ->
            let r = ok [| 1uy |]
            r.Content.Headers.ContentLength <- Nullable(65L * 1024L * 1024L)
            ValueSome r)
    use s = new Scene(topLeft (Image.fromUrl url []), http)
    s.WaitFor(fun () -> s.Layout(); s.Render() |> ignore; s.Errors.Count > 0)
    Assert.Contains("over 64 MB", (Assert.Single s.Errors).Message)

[<Fact>]
let ``a download that takes too long fails`` () =
    let url = "https://example.test/" + unique "slow" + ".png"
    let http = new FakeHttp(fun _ -> ValueNone)
    use s = new Scene(topLeft (Image.fromUrl url []), http, timeout = TimeSpan.FromMilliseconds 100.0)
    s.WaitFor(fun () -> s.Layout(); s.Render() |> ignore; s.Errors.Count > 0)
    Assert.Contains("took longer than", (Assert.Single s.Errors).Message)

[<Fact>]
let ``on the desktop a relative URL, or one that isn't http, fails with why`` () =
    let http = new FakeHttp(fun _ -> ValueNone)
    use s =
        new Scene(
            StackPanel.create [
                StackPanel.children [
                    topLeft (Image.fromUrl ("images/" + unique "r" + ".png") [])
                    topLeft (Image.fromUrl ("ftp://example.test/" + unique "f" + ".png") [])
                ]
            ],
            http
        )
    s.Render() |> ignore
    Assert.Equal(2, s.Errors.Count)
    Assert.Contains("relative URL", s.Errors[0].Message)
    Assert.Contains("http and https", s.Errors[1].Message)
    Assert.Empty http.Requests

[<Fact>]
let ``at most 6 download at once, the rest in the order asked`` () =
    let run = unique "six"
    let urls = [ for i in 1..10 -> "https://example.test/" + run + "/" + string i + ".png" ]
    let http = new FakeHttp(fun _ -> ValueNone)
    use s = new Scene(StackPanel.create [ StackPanel.children [ for u in urls -> topLeft (Image.fromUrl u []) ] ], http)
    s.WaitFor(fun () -> http.Requests.Length = 6)
    Assert.Equal<string list>(List.take 6 urls, http.Requests)
    let struct (running, waiting) = s.Tree.Images.Downloads
    Assert.True(running >= 6 && waiting >= 4)
    http.Answer(urls[0], ok pngBytes)
    s.WaitFor(fun () -> http.Requests.Length = 7)
    Assert.Equal(urls[6], List.last http.Requests)

[<Fact>]
let ``a download nothing shows any more is cancelled, and one still waiting is never asked for`` () =
    let run = unique "cancel"
    let urls = [ for i in 1..8 -> "https://example.test/" + run + "/" + string i + ".png" ]
    let http = new FakeHttp(fun _ -> ValueNone)
    use s = new Scene(StackPanel.create [ StackPanel.children [ for u in urls -> topLeft (Image.fromUrl u []) ] ], http)
    s.WaitFor(fun () -> http.Requests.Length = 6)
    s.Update(StackPanel.create [])
    s.WaitFor(fun () -> http.Cancelled.Length = 6)
    Assert.Equal(6, http.Requests.Length)
    for u in urls do
        Assert.False(s.Tree.Images.Contains("url:" + u))
    Assert.Empty s.Errors

// --- Files --------------------------------------------------------------------------------------

[<Fact>]
let ``a file is read off the UI thread; a missing one is reported`` () =
    let dir = Path.Combine(Path.GetTempPath(), unique "scribble")
    Directory.CreateDirectory dir |> ignore
    try
        let path = Path.Combine(dir, "a.png")
        File.WriteAllBytes(path, pngBytes)
        use s =
            new Scene(
                StackPanel.create [
                    StackPanel.children [ topLeft (Image.fromFile path []); topLeft (Image.fromFile (Path.Combine(dir, "none.png")) []) ]
                ],
                null
            )
        // Inline here, so it has loaded by the first render; it lays out on the next.
        s.Render() |> ignore
        s.Layout()
        Assert.Equal(32.0, s.Root.Children[0].Bounds.Width)
        Assert.Equal(1, s.Render().Images.Count)
        let e = Assert.Single s.Errors
        Assert.IsType<FileNotFoundException>(e.InnerException) |> ignore
    finally
        Directory.Delete(dir, true)

// --- Proportional to what's shown ---------------------------------------------------------------

[<Fact>]
let ``10,000 rows of downloaded avatars ask only for the realized rows, also after jumping to 9,000`` () =
    let run = unique "rows"
    let urlOf (i: int) = "https://example.test/" + run + "/" + string i + ".png"
    let http = new FakeHttp(fun _ -> ValueNone)
    let scroller = Scroller()
    let view =
        ScrollViewer.create [
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source [| 0..9999 |]
                    ItemsView.keySelector id
                    ItemsView.viewSelector (fun (i: int) -> ImageView.create [ ImageView.image (Image.fromUrl (urlOf i) []); ImageView.height 32.0 ])
                    ItemsView.scroller scroller
                ])
        ]
    use s = new Scene(view, http, 300.0, 200.0)
    s.Render() |> ignore
    s.WaitFor(fun () -> http.Requests.Length = 6)
    scroller.ScrollToIndex(9000, ScrollAlign.Start, false)
    s.Layout()
    s.Render() |> ignore
    // The first rows' downloads are cancelled and give their places to the new rows.
    s.WaitFor(fun () -> http.Requests.Length = 12)
    let asked = http.Requests
    Assert.Equal(6, http.Cancelled.Length)
    for u in List.skip 6 asked do
        Assert.True(int (Path.GetFileNameWithoutExtension(Uri(u).LocalPath)) >= 8990, u)
    for i in 100..8900 do
        Assert.DoesNotContain(urlOf i, asked)
    Assert.Empty s.Errors

// --- Image cursors (M8 step 3b (f) 1) ------------------------------------------------------------------

[<Fact>]
let ``an image cursor shows the arrow until its picture arrives, then changes without a frame`` () =
    let url = "https://example.test/" + unique "cursor" + ".png"
    let http = new FakeHttp(fun _ -> ValueNone)
    let platform, device = new Scribble.Tests.AppTests.FakePlatform(), new Scribble.Tests.AppTests.FakeDevice()
    let errors = List<exn>()
    let cursor = Cursor.fromImage (Image.fromUrl url []) []
    let view =
        Border.create [
            Border.width 100.0
            Border.height 100.0
            Border.horizontalAlignment HorizontalAlignment.Left
            Border.verticalAlignment VerticalAlignment.Top
            Border.cursor cursor
        ]
    let app = App.start (platform, device, new FixedShaper(), [ App.view view; App.onError (fun _ e -> errors.Add e) ])
    let w = platform.Window.Value
    let host = app :?> ElementHost
    // Before the first frame, which asks for the picture.
    host.AppValues.Images.Http.Handler <- http
    try
        w.DeliverFrame() |> ignore
        // The first frame asked for it: the download started before any pointer.
        let deadline = DateTime.UtcNow.AddSeconds 5.0
        while http.Requests.IsEmpty && DateTime.UtcNow < deadline do
            Thread.Yield() |> ignore
        Assert.Equal<string list>([ url ], http.Requests)
        w.Send(Scribble.Platform.PointerMoved(Scribble.Platform.Mouse, 0, { X = 50.0; Y = 50.0 }, Scribble.Platform.Modifiers.None))
        Assert.Equal(Cursor.Default, w.Cursor)
        // Hovering may redraw (element states); that frame is drawn before the picture comes.
        w.DeliverFrame() |> ignore
        Assert.False(w.FrameRequested)
        let frames = device.Frames
        http.Answer(url, ok pngBytes)
        let deadline = DateTime.UtcNow.AddSeconds 5.0
        while w.Cursor = Cursor.Default && DateTime.UtcNow < deadline do
            platform.RunWake()
            Thread.Yield() |> ignore
        Assert.Equal(cursor, w.Cursor)
        // A 32 × 32 picture: its bitmap at 1×.
        match w.Cursor with
        | Cursor.Image image -> Assert.Equal(32, (image.Render 1.0).Width)
        | _ -> ()
        Assert.False(w.FrameRequested)
        Assert.Equal(frames, device.Frames)
        Assert.Empty errors
    finally
        host.App.Update(Border.create [])
        w.DeliverFrame() |> ignore

// --- Image state belongs to an app, not the process (DESIGN.md, "Image state per app, not per process") ---------

[<Fact>]
let ``a tree's image cache is its app values', and two bare trees share none`` () =
    let a, b = ElementTree(textSystem ()), ElementTree(textSystem ())
    Assert.Same(a.AppValues.Images, a.Images)
    Assert.NotSame(a.Images, b.Images)
    Assert.NotSame(a.Images.Http, b.Images.Http)
    // A tree given another's app values (a host's layers, an app's windows) has its pictures too.
    b.AppValues <- a.AppValues
    Assert.Same(a.Images, b.Images)

[<Fact>]
let ``the same picture in two trees is decoded by each`` () =
    let key = unique "twice"
    let image () = Image.fromBytes (key, pngBytes) []
    use a = new Scene(topLeft (image ()), null)
    use b = new Scene(topLeft (image ()), null)
    a.Render() |> ignore
    b.Render() |> ignore
    Assert.Equal(1, a.Tree.Images.Decodes("bytes:" + key))
    Assert.Equal(1, b.Tree.Images.Decodes("bytes:" + key))

/// What failed on GitHub's runners while the six places were the process's (CI run 7, 2026-11-20): a test waiting
/// for its six requests while another test's downloads held two of the places.
[<Fact>]
let ``one tree's downloads take none of another's six places`` () =
    let urlsOf (run: string) (n: int) = [ for i in 1..n -> "https://example.test/" + run + "/" + string i + ".png" ]
    let viewOf (urls: string list) = StackPanel.create [ StackPanel.children [ for u in urls -> topLeft (Image.fromUrl u []) ] ]
    // The first tree fills its six places and keeps four waiting; nothing answers.
    let busy = new FakeHttp(fun _ -> ValueNone)
    use a = new Scene(viewOf (urlsOf (unique "busy") 10), busy)
    a.WaitFor(fun () -> busy.Requests.Length = 6)
    Assert.Equal(struct (6, 4), a.Tree.Images.Downloads)
    // The second still gets all six of its own at once.
    let http = new FakeHttp(fun _ -> ValueNone)
    use b = new Scene(viewOf (urlsOf (unique "free") 6), http)
    b.WaitFor(fun () -> http.Requests.Length = 6)
    Assert.Equal(struct (6, 0), b.Tree.Images.Downloads)
    Assert.Equal(struct (6, 4), a.Tree.Images.Downloads)
    Assert.Equal(6, busy.Requests.Length)

[<Fact>]
let ``in the unit tests a new cache decodes inline and refuses the network`` () =
    let url = "https://example.test/" + unique "nobody" + ".png"
    use s = new Scene(topLeft (Image.fromUrl url []), null)
    Assert.True(s.Tree.Images.DecodeInline)
    s.WaitFor(fun () -> s.Layout(); s.Render() |> ignore; s.Errors.Count > 0)
    Assert.Contains("Unit tests have no network", (Assert.Single s.Errors).Message)
    Assert.Contains(url, (Assert.Single s.Errors).Message)
