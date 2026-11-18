// In Scribble.Core's namespace (opened by every app), so an app's code is the same on every
// platform: only the package it references (Scribble.Desktop or Scribble.Browser) changes.
namespace Scribble.Core

open System
open Scribble.Platform
open Scribble.Platform.SDL3
open Scribble.Rendering
open Scribble.Rendering.WebGPU
open Scribble.Text
open Scribble.Text.HarfBuzz

[<AutoOpen>]
module DesktopApp =
    type App with
        /// Runs an app in a desktop window and returns when the window closes (use it as main's
        /// result). Picks the platform, the GPU renderer (SCRIBBLE_GPU_BACKEND chooses a backend)
        /// and HarfBuzz shaping. The same call runs an app in the browser with Scribble.Browser.
        static member run<'Msg>(settings: AppSetting<'Msg> list) : int =
            if not (OperatingSystem.IsWindows()) then
                Console.Error.WriteLine "Scribble runs on Windows for now (macOS and Linux are planned); the browser build runs anywhere."
                1
            else
                use platform = new SdlPlatform() :> IPlatform
                use device =
                    WebGpuDevice.Create [ WebGpuDevice.backend (WebGpuDevice.BackendFromEnvironment()) ]
                    :> IRenderDevice
                use shaper = new HarfBuzzShaper() :> IShaper
                use app = App.start (platform, device, shaper, settings)
                // `await` in app code resumes on this (the UI) thread.
                (app :?> ElementHost).Dispatcher |> ValueOption.iter _.Install()
                platform.Run()
                0

    /// Rendering in a tool or script, with no window (DESIGN.md, "Rendering to an image and saving it", round 2, 9):
    /// a GPU device and HarfBuzz shaping for `ViewImage.render`, until the result is disposed.
    /// ```
    /// use _ = DesktopApp.headless [ Headless.fonts [ Emoji.Twemoji() ] ]
    /// let image = (ViewImage.render (myView ()) [ ViewImage.scale 2.0 ]).Result
    /// File.WriteAllBytes("picture.png", (Image.toPng image).Result)
    /// ```
    let headless (settings: HeadlessSetting list) : IDisposable =
        let device =
            WebGpuDevice.Create [ WebGpuDevice.backend (WebGpuDevice.BackendFromEnvironment()) ] :> IRenderDevice
        let shaper = new HarfBuzzShaper() :> IShaper
        let rendering = Headless.start device shaper settings
        { new IDisposable with
            member _.Dispose() =
                rendering.Dispose()
                shaper.Dispose()
                device.Dispose() }
