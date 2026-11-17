namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

module internal HighlightProps =
    let target = Prop.define "Target" (ValueNone: Element voption) Affects.Render
    let outlineOnly = Prop.define "OutlineOnly" false Affects.Render

/// Draws the devtools-style highlight over an element (bounds, margin, a label with the size), or
/// just a thin outline around it (the selection, while nothing is pointed at).
/// Reads the target's position while drawing, so it's exact in every frame.
[<AbstractClass>]
type internal Highlight =
    inherit Layoutable
    static member target<'k when 'k :> Highlight>(value: Element voption) : Setting<'k> = Setting.create HighlightProps.target value
    static member outlineOnly<'k when 'k :> Highlight>(value: bool) : Setting<'k> = Setting.create HighlightProps.outlineOnly value

module internal Highlight =
    let private boxColor = Color.Hex 0x6FA8DC
    let private marginColor = (Color.Hex 0xF6B26B).WithAlpha 0.35f
    let private gridColor = Color.Hex 0xC792EA

    /// A Grid's column and row lines (dashed) and its gaps (shaded), like a browser's grid overlay.
    let private gridLines (list: DrawList) (t: Element) (r: Rect) =
        match Grid.edges t with
        | ValueSome(struct (colStarts, colEnds, rowStarts, rowEnds)) ->
            let dash = StrokeStyle.Dashed(4.0, 3.0)
            let vertical (x: float) = list.StrokeRect(Rect.Create(r.X + x, r.Y, 1.0, r.Height), CornerRadius.Zero, 1.0, gridColor, style = dash)
            let horizontal (y: float) = list.StrokeRect(Rect.Create(r.X, r.Y + y, r.Width, 1.0), CornerRadius.Zero, 1.0, gridColor, style = dash)
            for k in 0 .. colStarts.Length - 1 do
                if k > 0 && colStarts[k] > colEnds[k - 1] then
                    list.FillRect(Rect.Create(r.X + colEnds[k - 1], r.Y, colStarts[k] - colEnds[k - 1], r.Height), gridColor.WithAlpha 0.18f)
                if colStarts[k] > 0.0 then vertical colStarts[k]
                if colEnds[k] < r.Width && (k = colStarts.Length - 1 || colStarts[k + 1] > colEnds[k]) then vertical (colEnds[k] - 1.0)
            for k in 0 .. rowStarts.Length - 1 do
                if k > 0 && rowStarts[k] > rowEnds[k - 1] then
                    list.FillRect(Rect.Create(r.X, r.Y + rowEnds[k - 1], r.Width, rowStarts[k] - rowEnds[k - 1]), gridColor.WithAlpha 0.18f)
                if rowStarts[k] > 0.0 then horizontal rowStarts[k]
                if rowEnds[k] < r.Height && (k = rowStarts.Length - 1 || rowStarts[k + 1] > rowEnds[k]) then horizontal (rowEnds[k] - 1.0)
        | ValueNone -> ()

    /// Which of an element's edges a clip (a ScrollViewer's viewport) cuts off, given its full rect
    /// and its visible part.
    [<Struct>]
    type internal Cuts =
        { Left: bool; Top: bool; Right: bool; Bottom: bool }
        static member Of(full: Rect, visible: Rect) =
            { Left = visible.X > full.X + 0.5; Top = visible.Y > full.Y + 0.5
              Right = visible.Right < full.Right - 0.5; Bottom = visible.Bottom < full.Bottom - 0.5 }

    /// A dashed line along a cut edge of `v`, meaning "the element goes on past here": blue dashes
    /// on a dark edge, like the corner marks, so it stands out on light, dark and blue alike.
    let private cutEdges (list: DrawList) (window: Size) (v: Rect) (cuts: Cuts) =
        let t, dash, gap = 2.0, 8.0, 5.0
        let dark = Color.Hex 0x11111B
        let dashes (horizontal: bool) (x: float) (y: float) (length: float) =
            let mutable at = 0.0
            while at < length do
                let n = min dash (length - at)
                let r = if horizontal then Rect.Create(x + at, y, n, t) else Rect.Create(x, y + at, t, n)
                list.FillRect(Rect.Create(r.X - 1.0, r.Y - 1.0, r.Width + 2.0, r.Height + 2.0), dark)
                list.FillRect(r, boxColor)
                at <- at + dash + gap
        // Just outside the cut, as far out as the corner marks (3 DIPs), so they line up with them
        // and don't sit on the element's own content (text in a list). Just inside when outside
        // would be off the window (a list at the window's edge).
        let out = 3.0
        let before (edge: float) = if edge - out - t - 1.0 >= 0.0 then edge - out - t else edge + 1.0
        let after (edge: float) (limit: float) = if edge + out + t + 1.0 <= limit then edge + out else edge - t - 1.0
        if cuts.Top then dashes true v.X (before v.Y) v.Width
        if cuts.Bottom then dashes true v.X (after v.Bottom window.Height) v.Width
        if cuts.Left then dashes false (before v.X) v.Y v.Height
        if cuts.Right then dashes false (after v.Right window.Width) v.Y v.Height

    let private render (ctx: RenderContext) (list: DrawList) =
        match ctx.Get HighlightProps.target with
        | ValueSome t when t.Root.IsSome && t.Bounds.Width >= 0.0 && t.Bounds.Height >= 0.0 ->
            let p = t.PositionInTree
            let b = t.Bounds
            let r = Rect.Create(p.X, p.Y, b.Width, b.Height)
            // Only the part the ancestors' clips let show (a ScrollViewer's viewport); nothing when
            // it's scrolled out entirely.
            let v = t.VisibleBounds
            if v.IsEmpty then () else
            let cuts = Cuts.Of(r, v)
            list.PushClip v
            gridLines list t r
            list.PopClip()
            if ctx.Get HighlightProps.outlineOnly then
                // Corner marks 3 DIPs outside the element, like a viewfinder: the sides stay clear, so
                // its edges, border and shadow show, and the corners still show its exact extent.
                // Blue on a dark edge, so they stand out on light, dark and blue alike. Only at
                // corners that show; a cut edge gets a dashed line instead.
                let t, gap = 2.0, 3.0
                let o = Rect.Create(r.X - gap - t, r.Y - gap - t, r.Width + 2.0 * (gap + t), r.Height + 2.0 * (gap + t))
                // Arms up to 8 long, shorter on small elements so the marks never meet.
                let armX, armY = min 8.0 (o.Width / 2.0 - 1.0), min 8.0 (o.Height / 2.0 - 1.0)
                let marks (grow: float) (color: Color) =
                    let bar (x: float) (y: float) (w: float) (h: float) =
                        list.FillRect(Rect.Create(x - grow, y - grow, w + 2.0 * grow, h + 2.0 * grow), color)
                    let right, bottom = o.X + o.Width, o.Y + o.Height
                    if not (cuts.Top || cuts.Left) then (bar o.X o.Y armX t; bar o.X o.Y t armY)
                    if not (cuts.Top || cuts.Right) then (bar (right - armX) o.Y armX t; bar (right - t) o.Y t armY)
                    if not (cuts.Bottom || cuts.Left) then (bar o.X (bottom - t) armX t; bar o.X (bottom - armY) t armY)
                    if not (cuts.Bottom || cuts.Right) then (bar (right - armX) (bottom - t) armX t; bar (right - t) (bottom - armY) t armY)
                marks 1.0 (Color.Hex 0x11111B)
                marks 0.0 boxColor
                cutEdges list ctx.Size v cuts
            else
            let m = t.Get LayoutProps.margin
            // The margin bands, cut by the same clips (they're outside the element, but inside its parent).
            list.PushClip(t.VisiblePart(Rect.Create(-m.Left, -m.Top, b.Width + m.Left + m.Right, b.Height + m.Top + m.Bottom)))
            let outerWidth = r.Width + m.Left + m.Right
            if m.Top > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y - m.Top, outerWidth, m.Top), marginColor)
            if m.Bottom > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y + r.Height, outerWidth, m.Bottom), marginColor)
            if m.Left > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y, m.Left, r.Height), marginColor)
            if m.Right > 0.0 then list.FillRect(Rect.Create(r.X + r.Width, r.Y, m.Right, r.Height), marginColor)
            list.PopClip()
            list.PushClip v
            list.FillRect(r, boxColor.WithAlpha 0.3f)
            list.StrokeRect(r, CornerRadius.Zero, 1.0, boxColor)
            list.PopClip()
            cutEdges list ctx.Size v cuts
            // The label (the full size): above the element and its margin, else below, else just
            // inside the top of what shows (a cut top edge, or no room either way).
            let text = Format.label t + "  " + Format.size b.Width b.Height
            let layout = ctx.Text.Layout(text, ctx.Get TextProps.font, 12.0, Double.PositiveInfinity, NoWrap, NoTrimming, 0)
            let w, h = layout.Width + 12.0, layout.Height + 6.0
            let above = r.Y - m.Top - h - 4.0
            let below = r.Y + r.Height + m.Bottom + 4.0
            let y =
                if not cuts.Top && above >= 0.0 then above
                elif not cuts.Bottom && below + h <= ctx.Size.Height then below
                else min (v.Y + 4.0) (ctx.Size.Height - h)
            let left = if cuts.Left then v.X else r.X - m.Left
            let x = max 0.0 (min left (ctx.Size.Width - w))
            list.FillRoundedRect(Rect.Create(x, y, w, h), CornerRadius.Uniform 4.0, Color.Hex 0x11111B)
            ctx.Text.DrawLayout(list, layout, x + 6.0, y + 3.0, layout.Width, TextAlignment.Start, Color.Hex 0xE6E6F0)
        | _ -> ()

    let Type = Element.define "InspectorHighlight" [ Element.render render ]
    let create (settings: Setting<Highlight> list) = View.Create(Type, settings)
