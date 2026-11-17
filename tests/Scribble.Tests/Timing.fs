/// Wall-clock assertions (project review 2026-11-17, decision 12): they're the likeliest tests to
/// fail on a busy machine or in Debug, so they're skipped unless SCRIBBLE_TIMING=1, and run in
/// Release before each milestone report:
///   $env:SCRIBBLE_TIMING = "1"; dotnet test tests/Scribble.Tests -c Release --filter Category=Timing
/// Count-based checks (what's realized, measured, made) stay in the normal run.
module Scribble.Tests.Timing

open System
open Xunit

/// A test that asserts how long something takes: skipped unless SCRIBBLE_TIMING=1. Put
/// `[<Trait("Category", "Timing")>]` beside it so `--filter Category=Timing` finds it.
type TimingFactAttribute() as this =
    inherit FactAttribute()
    do
        if Environment.GetEnvironmentVariable "SCRIBBLE_TIMING" <> "1" then
            this.Skip <- "Timing test: set SCRIBBLE_TIMING=1 (and use -c Release) to run it."
