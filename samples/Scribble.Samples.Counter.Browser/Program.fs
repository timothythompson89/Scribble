module Scribble.Samples.CounterBrowser

open Scribble.Core
open Scribble.Browser

/// `?arg=--builders` in the page address runs the builder-syntax version.
[<EntryPoint>]
let main args =
    App.runInBrowser (if Array.contains "--builders" args then Scribble.Samples.CounterBuilders.app else Scribble.Samples.Counter.app) |> ignore
    0
