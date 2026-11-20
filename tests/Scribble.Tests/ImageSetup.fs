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

/// xUnit makes one of these before it runs (or lists) the tests of this assembly: the one place that runs once
/// for the whole test process.
type ScribbleTestFramework(messageSink: IMessageSink) =
    inherit XunitTestFramework(messageSink)

    do
        ImageCache.ConfigureNew <-
            Action<ImageCache>(fun cache ->
                cache.DecodeInline <- true
                cache.Http.Handler <- new NoNetwork())

module ImageSetup =
    [<assembly: Xunit.TestFramework("Scribble.Tests.ScribbleTestFramework", "Scribble.Tests")>]
    do ()
