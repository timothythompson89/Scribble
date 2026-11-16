namespace Scribble.Platform.Win32

#nowarn "9"

open System
open System.Runtime.InteropServices
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
    /// True while Windows is watching for the mouse to leave (TrackMouseEvent), i.e. it's inside.
    let mutable trackingLeave = false
    let mutable cursor = Cursor.Default
    let cursorHandles = System.Collections.Generic.Dictionary<Cursor, nativeint>()
    /// Image cursors by key and DPI: the 32 used most recently (each costs one USER and three GDI
    /// handles of the process's 10,000 each, so they can't pile up); the rest destroyed, and all of
    /// them with the window.
    let imageCursors =
        RecentCache<struct (string * uint32), nativeint>(32, fun h -> if h <> 0n then DestroyIcon h |> ignore)

    let scale () = float dpi / float USER_DEFAULT_SCREEN_DPI

    let readClientSize () =
        let mutable r = Unchecked.defaultof<RECT>
        GetClientRect(hwnd, &&r) |> ignore
        ({ Width = r.Width; Height = r.Height }: PixelSize)

    let mousePosition (lParam: nativeint) =
        ({ X = float (signedLoWord lParam); Y = float (signedHiWord lParam) }: PixelPoint)

    let modifiers () =
        let down vk = GetKeyState vk < 0s
        (if down VK_SHIFT then Modifiers.Shift else Modifiers.None)
        ||| (if down VK_CONTROL then Modifiers.Control else Modifiers.None)
        ||| (if down VK_MENU then Modifiers.Alt else Modifiers.None)
        ||| (if down VK_LWIN || down VK_RWIN then Modifiers.Meta else Modifiers.None)

    /// A Win32 virtual key as a portable Key: named keys by code, the rest through the active layout.
    let keyOf (vk: int) : Key =
        match vk with
        | 0x08 -> Key.Backspace
        | 0x09 -> Key.Tab
        | 0x0D -> Key.Enter
        | 0x10 | 0xA0 | 0xA1 -> Key.Shift
        | 0x11 | 0xA2 | 0xA3 -> Key.Control
        | 0x12 | 0xA4 | 0xA5 -> Key.Alt
        | 0x14 -> Key.CapsLock
        | 0x1B -> Key.Escape
        | 0x20 -> Key.Space
        | 0x21 -> Key.PageUp
        | 0x22 -> Key.PageDown
        | 0x23 -> Key.End
        | 0x24 -> Key.Home
        | 0x25 -> Key.Left
        | 0x26 -> Key.Up
        | 0x27 -> Key.Right
        | 0x28 -> Key.Down
        | 0x2D -> Key.Insert
        | 0x2E -> Key.Delete
        | 0x5B | 0x5C -> Key.Meta
        | vk when vk >= 0x70 && vk <= 0x87 -> Key.F(vk - 0x6F)
        | vk ->
            let c = MapVirtualKeyW(uint32 vk, 2u) &&& 0x7FFFFFFFu
            if c > 0u && c < 0xFFFFu then Key.Char(Char.ToLowerInvariant(char c))
            else Key.Other("VK" + vk.ToString("X2"))

    /// A cursor from an app's image: straight-alpha 32-bit color plus an all-zero mask. 0 if it failed.
    let makeImageCursor (b: CursorBitmap) =
        if b.Width <= 0 || b.Height <= 0 then 0n
        else
            let mutable header =
                { biSize = uint32 sizeof<BITMAPINFOHEADER>; biWidth = b.Width; biHeight = -b.Height; biPlanes = 1us
                  biBitCount = 32us; biCompression = BI_RGB; biSizeImage = 0u; biXPelsPerMeter = 0
                  biYPelsPerMeter = 0; biClrUsed = 0u; biClrImportant = 0u }
            let mutable bits = 0n
            let color = CreateDIBSection(0n, &&header, DIB_RGB_COLORS, &&bits, 0n, 0u)
            if color = 0n then 0n
            else
                // Windows wants straight alpha in cursors; ours is premultiplied.
                let pixels = Array.zeroCreate<byte> (b.Width * b.Height * 4)
                for i in 0..4 .. pixels.Length - 4 do
                    let a = int b.Pixels[i + 3]
                    if a > 0 then
                        let straight (c: byte) = byte (min 255 ((int c * 255 + a / 2) / a))
                        pixels[i] <- straight b.Pixels[i]
                        pixels[i + 1] <- straight b.Pixels[i + 1]
                        pixels[i + 2] <- straight b.Pixels[i + 2]
                        pixels[i + 3] <- byte a
                Marshal.Copy(pixels, 0, bits, pixels.Length)
                // Monochrome rows are padded to 16 bits.
                let maskBytes = Array.zeroCreate<byte> (((b.Width + 15) / 16) * 2 * b.Height)
                use maskPtr = fixed maskBytes
                let mask = CreateBitmap(b.Width, b.Height, 1u, 1u, NativePtr.toNativeInt maskPtr)
                let mutable info =
                    { fIcon = 0; xHotspot = uint32 (max 0 b.HotspotX); yHotspot = uint32 (max 0 b.HotspotY)
                      hbmMask = mask; hbmColor = color }
                let cursor = CreateIconIndirect(&&info)
                DeleteObject color |> ignore
                DeleteObject mask |> ignore
                cursor

    let rec cursorHandle (c: Cursor) =
        match c with
        | Cursor.Image image ->
            // Made for this DPI the first time it's needed here, then kept while used recently.
            // The one asked for last (the one showing) is never the one destroyed.
            let h = imageCursors.GetOrAdd(struct (image.Key, dpi), fun () -> makeImageCursor (image.Render(scale ())))
            if h = 0n then cursorHandle Cursor.Default else h
        | _ ->
        match cursorHandles.TryGetValue c with
        | true, h -> h
        | _ ->
            let id =
                match c with
                | Cursor.Default -> IDC_ARROW
                | Cursor.Hand -> IDC_HAND
                | Cursor.Text -> IDC_IBEAM
                | Cursor.Crosshair -> IDC_CROSS
                | Cursor.Move -> IDC_SIZEALL
                | Cursor.ResizeDiagonal -> IDC_SIZENWSE
                | Cursor.ResizeLeftRight -> IDC_SIZEWE
                | Cursor.ResizeUpDown -> IDC_SIZENS
                | Cursor.NotAllowed -> IDC_NO
                | Cursor.Wait -> IDC_WAIT
                | Cursor.Image _ -> IDC_ARROW
            let h = LoadCursorW(0n, id)
            cursorHandles[c] <- h
            h

    let isInside (p: PixelPoint) =
        p.X >= 0.0 && p.Y >= 0.0 && p.X < float clientSize.Width && p.Y < float clientSize.Height

    let pressed button lParam =
        // Capture the mouse while any button is down so drags that leave the window keep reporting.
        if pressedButtons = 0 then SetCapture hwnd |> ignore
        pressedButtons <- pressedButtons + 1
        onEvent (PointerPressed(Mouse, 0, mousePosition lParam, button, modifiers ()))

    let released button lParam =
        let wasPressed = pressedButtons > 0
        pressedButtons <- max 0 (pressedButtons - 1)
        // Zero before ReleaseCapture, so WM_CAPTURECHANGED knows this release was ours.
        if pressedButtons = 0 then ReleaseCapture() |> ignore
        let position = mousePosition lParam
        onEvent (PointerReleased(Mouse, 0, position, button, modifiers ()))
        // Released outside the window after a drag: the pointer has left (Windows doesn't say so).
        if wasPressed && pressedButtons = 0 && not (isInside position) then
            trackingLeave <- false
            onEvent (PointerExited(Mouse, 0))

    let wheel (wParam: nativeint) (lParam: nativeint) horizontal =
        // Wheel positions arrive in screen coordinates.
        let mutable p: POINT = { X = signedLoWord lParam; Y = signedHiWord lParam }
        ScreenToClient(hwnd, &&p) |> ignore
        let delta = float (signedHiWord wParam) / WHEEL_DELTA
        let position: PixelPoint = { X = float p.X; Y = float p.Y }
        if horizontal then
            onEvent (PointerWheel(position, delta, 0.0, modifiers ()))
        else
            onEvent (PointerWheel(position, 0.0, delta, modifiers ()))

    /// The press ended without a release we'll see (focus or capture went elsewhere).
    let cancelPress () =
        if pressedButtons > 0 then
            pressedButtons <- 0
            if GetCapture() = hwnd then ReleaseCapture() |> ignore
            onEvent (PointerCanceled(Mouse, 0))

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
            // An image cursor is made per scale: show the one for the new scale.
            match cursor with
            | Cursor.Image _ when trackingLeave || pressedButtons > 0 -> SetCursor(cursorHandle cursor) |> ignore
            | _ -> ()
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
            // Not shown any more: the window is gone. (Windows frees what the system loads itself.)
            imageCursors.Clear()
            onDestroyed hwnd
            ValueSome 0n

        | WM_SETFOCUS ->
            onEvent (FocusChanged true)
            ValueSome 0n

        | WM_KILLFOCUS ->
            cancelPress ()
            onEvent (FocusChanged false)
            ValueSome 0n

        // Another window took the mouse in the middle of a press (our own release zeroes the count first).
        | WM_CAPTURECHANGED ->
            if lParam <> hwnd then cancelPress ()
            ValueSome 0n

        | WM_MOUSEMOVE ->
            if not trackingLeave then
                let mutable t = { cbSize = uint32 sizeof<TRACKMOUSEEVENT>; dwFlags = TME_LEAVE; hwndTrack = hwnd; dwHoverTime = 0u }
                trackingLeave <- TrackMouseEvent(&&t) <> 0
            onEvent (PointerMoved(Mouse, 0, mousePosition lParam, modifiers ()))
            ValueSome 0n

        | WM_MOUSELEAVE ->
            trackingLeave <- false
            // While a button is held the pointer is captured: the release reports where it ended up.
            if pressedButtons = 0 then onEvent (PointerExited(Mouse, 0))
            ValueSome 0n

        | WM_SETCURSOR when loWord lParam = HTCLIENT ->
            SetCursor(cursorHandle cursor) |> ignore
            ValueSome 1n

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
            onEvent (KeyDown(keyOf (int wParam), modifiers (), isRepeat))
            // Let system keys (Alt+F4, Alt+Space) keep working.
            if msg = WM_SYSKEYDOWN then ValueNone else ValueSome 0n

        | WM_KEYUP
        | WM_SYSKEYUP ->
            onEvent (KeyUp(keyOf (int wParam), modifiers ()))
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

        member _.SetCursor(value: Cursor) =
            if value <> cursor then
                cursor <- value
                // Change it now if the pointer is over us; otherwise WM_SETCURSOR applies it on entry.
                if not closed && (trackingLeave || pressedButtons > 0) then SetCursor(cursorHandle value) |> ignore

        // Windows has no shortcuts of its own on keys an app would claim (F10 and Alt only matter to
        // windows with menus, and this one has none).
        member _.ClaimKey(_: Key, _: Modifiers) = ()

        member _.Show() =
            if not closed then ShowWindow(hwnd, SW_SHOW) |> ignore

        member _.Close() =
            if not closed then DestroyWindow hwnd |> ignore

        member this.Dispose() = (this :> IWindow).Close()
