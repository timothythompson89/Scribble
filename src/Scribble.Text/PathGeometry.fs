namespace Scribble.Text

open System
open System.Globalization
open System.Threading

/// One piece of a contour, in path coordinates (y down, as in SVG).
type internal PathSegment =
    | LineTo of x: float * y: float
    | QuadTo of cx: float * cy: float * x: float * y: float
    | CubicTo of c1x: float * c1y: float * c2x: float * c2y: float * x: float * y: float

/// A closed shape: a start point and segments (the last point joins the start when filled).
type internal PathContour = { StartX: float; StartY: float; Segments: PathSegment[] }

/// Reads SVG path data ("M2 12h20v2z"): M, L, H, V, C, S, Q, T, A and Z, absolute and relative.
module internal PathParser =
    type private Scanner(text: string) =
        let mutable i = 0
        member _.Position = i

        member _.SkipSeparators() =
            while i < text.Length && (Char.IsWhiteSpace text[i] || text[i] = ',') do
                i <- i + 1

        member this.AtEnd =
            this.SkipSeparators()
            i >= text.Length

        /// Whether a number comes next (more arguments for the current command).
        member this.NumberNext =
            this.SkipSeparators()
            i < text.Length && (Char.IsDigit text[i] || text[i] = '-' || text[i] = '+' || text[i] = '.')

        member this.Command() : char =
            this.SkipSeparators()
            let c = text[i]
            if not (Char.IsLetter c) then
                raise (FormatException($"Path data: expected a command at {i}, found '{c}'."))
            i <- i + 1
            c

        /// A number: sign, digits, one decimal point, exponent. "1.5.5" is two numbers, "1-2" too.
        member this.Number() : float =
            this.SkipSeparators()
            let start = i
            if i < text.Length && (text[i] = '-' || text[i] = '+') then i <- i + 1
            let mutable seenDot = false
            let mutable seenDigit = false
            let mutable go = true
            while go && i < text.Length do
                let c = text[i]
                if Char.IsDigit c then
                    seenDigit <- true
                    i <- i + 1
                elif c = '.' && not seenDot then
                    seenDot <- true
                    i <- i + 1
                else go <- false
            if seenDigit && i < text.Length && (text[i] = 'e' || text[i] = 'E') then
                let save = i
                i <- i + 1
                if i < text.Length && (text[i] = '-' || text[i] = '+') then i <- i + 1
                if i < text.Length && Char.IsDigit text[i] then
                    while i < text.Length && Char.IsDigit text[i] do
                        i <- i + 1
                else i <- save
            if not seenDigit then
                raise (FormatException($"Path data: expected a number at {start}."))
            Double.Parse(text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture)

        /// An arc flag: a single 0 or 1, which may be written without separators ("a1 1 0 00 1 1").
        member this.Flag() : bool =
            this.SkipSeparators()
            if i < text.Length && (text[i] = '0' || text[i] = '1') then
                i <- i + 1
                text[i - 1] = '1'
            else raise (FormatException($"Path data: expected an arc flag (0 or 1) at {i}."))

    /// Appends an SVG elliptical arc as cubic curves (SVG 1.1, appendix F.6.5/F.6.6), at most 90° each.
    let private arcToCubics (segments: ResizeArray<PathSegment>) (x1: float) (y1: float) (rxIn: float) (ryIn: float) (angle: float) (large: bool) (sweep: bool) (x2: float) (y2: float) =
        let mutable rx, ry = abs rxIn, abs ryIn
        if (x1 = x2 && y1 = y2) then ()
        elif rx = 0.0 || ry = 0.0 then segments.Add(LineTo(x2, y2))
        else
            let phi = angle * Math.PI / 180.0
            let cosPhi, sinPhi = cos phi, sin phi
            let dx, dy = (x1 - x2) / 2.0, (y1 - y2) / 2.0
            let x1p = cosPhi * dx + sinPhi * dy
            let y1p = -sinPhi * dx + cosPhi * dy
            // Radii too small to reach: scale them up (F.6.6).
            let lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry)
            if lambda > 1.0 then
                let s = sqrt lambda
                rx <- rx * s
                ry <- ry * s
            let num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
            let den = rx * rx * y1p * y1p + ry * ry * x1p * x1p
            let coef = (if large = sweep then -1.0 else 1.0) * sqrt (max 0.0 (num / den))
            let cxp = coef * rx * y1p / ry
            let cyp = -coef * ry * x1p / rx
            let cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0
            let cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0
            let vectorAngle (ux: float) (uy: float) (vx: float) (vy: float) =
                let a = atan2 (ux * vy - uy * vx) (ux * vx + uy * vy)
                a
            let theta1 = vectorAngle 1.0 0.0 ((x1p - cxp) / rx) ((y1p - cyp) / ry)
            let mutable delta = vectorAngle ((x1p - cxp) / rx) ((y1p - cyp) / ry) ((-x1p - cxp) / rx) ((-y1p - cyp) / ry)
            if not sweep && delta > 0.0 then delta <- delta - 2.0 * Math.PI
            elif sweep && delta < 0.0 then delta <- delta + 2.0 * Math.PI
            let pieces = max 1 (int (Math.Ceiling(abs delta / (Math.PI / 2.0) - 1e-9)))
            let step = delta / float pieces
            // Control point distance for a circular arc of `step` radians.
            let k = 4.0 / 3.0 * tan (step / 4.0)
            let point (t: float) =
                let ex, ey = rx * cos t, ry * sin t
                struct (cosPhi * ex - sinPhi * ey + cx, sinPhi * ex + cosPhi * ey + cy)
            let derivative (t: float) =
                let ex, ey = -rx * sin t, ry * cos t
                struct (cosPhi * ex - sinPhi * ey, sinPhi * ex + cosPhi * ey)
            let mutable t = theta1
            for p in 0 .. pieces - 1 do
                let t2 = t + step
                let struct (ax, ay) = point t
                let struct (dax, day) = derivative t
                let struct (bx, by) = if p = pieces - 1 then struct (x2, y2) else point t2
                let struct (dbx, dby) = derivative t2
                segments.Add(CubicTo(ax + k * dax, ay + k * day, bx - k * dbx, by - k * dby, bx, by))
                t <- t2

    /// Parses path data into contours. Throws FormatException on malformed data.
    let parse (data: string) : PathContour[] =
        let s = Scanner(data)
        let contours = ResizeArray<PathContour>()
        let segments = ResizeArray<PathSegment>()
        let mutable startX, startY = 0.0, 0.0
        let mutable x, y = 0.0, 0.0
        // The last control point, for S (cubic) and T (quadratic) reflections.
        let mutable lastCubic: struct (float * float) voption = ValueNone
        let mutable lastQuad: struct (float * float) voption = ValueNone
        let mutable open' = false

        let finish () =
            if open' then
                if segments.Count > 0 then
                    contours.Add { StartX = startX; StartY = startY; Segments = segments.ToArray() }
                segments.Clear()
                open' <- false

        let begin' nx ny =
            finish ()
            startX <- nx
            startY <- ny
            x <- nx
            y <- ny
            open' <- true

        let ensureOpen () = if not open' then begin' x y

        while not s.AtEnd do
            let command = s.Command()
            let relative = Char.IsLower command
            let ox () = if relative then x else 0.0
            let oy () = if relative then y else 0.0
            let mutable first = true
            // Each command repeats while numbers follow (M's extra pairs are lines).
            while first || (Char.ToUpperInvariant command <> 'Z' && s.NumberNext) do
                let mutable cubic: struct (float * float) voption = ValueNone
                let mutable quad: struct (float * float) voption = ValueNone
                match Char.ToUpperInvariant command with
                | 'M' ->
                    let nx = ox () + s.Number()
                    let ny = oy () + s.Number()
                    if first then begin' nx ny
                    else
                        segments.Add(LineTo(nx, ny))
                        x <- nx
                        y <- ny
                | 'L' ->
                    ensureOpen ()
                    let nx = ox () + s.Number()
                    let ny = oy () + s.Number()
                    segments.Add(LineTo(nx, ny))
                    x <- nx
                    y <- ny
                | 'H' ->
                    ensureOpen ()
                    let nx = ox () + s.Number()
                    segments.Add(LineTo(nx, y))
                    x <- nx
                | 'V' ->
                    ensureOpen ()
                    let ny = oy () + s.Number()
                    segments.Add(LineTo(x, ny))
                    y <- ny
                | 'C' ->
                    ensureOpen ()
                    let bx, by = ox (), oy ()
                    let c1x = bx + s.Number()
                    let c1y = by + s.Number()
                    let c2x = bx + s.Number()
                    let c2y = by + s.Number()
                    let nx = bx + s.Number()
                    let ny = by + s.Number()
                    segments.Add(CubicTo(c1x, c1y, c2x, c2y, nx, ny))
                    cubic <- ValueSome(struct (c2x, c2y))
                    x <- nx
                    y <- ny
                | 'S' ->
                    ensureOpen ()
                    let bx, by = ox (), oy ()
                    let c1x, c1y =
                        match lastCubic with
                        | ValueSome(struct (px, py)) -> 2.0 * x - px, 2.0 * y - py
                        | ValueNone -> x, y
                    let c2x = bx + s.Number()
                    let c2y = by + s.Number()
                    let nx = bx + s.Number()
                    let ny = by + s.Number()
                    segments.Add(CubicTo(c1x, c1y, c2x, c2y, nx, ny))
                    cubic <- ValueSome(struct (c2x, c2y))
                    x <- nx
                    y <- ny
                | 'Q' ->
                    ensureOpen ()
                    let bx, by = ox (), oy ()
                    let cx = bx + s.Number()
                    let cy = by + s.Number()
                    let nx = bx + s.Number()
                    let ny = by + s.Number()
                    segments.Add(QuadTo(cx, cy, nx, ny))
                    quad <- ValueSome(struct (cx, cy))
                    x <- nx
                    y <- ny
                | 'T' ->
                    ensureOpen ()
                    let cx, cy =
                        match lastQuad with
                        | ValueSome(struct (px, py)) -> 2.0 * x - px, 2.0 * y - py
                        | ValueNone -> x, y
                    let nx = ox () + s.Number()
                    let ny = oy () + s.Number()
                    segments.Add(QuadTo(cx, cy, nx, ny))
                    quad <- ValueSome(struct (cx, cy))
                    x <- nx
                    y <- ny
                | 'A' ->
                    ensureOpen ()
                    let rx = s.Number()
                    let ry = s.Number()
                    let angle = s.Number()
                    let large = s.Flag()
                    let sweep = s.Flag()
                    let nx = ox () + s.Number()
                    let ny = oy () + s.Number()
                    arcToCubics segments x y rx ry angle large sweep nx ny
                    x <- nx
                    y <- ny
                | 'Z' ->
                    // Closing returns to the start; the next command starts there.
                    finish ()
                    x <- startX
                    y <- startY
                | c -> raise (FormatException($"Path data: unknown command '{c}' at {s.Position - 1}."))
                lastCubic <- cubic
                lastQuad <- quad
                first <- false
        finish ()
        contours.ToArray()

module internal PathIds =
    let private counter = ref 0
    let next () = Interlocked.Increment(&counter.contents)

/// A shape from SVG path data, parsed once: `PathGeometry.Parse "M2 12h20v2H2z"`. Filled with the
/// nonzero rule (as fonts are); SVG's even-odd rule isn't supported. Icons are made of these.
[<Sealed>]
type PathGeometry private (data: string, contours: PathContour[]) =
    let id = PathIds.next ()
    /// The path data it was made from.
    member _.Data = data
    member internal _.Id = id
    member internal _.Contours = contours

    /// Parses SVG path data (M, L, H, V, C, S, Q, T, A, Z; absolute and relative). Throws
    /// FormatException when the data is malformed.
    static member Parse(data: string) : PathGeometry =
        if isNull data then nullArg (nameof data)
        PathGeometry(data, PathParser.parse data)
