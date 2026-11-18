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

    // ---- Image pages (DESIGN.md, "M8 step 3b", (c)) ----

    let createRgbaTexture (w: int) (h: int) =
        let mutable descriptor: TextureDescriptor =
            { NextInChain = 0n
              Label = StringView.Null
              Usage = TextureUsage.TextureBinding ||| TextureUsage.CopyDst
              Dimension = TextureDimension.D2
              Size = { Width = uint32 w; Height = uint32 h; DepthOrArrayLayers = 1u }
              Format = TextureFormat.RGBA8Unorm
              MipLevelCount = 1u
              SampleCount = 1u
              ViewFormatCount = 0un
              ViewFormats = 0n }
        let texture = wgpuDeviceCreateTexture(ctx.Device, &&descriptor)
        struct (texture, wgpuTextureCreateView(texture, 0n))

    let releaseTexture (struct (texture: nativeint, view: nativeint)) =
        wgpuTextureViewRelease view
        wgpuTextureDestroy texture
        wgpuTextureRelease texture

    /// Bound when a draw call has no image: the bind group layout always has a page.
    let placeholder = createRgbaTexture 1 1
    let imagePages = Dictionary<int, struct (nativeint * nativeint)>()

    /// Bind groups per target format and image page (-1: the placeholder). They also hold the shapes buffer, so
    /// they are dropped whenever that buffer is reallocated.
    let bindGroups = Dictionary<struct (TextureFormat * int), nativeint>()

    let dropBindGroups (page: int voption) =
        for KeyValue(struct (format, p), g) in Seq.toArray bindGroups do
            if page.IsNone || page.Value = p then
                wgpuBindGroupRelease g
                bindGroups.Remove(struct (format, p)) |> ignore

    let images =
        new ImageAtlas(
            { new IImagePages with
                member _.Create(page, w, h) = imagePages[page] <- createRgbaTexture w h

                member _.Destroy(page) =
                    dropBindGroups (ValueSome page)
                    releaseTexture imagePages[page]
                    imagePages.Remove page |> ignore

                member _.Upload(page, x, y, level) =
                    let struct (texture, _) = imagePages[page]
                    let mutable destination: TexelCopyTextureInfo =
                        { Texture = texture
                          MipLevel = 0u
                          Origin = { X = uint32 x; Y = uint32 y; Z = 0u }
                          Aspect = TextureAspect.All }
                    let mutable layout: TexelCopyBufferLayout =
                        { Offset = 0UL; BytesPerRow = uint32 (level.Width * 4); RowsPerImage = uint32 level.Height }
                    let mutable extent: Extent3D = { Width = uint32 level.Width; Height = uint32 level.Height; DepthOrArrayLayers = 1u }
                    use pixels = fixed level.Pixels
                    wgpuQueueWriteTexture(ctx.Queue, &&destination, NativePtr.toNativeInt pixels, unativeint level.Pixels.Length, &&layout, &&extent) },
            // RequiredLimits is left out when the device is made, so it has WebGPU's defaults.
            8192
        )

    let mutable shapesCapacity = 1024
    let mutable shapes = createBuffer BufferUsage.Storage (uint64 (shapesCapacity * DrawList.BytesPerShape))

    /// Pipeline and bind group layout per target format.
    let pipelines = Dictionary<TextureFormat, struct (nativeint * nativeint)>()

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

    let bindGroupFor (format: TextureFormat) (page: int) =
        match bindGroups.TryGetValue(struct (format, page)) with
        | true, g -> g
        | _ ->
            let struct (_, layout) = pipelineFor format
            let struct (_, pageView) = if page < 0 then placeholder else imagePages[page]
            let entries =
                [| { NextInChain = 0n; Binding = 0u; Buffer = globals; Offset = 0UL; Size = uint64 Shaders.GlobalsSize; Sampler = 0n; TextureView = 0n }
                   { NextInChain = 0n; Binding = 1u; Buffer = shapes; Offset = 0UL; Size = UInt64.MaxValue; Sampler = 0n; TextureView = 0n }
                   { NextInChain = 0n; Binding = 2u; Buffer = 0n; Offset = 0UL; Size = 0UL; Sampler = 0n; TextureView = atlasView }
                   { NextInChain = 0n; Binding = 3u; Buffer = 0n; Offset = 0UL; Size = 0UL; Sampler = 0n; TextureView = colorView }
                   { NextInChain = 0n; Binding = 4u; Buffer = 0n; Offset = 0UL; Size = 0UL; Sampler = 0n; TextureView = pageView } |]
            use entriesPtr = fixed entries
            let mutable descriptor: BindGroupDescriptor =
                { NextInChain = 0n
                  Label = StringView.Null
                  Layout = layout
                  EntryCount = unativeint entries.Length
                  Entries = NativePtr.toNativeInt entriesPtr }
            let group = wgpuDeviceCreateBindGroup(ctx.Device, &&descriptor)
            bindGroups[struct (format, page)] <- group
            group

    let ensureCapacity (count: int) =
        if count > shapesCapacity then
            while shapesCapacity < count do
                shapesCapacity <- shapesCapacity * 2
            wgpuBufferDestroy shapes
            wgpuBufferRelease shapes
            shapes <- createBuffer BufferUsage.Storage (uint64 (shapesCapacity * DrawList.BytesPerShape))
            dropBindGroups ValueNone

    do
        if shaderModule = 0n then raise (InvalidOperationException "WebGPU: the shape shader failed to compile.")

    /// This device's image atlas (pages, uploads), for tests and the inspector.
    member _.Images = images

    /// Uploads the shapes and records the draw into an open render pass: one draw call per run of shapes on the same
    /// image page (one in all when the frame has no more than one page).
    member _.Encode(pass: nativeint, format: TextureFormat, size: PixelSize, drawList: DrawList) =
        if drawList.Count > 0 then
            uploadAtlases ()
            let runs = images.Prepare drawList
            ensureCapacity drawList.Count

            let viewport = [| float32 size.Width; float32 size.Height; 0.0f; 0.0f |]
            use viewportPtr = fixed viewport
            wgpuQueueWriteBuffer(ctx.Queue, globals, 0UL, NativePtr.toNativeInt viewportPtr, unativeint Shaders.GlobalsSize)

            let bytes = drawList.InstanceBytes
            use bytesPtr = fixed bytes
            wgpuQueueWriteBuffer(ctx.Queue, shapes, 0UL, NativePtr.toNativeInt bytesPtr, unativeint bytes.Length)

            let struct (pipeline, _) = pipelineFor format
            wgpuRenderPassEncoderSetPipeline(pass, pipeline)
            for run in runs do
                wgpuRenderPassEncoderSetBindGroup(pass, 0u, bindGroupFor format run.Page, 0un, 0n)
                wgpuRenderPassEncoderDraw(pass, 6u, uint32 run.Count, 0u, uint32 run.First)

    interface IDisposable with
        member _.Dispose() =
            dropBindGroups ValueNone
            (images :> IDisposable).Dispose()
            releaseTexture placeholder
            for struct (pipeline, layout) in pipelines.Values do
                wgpuBindGroupLayoutRelease layout
                wgpuRenderPipelineRelease pipeline
            wgpuBufferDestroy shapes
            wgpuBufferRelease shapes
            wgpuBufferDestroy globals
            wgpuBufferRelease globals
            for t in [ struct (atlasTexture, atlasView); struct (colorTexture, colorView) ] do
                releaseTexture t
            wgpuShaderModuleRelease shaderModule
