namespace Scribble.Core

open System
open System.Globalization
open System.Runtime.CompilerServices
open Scribble.Rendering
open Scribble.Text
open Scribble.Platform

/// How an icon or an image becomes a cursor (the settings of `Cursor.fromIcon` and `Cursor.fromImage`).
type internal IconCursorOptions =
    { Size: float
      /// Whether `Cursor.size` was given (an image cursor is otherwise its image's own size).
      SizeSet: bool
      Hotspot: struct (float * float) voption
      Fill: Color
      Outline: Color
      OutlineWidth: float
      /// Image cursors only.
      Scaling: ImageScaling
      /// Whether an icon-only setting (fill, outline, outlineWidth) was given: an image cursor warns.
      IconOnlySet: bool }

type CursorSetting = internal CursorSetting of (IconCursorOptions -> IconCursorOptions)

module internal IconCursor =
    let defaults =
        { Size = 24.0
          SizeSet = false
          Hotspot = ValueNone
          Fill = Color.White
          Outline = Color.Black
          OutlineWidth = 1.0
          Scaling = ImageScaling.Smooth
          IconOnlySet = false }

    /// The icon at `scale`: filled, outlined (the shape grown by the outline width, behind it),
    /// premultiplied BGRA, with room around it for the outline.
    let render (icon: Icon) (o: IconCursorOptions) (scale: float) : CursorBitmap =
        let sizePx = max 1.0 (Math.Round(o.Size * scale))
        let radius = if o.Outline.A > 0.0f && o.OutlineWidth > 0.0 then max 1.0 (o.OutlineWidth * scale) else 0.0
        let pad = int (Math.Ceiling radius)
        let n = int sizePx + 2 * pad
        let v = icon.ViewBox
        let glyph =
            Rasterizer.rasterizeIcon (Icon.geometry icon) (v.X, v.Y, v.Width, v.Height) sizePx icon.Rotation icon.FlipHorizontal icon.FlipVertical
        let shape = Array.zeroCreate<float32> (n * n)
        for gy in 0 .. glyph.Height - 1 do
            for gx in 0 .. glyph.Width - 1 do
                let x, y = pad + glyph.Left + gx, pad + glyph.Top + gy
                if x >= 0 && y >= 0 && x < n && y < n then
                    shape[y * n + x] <- float32 glyph.Coverage[gy * glyph.Width + gx] / 255.0f
        // The outline: each pixel takes the strongest shape pixel within the radius, weaker
        // toward its edge (a 1-pixel outline is solid beside the shape, soft on diagonals).
        let grown = Array.zeroCreate<float32> (n * n)
        if radius > 0.0 then
            let reach = int (Math.Ceiling(radius + 1.0))
            for y in 0 .. n - 1 do
                for x in 0 .. n - 1 do
                    let mutable best = 0.0f
                    for dy in -reach .. reach do
                        for dx in -reach .. reach do
                            let sx, sy = x + dx, y + dy
                            if sx >= 0 && sy >= 0 && sx < n && sy < n then
                                let weight = float32 (Math.Clamp(radius + 1.0 - sqrt (float (dx * dx + dy * dy)), 0.0, 1.0))
                                best <- max best (shape[sy * n + sx] * weight)
                    grown[y * n + x] <- best
        let pixels = Array.zeroCreate<byte> (n * n * 4)
        let toByte (v: float32) = byte (Math.Clamp(Math.Round(float v * 255.0), 0.0, 255.0))
        for i in 0 .. n * n - 1 do
            // The fill over the outline, premultiplied.
            let fa = o.Fill.A * shape[i]
            let oa = o.Outline.A * grown[i] * (1.0f - fa)
            pixels[i * 4] <- toByte (o.Fill.B * fa + o.Outline.B * oa)
            pixels[i * 4 + 1] <- toByte (o.Fill.G * fa + o.Outline.G * oa)
            pixels[i * 4 + 2] <- toByte (o.Fill.R * fa + o.Outline.R * oa)
            pixels[i * 4 + 3] <- toByte (fa + oa)
        let struct (hx, hy) = o.Hotspot |> ValueOption.defaultValue (struct (o.Size / 2.0, o.Size / 2.0))
        { Width = n
          Height = n
          HotspotX = pad + int (Math.Round(hx * scale))
          HotspotY = pad + int (Math.Round(hy * scale))
          Pixels = pixels }

    /// Everything the image depends on, as text: equal keys draw equal cursors. The path data goes
    /// in as its length and hash (keys only need to differ within one run, and a whole icon's data
    /// in every key would cost a list of thousands of icon cursors real time and memory).
    let key (icon: Icon) (o: IconCursorOptions) =
        let f (v: float) = v.ToString("R", CultureInfo.InvariantCulture)
        let c (v: Color) = String.Join(",", [ f (float v.R); f (float v.G); f (float v.B); f (float v.A) ])
        let hot = match o.Hotspot with ValueSome(struct (x, y)) -> f x + "," + f y | ValueNone -> "center"
        let v = icon.ViewBox
        String.Join(
            "|",
            [ "icon"; Icon.describe icon; f v.X; f v.Y; f v.Width; f v.Height; f o.Size; hot; c o.Fill; c o.Outline
              f o.OutlineWidth; string icon.PathData.Length + ":" + string (StringComparer.Ordinal.GetHashCode icon.PathData) ]
        )

