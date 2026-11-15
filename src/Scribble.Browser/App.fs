namespace Scribble.Browser

open System
open System.Threading.Tasks
open Scribble.Core
open Scribble.Platform
open Scribble.Platform.Browser
open Scribble.Rendering
open Scribble.Rendering.WebGPU.Browser
open Scribble.Text
open Scribble.Text.HarfBuzz.Browser

[<AutoOpen>]
module AppRun =
    type App with
        /// Runs an app on a full-page canvas. Setting up WebGPU and HarfBuzz is asynchronous in the
        /// browser, so this returns a task; from then on the page keeps the app running. Call it from
        /// main and return straight away. Errors go to the browser's developer console.
        static member runInBrowser<'Msg>(settings: AppSetting<'Msg> list) : Task =
            task {
                try
                    let! platform = BrowserPlatform.InitializeAsync()
                    let! device = BrowserGpuDevice.CreateAsync()
                    let! shaper = BrowserHarfBuzzShaper.CreateAsync()
                    App.start (platform :> IPlatform, device :> IRenderDevice, shaper :> IShaper, settings) |> ignore
                    (platform :> IPlatform).Run()
                with ex ->
                    Console.Error.WriteLine $"Scribble failed to start: {ex}"
            }
