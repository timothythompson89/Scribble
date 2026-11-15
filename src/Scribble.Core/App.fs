namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text

/// Key codes for App.onKey. These are the platform's codes (Win32 virtual keys; the browser
/// reports the same values for these keys) until a portable Key type arrives in Milestone 5.
module Key =
    [<Literal>]
    let Backspace = 0x08
    [<Literal>]
    let Tab = 0x09
    [<Literal>]
    let Enter = 0x0D
    [<Literal>]
    let Escape = 0x1B
    [<Literal>]
    let Space = 0x20
    [<Literal>]
    let Left = 0x25
    [<Literal>]
    let Up = 0x26
    [<Literal>]
    let Right = 0x27
    [<Literal>]
    let Down = 0x28

/// A running app's view and dispatch, as the runtime sees it.
type internal ProgramRunner<'Msg> = { View: unit -> View; Dispatch: 'Msg -> unit }

/// Everything App settings configure.
type AppConfig<'Msg> internal () =
    member val internal Title = "Scribble" with get, set
    member val internal Size: DipSize = { Width = 960.0; Height = 600.0 } with get, set
    member val internal Background = Color.Hex 0x181825 with get, set
    member val internal Fonts: Font list = [] with get, set
    member val internal ShowStats = false with get, set
    member val internal CloseOnEscape = false with get, set
    member val internal OnKey: (int -> 'Msg option) list = [] with get, set
    member val internal OnFrame: (FrameTime -> 'Msg option) list = [] with get, set
    member val internal OnPointerMove: (Element voption -> 'Msg option) list = [] with get, set
    /// Makes the program, given "the view needs rebuilding".
    member val internal Program: ((unit -> unit) -> ProgramRunner<'Msg>) voption = ValueNone with get, set

/// One app setting (the same idea as element settings): `App.title "Counter"`.
[<Struct>]
type AppSetting<'Msg> = internal AppSetting of (AppConfig<'Msg> -> unit)

/// Settings for an app, and `App.start`, which runs one on a given platform. `App.run`
/// (Scribble.Desktop) and `App.runInBrowser` (Scribble.Browser) pick the platform for you.
[<AbstractClass; Sealed>]
type App =
    static member title<'Msg>(value: string) : AppSetting<'Msg> = AppSetting(fun c -> c.Title <- value)

    /// Window size in DIPs (desktop; in the browser the canvas fills the page).
    static member size<'Msg>(width: float, height: float) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Size <- { Width = width; Height = height })

    /// Cleared behind the view every frame.
    static member background<'Msg>(value: Color) : AppSetting<'Msg> = AppSetting(fun c -> c.Background <- value)

    /// Fallback fonts for characters the main fonts lack, e.g. `[ Scribble.Text.Emoji.Twemoji() ]`.
    static member fonts<'Msg>(fallbacks: Font list) : AppSetting<'Msg> = AppSetting(fun c -> c.Fonts <- fallbacks)

    /// Adds frames per second and layout time to the window title.
    static member showStats<'Msg>(value: bool) : AppSetting<'Msg> = AppSetting(fun c -> c.ShowStats <- value)

    static member closeOnEscape<'Msg>(value: bool) : AppSetting<'Msg> = AppSetting(fun c -> c.CloseOnEscape <- value)

    /// The Elmish pattern: a model, an update that makes the next model from a message, and a view
    /// of the model. Messages update the model at once; the view is rebuilt (and patched onto the
    /// elements) on the next frame, once, however many messages arrived. An update that returns an
    /// equal model causes no rebuild.
    static member program<'Model, 'Msg> (init: 'Model) (update: 'Msg -> 'Model -> 'Model) (view: 'Model -> ('Msg -> unit) -> View) : AppSetting<'Msg> =
        AppSetting(fun c ->
            c.Program <-
                ValueSome(fun changed ->
                    let model = ref init
                    let dispatch (msg: 'Msg) =
                        let next = update msg model.Value
                        if not (EqualityComparer<'Model>.Default.Equals(next, model.Value)) then
                            model.Value <- next
                            changed ()
                    { View = (fun () -> view model.Value dispatch); Dispatch = dispatch }))

    /// A view that never changes.
    static member view<'Msg>(value: View) : AppSetting<'Msg> =
        AppSetting(fun c -> c.Program <- ValueSome(fun _ -> { View = (fun () -> value); Dispatch = ignore }))

    /// Turns key presses into messages (repeats are left out).
    static member onKey<'Msg>(handler: int -> 'Msg option) : AppSetting<'Msg> = AppSetting(fun c -> c.OnKey <- c.OnKey @ [ handler ])

    /// Runs at the start of every frame. Frames only happen when something asked for one, so an
    /// animation keeps going by changing the model on each frame and stops when it doesn't.
    static member onFrame<'Msg>(handler: FrameTime -> 'Msg option) : AppSetting<'Msg> =
        AppSetting(fun c -> c.OnFrame <- c.OnFrame @ [ handler ])

    /// The element under the pointer whenever it moves (a stopgap for hover until Milestone 5).
    static member onPointerMove<'Msg>(handler: Element voption -> 'Msg option) : AppSetting<'Msg> =
        AppSetting(fun c -> c.OnPointerMove <- c.OnPointerMove @ [ handler ])

    /// Runs an app on a given platform, device and shaper: creates the window, shows it, and from
    /// then on rebuilds the view when the model changes and draws when something asks for a frame.
    /// Call the platform's Run afterwards; dispose the result when it returns (desktop).
    static member start<'Msg>(platform: IPlatform, device: IRenderDevice, shaper: IShaper, settings: AppSetting<'Msg> list) : IDisposable =
        let config = AppConfig<'Msg>()
        for AppSetting apply in settings do
            apply config
        let text = TextSystem(device, shaper, config.Fonts)
        let clock = FrameClock()
        let frameRate = FrameRateCounter()
        let host = ref ValueNone
        let dirty = ref true
        let requestRebuild () =
            dirty.Value <- true
            host.Value |> ValueOption.iter (fun (h: ElementHost) -> h.Window.RequestFrame())
        let runner =
            match config.Program with
            | ValueSome make -> make requestRebuild
            | ValueNone -> { View = (fun () -> Border.create []); Dispatch = ignore }
        let send (handlers: ('a -> 'Msg option) list) (value: 'a) =
            for handler in handlers do
                match handler value with
                | Some msg -> runner.Dispatch msg
                | None -> ()
        let updateTitle (h: ElementHost) =
            if config.ShowStats then
                let s = h.LastFrame
                h.Window.Title <-
                    $"{config.Title} · {frameRate.FramesPerSecond:F0} fps · layout {s.LayoutMilliseconds:F2} ms · {s.Shapes} shapes"

        let onEvent (event: PlatformEvent) =
            match host.Value with
            | ValueSome(h: ElementHost) ->
                h.HandleEvent event
                match event with
                | CloseRequested -> h.Window.Close()
                | KeyDown(Key.Escape, false) when config.CloseOnEscape -> h.Window.Close()
                | KeyDown(key, false) -> send config.OnKey key
                | PointerMoved(_, _, p) when not config.OnPointerMove.IsEmpty ->
                    send config.OnPointerMove (h.Tree.HitTest(float p.X / h.Window.Scale, float p.Y / h.Window.Scale))
                | _ -> ()
            | ValueNone -> ()

        let window = platform.CreateWindow({ WindowOptions.Default with Title = config.Title; Size = config.Size }, onEvent)
        let h = new ElementHost(window, device, text, Background = config.Background)
        h.BeforeFrame <-
            fun () ->
                let time = clock.Tick()
                send config.OnFrame time
                if dirty.Value then
                    dirty.Value <- false
                    h.Update(runner.View())
                if frameRate.Add time then updateTitle h
        host.Value <- ValueSome h
        Console.WriteLine($"Scribble: {device.Description}, {shaper.Name} shaping, {platform.Name}")
        window.Show()
        window.RequestFrame()
        h :> IDisposable
