/// Desktop host for the Gallery sample: the same Program.fs as Gallery.Browser (only the project's SDK and its
/// Scribble.Desktop / Scribble.Browser reference differ). The app itself is in samples/Gallery.
module Program

open Scribble.Core
open Scribble.Inspector

[<EntryPoint>]
let main _ = App.run (Scribble.Samples.GalleryApp.app @ [ Inspector.enable [] ])
