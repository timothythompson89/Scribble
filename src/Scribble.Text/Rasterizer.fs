/// Turns glyph outlines into anti-aliased coverage bitmaps.
///
/// The algorithm is the "signed area accumulation" approach used by font-rs: every line segment
/// adds the exact area it covers to an accumulation buffer, and a running sum along each row turns
/// those signed areas into per-pixel coverage. It's exact for straight edges, needs no sorting, and
/// handles overlapping contours (common in fonts made from variable fonts) by clamping at full
/// coverage. Quadratic curves are flattened into short lines first.
module Scribble.Text.Rasterizer

open System

/// A glyph bitmap: `Width × Height` coverage bytes (0 = empty, 255 = fully covered), or for color
/// glyphs `Width × Height × 4` premultiplied RGBA bytes.
/// `Left`/`Top` place its top-left pixel relative to the pen position on the baseline
/// (Top is negative above the baseline, since y points down).
[<Struct>]
type GlyphBitmap =
    { Width: int
      Height: int
      Left: int
      Top: int
      Coverage: byte[] }

    static member Empty = { Width = 0; Height = 0; Left = 0; Top = 0; Coverage = [||] }

[<Struct>]
type private Line = { X0: float; Y0: float; X1: float; Y1: float }

/// Converts contours (font units, y up) into lines in pixel space (y down), scaled by `scale`
/// and shifted right by `offsetX` pixels (for subpixel positioning).
let private flatten (contours: OutlinePoint[][]) (scale: float) (offsetX: float) =
    let lines = ResizeArray<Line>()
    let toPx (p: OutlinePoint) = struct (p.X * scale + offsetX, -p.Y * scale)

    let addLine (struct (x0, y0)) (struct (x1, y1)) =
        lines.Add { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1 }

    let addQuad (struct (x0, y0) as p0) (struct (cx, cy)) (struct (x1, y1) as p1) =
        // Number of segments from how far the curve bends (font-rs's heuristic, ~1/3 px error).
        let ddx, ddy = x0 - 2.0 * cx + x1, y0 - 2.0 * cy + y1
        let deviation = ddx * ddx + ddy * ddy
        if deviation < 0.333 then
            addLine p0 p1
        else
            let segments = 1 + int (Math.Floor(Math.Sqrt(Math.Sqrt(3.0 * deviation))))
            let mutable previous = p0
            for i in 1..segments do
                let t = float i / float segments
                let mt = 1.0 - t
                let next = struct (mt * mt * x0 + 2.0 * mt * t * cx + t * t * x1, mt * mt * y0 + 2.0 * mt * t * cy + t * t * y1)
                addLine previous next
                previous <- next

    for contour in contours do
        let n = contour.Length
        if n >= 2 then
            // Start on an on-curve point; if there is none, start at the midpoint of the first two.
            let first =
                match Array.tryFindIndex (fun (p: OutlinePoint) -> p.OnCurve) contour with
                | Some i -> i
                | None -> -1

            let startPoint =
                if first >= 0 then toPx contour[first]
                else
                    let struct (ax, ay) = toPx contour[0]
                    let struct (bx, by) = toPx contour[1]
                    struct ((ax + bx) / 2.0, (ay + by) / 2.0)

            let startIndex = if first >= 0 then first else 0
            let mutable current = startPoint
            let mutable control: struct (float * float) voption = ValueNone

            for k in 1..n do
                let p = contour[(startIndex + k) % n]
                let pt = toPx p
                if p.OnCurve then
                    match control with
                    | ValueSome c -> addQuad current c pt
                    | ValueNone -> addLine current pt
                    control <- ValueNone
                    current <- pt
                else
                    match control with
                    | ValueSome c ->
                        // Two control points in a row imply an on-curve point halfway between them.
                        let struct (cx, cy) = c
                        let struct (px, py) = pt
                        let mid = struct ((cx + px) / 2.0, (cy + py) / 2.0)
                        addQuad current c mid
                        current <- mid
                    | ValueNone -> ()
                    control <- ValueSome pt

            // Close the contour.
            match control with
            | ValueSome c -> addQuad current c startPoint
            | ValueNone -> if current <> startPoint then addLine current startPoint
    lines

