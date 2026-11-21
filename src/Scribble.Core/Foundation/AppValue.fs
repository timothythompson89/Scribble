namespace Scribble.Core

open System
open System.Collections.Generic
open System.Globalization

/// Warnings for app authors, in Debug builds only.
module internal DebugOnly =
    let warn (key: string, message: string) =
#if DEBUG
        DebugWarnings.Once(key, message)
#else
        ignore key
        ignore message
#endif

/// How a stored value's text reads to a store that keeps types apart (a JSON file writes `true`, `0.5` and
/// `"Dark"`); a store of plain text (the browser's localStorage) ignores it.
[<RequireQualifiedAccess>]
type StoredKind =
    | Text
    | Number
    | Bool

/// Where an app's stored values (`StoredValue.bool`, `int`, `float`, `string`, `enum`) are kept between runs (DESIGN.md, "Stored values").
/// `App.run` gives the platform's: a JSON file in isolated storage on the desktop, localStorage in the browser.
/// Called on the UI thread. A store never throws for a value it can't read: it answers `ValueNone`.
type IValueStore =
    /// The text stored under `name`, if any.
    abstract TryRead: name: string -> string voption
    /// Keeps `text` for `name`; it may wait for `Flush` to make it last.
    abstract Write: name: string * kind: StoredKind * text: string -> unit
    /// Makes everything written last (called half a second after the last change, and when the app closes).
    abstract Flush: unit -> unit

/// A store that lasts as long as the process: what the unit tests use, and an app run without a platform's store.
[<Sealed>]
type MemoryValueStore() =
    let values = Dictionary<string, string>()
    /// How often `Flush` was called. For tests.
    member val Flushes = 0 with get, set
    /// What is stored, by name. For tests.
    member _.Values = values
    interface IValueStore with
        member _.TryRead name =
            match values.TryGetValue name with
            | true, v -> ValueSome v
            | _ -> ValueNone
        member _.Write(name, _, text) = values[name] <- text
        member this.Flush() = this.Flushes <- this.Flushes + 1

/// What an app started without a store has (`App.start` on its own; `App.run` gives the platform's): stored values
/// last for the run, and a Debug build says so once.
[<Sealed>]
type internal UnkeptValueStore() =
    let memory = MemoryValueStore() :> IValueStore
    let warn () =
        DebugOnly.warn (
            "StoredValue.unkept",
            "This app uses stored values but has no store for them, so they last for this run only. App.run (Scribble.Desktop, Scribble.Browser) gives one; with App.start, add App.valueStore.")
    interface IValueStore with
        member _.TryRead name =
            if not (name.StartsWith("Scribble.", StringComparison.Ordinal)) then warn ()
            memory.TryRead name
        member _.Write(name, kind, text) = memory.Write(name, kind, text)
        member _.Flush() = memory.Flush()

