/// Desktop host for the Hello sample (see samples/Shared/HelloApp.fs).
module Scribble.Samples.Hello.Program

open Scribble.Core
open Scribble.Desktop

[<EntryPoint>]
let main _ = App.run Scribble.Samples.HelloApp.settings
