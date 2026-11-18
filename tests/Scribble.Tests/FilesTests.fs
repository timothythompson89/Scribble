/// `Files.save` (DESIGN.md, "Rendering to an image and saving it", decision 7, round 2 decision 8) on the fake
/// platform: the bytes reach the platform with the name, the answer comes back through the dispatcher (on the UI
/// thread), cancelling answers false, and each app's thread finds its own app.
module Scribble.Tests.FilesTests

open System
open System.Threading.Tasks
open Xunit
open Scribble.Core
open Scribble.Tests.AppTests
open Scribble.Tests.TestShaping

let private startApp () =
    let platform = new FakePlatform()
    let app = App.start (platform, new FakeDevice(), new FixedShaper(), [ App.view (Border.create []) ])
    platform, app

[<Fact>]
let ``Files.save hands the bytes to the platform and answers on the UI thread`` () =
    let platform, app = startApp ()
    let bytes = [| 1uy; 2uy; 3uy |]
    let saved = Files.save "chart.png" bytes
    // The platform answered at once; the answer waits for the dispatcher.
    Assert.False saved.IsCompleted
    platform.RunWake()
    Assert.True saved.Result
    let name, given = Assert.Single platform.Saved
    Assert.Equal("chart.png", name)
    Assert.Same(bytes, given)
    platform.SaveAnswer <- false
    let cancelled = Files.save "chart.png" bytes
    platform.RunWake()
    Assert.False cancelled.Result
    app.Dispose()

[<Fact>]
let ``Files.save refuses a missing name or bytes`` () =
    let _, app = startApp ()
    Assert.True((Files.save "" [| 1uy |]).IsFaulted)
    Assert.True((Files.save "a.png" null).IsFaulted)
    app.Dispose()

[<Fact>]
let ``apps on different threads each save through their own platform`` () =
    let platform, app = startApp ()
    // Another app, started on another thread, doesn't take this thread's place. A thread of its own: waiting on a
    // Task.Run can run it inline on this thread.
    let mutable other = Unchecked.defaultof<FakePlatform * IDisposable>
    let thread =
        Threading.Thread(fun () ->
            let p, a = startApp ()
            (Files.save "other.png" [| 9uy |]) |> ignore
            other <- (p, a))
    thread.Start()
    thread.Join()
    let saved = Files.save "mine.png" [| 1uy |]
    platform.RunWake()
    Assert.True saved.Result
    Assert.Equal("mine.png", fst (Assert.Single platform.Saved))
    Assert.Equal("other.png", fst (Assert.Single (fst other).Saved))
    app.Dispose()
    (snd other).Dispose()
