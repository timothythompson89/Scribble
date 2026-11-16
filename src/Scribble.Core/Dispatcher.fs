namespace Scribble.Core

open System
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

/// Runs work on the UI thread. Anything may `Post` from any thread; the platform wakes the UI
/// thread, which runs the queue in order (`RunPending`). App code rarely needs it directly: state
/// setters and Elmish dispatch use it when called from another thread, and `await` inside an app
/// resumes on the UI thread through `DispatcherSynchronizationContext`.
[<Sealed>]
type Dispatcher(wake: unit -> unit) =
    let queue = ConcurrentQueue<unit -> unit>()
    let threadId = Environment.CurrentManagedThreadId
    /// 1 while a wake is on its way (so a burst of posts wakes the platform once).
    let mutable wakePending = 0
    let mutable onError: exn -> unit =
        fun e -> Console.Error.WriteLine("Scribble: unhandled error in work posted to the UI thread: " + string e)

    /// True on the UI thread (the thread that created the dispatcher).
    member _.CheckAccess() = Environment.CurrentManagedThreadId = threadId

    /// Queues `action` to run on the UI thread, after what's already queued. Safe from any thread.
    member _.Post(action: unit -> unit) =
        queue.Enqueue action
        if Interlocked.Exchange(&wakePending, 1) = 0 then wake ()

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

    /// Where exceptions from posted work go (App.onError sets it). By default they're written to
    /// the console and the app keeps running.
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
                onError e

    /// Routes an exception to OnError from any thread.
    member this.ReportError(e: exn) =
        if this.CheckAccess() then onError e else this.Post(fun () -> onError e)

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
