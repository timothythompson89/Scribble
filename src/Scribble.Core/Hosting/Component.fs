namespace Scribble.Core

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Scribble.Rendering

/// One hook's storage in a component, at its position in the hook order.
[<AbstractClass>]
type internal HookSlot(kind: string) =
    member _.Kind = kind
    /// The current value and status, for `Element.Hooks`.
    abstract Inspect: now: TimeSpan -> struct (obj voption * string)
    default _.Inspect _ = struct (ValueNone, "")

/// A component's hooks and lifecycle, kept on the element that hosts it.
[<Sealed>]
type internal ComponentInstance(element: Element, name: string) =
    inherit ComponentState()
    let slots = List<HookSlot>()
    let pending = List<unit -> unit>()
    let mutable index = 0
    let mutable firstRun = true
    let mutable running = false
    let mutable dirty = false
    let mutable removed = false
    let mutable flushScheduled = false
    /// What the last successful run returned, shown again when a run fails.
    let mutable lastView: View voption = ValueNone

    let depth () =
        let mutable d = 0
        let mutable p = element.Parent
        while p.IsSome do
            d <- d + 1
            p <- p.Value.Parent
        d

    override _.Name = name

    override _.Hooks(now: TimeSpan) =
        [ for s in slots ->
              let struct (value, status) = s.Inspect now
              { Kind = s.Kind; Value = value; Status = status } ]
    member _.Now = element.Time

    /// Runs `action` on the UI thread: now if this is it (or there's no dispatcher, as in bare
    /// trees), else queued through the dispatcher. Safe from any thread.
    member _.OnUiThread(action: unit -> unit) =
        match element.Root with
        | ValueSome r ->
            match r.Dispatcher with
            | ValueSome d when not (d.CheckAccess()) -> d.Post action
            | _ -> action ()
        | ValueNone -> action ()

    /// Reports an error from async work or a command (App.onError); rethrows without a dispatcher.
    member _.ReportError(e: exn) =
        match element.Root with
        | ValueSome r when r.Dispatcher.IsSome -> r.Dispatcher.Value.ReportError e
        | _ -> raise e

    member this.Schedule(r: IElementRoot) = r.ScheduleComponent(depth (), this.RerunIfDirty)

    member _.RerunIfDirty() =
        if dirty && not removed && element.Root.IsSome then element.RunComponent()

    /// State changed: run again on the next frame.
    member this.Invalidate() =
        element.VerifyAccess()
        if running then
            invalidOp $"Component '{name}' set state while it was running. Set state in event handlers and effects, not in the component function."
        if not removed && not dirty then
            dirty <- true
            match element.Root with
            | ValueSome r -> this.Schedule r
            | ValueNone -> ()

    /// Re-runs after this frame (an animation still moving).
    member this.InvalidateAfterFrame() =
        match element.Root with
        | ValueSome r -> r.AfterFrame(fun () -> this.Invalidate())
        | ValueNone -> ()

    /// The hook at the current position, created on the first run. Later runs must ask for the
    /// same hooks in the same order.
    member _.Slot<'S when 'S :> HookSlot>(kind: string, create: unit -> 'S) : 'S =
        let i = index
        index <- index + 1
        if i < slots.Count then
            match slots[i] with
            | :? 'S as s -> s
            | other ->
                invalidOp $"Component '{name}' called its hooks in a different order: hook {i + 1} was {other.Kind} last time, now {kind}. Call hooks unconditionally, in the same order every time."
        elif firstRun then
            let s = create ()
            slots.Add s
            s
        else
            invalidOp $"Component '{name}' called more hooks than last time (hook {i + 1}, {kind}). Call hooks unconditionally, in the same order every time."

    /// Puts `next` in place of the hook just asked for (`Slot`).
    member _.ReplaceSlot<'S when 'S :> HookSlot>(_old: 'S, next: 'S) : 'S =
        slots[index - 1] <- next
        next

    /// The app values of the tree it's in.
    member _.AppValues: AppValues =
        match element.Root with
        | ValueSome r -> r.AppValues
        | ValueNone ->
            // Its first run: the element hasn't joined the tree yet, which is being built.
            match Element.Realizing with
            | ValueSome r -> r.AppValues
            | ValueNone -> invalidOp $"Component '{name}' used an app value outside a tree."

    /// The tree it's in (or is being built in), when it's an ElementTree (a host's layer, or a bare tree).
    member _.Tree: ElementTree voption =
        let root =
            match element.Root with
            | ValueSome r -> ValueSome r
            | ValueNone -> Element.Realizing
        match root with
        | ValueSome(:? ElementTree as tree) -> ValueSome tree
        | _ -> ValueNone

    /// What the host of its tree offers (its toasts and dialogs); none in a tree no host runs.
    member this.Services: HostServices voption = this.Tree |> ValueOption.bind (fun t -> HostServices.Of t.Services)

    /// Runs `action` after the frame that drew this run (effects).
    member _.Queue(action: unit -> unit) = pending.Add action

    member this.Flush() =
        flushScheduled <- false
        if not removed && pending.Count > 0 then
            let actions = pending.ToArray()
            pending.Clear()
            for action in actions do
                try
                    action ()
                with e ->
                    match element.Root with
                    | ValueSome r -> r.ReportError e
                    | ValueNone -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw e

    member this.ScheduleFlush() =
        if pending.Count > 0 && not flushScheduled then
            match element.Root with
            | ValueSome r ->
                flushScheduled <- true
                r.AfterFrame this.Flush
            | ValueNone -> ()

    /// Runs the component function. If it throws (or breaks the hook rules), the error goes to
    /// App.onError, what the run added (hooks on a first run, effects) is taken back, and the
    /// component keeps showing its last output (nothing, on a first run).
    member this.Run(render: unit -> View) : View =
        running <- true
        index <- 0
        dirty <- false
        let slotsBefore = slots.Count
        let pendingBefore = pending.Count
        let result =
            try
                try
                    let view = render ()
                    if not firstRun && index < slots.Count then
                        invalidOp $"Component '{name}' called fewer hooks than last time ({index} instead of {slots.Count}). Call hooks unconditionally, in the same order every time."
                    ValueSome view
                with e ->
                    for i in slots.Count - 1 .. -1 .. slotsBefore do
                        match box slots[i] with
                        | :? IDisposable as d -> d.Dispose()
                        | _ -> ()
                        slots.RemoveAt i
                    pending.RemoveRange(pendingBefore, pending.Count - pendingBefore)
                    match element.Root with
                    | ValueSome r -> r.ReportError e
                    | ValueNone ->
                        match Element.Realizing with
                        | ValueSome r -> r.ReportError e
                        | ValueNone -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw e
                    ValueNone
            finally
                running <- false
        match result with
        | ValueSome view ->
            firstRun <- false
            lastView <- ValueSome view
            this.ScheduleFlush()
            view
        | ValueNone ->
            match lastView with
            | ValueSome view -> view
            | ValueNone -> Border.create []

    override this.Attached(r: IElementRoot) =
        if dirty then this.Schedule r
        this.ScheduleFlush()

    /// The element left the tree: the component is gone. Subscriptions are disposed.
    override _.Detached() =
        removed <- true
        pending.Clear()
        for s in slots do
            match box s with
            | :? IDisposable as d -> d.Dispose()
            | _ -> ()

/// A piece of state from `ctx.useState`. Setting it re-runs the component on the next frame (an
/// equal value changes nothing). Set and Update work from any thread: off the UI thread they're
/// queued to it. Read `Current` on the UI thread.
/// `ctx.useAppValue` gives the same type, backed by the app's value instead of the component.
[<Sealed>]
type State<'T> internal (read: unit -> 'T, set: 'T -> unit, onUiThread: (unit -> unit) -> unit) =

    member _.Current = read ()

    member _.Set(next: 'T) = onUiThread (fun () -> set next)

    /// Sets the state from its current value: `count.Update(fun c -> c + 1)`. Use it in handlers
    /// that may run after other changes, so they never work from a stale value (from another
    /// thread, `change` runs on the UI thread with the value current there).
    member _.Update(change: 'T -> 'T) = onUiThread (fun () -> set (change (read ())))

    override _.ToString() = string (box (read ()))

    /// A component's own state.
    static member internal Local(initial: 'T, instance: ComponentInstance) =
        let value = ref initial
        let set (next: 'T) =
            if not (EqualityComparer<'T>.Default.Equals(value.Value, next)) then
                instance.Invalidate()
                value.Value <- next
        State<'T>((fun () -> value.Value), set, instance.OnUiThread)

/// `ctx.useAppValue`: subscribed to the app's value while the component lives (from its first run,
/// so a change before its effects run isn't missed).
[<Sealed>]
type internal AppValueSlot<'T>(value: AppValue<'T>, values: AppValues, instance: ComponentInstance) =
    inherit HookSlot($"useAppValue {value.Name}")
    let subscription = values.Subscribe(value.Id, fun () -> instance.Invalidate())
    member _.Value = value
    member val State = State<'T>((fun () -> values.Get value), (fun next -> values.Set(value, next)), instance.OnUiThread)
    override _.Inspect _ = struct (ValueSome(box (values.Get value)), "")
    interface IDisposable with
        member _.Dispose() = subscription.Dispose()

[<Sealed>]
type internal StateSlot<'T>(kind: string, state: State<'T>) =
    inherit HookSlot(kind)
    member _.State = state
    override _.Inspect _ = struct (ValueSome(box state.Current), "")

/// `ctx.useDialog`: the dialog it keeps; it closes when the component goes away.
[<Sealed>]
type internal LiveDialogSlot(live: LiveDialog) =
    inherit HookSlot("useDialog")
    member _.Live = live
    override _.Inspect _ = struct (ValueSome(box (if live.IsOpen then "open" else "closed")), "")
    interface IDisposable with
        member _.Dispose() = live.Drop()

[<Sealed>]
type internal MemoSlot<'D, 'T>() =
    inherit HookSlot("useMemo")
    member val Deps = Unchecked.defaultof<'D> with get, set
    member val Value = Unchecked.defaultof<'T> with get, set
    member val HasValue = false with get, set
    override this.Inspect _ = struct ((if this.HasValue then ValueSome(box this.Value) else ValueNone), "")

[<Sealed>]
type internal EffectSlot<'D>(kind: string) =
    inherit HookSlot(kind)
    member val Deps = Unchecked.defaultof<'D> with get, set
    member val HasRun = false with get, set

    /// True (and remembers `deps`) the first time and whenever `deps` changed.
    member this.Changed(deps: 'D) =
        if this.HasRun && EqualityComparer<'D>.Default.Equals(this.Deps, deps) then false
        else
            this.HasRun <- true
            this.Deps <- deps
            true

[<Sealed>]
type internal SubscriptionSlot<'D>() =
    inherit HookSlot("useSubscription")
    let mutable current: IDisposable voption = ValueNone
    member val Effect = EffectSlot<'D>("useSubscription")

    member this.Replace(subscribe: unit -> IDisposable) =
        (this :> IDisposable).Dispose()
        current <- ValueSome(subscribe ())

    interface IDisposable with
        member _.Dispose() =
            match current with
            | ValueSome d ->
                current <- ValueNone
                d.Dispose()
            | ValueNone -> ()

[<Sealed>]
type internal AnimatedSlot<'T>(tween: Tween<'T>) =
    inherit HookSlot("useAnimated")
    member _.Tween = tween
    override _.Inspect now = struct (ValueSome(box (tween.ValueAt now)), "")

/// What `ctx.useAsync` has so far.
[<RequireQualifiedAccess>]
type AsyncValue<'T> =
    /// Started, not finished (also while reloading after the dependencies changed).
    | Loading
    | Loaded of value: 'T
    | Failed of error: exn

[<Sealed>]
type internal AsyncSlot<'D, 'T>() =
    inherit HookSlot("useAsync")
    let mutable cancel: CancellationTokenSource voption = ValueNone
    member val Effect = EffectSlot<'D>("useAsync")
    member val Value: AsyncValue<'T> = AsyncValue.Loading with get, set
    /// Bumped whenever a load starts or the component goes: a result from an older load is ignored.
    member val Version = 0 with get, set

    override this.Inspect _ =
        match this.Value with
        | AsyncValue.Loading -> struct (ValueNone, "loading")
        | AsyncValue.Loaded v -> struct (ValueSome(box v), "loaded")
        | AsyncValue.Failed e -> struct (ValueSome(box e), "failed")

    /// Cancels the running load (if any) and makes its result stale.
    member this.Stop() =
        this.Version <- this.Version + 1
        match cancel with
        | ValueSome c ->
            cancel <- ValueNone
            c.Cancel()
            c.Dispose()
        | ValueNone -> ()

    /// Starts a load (after stopping the previous one) and returns its token and version.
    member this.Start() =
        this.Stop()
        let c = new CancellationTokenSource()
        cancel <- ValueSome c
        struct (c.Token, this.Version)

    interface IDisposable with
        member this.Dispose() = this.Stop()

/// What a component function gets: its hooks. Call them unconditionally, in the same order
/// every time the component runs (not inside `if`, loops or handlers).
[<Sealed>]
type ComponentContext internal (instance: ComponentInstance) =

    member private _.Animated<'T>(target: 'T, duration: TimeSpan, easing: float -> float, lerp: 'T -> 'T -> float -> 'T) : 'T =
        let slot = instance.Slot("useAnimated", fun () -> AnimatedSlot<'T>(Tween<'T>(lerp, target)))
        let now = instance.Now
        let value = slot.Tween.Update(target, now, duration, easing)
        if slot.Tween.IsMoving now then instance.InvalidateAfterFrame()
        value

    /// State that survives re-runs: `let count = ctx.useState 0`, then `count.Current`,
    /// `count.Set 5`, `count.Update(fun c -> c + 1)`. `initial` is used on the first run only.
    member this.useState<'T>(initial: 'T) : State<'T> =
        this.State("useState", initial)

    /// A state hook under another name (useElmish keeps its model in one).
    member private _.State<'T>(kind: string, initial: 'T) : State<'T> =
        (instance.Slot(kind, fun () -> StateSlot<'T>(kind, State<'T>.Local(initial, instance)))).State

    /// An app value (`AppValue.define`), shared by every component that uses it: `let light =
    /// ctx.useAppValue compactMode`, then `compact.Current`, `compact.Set true`. The component re-runs
    /// when anything sets it (an equal value changes nothing).
    member _.useAppValue<'T>(value: AppValue<'T>) : State<'T> =
        let create () = new AppValueSlot<'T>(value, instance.AppValues, instance)
        let slot = instance.Slot("useAppValue", create)
        if obj.ReferenceEquals(slot.Value, value) then slot.State
        else
            // Another value at this place than last time: subscribe to that one instead.
            let replaced = instance.ReplaceSlot(slot, create ())
            (slot :> IDisposable).Dispose()
            replaced.State

    /// A value computed from `deps`, computed again only when `deps` change (F# equality):
    /// `ctx.useMemo(items, fun items -> List.sortBy _.Name items)`.
    member _.useMemo<'D, 'T>(deps: 'D, compute: 'D -> 'T) : 'T =
        let slot = instance.Slot("useMemo", fun () -> MemoSlot<'D, 'T>())
        if not slot.HasValue || not (EqualityComparer<'D>.Default.Equals(slot.Deps, deps)) then
            slot.Value <- compute deps
            slot.Deps <- deps
            slot.HasValue <- true
        slot.Value

    /// A scroller to scroll a ScrollViewer or an ItemsView from code: give it to the element
    /// (`ItemsView.scroller scroller`), then call it from handlers (`scroller.ScrollTo key`). The
    /// same scroller for as long as the component lives.
    member _.useScroller() : Scroller =
        let slot = instance.Slot("useScroller", fun () -> MemoSlot<unit, Scroller>())
        if not slot.HasValue then
            slot.Value <- Scroller()
            slot.HasValue <- true
        slot.Value

    /// A capture, to make an image of an element as it is on screen: give it to the element
    /// (`Border.capture capture`), then ask from handlers (`capture.ToImage []`). The same capture for as long as
    /// the component lives.
    member _.useCapture() : Capture =
        let slot = instance.Slot("useCapture", fun () -> MemoSlot<unit, Capture>())
        if not slot.HasValue then
            slot.Value <- Capture()
            slot.HasValue <- true
        slot.Value

    /// The system clipboard's text: `let clipboard = ctx.useClipboard ()`, then in a handler
    /// `clipboard.SetText link` (a Copy button) or `let! text = clipboard.GetText()`. The same clipboard for as
    /// long as the component lives.
    member _.useClipboard() : Clipboard =
        let slot = instance.Slot("useClipboard", fun () -> MemoSlot<unit, Clipboard>())
        if not slot.HasValue then
            slot.Value <- Clipboard()
            slot.HasValue <- true
        slot.Value

    /// The toasts of the window this component is in: `let toaster = ctx.useToaster ()`, then in a handler
    /// `toaster.Show "Saved"`. The same toaster for as long as the component lives; safe to call from a task.
    member _.useToaster() : Toaster =
        let slot = instance.Slot("useToaster", fun () -> MemoSlot<unit, Toaster>())
        if not slot.HasValue then
            slot.Value <-
                Toaster(
                    (fun () -> instance.Tree |> ValueOption.bind (fun t -> t.Dispatcher)),
                    fun () -> instance.Services |> ValueOption.bind (fun s -> s.FindToasts()))
            slot.HasValue <- true
        slot.Value

    /// The dialogs of the window this component is in: `let dialogs = ctx.useDialogs ()`, then in a
    /// handler `let! ok = dialogs.Confirm [ Confirm.title "Delete the draft?" ]` (also `Inform` and `Ask`).
    member _.useDialogs() : Dialogs =
        let slot = instance.Slot("useDialogs", fun () -> MemoSlot<unit, Dialogs>())
        if not slot.HasValue then
            slot.Value <-
                Dialogs(
                    (fun () -> instance.Tree |> ValueOption.bind (fun t -> t.Dispatcher)),
                    fun () -> instance.Services |> ValueOption.bind (fun s -> s.FindDialogs()))
            slot.HasValue <- true
        slot.Value

    /// A dialog with content of your own, declared here and opened from a handler:
    ///
    ///     let rename =
    ///         ctx.useDialog (fun close -> [
    ///             Dialog.title "Rename the file"
    ///             Dialog.content (renameForm name.Current (fun () -> dispatch (Rename name.Current); close ()))
    ///         ])
    ///     Button.onClick (fun _ -> rename ())
    ///
    /// `close ()` closes it (so does Escape, unless `Dialog.closeOnEscape false`). While it's open its content is
    /// this component's latest run's, so it follows the component's state. It opens in the component's window, one
    /// at a time with the window's other dialogs, and closes if the component goes away. For a modal whose
    /// being open belongs in the app's model, write a `Modal` in the view instead.
    member this.useDialog(settingsFor: (unit -> unit) -> DialogSetting list) : unit -> unit =
        let dialogs = this.useDialogs ()
        let slot = instance.Slot("useDialog", fun () -> new LiveDialogSlot(LiveDialog dialogs))
        let live = slot.Live
        live.Settings <- fun () -> settingsFor live.Close
        live.Refresh()
        live.Open

    /// A dialog of your own that depends on what was clicked (a row's item): the settings come from that value
    /// too, and the function it gives takes the value: `let edit = ctx.useDialog (fun (file: File) close -> [ ... ])`,
    /// then `Button.onClick (fun _ -> edit file)`.
    member this.useDialog<'A>(settingsFor: 'A -> (unit -> unit) -> DialogSetting list) : 'A -> unit =
        let dialogs = this.useDialogs ()
        let slot = instance.Slot("useDialog", fun () -> new LiveDialogSlot(LiveDialog dialogs))
        let live = slot.Live
        live.Settings <- fun () -> settingsFor (unbox<'A> live.Argument) live.Close
        live.Refresh()
        fun arg ->
            if not live.IsOpen then
                live.Argument <- box arg
                live.Open()

    /// A confirmation, declared here and opened from a handler:
    ///
    ///     let confirmDelete =
    ///         ctx.useConfirm [ Confirm.title "Delete the draft?"; Confirm.accept "Delete"
    ///                          Confirm.severity Severity.Critical
    ///                          Confirm.onAccept (fun () -> dispatch DeleteDraft) ]
    ///     Button.onClick (fun _ -> confirmDelete ())
    ///
    /// The settings are this run's, so they can follow the model. It opens in the component's window. For a
    /// sequence of questions, use the task forms of `ctx.useDialogs ()`.
    member this.useConfirm(settings: ConfirmSetting list) : unit -> unit =
        let dialogs = this.useDialogs ()
        fun () -> dialogs.Confirm settings |> ignore

    /// A confirmation that depends on what was clicked (a row's item): the settings come from that value, and so
    /// does the function it gives: `let confirmDelete = ctx.useConfirm (fun (file: File) -> [ Confirm.title
    /// ("Delete " + file.Name + "?"); Confirm.onAccept (fun () -> dispatch (Delete file.Id)) ])`, then
    /// `Button.onClick (fun _ -> confirmDelete file)`.
    member this.useConfirm<'A>(settingsFor: 'A -> ConfirmSetting list) : 'A -> unit =
        let dialogs = this.useDialogs ()
        fun arg -> dialogs.Confirm(settingsFor arg) |> ignore

    /// A choice, declared here and opened from a handler; each choice says what it does:
    ///
    ///     let askClose =
    ///         ctx.useAsk [ Ask.title "Unsaved changes"
    ///                      Ask.choice ("Save", fun () -> dispatch Save)
    ///                      Ask.choice ("Discard", fun () -> dispatch Discard)
    ///                      Ask.choice ("Cancel", ignore) ]
    ///     Button.onClick (fun _ -> askClose ())
    member this.useAsk(settings: AskSetting<unit -> unit> list) : unit -> unit =
        let dialogs = this.useDialogs ()
        fun () -> dialogs.Ask settings |> ignore

    /// A choice that depends on what was clicked: `ctx.useAsk (fun (file: File) -> [ ... ])` gives a function
    /// taking the file.
    member this.useAsk<'A>(settingsFor: 'A -> AskSetting<unit -> unit> list) : 'A -> unit =
        let dialogs = this.useDialogs ()
        fun arg -> dialogs.Ask(settingsFor arg) |> ignore

    /// Something to tell the user, declared here and opened from a handler:
    /// `let sessionExpired = ctx.useInform [ Inform.title "Your session has expired"; Inform.text "Sign in again." ]`,
    /// then `sessionExpired ()`. `Inform.onClosed` runs when it's closed.
    member this.useInform(settings: InformSetting list) : unit -> unit =
        let dialogs = this.useDialogs ()
        fun () -> dialogs.Inform settings |> ignore

    /// Something to tell the user that depends on what happened: `ctx.useInform (fun (e: exn) -> [ Inform.text
    /// e.Message ])` gives a function taking the error.
    member this.useInform<'A>(settingsFor: 'A -> InformSetting list) : 'A -> unit =
        let dialogs = this.useDialogs ()
        fun arg -> dialogs.Inform(settingsFor arg) |> ignore

    /// Runs `effect` after the frame that shows the component, the first time and whenever `deps`
    /// change (F# equality; use a tuple for several values, `()` for "once").
    member _.useEffect<'D>(deps: 'D, effect: 'D -> unit) : unit =
        let slot = instance.Slot("useEffect", fun () -> EffectSlot<'D>("useEffect"))
        if slot.Changed deps then instance.Queue(fun () -> effect deps)

    /// Like useEffect for things that must be undone: `subscribe` returns an IDisposable, which
    /// is disposed when `deps` change (before subscribing again) and when the component goes away.
    member _.useSubscription<'D>(deps: 'D, subscribe: 'D -> IDisposable) : unit =
        let slot = instance.Slot("useSubscription", fun () -> new SubscriptionSlot<'D>())
        if slot.Effect.Changed deps then instance.Queue(fun () -> slot.Replace(fun () -> subscribe deps))

    /// The Elmish pattern inside a component: `let model, dispatch = ctx.useElmish(init, update)`.
    /// A message that produces an equal model changes nothing.
    member this.useElmish<'Model, 'Msg>(init: 'Model, update: 'Msg -> 'Model -> 'Model) : 'Model * ('Msg -> unit) =
        let state = this.State("useElmish", init)
        state.Current, (fun msg -> state.Update(update msg))

    /// useElmish with commands: `init` and `update` return the model and a `Cmd` (messages to send,
    /// tasks to run). The init command runs after the first frame. `dispatch` works from any thread.
    member this.useElmishWithCmd<'Model, 'Msg>(init: 'Model * Cmd<'Msg>, update: 'Msg -> 'Model -> 'Model * Cmd<'Msg>) : 'Model * ('Msg -> unit) =
        let state = this.State("useElmish", fst init)
        let rec dispatch (msg: 'Msg) =
            instance.OnUiThread(fun () ->
                let model, cmd = update msg state.Current
                state.Set model
                Cmd.execute instance.ReportError instance.AppValues dispatch cmd)
        this.useEffect((), fun () -> Cmd.execute instance.ReportError instance.AppValues dispatch (snd init))
        state.Current, dispatch

    member private _.Async<'D, 'T>(deps: 'D, start: 'D -> CancellationToken -> Task<'T>) : AsyncValue<'T> =
        let slot = instance.Slot("useAsync", fun () -> new AsyncSlot<'D, 'T>())
        if slot.Effect.Changed deps then
            // Cancel the old load and make its result stale now, not when the new one starts after
            // the frame: a host may finish the old load in between.
            slot.Stop()
            slot.Value <- AsyncValue.Loading
            instance.Queue(fun () ->
                let struct (token, version) = slot.Start()
                let work =
                    try
                        start deps token
                    with e ->
                        Task.FromException<'T> e
                work.ContinueWith(
                    (fun (t: Task<'T>) ->
                        instance.OnUiThread(fun () ->
                            // A newer load started, or the component went away: drop this result.
                            if slot.Version = version then
                                slot.Value <-
                                    if t.IsCompletedSuccessfully then AsyncValue.Loaded t.Result
                                    elif t.IsFaulted then AsyncValue.Failed(t.Exception.GetBaseException())
                                    else AsyncValue.Failed(TaskCanceledException t)
                                instance.Invalidate())),
                    TaskContinuationOptions.ExecuteSynchronously
                )
                |> ignore)
        slot.Value

    /// Loads something asynchronously: `match ctx.useAsync(id, fun id ct -> api.Load(id, ct)) with`
    /// `AsyncValue.Loading | Loaded v | Failed e`. Starts after the frame that shows the component;
    /// when `deps` change it cancels `ct` and loads again (showing Loading); when the component goes
    /// it cancels. A late result from an older load is ignored even if the load ignores `ct`.
    member this.useAsync<'D, 'T>(deps: 'D, load: 'D -> CancellationToken -> Task<'T>) : AsyncValue<'T> =
        this.Async(deps, load)

    /// useAsync with an F# async (`ct` is also the async's cancellation token).
    member this.useAsync<'D, 'T>(deps: 'D, load: 'D -> CancellationToken -> Async<'T>) : AsyncValue<'T> =
        this.Async(deps, fun d ct -> Async.StartAsTask(load d ct, cancellationToken = ct))

    /// A number that moves to `target` over `duration` whenever `target` changes (the first one is
    /// taken at once). The component re-runs every frame while it moves.
    member this.useAnimated(target: float, duration: TimeSpan, ?easing: float -> float) : float =
        this.Animated(target, duration, defaultArg easing Easing.easeOut, Lerp.float)

    /// A color that fades to `target` over `duration` whenever `target` changes.
    member this.useAnimated(target: Color, duration: TimeSpan, ?easing: float -> float) : Color =
        this.Animated(target, duration, defaultArg easing Easing.easeOut, Lerp.color)

[<Sealed>]
type internal ComponentInput<'P>(name: string, render: ComponentContext -> 'P -> View, props: 'P) =
    inherit ComponentInput()
    member _.Props = props
    member _.Render = render

    override _.Run(element: Element) =
        let instance =
            match element.ComponentState with
            | ValueSome(:? ComponentInstance as i) -> i
            | _ ->
                let i = ComponentInstance(element, name)
                element.ComponentState <- ValueSome i
                i
        let ctx = ComponentContext instance
        instance.Run(fun () -> render ctx props)

    override _.Equals(other: obj) =
        match other with
        | :? ComponentInput<'P> as o -> obj.ReferenceEquals(render, o.Render) && EqualityComparer<'P>.Default.Equals(props, o.Props)
        | _ -> false

    override _.GetHashCode() = EqualityComparer<'P>.Default.GetHashCode props

/// Components: pieces of view with their own state (hooks).
///
///     let counter = Component.define "Counter" (fun ctx (start: int) ->
///         let count = ctx.useState start
///         Button.create [ Button.text $"{count.Current}"; Button.onClick (fun _ -> count.Update((+) 1)) ])
///
///     StackPanel.children [ counter 0; counter 10 ]
module Component =
    /// Defines a component: a function from props to a view, with hooks on `ctx`. Define each one
    /// once, at module level (it's recognized by reference, like element types). A component
    /// re-runs when its state changes, or when its parent re-runs with different props (F#
    /// equality); only its own part of the tree is patched. No props: `fun ctx () -> ...`.
    let define (name: string) (render: ComponentContext -> 'P -> View) : 'P -> View =
        let elementType = Element.define name []
        fun props -> View(elementType, [| struct (Element.ComponentInputProp :> Prop, box (ComponentInput<'P>(name, render, props))) |])
