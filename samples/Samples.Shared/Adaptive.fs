/// A small layout element the samples use to fit narrow windows and phones, written the way any
/// app can write its own (Grid, DockPanel and WrapPanel are in Scribble.Core):
///
/// - `SplitRow`: a start part and an end part on one row (the end part at the far end), or, when
///   the two don't fit side by side, the end part on its own row below.
namespace Scribble.Samples

open System
open Scribble.Rendering
open Scribble.Core

module SplitRowProps =
    let gap = Prop.define "Gap" 16.0 Affects.Measure
    let endMargin = Prop.define "EndMargin" Thickness.Zero Affects.Measure
    let wrapMargin = Prop.define "WrapMargin" Thickness.Zero Affects.Measure
    /// Whether the last measure put the end part on its own row.
    let internal wrapped = Prop.local "Wrapped" false

/// Two children: the start part and the end part. When both fit side by side at their natural
/// widths (with `gap` between them), the end part sits at the far end, centred on the row, and the
/// start part gets the rest of the width. Otherwise the start part gets the whole width and the
/// end part goes below it, with the whole width to wrap in. With one child, it's just that child.
[<AbstractClass>]
type SplitRow =
    inherit Panel
    /// The least space between the two parts on one row.
    static member gap<'k when 'k :> SplitRow>(value: float) : Setting<'k> = Setting.create SplitRowProps.gap value
    /// Space around the end part while it's on the same row.
    static member endMargin<'k when 'k :> SplitRow>(left: float, top: float, right: float, bottom: float) : Setting<'k> =
        Setting.create SplitRowProps.endMargin { Left = left; Top = top; Right = right; Bottom = bottom }
    /// Space around the end part while it's on its own row.
    static member wrapMargin<'k when 'k :> SplitRow>(left: float, top: float, right: float, bottom: float) : Setting<'k> =
        Setting.create SplitRowProps.wrapMargin { Left = left; Top = top; Right = right; Bottom = bottom }

module SplitRow =
    let private horizontal (t: Thickness) = t.Left + t.Right
    let private vertical (t: Thickness) = t.Top + t.Bottom

    let Type =
        Element.define "SplitRow" [
            Element.measure (fun ctx available ->
                let infinite = Size.Create(Double.PositiveInfinity, available.Height)
                if ctx.Children.Count < 2 then
                    let mutable size = Size.Create(0.0, 0.0)
                    for c in ctx.Children do
                        size <- ctx.Measure(c, available)
                    size
                else
                    let start, finish = ctx.Children[0], ctx.Children[1]
                    let gap, endM, wrapM = ctx.Get SplitRowProps.gap, ctx.Get SplitRowProps.endMargin, ctx.Get SplitRowProps.wrapMargin
                    let startNatural = ctx.Measure(start, infinite)
                    let endNatural = ctx.Measure(finish, Size.Create(Double.PositiveInfinity, Double.PositiveInfinity))
                    let endWidth = endNatural.Width + horizontal endM
                    let fits = startNatural.Width + gap + endWidth <= available.Width
                    ctx.SetLocal(SplitRowProps.wrapped, not fits)
                    if fits then
                        let room = if Double.IsInfinity available.Width then startNatural.Width else available.Width - endWidth - gap
                        let s = ctx.Measure(start, Size.Create(room, available.Height))
                        let width = if Double.IsInfinity available.Width then s.Width + gap + endWidth else available.Width
                        Size.Create(width, max s.Height (endNatural.Height + vertical endM))
                    else
                        let s = ctx.Measure(start, Size.Create(available.Width, available.Height))
                        let e = ctx.Measure(finish, Size.Create(available.Width - horizontal wrapM, Double.PositiveInfinity))
                        Size.Create(available.Width, s.Height + e.Height + vertical wrapM))
            Element.arrange (fun ctx size ->
                if ctx.Children.Count < 2 then
                    for c in ctx.Children do
                        ctx.Arrange(c, Rect.Create(0.0, 0.0, size.Width, size.Height))
                else
                    let start, finish = ctx.Children[0], ctx.Children[1]
                    let s, e = ctx.DesiredSize start, ctx.DesiredSize finish
                    if ctx.Get SplitRowProps.wrapped then
                        let m = ctx.Get SplitRowProps.wrapMargin
                        ctx.Arrange(start, Rect.Create(0.0, 0.0, size.Width, s.Height))
                        ctx.Arrange(finish, Rect.Create(m.Left, s.Height + m.Top, size.Width - horizontal m, e.Height))
                    else
                        let m, gap = ctx.Get SplitRowProps.endMargin, ctx.Get SplitRowProps.gap
                        let x = size.Width - m.Right - e.Width
                        ctx.Arrange(start, Rect.Create(0.0, 0.0, max 0.0 (x - m.Left - gap), size.Height))
                        ctx.Arrange(finish, Rect.Create(x, m.Top + (size.Height - vertical m - e.Height) / 2.0, e.Width, e.Height)))
        ]

    let create (settings: Setting<SplitRow> list) = View.Create(Type, settings)