/// How a stored value becomes text and back.
[<NoEquality; NoComparison>]
type internal StoredCodec<'T> =
    { Kind: StoredKind
      ToText: 'T -> string
      OfText: string -> 'T voption }


/// A piece of app-wide state, declared once and used anywhere: `let compactMode = AppValue.define
/// "CompactMode" false`. Components read and set it with `ctx.useAppValue compactMode`; code outside
/// them through `AppContext` (`Get`, `Set`) or `Cmd.setAppValue`. Each app has its own value (one
/// store per app, shared by its windows and layers); until something sets it, it's `Default`.
///
/// For UI-wide things (a compact layout, the selected section, the signed-in user's name; the window's
/// theme is one, `Theme.current`). The app's
/// data belongs in its Elmish model; the two can be used together.
[<Sealed>]
type AppValue<'T> internal (name: string, defaultValue: 'T, codec: StoredCodec<'T> voption) =
    let id = Ids.NextAppValue()
    internal new(name: string, defaultValue: 'T) = AppValue<'T>(name, defaultValue, ValueNone)
    /// How it is stored, for a stored value (`StoredValue.bool`, `int`, `float`, `string`, `enum`).
    member internal _.Codec = codec
    /// True for a stored value (`StoredValue.bool`, `int`, `float`, `string`, `enum`): kept between runs under its name.
    member _.IsStored = codec.IsSome
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
    let mutable images: ImageCache = null
    let mutable store: IValueStore = Unchecked.defaultof<IValueStore>
    let storedNames = Dictionary<string, int>()
    let mutable changes = 0
    let mutable flushPending = false

    /// Where sets from other threads go (`App.run` sets it); without one they run where they're made.
    member val internal Dispatcher: Dispatcher voption = ValueNone with get, set

    /// The scrollers of named ScrollViewers and ItemsViews (their `name` setting), registered at
    /// layout, for `Cmd.scrollTo`. Weak: a removed element's scroller goes away with it.
    member val internal NamedScrollers = Dictionary<string, WeakReference<Scroller>>() with get

    /// What the host that runs these values offers commands (its toasts and dialogs): a `HostServices`,
    /// kept untyped because those types come later in the compile order.
    member val internal Services: obj = null with get, set

    /// The app's pictures (DESIGN.md, "Image state per app, not per process"): its cache, downloads and HTTP client,
    /// made on first use and shared by everything that shares these values. `ViewImage` in a tool gives a tree the
    /// headless environment's instead.
    member internal this.Images
        with get () : ImageCache =
            lock listeners (fun () ->
                if isNull images then images <- ImageCache.Create()
                images)
        and set (v: ImageCache) = lock listeners (fun () -> images <- v)

    /// Registers a named scroller; called at every arrange, so it allocates only when it changes.
    member internal this.RegisterScroller(name: string, scroller: Scroller) =
        match this.NamedScrollers.TryGetValue name with
        | true, existing when (match existing.TryGetTarget() with | true, s -> obj.ReferenceEquals(s, scroller) | _ -> false) -> ()
        | _ -> this.NamedScrollers[name] <- WeakReference<Scroller>(scroller)

    /// Where stored values are kept (`App.run` gives the platform's); a `MemoryValueStore` until one is given.
    member this.Store
        with get () : IValueStore =
            if isNull (box store) then store <- MemoryValueStore()
            store
        and set (v: IValueStore) = store <- v

    /// Stored values this app doesn't keep (`App.rememberTheme false`): they are plain app values here.
    member val internal Unkept = HashSet<int>() with get

    /// Gives a value for this run without telling listeners or the store (a built-in stored value whose stored
    /// text names nothing the app has: the stored text stays as it is until something sets the value).
    member internal _.Prime<'T>(value: AppValue<'T>, start: 'T) = values[value.Id] <- box start

    /// True when the store holds something under a stored value's name (the user has picked before).
    member internal this.HasStored<'T>(value: AppValue<'T>) : bool =
        value.IsStored
        && not (this.Unkept.Contains value.Id)
        && (try (this.Store.TryRead value.Name).IsSome with _ -> false)

    /// A stored value's first read: what the store has under its name, else its default. Nothing here throws into
    /// the app: a store that fails, or text that is not the value's type, gives the default and a Debug warning.
    member private this.Load<'T>(value: AppValue<'T>, codec: StoredCodec<'T>) : 'T =
        match storedNames.TryGetValue value.Name with
        | true, id when id <> value.Id ->
            DebugOnly.warn (
                "StoredValue.name " + value.Name,
                "Two stored values are named " + value.Name + ": they are kept under one name and overwrite each other. Define each stored value once, with a name of its own.")
        | _ -> storedNames[value.Name] <- value.Id
        let loaded =
            try
                match this.Store.TryRead value.Name with
                | ValueSome text ->
                    match codec.OfText text with
                    | ValueSome v -> v
                    | ValueNone ->
                        DebugOnly.warn (
                            "StoredValue.read " + value.Name,
                            "The stored value " + value.Name + " could not be read from what was stored (\"" + text + "\"): its default is used.")
                        value.Default
                | ValueNone -> value.Default
            with e ->
                DebugOnly.warn ("StoredValue.read " + value.Name, "The stored value " + value.Name + " could not be read (" + e.Message + "): its default is used.")
                value.Default
        values[value.Id] <- box loaded
        loaded

    /// A stored value changed: its text goes to the store now, and the store is flushed once no stored value has
    /// changed for half a second (a slider sets its value on every frame of a drag). Without a dispatcher (a bare
    /// tree) it is flushed at once.
    member private this.Keep<'T>(value: AppValue<'T>, codec: StoredCodec<'T>, next: 'T) =
        try
            this.Store.Write(value.Name, codec.Kind, codec.ToText next)
            changes <- changes + 1
            match this.Dispatcher with
            | ValueSome d ->
                if not flushPending then
                    flushPending <- true
                    let rec wait (seen: int) =
                        d.PostAfter(
                            TimeSpan.FromMilliseconds 500.0,
                            fun () ->
                                if not flushPending then ()
                                elif changes <> seen then wait changes
                                else this.FlushStore())
                    wait changes
            | ValueNone -> this.FlushStore()
        with e ->
            DebugOnly.warn ("StoredValue.write " + value.Name, "The stored value " + value.Name + " could not be stored (" + e.Message + ").")

    /// Makes what stored values hold now last (the app calls it when its window closes).
    member this.FlushStore() =
        flushPending <- false
        if not (isNull (box store)) then
            try
                store.Flush()
            with e ->
                DebugOnly.warn ("StoredValue.flush", "The stored values could not be saved (" + e.Message + ").")

    member this.Get<'T>(value: AppValue<'T>) : 'T =
