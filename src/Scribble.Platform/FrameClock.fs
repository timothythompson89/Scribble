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

/// A monotonic clock that animations and frame statistics read from. Call Tick once per frame.
type FrameClock() =
    let stopwatch = Stopwatch.StartNew()
    let mutable last = TimeSpan.Zero
    let mutable number = -1L

    member _.Tick() : FrameTime =
        let now = stopwatch.Elapsed
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
