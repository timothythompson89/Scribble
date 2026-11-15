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
