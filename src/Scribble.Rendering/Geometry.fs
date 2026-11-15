namespace Scribble.Rendering

open System

/// An axis-aligned rectangle. Drawing code works in DIPs; the draw list converts to pixels.
[<Struct>]
type Rect =
    { X: float
      Y: float
      Width: float
      Height: float }

    member r.Left = r.X
    member r.Top = r.Y
    member r.Right = r.X + r.Width
    member r.Bottom = r.Y + r.Height
    member r.IsEmpty = r.Width <= 0.0 || r.Height <= 0.0

    static member Create(x, y, width, height) : Rect = { X = x; Y = y; Width = width; Height = height }

    static member FromEdges(left, top, right, bottom) : Rect =
        { X = left; Y = top; Width = right - left; Height = bottom - top }

    /// Grows (positive) or shrinks (negative) the rect on every side.
    member r.Inflate(amount: float) : Rect =
        { X = r.X - amount; Y = r.Y - amount; Width = r.Width + 2.0 * amount; Height = r.Height + 2.0 * amount }

    member r.Offset(dx: float, dy: float) : Rect =
        { X = r.X + dx; Y = r.Y + dy; Width = r.Width; Height = r.Height }

    /// The overlap of two rects; empty (zero size) when they don't overlap.
    member r.Intersect(other: Rect) : Rect =
        let left = max r.Left other.Left
        let top = max r.Top other.Top
        let right = min r.Right other.Right
        let bottom = min r.Bottom other.Bottom
        Rect.FromEdges(left, top, max left right, max top bottom)

/// Radii of a rounded rectangle's corners, clockwise from the top left.
[<Struct>]
type CornerRadius =
    { TopLeft: float
      TopRight: float
      BottomRight: float
      BottomLeft: float }

    static member Uniform(radius: float) =
        { TopLeft = radius; TopRight = radius; BottomRight = radius; BottomLeft = radius }

    static member Zero = CornerRadius.Uniform 0.0

    member c.Scale(factor: float) =
        { TopLeft = c.TopLeft * factor
          TopRight = c.TopRight * factor
          BottomRight = c.BottomRight * factor
          BottomLeft = c.BottomLeft * factor }

    member c.IsZero = c.TopLeft = 0.0 && c.TopRight = 0.0 && c.BottomRight = 0.0 && c.BottomLeft = 0.0

/// A 2D transform limited to scale and translation, so rects stay axis-aligned and clips stay
/// rectangles. (Rotation can come later; it changes clipping, not the API.)
[<Struct>]
type Transform =
    { ScaleX: float
      ScaleY: float
      OffsetX: float
      OffsetY: float }

    static member Identity = { ScaleX = 1.0; ScaleY = 1.0; OffsetX = 0.0; OffsetY = 0.0 }
    static member Translate(dx, dy) = { Transform.Identity with OffsetX = dx; OffsetY = dy }
    static member Scale(sx, sy) = { Transform.Identity with ScaleX = sx; ScaleY = sy }

    /// Applies `inner` first, then this transform.
    member t.Then(inner: Transform) =
        { ScaleX = t.ScaleX * inner.ScaleX
          ScaleY = t.ScaleY * inner.ScaleY
          OffsetX = t.OffsetX + t.ScaleX * inner.OffsetX
          OffsetY = t.OffsetY + t.ScaleY * inner.OffsetY }

    member t.Apply(r: Rect) : Rect =
        let x0 = t.OffsetX + t.ScaleX * r.Left
        let y0 = t.OffsetY + t.ScaleY * r.Top
        let x1 = t.OffsetX + t.ScaleX * r.Right
        let y1 = t.OffsetY + t.ScaleY * r.Bottom
        Rect.FromEdges(min x0 x1, min y0 y1, max x0 x1, max y0 y1)