/// Accumulates the signed area of one line into `acc` (row-major, `width` wide).
let private accumulate (acc: float32[]) (width: int) (height: int) (line: Line) =
    if line.Y0 <> line.Y1 then
        let dir, x0, y0, x1, y1 =
            if line.Y0 < line.Y1 then 1.0, line.X0, line.Y0, line.X1, line.Y1
            else -1.0, line.X1, line.Y1, line.X0, line.Y0
        let dxdy = (x1 - x0) / (y1 - y0)
        let mutable x = x0
        if y0 < 0.0 then x <- x - y0 * dxdy
        let yStart = max 0 (int (Math.Floor y0))
        let yEnd = min height (int (Math.Ceiling y1))
        for y in yStart .. yEnd - 1 do
            let rowStart = y * width
            let dy = min (float (y + 1)) y1 - max (float y) y0
            let xNext = x + dxdy * dy
            let d = dy * dir
            let xa, xb = if x < xNext then x, xNext else xNext, x
            let xaFloor = Math.Floor xa
            let xai = int xaFloor
            let xbCeil = Math.Ceiling xb
            let xbi = int xbCeil
            let add (i: int) (v: float) =
                let index = rowStart + i
                if i >= 0 && index < acc.Length then acc[index] <- acc[index] + float32 v
            if xbi <= xai + 1 then
                // The segment stays within one pixel column on this row.
                let xm = 0.5 * (x + xNext) - xaFloor
                add xai (d - d * xm)
                add (xai + 1) (d * xm)
            else
                let s = 1.0 / (xb - xa)
                let xaf = xa - xaFloor
                let a0 = 0.5 * s * (1.0 - xaf) * (1.0 - xaf)
                let xbf = xb - xbCeil + 1.0
                let am = 0.5 * s * xbf * xbf
                add xai (d * a0)
                if xbi = xai + 2 then
                    add (xai + 1) (d * (1.0 - a0 - am))
                else
                    let a1 = s * (1.5 - xaf)
                    add (xai + 1) (d * (a1 - a0))
                    for xi in xai + 2 .. xbi - 2 do
                        add xi (d * s)
                    let a2 = a1 + float (xbi - xai - 3) * s
                    add (xbi - 1) (d * (1.0 - a2 - am))
                add xbi (d * am)
            x <- xNext

/// Fills closed polygons given as lines in pixel space (y down), nonzero rule.
let private rasterizeLines (lines: ResizeArray<Line>) : GlyphBitmap =
    if lines.Count = 0 then
        GlyphBitmap.Empty
    else
        let mutable minX, minY, maxX, maxY = Double.MaxValue, Double.MaxValue, Double.MinValue, Double.MinValue
        for l in lines do
            minX <- min minX (min l.X0 l.X1)
            maxX <- max maxX (max l.X0 l.X1)
            minY <- min minY (min l.Y0 l.Y1)
            maxY <- max maxY (max l.Y0 l.Y1)
        let left = int (Math.Floor minX)
        let top = int (Math.Floor minY)
        // One extra column: the accumulation spills one pixel to the right of the last edge.
        let width = int (Math.Ceiling maxX) - left + 1
        let height = int (Math.Ceiling maxY) - top
        if width <= 0 || height <= 0 then
            GlyphBitmap.Empty
        else
            let acc = Array.zeroCreate<float32> (width * height + 1)
            for l in lines do
                accumulate acc width height { X0 = l.X0 - float left; Y0 = l.Y0 - float top; X1 = l.X1 - float left; Y1 = l.Y1 - float top }
            let coverage = Array.zeroCreate<byte> (width * height)
            for y in 0 .. height - 1 do
                // Each row's signed areas sum to zero for closed outlines, so a per-row running sum
                // keeps rounding error from leaking into the next row.
                let mutable sum = 0.0f
                for x in 0 .. width - 1 do
                    sum <- sum + acc[y * width + x]
                    coverage[y * width + x] <- byte (MathF.Min(1.0f, MathF.Abs sum) * 255.0f + 0.5f)
            { Width = width; Height = height; Left = left; Top = top; Coverage = coverage }

/// Rasterizes contours at `scale` pixels per font unit, shifted right by `offsetX` (0 ≤ offsetX < 1).
let rasterize (contours: OutlinePoint[][]) (scale: float) (offsetX: float) : GlyphBitmap =
    rasterizeLines (flatten contours scale offsetX)

/// Turns path contours into lines in pixel space, each point mapped by `map`. Curves are split
/// by Wang's formula so no line strays more than 0.1 px from the curve (the lines run inside a
/// curve, so a coarser tolerance visibly shrinks round shapes).
let private flattenPath (contours: PathContour[]) (map: float -> float -> struct (float * float)) =
    let lines = ResizeArray<Line>()
    let tolerance = 0.1
    let add (struct (x0, y0)) (struct (x1, y1)) = lines.Add { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1 }
    let length (x: float) (y: float) = sqrt (x * x + y * y)
    for contour in contours do
        let start = map contour.StartX contour.StartY
        let mutable current = start
        for segment in contour.Segments do
            match segment with
            | LineTo(x, y) ->
                let p = map x y
                add current p
                current <- p
            | QuadTo(cx, cy, x, y) ->
                let struct (x0, y0) = current
                let struct (x1, y1) = map cx cy
                let struct (x2, y2) as p2 = map x y
                let dd = length (x0 - 2.0 * x1 + x2) (y0 - 2.0 * y1 + y2)
                let n = max 1 (int (Math.Ceiling(sqrt (0.25 * dd / tolerance))))
                for i in 1..n do
                    let t = float i / float n
                    let mt = 1.0 - t
                    let p = if i = n then p2 else struct (mt * mt * x0 + 2.0 * mt * t * x1 + t * t * x2, mt * mt * y0 + 2.0 * mt * t * y1 + t * t * y2)
                    add current p
                    current <- p
            | CubicTo(c1x, c1y, c2x, c2y, x, y) ->
                let struct (x0, y0) = current
                let struct (x1, y1) = map c1x c1y
                let struct (x2, y2) = map c2x c2y
                let struct (x3, y3) as p3 = map x y
                let dd = max (length (x0 - 2.0 * x1 + x2) (y0 - 2.0 * y1 + y2)) (length (x1 - 2.0 * x2 + x3) (y1 - 2.0 * y2 + y3))
                let n = max 1 (int (Math.Ceiling(sqrt (0.75 * dd / tolerance))))
                for i in 1..n do
                    let t = float i / float n
                    let mt = 1.0 - t
                    let a, b, c, d = mt * mt * mt, 3.0 * mt * mt * t, 3.0 * mt * t * t, t * t * t
                    let p = if i = n then p3 else struct (a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3)
                    add current p
                    current <- p
        // Filled shapes are closed.
        if current <> start then add current start
    lines

