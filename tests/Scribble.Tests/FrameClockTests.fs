module Scribble.Tests.FrameClockTests

open System
open Xunit
open Scribble.Platform

[<Fact>]
let ``first tick has zero delta and number zero`` () =
    let clock = FrameClock()
    let t = clock.Tick()
    Assert.Equal(TimeSpan.Zero, t.Delta)
    Assert.Equal(0L, t.Number)

[<Fact>]
let ``ticks are monotonic and numbered`` () =
    let clock = FrameClock()
    let a = clock.Tick()
    let b = clock.Tick()
    Assert.True(b.Total >= a.Total)
    Assert.Equal(b.Total - a.Total, b.Delta)
    Assert.Equal(1L, b.Number)

let private frame (ms: float) number =
    { Delta = TimeSpan.FromMilliseconds 16.0
      Total = TimeSpan.FromMilliseconds ms
      Number = number }

[<Fact>]
let ``frame rate counter reports after one second`` () =
    let counter = FrameRateCounter()
    // 60 frames spaced 1000/60 ms apart; the 61st crosses the one-second mark.
    let results = [ for i in 1..61 -> counter.Add(frame (float i * 1000.0 / 60.0) (int64 i)) ]
    Assert.Equal(1, results |> List.filter id |> List.length)
    Assert.InRange(counter.FramesPerSecond, 59.0, 62.0)

[<Fact>]
let ``frame rate counter tracks the slowest frame`` () =
    let counter = FrameRateCounter()
    counter.Add { Delta = TimeSpan.FromMilliseconds 16.0; Total = TimeSpan.FromMilliseconds 500.0; Number = 1L } |> ignore
    counter.Add { Delta = TimeSpan.FromMilliseconds 50.0; Total = TimeSpan.FromMilliseconds 550.0; Number = 2L } |> ignore
    counter.Add { Delta = TimeSpan.FromMilliseconds 16.0; Total = TimeSpan.FromMilliseconds 1001.0; Number = 3L } |> ignore
    Assert.Equal(TimeSpan.FromMilliseconds 50.0, counter.WorstFrame)

[<Fact>]
let ``empty pixel sizes`` () =
    Assert.True(PixelSize.Zero.IsEmpty)
    Assert.True(({ Width = 100; Height = 0 }: PixelSize).IsEmpty)
    Assert.False(({ Width = 1; Height = 1 }: PixelSize).IsEmpty)

[<Fact>]
let ``a frame clock reads the time it's given`` () =
    let mutable now = TimeSpan.FromSeconds 5.0
    let clock = FrameClock(fun () -> now)
    Assert.Equal(TimeSpan.Zero, clock.Tick().Total)
    now <- now + TimeSpan.FromMilliseconds 16.0
    let t = clock.Tick()
    Assert.Equal(TimeSpan.FromMilliseconds 16.0, t.Total)
    Assert.Equal(TimeSpan.FromMilliseconds 16.0, t.Delta)

[<Fact>]
let ``a timer queue runs work in time order, once each, when it comes due`` () =
    let q = TimerQueue()
    let ran = Collections.Generic.List<string>()
    let ms (v: float) = TimeSpan.FromMilliseconds v
    Assert.True(q.Add(ms 30.0, fun () -> ran.Add "c"))
    Assert.False(q.Add(ms 50.0, fun () -> ran.Add "d"))
    Assert.True(q.Add(ms 10.0, fun () -> ran.Add "a"))
    Assert.False(q.Add(ms 10.0, fun () -> ran.Add "b")) // same time: after the one added first
    Assert.Equal(ValueSome(ms 10.0), q.NextDue)
    q.RunDue(ms 5.0)
    Assert.Empty ran
    q.RunDue(ms 30.0)
    Assert.Equal<string list>([ "a"; "b"; "c" ], List.ofSeq ran)
    // Work added while running, already due, runs in the same pass.
    q.Add(ms 40.0, fun () -> q.Add(ms 0.0, fun () -> ran.Add "e") |> ignore) |> ignore
    q.RunDue(ms 45.0)
    Assert.Equal<string list>([ "a"; "b"; "c"; "e" ], List.ofSeq ran)
    q.RunDue(ms 100.0)
    Assert.Equal<string list>([ "a"; "b"; "c"; "e"; "d" ], List.ofSeq ran)
    Assert.Equal(ValueNone, q.NextDue)
