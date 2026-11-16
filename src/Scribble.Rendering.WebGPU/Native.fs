/// Bindings for the parts of webgpu.h that Scribble uses, matching wgpu-native v29.0.1.1
/// (webgpu-headers 673658b). Only the core API is used, so Dawn should work as a drop-in.
///
/// Conventions: handles are `nativeint`; `size_t` is `unativeint`; enums and flags are their C widths;
/// every parameter and field is blittable, so no runtime marshalling is needed (NativeAOT-safe).
/// Struct field order and types must match the header exactly; Sequential layout gives C alignment.
module Scribble.Rendering.WebGPU.Native

#nowarn "9"

open System.Runtime.InteropServices

[<Literal>]
let private Lib = "wgpu_native"

// ---------------------------------------------------------------------------------------------
// Enums and flags (subset)
// ---------------------------------------------------------------------------------------------

type SType =
    | ShaderSourceWGSL = 0x00000002u
    | SurfaceSourceWindowsHWND = 0x00000005u
    | SurfaceSourceXlibWindow = 0x00000006u
    | SurfaceSourceWaylandSurface = 0x00000007u
    /// wgpu-native extension (wgpu.h).
    | SurfaceConfigurationExtras = 0x00030008u

type CallbackMode =
    | WaitAnyOnly = 1u
    | AllowProcessEvents = 2u
    | AllowSpontaneous = 3u

type RequestAdapterStatus =
    | Success = 1u
    | CallbackCancelled = 2u
    | Unavailable = 3u
    | Error = 4u

type RequestDeviceStatus =
    | Success = 1u
    | CallbackCancelled = 2u
    | Error = 3u

type MapAsyncStatus =
    | Success = 1u
    | CallbackCancelled = 2u
    | Error = 3u
    | Aborted = 4u

type Status =
    | Success = 1u
    | Error = 2u

type FeatureLevel =
    | Undefined = 0u
    | Compatibility = 1u
    | Core = 2u

type PowerPreference =
    | Undefined = 0u
    | LowPower = 1u
    | HighPerformance = 2u

type BackendType =
    | Undefined = 0u
    | Null = 1u
    | WebGPU = 2u
    | D3D11 = 3u
    | D3D12 = 4u
    | Metal = 5u
    | Vulkan = 6u
    | OpenGL = 7u
    | OpenGLES = 8u

type AdapterType =
    | DiscreteGPU = 1u
    | IntegratedGPU = 2u
    | CPU = 3u
    | Unknown = 4u

type ErrorType =
    | NoError = 1u
    | Validation = 2u
    | OutOfMemory = 3u
    | Internal = 4u
    | Unknown = 5u

type DeviceLostReason =
    | Unknown = 1u
    | Destroyed = 2u
    | CallbackCancelled = 3u
    | FailedCreation = 4u

type SurfaceGetCurrentTextureStatus =
    | SuccessOptimal = 1u
    | SuccessSuboptimal = 2u
    | Timeout = 3u
    | Outdated = 4u
    | Lost = 5u
    | Error = 6u

type TextureFormat =
    | Undefined = 0x00u
    | R8Unorm = 0x01u
    | RGBA8Unorm = 0x16u
    | RGBA8UnormSrgb = 0x17u
    | BGRA8Unorm = 0x1Bu
    | BGRA8UnormSrgb = 0x1Cu

type PresentMode =
    | Undefined = 0u
    | Fifo = 1u
    | FifoRelaxed = 2u
    | Immediate = 3u
    | Mailbox = 4u

type CompositeAlphaMode =
    | Auto = 0u
    | Opaque = 1u
    | Premultiplied = 2u
    | Unpremultiplied = 3u
    | Inherit = 4u

type LoadOp =
    | Undefined = 0u
    | Load = 1u
    | Clear = 2u

type StoreOp =
    | Undefined = 0u
    | Store = 1u
    | Discard = 2u

type TextureDimension =
    | D1 = 1u
    | D2 = 2u
    | D3 = 3u

type TextureAspect =
    | Undefined = 0u
    | All = 1u

type PrimitiveTopology =
    | TriangleList = 4u

type IndexFormat =
    | Undefined = 0u

type FrontFace =
    | CCW = 1u
    | CW = 2u

type CullMode =
    | None = 1u

type BlendOperation =
    | Add = 1u

type BlendFactor =
    | Zero = 1u
    | One = 2u
    | OneMinusSrcAlpha = 6u

module ColorWriteMask =
    [<Literal>]
    let All = 0xFUL

