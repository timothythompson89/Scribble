namespace Scribble.Platform.Browser

open System
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Platform.Browser.Js

/// Bindings to scribble-platform.js. Kinds must match the `Kind` table in that file.
module internal PlatformJs =
    [<Literal>]
    let ModuleName = "scribble-platform"

    let init = JsFunction(ModuleName, "init", T.void', [ T.action; T.action; T.action ])
    let wake = JsFunction(ModuleName, "wake", T.void', [])
    let takeEvents = JsFunction(ModuleName, "takeEvents", T.doubles, [])
    let takeTexts = JsFunction(ModuleName, "takeTexts", T.strings, [])
    let scale = JsFunction(ModuleName, "scale", T.double, [])
    let requestFrame = JsFunction(ModuleName, "requestFrame", T.void', [])
    let setTitle = JsFunction(ModuleName, "setTitle", T.void', [ T.string ])
    let setCursor = JsFunction(ModuleName, "setCursor", T.void', [ T.double ])
    let makeImageCursor =
        JsFunction(
            ModuleName,
            "makeImageCursor",
            T.void',
            [ T.double; T.double; T.double; T.double; T.double; T.bytes; T.double; T.double; T.double; T.double; T.bytes; T.double ]
        )
    let setImageCursor = JsFunction(ModuleName, "setImageCursor", T.void', [ T.double ])
    let forgetImageCursor = JsFunction(ModuleName, "forgetImageCursor", T.void', [ T.double ])
    let claimKey = JsFunction(ModuleName, "claimKey", T.void', [ T.string ])
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

    let callDouble (f: JsFunction) (value: float) =
        let args = f.Args()
        args[2].ToJS value
        f.Invoke args
        args

    [<Literal>]
    let RecordSize = 7

/// The browser "window": a canvas that fills the page.
[<Sealed>]
type internal BrowserWindow(elementId: string, initialSize: PixelSize, onEvent: PlatformEvent -> unit) =
    let mutable size = initialSize
    let mutable title = ""
    let mutable closed = false
    let mutable frameRequested = false
    let mutable cursor = Cursor.Default
    /// Image cursors made in scribble-platform.js, by key and density: the 32 used most recently
    /// (each holds two PNG data URLs in the page), the rest forgotten there too.
    let imageCursorIds =
        RecentCache<struct (string * float), int>(32, fun id ->
            PlatformJs.callDouble PlatformJs.forgetImageCursor (float id) |> ignore)
    let mutable lastImageCursorId = 0

    /// Shows an image cursor, made at the page's density (and at 1x) the first time.
    let showImage (image: CursorImage) =
        let density = PlatformJs.call0 PlatformJs.scale |> returnedDouble
        let id =
            imageCursorIds.GetOrAdd(struct (image.Key, density), fun () ->
                lastImageCursorId <- lastImageCursorId + 1
                let id = lastImageCursorId
                let sharp = image.Render density
                let one = if density = 1.0 then sharp else image.Render 1.0
                let f = PlatformJs.makeImageCursor
                let args = f.Args()
                args[2].ToJS(float id)
                args[3].ToJS(float sharp.Width)
                args[4].ToJS(float sharp.Height)
                args[5].ToJS(float sharp.HotspotX)
                args[6].ToJS(float sharp.HotspotY)
                args[7].ToJS(Span<byte> sharp.Pixels)
                args[8].ToJS(float one.Width)
                args[9].ToJS(float one.Height)
                args[10].ToJS(float one.HotspotX)
                args[11].ToJS(float one.HotspotY)
                args[12].ToJS(Span<byte> one.Pixels)
                args[13].ToJS density
                f.Invoke args
                id)
        PlatformJs.callDouble PlatformJs.setImageCursor (float id) |> ignore

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
            | ScaleChanged _ ->
                onEvent event
                // An image cursor is made per density: show the one for the new density.
                match cursor with
                | Cursor.Image image -> showImage image
                | _ -> ()
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

        member _.SetCursor(value: Cursor) =
            if not closed && value <> cursor then
                cursor <- value
                // The index into the `cursors` table in scribble-platform.js (declaration order).
                let index =
                    match value with
                    | Cursor.Default -> 0
                    | Cursor.Hand -> 1
                    | Cursor.Text -> 2
                    | Cursor.Crosshair -> 3
                    | Cursor.Move -> 4
                    | Cursor.ResizeLeftRight -> 5
                    | Cursor.ResizeUpDown -> 6
                    | Cursor.NotAllowed -> 7
                    | Cursor.Wait -> 8
                    | Cursor.ResizeDiagonal -> 9
                    | Cursor.Image _ -> -1
                match value with
                | Cursor.Image image -> showImage image
                | _ -> PlatformJs.callDouble PlatformJs.setCursor (float index) |> ignore

        member _.ClaimKey(key: Key, modifiers: Modifiers) =
            // The browser's `key` name, lower case, as scribble-platform.js compares it.
            let name =
                match key with
                | Key.F n -> ValueSome("f" + string n)
                | Key.Char c -> ValueSome(string (Char.ToLowerInvariant c))
                | Key.Escape -> ValueSome "escape"
                | Key.Enter -> ValueSome "enter"
                | _ -> ValueNone
            match name with
            | ValueSome n -> PlatformJs.callString PlatformJs.claimKey (string (int modifiers) + "|" + n) |> ignore
            | ValueNone -> ()

        member this.Dispose() = (this :> IWindow).Close()

/// The browser windowing backend. One window per page for now: a canvas that fills it.
/// Create it with `BrowserPlatform.InitializeAsync()`, since loading the JavaScript side is async.
[<Sealed>]
type BrowserPlatform private () =
    let mutable window: BrowserWindow option = None

    let kindOf (code: float) =
        match int code with
        | 1 -> Touch
        | 2 -> Pen
        | _ -> Mouse

    let pointer (r: float[]) i =
        kindOf r[i + 1], int r[i + 2], ({ X = r[i + 3]; Y = r[i + 4] }: PixelPoint)

    let button (code: float) =
        match int code with
        | 1 -> Middle
        | 2 -> Right
        | 3 -> XButton1
        | 4 -> XButton2
        | _ -> Left

    let modifiers (code: float) : Modifiers = enum (int code)

    /// A KeyboardEvent.key value as a Key.
    let keyOf (name: string) : Key =
        match name with
        | " " | "Spacebar" -> Key.Space
        | "Enter" -> Key.Enter
        | "Escape" | "Esc" -> Key.Escape
        | "Tab" -> Key.Tab
        | "Backspace" -> Key.Backspace
        | "Delete" | "Del" -> Key.Delete
        | "Insert" -> Key.Insert
        | "ArrowLeft" | "Left" -> Key.Left
        | "ArrowUp" | "Up" -> Key.Up
        | "ArrowRight" | "Right" -> Key.Right
        | "ArrowDown" | "Down" -> Key.Down
        | "Home" -> Key.Home
        | "End" -> Key.End
        | "PageUp" -> Key.PageUp
        | "PageDown" -> Key.PageDown
        | "Shift" -> Key.Shift
        | "Control" -> Key.Control
        | "Alt" | "AltGraph" -> Key.Alt
        | "Meta" | "OS" -> Key.Meta
        | "CapsLock" -> Key.CapsLock
        | n when n.Length = 1 -> Key.Char(Char.ToLowerInvariant n[0])
        | n when n.Length >= 2 && n.Length <= 3 && n[0] = 'F' && Char.IsDigit n[1] ->
            match Int32.TryParse(n.Substring 1) with
            | true, f when f >= 1 && f <= 24 -> Key.F f
            | _ -> Key.Other n
        | n -> Key.Other n

    let textAt (texts: string[]) (index: float) =
        let i = int index
        if i >= 0 && i < texts.Length then ValueSome texts[i] else ValueNone

    let decode (r: float[]) (texts: string[]) i =
        match int r[i] with
        | 0 -> Some(Resized { Width = int r[i + 1]; Height = int r[i + 2] })
        | 1 -> Some(ScaleChanged r[i + 1])
        | 2 -> let k, id, p = pointer r i in Some(PointerMoved(k, id, p, modifiers r[i + 6]))
        | 3 -> let k, id, p = pointer r i in Some(PointerPressed(k, id, p, button r[i + 5], modifiers r[i + 6]))
        | 4 -> let k, id, p = pointer r i in Some(PointerReleased(k, id, p, button r[i + 5], modifiers r[i + 6]))
        | 5 -> Some(PointerWheel({ X = r[i + 1]; Y = r[i + 2] }, r[i + 3], r[i + 4], modifiers r[i + 5]))
        | 10 -> Some(PointerExited(kindOf r[i + 1], int r[i + 2]))
        | 11 -> Some(PointerCanceled(kindOf r[i + 1], int r[i + 2]))
        | 6 ->
            match textAt texts r[i + 1] with
            | ValueSome name -> Some(KeyDown(keyOf name, modifiers r[i + 3], r[i + 2] <> 0.0))
            | ValueNone -> None
        | 7 ->
            match textAt texts r[i + 1] with
            | ValueSome name -> Some(KeyUp(keyOf name, modifiers r[i + 3]))
            | ValueNone -> None
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
    let mutable wakeHandler: unit -> unit = ignore
    let onEventsAction = Action onEvents
    let onFrameAction = Action onFrame
    let onWakeAction = Action(fun () -> wakeHandler ())

    member private _.Start() =
        let args = PlatformJs.init.Args()
        args[2].ToJS onEventsAction
        args[3].ToJS onFrameAction
        args[4].ToJS onWakeAction
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
        member _.SupportsMultipleWindows = false

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

        member _.SetWakeHandler(handler) = wakeHandler <- handler

        // WebAssembly is single-threaded here, so "any thread" is this one.
        member _.Wake() = PlatformJs.call0 PlatformJs.wake |> ignore

        member _.Dispose() =
            window |> Option.iter (fun w -> (w :> IWindow).Close())
            window <- None