/// An image cursor's picture and settings, found from its `CursorImage` (DESIGN.md, "M8 step 3b", (f)). `Decoded` is
/// set by the element showing it once the picture is ready; the platform only asks for bitmaps after that.
[<Sealed; AllowNullLiteral>]
type internal ImageCursorSpec(image: Image, options: IconCursorOptions) =
    let mutable decoded: Scribble.Imaging.DecodedImage voption = ValueNone

    member _.Image = image
    member _.Options = options

    /// Read by the platform's cursor code, set on the UI thread.
    member this.Decoded
        with get () = lock this (fun () -> decoded)
        and set (v: Scribble.Imaging.DecodedImage voption) = lock this (fun () -> decoded <- v)

/// Image cursors by their `CursorImage` (by reference: the one `Cursor.fromImage` made), kept only while it lives.
type internal ImageCursors private () =
    [<DefaultValue>]
    static val mutable private table: ConditionalWeakTable<CursorImage, ImageCursorSpec>

    static member private Table =
        lock typeof<ImageCursors> (fun () ->
            if isNull ImageCursors.table then ImageCursors.table <- ConditionalWeakTable()
            ImageCursors.table)

    static member Add(cursor: CursorImage, spec: ImageCursorSpec) = ImageCursors.Table.AddOrUpdate(cursor, spec)

    static member Find(cursor: CursorImage) : ImageCursorSpec voption =
        match ImageCursors.Table.TryGetValue cursor with
        | true, s -> ValueSome s
        | _ -> ValueNone

module internal ImageCursor =
    /// Browsers ignore cursor images larger than 128 × 128, so every platform keeps to it ((f) 6).
    let maxSide = 128.0

    /// The cursor's size in DIPs: the image's own (pixels before any reduction ÷ density) or `Cursor.size` wide,
    /// the aspect ratio kept, shrunk to fit 128.
    let dipSize (image: Image) (o: IconCursorOptions) (width: int) (height: int) : struct (float * float) =
        let w0 = float width / image.Density
        let h0 = float height / image.Density
        let w, h = if o.SizeSet then o.Size, o.Size * h0 / w0 else w0, h0
        let largest = max w h
        if largest > maxSide then
#if DEBUG
            DebugWarnings.Once(
                "cursor-too-big:" + image.Key,
                "Cursor.fromImage: '" + image.Key
                + "' is over 128 DIPs a side as a cursor; it is shrunk to fit (browsers ignore larger cursors)."
            )
