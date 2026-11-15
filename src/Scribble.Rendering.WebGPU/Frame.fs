namespace Scribble.Rendering.WebGPU

#nowarn "9"

open System
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Rendering
open Scribble.Rendering.WebGPU.Native

/// A frame that records one render pass into `view`: clear, then every shape in the draw list.
///
/// `beforeFinish` lets the owner append commands (e.g. a readback copy) after the pass;
/// `afterSubmit` presents or releases the target.
type internal RenderFrame
    (
        ctx: GpuContext,
        shapes: ShapeRenderer,
        view: nativeint,
        format: TextureFormat,
        size: PixelSize,
        scale: float,
        beforeFinish: nativeint -> unit,
        afterSubmit: unit -> unit
    ) =
    let mutable clearColor = Color.Transparent
    let drawList = DrawList(scale)
    let mutable finished = false

    interface IFrame with
        member _.Size = size
        member _.Scale = scale
        member _.Clear color = clearColor <- color
        member _.DrawList = drawList

        member _.Dispose() =
            if not finished then
                finished <- true
                let encoder = wgpuDeviceCreateCommandEncoder(ctx.Device, 0n)

                let mutable attachment =
                    { NextInChain = 0n
                      View = view
                      DepthSlice = DepthSliceUndefined
                      ResolveTarget = 0n
                      LoadOp = LoadOp.Clear
                      StoreOp = StoreOp.Store
                      ClearValue = Gpu.toGpuColor clearColor }

                let mutable pass =
                    { NextInChain = 0n
                      Label = StringView.Null
                      ColorAttachmentCount = 1un
                      ColorAttachments = NativePtr.toNativeInt &&attachment
                      DepthStencilAttachment = 0n
                      OcclusionQuerySet = 0n
                      TimestampWrites = 0n }

                let passEncoder = wgpuCommandEncoderBeginRenderPass(encoder, &&pass)
                shapes.Encode(passEncoder, format, size, drawList)
                wgpuRenderPassEncoderEnd passEncoder
                wgpuRenderPassEncoderRelease passEncoder

                beforeFinish encoder

                let mutable commands = wgpuCommandEncoderFinish(encoder, 0n)
                wgpuQueueSubmit(ctx.Queue, 1un, &&commands)
                wgpuCommandBufferRelease commands
                wgpuCommandEncoderRelease encoder
                afterSubmit ()
