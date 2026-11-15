namespace Scribble.Platform.Browser

open System
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Platform.Browser.Js

/// Bindings to scribble-platform.js. Kinds must match the `Kind` table in that file.
module internal PlatformJs =
    [<Literal>]
    let ModuleName = "scribble-platform"

    let init = JsFunction(ModuleName, "init", T.void', [ T.action; T.action ])
    let takeEvents = JsFunction(ModuleName, "takeEvents", T.doubles, [])
    let takeTexts = JsFunction(ModuleName, "takeTexts", T.strings, [])
    let scale = JsFunction(ModuleName, "scale", T.double, [])
    let requestFrame = JsFunction(ModuleName, "requestFrame", T.void', [])
    let setTitle = JsFunction(ModuleName, "setTitle", T.void', [ T.string ])
    let createCanvas = JsFunction(ModuleName, "createCanvas", T.doubles, [ T.string ])
    let removeCanvas = JsFunction(ModuleName, "removeCanvas", T.void', [])

    let call0 (f: JsFunction) =
        let args = f.Args()
        f.Invoke args
        args

    let callString (f: JsFunction) (value: string) =
        let args = f.Args()
        args[2].ToJS value
        f.Invoke args
        args

    [<Literal>]
    let RecordSize = 6

/// The browser "window": a canvas that fills the page.
[<Sealed>]
type internal BrowserWindow(elementId: string, initialSize: PixelSize, onEvent: PlatformEvent -> unit) =
    let mutable size = initialSize
    let mutable title = ""
    let mutable closed = false
    let mutable frameRequested = false

    member _.FrameRequested = frameRequested && not closed

    member _.DeliverFrame() =
        frameRequested <- false
        if not closed && not size.IsEmpty then onEvent Frame

    member this.Dispatch(event: PlatformEvent) =
        if not closed then
            match event with
            | Resized newSize ->
                size <- newSize
                onEvent event
                // Draw before the browser paints, so the new canvas size never shows stale pixels.
                this.DeliverFrame()
            | _ -> onEvent event

    interface IWindow with
        member _.ClientSize = size
        member _.Scale = PlatformJs.call0 PlatformJs.scale |> returnedDouble
        member _.Surface = BrowserCanvas elementId
        member _.IsClosed = closed

        member _.Title
            with get () = title
            and set value =
                title <- value
                PlatformJs.callString PlatformJs.setTitle value |> ignore

        member _.Show() = ()

        member _.Close() =
            if not closed then
                closed <- true
                PlatformJs.call0 PlatformJs.removeCanvas |> ignore

        member _.RequestFrame() =
            if not closed then
                frameRequested <- true
                PlatformJs.call0 PlatformJs.requestFrame |> ignore

        member this.Dispose() = (this :> IWindow).Close()

/// The browser windowing backend. One window per page for now: a canvas that fills it.
/// Create it with `BrowserPlatform.InitializeAsync()`, since loading the JavaScript side is async.
[<Sealed>]
type BrowserPlatform private () =
    let mutable window: BrowserWindow option = None

    let pointer (r: float[]) i =
        let kind =
            match int r[i + 1] with
            | 1 -> Touch
            | 2 -> Pen
            | _ -> Mouse
        kind, int r[i + 2], ({ X = r[i + 3]; Y = r[i + 4] }: PixelPoint)

    let button (code: float) =
        match int code with
        | 1 -> Middle
        | 2 -> Right
        | 3 -> XButton1
        | 4 -> XButton2
        | _ -> Left

    let decode (r: float[]) (texts: string[]) i =
        match int r[i] with
        | 0 -> Some(Resized { Width = int r[i + 1]; Height = int r[i + 2] })
        | 1 -> Some(ScaleChanged r[i + 1])
        | 2 -> let k, id, p = pointer r i in Some(PointerMoved(k, id, p))
        | 3 -> let k, id, p = pointer r i in Some(PointerPressed(k, id, p, button r[i + 5]))
        | 4 -> let k, id, p = pointer r i in Some(PointerReleased(k, id, p, button r[i + 5]))
        | 5 -> Some(PointerWheel({ X = r[i + 1]; Y = r[i + 2] }, r[i + 3], r[i + 4]))
        | 6 -> Some(KeyDown(int r[i + 1], r[i + 2] <> 0.0))
        | 7 -> Some(KeyUp(int r[i + 1]))
        | 8 ->
            let index = int r[i + 1]
            if index >= 0 && index < texts.Length then Some(TextInput texts[index]) else None
        | 9 -> Some(FocusChanged(r[i + 1] <> 0.0))
        | _ -> None

    let onEvents () =
        let records = PlatformJs.call0 PlatformJs.takeEvents |> returnedDoubles
        let texts = PlatformJs.call0 PlatformJs.takeTexts |> returnedStrings
        match window with
        | Some w ->
            for i in 0 .. PlatformJs.RecordSize .. records.Length - PlatformJs.RecordSize do
                decode records texts i |> Option.iter w.Dispatch
        | None -> ()

    let onFrame () =
        match window with
        | Some w when w.FrameRequested -> w.DeliverFrame()
        | _ -> ()

    // JavaScript holds these delegates; keeping them in fields documents that they must stay alive.
    let onEventsAction = Action onEvents
    let onFrameAction = Action onFrame

    member private _.Start() =
        let args = PlatformJs.init.Args()
        args[2].ToJS onEventsAction
        args[3].ToJS onFrameAction
        PlatformJs.init.Invoke args

    /// Loads the JavaScript side and returns a ready platform.
    static member InitializeAsync() : Task<BrowserPlatform> =
        task {
            do! importEmbeddedModule PlatformJs.ModuleName typeof<BrowserPlatform>.Assembly "scribble-platform.js"
            let platform = new BrowserPlatform()
            platform.Start()
            return platform
        }

    interface IPlatform with
        member _.Name = "Browser"

        member _.CreateWindow(options, onEvent) =
            if window.IsSome then
                raise (NotSupportedException "The browser platform supports one window (canvas) per page for now.")

            let elementId = "scribble-canvas"
            let size = PlatformJs.callString PlatformJs.createCanvas elementId |> returnedDoubles
            let w = new BrowserWindow(elementId, { Width = int size[0]; Height = int size[1] }, onEvent)
            (w :> IWindow).Title <- options.Title
            window <- Some w
            w :> IWindow

        // The browser drives events and frames; there is no loop to run.
        member _.Run() = ()

        member _.Dispose() =
            window |> Option.iter (fun w -> (w :> IWindow).Close())
            window <- None