module TextureUsage =
    [<Literal>]
    let CopySrc = 0x01UL
    [<Literal>]
    let CopyDst = 0x02UL
    [<Literal>]
    let TextureBinding = 0x04UL
    [<Literal>]
    let RenderAttachment = 0x10UL

module BufferUsage =
    [<Literal>]
    let MapRead = 0x001UL
    [<Literal>]
    let CopyDst = 0x008UL
    [<Literal>]
    let Uniform = 0x040UL
    [<Literal>]
    let Storage = 0x080UL

module MapMode =
    [<Literal>]
    let Read = 0x1UL

[<Literal>]
let DepthSliceUndefined = 0xFFFFFFFFu

// ---------------------------------------------------------------------------------------------
// Structs
// ---------------------------------------------------------------------------------------------

/// A UTF-8 string slice. `{ null, SIZE_MAX }` means "no string"; `{ p, SIZE_MAX }` means null-terminated.
[<Struct; StructLayout(LayoutKind.Sequential)>]
type StringView =
    { Data: nativeint
      Length: unativeint }

    static member Null = { Data = 0n; Length = unativeint System.UIntPtr.MaxValue }

    override this.ToString() =
        if this.Data = 0n then ""
        elif this.Length = unativeint System.UIntPtr.MaxValue then
            Marshal.PtrToStringUTF8 this.Data
        else
            Marshal.PtrToStringUTF8(this.Data, int this.Length)

