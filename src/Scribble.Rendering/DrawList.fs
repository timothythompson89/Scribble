namespace Scribble.Rendering

open System
open System.Runtime.InteropServices

/// How a shape's edges meet the pixel grid.
type EdgeMode =
    /// Rect edges and border widths are rounded to whole physical pixels, so 1 DIP lines stay
    /// sharp at any scale. The default for boxes, borders and fills.
    | Snapped
    /// Edges land exactly where the math says; anti-aliasing smooths partial pixels. Use for
    /// shapes that move smoothly (animations), where snapping would make them jitter.
    | Precise

/// How a border is drawn along its length.
[<RequireQualifiedAccess; Struct>]
type StrokeStyle =
    | Solid
    /// Dashes `dash` long with `gap` between them (DIPs). Around a closed shape both are adjusted a
    /// little so the pattern meets itself evenly; dashes follow rounded corners.
    | Dashed of dash: float * gap: float
    /// Round dots as wide as the line, one line width apart (adjusted to meet evenly).
    | Dotted

/// How an image is filtered when drawn bigger than its pixels.
[<RequireQualifiedAccess>]
type ImageScaling =
    /// Bilinear: smooth when enlarging (and when shrinking).
    | Smooth
    /// Nearest pixel when enlarging, so pixels stay square (CSS `image-rendering: pixelated`); shrinking is
    /// smooth, as in CSS.
    | Pixelated

/// A point in a draw list to go back to (`DrawList.Mark`, `DrawList.RollBack`).
[<Struct>]
type DrawListMark = internal { Shapes: int; Transforms: int; Clips: int; Opacities: int; Images: int }

/// An image shape in a draw list, for the image atlas to place (DESIGN.md, "M8 step 3b", (c) 1): its instance,
/// the decoded image, the part of it drawn (image pixels) and the mip level the draw list chose.
[<Struct>]
type internal ImageDraw = { Instance: int; Image: Scribble.Imaging.DecodedImage; Source: Rect; Level: int }

