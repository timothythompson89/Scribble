namespace Scribble.Core

open System
open Scribble.Accessibility
open Scribble.Rendering

module internal ToastProps =
    /// How far below its place the toast is drawn, in DIPs (above, when negative): it slides up as it comes, and on up
    /// as it goes.
    let offset = Prop.define "Offset" 0.0 Affects.Render

/// The box a toast sits in (the Toaster makes them; `ToastPresenter.style [] [ ... ]` restyles them,
/// `Toast.style [ Style.cls "critical" ] [ ... ]` the critical ones).
[<AbstractClass>]
type ToastPresenter =
    inherit Border

module ToastPresenter =
    /// How long a toast takes to slide and fade, in or out.
    let internal moveTime = TimeSpan.FromMilliseconds 150.0

    let Type =
        Element.extend Border.Type "ToastPresenter" [
            Element.transform (fun ctx -> Transform.Translate(0.0, ctx.Animate("slide", ctx.Get ToastProps.offset, moveTime)))
            Element.defaults [
                Semantics.role Role.Alert
                // The dark base look (also without a theme).
                Layoutable.foreground (Color.Hex 0xE6E6F0)
                Border.background (Color.Hex 0x24243A)
                Border.cornerRadius 6.0
                Border.padding (10.0, 8.0)
            ]
        ]
    let internal create (settings: Setting<ToastPresenter> list) = View.Create(Type, settings)
    let style parts (settings: Setting<ToastPresenter> list) = Style.forType Type parts settings

/// Where a window's toasts stack: `Toasts.placement` (an app value), set with `App.toasts [ ... ]`.
type ToastPlacement =
    { /// Left, Center, Right (default), or Stretch for the window's width.
      Horizontal: HorizontalAlignment
      /// Bottom (default) or Top.
      Vertical: VerticalAlignment }

    /// Bottom right.
    static member Default = { Horizontal = HorizontalAlignment.Right; Vertical = VerticalAlignment.Bottom }

module internal ToastPanelProps =
    let horizontal = Prop.define "ToastsHorizontal" HorizontalAlignment.Right Affects.Measure
    /// True: at the top of the window, read downwards; false: at the bottom, read upwards.
    let atTop = Prop.define "ToastsAtTop" false Affects.Measure

/// The toasts' layer: its toasts one above another at an edge of the window (the bottom right unless
/// `Toasts.placement` says otherwise); across the window when it's narrow. Its children are in the
/// order they stand in, top to bottom. Only the toasts take the pointer.
module internal ToastPanel =
    let private edge = 16.0
    let private spacing = 8.0
    /// The most a toast is wide; narrower than this plus the edges, a window gets them full width.
    let private maxWidth = 360.0
    let private narrow = 480.0

    /// Full width: the window is narrow, or the placement says Stretch.
    let private stretches (horizontal: HorizontalAlignment) (window: float) = window < narrow || horizontal = HorizontalAlignment.Stretch

    let private widthFor (horizontal: HorizontalAlignment) (window: float) =
        if stretches horizontal window then max 0.0 (window - 2.0 * edge) else maxWidth

    let private measure (ctx: MeasureContext) (available: Size) =
        let width = widthFor (ctx.Get ToastPanelProps.horizontal) available.Width
        for child in ctx.Children do
            ctx.Measure(child, Size.Create(width, Double.PositiveInfinity)) |> ignore
        available

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let horizontal = ctx.Get ToastPanelProps.horizontal
        let stretch = stretches horizontal size.Width
        let place (child: Element) (y: float) =
            let d = ctx.DesiredSize child
            let w = if stretch then widthFor horizontal size.Width else min d.Width maxWidth
            let x =
                match horizontal with
                | _ when stretch -> edge
                | HorizontalAlignment.Left -> edge
                | HorizontalAlignment.Center -> Math.Floor((size.Width - w) / 2.0)
                | _ -> size.Width - edge - w
            ctx.Arrange(child, Rect.Create(x, y, w, d.Height))
        if ctx.Get ToastPanelProps.atTop then
            let mutable top = edge
            for child in ctx.Children do
                place child top
                top <- top + (ctx.DesiredSize child).Height + spacing
        else
            let mutable bottom = size.Height - edge
            for i in ctx.Children.Count - 1 .. -1 .. 0 do
                let child = ctx.Children[i]
                let h = (ctx.DesiredSize child).Height
                place child (bottom - h)
                bottom <- bottom - h - spacing

    let Type = Element.define "ToastPanel" [ Element.measure measure; Element.arrange arrange; Element.passThrough ]
