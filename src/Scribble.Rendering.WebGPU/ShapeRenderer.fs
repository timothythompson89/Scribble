namespace Scribble.Rendering.WebGPU

#nowarn "9"

open System
open System.Collections.Generic
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Scribble.Platform
open Scribble.Rendering
open Scribble.Rendering.WebGPU.Native

/// Draws a DrawList with the shared WGSL shape shader: one pipeline per target format, one
/// storage buffer holding every shape, one instanced draw call per frame.
[<Sealed>]
type internal ShapeRenderer(ctx: GpuContext, atlas: GlyphAtlas, colorAtlas: GlyphAtlas) =
    let utf8 (text: string) = Marshal.StringToCoTaskMemUTF8 text

    let shaderModule =
        let code = utf8 Shaders.shapesWgsl.Value
        let label = utf8 "Scribble shapes"
        try
            let mutable source: ShaderSourceWGSL =
                { Chain = { Next = 0n; SType = SType.ShaderSourceWGSL }
                  Code = { Data = code; Length = unativeint UIntPtr.MaxValue } }

            let mutable descriptor: ShaderModuleDescriptor =
                { NextInChain = NativePtr.toNativeInt &&source
                  Label = { Data = label; Length = unativeint UIntPtr.MaxValue } }

            wgpuDeviceCreateShaderModule(ctx.Device, &&descriptor)
        finally
            Marshal.FreeCoTaskMem code
            Marshal.FreeCoTaskMem label

    let createBuffer (usage: uint64) (size: uint64) =
        let mutable descriptor: BufferDescriptor =
            { NextInChain = 0n
              Label = StringView.Null
              Usage = usage ||| BufferUsage.CopyDst
              Size = size
              MappedAtCreation = 0u }
        wgpuDeviceCreateBuffer(ctx.Device, &&descriptor)

    let globals = createBuffer BufferUsage.Uniform (uint64 Shaders.GlobalsSize)

    /// A GPU texture mirroring a CPU glyph atlas (R8 coverage or RGBA8 color).
    let createAtlasTexture (source: GlyphAtlas) =
        let mutable descriptor: TextureDescriptor =
            { NextInChain = 0n
              Label = StringView.Null
              Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst
              Dimension = TextureDimension.D2
              Size = { Width = uint32 source.Size; Height = uint32 source.Size; DepthOrArrayLayers = 1u }
              Format = (if source.BytesPerPixel = 4 then TextureFormat.RGBA8Unorm else TextureFormat.R8Unorm)
              MipLevelCount = 1u
              SampleCount = 1u
              ViewFormatCount = 0un
              ViewFormats = 0n }
        let texture = wgpuDeviceCreateTexture(ctx.Device, &&descriptor)
        struct (texture, wgpuTextureCreateView(texture, 0n))

    let struct (atlasTexture, atlasView) = createAtlasTexture atlas
    let struct (colorTexture, colorView) = createAtlasTexture colorAtlas

    /// Copies the part of a CPU atlas that changed since the last upload to its GPU texture.
    let upload (source: GlyphAtlas) (texture: nativeint) =
        let region = source.TakeDirtyRegion()
        if not region.IsEmpty then
            let x, y = int region.X, int region.Y
            let w, h = int region.Width, int region.Height
            let bpp = source.BytesPerPixel
            let mutable destination: TexelCopyTextureInfo =
                { Texture = texture
                  MipLevel = 0u
                  Origin = { X = uint32 x; Y = uint32 y; Z = 0u }
                  Aspect = TextureAspect.All }
            // Point at the region's first byte inside the full atlas; rows are a whole atlas row apart.
            let mutable layout: TexelCopyBufferLayout =
                { Offset = 0UL; BytesPerRow = uint32 (source.Size * bpp); RowsPerImage = uint32 h }
            let mutable extent: Extent3D = { Width = uint32 w; Height = uint32 h; DepthOrArrayLayers = 1u }
            use pixels = fixed source.Pixels
            let start = NativePtr.toNativeInt (NativePtr.add pixels ((y * source.Size + x) * bpp))
            let length = ((h - 1) * source.Size + w) * bpp
            wgpuQueueWriteTexture(ctx.Queue, &&destination, start, unativeint length, &&layout, &&extent)

    let uploadAtlases () =
        upload atlas atlasTexture
        upload colorAtlas colorTexture

    let mutable shapesCapacity = 1024
    let mutable shapes = createBuffer BufferUsage.Storage (uint64 (shapesCapacity * DrawList.BytesPerShape))

    /// Pipeline and bind group layout per target format. Bind groups depend on the shapes buffer,
    /// so they are cached separately and dropped whenever that buffer is reallocated.
    let pipelines = Dictionary<TextureFormat, struct (nativeint * nativeint)>()
    let bindGroups = Dictionary<TextureFormat, nativeint>()

    let createPipeline (format: TextureFormat) =
        let vsMain = utf8 "vs_main"
        let fsMain = utf8 "fs_main"
        try
            let premultipliedOver =
                { Operation = BlendOperation.Add
                  SrcFactor = BlendFactor.One
                  DstFactor = BlendFactor.OneMinusSrcAlpha }

            let mutable blend: BlendState = { Color = premultipliedOver; Alpha = premultipliedOver }

            let mutable target: ColorTargetState =
                { NextInChain = 0n
                  Format = format
                  Blend = NativePtr.toNativeInt &&blend
                  WriteMask = ColorWriteMask.All }

            let mutable fragment: FragmentState =
                { NextInChain = 0n
                  Module = shaderModule
                  EntryPoint = { Data = fsMain; Length = unativeint UIntPtr.MaxValue }
                  ConstantCount = 0un
                  Constants = 0n
                  TargetCount = 1un
                  Targets = NativePtr.toNativeInt &&target }

            let mutable descriptor: RenderPipelineDescriptor =
                { NextInChain = 0n
                  Label = StringView.Null
                  Layout = 0n // "auto": derived from the shader
                  Vertex =
                    { NextInChain = 0n
                      Module = shaderModule
                      EntryPoint = { Data = vsMain; Length = unativeint UIntPtr.MaxValue }
                      ConstantCount = 0un
                      Constants = 0n
                      BufferCount = 0un
                      Buffers = 0n }
                  Primitive =
                    { NextInChain = 0n
                      Topology = PrimitiveTopology.TriangleList
                      StripIndexFormat = IndexFormat.Undefined
                      FrontFace = FrontFace.CCW
                      CullMode = CullMode.None
                      UnclippedDepth = 0u }
                  DepthStencil = 0n
                  Multisample = { NextInChain = 0n; Count = 1u; Mask = 0xFFFFFFFFu; AlphaToCoverageEnabled = 0u }
                  Fragment = NativePtr.toNativeInt &&fragment }

            let pipeline = wgpuDeviceCreateRenderPipeline(ctx.Device, &&descriptor)
            if pipeline = 0n then raise (InvalidOperationException "WebGPU: could not create the shape pipeline.")
            struct (pipeline, wgpuRenderPipelineGetBindGroupLayout(pipeline, 0u))
        finally
            Marshal.FreeCoTaskMem vsMain
            Marshal.FreeCoTaskMem fsMain

    let pipelineFor format =
        match pipelines.TryGetValue format with
        | true, p -> p
        | _ ->
            let p = createPipeline format
            pipelines[format] <- p
            p

    let bindGroupFor format =
        match bindGroups.TryGetValue format with
        | true, g -> g
        | _ ->
            let struct (_, layout) = pipelineFor format
            let entries =
                [| { NextInChain = 0n; Binding = 0u; Buffer = globals; Offset = 0UL; Size = uint64 Shaders.GlobalsSize; Sampler = 0n; TextureView = 0n }
                   { NextInChain = 0n; Binding = 1u; Buffer = shapes; Offset = 0UL; Size = UInt64.MaxValue; Sampler = 0n; TextureView = 0n }
                   { NextInChain = 0n; Binding = 2u; Buffer = 0n; Offset = 0UL; Size = 0UL; Sampler = 0n; TextureView = atlasView }
                   { NextInChain = 0n; Binding = 3u; Buffer = 0n; Offset = 0UL; Size = 0UL; Sampler = 0n; TextureView = colorView } |]
            use entriesPtr = fixed entries
            let mutable descriptor: BindGroupDescriptor =
                { NextInChain = 0n
                  Label = StringView.Null
                  Layout = layout
                  EntryCount = unativeint entries.Length
                  Entries = NativePtr.toNativeInt entriesPtr }
            let group = wgpuDeviceCreateBindGroup(ctx.Device, &&descriptor)
            bindGroups[format] <- group
            group

    let ensureCapacity (count: int) =
        if count > shapesCapacity then
            while shapesCapacity < count do
                shapesCapacity <- shapesCapacity * 2
            wgpuBufferDestroy shapes
            wgpuBufferRelease shapes
            shapes <- createBuffer BufferUsage.Storage (uint64 (shapesCapacity * DrawList.BytesPerShape))
            for g in bindGroups.Values do
                wgpuBindGroupRelease g
            bindGroups.Clear()

    do
        if shaderModule = 0n then raise (InvalidOperationException "WebGPU: the shape shader failed to compile.")

    /// Uploads the shapes and records the draw into an open render pass.
    member _.Encode(pass: nativeint, format: TextureFormat, size: PixelSize, drawList: DrawList) =
        if drawList.Count > 0 then
            uploadAtlases ()
            ensureCapacity drawList.Count

            let viewport = [| float32 size.Width; float32 size.Height; 0.0f; 0.0f |]
            use viewportPtr = fixed viewport
            wgpuQueueWriteBuffer(ctx.Queue, globals, 0UL, NativePtr.toNativeInt viewportPtr, unativeint Shaders.GlobalsSize)

            let bytes = drawList.InstanceBytes
            use bytesPtr = fixed bytes
            wgpuQueueWriteBuffer(ctx.Queue, shapes, 0UL, NativePtr.toNativeInt bytesPtr, unativeint bytes.Length)

            let struct (pipeline, _) = pipelineFor format
            wgpuRenderPassEncoderSetPipeline(pass, pipeline)
            wgpuRenderPassEncoderSetBindGroup(pass, 0u, bindGroupFor format, 0un, 0n)
            wgpuRenderPassEncoderDraw(pass, 6u, uint32 drawList.Count, 0u, 0u)

    interface IDisposable with
        member _.Dispose() =
            for g in bindGroups.Values do
                wgpuBindGroupRelease g
            for struct (pipeline, layout) in pipelines.Values do
                wgpuBindGroupLayoutRelease layout
                wgpuRenderPipelineRelease pipeline
            wgpuBufferDestroy shapes
            wgpuBufferRelease shapes
            wgpuBufferDestroy globals
            wgpuBufferRelease globals
            for struct (texture, view) in [ struct (atlasTexture, atlasView); struct (colorTexture, colorView) ] do
                wgpuTextureViewRelease view
                wgpuTextureDestroy texture
                wgpuTextureRelease texture
            wgpuShaderModuleRelease shaderModule
