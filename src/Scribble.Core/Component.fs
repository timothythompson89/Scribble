namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Rendering

/// One hook's storage in a component, at its position in the hook order.
[<AbstractClass>]
type internal HookSlot(kind: string) =
    member _.Kind = kind

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

    let depth () =
        let mutable d = 0
        let mutable p = element.Parent
        while p.IsSome do
            d <- d + 1
            p <- p.Value.Parent
        d

    member _.Name = name
    member _.Now = element.Time

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

    /// Runs `action` after the frame that drew this run (effects).
    member _.Queue(action: unit -> unit) = pending.Add action

    member this.Flush() =
        flushScheduled <- false
        if not removed && pending.Count > 0 then
            let actions = pending.ToArray()
            pending.Clear()
            for action in actions do
                action ()

    member this.ScheduleFlush() =
        if pending.Count > 0 && not flushScheduled then
            match element.Root with
            | ValueSome r ->
                flushScheduled <- true
                r.AfterFrame this.Flush
            | ValueNone -> ()

    member this.Run(render: unit -> View) : View =
        running <- true
        index <- 0
        dirty <- false
        let view =
            try
                render ()
            finally
                running <- false
        if not firstRun && index < slots.Count then
            invalidOp $"Component '{name}' called fewer hooks than last time ({index} instead of {slots.Count}). Call hooks unconditionally, in the same order every time."
        firstRun <- false
        this.ScheduleFlush()
        view

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
/// equal value changes nothing).
[<Sealed>]
type State<'T> internal (initial: 'T, instance: ComponentInstance) =
    let mutable value = initial
    member _.Current = value

    member _.Set(next: 'T) =
        if not (EqualityComparer<'T>.Default.Equals(value, next)) then
            instance.Invalidate()
            value <- next

    /// Sets the state from its current value: `count.Update(fun c -> c + 1)`. Use it in handlers
    /// that may run after other changes, so they never work from a stale value.
    member this.Update(change: 'T -> 'T) = this.Set(change value)

    override _.ToString() = string (box value)

[<Sealed>]
type internal StateSlot<'T>(state: State<'T>) =
    inherit HookSlot("useState")
    member _.State = state

[<Sealed>]
type internal MemoSlot<'D, 'T>() =
    inherit HookSlot("useMemo")
    member val Deps = Unchecked.defaultof<'D> with get, set
    member val Value = Unchecked.defaultof<'T> with get, set
    member val HasValue = false with get, set

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
    member _.useState<'T>(initial: 'T) : State<'T> =
        (instance.Slot("useState", fun () -> StateSlot<'T>(State<'T>(initial, instance)))).State

    /// A value computed from `deps`, computed again only when `deps` change (F# equality):
    /// `ctx.useMemo(items, fun items -> List.sortBy _.Name items)`.
    member _.useMemo<'D, 'T>(deps: 'D, compute: 'D -> 'T) : 'T =
        let slot = instance.Slot("useMemo", fun () -> MemoSlot<'D, 'T>())
        if not slot.HasValue || not (EqualityComparer<'D>.Default.Equals(slot.Deps, deps)) then
            slot.Value <- compute deps
            slot.Deps <- deps
            slot.HasValue <- true
        slot.Value

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
        let state = this.useState init
        state.Current, (fun msg -> state.Update(update msg))

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
///         Button.create [ Button.text $"{count.Current}"; Button.onClick (fun () -> count.Update((+) 1)) ])
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
