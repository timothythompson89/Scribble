module Scribble.Samples.CounterBrowser

open Scribble.Core
open Scribble.Browser
open Scribble.Inspector

[<EntryPoint>]
let main _ =
    App.runInBrowser (Scribble.Samples.Counter.app @ [ Inspector.enable [] ]) |> ignore
    0
