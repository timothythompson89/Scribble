module Scribble.Samples.IconsBrowser

open Scribble.Core
open Scribble.Browser
open Scribble.Inspector

[<EntryPoint>]
let main _ =
    App.runInBrowser (Scribble.Samples.IconGallery.app @ [ Inspector.enable [] ]) |> ignore
    0