#if DEBUG
        match this.Dispatcher with
        | ValueSome d when not (d.CheckAccess()) ->
            DebugOnly.warn (
                "AppValues.Get " + value.Name,
                "The app value " + value.Name + " was read off the UI thread, where it may be changing: read it on the UI thread (Set and Update are safe from any thread).")
        | _ -> ()
#endif
        match values.TryGetValue value.Id with
        | true, v -> unbox<'T> v
        | _ ->
            match value.Codec with
            | ValueSome codec when not (this.Unkept.Contains value.Id) -> this.Load(value, codec)
            | _ -> value.Default

    member private this.SetNow<'T>(value: AppValue<'T>, next: 'T) =
        if not (EqualityComparer<'T>.Default.Equals(this.Get value, next)) then
            values[value.Id] <- box next
            match value.Codec with
            | ValueSome codec when not (this.Unkept.Contains value.Id) -> this.Keep(value, codec, next)
            | _ -> ()
            match listeners.TryGetValue value.Id with
            | true, l ->
                // A listener may be app code (`Subscribe`): one that throws doesn't stop the others. Its error goes
                // to App.onError; without a dispatcher (a bare tree) the first is thrown once all have run.
                let mutable failed: exn voption = ValueNone
                for listener in l.ToArray() do
                    try
                        listener ()
                    with e ->
                        match this.Dispatcher with
                        | ValueSome d -> d.ReportError e
                        | ValueNone -> if failed.IsNone then failed <- ValueSome e
                match failed with
                | ValueSome e -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw e
                | ValueNone -> ()
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

    /// How many listen for `value`'s changes (components using it, hosts, `Subscribe`). For tests.
    member internal _.ListenerCount<'T>(value: AppValue<'T>) : int =
        match listeners.TryGetValue value.Id with
        | true, l -> l.Count
        | _ -> 0

    /// Calls `listener` with the new value whenever it changes (on the UI thread), until the result
    /// is disposed. For code outside components (components use `ctx.useAppValue`).
    member this.Subscribe<'T>(value: AppValue<'T>, listener: 'T -> unit) : IDisposable =
        this.Subscribe(value.Id, fun () -> listener (this.Get value))

