namespace Scribble.Rendering.WebGPU

#nowarn "9"

open System
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Rendering
open Scribble.Rendering.WebGPU.Native

/// An error the GPU reported asynchronously (validation errors, out of memory, device lost).
type GpuError = { Kind: string; Message: string }

/// What the WebGpuDevice settings configure.
type internal WebGpuOptions =
    { PowerPreference: PowerPreference
      /// Which native API wgpu should use. Undefined lets wgpu choose.
      Backend: BackendType
      /// Called for every uncaptured error. Default: write to stderr.
      OnError: GpuError -> unit
      /// Frames that may wait between drawing and the screen (1: least lag).
      FrameLatency: int }

    static member Default =
        { PowerPreference = PowerPreference.HighPerformance
          Backend = BackendType.Undefined
          OnError = fun e -> Console.Error.WriteLine $"[WebGPU] {e.Kind}: {e.Message}"
          FrameLatency = 1 }

/// One setting for `WebGpuDevice.Create [ ... ]`: `WebGpuDevice.backend BackendType.D3D12`.
[<Struct>]
type WebGpuSetting = internal WebGpuSetting of (WebGpuOptions -> WebGpuOptions)

/// The WebGPU implementation of IRenderDevice, using wgpu-native.
[<Sealed>]
type WebGpuDevice private (ctx: GpuContext, description: string, surfaceLatency: int, keepAlive: obj list) =
    let atlas = GlyphAtlas()
    let colorAtlas = GlyphAtlas(1024, 4)
    let shapes = new ShapeRenderer(ctx, atlas, colorAtlas)
    let mutable disposed = false

    /// Reads SCRIBBLE_GPU_BACKEND (d3d12, vulkan, metal, opengl, opengles), for trying backends
    /// without recompiling. Returns Undefined when it's unset or unknown.
    static member BackendFromEnvironment() =
        match Environment.GetEnvironmentVariable "SCRIBBLE_GPU_BACKEND" |> Option.ofObj |> Option.map _.ToLowerInvariant() with
        | Some "d3d12" | Some "dx12" -> BackendType.D3D12
        | Some "vulkan" -> BackendType.Vulkan
        | Some "metal" -> BackendType.Metal
        | Some "opengl" | Some "gl" -> BackendType.OpenGL
        | Some "opengles" | Some "gles" -> BackendType.OpenGLES
        | _ -> BackendType.Undefined

    /// Which native API wgpu uses (D3D12, Vulkan, Metal, ...). By default wgpu chooses.
    static member backend(value: BackendType) = WebGpuSetting(fun o -> { o with Backend = value })

    /// Which GPU to prefer on machines with more than one. Default: HighPerformance.
    static member powerPreference(value: GpuPower) =
        let native =
            match value with
            | GpuPower.Default -> PowerPreference.Undefined
            | GpuPower.LowPower -> PowerPreference.LowPower
            | GpuPower.HighPerformance -> PowerPreference.HighPerformance
        WebGpuSetting(fun o -> { o with PowerPreference = native })

    /// Called for every error the GPU reports asynchronously (validation, out of memory, device
    /// lost). Default: written to stderr.
    static member onError(handler: GpuError -> unit) = WebGpuSetting(fun o -> { o with OnError = handler })

    /// How many finished frames may wait for the screen (default: 1). 1 shows each frame at the
    /// next refresh, so what follows the mouse lags least; 2 or more lets the CPU and GPU overlap,
    /// for apps whose frames are heavy enough to need it. It never adds frames: power use is the same.
    static member frameLatency(value: int) = WebGpuSetting(fun o -> { o with FrameLatency = max 1 value })

    /// Creates the instance, picks an adapter and creates a device. Throws if no GPU is usable:
    /// `WebGpuDevice.Create []`, or `WebGpuDevice.Create [ WebGpuDevice.backend BackendType.Vulkan ]`.
    static member Create(settings: WebGpuSetting list) : WebGpuDevice =
        let options = settings |> List.fold (fun o (WebGpuSetting apply) -> apply o) WebGpuOptions.Default

        let instance = wgpuCreateInstance NativePtr.nullPtr
        if instance = 0n then
            raise (InvalidOperationException "WebGPU: could not create an instance. Is wgpu_native next to the app?")

        // --- Adapter -----------------------------------------------------------------------
        let adapterResult = ref 0n
        let adapterDone = ref false
        let adapterMessage = ref ""

        let onAdapter =
            RequestAdapterCallback(fun status handle message _ _ ->
                if status = RequestAdapterStatus.Success then adapterResult.Value <- handle
                else adapterMessage.Value <- $"{status}: {message}"
                adapterDone.Value <- true)

        let mutable adapterOptions =
            { NextInChain = 0n
              FeatureLevel = FeatureLevel.Core
              PowerPreference = options.PowerPreference
              ForceFallbackAdapter = 0u
              BackendType = options.Backend
              CompatibleSurface = 0n }

        wgpuInstanceRequestAdapter(instance, &&adapterOptions, Gpu.callbackInfo onAdapter) |> ignore
        Gpu.waitUntil instance 0n "an adapter" (fun () -> adapterDone.Value)
        GC.KeepAlive onAdapter
        let adapter = adapterResult.Value

        if adapter = 0n then
            wgpuInstanceRelease instance
            raise (InvalidOperationException $"WebGPU: no suitable GPU adapter ({adapterMessage.Value}).")

        let description =
            let mutable info = Unchecked.defaultof<AdapterInfo>
            if wgpuAdapterGetInfo(adapter, &&info) = Status.Success then
                let text = $"WebGPU/{info.BackendType} on {info.Device} ({info.AdapterType})"
                wgpuAdapterInfoFreeMembers info
                text
            else
                "WebGPU (unknown adapter)"

        // --- Device ------------------------------------------------------------------------
        // These two callbacks live as long as the device, so they are kept alive by the device object.
        let onLost =
            DeviceLostCallback(fun _ reason message _ _ ->
                // Destroyed and CallbackCancelled are the normal shutdown path.
                if reason = DeviceLostReason.Unknown || reason = DeviceLostReason.FailedCreation then
                    options.OnError { Kind = $"Device lost ({reason})"; Message = string message })

        let onError =
            UncapturedErrorCallback(fun _ errorType message _ _ ->
                options.OnError { Kind = string errorType; Message = string message })

        let deviceResult = ref 0n
        let deviceDone = ref false
        let deviceMessage = ref ""

        let onDevice =
            RequestDeviceCallback(fun status handle message _ _ ->
                if status = RequestDeviceStatus.Success then deviceResult.Value <- handle
                else deviceMessage.Value <- $"{status}: {message}"
                deviceDone.Value <- true)

        let label = Marshal.StringToCoTaskMemUTF8 "Scribble device"

        let mutable deviceDescriptor =
            { NextInChain = 0n
              Label = { Data = label; Length = unativeint UIntPtr.MaxValue }
              RequiredFeatureCount = 0un
              RequiredFeatures = 0n
              RequiredLimits = 0n
              DefaultQueue = { NextInChain = 0n; Label = StringView.Null }
              DeviceLostCallbackInfo = { Gpu.callbackInfo onLost with Mode = CallbackMode.AllowSpontaneous }
              UncapturedErrorCallbackInfo =
                { NextInChain = 0n
                  Callback = Gpu.functionPointer onError
                  Userdata1 = 0n
                  Userdata2 = 0n } }

        wgpuAdapterRequestDevice(adapter, &&deviceDescriptor, Gpu.callbackInfo onDevice) |> ignore
        Gpu.waitUntil instance 0n "a device" (fun () -> deviceDone.Value)
        GC.KeepAlive onDevice
        Marshal.FreeCoTaskMem label
        let device = deviceResult.Value

        if device = 0n then
            wgpuAdapterRelease adapter
            wgpuInstanceRelease instance
            raise (InvalidOperationException $"WebGPU: could not create a device ({deviceMessage.Value}).")

        let ctx =
            { Instance = instance
              Adapter = adapter
              Device = device
              Queue = wgpuDeviceGetQueue device }

        new WebGpuDevice(ctx, description, options.FrameLatency, [ box onLost; box onError ])

    interface IRenderDevice with
        member _.Description = description
        member _.GlyphAtlas = atlas
        member _.ColorGlyphAtlas = colorAtlas

        member _.CreateSurface(window, size) = new WebGpuSurface(ctx, shapes, window, size, surfaceLatency) :> IRenderSurface

        member _.RenderOffscreen(size, scale, draw) =
            if size.IsEmpty then invalidArg (nameof size) "Offscreen size must be non-zero."

            let width, height = uint32 size.Width, uint32 size.Height
            let extent = { Width = width; Height = height; DepthOrArrayLayers = 1u }

            let mutable textureDescriptor =
                { NextInChain = 0n
                  Label = StringView.Null
                  Usage = TextureUsage.RenderAttachment ||| TextureUsage.CopySrc
                  Dimension = TextureDimension.D2
                  Size = extent
                  Format = TextureFormat.RGBA8Unorm
                  MipLevelCount = 1u
                  SampleCount = 1u
                  ViewFormatCount = 0un
                  ViewFormats = 0n }

            let texture = wgpuDeviceCreateTexture(ctx.Device, &&textureDescriptor)
            let view = wgpuTextureCreateView(texture, 0n)

            // Buffer rows must be a multiple of 256 bytes for texture-to-buffer copies.
            let tightRow = int width * 4
            let paddedRow = (tightRow + 255) / 256 * 256
            let bufferSize = uint64 paddedRow * uint64 height

            let mutable bufferDescriptor =
                { NextInChain = 0n
                  Label = StringView.Null
                  Usage = BufferUsage.MapRead ||| BufferUsage.CopyDst
                  Size = bufferSize
                  MappedAtCreation = 0u }

            let buffer = wgpuDeviceCreateBuffer(ctx.Device, &&bufferDescriptor)

            let copyToBuffer encoder =
                let mutable source =
                    { Texture = texture
                      MipLevel = 0u
                      Origin = { X = 0u; Y = 0u; Z = 0u }
                      Aspect = TextureAspect.All }

                let mutable destination =
                    { Layout =
                        { Offset = 0UL
                          BytesPerRow = uint32 paddedRow
                          RowsPerImage = height }
                      Buffer = buffer }

                let mutable copySize = extent
                wgpuCommandEncoderCopyTextureToBuffer(encoder, &&source, &&destination, &&copySize)

            try
                let frame = new RenderFrame(ctx, shapes, view, TextureFormat.RGBA8Unorm, size, scale, copyToBuffer, ignore)
                (try draw frame finally (frame :> IDisposable).Dispose())

                let mapStatus = ref MapAsyncStatus.CallbackCancelled
                let mapDone = ref false
                let onMapped =
                    BufferMapCallback(fun status _ _ _ ->
                        mapStatus.Value <- status
                        mapDone.Value <- true)

                wgpuBufferMapAsync(buffer, MapMode.Read, 0un, unativeint bufferSize, Gpu.callbackInfo onMapped)
                |> ignore

                Gpu.waitUntil ctx.Instance ctx.Device "the readback" (fun () -> mapDone.Value)
                GC.KeepAlive onMapped

                if mapStatus.Value <> MapAsyncStatus.Success then
                    raise (InvalidOperationException $"WebGPU: readback failed ({mapStatus.Value}).")

                let mapped = wgpuBufferGetConstMappedRange(buffer, 0un, unativeint bufferSize)
                let pixels = Array.zeroCreate<byte> (tightRow * int height)
                for y in 0 .. int height - 1 do
                    Marshal.Copy(mapped + nativeint (y * paddedRow), pixels, y * tightRow, tightRow)
                wgpuBufferUnmap buffer

                { Size = size; Pixels = pixels }
            finally
                wgpuBufferDestroy buffer
                wgpuBufferRelease buffer
                wgpuTextureViewRelease view
                wgpuTextureDestroy texture
                wgpuTextureRelease texture

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                (shapes :> IDisposable).Dispose()
                wgpuQueueRelease ctx.Queue
                wgpuDeviceRelease ctx.Device
                wgpuAdapterRelease ctx.Adapter
                // Let wgpu deliver the device-lost callback before the instance goes away.
                wgpuInstanceProcessEvents ctx.Instance
                wgpuInstanceRelease ctx.Instance
                GC.KeepAlive keepAlive
