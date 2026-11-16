namespace Scribble.Rendering.WebGPU

#nowarn "9"

open System
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Rendering
open Scribble.Rendering.WebGPU.Native

/// A window's swap chain on WebGPU.
///
/// Format: BGRA8Unorm (or RGBA8Unorm), deliberately not an sRGB format: Scribble's colors are sRGB
/// values and are written as-is, like most UI toolkits. Present mode: Fifo (vsync), the only mode
/// every platform supports.
[<Sealed>]
type internal WebGpuSurface(ctx: GpuContext, shapes: ShapeRenderer, window: NativeSurfaceHandle, initialSize: PixelSize, frameLatency: int) =
    let surface =
        match window with
        | Win32Handle(hinstance, hwnd) ->
            let mutable source: SurfaceSourceWindowsHWND =
                { Chain = { Next = 0n; SType = SType.SurfaceSourceWindowsHWND }
                  Hinstance = hinstance
                  Hwnd = hwnd }

            let mutable descriptor: SurfaceDescriptor =
                { NextInChain = NativePtr.toNativeInt &&source
                  Label = StringView.Null }

            wgpuInstanceCreateSurface(ctx.Instance, &&descriptor)
        | other ->
            raise (PlatformNotSupportedException $"The native WebGPU backend can't draw into {other}; use Scribble.Rendering.WebGPU.Browser.")

    do
        if surface = 0n then
            raise (InvalidOperationException "WebGPU: could not create a surface for the window.")

    let format, alphaMode =
        let mutable caps = Unchecked.defaultof<SurfaceCapabilities>
        if wgpuSurfaceGetCapabilities(surface, ctx.Adapter, &&caps) <> Status.Success then
            raise (InvalidOperationException "WebGPU: the adapter cannot present to this window.")

        let read (ptr: nativeint) (count: unativeint) : 'T list =
            [ for i in 0 .. int count - 1 -> NativePtr.get (NativePtr.ofNativeInt<'T> ptr) i ]

        let formats: TextureFormat list = read caps.Formats caps.FormatCount
        let alphaModes: CompositeAlphaMode list = read caps.AlphaModes caps.AlphaModeCount
        wgpuSurfaceCapabilitiesFreeMembers caps

        let format =
            [ TextureFormat.BGRA8Unorm; TextureFormat.RGBA8Unorm ]
            |> List.tryFind (fun f -> List.contains f formats)
            |> Option.orElse (List.tryHead formats)
            |> Option.defaultWith (fun () -> raise (InvalidOperationException "WebGPU: surface has no formats."))

        let alpha =
            if List.contains CompositeAlphaMode.Opaque alphaModes then CompositeAlphaMode.Opaque
            else CompositeAlphaMode.Auto

        format, alpha

    let mutable size = PixelSize.Zero
    let mutable configured = false
    let mutable disposed = false

    let configure (newSize: PixelSize) =
        size <- newSize
        if newSize.IsEmpty then
            if configured then wgpuSurfaceUnconfigure surface
            configured <- false
        else
            // One frame in flight by default: each frame reaches the screen at the next refresh
            // instead of queueing behind another (wgpu's default is 2), so drags and hovers lag
            // the mouse by one frame less. It doesn't change how many frames are drawn.
            let mutable extras: SurfaceConfigurationExtras =
                { Chain = { Next = 0n; SType = SType.SurfaceConfigurationExtras }
                  DesiredMaximumFrameLatency = uint32 frameLatency }

            let mutable config =
                { NextInChain = NativePtr.toNativeInt &&extras
                  Device = ctx.Device
                  Format = format
                  Usage = TextureUsage.RenderAttachment
                  Width = uint32 newSize.Width
                  Height = uint32 newSize.Height
                  ViewFormatCount = 0un
                  ViewFormats = 0n
                  AlphaMode = alphaMode
                  PresentMode = PresentMode.Fifo }

            wgpuSurfaceConfigure(surface, &&config)
            configured <- true

    do configure initialSize

    member _.Format = format

    interface IRenderSurface with
        member _.Size = size

        member _.Resize newSize =
            if newSize <> size then configure newSize

        member _.TryBeginFrame(scale) =
            if disposed || not configured then
                None
            else
                let mutable st = Unchecked.defaultof<SurfaceTexture>
                wgpuSurfaceGetCurrentTexture(surface, &&st)

                match st.Status with
                | SurfaceGetCurrentTextureStatus.SuccessOptimal
                | SurfaceGetCurrentTextureStatus.SuccessSuboptimal ->
                    let texture = st.Texture
                    let view = wgpuTextureCreateView(texture, 0n)
                    let suboptimal = st.Status = SurfaceGetCurrentTextureStatus.SuccessSuboptimal

                    let afterSubmit () =
                        wgpuSurfacePresent surface |> ignore
                        wgpuTextureViewRelease view
                        wgpuTextureRelease texture
                        // Suboptimal still presents, but the swap chain should be rebuilt.
                        if suboptimal then configure size

                    Some(new RenderFrame(ctx, shapes, view, format, size, scale, ignore, afterSubmit) :> IFrame)

                | SurfaceGetCurrentTextureStatus.Timeout ->
                    if st.Texture <> 0n then wgpuTextureRelease st.Texture
                    None

                | SurfaceGetCurrentTextureStatus.Outdated
                | SurfaceGetCurrentTextureStatus.Lost ->
                    if st.Texture <> 0n then wgpuTextureRelease st.Texture
                    configure size
                    None

                | status -> raise (InvalidOperationException $"WebGPU: could not get the surface texture ({status}).")

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                if configured then wgpuSurfaceUnconfigure surface
                wgpuSurfaceRelease surface
