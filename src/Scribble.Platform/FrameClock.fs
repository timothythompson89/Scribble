namespace Scribble.Platform

open System
open System.Diagnostics

/// Timing for one frame.
[<Struct>]
type FrameTime =
    { /// Time since the previous frame.
      Delta: TimeSpan
      /// Time since the clock started.
      Total: TimeSpan
      /// 0 for the first frame.
      Number: int64 }

/// Frame timing read from a monotonic clock (`now`: the platform's `Elapsed`, or a stopwatch).
/// Call Tick once per frame.
type FrameClock(now: unit -> TimeSpan) =
    let mutable last = TimeSpan.Zero
    let mutable number = -1L
    let start = now ()

    /// Frame timing from a stopwatch started now.
    new() =
        let stopwatch = Stopwatch.StartNew()
        FrameClock(fun () -> stopwatch.Elapsed)

    member _.Tick() : FrameTime =
        let now = now () - start
        let delta = if number < 0L then TimeSpan.Zero else now - last
        last <- now
        number <- number + 1L
        { Delta = delta; Total = now; Number = number }

/// Measures frames per second and the slowest frame, over one-second windows.
type FrameRateCounter() =
    let mutable windowStart = TimeSpan.Zero
    let mutable frames = 0
    let mutable worstInWindow = TimeSpan.Zero
    let mutable rate = 0.0
    let mutable worst = TimeSpan.Zero

    /// Frames per second over the last completed window.
    member _.FramesPerSecond = rate
    /// Slowest frame in the last completed window.
    member _.WorstFrame = worst

    /// Records a frame. Returns true when a window has just completed and the figures changed.
    member _.Add(time: FrameTime) =
        frames <- frames + 1
        if time.Delta > worstInWindow then worstInWindow <- time.Delta
        let elapsed = time.Total - windowStart
        if elapsed >= TimeSpan.FromSeconds 1.0 then
            rate <- float frames / elapsed.TotalSeconds
            worst <- worstInWindow
            frames <- 0
            worstInWindow <- TimeSpan.Zero
            windowStart <- time.Total
            true
        else
            false

/// Work to run at given times, for a platform's `IPlatform.After`: the platform keeps one OS
/// timer for the earliest entry (`NextDue`) and calls `RunDue` when it fires. UI thread only.
[<Sealed>]
type TimerQueue() =
    let entries = System.Collections.Generic.List<struct (TimeSpan * int64 * (unit -> unit))>()
    let mutable order = 0L

    /// Adds `action`, due at `due`. Returns true when it's now the earliest (re-arm the OS timer).
    member _.Add(due: TimeSpan, action: unit -> unit) =
        order <- order + 1L
        entries.Add(struct (due, order, action))
        entries.Sort(fun (struct (a, oa, _)) (struct (b, ob, _)) -> let c = compare a b in if c <> 0 then c else compare oa ob)
        let struct (first, firstOrder, _) = entries[0]
        first = due && firstOrder = order

    /// When the earliest entry is due, if there is one.
    member _.NextDue: TimeSpan voption =
        if entries.Count = 0 then ValueNone
        else
            let struct (due, _, _) = entries[0]
            ValueSome due

    /// Runs every entry due at `now`, earliest first (each removed before it runs; one added
    /// meanwhile and already due runs too).
    member _.RunDue(now: TimeSpan) =
        let mutable go = true
        while go && entries.Count > 0 do
            let struct (due, _, action) = entries[0]
            if due <= now then
                entries.RemoveAt 0
                action ()
            else go <- false
