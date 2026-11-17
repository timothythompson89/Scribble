namespace Scribble.Core

open System
open Scribble.Rendering

module CanvasProps =
    /// Attached: distance from the Canvas's left edge to the child's (DIPs).
    let left: Prop<float voption> = Prop.attached "Canvas" "Left" ValueNone Affects.Measure
    let top: Prop<float voption> = Prop.attached "Canvas" "Top" ValueNone Affects.Measure
    /// Attached: distance from the child's right edge to the Canvas's (used when `left` isn't set).
    let right: Prop<float voption> = Prop.attached "Canvas" "Right" ValueNone Affects.Measure
    let bottom: Prop<float voption> = Prop.attached "Canvas" "Bottom" ValueNone Affects.Measure

/// Places children at positions: `Border.create [ Canvas.left 40.0; Canvas.top 20.0 ]` (or from
/// the right and bottom edges), each at its own size. Its own size is the extent of its children
/// (unlike WPF's 0), so a Canvas inside a ScrollViewer scrolls. Overlaps are ordered by `zIndex`.
[<AbstractClass>]
type Canvas =
    inherit Panel
    /// Attached, on a child: its left edge (DIPs from the Canvas's left).
    static member left<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create CanvasProps.left (ValueSome value)
    /// Attached, on a child: its top edge (DIPs from the Canvas's top).
    static member top<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create CanvasProps.top (ValueSome value)
    /// Attached, on a child: its right edge (DIPs from the Canvas's right), when `left` isn't set.
    static member right<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create CanvasProps.right (ValueSome value)
    /// Attached, on a child: its bottom edge (DIPs from the Canvas's bottom), when `top` isn't set.
    static member bottom<'k when 'k :> Layoutable>(value: float) : Setting<'k> = Setting.create CanvasProps.bottom (ValueSome value)

module Canvas =
    let private measure (ctx: MeasureContext) (_: Size) =
        let mutable w, h = 0.0, 0.0
        for child in ctx.Children do
            let d = ctx.Measure(child, Size.Create(Double.PositiveInfinity, Double.PositiveInfinity))
            if child.IsVisible then
                let x = match child.Get CanvasProps.left, child.Get CanvasProps.right with
                        | ValueSome l, _ -> l + d.Width
                        | ValueNone, ValueSome r -> d.Width + r
                        | ValueNone, ValueNone -> d.Width
                let y = match child.Get CanvasProps.top, child.Get CanvasProps.bottom with
                        | ValueSome t, _ -> t + d.Height
                        | ValueNone, ValueSome b -> d.Height + b
                        | ValueNone, ValueNone -> d.Height
                w <- max w x
                h <- max h y
        Size.Create(w, h)

    let private arrange (ctx: ArrangeContext) (size: Size) =
        for child in ctx.Children do
            let d = ctx.DesiredSize child
            let x = match child.Get CanvasProps.left, child.Get CanvasProps.right with
                    | ValueSome l, _ -> l
                    | ValueNone, ValueSome r -> size.Width - r - d.Width
                    | ValueNone, ValueNone -> 0.0
            let y = match child.Get CanvasProps.top, child.Get CanvasProps.bottom with
                    | ValueSome t, _ -> t
                    | ValueNone, ValueSome b -> size.Height - b - d.Height
                    | ValueNone, ValueNone -> 0.0
            ctx.Arrange(child, Rect.Create(x, y, d.Width, d.Height))

    let Type = Element.define "Canvas" [ Element.measure measure; Element.arrange arrange ]

    let create (settings: Setting<Canvas> list) : View = View.Create(Type, settings)
    /// A style for Canvases (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Canvas> list) : Style = Style.forType Type parts settings
