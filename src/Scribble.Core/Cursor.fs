namespace Scribble.Core

open System
open System.Globalization
open Scribble.Rendering
open Scribble.Text
open Scribble.Platform

/// How an icon becomes a cursor (the settings of `Cursor.fromIcon`).
type internal IconCursorOptions =
    { Size: float
      Hotspot: struct (float * float) voption
      Fill: Color
      Outline: Color
      OutlineWidth: float }

type CursorSetting = internal CursorSetting of (IconCursorOptions -> IconCursorOptions)

module internal IconCursor =
    let defaults =
        { Size = 24.0; Hotspot = ValueNone; Fill = Color.White; Outline = Color.Black; OutlineWidth = 1.0 }

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

        /// The cursor's size in DIPs (default 24).
        static member size(value: float) = CursorSetting(fun o -> { o with Size = value })

        /// The point that points, in DIPs from the icon's top-left (default its center).
        static member hotspot(x: float, y: float) = CursorSetting(fun o -> { o with Hotspot = ValueSome(struct (x, y)) })

        /// The icon's color (default white).
        static member fill(value: Color) = CursorSetting(fun o -> { o with Fill = value })

        /// The outline's color (default black; transparent for none).
        static member outline(value: Color) = CursorSetting(fun o -> { o with Outline = value })

        /// The outline's width in DIPs (default 1).
        static member outlineWidth(value: float) = CursorSetting(fun o -> { o with OutlineWidth = value })
