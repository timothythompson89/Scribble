namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text

/// A running app's view and dispatch, as the runtime sees it. `Start` runs once the window exists
/// (the init command).
type internal ProgramRunner<'Msg> = { View: unit -> View; Dispatch: 'Msg -> unit; Start: unit -> unit }

/// What a program gets from the runtime: "the view needs rebuilding" and the UI thread's dispatcher.
type internal ProgramHost = { Changed: unit -> unit; Dispatcher: Dispatcher; AppValues: AppValues }

/// What `App.onStart` handlers get: the running app's host and platform, for packages that plug
/// into the app (the inspector adds layers and may open a window of its own).
[<Sealed>]
type AppContext internal (host: ElementHost, platform: IPlatform) =
    member _.Host = host
    member _.Platform = platform
    /// The window's title now (`WindowTitle.current`, started by `App.title`), without the stats.
    member _.Title = host.AppValues.Get WindowTitle.current
    /// The app values (`AppValue.define`), shared by the app's windows and layers.
    member _.AppValues = host.AppValues
    /// An app value: `ctx.Get compactMode`.
    member _.Get<'T>(value: AppValue<'T>) : 'T = host.AppValues.Get value
    /// Sets an app value for the whole app (from any thread): `ctx.Set(compactMode, true)`.
    member _.Set<'T>(value: AppValue<'T>, next: 'T) : unit = host.AppValues.Set(value, next)
    /// The window's toasts: `app.Toaster.Show "Saved"`.
    member _.Toaster = host.Toaster
    /// The window's dialogs: `app.Dialogs.Confirm [ ... ]`.
    member _.Dialogs = host.Dialogs

/// The stored values behind `App.rememberWindow` (DESIGN.md, "A remembered window"): the library's own, made on
/// first use. Width and height are in DIPs; x and y are the platform's screen coordinates.
module internal RememberedWindow =
    /// No position stored yet.
    [<Literal>]
    let Unknown = Int32.MinValue
    let x = lazy (StoredValue.int "Scribble.Window.X" Unknown)
    let y = lazy (StoredValue.int "Scribble.Window.Y" Unknown)
    let width = lazy (StoredValue.float "Scribble.Window.Width" 0.0)
    let height = lazy (StoredValue.float "Scribble.Window.Height" 0.0)
    let maximized = lazy (StoredValue.bool "Scribble.Window.Maximized" false)

