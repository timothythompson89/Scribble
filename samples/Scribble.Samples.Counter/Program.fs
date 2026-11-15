module Scribble.Samples.CounterDesktop

open Scribble.Core
open Scribble.Desktop

/// `--builders` runs the same counter written with the experimental builder syntax.
[<EntryPoint>]
let main args =
    App.run (if Array.contains "--builders" args then Scribble.Samples.CounterBuilders.app else Scribble.Samples.Counter.app)
