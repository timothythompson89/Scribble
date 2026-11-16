module Scribble.Samples.CounterDesktop

open Scribble.Core
open Scribble.Desktop
open Scribble.Inspector

[<EntryPoint>]
let main _ = App.run (Scribble.Samples.Counter.app @ [ Inspector.enable [] ])
