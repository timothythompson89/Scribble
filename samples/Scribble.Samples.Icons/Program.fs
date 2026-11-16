module Scribble.Samples.IconsDesktop

open Scribble.Core
open Scribble.Desktop
open Scribble.Inspector

[<EntryPoint>]
let main _ = App.run (Scribble.Samples.IconGallery.app @ [ Inspector.enable [] ])