/// A stored value: an app value that is also kept between runs (DESIGN.md, "Stored values"). Declare it once, at
/// module level, with the function for its type: `let compact = StoredValue.bool "Compact" false`. Use it as an
/// app value: `ctx.useStoredValue compact` in a component (`.Current`, `.Set true`), `AppContext.Get`/`Set` and
/// `Cmd.setAppValue` outside. It is read from the app's store the first time something asks for it (its default
/// when nothing is stored, or what is stored can't be read), and saved half a second after its last change and
/// when the app closes.
///
/// **Primitive values only:** `bool`, `int`, `float`, `string` and enums, each through its own function, so
/// another type doesn't compile. Something bigger is stored as its parts (a window's size as `WindowSizeX` and
/// `WindowSizeY`).
///
/// The name is what it is stored under: change it and the value starts again from its default. Where: a JSON file
/// in isolated storage on the desktop (found by the exe's path: a moved or reinstalled exe starts from the
/// defaults), localStorage in the browser; `App.storeName` names the file or prefixes the keys.
[<RequireQualifiedAccess>]
module StoredValue =
    let private make<'T> (name: string) (defaultValue: 'T) (kind: StoredKind) (toText: 'T -> string) (ofText: string -> 'T voption) : AppValue<'T> =
        AppValue<'T>(name, defaultValue, ValueSome { Kind = kind; ToText = toText; OfText = ofText })

    /// A stored true or false: `StoredValue.bool "Compact" false`.
    let bool (name: string) (defaultValue: bool) : AppValue<bool> =
        make name defaultValue StoredKind.Bool (fun v -> if v then "true" else "false") (fun s ->
            match s with
            | "true" -> ValueSome true
            | "false" -> ValueSome false
            | _ -> ValueNone)

    /// A stored whole number: `StoredValue.int "WindowSizeX" 960`.
    let int (name: string) (defaultValue: int) : AppValue<int> =
        make name defaultValue StoredKind.Number (fun v -> v.ToString CultureInfo.InvariantCulture) (fun s ->
            match Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture) with
            | true, n -> ValueSome n
            | _ -> ValueNone)

    /// A stored number: `StoredValue.float "Volume" 0.5`. It comes back as the same float, in every culture.
    let float (name: string) (defaultValue: float) : AppValue<float> =
        make name defaultValue StoredKind.Number (fun v -> v.ToString("R", CultureInfo.InvariantCulture)) (fun s ->
            match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
            | true, n -> ValueSome n
            | _ -> ValueNone)

    /// A stored text: `StoredValue.string "LastFile" ""`.
    let string (name: string) (defaultValue: string) : AppValue<string> =
        make name defaultValue StoredKind.Text (fun v -> if isNull v then "" else v) ValueSome

    /// A stored choice among an enum's cases, kept by the case's name: `StoredValue.enum "Density" Density.Roomy`.
    /// A stored name the enum no longer has gives the default.
    let enum<'T when 'T: struct and 'T :> Enum and 'T: (new: unit -> 'T)> (name: string) (defaultValue: 'T) : AppValue<'T> =
        make name defaultValue StoredKind.Text (fun v -> v.ToString()) (fun s ->
            match Enum.TryParse<'T> s with
            | true, v when Enum.IsDefined(typeof<'T>, v) -> ValueSome v
            | _ -> ValueNone)

module internal WindowTitleValues =
    /// Made on first use (an app value's definition is shared state: `Theme.current` does the same).
    let current = lazy (AppValue.define "WindowTitle" "Scribble")

/// The window's title (the tab's title in the browser), an app value.
[<AbstractClass; Sealed>]
type WindowTitle =
    /// The window's title: `App.title "Counter"` sets its starting value (default "Scribble");
    /// change it from anywhere with `ctx.useAppValue WindowTitle.current` (`.Set "Counter – Styles"`),
    /// `AppContext.Set(WindowTitle.current, t)` or `Cmd.setAppValue WindowTitle.current t`.
    /// `App.showStats` adds its numbers after it.
    static member current: AppValue<string> = WindowTitleValues.current.Value

module internal WindowValues =
    let size = lazy (AppValue.define "Window.size" Size.Zero)
    let isActive = lazy (AppValue.define "Window.isActive" true)

/// Facts about the app's window, as app values (DESIGN.md, "Common app values and stored values").
[<AbstractClass; Sealed>]
type Window =
    /// The window's size in DIPs (its client area; the page in the browser), kept current by the app: a component
    /// that uses it runs again when the window is resized. `let size = ctx.useAppValue Window.size`, then
    /// `if size.Current.Width < 600.0 then narrow () else wide ()`. It reports: setting it doesn't resize the
    /// window. Zero in a bare `ElementTree`, which has no window.
    static member size: AppValue<Size> = WindowValues.size.Value

    /// True while the app's window is the active one (where typing goes; in the browser, while the page has the
    /// focus), kept current by the app: a component that uses it runs again when that changes. `let active =
    /// ctx.useAppValue Window.isActive`, to dim a title or pause something while the user is elsewhere. It is the
    /// app's own window only: false while the inspector's popped-out window is the active one. True until the
    /// platform says otherwise, and in a bare `ElementTree`. It reports: setting it doesn't activate the window.
    static member isActive: AppValue<bool> = WindowValues.isActive.Value

module internal SystemThemeValues =
    let prefersDark = lazy (AppValue.define "SystemTheme.prefersDark" true)

/// What the system is set to (DESIGN.md, "Following the system's theme").
[<AbstractClass; Sealed>]
type SystemTheme =
    /// True while the system (in the browser, the browser) is set to dark, an app value kept current by the app: a
    /// component that uses it runs again when the setting changes. `let dark = ctx.useAppValue
    /// SystemTheme.prefersDark`. True where the platform can't say, and in a bare `ElementTree`. It reports:
    /// setting it changes nothing on the system. For the app's theme, pick `Theme.system` (`Theme.picked`).
    static member prefersDark: AppValue<bool> = SystemThemeValues.prefersDark.Value
