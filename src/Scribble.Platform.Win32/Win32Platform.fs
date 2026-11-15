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

    // An exception must never unwind through native frames, so the window procedure catches it,
    // stores it, and the message loop rethrows it with its original stack trace.
    let wndProc =
        WndProc(fun hwnd msg wParam lParam ->
            try
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
        use titlePtr = fixed options.Title

        let hwnd =
            CreateWindowExW(
                0u,
                classNamePtr,
                NativeInterop.NativePtr.toNativeInt titlePtr,
                style,
                CW_USEDEFAULT,
                CW_USEDEFAULT,
                w,
                h,
                0n,
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
        member _.CreateWindow(options, onEvent) = createWindow options onEvent
        member _.Run() = run ()

        member _.Dispose() =
            if not disposed then
                disposed <- true
                for w in List.ofSeq windows.Values do
                    (w :> IWindow).Close()
                UnregisterClassW(classNamePtr, hinstance) |> ignore
                Marshal.FreeHGlobal classNamePtr
                GC.KeepAlive wndProc
