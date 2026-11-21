namespace Scribble.Platform.SDL3

#nowarn "9"

open System
open System.Diagnostics
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Platform.SDL3.Native

/// A top-level SDL window. Created by SdlPlatform; all calls happen on the UI thread. SDL's events reach it through
/// the platform's event filter as they happen (as a Win32 window procedure would), not later from the queue: that is
/// how it keeps drawing while Windows runs its modal size-move loop.
[<Sealed>]
type SdlWindow
    internal
    (
        handle: nativeint,
        onEvent: PlatformEvent -> unit,
        systemCursor: Cursor -> nativeint,
        onDestroyed: unit -> unit
    ) =
    let id = SDL_GetWindowID handle
    let mutable title = ""
    let mutable closed = false
    let mutable clientSize = PixelSize.Zero
    let mutable scale = 1.0
    /// Window coordinates (mouse positions) to pixels: 1 on Windows, 2 on a Retina Mac.
    let mutable density = 1.0
    let mutable minimized = false
    let mutable frameRequested = false
    let mutable pressedButtons = 0
    /// Text input is on (`SetTextInput` with a request).
    let mutable textInputOn = false
    /// SCRIBBLE_LOG_TEXT=1 writes what SDL says of text input to the console: for finding out what an input method
    /// sends on a machine.
    let logText = Environment.GetEnvironmentVariable "SCRIBBLE_LOG_TEXT" = "1"
    let log (message: string) = if logText then Console.WriteLine("[text] " + message)
    /// The pointer is over the window (as far as this window has said: PointerExited sent when it leaves).
    let mutable inside = false
    let mutable cursor = Cursor.Default
    /// During a live resize (SDL's live-resize expose events): resizes draw at most once per display refresh
    /// (DESIGN.md, "Live resize lag"). The refresh period in Stopwatch ticks, read when it starts.
    let mutable inLiveResize = false
    let mutable lastFrame = 0L
    let mutable refreshTicks = Stopwatch.Frequency / 60L
    /// Image cursors by key and scale, the 32 used most recently (Windows allows a process 10,000 cursor handles); the rest destroyed, and all of them
    /// with the window.
    let imageCursors =
        RecentCache<struct (string * float), nativeint>(32, fun h -> if h <> 0n then SDL_DestroyCursor h)

    let readPixelSize () =
        let mutable w = 0
        let mutable h = 0
        SDL_GetWindowSizeInPixels(handle, &&w, &&h) |> ignore
        ({ Width = w; Height = h }: PixelSize)

    let readScale () =
        let s = float (SDL_GetWindowDisplayScale handle)
        scale <- (if s > 0.0 then s else 1.0)
        let d = float (SDL_GetWindowPixelDensity handle)
        density <- (if d > 0.0 then d else 1.0)

    let surface =
        let props = SDL_GetWindowProperties handle
        let hwnd = pointerProperty props "SDL.window.win32.hwnd"
        if hwnd = 0n then
            raise (PlatformNotSupportedException "The SDL platform runs on Windows for now (macOS and Linux come with their builds).")
        Win32Handle(pointerProperty props "SDL.window.win32.instance", hwnd)

    let modifiersOf (m: uint16) =
        (if m &&& SDL_KMOD_SHIFT <> 0us then Modifiers.Shift else Modifiers.None)
        ||| (if m &&& SDL_KMOD_CTRL <> 0us then Modifiers.Control else Modifiers.None)
        ||| (if m &&& SDL_KMOD_ALT <> 0us then Modifiers.Alt else Modifiers.None)
        ||| (if m &&& SDL_KMOD_GUI <> 0us then Modifiers.Meta else Modifiers.None)

    let modifiers () = modifiersOf (SDL_GetModState())

    /// An SDL keycode as a portable Key. SDL's keycodes already follow the layout (Shift+1 is '!' on a US layout,
    /// letters are Latin on any layout); keys without a character are scancodes.
    let keyOf (key: uint32) : Key =
        if key &&& SDLK_SCANCODE_MASK <> 0u then
            match int (key &&& ~~~SDLK_SCANCODE_MASK) with
            | 57 -> Key.CapsLock
            | s when s >= 58 && s <= 69 -> Key.F(s - 57)
            | s when s >= 104 && s <= 115 -> Key.F(s - 91)
            | 73 -> Key.Insert
            | 74 -> Key.Home
            | 75 -> Key.PageUp
            | 77 -> Key.End
            | 78 -> Key.PageDown
            | 79 -> Key.Right
            | 80 -> Key.Left
            | 81 -> Key.Down
            | 82 -> Key.Up
            // SDL_SCANCODE_APPLICATION.
            | 101 -> Key.Menu
            | 224 | 228 -> Key.Control
            | 225 | 229 -> Key.Shift
            | 226 | 230 -> Key.Alt
            | 227 | 231 -> Key.Meta
            | _ -> Key.Other(readUtf8 (SDL_GetKeyName key))
        else
            match key with
            | 0x08u -> Key.Backspace
            | 0x09u -> Key.Tab
            | 0x0Du -> Key.Enter
            | 0x1Bu -> Key.Escape
            | 0x20u -> Key.Space
            | 0x7Fu -> Key.Delete
            | k when k > 0x20u && k < 0xD800u -> Key.Char(Char.ToLowerInvariant(char k))
            | k when k >= 0xE000u && k < 0x10000u -> Key.Char(Char.ToLowerInvariant(char k))
            | _ -> Key.Other(readUtf8 (SDL_GetKeyName key))

    let kindOf (which: uint32) =
        if which = SDL_TOUCH_MOUSEID then Touch
        elif which = SDL_PEN_MOUSEID then Pen
        else Mouse

    let buttonOf (b: byte) =
        if b = SDL_BUTTON_LEFT then ValueSome Left
        elif b = SDL_BUTTON_RIGHT then ValueSome Right
        elif b = SDL_BUTTON_MIDDLE then ValueSome Middle
        elif b = SDL_BUTTON_X1 then ValueSome XButton1
        elif b = SDL_BUTTON_X1 + 1uy then ValueSome XButton2
        else ValueNone

    let toPixels (x: float32) (y: float32) : PixelPoint = { X = float x * density; Y = float y * density }

    let isInside (p: PixelPoint) =
        p.X >= 0.0 && p.Y >= 0.0 && p.X < float clientSize.Width && p.Y < float clientSize.Height

    /// A cursor from an app's image: straight-alpha BGRA (SDL makes a Windows icon from it, which wants straight
    /// alpha; ours is premultiplied). 0 if it failed.
    let makeImageCursor (b: CursorBitmap) =
        if b.Width <= 0 || b.Height <= 0 then 0n
        else
            let pixels = Array.zeroCreate<byte> (b.Width * b.Height * 4)
            for i in 0..4 .. pixels.Length - 4 do
                let a = int b.Pixels[i + 3]
                if a > 0 then
                    let straight (c: byte) = byte (min 255 ((int c * 255 + a / 2) / a))
                    pixels[i] <- straight b.Pixels[i]
                    pixels[i + 1] <- straight b.Pixels[i + 1]
                    pixels[i + 2] <- straight b.Pixels[i + 2]
                    pixels[i + 3] <- byte a
            use p = fixed pixels
            // SDL copies a surface made over our memory when it makes the cursor, so the array can go after.
            let s = SDL_CreateSurfaceFrom(b.Width, b.Height, SDL_PIXELFORMAT_ARGB8888, NativePtr.toNativeInt p, b.Width * 4)
            if s = 0n then 0n
            else
                let c = SDL_CreateColorCursor(s, max 0 b.HotspotX, max 0 b.HotspotY)
                SDL_DestroySurface s
                c

    let rec cursorHandle (c: Cursor) =
        match c with
        | Cursor.Image image ->
            let h = imageCursors.GetOrAdd(struct (image.Key, scale), fun () -> makeImageCursor (image.Render scale))
            if h = 0n then cursorHandle Cursor.Default else h
        | _ -> systemCursor c

    /// SDL's cursor is the app's, not the window's: shown only while the pointer is over this window.
    let applyCursor () =
        if not closed && (SDL_GetMouseFocus() = handle || pressedButtons > 0) then
            SDL_SetCursor(cursorHandle cursor) |> ignore

    let exited kind =
        if inside then
            inside <- false
            onEvent (PointerExited(kind, 0))

    let cancelPress () =
        if pressedButtons > 0 then
            pressedButtons <- 0
            onEvent (PointerCanceled(Mouse, 0))

    let readRefreshTicks () =
        let hz = refreshRate (SDL_GetDisplayForWindow handle)
        let hz = if hz >= 24.0 && hz <= 1000.0 then hz else 60.0
        int64 (float Stopwatch.Frequency / hz)

    do
        readScale ()
        clientSize <- readPixelSize ()
        // Typed characters arrive as text input (and input methods work) only while it's on: `SetTextInput` turns
        // it on while the focused element takes text.

    member internal _.Id = id
    member internal _.Handle = handle

    member internal _.IsFrameDue = frameRequested && not closed && not clientSize.IsEmpty

    member internal _.DeliverFrame() =
        frameRequested <- false
        lastFrame <- Stopwatch.GetTimestamp()
        if not closed && not clientSize.IsEmpty then onEvent Frame

    /// A tick of SDL's timer during a modal loop (a live resize or move; `resizing`: this is the window being
    /// resized): draws what's due, as our loop would.
    member internal this.LiveTick(resizing: bool) =
        if resizing && not inLiveResize then
            inLiveResize <- true
            refreshTicks <- readRefreshTicks ()
            onEvent (LiveResize true)
        if this.IsFrameDue then this.DeliverFrame()

    /// Back in the platform's own loop: any live resize has ended.
    member internal _.EndLiveResize() =
        if inLiveResize then
            inLiveResize <- false
            onEvent (LiveResize false)

    member internal this.Resize(size: PixelSize) =
        if size <> clientSize then
            clientSize <- size
            onEvent (Resized size)
            // Draws now, unless a live resize drew less than a refresh ago (then the next tick draws it).
            if inLiveResize && Stopwatch.GetTimestamp() - lastFrame < refreshTicks * 9L / 10L then
                frameRequested <- true
            else
                this.DeliverFrame()

    member internal _.MarkDestroyed() =
        if not closed then
            closed <- true
            imageCursors.Clear()
            onDestroyed ()

    /// An event that is not this window's alone (the system's theme changed).
    member internal _.Send(e: PlatformEvent) = if not closed then onEvent e

    /// Handles an SDL event for this window, as it happens (from the platform's event filter, on the UI thread).
    member internal this.HandleEvent(e: nativeptr<SDL_Event>) =
        let t = (NativePtr.read e).Type
        if t >= SDL_EVENT_WINDOW_FIRST && t <= SDL_EVENT_WINDOW_LAST then
            let w = NativePtr.read e
            if t = SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED then
                readScale ()
                if not minimized then this.Resize { Width = w.Data1; Height = w.Data2 }
            elif t = SDL_EVENT_WINDOW_EXPOSED then
                frameRequested <- true
            elif t = SDL_EVENT_WINDOW_MOVED || t = SDL_EVENT_WINDOW_MAXIMIZED then
                onEvent Moved
            elif t = SDL_EVENT_WINDOW_MINIMIZED then
                minimized <- true
                this.Resize PixelSize.Zero
            elif t = SDL_EVENT_WINDOW_RESTORED || t = SDL_EVENT_WINDOW_SHOWN then
                if minimized then
                    minimized <- false
                    this.Resize(readPixelSize ())
                // Back from maximized too: where it is now is its normal place again.
                if t = SDL_EVENT_WINDOW_RESTORED then onEvent Moved
            elif t = SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED then
                let before = scale
                readScale ()
                if scale <> before then
                    onEvent (ScaleChanged scale)
                    // An image cursor is made per scale: show the one for the new scale.
                    match cursor with
                    | Cursor.Image _ -> applyCursor ()
                    | _ -> ()
            elif t = SDL_EVENT_WINDOW_MOUSE_ENTER then
                inside <- true
                applyCursor ()
            elif t = SDL_EVENT_WINDOW_MOUSE_LEAVE then
                // While a button is held the pointer is captured: the release reports where it ended up.
                if pressedButtons = 0 then exited Mouse
            elif t = SDL_EVENT_WINDOW_FOCUS_GAINED then
                onEvent (FocusChanged true)
            elif t = SDL_EVENT_WINDOW_FOCUS_LOST then
                cancelPress ()
                onEvent (FocusChanged false)
            elif t = SDL_EVENT_WINDOW_CLOSE_REQUESTED then
                onEvent CloseRequested
            elif t = SDL_EVENT_WINDOW_DESTROYED then
                this.MarkDestroyed()
        elif t = SDL_EVENT_MOUSE_MOTION then
            let m = NativePtr.read (NativePtr.ofNativeInt<SDL_MouseMotionEvent> (NativePtr.toNativeInt e))
            let p = toPixels m.X m.Y
            if not inside && isInside p then inside <- true
            onEvent (PointerMoved(kindOf m.Which, 0, p, modifiers ()))
        elif t = SDL_EVENT_MOUSE_BUTTON_DOWN || t = SDL_EVENT_MOUSE_BUTTON_UP then
            let b = NativePtr.read (NativePtr.ofNativeInt<SDL_MouseButtonEvent> (NativePtr.toNativeInt e))
            match buttonOf b.Button with
            | ValueSome button ->
                let kind = kindOf b.Which
                let p = toPixels b.X b.Y
                if t = SDL_EVENT_MOUSE_BUTTON_DOWN then
                    // SDL captures the mouse while a button is down (SDL_HINT_MOUSE_AUTO_CAPTURE), so drags that
                    // leave the window keep reporting.
                    pressedButtons <- pressedButtons + 1
                    inside <- true
                    onEvent (PointerPressed(kind, 0, p, button, modifiers ()))
                else
                    let wasPressed = pressedButtons > 0
                    pressedButtons <- max 0 (pressedButtons - 1)
                    onEvent (PointerReleased(kind, 0, p, button, modifiers ()))
                    // Released outside after a drag, or a finger lifted: the pointer has left.
                    if kind = Touch then exited kind
                    elif wasPressed && pressedButtons = 0 && not (isInside p) then exited kind
            | ValueNone -> ()
        elif t = SDL_EVENT_MOUSE_WHEEL then
            let w = NativePtr.read (NativePtr.ofNativeInt<SDL_MouseWheelEvent> (NativePtr.toNativeInt e))
            // Notches, positive away from the user and to the right, as Scribble's are (fractions on precise wheels).
            onEvent (PointerWheel(toPixels w.MouseX w.MouseY, float w.X, float w.Y, modifiers ()))
        elif t = SDL_EVENT_KEY_DOWN || t = SDL_EVENT_KEY_UP then
            let k = NativePtr.read (NativePtr.ofNativeInt<SDL_KeyboardEvent> (NativePtr.toNativeInt e))
            if t = SDL_EVENT_KEY_DOWN then
                if logText then log ("key " + string (keyOf k.Key))
                onEvent (KeyDown(keyOf k.Key, modifiersOf k.Mod, k.Repeat <> 0uy))
            else onEvent (KeyUp(keyOf k.Key, modifiersOf k.Mod))
        elif t = SDL_EVENT_TEXT_INPUT then
            let text = readUtf8 (NativePtr.read (NativePtr.ofNativeInt<SDL_TextInputEvent> (NativePtr.toNativeInt e))).Text
            log ("input \"" + text + "\"")
            // Control characters (backspace, enter, escape) are reported as KeyDown, not text.
            if text.Length > 0 && not (Char.IsControl text[0]) then onEvent (TextInput text)
        elif t = SDL_EVENT_TEXT_EDITING then
            let editing = NativePtr.read (NativePtr.ofNativeInt<SDL_TextEditingEvent> (NativePtr.toNativeInt e))
            let text = readUtf8 editing.Text
            log ("editing \"" + text + "\" start " + string editing.Start + " length " + string editing.Length)
            // The method's caret is counted in characters: as a UTF-16 index, the end when it isn't said.
            let caret =
                if editing.Start < 0 then text.Length
                else
                    let mutable index = 0
                    let mutable left = editing.Start
                    while left > 0 && index < text.Length do
                        index <- index + (if Char.IsHighSurrogate text[index] && index + 1 < text.Length then 2 else 1)
                        left <- left - 1
                    index
            onEvent (TextComposition(text, caret))

    interface IWindowPlacement with
        member _.Position =
            let mutable x = 0
            let mutable y = 0
            SDL_GetWindowPosition(handle, &&x, &&y) |> ignore
            struct (x, y)
        member _.IsMaximized = not closed && SDL_GetWindowFlags handle &&& SDL_WINDOW_MAXIMIZED <> 0UL
        member _.IsMinimized = minimized || (not closed && SDL_GetWindowFlags handle &&& SDL_WINDOW_MINIMIZED <> 0UL)

    interface IWindow with
        member _.ClientSize = clientSize
        member _.Scale = scale
        member _.Surface = surface
        member _.IsClosed = closed

        member _.Title
            with get () = title
            and set value =
                title <- value
                if not closed then withUtf8 value (fun p -> SDL_SetWindowTitle(handle, p) |> ignore)

        member _.RequestFrame() = if not closed then frameRequested <- true

        member _.SetCursor(value: Cursor) =
            if value <> cursor then
                cursor <- value
                applyCursor ()

        // On the desktop the platform has no shortcuts of its own on keys an app would claim.
        member _.ClaimKey(_: Key, _: Modifiers) = ()

        // Nothing shows around the app's drawing in a desktop window.
        member _.SetBackground(_: byte, _: byte, _: byte) = ()

        member _.SetTextInput(request: TextInputRequest voption) =
            if not closed then
                match request with
                | ValueSome r ->
                    if not textInputOn then
                        textInputOn <- true
                        // The kind of text matters where SDL shows an on-screen keyboard; a desktop takes it as told.
                        let props = SDL_CreateProperties()
                        let kind =
                            match r.Keyboard with
                            | KeyboardKind.Email -> 2L
                            | KeyboardKind.Password -> 4L
                            | KeyboardKind.Number | KeyboardKind.Decimal | KeyboardKind.Phone -> 6L
                            | _ -> 0L
                        withUtf8 "SDL.textinput.type" (fun name -> SDL_SetNumberProperty(props, name, kind) |> ignore)
                        withUtf8 "SDL.textinput.multiline" (fun name -> SDL_SetBooleanProperty(props, name, (if r.Multiline then 1uy else 0uy)) |> ignore)
                        let started = SDL_StartTextInputWithProperties(handle, props)
                        log ("start: " + (if started <> 0uy then "ok" else "FAILED: " + lastError ()))
                        SDL_DestroyProperties props
                    // Where the caret is, in window coordinates: an input method's candidates show beside it.
                    let mutable rect = SDL_Rect()
                    rect.X <- int (Math.Floor(r.CaretX / density))
                    rect.Y <- int (Math.Floor(r.CaretY / density))
                    rect.W <- max 1 (int (Math.Ceiling(r.CaretWidth / density)))
                    rect.H <- max 1 (int (Math.Ceiling(r.CaretHeight / density)))
                    SDL_SetTextInputArea(handle, &&rect, 0) |> ignore
                    log ("area " + string rect.X + ", " + string rect.Y + ", " + string rect.W + " x " + string rect.H)
                | ValueNone ->
                    if textInputOn then
                        textInputOn <- false
                        log "stop"
                        // Stopping also drops what was being composed.
                        SDL_StopTextInput handle |> ignore

        member _.ClearComposition() =
            if not closed && textInputOn then SDL_ClearComposition handle |> ignore

        member _.Show() =
            if not closed then SDL_ShowWindow handle |> ignore

        member this.Close() =
            if not closed then
                // SDL destroys this window's own windows (the ones it owns) first, each with its DESTROYED event.
                SDL_DestroyWindow handle
                this.MarkDestroyed()

        member this.Dispose() = (this :> IWindow).Close()
