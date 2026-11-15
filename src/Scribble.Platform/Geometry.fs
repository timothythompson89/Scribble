namespace Scribble.Platform

/// A size in physical (device) pixels. Used for swap chains and native window sizes.
[<Struct>]
type PixelSize =
    { Width: int
      Height: int }

    /// True when either dimension is zero, e.g. while the window is minimized.
    member this.IsEmpty = this.Width <= 0 || this.Height <= 0

    static member Zero = { Width = 0; Height = 0 }

/// A size in device-independent pixels (DIPs). 1 DIP = 1 physical pixel at 100% scaling.
[<Struct>]
type DipSize =
    { Width: float
      Height: float }

/// A point in physical pixels, relative to the window's client area.
[<Struct>]
type PixelPoint = { X: float; Y: float }
