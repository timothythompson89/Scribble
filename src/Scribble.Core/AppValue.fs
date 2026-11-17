namespace Scribble.Core

open System
open System.Collections.Generic
open System.Threading

module internal AppValueIds =
    let private counter = ref 0
    let next () = Interlocked.Increment(&counter.contents)

/// A piece of app-wide state, declared once and used anywhere: `let compactMode = AppValue.define
/// "CompactMode" false`. Components read and set it with `ctx.useAppValue compactMode`; code outside
/// them through `AppContext` (`Get`, `Set`) or `Cmd.setAppValue`. Each app has its own value (one
/// store per app, shared by its windows and layers); until something sets it, it's `Default`.
///
/// For UI-wide things (a compact layout, the selected section, the signed-in user's name; the window's
/// theme is one, `Theme.current`). The app's
/// data belongs in its Elmish model; the two can be used together.
[<Sealed>]
type AppValue<'T> internal (name: string, defaultValue: 'T) =
    let id = AppValueIds.next ()
    /// The name shown by the inspector.
    member _.Name = name
    /// The value until something sets it.
    member _.Default = defaultValue
    member internal _.Id = id
    override _.ToString() = name

module AppValue =
    /// Declares an app value: a name (for the inspector) and its default. Declare each one once,
    /// at module level (it's recognized by reference).
    let define (name: string) (defaultValue: 'T) : AppValue<'T> = AppValue<'T>(name, defaultValue)

/// An app's app values (`App.run` makes one, shared by its windows and layers; a bare
/// `ElementTree` has its own). Read on the UI thread; `Set` and `Update` work from any thread
/// (queued to the UI thread). Setting an equal value (F# equality) changes nothing.
[<Sealed>]
type AppValues() =
    let values = Dictionary<int, obj>()
    let listeners = Dictionary<int, List<unit -> unit>>()

    /// Where sets from other threads go (`App.run` sets it); without one they run where they're made.
    member val internal Dispatcher: Dispatcher voption = ValueNone with get, set

    /// The scrollers of named ScrollViewers and ItemsViews (their `name` setting), registered at
    /// layout, for `Cmd.scrollTo`. Weak: a removed element's scroller goes away with it.
    member val internal NamedScrollers = Dictionary<string, WeakReference<Scroller>>() with get

    /// Registers a named scroller; called at every arrange, so it allocates only when it changes.
    member internal this.RegisterScroller(name: string, scroller: Scroller) =
        match this.NamedScrollers.TryGetValue name with
        | true, existing when (match existing.TryGetTarget() with | true, s -> obj.ReferenceEquals(s, scroller) | _ -> false) -> ()
        | _ -> this.NamedScrollers[name] <- WeakReference<Scroller>(scroller)

    member _.Get<'T>(value: AppValue<'T>) : 'T =
        match values.TryGetValue value.Id with
        | true, v -> unbox<'T> v
        | _ -> value.Default

    member private this.SetNow<'T>(value: AppValue<'T>, next: 'T) =
        if not (EqualityComparer<'T>.Default.Equals(this.Get value, next)) then
            values[value.Id] <- box next
            match listeners.TryGetValue value.Id with
            | true, l ->
                for listener in l.ToArray() do
                    listener ()
            | _ -> ()

    member private this.OnUiThread(action: unit -> unit) =
        match this.Dispatcher with
        | ValueSome d when not (d.CheckAccess()) -> d.Post action
        | _ -> action ()

    member this.Set<'T>(value: AppValue<'T>, next: 'T) : unit = this.OnUiThread(fun () -> this.SetNow(value, next))

    /// Sets it from its current value (on the UI thread, with the value current there).
    member this.Update<'T>(value: AppValue<'T>, change: 'T -> 'T) : unit =
        this.OnUiThread(fun () -> this.SetNow(value, change (this.Get value)))

    /// Calls `listener` whenever the value changes, until the result is disposed.
    member internal _.Subscribe(id: int, listener: unit -> unit) : IDisposable =
        let l =
            match listeners.TryGetValue id with
            | true, l -> l
            | _ ->
                let l = List<unit -> unit>()
                listeners[id] <- l
                l
        l.Add listener
        { new IDisposable with
            member _.Dispose() = l.Remove listener |> ignore }

    /// Calls `listener` with the new value whenever it changes (on the UI thread), until the result
    /// is disposed. For code outside components (components use `ctx.useAppValue`).
    member this.Subscribe<'T>(value: AppValue<'T>, listener: 'T -> unit) : IDisposable =
        this.Subscribe(value.Id, fun () -> listener (this.Get value))

/// The window's title (the tab's title in the browser), an app value.
module WindowTitle =
    /// The window's title: `App.title "Counter"` sets its starting value (default "Scribble");
    /// change it from anywhere with `ctx.useAppValue WindowTitle.current` (`.Set "Counter – Styles"`),
    /// `AppContext.Set(WindowTitle.current, t)` or `Cmd.setAppValue WindowTitle.current t`.
    /// `App.showStats` adds its numbers after it.
    let current: AppValue<string> = AppValue.define "WindowTitle" "Scribble"