#endif
            struct (w * maxSide / largest, h * maxSide / largest)
        else
            struct (w, h)

    /// For each of `dst` pixels along one side, the `src` pixels it takes and their weights: as they are when the
    /// sizes match, the nearest when enlarging pixelated, bilinear when enlarging smooth, and the area each covers
    /// when shrinking (shrinking is smooth either way, as in CSS and `DrawList.DrawImage`).
    let weights (src: int) (dst: int) (pixelated: bool) : struct (int * float)[][] =
        let ratio = float src / float dst
        if src = dst then
            Array.init dst (fun i -> [| struct (i, 1.0) |])
        elif dst > src then
            Array.init dst (fun i ->
                if pixelated then
                    [| struct (min (src - 1) (int (Math.Floor((float i + 0.5) * ratio))), 1.0) |]
                else
                    let c = (float i + 0.5) * ratio - 0.5
                    let j = int (Math.Floor c)
                    let f = c - float j
                    let clamp k = Math.Clamp(k, 0, src - 1)
                    [| struct (clamp j, 1.0 - f); struct (clamp (j + 1), f) |])
        else
            Array.init dst (fun i ->
                let a = float i * ratio
                let b = float (i + 1) * ratio
                [| for j in int (Math.Floor a) .. min (src - 1) (int (Math.Ceiling b) - 1) do
                       let cover = min b (float (j + 1)) - max a (float j)
                       if cover > 0.0 then struct (j, cover / ratio) |])

    /// `pixels` (premultiplied RGBA, `sw` × `sh`) resampled to `dw` × `dh`, as premultiplied BGRA.
    let resample (pixels: byte[]) (sw: int) (sh: int) (dw: int) (dh: int) (pixelated: bool) : byte[] =
        let xs = weights sw dw pixelated
        let ys = weights sh dh pixelated
        // Across first (sh rows of dw), then down.
        let across = Array.zeroCreate<float> (sh * dw * 4)
        for y in 0 .. sh - 1 do
            for x in 0 .. dw - 1 do
                let o = (y * dw + x) * 4
                for struct (j, w) in xs[x] do
                    let i = (y * sw + j) * 4
                    for c in 0..3 do
                        across[o + c] <- across[o + c] + float pixels[i + c] * w
        let out = Array.zeroCreate<byte> (dw * dh * 4)
        let toByte (v: float) = byte (Math.Clamp(Math.Round v, 0.0, 255.0))
        for y in 0 .. dh - 1 do
            for x in 0 .. dw - 1 do
                let mutable r = 0.0
                let mutable g = 0.0
                let mutable b = 0.0
                let mutable a = 0.0
                for struct (j, w) in ys[y] do
                    let i = (j * dw + x) * 4
                    r <- r + across[i] * w
                    g <- g + across[i + 1] * w
                    b <- b + across[i + 2] * w
                    a <- a + across[i + 3] * w
                let o = (y * dw + x) * 4
                out[o] <- toByte b
                out[o + 1] <- toByte g
                out[o + 2] <- toByte r
                out[o + 3] <- toByte a
        out

    /// The cursor at `scale`: the picture resampled to whole pixels, the hotspot in them ((f) 3, 4).
    let render (spec: ImageCursorSpec) (scale: float) : CursorBitmap =
        match spec.Decoded with
        | ValueNone ->
            // Not asked for before the picture is ready (the element showing it waits); a blank one just in case.
            { Width = 1; Height = 1; HotspotX = 0; HotspotY = 0; Pixels = Array.zeroCreate 4 }
        | ValueSome d ->
            let o = spec.Options
            let struct (w, h) = dipSize spec.Image o d.OriginalWidth d.OriginalHeight
            let dw = max 1 (int (Math.Round(w * scale)))
            let dh = max 1 (int (Math.Round(h * scale)))
            // The hotspot is in DIPs of the size asked for (own or `Cursor.size`): moved with the cap's shrinking.
            let asked = if o.SizeSet then o.Size else float d.OriginalWidth / spec.Image.Density
            let k = if asked > 0.0 then w / asked else 1.0
            let struct (hx, hy) = o.Hotspot |> ValueOption.defaultValue (struct (asked / 2.0, asked * h / w / 2.0))
            { Width = dw
              Height = dh
              HotspotX = Math.Clamp(int (Math.Round(hx * k * scale)), 0, dw - 1)
              HotspotY = Math.Clamp(int (Math.Round(hy * k * scale)), 0, dh - 1)
              Pixels = resample d.Pixels d.Width d.Height dw dh (o.Scaling = ImageScaling.Pixelated) }

    /// Everything the bitmap depends on: equal keys draw equal cursors. "image|<the image's key>|...".
    let key (image: Image) (o: IconCursorOptions) =
        let f (v: float) = v.ToString("R", CultureInfo.InvariantCulture)
        let hot = match o.Hotspot with ValueSome(struct (x, y)) -> f x + "," + f y | ValueNone -> "center"
        let size = if o.SizeSet then f o.Size else "own"
        let scaling = match o.Scaling with ImageScaling.Pixelated -> "pixelated" | ImageScaling.Smooth -> "smooth"
        String.Join("|", [ "image"; image.Key; f image.Density; size; hot; scaling ])

