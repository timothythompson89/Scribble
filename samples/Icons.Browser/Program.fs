/// Browser host for the Icons sample: the same Program.fs as Icons.Desktop (only the project's SDK and its
/// Scribble.Desktop / Scribble.Browser reference differ). The app itself is in samples/Icons.
module Program

open Scribble.Core
open Scribble.Inspector

[<EntryPoint>]
let main _ = App.run (Scribble.Samples.IconGallery.app @ [ Inspector.enable [] ])
