// In Scribble.Core's namespace (opened by every app), so an app's code is the same on every
// platform: only the package it references (Scribble.Desktop or Scribble.Browser) changes.
namespace Scribble.Core

open System
open Scribble.Platform
open Scribble.Platform.Browser
open Scribble.Rendering
open Scribble.Rendering.WebGPU.Browser
open Scribble.Text
open Scribble.Text.HarfBuzz.Browser

[<AutoOpen>]
module BrowserApp =
    type App with
        /// Runs an app on a full-page canvas. Setting up WebGPU and HarfBuzz is asynchronous in the
        /// browser, so this starts the app and returns 0 straight away (use it as main's result, as
        /// on the desktop); the page keeps the app running. Errors go to the browser's console and
        /// the page's error box. The same call runs an app on the desktop with Scribble.Desktop.
        static member run<'Msg>(settings: AppSetting<'Msg> list) : int =
            task {
                try
                    let! platform = BrowserPlatform.InitializeAsync()
                    // Relative image URLs (Image.fromUrl) resolve against the page.
                    ImageHttp.BaseUrl <- Uri(platform.PageUrl)
                    let! device = BrowserGpuDevice.CreateAsync []
                    let! shaper = BrowserHarfBuzzShaper.CreateAsync()
                    let app = App.start (platform :> IPlatform, device :> IRenderDevice, shaper :> IShaper, settings)
                    // `await` in app code resumes through Scribble's dispatcher (and its error handling).
                    (app :?> ElementHost).Dispatcher |> ValueOption.iter _.Install()
                    (platform :> IPlatform).Run()
                with ex ->
                    Console.Error.WriteLine $"Scribble failed to start: {ex}"
            }
            |> ignore
            0