/// Cursors made from icons: `Cursor.fromIcon Material.colorize [ Cursor.hotspot (3.0, 21.0) ]`,
/// then `Border.cursor` like the built-in ones. The OS shows it (a hardware cursor), made for the
/// window's scale.
[<AutoOpen>]
module CursorExtensions =
    type Scribble.Platform.Cursor with
        /// A cursor from an icon: white with a black outline (shows on any background) unless set,
        /// 24 DIPs, pointing at its center unless `Cursor.hotspot` says where. Define it once, as
        /// a value: cursors are compared by what they look like.
        static member fromIcon (icon: Icon) (settings: CursorSetting list) : Cursor =
            let o = settings |> List.fold (fun o (CursorSetting apply) -> apply o) IconCursor.defaults
            Cursor.Image { Key = IconCursor.key icon o; Render = IconCursor.render icon o }

        /// A cursor from an image, at its own size in DIPs unless `Cursor.size` gives the width (at most 128 a side),
        /// pointing at its center unless `Cursor.hotspot` says where, resampled for each window scale
        /// (`Cursor.scaling`). The element using it holds the image while it's in the tree; until the picture is
        /// ready, or if it fails (reported to `App.onError`), the arrow shows. Define it once, as a value.
        static member fromImage (image: Image) (settings: CursorSetting list) : Cursor =
            let o = settings |> List.fold (fun o (CursorSetting apply) -> apply o) IconCursor.defaults
#if DEBUG
            if o.IconOnlySet then
                DebugWarnings.Once(
                    "cursor-image-icon-settings",
                    "Cursor.fromImage: Cursor.fill, Cursor.outline and Cursor.outlineWidth only apply to icon cursors; they are ignored."
                )
#endif
            let spec = ImageCursorSpec(image, o)
            let cursor = { Key = ImageCursor.key image o; Render = ImageCursor.render spec }
            ImageCursors.Add(cursor, spec)
            Cursor.Image cursor

        /// The cursor's size in DIPs: an icon's (default 24), or an image's width (default its own).
        static member size(value: float) = CursorSetting(fun o -> { o with Size = value; SizeSet = true })

        /// How an image cursor is enlarged (default smooth; pixelated keeps pixels square). Shrinking is smooth.
        static member scaling(value: ImageScaling) = CursorSetting(fun o -> { o with Scaling = value })

        /// The point that points, in DIPs from the icon's top-left (default its center).
        static member hotspot(x: float, y: float) = CursorSetting(fun o -> { o with Hotspot = ValueSome(struct (x, y)) })

        /// The icon's color (default white).
        static member fill(value: Color) = CursorSetting(fun o -> { o with Fill = value; IconOnlySet = true })

        /// The outline's color (default black; transparent for none).
        static member outline(value: Color) = CursorSetting(fun o -> { o with Outline = value; IconOnlySet = true })

        /// The outline's width in DIPs (default 1).
        static member outlineWidth(value: float) = CursorSetting(fun o -> { o with OutlineWidth = value; IconOnlySet = true })
