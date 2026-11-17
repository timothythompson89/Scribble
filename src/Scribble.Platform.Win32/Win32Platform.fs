namespace Scribble.Platform.Win32

#nowarn "9"

open System
open System.Collections.Generic
open System.Runtime.ExceptionServices
open System.Runtime.InteropServices
open Scribble.Platform
open Scribble.Platform.Win32.Native

/// The Win32 windowing backend. Create one per process, on the thread that will be the UI thread.
[<Sealed>]
type Win32Platform() =
    static let className = "ScribbleWindow"

    let hinstance = GetModuleHandleW 0n
    let windows = Dictionary<nativeint, Win32Window>()
    let classNamePtr = Marshal.StringToHGlobalUni className
    let mutable pendingException: ExceptionDispatchInfo option = None
    let mutable disposed = false
    /// A hidden message-only window that receives wake messages. Posted to a window (not the
    /// thread) so they still arrive during the modal loop Windows runs while a window is dragged
    /// or resized.
    let mutable wakeWindow = 0n
    let mutable wakeHandler: unit -> unit = ignore
    /// Time for `Elapsed`, and work for `After`, run by one timer on the wake window.
    let clock = Diagnostics.Stopwatch.StartNew()
    let timers = TimerQueue()
    let wakeTimerId = 1n

    /// Sets the wake window's timer for the earliest work (or stops it when there is none).
    let armTimer () =
        if wakeWindow <> 0n then
            match timers.NextDue with
            | ValueSome due ->
                let ms = Math.Ceiling (due - clock.Elapsed).TotalMilliseconds
                SetTimer(wakeWindow, wakeTimerId, uint32 (Math.Clamp(ms, 1.0, float Int32.MaxValue)), 0n) |> ignore
            | ValueNone -> KillTimer(wakeWindow, wakeTimerId) |> ignore

    // An exception must never unwind through native frames, so the window procedure catches it,
    // stores it, and the message loop rethrows it with its original stack trace.
    let wndProc =
        WndProc(fun hwnd msg wParam lParam ->
            try
                if hwnd = wakeWindow && msg = WM_SCRIBBLE_WAKE then
                    wakeHandler ()
                    0n
                elif hwnd = wakeWindow && msg = WM_TIMER && wParam = wakeTimerId then
                    try
                        timers.RunDue clock.Elapsed
                    finally
                        armTimer ()
                    0n
                else
                    match windows.TryGetValue hwnd with
                    | true, window ->
                        match window.HandleMessage(msg, wParam, lParam) with
                        | ValueSome result -> result
                        | ValueNone -> DefWindowProcW(hwnd, msg, wParam, lParam)
                    | _ -> DefWindowProcW(hwnd, msg, wParam, lParam)
            with ex ->
                if pendingException.IsNone then pendingException <- Some(ExceptionDispatchInfo.Capture ex)
                DefWindowProcW(hwnd, msg, wParam, lParam))

    // The delegate must outlive the window class, so it is kept in a field and never collected early.
    let wndProcPtr = Marshal.GetFunctionPointerForDelegate<WndProc> wndProc

    do
        if not (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063)) then
            raise (PlatformNotSupportedException "Scribble needs Windows 10 version 1703 or later.")

        // Per-monitor DPI awareness v2: we get WM_DPICHANGED and draw at the monitor's real resolution.
        // Fails harmlessly if an app manifest already set the awareness.
        SetProcessDpiAwarenessContext DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 |> ignore

        let mutable wc =
            { cbSize = uint32 sizeof<WNDCLASSEXW>
              style = 0u
              lpfnWndProc = wndProcPtr
              cbClsExtra = 0
              cbWndExtra = 0
              hInstance = hinstance
              hIcon = 0n
              hCursor = LoadCursorW(0n, IDC_ARROW)
              hbrBackground = GetStockObject BLACK_BRUSH
              lpszMenuName = 0n
              lpszClassName = classNamePtr
              hIconSm = 0n }

        if RegisterClassExW(&&wc) = 0us then
            raise (InvalidOperationException $"RegisterClassExW failed (error {Marshal.GetLastPInvokeError()}).")

    let rethrowPending () =
        match pendingException with
        | Some info ->
            pendingException <- None
            info.Throw()
        | None -> ()

    let createWindow (options: WindowOptions) (onEvent: PlatformEvent -> unit) =
        if disposed then raise (ObjectDisposedException(nameof Win32Platform))

        let style =
            if options.Resizable then WS_OVERLAPPEDWINDOW
            else WS_OVERLAPPEDWINDOW &&& ~~~(WS_THICKFRAME ||| WS_MAXIMIZEBOX)

        // Turn the requested client size (DIPs) into an outer window size (pixels) at a given DPI.
        let outerSize dpi =
            let scale = float dpi / float USER_DEFAULT_SCREEN_DPI
            let mutable r =
                { Left = 0
                  Top = 0
                  Right = int (Math.Round(options.Size.Width * scale))
                  Bottom = int (Math.Round(options.Size.Height * scale)) }
            AdjustWindowRectExForDpi(&&r, style, 0, 0u, dpi) |> ignore
            r.Width, r.Height

        let window = new Win32Window(hinstance, onEvent, fun h -> windows.Remove h |> ignore)
        let w, h = outerSize (GetDpiForSystem())
        // Pinned with a handle, not `fixed` (see HarfBuzzShaper: a background GC misread a `fixed`
        // string pin); CreateWindowExW calls back into managed code, so a GC can happen meanwhile.
        let titlePin = GCHandle.Alloc(options.Title, GCHandleType.Pinned)
        use _titleFree = { new IDisposable with member _.Dispose() = titlePin.Free() }
        // An owned window stays above its owner, minimizes with it and has no taskbar button.
        let owner =
            match options.Owner with
            | ValueSome o ->
                match o.Surface with
                | Win32Handle(_, ownerHwnd) when not o.IsClosed -> ownerHwnd
                | _ -> 0n
            | ValueNone -> 0n

        let hwnd =
            CreateWindowExW(
                0u,
                classNamePtr,
                titlePin.AddrOfPinnedObject(),
                style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                w,
                h,
                owner,
                0n,
                hinstance,
                0n
            )

        if hwnd = 0n then
            raise (InvalidOperationException $"CreateWindowExW failed (error {Marshal.GetLastPInvokeError()}).")

        windows[hwnd] <- window
        window.Attach hwnd
        (window :> IWindow).Title <- options.Title

        // The window may have opened on a monitor whose DPI differs from the system DPI.
        let actualDpi = GetDpiForWindow hwnd
        if actualDpi <> GetDpiForSystem() then
            let w, h = outerSize actualDpi
            SetWindowPos(hwnd, 0n, 0, 0, w, h, SWP_NOMOVE ||| SWP_NOZORDER ||| SWP_NOACTIVATE) |> ignore

        rethrowPending ()
        window :> IWindow

    let ensureWakeWindow () =
        if wakeWindow = 0n then
            wakeWindow <- CreateWindowExW(0u, classNamePtr, 0n, 0u, 0, 0, 0, 0, HWND_MESSAGE, 0n, hinstance, 0n)
            if wakeWindow = 0n then
                raise (InvalidOperationException $"Creating the wake window failed (error {Marshal.GetLastPInvokeError()}).")

    let run () =
        let mutable msg = Unchecked.defaultof<MSG>
        let mutable quit = false

        while not quit && windows.Count > 0 do
            while not quit && PeekMessageW(&&msg, 0n, 0u, 0u, PM_REMOVE) <> 0 do
                if msg.message = WM_QUIT then
                    quit <- true
                else
                    TranslateMessage(&&msg) |> ignore
                    DispatchMessageW(&&msg) |> ignore
                    rethrowPending ()

            if not quit && windows.Count > 0 then
                let due = windows.Values |> Seq.filter _.IsFrameDue |> List.ofSeq
                if due.IsEmpty then
                    // Nothing to draw: sleep until the OS has a message for us. This is what keeps
                    // an idle Scribble app at zero CPU and GPU.
                    WaitMessage() |> ignore
                else
                    // Presenting with vsync paces this loop to the display's refresh rate.
                    for window in due do
                        window.DeliverFrame()
                        rethrowPending ()

    interface IPlatform with
        member _.Name = "Win32"
        member _.SupportsMultipleWindows = true
        member _.CreateWindow(options, onEvent) = createWindow options onEvent
        member _.Run() = run ()

        member _.SetWakeHandler(handler) =
            wakeHandler <- handler
            ensureWakeWindow ()

        member _.Elapsed = clock.Elapsed

        member _.After(delay, action) =
            ensureWakeWindow ()
            if timers.Add(clock.Elapsed + delay, action) then armTimer ()

        member _.Wake() =
            if wakeWindow <> 0n then PostMessageW(wakeWindow, WM_SCRIBBLE_WAKE, 0n, 0n) |> ignore

        member _.Dispose() =
            if not disposed then
                disposed <- true
                if wakeWindow <> 0n then
                    DestroyWindow wakeWindow |> ignore
                    wakeWindow <- 0n
                for w in List.ofSeq windows.Values do
                    (w :> IWindow).Close()
                UnregisterClassW(classNamePtr, hinstance) |> ignore
                Marshal.FreeHGlobal classNamePtr
                GC.KeepAlive wndProc
