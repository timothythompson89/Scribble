namespace Scribble.Core

open System
open Scribble.Rendering

module ScrollViewerProps =
    let content = Prop.child "Content"
    /// The scrollbar thumb's color (the theme sets it).
    let thumb = Prop.define "Thumb" (Color.White.WithAlpha 0.3f) Affects.Render
    /// How far the content is scrolled, in DIPs (kept by the element; the wheel changes it).
    let internal offset = Prop.local "ScrollOffset" 0.0
    /// The content's height and the visible height after the last arrange.
    let internal extent = Prop.local "ScrollExtent" 0.0
    let internal viewport = Prop.local "ScrollViewport" 0.0

/// Shows one child, scrolled vertically with the mouse wheel or a finger drag when it's taller than
/// the space the ScrollViewer gets. Give it a bounded height (a fixed height, a Star share, or a place that
/// stretches it, like a layer or a window): with unlimited height it simply grows to fit.
///
/// Basic for now (vertical, wheel, touch drag without momentum, a thin scrollbar that shows the
/// position); horizontal scrolling, momentum, dragging the scrollbar and keyboard come with the
/// other controls.
[<AbstractClass>]
type ScrollViewer =
    inherit Layoutable
    static member content<'k when 'k :> ScrollViewer>(value: View) : Setting<'k> = Setting.create ScrollViewerProps.content (ValueSome value)
    /// The scrollbar thumb's color.
    static member thumb<'k when 'k :> ScrollViewer>(value: Color) : Setting<'k> = Setting.create ScrollViewerProps.thumb value
    static member thumb<'k when 'k :> ScrollViewer>(token: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme ScrollViewerProps.thumb token

module ScrollViewer =
    /// DIPs scrolled per wheel notch (about three lines of text).
    let private step = 48.0
    let private barWidth = 4.0
    let private barInset = 2.0
    let private minThumb = 20.0

    let private maxOffset (extent: float) (viewport: float) = max 0.0 (extent - viewport)

    let Type =
        Element.define "ScrollViewer" [
            Element.measure (fun ctx available ->
                let mutable w, h = 0.0, 0.0
                for child in ctx.Children do
                    // As tall as it likes: the part that doesn't fit is scrolled to.
                    let d = ctx.Measure(child, Size.Create(available.Width, Double.PositiveInfinity))
                    w <- max w d.Width
                    h <- max h d.Height
                Size.Create(w, min h available.Height))
            Element.arrange (fun ctx size ->
                let mutable extent = 0.0
                for child in ctx.Children do
                    extent <- max extent (ctx.DesiredSize child).Height
                // A resize or smaller content can leave the offset past the end.
                let offset = min (ctx.Get ScrollViewerProps.offset) (maxOffset extent size.Height)
                ctx.SetLocal(ScrollViewerProps.offset, offset)
                ctx.SetLocal(ScrollViewerProps.extent, extent)
                ctx.SetLocal(ScrollViewerProps.viewport, size.Height)
                for child in ctx.Children do
                    ctx.Arrange(child, Rect.Create(0.0, -offset, size.Width, max extent size.Height)))
            Element.renderAfter (fun ctx list ->
                let extent, viewport = ctx.Get ScrollViewerProps.extent, ctx.Get ScrollViewerProps.viewport
                if extent > viewport && viewport > 0.0 then
                    let track = viewport - 2.0 * barInset
                    let thumb = max minThumb (track * viewport / extent)
                    let travel = ctx.Get ScrollViewerProps.offset / maxOffset extent viewport
                    let y = barInset + (track - thumb) * travel
                    let x = ctx.Size.Width - barInset - barWidth
                    list.FillRoundedRect(Rect.Create(x, y, barWidth, thumb), CornerRadius.Uniform(barWidth / 2.0), ctx.Get ScrollViewerProps.thumb))
            Element.wheel (fun ctx e ->
                let extent, viewport = ctx.Get ScrollViewerProps.extent, ctx.Get ScrollViewerProps.viewport
                let offset = ctx.Get ScrollViewerProps.offset
                // Positive DeltaY is away from the user: towards the top.
                let next = Math.Clamp(offset - e.DeltaY * step, 0.0, maxOffset extent viewport)
                // At the end already: leave the wheel to an outer scroller.
                if next <> offset then
                    ctx.SetLocal(ScrollViewerProps.offset, next)
                    ctx.InvalidateArrange()
                    e.Handle())
            Element.pan (fun ctx dx dy ->
                let extent, viewport = ctx.Get ScrollViewerProps.extent, ctx.Get ScrollViewerProps.viewport
                let offset = ctx.Get ScrollViewerProps.offset
                // The content follows the finger: a drag down shows what's above.
                let next = Math.Clamp(offset - dy, 0.0, maxOffset extent viewport)
                if next <> offset then
                    ctx.SetLocal(ScrollViewerProps.offset, next)
                    ctx.InvalidateArrange()
                // Takes a mostly vertical drag whenever there's something to scroll, even at an end
                // (as phones do); a sideways drag, or content that fits, stays a press.
                extent > viewport && abs dy >= abs dx)
            Element.defaults [ Layoutable.clipToBounds true ]
        ]

    let create (settings: Setting<ScrollViewer> list) = View.Create(Type, settings)
    /// A style for ScrollViewers: `ScrollViewer.style [ Style.cls "c"; States.hover ] [ ScrollViewer.margin 4.0 ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<ScrollViewer> list) : Style = Style.forType Type parts settings
