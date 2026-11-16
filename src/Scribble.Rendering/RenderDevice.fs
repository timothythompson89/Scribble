namespace Scribble.Rendering

open System
open Scribble.Platform

/// Which GPU to prefer when a machine has more than one.
[<RequireQualifiedAccess>]
type GpuPower =
    /// Let the system choose.
    | Default
    /// The integrated GPU: less power, less heat.
    | LowPower
    /// The discrete GPU.
    | HighPerformance

/// One frame being recorded for a surface or an offscreen target.
/// Disposing the frame submits it (and presents it, for a window surface).
type IFrame =
    inherit IDisposable

    /// Size of the target in physical pixels.
    abstract Size: PixelSize
    /// Physical pixels per DIP.
    abstract Scale: float
    /// Fills the whole target with a color before any shapes are drawn.
    abstract Clear: color: Color -> unit
    /// Shapes to draw this frame, in DIPs, on top of the clear color.
    abstract DrawList: DrawList

/// A window's swap chain.
type IRenderSurface =
    inherit IDisposable

    abstract Size: PixelSize

    /// Call when the window's client area changes size. A zero size suspends rendering.
    abstract Resize: size: PixelSize -> unit

    /// Starts a frame, or returns None when there is nothing to draw into right now
    /// (minimized window, surface lost and being recreated, timeout).
    /// `scale` is the window's scale factor: DIPs in the draw list become scale × pixels.
    abstract TryBeginFrame: scale: float -> IFrame option

/// Pixels read back from an offscreen render: tightly packed RGBA8, top row first.
type Bitmap =
    { Size: PixelSize
      Pixels: byte[] }

    member this.GetPixel(x: int, y: int) =
        let i = (y * this.Size.Width + x) * 4
        this.Pixels[i], this.Pixels[i + 1], this.Pixels[i + 2], this.Pixels[i + 3]

/// A GPU device. One per app; surfaces and offscreen targets are created from it.
type IRenderDevice =
    inherit IDisposable

    /// Name of the backend and adapter, for logs and the inspector.
    abstract Description: string

    /// Coverage atlas shared by all text drawn with this device.
    abstract GlyphAtlas: GlyphAtlas

    /// RGBA atlas for color glyphs (emoji), shared by all text drawn with this device.
    abstract ColorGlyphAtlas: GlyphAtlas

    abstract CreateSurface: window: NativeSurfaceHandle * size: PixelSize -> IRenderSurface

    /// Renders one frame offscreen and reads the pixels back. Used by screenshot tests.
    abstract RenderOffscreen: size: PixelSize * scale: float * draw: (IFrame -> unit) -> Bitmap
