namespace Scribble.Rendering.WebGPU

#nowarn "9"

open System
open System.Diagnostics
open System.Threading
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Rendering
open Scribble.Rendering.WebGPU.Native

/// The handles shared by a device and everything created from it.
type internal GpuContext =
    { Instance: nativeint
      Adapter: nativeint
      Device: nativeint
      Queue: nativeint }

module internal Gpu =
    /// Runs `ProcessEvents` (and a device poll, when there is a device) until `isDone` returns true.
    /// webgpu.h reports async results through callbacks; Scribble waits for them synchronously
    /// because device setup and readback happen at well-defined, rare moments.
    let waitUntil (instance: nativeint) (device: nativeint) (what: string) (isDone: unit -> bool) =
        let timer = Stopwatch.StartNew()
        while not (isDone ()) do
            if device <> 0n then wgpuDevicePoll(device, 1u, 0n) |> ignore
            wgpuInstanceProcessEvents instance
            if not (isDone ()) then
                if timer.Elapsed > TimeSpan.FromSeconds 10.0 then
                    raise (TimeoutException $"WebGPU: timed out waiting for {what}.")
                Thread.Sleep 1

    /// Native function pointer for a callback delegate. The generic overload lets NativeAOT
    /// generate the marshalling stub for this exact delegate type ahead of time.
    let functionPointer<'T when 'T :> Delegate> (callback: 'T) =
        Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate<'T> callback

    let callbackInfo<'T when 'T :> Delegate> (callback: 'T) =
        { NextInChain = 0n
          Mode = CallbackMode.AllowProcessEvents
          Callback = functionPointer callback
          Userdata1 = 0n
          Userdata2 = 0n }

    let toGpuColor (c: Color) : GpuColor =
        { R = float c.R; G = float c.G; B = float c.B; A = float c.A }

