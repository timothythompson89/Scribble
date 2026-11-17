namespace Scribble.Core

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Runtime.ExceptionServices
open System.Threading
open System.Threading.Tasks

/// Runs work on the UI thread. Anything may `Post` from any thread; the platform wakes the UI
/// thread, which runs the queue in order (`RunPending`). App code rarely needs it directly: state
/// setters and Elmish dispatch use it when called from another thread, and `await` inside an app
/// resumes on the UI thread through `DispatcherSynchronizationContext`.
[<Sealed>]
type Dispatcher private (wake: unit -> unit, after: (TimeSpan -> (unit -> unit) -> unit) voption) =
    let queue = ConcurrentQueue<unit -> unit>()
    let threadId = Environment.CurrentManagedThreadId
    /// 1 while a wake is on its way (so a burst of posts wakes the platform once).
    let mutable wakePending = 0
    /// Without App.onError, an error ends the app: rethrown here, on the UI thread.
    let mutable onError: exn -> unit = fun e -> ExceptionDispatchInfo.Throw e
    /// For UI errors (ReportUiError): the last one reported, and whether a frame has failed since
    /// the last frame ended.
    let mutable lastUiError = ""
    let mutable uiErrorSinceFrame = false
    let report (e: exn) =
#if DEBUG
        Debug.WriteLine("Scribble: unhandled error: " + string e)
#endif
        onError e

    /// A dispatcher whose delayed work (`PostAfter`) waits on the platform's timer: `after delay
    /// action` runs `action` on the UI thread once, `delay` from now (`IPlatform.After`).
    new(wake: unit -> unit, after: TimeSpan -> (unit -> unit) -> unit) = Dispatcher(wake, ValueSome after)

    /// A dispatcher whose delayed work waits on a .NET timer (real time), for hosts without a
    /// platform. App.start uses the platform's timer instead.
    new(wake: unit -> unit) = Dispatcher(wake, ValueNone)

    /// True on the UI thread (the thread that created the dispatcher).
    member _.CheckAccess() = Environment.CurrentManagedThreadId = threadId

    /// Queues `action` to run on the UI thread, after what's already queued. Safe from any thread.
    member _.Post(action: unit -> unit) =
        queue.Enqueue action
        if Interlocked.Exchange(&wakePending, 1) = 0 then wake ()

    /// Queues `action` to run on the UI thread `delay` from now, on the platform's clock (a test
    /// platform moves it only when the test says). Safe from any thread; errors go to OnError.
    member this.PostAfter(delay: TimeSpan, action: unit -> unit) =
        let guarded () =
            try
                action ()
            with e ->
                report e
        match after with
        | ValueSome after when this.CheckAccess() -> after delay guarded
        | ValueSome after -> this.Post(fun () -> after delay guarded)
        | ValueNone -> Task.Delay(delay).ContinueWith(fun (_: Task) -> this.Post guarded) |> ignore

    /// Runs `work` on the UI thread and returns its result. Safe from any thread (on the UI thread
    /// it still queues, so it never runs re-entrantly).
    member this.InvokeAsync<'T>(work: unit -> 'T) : Task<'T> =
        let result = TaskCompletionSource<'T>(TaskCreationOptions.RunContinuationsAsynchronously)
        this.Post(fun () ->
            try
                result.SetResult(work ())
            with e ->
                result.SetException e)
        result.Task

    /// Where errors from app code go (App.onError sets it): posted work, async work, event
    /// handlers, views, components, render functions. By default they're rethrown on the UI thread,
    /// which ends the app.
    member _.OnError
        with get () = onError
        and set v = onError <- v

    /// Runs everything queued (the platform calls this on the UI thread after a wake; a host that
    /// owns the loop, such as a game engine, can call it every tick). Work posted meanwhile runs too.
    member _.RunPending() =
        Volatile.Write(&wakePending, 0)
        let mutable action = Unchecked.defaultof<unit -> unit>
        while queue.TryDequeue(&action) do
            try
                action ()
            with e ->
                report e

    /// Routes an exception to OnError from any thread.
    member this.ReportError(e: exn) =
        if this.CheckAccess() then report e else this.Post(fun () -> report e)

    /// An error from app code during input or a frame (a handler, a view, a component, a render
    /// function): reported like any other, except that one identical to the last (type, message,
    /// place) is not reported again until a frame ends without errors, so a render that fails every
    /// frame of an animation is reported once.
    member internal _.ReportUiError(e: exn) =
        uiErrorSinceFrame <- true
        let trace = match e.StackTrace with null -> "" | s -> s
        let place = match trace.IndexOf '\n' with -1 -> trace | i -> trace.Substring(0, i)
        let key = e.GetType().FullName + "|" + e.Message + "|" + place
        if key <> lastUiError then
            lastUiError <- key
            report e

    /// A frame has ended: if it had no errors, the next UI error is reported even if it repeats.
    member internal _.FrameEnded() =
        if not uiErrorSinceFrame then lastUiError <- ""
        uiErrorSinceFrame <- false

/// Makes `await` (in `task { }`, and `async` started on the UI thread) come back to the UI thread.
/// `App.run` installs it with `Dispatcher.Install`.
[<Sealed>]
type DispatcherSynchronizationContext(dispatcher: Dispatcher) =
    inherit SynchronizationContext()
    member _.Dispatcher = dispatcher
    override _.Post(callback, state) = dispatcher.Post(fun () -> callback.Invoke state)

    override _.Send(callback, state) =
        if dispatcher.CheckAccess() then callback.Invoke state
        else dispatcher.InvokeAsync(fun () -> callback.Invoke state).Wait()

    override this.CreateCopy() = this

[<AutoOpen>]
module DispatcherInstall =
    type Dispatcher with
        /// Makes this the UI thread's dispatcher for the whole process: `await` on this thread
        /// resumes here, and exceptions from tasks nobody awaited go to its OnError. Call it on the
        /// UI thread before running the loop (App.run does on every platform; custom hosts may).
        member dispatcher.Install() =
            if not (dispatcher.CheckAccess()) then invalidOp "Install the dispatcher on its own (UI) thread."
            SynchronizationContext.SetSynchronizationContext(DispatcherSynchronizationContext dispatcher)
            TaskScheduler.UnobservedTaskException.Add(fun e ->
                e.SetObserved()
                dispatcher.ReportError(e.Exception.GetBaseException()))
