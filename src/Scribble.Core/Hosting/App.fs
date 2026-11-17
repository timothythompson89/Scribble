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

/// Everything App settings configure.
type AppConfig<'Msg> internal () =
    member val internal Size: DipSize = { Width = 960.0; Height = 600.0 } with get, set
    member val internal Background: Color voption = ValueNone with get, set
    member val internal Fonts: Font list = [] with get, set
    member val internal ShowStats = false with get, set
    member val internal CloseOnEscape = false with get, set
    member val internal OnKey: (Key -> Modifiers -> 'Msg option) list = [] with get, set
    member val internal OnFrame: (FrameTime -> 'Msg option) list = [] with get, set
    member val internal OnError: (exn -> unit) voption = ValueNone with get, set
    member val internal Layers: LayerSetting list list = [] with get, set
    member val internal Styles: Style list = [] with get, set
    member val internal OnStart: (AppContext -> unit) list = [] with get, set
    member val internal Values: (AppValues -> unit) list = [] with get, set
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

    /// Where errors from async work go: commands, work queued to the UI thread, tasks nobody
    /// awaited. By default they're written to the console and the app keeps running.
    static member onError<'Msg>(handler: exn -> unit) : AppSetting<'Msg> = AppSetting(fun c -> c.OnError <- ValueSome handler)

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
        AppSetting(fun c -> c.Values <- c.Values @ [ fun values -> values.Set(Theme.current, value) ])

    /// A layer above the app: `App.layer [ Layer.view (toasts ()) ]`, or a modal one with
    /// `Layer.modal true`. Its view is fixed (components inside keep their own state); see `Layer`.
    static member layer<'Msg>(settings: LayerSetting list) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Layers <- c.Layers @ [ settings ])

    /// An app value's starting value (`AppValue.define`): `App.value compactMode true`.
    static member value<'Msg, 'T> (value: AppValue<'T>) (start: 'T) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Values <- c.Values @ [ fun values -> values.Set(value, start) ])

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
        config.OnError |> ValueOption.iter (fun handler -> dispatcher.OnError <- handler)
        platform.SetWakeHandler dispatcher.RunPending
        // The app values, shared by its windows and layers, with their starting values.
        let values = AppValues(Dispatcher = ValueSome dispatcher)
        for set in config.Values do
            set values
        let requestRebuild () =
            dirty.Value <- true
            host.Value |> ValueOption.iter (fun (h: ElementHost) -> h.Window.RequestFrame())
        let runner =
            match config.Program with
            | ValueSome make -> make { Changed = requestRebuild; Dispatcher = dispatcher; AppValues = values }
            | ValueNone -> { View = (fun () -> Border.create []); Dispatch = ignore; Start = ignore }
        let send (handlers: ('a -> 'Msg option) list) (value: 'a) =
            for handler in handlers do
                match handler value with
                | Some msg -> runner.Dispatch msg
                | None -> ()
        // The window's title: `WindowTitle.current`, plus the stats when shown (refreshed once a second).
        let stats = ref ""
        let applyTitle (window: IWindow) =
            window.Title <- values.Get(WindowTitle.current) + stats.Value
        let updateStats (h: ElementHost) =
            if config.ShowStats then
                let s = h.LastFrame
                stats.Value <- $" · {frameRate.FramesPerSecond:F0} fps · layout {s.LayoutMilliseconds:F2} ms · {s.Shapes} shapes"
                applyTitle h.Window

        let onEvent (event: PlatformEvent) =
            match host.Value with
            | ValueSome(h: ElementHost) ->
                let handled = h.HandleEvent event
                match event with
                | CloseRequested -> h.Close()
                | KeyDown _ when handled -> ()
                | KeyDown(Key.Escape, _, false) when config.CloseOnEscape -> h.Close()
                | KeyDown(key, mods, false) ->
                    for handler in config.OnKey do
                        match handler key mods with
                        | Some msg -> runner.Dispatch msg
                        | None -> ()
                | _ -> ()
            | ValueNone -> ()

        let window =
            platform.CreateWindow({ WindowOptions.Default with Title = values.Get WindowTitle.current; Size = config.Size }, onEvent)
        // Lives as long as the values (the app's), so it's never removed.
        values.Subscribe(WindowTitle.current, fun _ -> applyTitle window) |> ignore
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
                    h.Update(runner.View())
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
        for layer in config.Layers do
            h.AddLayer layer |> ignore
        for handler in config.OnStart do
            handler (AppContext(h, platform))
        Console.WriteLine($"Scribble: {device.Description}, {shaper.Name} shaping, {platform.Name}")
        window.Show()
        window.RequestFrame()
        runner.Start()
        h :> IDisposable
