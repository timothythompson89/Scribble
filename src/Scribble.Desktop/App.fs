namespace Scribble.Desktop

open System
open Scribble.Core
open Scribble.Platform
open Scribble.Platform.Win32
open Scribble.Rendering
open Scribble.Rendering.WebGPU
open Scribble.Text
open Scribble.Text.HarfBuzz

[<AutoOpen>]
module AppRun =
    type App with
        /// Runs an app in a desktop window and returns when the window closes (use it as main's
        /// result). Picks the platform, the GPU renderer (SCRIBBLE_GPU_BACKEND chooses a backend)
        /// and HarfBuzz shaping.
        static member run<'Msg>(settings: AppSetting<'Msg> list) : int =
            if not (OperatingSystem.IsWindows()) then
                Console.Error.WriteLine "Scribble runs on Windows for now (macOS and Linux are planned); the browser build runs anywhere."
                1
            else
                use platform = new Win32Platform() :> IPlatform
                use device =
                    WebGpuDevice.Create { WebGpuOptions.Default with Backend = WebGpuDevice.BackendFromEnvironment() }
                    :> IRenderDevice
                use shaper = new HarfBuzzShaper() :> IShaper
                use _app = App.start (platform, device, shaper, settings)
                platform.Run()
                0
