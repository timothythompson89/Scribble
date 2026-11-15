namespace Scribble.Platform.Win32

#nowarn "9"

open System
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Platform.Win32.Native

[<AutoOpen>]
module internal Win32WindowConstants =
    /// Timer that drives frames during Windows' modal size/move loop.
    let SizeMoveTimerId = 1n

/// A top-level Win32 window. Created by Win32Platform; all calls must happen on the UI thread.
[<Sealed>]
type Win32Window internal (hinstance: nativeint, onEvent: PlatformEvent -> unit, onDestroyed: nativeint -> unit) =
    let mutable hwnd = 0n
    let mutable title = ""
    let mutable dpi = USER_DEFAULT_SCREEN_DPI
    let mutable clientSize = PixelSize.Zero
    let mutable closed = false
    let mutable pressedButtons = 0
    let mutable frameRequested = false
    /// WM_CHAR delivers UTF-16 code units; a character outside the BMP arrives as two messages.
    let mutable pendingHighSurrogate = None

    let scale () = float dpi / float USER_DEFAULT_SCREEN_DPI

    let readClientSize () =
        let mutable r = Unchecked.defaultof<RECT>
        GetClientRect(hwnd, &&r) |> ignore
        ({ Width = r.Width; Height = r.Height }: PixelSize)

    let mousePosition (lParam: nativeint) =
        ({ X = float (signedLoWord lParam); Y = float (signedHiWord lParam) }: PixelPoint)

    let pressed button lParam =
        // Capture the mouse while any button is down so drags that leave the window keep reporting.
        if pressedButtons = 0 then SetCapture hwnd |> ignore
        pressedButtons <- pressedButtons + 1
        onEvent (PointerPressed(Mouse, 0, mousePosition lParam, button))

    let released button lParam =
        pressedButtons <- max 0 (pressedButtons - 1)
        if pressedButtons = 0 then ReleaseCapture() |> ignore
        onEvent (PointerReleased(Mouse, 0, mousePosition lParam, button))

    let wheel (wParam: nativeint) (lParam: nativeint) horizontal =
        // Wheel positions arrive in screen coordinates.
        let mutable p: POINT = { X = signedLoWord lParam; Y = signedHiWord lParam }
        ScreenToClient(hwnd, &&p) |> ignore
        let delta = float (signedHiWord wParam) / WHEEL_DELTA
        let position: PixelPoint = { X = float p.X; Y = float p.Y }
        if horizontal then
            onEvent (PointerWheel(position, delta, 0.0))
        else
            onEvent (PointerWheel(position, 0.0, delta))

    member internal _.Attach(handle: nativeint) =
        hwnd <- handle
        dpi <- GetDpiForWindow handle
        clientSize <- readClientSize ()

    member internal _.Handle = hwnd

    /// True when a frame was requested and there is something to draw into.
    member internal _.IsFrameDue = frameRequested && not closed && not clientSize.IsEmpty

    member internal _.DeliverFrame() =
        frameRequested <- false
        if not closed && not clientSize.IsEmpty then onEvent Frame

    /// Handles a message for this window. Returns Some result when handled, None to fall through
    /// to DefWindowProc.
    member internal this.HandleMessage(msg: uint32, wParam: nativeint, lParam: nativeint) : nativeint voption =
        match msg with
        | WM_SIZE ->
            let size: PixelSize = { Width = loWord lParam; Height = hiWord lParam }
            if size <> clientSize then
                clientSize <- size
                onEvent (Resized size)
                // Draw now rather than at the next loop iteration: during a live resize Windows runs
                // its own modal loop, and drawing here keeps content in step with the window edge.
                this.DeliverFrame()
            ValueSome 0n

        | WM_PAINT ->
            // The OS wants pixels (window uncovered, restored, first shown). We draw with the GPU,
            // so just mark the area valid and schedule a frame.
            ValidateRect(hwnd, 0n) |> ignore
            frameRequested <- true
            ValueSome 0n

        // While the user drags or resizes the window, Windows runs a modal loop and our message
        // loop is blocked. A timer keeps requested frames (animations) flowing during that time.
        | WM_TIMER when wParam = SizeMoveTimerId ->
            if this.IsFrameDue then this.DeliverFrame()
            ValueSome 0n

        | WM_ENTERSIZEMOVE ->
            SetTimer(hwnd, SizeMoveTimerId, 10u, 0n) |> ignore
            onEvent (LiveResize true)
            ValueSome 0n

        | WM_EXITSIZEMOVE ->
            KillTimer(hwnd, SizeMoveTimerId) |> ignore
            onEvent (LiveResize false)
            ValueSome 0n

        | WM_DPICHANGED ->
            dpi <- uint32 (loWord wParam)
            onEvent (ScaleChanged(scale ()))
            // Windows suggests a rect that keeps the window the same physical size in DIPs.
            let suggested = NativePtr.read (NativePtr.ofNativeInt<RECT> lParam)
            SetWindowPos(
                hwnd,
                0n,
                suggested.Left,
                suggested.Top,
                suggested.Width,
                suggested.Height,
                SWP_NOZORDER ||| SWP_NOACTIVATE
            )
            |> ignore
            ValueSome 0n

        // Let Windows fill areas exposed before our next frame with the class brush (black),
        // which is far less visible than the default white.
        | WM_ERASEBKGND -> ValueNone

        | WM_CLOSE ->
            onEvent CloseRequested
            ValueSome 0n

        | WM_DESTROY ->
            closed <- true
            onDestroyed hwnd
            ValueSome 0n

        | WM_SETFOCUS ->
            onEvent (FocusChanged true)
            ValueSome 0n

        | WM_KILLFOCUS ->
            pressedButtons <- 0
            onEvent (FocusChanged false)
            ValueSome 0n

        | WM_MOUSEMOVE ->
            onEvent (PointerMoved(Mouse, 0, mousePosition lParam))
            ValueSome 0n

        | WM_LBUTTONDOWN -> pressed Left lParam; ValueSome 0n
        | WM_LBUTTONUP -> released Left lParam; ValueSome 0n
        | WM_RBUTTONDOWN -> pressed Right lParam; ValueSome 0n
        | WM_RBUTTONUP -> released Right lParam; ValueSome 0n
        | WM_MBUTTONDOWN -> pressed Middle lParam; ValueSome 0n
        | WM_MBUTTONUP -> released Middle lParam; ValueSome 0n
        | WM_XBUTTONDOWN ->
            pressed (if hiWord wParam = 1 then XButton1 else XButton2) lParam
            ValueSome 1n
        | WM_XBUTTONUP ->
            released (if hiWord wParam = 1 then XButton1 else XButton2) lParam
            ValueSome 1n

        | WM_MOUSEWHEEL -> wheel wParam lParam false; ValueSome 0n
        | WM_MOUSEHWHEEL -> wheel wParam lParam true; ValueSome 0n

        | WM_KEYDOWN
        | WM_SYSKEYDOWN ->
            let isRepeat = (int64 lParam &&& (1L <<< 30)) <> 0L
            onEvent (KeyDown(int wParam, isRepeat))
            // Let system keys (Alt+F4, Alt+Space) keep working.
            if msg = WM_SYSKEYDOWN then ValueNone else ValueSome 0n

        | WM_KEYUP
        | WM_SYSKEYUP ->
            onEvent (KeyUp(int wParam))
            if msg = WM_SYSKEYUP then ValueNone else ValueSome 0n

        | WM_CHAR ->
            let unit = char (int wParam &&& 0xFFFF)
            if Char.IsHighSurrogate unit then
                pendingHighSurrogate <- Some unit
            else
                let text =
                    match pendingHighSurrogate with
                    | Some high when Char.IsLowSurrogate unit -> String([| high; unit |])
                    | _ -> string unit
                pendingHighSurrogate <- None
                // Control characters (backspace, enter, escape) are reported as KeyDown, not text.
                if not (Char.IsControl text[0]) then onEvent (TextInput text)
            ValueSome 0n

        | _ -> ValueNone

    interface IWindow with
        member _.ClientSize = clientSize
        member _.Scale = scale ()
        member _.Surface = Win32Handle(hinstance, hwnd)
        member _.IsClosed = closed

        member _.Title
            with get () = title
            and set value =
                title <- value
                if not closed then
                    use p = fixed value
                    SetWindowTextW(hwnd, p) |> ignore

        member _.RequestFrame() = if not closed then frameRequested <- true

        member _.Show() =
            if not closed then ShowWindow(hwnd, SW_SHOW) |> ignore

        member _.Close() =
            if not closed then DestroyWindow hwnd |> ignore

        member this.Dispose() = (this :> IWindow).Close()