[<Struct; StructLayout(LayoutKind.Sequential)>]
type ChainedStruct = { Next: nativeint; SType: SType }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type Future = { Id: uint64 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type InstanceDescriptor =
    { NextInChain: nativeint
      RequiredFeatureCount: unativeint
      RequiredFeatures: nativeint
      RequiredLimits: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type SurfaceSourceWindowsHWND =
    { Chain: ChainedStruct
      Hinstance: nativeint
      Hwnd: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type SurfaceDescriptor =
    { NextInChain: nativeint
      Label: StringView }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RequestAdapterOptions =
    { NextInChain: nativeint
      FeatureLevel: FeatureLevel
      PowerPreference: PowerPreference
      ForceFallbackAdapter: uint32
      BackendType: BackendType
      CompatibleSurface: nativeint }

/// Shared shape of WGPURequestAdapterCallbackInfo, WGPURequestDeviceCallbackInfo,
/// WGPUBufferMapCallbackInfo and WGPUDeviceLostCallbackInfo.
[<Struct; StructLayout(LayoutKind.Sequential)>]
type CallbackInfo =
    { NextInChain: nativeint
      Mode: CallbackMode
      Callback: nativeint
      Userdata1: nativeint
      Userdata2: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type UncapturedErrorCallbackInfo =
    { NextInChain: nativeint
      Callback: nativeint
      Userdata1: nativeint
      Userdata2: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type QueueDescriptor =
    { NextInChain: nativeint
      Label: StringView }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type DeviceDescriptor =
    { NextInChain: nativeint
      Label: StringView
      RequiredFeatureCount: unativeint
      RequiredFeatures: nativeint
      RequiredLimits: nativeint
      DefaultQueue: QueueDescriptor
      DeviceLostCallbackInfo: CallbackInfo
      UncapturedErrorCallbackInfo: UncapturedErrorCallbackInfo }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type AdapterInfo =
    { NextInChain: nativeint
      Vendor: StringView
      Architecture: StringView
      Device: StringView
      Description: StringView
      BackendType: BackendType
      AdapterType: AdapterType
      VendorID: uint32
      DeviceID: uint32
      SubgroupMinSize: uint32
      SubgroupMaxSize: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type SurfaceCapabilities =
    { NextInChain: nativeint
      Usages: uint64
      FormatCount: unativeint
      Formats: nativeint
      PresentModeCount: unativeint
      PresentModes: nativeint
      AlphaModeCount: unativeint
      AlphaModes: nativeint }

/// wgpu.h: WGPUSurfaceConfigurationExtras, chained to a SurfaceConfiguration.
[<Struct; StructLayout(LayoutKind.Sequential)>]
type SurfaceConfigurationExtras =
    { Chain: ChainedStruct
      /// Frames in flight between getting the surface texture and showing it (1: least lag).
      DesiredMaximumFrameLatency: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type SurfaceConfiguration =
    { NextInChain: nativeint
      Device: nativeint
      Format: TextureFormat
      Usage: uint64
      Width: uint32
      Height: uint32
      ViewFormatCount: unativeint
      ViewFormats: nativeint
      AlphaMode: CompositeAlphaMode
      PresentMode: PresentMode }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type SurfaceTexture =
    { NextInChain: nativeint
      Texture: nativeint
      Status: SurfaceGetCurrentTextureStatus }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type GpuColor = { R: float; G: float; B: float; A: float }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RenderPassColorAttachment =
    { NextInChain: nativeint
      View: nativeint
      DepthSlice: uint32
      ResolveTarget: nativeint
      LoadOp: LoadOp
      StoreOp: StoreOp
      ClearValue: GpuColor }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RenderPassDescriptor =
    { NextInChain: nativeint
      Label: StringView
      ColorAttachmentCount: unativeint
      ColorAttachments: nativeint
      DepthStencilAttachment: nativeint
      OcclusionQuerySet: nativeint
      TimestampWrites: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type Extent3D =
    { Width: uint32
      Height: uint32
      DepthOrArrayLayers: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type Origin3D = { X: uint32; Y: uint32; Z: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TextureDescriptor =
    { NextInChain: nativeint
      Label: StringView
      Usage: uint64
      Dimension: TextureDimension
      Size: Extent3D
      Format: TextureFormat
      MipLevelCount: uint32
      SampleCount: uint32
      ViewFormatCount: unativeint
      ViewFormats: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type BufferDescriptor =
    { NextInChain: nativeint
      Label: StringView
      Usage: uint64
      Size: uint64
      MappedAtCreation: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TexelCopyTextureInfo =
    { Texture: nativeint
      MipLevel: uint32
      Origin: Origin3D
      Aspect: TextureAspect }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TexelCopyBufferLayout =
    { Offset: uint64
      BytesPerRow: uint32
      RowsPerImage: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TexelCopyBufferInfo =
    { Layout: TexelCopyBufferLayout
      Buffer: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type ShaderSourceWGSL = { Chain: ChainedStruct; Code: StringView }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type ShaderModuleDescriptor =
    { NextInChain: nativeint
      Label: StringView }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type VertexState =
    { NextInChain: nativeint
      Module: nativeint
      EntryPoint: StringView
      ConstantCount: unativeint
      Constants: nativeint
      BufferCount: unativeint
      Buffers: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type PrimitiveState =
    { NextInChain: nativeint
      Topology: PrimitiveTopology
      StripIndexFormat: IndexFormat
      FrontFace: FrontFace
      CullMode: CullMode
      UnclippedDepth: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type MultisampleState =
    { NextInChain: nativeint
      Count: uint32
      Mask: uint32
      AlphaToCoverageEnabled: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type BlendComponent =
    { Operation: BlendOperation
      SrcFactor: BlendFactor
      DstFactor: BlendFactor }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type BlendState =
    { Color: BlendComponent
      Alpha: BlendComponent }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type ColorTargetState =
    { NextInChain: nativeint
      Format: TextureFormat
      Blend: nativeint
      WriteMask: uint64 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type FragmentState =
    { NextInChain: nativeint
      Module: nativeint
      EntryPoint: StringView
      ConstantCount: unativeint
      Constants: nativeint
      TargetCount: unativeint
      Targets: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RenderPipelineDescriptor =
    { NextInChain: nativeint
      Label: StringView
      Layout: nativeint
      Vertex: VertexState
      Primitive: PrimitiveState
      DepthStencil: nativeint
      Multisample: MultisampleState
      Fragment: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type BindGroupEntry =
    { NextInChain: nativeint
      Binding: uint32
      Buffer: nativeint
      Offset: uint64
      Size: uint64
      Sampler: nativeint
      TextureView: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type BindGroupDescriptor =
    { NextInChain: nativeint
      Label: StringView
      Layout: nativeint
      EntryCount: unativeint
      Entries: nativeint }

// ---------------------------------------------------------------------------------------------
// Callback signatures
// ---------------------------------------------------------------------------------------------

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type RequestAdapterCallback =
    delegate of status: RequestAdapterStatus * adapter: nativeint * message: StringView * u1: nativeint * u2: nativeint -> unit

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type RequestDeviceCallback =
    delegate of status: RequestDeviceStatus * device: nativeint * message: StringView * u1: nativeint * u2: nativeint -> unit

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type DeviceLostCallback =
    delegate of device: nativeint * reason: DeviceLostReason * message: StringView * u1: nativeint * u2: nativeint -> unit

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type UncapturedErrorCallback =
    delegate of device: nativeint * errorType: ErrorType * message: StringView * u1: nativeint * u2: nativeint -> unit

[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type BufferMapCallback =
    delegate of status: MapAsyncStatus * message: StringView * u1: nativeint * u2: nativeint -> unit

// ---------------------------------------------------------------------------------------------
// Functions
// ---------------------------------------------------------------------------------------------

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuCreateInstance(InstanceDescriptor* descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuInstanceCreateSurface(nativeint instance, SurfaceDescriptor* descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern Future wgpuInstanceRequestAdapter(nativeint instance, RequestAdapterOptions* options, CallbackInfo callbackInfo)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuInstanceProcessEvents(nativeint instance)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern Status wgpuAdapterGetInfo(nativeint adapter, AdapterInfo* info)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuAdapterInfoFreeMembers(AdapterInfo info)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern Future wgpuAdapterRequestDevice(nativeint adapter, DeviceDescriptor* descriptor, CallbackInfo callbackInfo)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuDeviceGetQueue(nativeint device)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuDeviceCreateCommandEncoder(nativeint device, nativeint descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuDeviceCreateTexture(nativeint device, TextureDescriptor* descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuDeviceCreateBuffer(nativeint device, BufferDescriptor* descriptor)

/// wgpu-native extension (wgpu.h): drives callbacks; `wait` blocks until queued work finishes.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint32 wgpuDevicePoll(nativeint device, uint32 wait, nativeint submissionIndex)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern Status wgpuSurfaceGetCapabilities(nativeint surface, nativeint adapter, SurfaceCapabilities* capabilities)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuSurfaceCapabilitiesFreeMembers(SurfaceCapabilities capabilities)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuSurfaceConfigure(nativeint surface, SurfaceConfiguration* config)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuSurfaceUnconfigure(nativeint surface)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuSurfaceGetCurrentTexture(nativeint surface, SurfaceTexture* surfaceTexture)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern Status wgpuSurfacePresent(nativeint surface)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuTextureCreateView(nativeint texture, nativeint descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuCommandEncoderBeginRenderPass(nativeint encoder, RenderPassDescriptor* descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuCommandEncoderCopyTextureToBuffer(
    nativeint encoder,
    TexelCopyTextureInfo* source,
    TexelCopyBufferInfo* destination,
    Extent3D* copySize
)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuCommandEncoderFinish(nativeint encoder, nativeint descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuRenderPassEncoderEnd(nativeint pass)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuQueueSubmit(nativeint queue, unativeint commandCount, nativeint* commands)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern Future wgpuBufferMapAsync(nativeint buffer, uint64 mode, unativeint offset, unativeint size, CallbackInfo callbackInfo)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuBufferGetConstMappedRange(nativeint buffer, unativeint offset, unativeint size)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuBufferUnmap(nativeint buffer)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuBufferDestroy(nativeint buffer)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuTextureDestroy(nativeint texture)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuDeviceCreateShaderModule(nativeint device, ShaderModuleDescriptor* descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuDeviceCreateRenderPipeline(nativeint device, RenderPipelineDescriptor* descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuRenderPipelineGetBindGroupLayout(nativeint pipeline, uint32 groupIndex)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint wgpuDeviceCreateBindGroup(nativeint device, BindGroupDescriptor* descriptor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuQueueWriteBuffer(nativeint queue, nativeint buffer, uint64 bufferOffset, nativeint data, unativeint size)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuQueueWriteTexture(
    nativeint queue,
    TexelCopyTextureInfo* destination,
    nativeint data,
    unativeint dataSize,
    TexelCopyBufferLayout* dataLayout,
    Extent3D* writeSize
)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuRenderPassEncoderSetPipeline(nativeint pass, nativeint pipeline)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuRenderPassEncoderSetBindGroup(nativeint pass, uint32 groupIndex, nativeint group, unativeint dynamicOffsetCount, nativeint dynamicOffsets)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuRenderPassEncoderDraw(nativeint pass, uint32 vertexCount, uint32 instanceCount, uint32 firstVertex, uint32 firstInstance)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuShaderModuleRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuRenderPipelineRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuBindGroupRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuBindGroupLayoutRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuInstanceRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuAdapterRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuDeviceRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuQueueRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuSurfaceRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuTextureRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuTextureViewRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuCommandEncoderRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuCommandBufferRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuRenderPassEncoderRelease(nativeint handle)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void wgpuBufferRelease(nativeint handle)
