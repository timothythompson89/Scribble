module Scribble.Samples.CounterBrowser

open Scribble.Core
open Scribble.Browser

[<EntryPoint>]
let main _ =
    App.runInBrowser Scribble.Samples.Counter.app |> ignore
    0
