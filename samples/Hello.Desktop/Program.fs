/// Desktop host for the Hello sample: the same Program.fs as Hello.Browser (only the project's SDK and its
/// Scribble.Desktop / Scribble.Browser reference differ). The app itself is in samples/Hello.
module Program

open Scribble.Core
open Scribble.Inspector

[<EntryPoint>]
let main _ = App.run (Scribble.Samples.HelloApp.app @ [ Inspector.enable [] ])
