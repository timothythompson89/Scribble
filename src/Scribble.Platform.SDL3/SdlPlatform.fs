namespace Scribble.Platform.SDL3

#nowarn "9"

open System
open System.Collections.Generic
open System.Runtime.ExceptionServices
open System.Threading
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Platform.SDL3.Native

/// The SDL3 windowing backend (DESIGN.md, "Platform layer: SDL3 and AccessKit"): windows, input, cursors and the
/// event loop, through SDL. Create one per process, on the thread that will be the UI thread.
///
/// SDL's events are handled as they happen, in an event filter, like a Win32 window procedure: during Windows' modal
/// size-move loop our loop doesn't run, and SDL's live-resize ticks reach the filter. The queue carries only one
/// event of ours, which wakes the loop (`Wake`, or something handled in the filter that needs the loop: a frame, a
/// new timer).
[<Sealed>]
type SdlPlatform() =
    let uiThread = Environment.CurrentManagedThreadId
    let windows = Dictionary<uint32, SdlWindow>()
    let clock = Diagnostics.Stopwatch.StartNew()
    let timers = TimerQueue()
    let systemCursors = Dictionary<Cursor, nativeint>()
    let mutable wakeHandler: unit -> unit = ignore
    let mutable pendingException: ExceptionDispatchInfo option = None
    let mutable disposed = false
    /// 1 while a wake is asked for and not yet handled (set from any thread).
    let mutable wakeRequested = 0
    /// 1 while our event is in SDL's queue: at most one at a time.
    let mutable queued = 0

    do
        // Before SDL_Init. SDL turns the screensaver off by default (it's for games); a UI framework leaves it on.
        setHint "SDL_VIDEO_ALLOW_SCREENSAVER" "1"
        // Alt+Space opens the system menu, as in any Windows app (SDL stops it by default).
        setHint "SDL_WINDOWS_ENABLE_MENU_MNEMONICS" "1"
        if SDL_Init SDL_INIT_VIDEO = 0uy then
            raise (InvalidOperationException("SDL_Init failed: " + lastError ()))

    let ourEvent = SDL_RegisterEvents 1

    /// Puts our event in SDL's queue unless it's there already. SDL calls the event filter while pushing, holding
    /// its event-watch lock: from another thread, a thread-pool item pushes it, so the caller (an image decoder, a
    /// task) never waits on the UI thread's filter.
    let enqueue () =
        if Interlocked.Exchange(&queued, 1) = 0 then
            let push () =
                if not disposed then
                    let mutable e = SDL_Event()
                    e.Type <- ourEvent
                    SDL_PushEvent(&&e) |> ignore
            if Environment.CurrentManagedThreadId = uiThread then push ()
            else ThreadPool.UnsafeQueueUserWorkItem((fun _ -> push ()), null) |> ignore

    let runWake () =
        if Interlocked.Exchange(&wakeRequested, 0) = 1 then wakeHandler ()

    let systemCursor (c: Cursor) =
        match systemCursors.TryGetValue c with
        | true, h -> h
        | _ ->
            let id =
                match c with
                | Cursor.Hand -> SDL_SYSTEM_CURSOR_POINTER
                | Cursor.Text -> SDL_SYSTEM_CURSOR_TEXT
                | Cursor.Crosshair -> SDL_SYSTEM_CURSOR_CROSSHAIR
                | Cursor.Move -> SDL_SYSTEM_CURSOR_MOVE
                | Cursor.ResizeDiagonal -> SDL_SYSTEM_CURSOR_NWSE_RESIZE
                | Cursor.ResizeLeftRight -> SDL_SYSTEM_CURSOR_EW_RESIZE
                | Cursor.ResizeUpDown -> SDL_SYSTEM_CURSOR_NS_RESIZE
                | Cursor.NotAllowed -> SDL_SYSTEM_CURSOR_NOT_ALLOWED
                | Cursor.Wait -> SDL_SYSTEM_CURSOR_WAIT
                | Cursor.Default
                | Cursor.Image _ -> SDL_SYSTEM_CURSOR_DEFAULT
            let h = SDL_CreateSystemCursor id
            systemCursors[c] <- h
            h

    /// A tick of SDL's timer while Windows runs a modal loop (our loop is blocked): what our loop would do.
    let liveTick (resizedWindow: uint32) =
        runWake ()
        timers.RunDue clock.Elapsed
        for w in List.ofSeq windows.Values do
            w.LiveTick(w.Id = resizedWindow)

    let handle (e: nativeptr<SDL_Event>) =
        let ev = NativePtr.read e
        if ev.Type = SDL_EVENT_WINDOW_EXPOSED && ev.Data1 = 1 then
            // data1 1: a live-resize expose, sent by SDL's timer during the modal loop.
            liveTick ev.WindowId
        elif ev.Type <> SDL_EVENT_QUIT then
            // QUIT is SDL's idea of the app closing; ours closes when its windows have.
            match windows.TryGetValue ev.WindowId with
            | true, w -> w.HandleEvent e
            | _ -> ()

    // An exception must never unwind through SDL's frames: the filter stores it and the loop rethrows it.
    let filter =
        EventFilter(fun _ e ->
            let p = NativePtr.ofNativeInt<SDL_Event> e
            let t = (NativePtr.read p).Type
            if t = ourEvent || Environment.CurrentManagedThreadId <> uiThread then 1uy
            else
                try
                    handle p
                with ex ->
                    if pendingException.IsNone then pendingException <- Some(ExceptionDispatchInfo.Capture ex)
                // Whatever was handled may need the loop (a frame requested, a timer added): wake it, if it waits.
                enqueue ()
                0uy)

    // Kept in a field so the delegate outlives SDL's use of it.
    let filterPtr = Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate<EventFilter> filter
    do SDL_SetEventFilter(filterPtr, 0n)

    let rethrowPending () =
        match pendingException with
        | Some info ->
            pendingException <- None
            info.Throw()
        | None -> ()

    let createWindow (options: WindowOptions) (onEvent: PlatformEvent -> unit) =
        if disposed then raise (ObjectDisposedException(nameof SdlPlatform))
        // Window sizes are in window coordinates: pixels on Windows, points on a Mac (README-highdpi in SDL's docs).
        // Made at the primary display's scale, then sized again for the display it opened on.
        let contentScale =
            let s = float (SDL_GetDisplayContentScale(SDL_GetPrimaryDisplay()))
            if s > 0.0 then s else 1.0
        let px (dips: float) (s: float) = max 1 (int (Math.Round(dips * s)))
        let flags =
            SDL_WINDOW_HIDDEN ||| SDL_WINDOW_HIGH_PIXEL_DENSITY
            ||| (if options.Resizable then SDL_WINDOW_RESIZABLE else 0UL)
        let handle =
            withUtf8 options.Title (fun t ->
                SDL_CreateWindow(t, px options.Size.Width contentScale, px options.Size.Height contentScale, flags))
        if handle = 0n then raise (InvalidOperationException("SDL_CreateWindow failed: " + lastError ()))
        let id = SDL_GetWindowID handle
        let window = new SdlWindow(handle, onEvent, systemCursor, fun () -> windows.Remove id |> ignore)
        windows[id] <- window
        (window :> IWindow).Title <- options.Title
        // An owned window stays above its owner and has no taskbar button (SDL sets the Win32 owner); SDL destroys
        // it with its owner.
        match options.Owner with
        | ValueSome(:? SdlWindow as owner) when not (owner :> IWindow).IsClosed ->
            SDL_SetWindowParent(handle, owner.Handle) |> ignore
        | _ -> ()
        let w = window :> IWindow
        let wanted = px options.Size.Width w.Scale, px options.Size.Height w.Scale
        let density = float (SDL_GetWindowPixelDensity handle)
        let density = if density > 0.0 then density else 1.0
        if wanted <> (w.ClientSize.Width, w.ClientSize.Height) then
            SDL_SetWindowSize(handle, int (Math.Round(float (fst wanted) / density)), int (Math.Round(float (snd wanted) / density)))
            |> ignore
        rethrowPending ()
        w

    /// What came out of SDL's queue: only our event.
    let dequeued (e: SDL_Event) =
        if e.Type = ourEvent then
            Volatile.Write(&queued, 0)
            runWake ()

    let run () =
        let mutable e = SDL_Event()
        while windows.Count > 0 do
            // Pumps SDL (the filter handles what the OS sends) and takes our event if it's there.
            while SDL_PollEvent(&&e) <> 0uy do
                dequeued e
                rethrowPending ()
            rethrowPending ()
            // Here, our loop runs again: a modal loop (live resize) has ended.
            for w in List.ofSeq windows.Values do
                w.EndLiveResize()
            timers.RunDue clock.Elapsed
            rethrowPending ()

            if windows.Count > 0 then
                let due = windows.Values |> Seq.filter _.IsFrameDue |> List.ofSeq
                if due.IsEmpty then
                    // Nothing to draw: sleep until SDL has something, or the next timer. This keeps an idle app at
                    // zero CPU and GPU.
                    let timeout =
                        match timers.NextDue with
                        | ValueSome due -> int (Math.Clamp(Math.Ceiling (due - clock.Elapsed).TotalMilliseconds, 0.0, float Int32.MaxValue))
                        | ValueNone -> -1
                    if SDL_WaitEventTimeout(&&e, timeout) <> 0uy then dequeued e
                    rethrowPending ()
                else
                    // Presenting with vsync paces this loop to the display's refresh rate.
                    for w in due do
                        w.DeliverFrame()
                        rethrowPending ()

    interface IPlatform with
        member _.Name = "SDL"
        member _.SupportsMultipleWindows = true
        member _.CreateWindow(options, onEvent) = createWindow options onEvent
        member _.Run() = run ()
        member _.SetWakeHandler(handler) = wakeHandler <- handler
        member _.Elapsed = clock.Elapsed

        member _.After(delay, action) =
            // A new earliest timer: a waiting loop must wait less.
            if timers.Add(clock.Elapsed + delay, action) then enqueue ()

        member _.Wake() =
            if Interlocked.Exchange(&wakeRequested, 1) = 0 then enqueue ()

        member _.Dispose() =
            if not disposed then
                for w in List.ofSeq windows.Values do
                    (w :> IWindow).Close()
                for h in systemCursors.Values do
                    SDL_DestroyCursor h
                systemCursors.Clear()
                disposed <- true
                SDL_SetEventFilter(0n, 0n)
                SDL_Quit()
                GC.KeepAlive filter