/// Everything App settings configure.
type AppConfig<'Msg> internal () =
    member val internal Size: DipSize = { Width = 960.0; Height = 600.0 } with get, set
    member val internal Background: Color voption = ValueNone with get, set
    member val internal Fonts: Font list = [] with get, set
    member val internal ShowStats = false with get, set
    member val internal CloseOnEscape = false with get, set
    member val internal OnKey: (Key -> Modifiers -> 'Msg option) list = [] with get, set
    member val internal OnFrame: (FrameTime -> 'Msg option) list = [] with get, set
    member val internal OnError: (AppContext -> exn -> unit) voption = ValueNone with get, set
    member val internal Layers: LayerSetting list list = [] with get, set
    member val internal Styles: Style list = [] with get, set
    member val internal OnStart: (AppContext -> unit) list = [] with get, set
    member val internal Values: (AppValues -> unit) list = [] with get, set
    member val internal StoreName: string voption = ValueNone with get, set
    member val internal Themes: (string * Theme) list = [] with get, set
    member val internal ThemeGiven = false with get, set
    member val internal RememberTheme = true with get, set
    member val internal RememberWindow = false with get, set
    member val internal ValueStore: (string -> IValueStore) voption = ValueNone with get, set
    /// Makes the program, given the runtime's side of it.
    member val internal Program: (ProgramHost -> ProgramRunner<'Msg>) voption = ValueNone with get, set

/// One app setting (the same idea as element settings): `App.title "Counter"`.
[<Struct>]
type AppSetting<'Msg> = internal AppSetting of (AppConfig<'Msg> -> unit)

/// Settings for an app, and `App.start`, which runs one on a given platform. `App.run`
/// (from Scribble.Desktop or Scribble.Browser, whichever the app references) picks the platform for you.
[<AbstractClass; Sealed>]
type App =
    /// A dispatch that works from any thread: off the UI thread it's queued to it.
    static member private OnUiThread (dispatcher: Dispatcher) (handle: 'Msg -> unit) : 'Msg -> unit =
        let rec dispatch (msg: 'Msg) =
            if dispatcher.CheckAccess() then handle msg else dispatcher.Post(fun () -> dispatch msg)
        dispatch

    /// The window's title (the tab's in the browser). It's the starting value of `WindowTitle.current`,
    /// an app value: change it while running with `ctx.useAppValue WindowTitle.current`,
    /// `AppContext.Set(WindowTitle.current, t)` or `Cmd.setAppValue WindowTitle.current t`.
    static member title<'Msg>(value: string) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Values <- c.Values @ [ fun values -> values.Set(WindowTitle.current, value) ])

    /// Window size in DIPs (desktop; in the browser the canvas fills the page).
    static member size<'Msg>(width: float, height: float) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Size <- { Width = width; Height = height })

    /// Cleared behind the view every frame, instead of the theme's background (the default, which
    /// follows a theme the view switches to).
    static member background<'Msg>(value: Color) : AppSetting<'Msg> = AppSetting(fun c -> c.Background <- ValueSome value)

    /// Fallback fonts for characters the main fonts lack, e.g. `[ Scribble.Text.Emoji.Twemoji() ]`.
    static member fonts<'Msg>(fallbacks: Font list) : AppSetting<'Msg> = AppSetting(fun c -> c.Fonts <- fallbacks)

    /// Adds frames per second and layout time after the window title.
    static member showStats<'Msg>(value: bool) : AppSetting<'Msg> = AppSetting(fun c -> c.ShowStats <- value)

    /// Closes the window on Escape, unless something in the view handled the key.
    static member closeOnEscape<'Msg>(value: bool) : AppSetting<'Msg> = AppSetting(fun c -> c.CloseOnEscape <- value)

    /// The Elmish pattern: a model, an update that makes the next model from a message, and a view
    /// of the model. Messages update the model at once; the view is rebuilt (and patched onto the
    /// elements) on the next frame, once, however many messages arrived. An update that returns an
    /// equal model causes no rebuild.
    /// `dispatch` works from any thread (off the UI thread it's queued to it).
    static member program<'Model, 'Msg> (init: 'Model) (update: 'Msg -> 'Model -> 'Model) (view: 'Model -> ('Msg -> unit) -> View) : AppSetting<'Msg> =
        AppSetting(fun c ->
            c.Program <-
                ValueSome(fun host ->
                    let model = ref init
                    let dispatch =
                        App.OnUiThread host.Dispatcher (fun (msg: 'Msg) ->
                            let next = update msg model.Value
                            if not (EqualityComparer<'Model>.Default.Equals(next, model.Value)) then
                                model.Value <- next
                                host.Changed())
                    { View = (fun () -> view model.Value dispatch); Dispatch = dispatch; Start = ignore }))

    /// `App.program` with commands: `init` and `update` return the model and a `Cmd` (messages
    /// to send, tasks to run): `| Load -> model, Cmd.ofTask api.Load () Loaded Failed`. The init
    /// command runs once the window exists. Errors from commands go to `App.onError`.
    static member programWithCmd<'Model, 'Msg> (init: 'Model * Cmd<'Msg>) (update: 'Msg -> 'Model -> 'Model * Cmd<'Msg>) (view: 'Model -> ('Msg -> unit) -> View) : AppSetting<'Msg> =
        AppSetting(fun c ->
            c.Program <-
                ValueSome(fun host ->
                    let model = ref (fst init)
                    let dispatch: ('Msg -> unit) ref = ref ignore
                    dispatch.Value <-
                        App.OnUiThread host.Dispatcher (fun (msg: 'Msg) ->
                            let next, cmd = update msg model.Value
                            if not (EqualityComparer<'Model>.Default.Equals(next, model.Value)) then
                                model.Value <- next
                                host.Changed()
                            Cmd.execute host.Dispatcher.ReportError host.AppValues dispatch.Value cmd)
                    { View = (fun () -> view model.Value dispatch.Value)
                      Dispatch = (fun msg -> dispatch.Value msg)
                      Start = fun () -> Cmd.execute host.Dispatcher.ReportError host.AppValues dispatch.Value (snd init) }))

    /// A view that never changes (components inside it keep their own state).
    static member view<'Msg>(value: View) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Program <- ValueSome(fun _ -> { View = (fun () -> value); Dispatch = ignore; Start = ignore }))

    /// Where errors from app code go, so the app keeps running: event handlers, the view,
    /// components, render functions, effects, commands, work queued to the UI thread, tasks nobody
    /// awaited. What failed is left out (README, "Errors"). Without it, an error ends the app.
    /// The handler gets the app's context, so it can tell the user in the app's own window:
    /// `App.onError (fun app e -> app.Toaster.Show [ Toast.severity Severity.Critical; Toast.text e.Message ])`.
    static member onError<'Msg>(handler: AppContext -> exn -> unit) : AppSetting<'Msg> = AppSetting(fun c -> c.OnError <- ValueSome handler)

    /// Styles for the whole app (every layer but those with `Layer.appStyles false`); later ones win
    /// over earlier ones, and styles set on elements win over these. Several `App.styles` add up.
    static member styles<'Msg>(value: Style list) : AppSetting<'Msg> = AppSetting(fun c -> c.Styles <- c.Styles @ value)

    /// The theme for the whole app (every layer but those with `Layer.appStyles false`): the
    /// built-in looks, below every other style, and the base text color, font and size. Default:
    /// `Theme.dark []`. It's the starting value of `Theme.current`, an app value: switch while running
    /// with `ctx.useAppValue Theme.current` (`.Set otherTheme`), `AppContext.Set(Theme.current, t)` or
    /// `Cmd.setAppValue Theme.current t`; the whole window follows (every layer that takes app styles,
    /// and the background).
    static member theme<'Msg>(value: Theme) : AppSetting<'Msg> =
        AppSetting(fun c ->
            c.ThemeGiven <- true
            c.Values <- c.Values @ [ fun values -> values.Set(Theme.current, value) ])

    /// The app's themes by name, the first the one it starts in: `App.themes [ "Dark", Theme.dark []; "Light",
    /// Theme.light [] ]`. The name of the one in use is `Theme.picked`, a stored value: set it to switch
    /// (`ctx.useStoredValue Theme.picked`), and the app starts in that theme next time. Without `App.themes` and
    /// `App.theme`, an app has the built-in pair under "Dark" and "Light"; with `App.theme` alone it has that one
    /// theme and `Theme.picked` does nothing.
    static member themes<'Msg>(value: (string * Theme) list) : AppSetting<'Msg> = AppSetting(fun c -> c.Themes <- value)

    /// True: the window opens where it was last time, at its size and position, maximized if it was (a window
    /// closed while minimized opens at its normal place). `App.size` is then the size on the first run. A position
    /// that is on no display any more (a monitor unplugged) is left out, and a size larger than the display is
    /// shrunk to it. Only the app's own window, on the desktop; the browser's page has no place to remember.
    /// Kept as stored values of the library's (`Scribble.Window.X`, `.Y`, `.Width`, `.Height`, `.Maximized`).
    /// Default: false.
    static member rememberWindow<'Msg>(value: bool) : AppSetting<'Msg> = AppSetting(fun c -> c.RememberWindow <- value)

    /// False: the theme picked (`Theme.picked`) is not kept between runs. Default: true.
    static member rememberTheme<'Msg>(value: bool) : AppSetting<'Msg> = AppSetting(fun c -> c.RememberTheme <- value)

    /// Where the window's toasts stack at the start (the default is the bottom right):
    /// `App.toasts [ Toasts.horizontalAlignment HorizontalAlignment.Center; Toasts.verticalAlignment VerticalAlignment.Top ]`.
    /// It is the starting value of `Toasts.placement`, an app value: change it while running with
    /// `ctx.useAppValue Toasts.placement` or `Cmd.setAppValue Toasts.placement p`.
    static member toasts<'Msg>(settings: ToastsSetting list) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Values <- c.Values @ [ fun values -> values.Set(Toasts.placement, Toasts.From settings) ])

    /// A layer above the app: `App.layer [ Layer.view (toasts ()) ]`, or a modal one with
    /// `Layer.modal true`. Its view is fixed (components inside keep their own state); see `Layer`.
    static member layer<'Msg>(settings: LayerSetting list) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Layers <- c.Layers @ [ settings ])

    /// An app value's starting value (`AppValue.define`): `App.value compactMode true`.
    static member value<'Msg, 'T> (value: AppValue<'T>) (start: 'T) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Values <- c.Values @ [ fun values -> values.Set(value, start) ])

    /// The name the app's stored values (`StoredValue.bool`, `int`, `float`, `string`, `enum`) are kept under: the file's name in isolated storage
    /// on the desktop (`Gallery.json`), the prefix of the keys in the browser's localStorage (`Gallery/Compact`),
    /// which keeps two apps at one web address apart. Default: the name of the app's assembly.
    static member storeName<'Msg>(value: string) : AppSetting<'Msg> = AppSetting(fun c -> c.StoreName <- ValueSome value)

    /// Where the app's stored values are kept, made from the store's name (`App.storeName`). `App.run` gives the
    /// platform's; one written after it in the settings replaces that (a store of your own, or a
    /// `MemoryValueStore` to keep nothing).
    static member valueStore<'Msg>(make: string -> IValueStore) : AppSetting<'Msg> = AppSetting(fun c -> c.ValueStore <- ValueSome make)

    /// False: the app's windows get no screen-reader bridge (DESIGN.md, "AccessKit: the screen-reader bridge").
    /// Default: true; a bridge does nothing until a screen reader, or another tool that reads windows, asks.
    /// It sets `ScreenReader.enabled`, an app value read when a window opens (changing it later does nothing for
    /// windows already open).
    static member screenReader<'Msg>(value: bool) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Values <- c.Values @ [ fun values -> values.Set(ScreenReader.enabled, value) ])

    /// Runs once the window and its host exist (layers added, before the first frame): for
    /// packages that plug into the host, such as `Inspector.enable`.
    static member onStart<'Msg>(handler: AppContext -> unit) : AppSetting<'Msg> =
        AppSetting(fun c -> c.OnStart <- c.OnStart @ [ handler ])

    /// Turns key presses into messages: `App.onKey (fun key mods -> match key with Key.Space -> Some Pause | _ -> None)`.
    /// Runs last, only for keys nothing in the view handled (a focused button takes Space and
    /// Enter; Tab moves focus), and never for repeats.
    static member onKey<'Msg>(handler: Key -> Modifiers -> 'Msg option) : AppSetting<'Msg> = AppSetting(fun c -> c.OnKey <- c.OnKey @ [ handler ])

    /// Runs at the start of every frame. Frames only happen when something asked for one, so an
    /// animation keeps going by changing the model on each frame and stops when it doesn't. The
    /// first frame's `Delta` is zero; the app draws a second frame after it, so a model that only
    /// moves with time still gets going.
    static member onFrame<'Msg>(handler: FrameTime -> 'Msg option) : AppSetting<'Msg> =
        AppSetting(fun c -> c.OnFrame <- c.OnFrame @ [ handler ])

    /// Runs an app on a given platform, device and shaper: creates the window, shows it, and from
    /// then on rebuilds the view when the model changes and draws when something asks for a frame.
    /// Call the platform's Run afterwards; dispose the result when it returns (desktop).
    static member start<'Msg>(platform: IPlatform, device: IRenderDevice, shaper: IShaper, settings: AppSetting<'Msg> list) : IDisposable =
        let config = AppConfig<'Msg>()
        for AppSetting apply in settings do
            apply config
        let text = TextSystem.create device shaper [ TextSystem.fallbacks config.Fonts ]
        let clock = FrameClock(fun () -> platform.Elapsed)
        let frameRate = FrameRateCounter()
        let host = ref ValueNone
        let dirty = ref true
        // Work from other threads reaches this (the UI) thread through the dispatcher; the
        // platform wakes the loop to run it.
        let dispatcher = Dispatcher(platform.Wake, fun delay action -> platform.After(delay, action))
        // The handler gets the app's context; an error before the host exists has none, and ends the app as it
        // would without a handler.
        config.OnError
        |> ValueOption.iter (fun handler ->
            dispatcher.OnError <-
                fun e ->
                    match host.Value with
                    | ValueSome h -> handler (AppContext(h, platform)) e
                    | ValueNone -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw e)
        platform.SetWakeHandler dispatcher.RunPending
        // The app values, shared by its windows and layers, with their starting values.
        let values = AppValues(Dispatcher = ValueSome dispatcher)
        // Where its stored values are kept: the platform's store (App.run gives it), else memory, with a Debug
        // warning when a stored value is first used. A store that can't be made is one too.
        let storeName =
            match config.StoreName with
            | ValueSome name when not (String.IsNullOrWhiteSpace name) -> name
            | _ ->
                match System.Reflection.Assembly.GetEntryAssembly() with
                | null -> "Scribble"
                | entry -> (match entry.GetName().Name with null -> "Scribble" | n -> n)
        values.Store <-
            match config.ValueStore with
            | ValueSome make ->
                try
                    make storeName
                with e ->
                    DebugOnly.warn ("StoredValue.store", "The store for stored values could not be opened (" + e.Message + "): they are kept for this run only.")
                    MemoryValueStore()
            | ValueNone -> UnkeptValueStore()
        for set in config.Values do
            set values
        // The app's themes by name, and the one picked (DESIGN.md, "Common app values and stored values"): the
        // stored name's theme at start, else the first; from then on the window's theme follows the name. A stored
        // name the app no longer has stays stored until something picks again.
        let themes =
            if not config.Themes.IsEmpty then config.Themes
            elif config.ThemeGiven then []
            else [ "Dark", values.Get Theme.current; "Light", ThemeExtensions.defaultLight.Value ]
        if not config.RememberTheme then values.Unkept.Add Theme.picked.Id |> ignore
        if not themes.IsEmpty then
            let find (name: string) = themes |> List.tryFind (fun (n, _) -> n = name)
            match find (values.Get Theme.picked) with
            | Some(_, theme) -> values.Set(Theme.current, theme)
            | None ->
                values.Prime(Theme.picked, fst themes.Head)
                values.Set(Theme.current, snd themes.Head)
            // Lives as long as the values (the app's), so it's never removed.
            values.Subscribe(Theme.picked, fun name -> find name |> Option.iter (fun (_, theme) -> values.Set(Theme.current, theme))) |> ignore
        let requestRebuild () =
            dirty.Value <- true
            host.Value |> ValueOption.iter (fun (h: ElementHost) -> h.Window.RequestFrame())
        let runner =
            match config.Program with
            | ValueSome make -> make { Changed = requestRebuild; Dispatcher = dispatcher; AppValues = values }
            | ValueNone -> { View = (fun () -> Border.create []); Dispatch = ignore; Start = ignore }
        // App code that throws here (a handler, `update` inside dispatch, the view) goes to App.onError
        // (repeats counted), or ends the app without one.
        let guarded (action: unit -> unit) =
            try
                action ()
            with e ->
                dispatcher.ReportUiError e
        let send (handlers: ('a -> 'Msg option) list) (value: 'a) =
            for handler in handlers do
                guarded (fun () ->
                    match handler value with
                    | Some msg -> runner.Dispatch msg
                    | None -> ())
        // The window's title: `WindowTitle.current`, plus the stats when shown (refreshed once a second).
        let stats = ref ""
        let applyTitle (window: IWindow) =
            window.Title <- values.Get(WindowTitle.current) + stats.Value
        let updateStats (h: ElementHost) =
            if config.ShowStats then
                let s = h.LastFrame
                stats.Value <- $" · {frameRate.FramesPerSecond:F0} fps · layout {s.LayoutMilliseconds:F2} ms · {s.Shapes} shapes"
                applyTitle h.Window

        // Closing saves what stored values hold now (a change in the last half second is not yet saved).
        // `Window.size`: the window's client area in DIPs, at the start and whenever it or its scale changes.
        let reportSize (window: IWindow) =
            let size, scale = window.ClientSize, max 0.01 window.Scale
            values.Set(Window.size, Size.Create(float size.Width / scale, float size.Height / scale))

        // A remembered window (`App.rememberWindow`; DESIGN.md, "A remembered window"): where a desktop window is
        // and whether it is maximized, kept as it changes. Its size and position are kept only while it is neither
        // maximized nor minimized, so they stay those of the normal window: what it returns to from maximized.
        let rememberPlacement (window: IWindow) =
            if config.RememberWindow && not window.IsClosed then
                match window with
                | :? IWindowPlacement as placed when not placed.IsMinimized ->
                    let maximized = placed.IsMaximized
                    values.Set(RememberedWindow.maximized.Value, maximized)
                    if not maximized then
                        let size = values.Get Window.size
                        let struct (x, y) = placed.Position
                        // A minimized window is parked far off the screen by some systems: never a place to return to.
                        if size.Width >= 1.0 && size.Height >= 1.0 && x > -16000 && y > -16000 then
                            values.Set(RememberedWindow.width.Value, size.Width)
                            values.Set(RememberedWindow.height.Value, size.Height)
                            values.Set(RememberedWindow.x.Value, x)
                            values.Set(RememberedWindow.y.Value, y)
                | _ -> ()

        let close (h: ElementHost) =
            rememberPlacement h.Window
            values.FlushStore()
            h.Close()

        let onEvent (event: PlatformEvent) =
            match host.Value with
            | ValueSome(h: ElementHost) ->
                let handled = h.HandleEvent event
                match event with
                | Resized _
                | ScaleChanged _ ->
                    reportSize h.Window
                    rememberPlacement h.Window
                | Moved -> rememberPlacement h.Window
                | _ -> ()
                match event with
                | CloseRequested -> close h
                | KeyDown _ when handled -> ()
                | KeyDown(Key.Escape, _, false) when config.CloseOnEscape -> close h
                | KeyDown(key, mods, false) ->
                    for handler in config.OnKey do
                        guarded (fun () ->
                            match handler key mods with
                            | Some msg -> runner.Dispatch msg
                            | None -> ())
                | _ -> ()
            | ValueNone -> ()

        let window =
            let options = { WindowOptions.Default with Title = values.Get WindowTitle.current; Size = config.Size }
            // A remembered window opens where it was: its stored size (App.size is then the first run's), its
            // position, and maximized if it was. The platform leaves out a position no display has any more.
            let options =
                if not config.RememberWindow then options
                else
                    let width, height = values.Get RememberedWindow.width.Value, values.Get RememberedWindow.height.Value
                    let x, y = values.Get RememberedWindow.x.Value, values.Get RememberedWindow.y.Value
                    { options with
                        Size = (if width >= 100.0 && height >= 100.0 then { Width = width; Height = height } else config.Size)
                        Position = (if x <> RememberedWindow.Unknown && y <> RememberedWindow.Unknown then ValueSome(struct (x, y)) else ValueNone)
                        Maximized = values.Get RememberedWindow.maximized.Value }
            platform.CreateWindow(options, onEvent)
        // Lives as long as the values (the app's), so it's never removed.
        values.Subscribe(WindowTitle.current, fun _ -> applyTitle window) |> ignore
        reportSize window
        let beforeFrame () =
            match host.Value with
            | ValueSome(h: ElementHost) ->
                let time = clock.Tick()
                send config.OnFrame time
                // The first frame has no time behind it (Delta zero), so an animation can't move yet
                // and its model doesn't change: ask for one more frame, or it would wait for input.
                if time.Number = 0L && not config.OnFrame.IsEmpty then h.Window.RequestFrame()
                if dirty.Value then
                    dirty.Value <- false
                    // A view that throws keeps the elements of the last one (the next model change tries again).
                    let view =
                        try
                            ValueSome(runner.View())
                        with e ->
                            dispatcher.ReportUiError e
                            ValueNone
                    view |> ValueOption.iter h.Update
                if frameRate.Add time then updateStats h
            | ValueNone -> ()
        let h =
            ElementHost.create window device text [
                yield! config.Background |> ValueOption.map ElementHost.background |> ValueOption.toList
                ElementHost.dispatcher dispatcher
                ElementHost.appValues values
                ElementHost.beforeFrame beforeFrame
                ElementHost.clock (fun () -> platform.Elapsed)
                ElementHost.styles config.Styles
            ]
        host.Value <- ValueSome h
        // What ViewImage, Capture and Files use (DESIGN.md, "Rendering to an image and saving it"): this app's. Kept
        // for its UI thread (a newer app on the same thread replaces it; a desktop process ends with its app).
        RenderServices.Use
            { Device = device
              Text = text
              Platform = ValueSome platform
              Window = ValueSome window
              Dispatcher = ValueSome dispatcher
              AppValues = ValueSome values
              Images = ValueNone }
        |> ignore
        for layer in config.Layers do
            h.AddLayer layer |> ignore
        for handler in config.OnStart do
            handler (AppContext(h, platform))
        Console.WriteLine($"Scribble: {device.Description}, {shaper.Name} shaping, {platform.Name}")
        window.Show()
        window.RequestFrame()
        runner.Start()
        h :> IDisposable
