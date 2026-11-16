/// Browser host for the Counter sample: the same Program.fs as Counter.Desktop (only the project's SDK and its
/// Scribble.Desktop / Scribble.Browser reference differ). The app itself is in samples/Counter.
module Program

open Scribble.Core
open Scribble.Inspector

[<EntryPoint>]
let main _ = App.run (Scribble.Samples.Counter.app @ [ Inspector.enable [] ])
