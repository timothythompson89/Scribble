/// Browser host for the Hello sample (see samples/Shared/HelloApp.fs). Main can't block in the
/// browser, so it starts the app and returns; the page keeps it running.
module Scribble.Samples.Browser.Program

open Scribble.Core
open Scribble.Browser

[<EntryPoint>]
let main _ =
#if BROWSER_AOT
    System.Console.WriteLine "Scribble browser build: WebAssembly AOT"
#else
    System.Console.WriteLine "Scribble browser build: interpreted"
#endif
    App.runInBrowser Scribble.Samples.HelloApp.settings |> ignore
    0
