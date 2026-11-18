namespace Scribble.Core

open System
open Scribble.Accessibility
open Scribble.Rendering

module internal ToastProps =
    /// How far below its place the toast is drawn, in DIPs: it slides up as it comes and down as it goes.
    let offset = Prop.define "Offset" 0.0 Affects.Render

/// The box a toast sits in (the Toaster makes them; `ToastPresenter.style [] [ ... ]` restyles them,
/// `ToastPresenter.style [ Style.cls "error" ] [ ... ]` the errors).
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

/// The toasts' layer: its toasts one above another at the bottom right, the newest lowest; across
/// the bottom in a narrow window. Only the toasts take the pointer.
module internal ToastPanel =
    let private edge = 16.0
    let private spacing = 8.0
    /// The most a toast is wide; narrower than this plus the edges, a window gets them full width.
    let private maxWidth = 360.0
    let private narrow = 480.0

    let private widthFor (window: float) = if window < narrow then max 0.0 (window - 2.0 * edge) else maxWidth

    let private measure (ctx: MeasureContext) (available: Size) =
        for child in ctx.Children do
            ctx.Measure(child, Size.Create(widthFor available.Width, Double.PositiveInfinity)) |> ignore
        available

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let stretch = size.Width < narrow
        let mutable bottom = size.Height - edge
        for i in ctx.Children.Count - 1 .. -1 .. 0 do
            let child = ctx.Children[i]
            let d = ctx.DesiredSize child
            let w = if stretch then widthFor size.Width else min d.Width maxWidth
            ctx.Arrange(child, Rect.Create(size.Width - edge - w, bottom - d.Height, w, d.Height))
            bottom <- bottom - d.Height - spacing

    let Type = Element.define "ToastPanel" [ Element.measure measure; Element.arrange arrange; Element.passThrough ]

