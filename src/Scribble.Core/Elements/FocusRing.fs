namespace Scribble.Core

open Scribble.Rendering

/// The keyboard focus ring, drawn by elements that can take focus:
///
///     Element.render (fun ctx list ->
///         ...
///         if ctx.IsFocusVisible then FocusRing.draw ctx list (CornerRadius.Uniform 6.0))
///
/// A 2 DIP ring, 2 DIPs outside the element, in the `focusRingColor` (inherited; the theme sets it).
module FocusRing =
    /// The default ring color.
    let color = Color.Hex 0x7AA2F7

    /// The ring's color (inherited: the theme gives its base value).
    let colorProp =
        let p = Prop.define "FocusRingColor" color Affects.Render
        p.Inherits <- true
        p

    let thickness = 2.0
    /// The gap between the element's edge and the ring.
    let offset = 2.0

    /// Draws the ring around the element, following its corner radius (call it from a render
    /// function when `ctx.IsFocusVisible`).
    let draw (ctx: RenderContext) (list: DrawList) (radius: CornerRadius) =
        let grow = offset + thickness
        let size = ctx.Size
        let outer (r: float) = if r > 0.0 then r + grow else 0.0
        let radius =
            { TopLeft = outer radius.TopLeft; TopRight = outer radius.TopRight
              BottomRight = outer radius.BottomRight; BottomLeft = outer radius.BottomLeft }
        list.StrokeRect(Rect.Create(-grow, -grow, size.Width + 2.0 * grow, size.Height + 2.0 * grow), radius, thickness, ctx.Get colorProp)
