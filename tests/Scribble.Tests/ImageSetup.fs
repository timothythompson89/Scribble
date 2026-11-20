/// What every image cache made in the unit tests starts with (DESIGN.md, "Image state per app, not per process",
/// decision 5): pictures decode inline, so a test is the same on every machine, and no request reaches the network.
/// Each app and each bare tree has its own cache, so nothing here is shared between tests: this only sets the
/// defaults a new cache gets. A test that downloads gives its tree's cache a handler (`tree.Images.Http.Handler`).
namespace Scribble.Tests

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Scribble.Core
open Xunit.Abstractions
open Xunit.Sdk

/// Refuses every request, saying which: a unit test that asks for a URL without saying what should come back fails
/// its picture at once, instead of waiting on a real server.
type NoNetwork() =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _: CancellationToken) =
        Task.FromException<HttpResponseMessage>(
            HttpRequestException(
                "Unit tests have no network: nothing was set to answer "
                + request.RequestUri.AbsoluteUri
                + " (give the tree's image cache a handler: tree.Images.Http.Handler)."
            )
        )

/// For the few tests that wait in real time for work on other threads (a download's cancellation coming back, a
/// background decode). Such a wait ends the moment its condition holds, so a long limit costs a passing test
/// nothing; a short one fails a slow machine (2026-11-20, CI run 17: three of six cancelled downloads had given
/// their places back after 5 s on a hosted runner; never seen on Tim's machine in over a hundred runs).
module RealTime =
    /// How long such a wait may last before the test gives up.
    let patience = TimeSpan.FromSeconds 30.0

    /// The thread pool as it is now, for a timeout's message: queued work with few threads means the wait starved.
    let poolNow () =
        let mutable workers, io = 0, 0
        ThreadPool.GetMinThreads(&workers, &io)
        "thread pool: "
        + string ThreadPool.ThreadCount
        + " threads (minimum "
        + string workers
        + "), "
        + string ThreadPool.PendingWorkItemCount
        + " work items queued, "
        + string Environment.ProcessorCount
        + " processors"

/// xUnit makes one of these before it runs (or lists) the tests of this assembly: the one place that runs once
/// for the whole test process.
type ScribbleTestFramework(messageSink: IMessageSink) =
    inherit XunitTestFramework(messageSink)

    do
        // xUnit runs test bodies on the thread pool, as many at once as there are processors, which is also how
        // many threads the pool starts at once without waiting. A test that waits for a continuation (a download
        // handing its place back) while the others hold those threads gets it only as the pool adds threads, about
        // one a second. A higher minimum lets continuations run at once on a small machine.
        let mutable workers, io = 0, 0
        ThreadPool.GetMinThreads(&workers, &io)
        ThreadPool.SetMinThreads(max workers 32, max io 32) |> ignore
        ImageCache.ConfigureNew <-
            Action<ImageCache>(fun cache ->
                cache.DecodeInline <- true
                cache.Http.Handler <- new NoNetwork())

module ImageSetup =
    [<assembly: Xunit.TestFramework("Scribble.Tests.ScribbleTestFramework", "Scribble.Tests")>]
    do ()