/// A list of shapes to draw in one frame. Controls draw into this; a backend turns it into GPU
/// work (one instanced draw call for all shapes).
///
/// Coordinates are DIPs. The list starts with a transform that scales DIPs to physical pixels;
/// PushTransform and PushClip nest on top of it.
///
/// Every shape becomes one "instance" of 24 floats, laid out exactly as `Shape` in shapes.wgsl:
///   rect (x, y, w, h) · radii (tl, tr, br, bl) · fill rgba · border rgba ·
///   params (border width, shadow sigma, kind) · pattern · clip (x0, y0, x1, y1)   — all in physical pixels.
/// The pattern is a u32 stored in the float's bits: 0 solid; else dash (high 16 bits) and gap (low 16
/// bits) in 1/16 px; a dash of 0xFFFF means dotted.
/// Kind 0 is a shape; kind 1 is a glyph (coverage atlas), kind 2 a color glyph (color atlas);
/// for both, radii.xy hold the glyph's top-left texel in its atlas. Kind 3 is an image: radii round its
/// corners, params.x is its opacity and params.y 1 for pixelated; the image atlas fills in the rest when it
/// places the image (fill = whole texel of the origin x, y and the step x, y in 1/65536 texel per pixel;
/// border = the texels it may read, x0, y0, x1, y1; pattern = the origin's 1/256 fractions, x << 8 | y).
[<Sealed>]
type DrawList(scale: float) =
    static let floatsPerShape = 24

    let mutable data: float32[] = Array.zeroCreate (floatsPerShape * 256)
    let mutable count = 0
    let rootTransform = Transform.Scale(scale, scale)
    let transforms = Collections.Generic.Stack<Transform>()
    let clips = Collections.Generic.Stack<Rect>()
    let mutable transform = rootTransform
    let noClip = Rect.FromEdges(-1e7, -1e7, 1e7, 1e7)
    let mutable clip = noClip
    let opacities = Collections.Generic.Stack<float32>()
    let images = ResizeArray<ImageDraw>()
    // Multiplies the alpha of every shape drawn (PushOpacity), 1 when none is active.
    let mutable opacity = 1.0f

    let ensureCapacity () =
        if (count + 1) * floatsPerShape > data.Length then
            let grown = Array.zeroCreate (data.Length * 2)
            Array.blit data 0 grown 0 (count * floatsPerShape)
            data <- grown

    let snapRect (r: Rect) =
        Rect.FromEdges(Math.Round r.Left, Math.Round r.Top, Math.Round r.Right, Math.Round r.Bottom)

    let snapWidth (w: float) = if w <= 0.0 then 0.0 else max 1.0 (Math.Round w)

    let pushPattern (kind: float) (rect: Rect) (radii: CornerRadius) (fill: Color) (border: Color) (borderWidth: float) (sigma: float) (pattern: int) =
        // Skip shapes that are fully clipped away or invisible.
        let reach = 1.0 + 3.0 * sigma
        let fill = if opacity < 1.0f then fill.WithAlpha(fill.A * opacity) else fill
        let border = if opacity < 1.0f then border.WithAlpha(border.A * opacity) else border
        if not (rect.Inflate(reach).Intersect(clip).IsEmpty) && (fill.A > 0.0f || (border.A > 0.0f && borderWidth > 0.0)) then
            ensureCapacity ()
            let i = count * floatsPerShape
            let inline set k (v: float) = data[i + k] <- float32 v
            set 0 rect.X
            set 1 rect.Y
            set 2 rect.Width
            set 3 rect.Height
            set 4 radii.TopLeft
            set 5 radii.TopRight
            set 6 radii.BottomRight
            set 7 radii.BottomLeft
            data[i + 8] <- fill.R
            data[i + 9] <- fill.G
            data[i + 10] <- fill.B
            data[i + 11] <- fill.A
            data[i + 12] <- border.R
            data[i + 13] <- border.G
            data[i + 14] <- border.B
            data[i + 15] <- border.A
            set 16 borderWidth
            set 17 sigma
            set 18 kind
            data[i + 19] <- BitConverter.Int32BitsToSingle pattern
            set 20 clip.Left
            set 21 clip.Top
            set 22 clip.Right
            set 23 clip.Bottom
            count <- count + 1

    let pushKind kind rect radii fill border borderWidth sigma = pushPattern kind rect radii fill border borderWidth sigma 0

    let push = pushKind 0.0

    /// A stroke style as the shader's pattern word, from DIPs (lengths in 1/16 physical pixel; kept
    /// below 2,000 px so the word's float reading is never NaN or denormal-sensitive on the CPU side).
    let patternOf (style: StrokeStyle) (scale: float) =
        match style with
        | StrokeStyle.Solid -> 0
        | StrokeStyle.Dotted -> 0xFFFF <<< 16
        | StrokeStyle.Dashed(dash, gap) ->
            let q (v: float) = int (Math.Round(Math.Clamp(v * scale, 0.0, 2000.0) * 16.0))
            let d, g = max 1 (q dash), q gap
            if g = 0 then 0 else (d <<< 16) ||| g

    let toPixels (mode: EdgeMode) (rect: Rect) =
        let r = transform.Apply rect
        if mode = Snapped then snapRect r else r

    let radiiToPixels (radii: CornerRadius) = radii.Scale(abs transform.ScaleX)

    /// Physical pixels per DIP for this frame.
    member _.Scale = scale

    /// The current transform from DIPs (as passed to drawing calls) to physical pixels.
    member _.Transform = transform

    /// Number of shapes recorded.
    member _.Count = count

    /// For backends: the recorded shapes, `Count` × 24 floats. Treat as read-only.
    member _.InstanceData = ArraySegment<float32>(data, 0, count * floatsPerShape)

    /// For backends: the recorded shapes as raw bytes, ready to upload.
    member this.InstanceBytes: Span<byte> = MemoryMarshal.AsBytes(this.InstanceData.AsSpan())

    static member BytesPerShape = floatsPerShape * sizeof<float32>

    /// Removes all shapes and resets the transform and clip stacks.
    member _.Reset() =
        count <- 0
        images.Clear()
        transforms.Clear()
        clips.Clear()
        opacities.Clear()
        transform <- rootTransform
        clip <- noClip
        opacity <- 1.0f

    /// Where the list is now: the shapes recorded and how deep the transform, clip and opacity
    /// stacks are. `RollBack` returns to it (an element whose drawing failed takes back what it drew).
    member _.Mark: DrawListMark =
        { Shapes = count; Transforms = transforms.Count; Clips = clips.Count; Opacities = opacities.Count; Images = images.Count }

    /// Removes the shapes recorded since `mark` and pops what was pushed since then.
    member _.RollBack(mark: DrawListMark) =
        count <- min count mark.Shapes
        if images.Count > mark.Images then images.RemoveRange(mark.Images, images.Count - mark.Images)
        while transforms.Count > mark.Transforms do
            transform <- transforms.Pop()
        while clips.Count > mark.Clips do
            clip <- clips.Pop()
        while opacities.Count > mark.Opacities do
            opacity <- opacities.Pop()

    // --- Transforms and clips ----------------------------------------------------------------

    member _.PushTransform(t: Transform) =
        transforms.Push transform
        transform <- transform.Then t

    member this.PushTranslate(dx: float, dy: float) = this.PushTransform(Transform.Translate(dx, dy))

    member _.PopTransform() =
        if transforms.Count = 0 then invalidOp "PopTransform without a matching PushTransform."
        transform <- transforms.Pop()

    /// Restricts drawing to `rect` (intersected with any clip already active), until PopClip.
    /// Clip edges are snapped to whole pixels.
    member _.PushClip(rect: Rect) =
        clips.Push clip
        clip <- clip.Intersect(snapRect (transform.Apply rect))

    /// Whether anything drawn inside `rect` (DIPs, under the current transform) can show: false
    /// when it lies entirely outside the current clip. Elements use it to skip drawing what a
    /// ScrollViewer has scrolled out of view.
    member _.CanShow(rect: Rect) = not ((transform.Apply rect).Intersect(clip).IsEmpty)

    member _.PopClip() =
        if clips.Count = 0 then invalidOp "PopClip without a matching PushClip."
        clip <- clips.Pop()

    /// Fades everything drawn until PopOpacity: each shape's alpha (fill, border, glyph) is
    /// multiplied by `value` (and by any opacity already active). Shapes fade one by one, so where
    /// they overlap they show through each other (not a group fade).
    member _.PushOpacity(value: float) =
        opacities.Push opacity
        opacity <- opacity * float32 (max 0.0 (min 1.0 value))

    member _.PopOpacity() =
        if opacities.Count = 0 then invalidOp "PopOpacity without a matching PushOpacity."
        opacity <- opacities.Pop()

    /// The opacity currently multiplied into every shape (1 = none).
    member _.Opacity = float opacity

    // --- Glyphs ------------------------------------------------------------------------------

    /// For text code: draws a `width × height` region of the glyph atlas, tinted with `color`, with
    /// its top-left corner at physical pixel (x, y). Glyph positions are already in pixels (text is
    /// rasterized at its final size), so the transform is not applied; the clip is.
    member _.DrawGlyph(x: int, y: int, width: int, height: int, atlasX: int, atlasY: int, color: Color) =
        let radii = { TopLeft = float atlasX; TopRight = float atlasY; BottomRight = 0.0; BottomLeft = 0.0 }
        pushKind 1.0 (Rect.Create(float x, float y, float width, float height)) radii color Color.Transparent 0.0 0.0

    /// For text code: draws a region of the color glyph atlas (premultiplied RGBA) at physical
    /// pixel (x, y). Only the alpha of `tint` is used, to fade the glyph.
    member _.DrawColorGlyph(x: int, y: int, width: int, height: int, atlasX: int, atlasY: int, tint: Color) =
        let radii = { TopLeft = float atlasX; TopRight = float atlasY; BottomRight = 0.0; BottomLeft = 0.0 }
        pushKind 2.0 (Rect.Create(float x, float y, float width, float height)) radii tint Color.Transparent 0.0 0.0

    // --- Images ------------------------------------------------------------------------------

    /// The image shapes recorded, in drawing order, for the image atlas.
    member internal _.Images = images

    /// For the image atlas: where an image shape reads its page (see the class summary for the slots).
    member internal _.PlaceImage(instance: int, originX: int, originY: int, stepX: int, stepY: int, x0: int, y0: int, x1: int, y1: int, fractions: int) =
        let i = instance * floatsPerShape
        data[i + 8] <- float32 originX
        data[i + 9] <- float32 originY
        data[i + 10] <- float32 stepX
        data[i + 11] <- float32 stepY
        data[i + 12] <- float32 x0
        data[i + 13] <- float32 y0
        data[i + 14] <- float32 x1
        data[i + 15] <- float32 y1
        data[i + 19] <- BitConverter.Int32BitsToSingle fractions

    /// For the image atlas: an instance's rectangle in physical pixels.
    member internal _.InstanceRect(instance: int) =
        let i = instance * floatsPerShape
        Rect.Create(float data[i], float data[i + 1], float data[i + 2], float data[i + 3])

    /// Draws a decoded image (premultiplied RGBA) stretched into `rect` (DIPs, snapped to whole pixels: DESIGN.md,
    /// "M8 step 3b", (c) 4). `source` is the part of the image drawn, in its pixels (all of it by default);
    /// `radius` rounds the corners; `scaling` Pixelated keeps pixels square when enlarging. The GPU device puts the
    /// image in its atlas when it draws the frame. Throws ObjectDisposedException for a released image.
    member _.DrawImage(rect: Rect, image: Scribble.Imaging.DecodedImage, ?radius: CornerRadius, ?scaling: ImageScaling, ?source: Rect) =
        if image.IsReleased then
            raise (ObjectDisposedException("DecodedImage", "This decoded image was released: it can't be drawn any more."))
        let whole = Rect.Create(0.0, 0.0, float image.Width, float image.Height)
        let source = match source with Some s -> s.Intersect whole | None -> whole
        let target = snapRect (transform.Apply rect)
        if not source.IsEmpty && target.Width >= 1.0 && target.Height >= 1.0 && opacity > 0.0f
           && not (target.Intersect(clip).IsEmpty) then
            // The first mip level shrunk by less than 2×, judged by the axis that shrinks more ((c) 5).
            let mutable ratio = max (source.Width / target.Width) (source.Height / target.Height)
            let mutable level = 0
            while ratio >= 2.0 && level < image.LevelCount - 1 do
                ratio <- ratio / 2.0
                level <- level + 1
            ensureCapacity ()
            let i = count * floatsPerShape
            Array.Clear(data, i, floatsPerShape)
            let radii = radiiToPixels (defaultArg radius CornerRadius.Zero)
            data[i] <- float32 target.X
            data[i + 1] <- float32 target.Y
            data[i + 2] <- float32 target.Width
            data[i + 3] <- float32 target.Height
            data[i + 4] <- float32 radii.TopLeft
            data[i + 5] <- float32 radii.TopRight
            data[i + 6] <- float32 radii.BottomRight
            data[i + 7] <- float32 radii.BottomLeft
            data[i + 16] <- opacity
            data[i + 17] <- (if defaultArg scaling ImageScaling.Smooth = ImageScaling.Pixelated then 1.0f else 0.0f)
            data[i + 18] <- 3.0f
            data[i + 20] <- float32 clip.Left
            data[i + 21] <- float32 clip.Top
            data[i + 22] <- float32 clip.Right
            data[i + 23] <- float32 clip.Bottom
            images.Add { Instance = count; Image = image; Source = source; Level = level }
            count <- count + 1

    // --- Shapes ------------------------------------------------------------------------------

    member _.FillRect(rect: Rect, color: Color, ?mode: EdgeMode) =
        push (toPixels (defaultArg mode Snapped) rect) CornerRadius.Zero color Color.Transparent 0.0 0.0

    member _.FillRoundedRect(rect: Rect, radius: CornerRadius, color: Color, ?mode: EdgeMode) =
        push (toPixels (defaultArg mode Snapped) rect) (radiiToPixels radius) color Color.Transparent 0.0 0.0

    /// A filled box with a border drawn inside its edge. Either part may be transparent.
    /// `borderThickness` is in DIPs; when snapped it's rounded to whole pixels (at least 1).
    /// `style` makes the border dashed or dotted (solid by default); the fill stays whole.
    member _.DrawBox(rect: Rect, radius: CornerRadius, fill: Color, borderThickness: float, borderColor: Color, ?mode: EdgeMode, ?style: StrokeStyle) =
        let mode = defaultArg mode Snapped
        let width = borderThickness * abs transform.ScaleX
        let width = if mode = Snapped then snapWidth width else width
        let pattern = match style with Some s -> patternOf s (abs transform.ScaleX) | None -> 0
        pushPattern 0.0 (toPixels mode rect) (radiiToPixels radius) fill borderColor width 0.0 pattern

    /// A border only, drawn inside the rect's edge; `style` makes it dashed or dotted.
    member this.StrokeRect(rect: Rect, radius: CornerRadius, thickness: float, color: Color, ?mode: EdgeMode, ?style: StrokeStyle) =
        this.DrawBox(rect, radius, Color.Transparent, thickness, color, defaultArg mode Snapped, defaultArg style StrokeStyle.Solid)

    /// A soft shadow behind a (rounded) box, like CSS box-shadow:
    /// `blur` is the blur radius in DIPs (≈ 2 standard deviations), `spread` grows the shape,
    /// `offsetX`/`offsetY` move it. Draw the shadow before the box it belongs to.
    member _.DrawShadow(rect: Rect, radius: CornerRadius, blur: float, color: Color, ?offsetX: float, ?offsetY: float, ?spread: float) =
        let spread = defaultArg spread 0.0
        let shaped = rect.Offset(defaultArg offsetX 0.0, defaultArg offsetY 0.0).Inflate(spread)
        let radii =
            { TopLeft = max 0.0 (radius.TopLeft + spread)
              TopRight = max 0.0 (radius.TopRight + spread)
              BottomRight = max 0.0 (radius.BottomRight + spread)
              BottomLeft = max 0.0 (radius.BottomLeft + spread) }
        let sigma = max 0.0 (blur * abs transform.ScaleX / 2.0)
        push (toPixels Precise shaped) (radiiToPixels radii) color Color.Transparent 0.0 sigma
