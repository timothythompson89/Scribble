namespace Scribble.Rendering

open System

/// A color with straight (not premultiplied) alpha. Components are 0..1, in sRGB space:
/// Scribble blends in sRGB like most UI toolkits, so a value here is what designers pick.
[<Struct>]
type Color =
    { R: float32
      G: float32
      B: float32
      A: float32 }

    static member Rgba(r: float32, g: float32, b: float32, a: float32) = { R = r; G = g; B = b; A = a }

    static member Rgb(r: float32, g: float32, b: float32) = { R = r; G = g; B = b; A = 1.0f }

    /// From a 0xRRGGBB value, e.g. Color.Hex 0x1E1E2E.
    static member Hex(rgb: int) =
        { R = float32 ((rgb >>> 16) &&& 0xFF) / 255.0f
          G = float32 ((rgb >>> 8) &&& 0xFF) / 255.0f
          B = float32 (rgb &&& 0xFF) / 255.0f
          A = 1.0f }

    /// Hue in degrees, saturation and value in 0..1.
    static member Hsv(hue: float, saturation: float, value: float) =
        let h = ((hue % 360.0) + 360.0) % 360.0 / 60.0
        let c = value * saturation
        let x = c * (1.0 - abs ((h % 2.0) - 1.0))
        let m = value - c
        let r, g, b =
            match int (Math.Floor h) with
            | 0 -> c, x, 0.0
            | 1 -> x, c, 0.0
            | 2 -> 0.0, c, x
            | 3 -> 0.0, x, c
            | 4 -> x, 0.0, c
            | _ -> c, 0.0, x
        { R = float32 (r + m); G = float32 (g + m); B = float32 (b + m); A = 1.0f }

    member c.WithAlpha(alpha: float32) = { R = c.R; G = c.G; B = c.B; A = alpha }

    static member Transparent = { R = 0.0f; G = 0.0f; B = 0.0f; A = 0.0f }
    static member Black = Color.Rgb(0.0f, 0.0f, 0.0f)
    static member White = Color.Rgb(1.0f, 1.0f, 1.0f)