/// Rasterizes a path, each point mapped into pixel space (y down) by `map`. Nonzero fill.
let rasterizePath (path: PathGeometry) (map: float -> float -> struct (float * float)) : GlyphBitmap =
    rasterizeLines (flattenPath path.Contours map)

/// Rasterizes an icon into a `sizePx` square: the viewBox (x, y, width, height) centered,
/// flipped, rotated clockwise by `rotation` degrees and scaled to fit. The bitmap's Left/Top are
/// relative to the square's top-left.
let rasterizeIcon (path: PathGeometry) (viewX: float, viewY: float, viewWidth: float, viewHeight: float) (sizePx: float) (rotation: float) (flipH: bool) (flipV: bool) : GlyphBitmap =
    let scale = sizePx / max viewWidth viewHeight
    let cx, cy = viewX + viewWidth / 2.0, viewY + viewHeight / 2.0
    let fx, fy = (if flipH then -1.0 else 1.0), (if flipV then -1.0 else 1.0)
    let angle = rotation * Math.PI / 180.0
    let c, s = cos angle, sin angle
    let half = sizePx / 2.0
    let map (x: float) (y: float) =
        let x, y = (x - cx) * fx, (y - cy) * fy
        struct ((x * c - y * s) * scale + half, (x * s + y * c) * scale + half)
    rasterizePath path map

/// Rasterizes a color glyph (COLR v0): each layer is an outline glyph in one color, painted
/// bottom to top with "over" blending. Returns premultiplied RGBA bytes.
/// Layers in the foreground color (no palette color) are painted with `foreground`.
let rasterizeColor (face: FontFace) (layers: ColorLayer[]) (scale: float) (offsetX: float) (foreground: struct (byte * byte * byte * byte)) : GlyphBitmap =
    let parts =
        layers
        |> Array.map (fun layer -> layer, rasterize (face.Outline layer.Glyph) scale offsetX)
        |> Array.filter (fun (_, b) -> b.Width > 0)
    if parts.Length = 0 then
        GlyphBitmap.Empty
    else
        let left = parts |> Array.map (fun (_, b) -> b.Left) |> Array.min
        let top = parts |> Array.map (fun (_, b) -> b.Top) |> Array.min
        let right = parts |> Array.map (fun (_, b) -> b.Left + b.Width) |> Array.max
        let bottom = parts |> Array.map (fun (_, b) -> b.Top + b.Height) |> Array.max
        let width, height = right - left, bottom - top
        let rgba = Array.zeroCreate<float32> (width * height * 4)
        for (layer, b) in parts do
            let struct (r, g, bl, a) = layer.Color |> ValueOption.defaultValue foreground
            let cr, cg, cb, ca = float32 r / 255.0f, float32 g / 255.0f, float32 bl / 255.0f, float32 a / 255.0f
            for y in 0 .. b.Height - 1 do
                for x in 0 .. b.Width - 1 do
                    let coverage = float32 b.Coverage[y * b.Width + x] / 255.0f
                    if coverage > 0.0f then
                        let alpha = coverage * ca
                        let i = ((b.Top - top + y) * width + (b.Left - left + x)) * 4
                        rgba[i] <- cr * alpha + rgba[i] * (1.0f - alpha)
                        rgba[i + 1] <- cg * alpha + rgba[i + 1] * (1.0f - alpha)
                        rgba[i + 2] <- cb * alpha + rgba[i + 2] * (1.0f - alpha)
                        rgba[i + 3] <- alpha + rgba[i + 3] * (1.0f - alpha)
        { Width = width
          Height = height
          Left = left
          Top = top
          Coverage = rgba |> Array.map (fun v -> byte (MathF.Min(1.0f, v) * 255.0f + 0.5f)) }
