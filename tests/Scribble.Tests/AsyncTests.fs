/// M5b: the dispatcher, the synchronization context, state and dispatch from other threads,
/// useAsync (cancellation, late results, errors), commands and App.onError.
module Scribble.Tests.AsyncTests

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Xunit
open Scribble.Core
open Scribble.Tests.TestShaping
open Scribble.Tests.AppTests

/// Runs `work` on a new thread and waits for it (Task.Run + Wait may run it inline on this thread).
let private onOtherThread (work: unit -> unit) =
    let thread = Thread(work)
    thread.Start()
    thread.Join()

let rec private firstText (e: Element) : string =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then e.Get TextProps.text
    else e.Children |> Seq.map firstText |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""

/// An app on the fake platform, plus the platform (to drain wakes) and the host.
let private startApp (settings: AppSetting<'Msg> list) =
    let platform = new FakePlatform()
    let app = App.start (platform, new FakeDevice(), new FixedShaper(), settings)
    let host = app :?> ElementHost
    platform, platform.Window.Value, host

/// Drains wakes and draws frames until `condition` holds (async work finishing on other threads).
let private until (platform: FakePlatform) (w: FakeWindow) (condition: unit -> bool) =
    let watch = Diagnostics.Stopwatch.StartNew()
    while not (condition ()) && watch.ElapsedMilliseconds < 5000L do
        platform.RunWake()
        w.DeliverFrame() |> ignore
        Thread.Sleep 1
    Assert.True(condition (), "timed out")

[<Fact>]
let ``posts from other threads run in order on the draining thread, one wake per burst`` () =
    let wakes = ref 0
    let d = Dispatcher(fun () -> Interlocked.Increment wakes |> ignore)
    let ran = List<int * int>()
    onOtherThread (fun () ->
        for i in 1..50 do
            d.Post(fun () -> ran.Add((i, Environment.CurrentManagedThreadId))))
    Assert.Equal(1, wakes.Value)
    Assert.Empty ran
    d.RunPending()
    Assert.Equal<int list>([ 1..50 ], [ for i, _ in ran -> i ])
    Assert.All(ran, fun (_, thread) -> Assert.Equal(Environment.CurrentManagedThreadId, thread))
    d.Post ignore // drained, so the next post wakes again
    Assert.Equal(2, wakes.Value)

[<Fact>]
let ``InvokeAsync returns results and errors, failing posts go to OnError and the rest still run`` () =
    let d = Dispatcher ignore
    let errors = List<exn>()
    d.OnError <- errors.Add
    let ok = d.InvokeAsync(fun () -> 6 * 7)
    let bad = d.InvokeAsync(fun () -> failwith "nope")
    let after = ref false
    d.Post(fun () -> raise (InvalidOperationException "posted"))
    d.Post(fun () -> after.Value <- true)
    d.RunPending()
    Assert.Equal(42, ok.Result)
    Assert.True bad.IsFaulted
    Assert.Equal("posted", (Assert.Single errors).Message)
    Assert.True after.Value

[<Fact>]
let ``await resumes on the dispatcher's thread`` () =
    let d = Dispatcher ignore
    let previous = SynchronizationContext.Current
    SynchronizationContext.SetSynchronizationContext(DispatcherSynchronizationContext d)
    try
        let threads = List<int>()
        let work =
            task {
                threads.Add Environment.CurrentManagedThreadId
                do! Task.Delay 20
                threads.Add Environment.CurrentManagedThreadId
                do! Task.Run(fun () -> Thread.Sleep 5)
                threads.Add Environment.CurrentManagedThreadId
            }
        let watch = Diagnostics.Stopwatch.StartNew()
        while not work.IsCompleted && watch.ElapsedMilliseconds < 5000L do
            d.RunPending()
            Thread.Sleep 1
        work.Wait()
        Assert.Equal(3, threads.Count)
        Assert.All(threads, fun t -> Assert.Equal(Environment.CurrentManagedThreadId, t))
    finally
        SynchronizationContext.SetSynchronizationContext previous

[<Fact>]
let ``state set from another thread is queued to the UI thread`` () =
    let mutable count: State<int> voption = ValueNone
    let counter =
        Component.define "Counter" (fun ctx () ->
            let c = ctx.useState 0
            count <- ValueSome c
            TextBlock.create [ TextBlock.text $"{c.Current}" ])
    let platform, w, host = startApp [ App.view (counter ()) ]
    w.DeliverFrame() |> ignore
    onOtherThread (fun () -> count.Value.Set 5; count.Value.Update(fun c -> c + 1))
    Assert.True(platform.Wakes > 0)
    Assert.Equal(0, count.Value.Current) // not applied off the UI thread
    platform.RunWake()
    w.DeliverFrame() |> ignore
    Assert.Equal("6", firstText host.Tree.Root.Value)

[<Fact>]
let ``App.program's dispatch works from another thread`` () =
    let mutable dispatch: (int -> unit) voption = ValueNone
    let platform, w, host =
        startApp [ App.program 0 (fun (n: int) model -> model + n) (fun model d ->
                    dispatch <- ValueSome d
                    TextBlock.create [ TextBlock.text $"{model}" ]) ]
    w.DeliverFrame() |> ignore
    onOtherThread (fun () -> dispatch.Value 3; dispatch.Value 4)
    Assert.Equal("0", firstText host.Tree.Root.Value)
    platform.RunWake()
    w.DeliverFrame() |> ignore
    Assert.Equal("7", firstText host.Tree.Root.Value)

/// A component that loads `n` through a task it hands to the test, which completes it.
let private loaderOn (tree: ElementTree) =
    let loads = List<int * TaskCompletionSource<string> * CancellationToken>()
    let loader =
        Component.define "Loader" (fun ctx (n: int) ->
            let value =
                ctx.useAsync(n, fun n ct ->
                    let tcs = TaskCompletionSource<string>()
                    loads.Add((n, tcs, ct))
                    tcs.Task)
            let text =
                match value with
                | AsyncValue.Loading -> "loading"
                | AsyncValue.Loaded v -> v
                | AsyncValue.Failed e -> "failed: " + e.Message
            TextBlock.create [ TextBlock.text text ])
    let frame () =
        tree.UpdateLayout(Size.Create(200.0, 100.0))
        tree.RunAfterFrame()
    loader, loads, frame

[<Fact>]
let ``useAsync loads after the frame and shows the result`` () =
    let tree = ElementTree(textSystem ())
    let loader, loads, frame = loaderOn tree
    tree.Update(loader 1)
    Assert.Empty loads // starts after the frame
    frame ()
    Assert.Equal("loading", firstText tree.Root.Value)
    let _, tcs, _ = Assert.Single loads
    tcs.SetResult "one"
    frame ()
    Assert.Equal("one", firstText tree.Root.Value)

[<Fact>]
let ``useAsync cancels the old load when dependencies change and ignores its late result`` () =
    let tree = ElementTree(textSystem ())
    let loader, loads, frame = loaderOn tree
    tree.Update(loader 1)
    frame ()
    tree.Update(loader 2)
    frame ()
    Assert.Equal(2, loads.Count)
    let _, first, firstToken = loads[0]
    let _, second, _ = loads[1]
    Assert.True firstToken.IsCancellationRequested
    second.SetResult "two"
    first.SetResult "one (late)"
    frame ()
    Assert.Equal("two", firstText tree.Root.Value)

[<Fact>]
let ``useAsync ignores an old load that finishes between a dependency change and the next frame`` () =
    let tree = ElementTree(textSystem ())
    let loader, loads, frame = loaderOn tree
    tree.Update(loader 1)
    frame ()
    tree.Update(loader 2) // the component runs with the new deps; the new load starts after the frame
    let _, first, firstToken = loads[0]
    Assert.True firstToken.IsCancellationRequested // cancelled at once, not after the frame
    first.SetResult "one (late)"
    frame ()
    Assert.Equal("loading", firstText tree.Root.Value)
    let _, second, _ = loads[1]
    second.SetResult "two"
    frame ()
    Assert.Equal("two", firstText tree.Root.Value)

[<Fact>]
let ``useAsync reports failures and cancels when the component goes away`` () =
    let tree = ElementTree(textSystem ())
    let loader, loads, frame = loaderOn tree
    let view (items: int list) = StackPanel.create [ StackPanel.children [ for i in items -> loader i ] ]
    tree.Update(view [ 1; 2 ])
    frame ()
    let _, failing, _ = loads[0]
    failing.SetException(InvalidOperationException "offline")
    frame ()
    Assert.Equal("failed: offline", firstText tree.Root.Value.Children[0])
    let _, _, secondToken = loads[1]
    tree.Update(view [ 1 ])
    Assert.True secondToken.IsCancellationRequested

[<Fact>]
let ``useAsync takes an F# async too, and its result comes back through the dispatcher`` () =
    let loader =
        Component.define "AsyncLoader" (fun ctx (n: int) ->
            let value =
                ctx.useAsync(n, fun n _ ->
                    async {
                        do! Async.Sleep 10
                        return n * 2
                    })
            TextBlock.create [ TextBlock.text (match value with AsyncValue.Loaded v -> string v | _ -> "...") ])
    let platform, w, host = startApp [ App.view (loader 21) ]
    w.DeliverFrame() |> ignore
    until platform w (fun () -> firstText host.Tree.Root.Value = "42")

[<Fact>]
let ``useElmishWithCmd runs the init command, then commands in order`` () =
    let log = List<string>()
    let update msg (model: string list) =
        log.Add msg
        let model = model @ [ msg ]
        match msg with
        | "start" -> model, Cmd.batch [ Cmd.ofMsg "a"; Cmd.ofMsg "b" ]
        | _ -> model, Cmd.none
    let mutable shown = []
    let elmish =
        Component.define "Elmish" (fun ctx () ->
            let model, _ = ctx.useElmishWithCmd(([], Cmd.ofMsg "start"), update)
            shown <- model
            TextBlock.create [ TextBlock.text (String.Join(",", model)) ])
    let tree = ElementTree(textSystem ())
    tree.Update(elmish ())
    Assert.Empty log // the init command runs after the first frame
    tree.UpdateLayout(Size.Create(100.0, 100.0))
    tree.RunAfterFrame()
    tree.UpdateLayout(Size.Create(100.0, 100.0))
    Assert.Equal<string list>([ "start"; "a"; "b" ], List.ofSeq log)
    Assert.Equal("start,a,b", firstText tree.Root.Value)

type private Msg =
    | Load
    | Loaded of int
    | LoadFailed of string
    | Boom

[<Fact>]
let ``App.programWithCmd runs tasks and routes errors to App.onError`` () =
    let errors = List<exn>()
    let gate = TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)
    let update msg (model: string) =
        match msg with
        | Load -> "loading", Cmd.ofTask (fun () -> gate.Task) () Loaded (fun e -> LoadFailed e.Message)
        | Loaded n -> $"loaded {n}", Cmd.none
        | LoadFailed m -> "failed " + m, Cmd.none
        | Boom -> model, Cmd.ofSub (fun _ -> failwith "boom")
    let mutable dispatch: (Msg -> unit) voption = ValueNone
    let platform, w, host =
        startApp [
            App.programWithCmd ("idle", Cmd.ofMsg Load) update (fun model d ->
                dispatch <- ValueSome d
                TextBlock.create [ TextBlock.text model ])
            App.onError (fun _ e -> errors.Add e)
        ]
    w.DeliverFrame() |> ignore
    Assert.Equal("loading", firstText host.Tree.Root.Value)
    gate.SetResult 7
    until platform w (fun () -> firstText host.Tree.Root.Value = "loaded 7")
    // A command that throws, and work posted to the UI thread that throws: both reported, and
    // the app keeps going.
    dispatch.Value Boom
    host.Dispatcher.Value.Post(fun () -> failwith "posted boom")
    platform.RunWake()
    Assert.Equal<string list>([ "boom"; "posted boom" ], [ for e in errors -> e.Message ])
    dispatch.Value(Loaded 8)
    w.DeliverFrame() |> ignore
    Assert.Equal("loaded 8", firstText host.Tree.Root.Value)
