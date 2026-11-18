namespace Scribble.Core

open System
open Scribble.Rendering

/// Where a flyout (or a tooltip) goes: the rect it's anchored to, in window coordinates, and how it's placed beside it
/// (DESIGN.md, "M8 step 4", round 1, 7). The host gives a new one when the anchor moves.
[<Sealed>]
type internal FlyoutTarget(rect: Rect, placement: Placement, alignment: FlyoutAlignment, matchWidth: bool, gap: float) =
    member _.Rect = rect
    member _.Placement = placement
    member _.Alignment = alignment
    member _.MatchWidth = matchWidth
    member _.Gap = gap

module internal FlyoutRootProps =
    let target = Prop<FlyoutTarget voption>("Target", ValueNone, Affects.Arrange, PropKind.Value)

/// Where a flyout of size `w` × `h` goes beside `anchor` in a `width` × `height` window: on the side asked for, or the
/// opposite one when only that fits (or has more room), lined up as asked, then pushed inside the window.
module internal FlyoutPlacement =
    let place (anchor: Rect) (side: Placement) (alignment: FlyoutAlignment) (gap: float) (w: float) (h: float) (width: float) (height: float) : Rect =
        let roomBelow = height - (anchor.Y + anchor.Height) - gap
        let roomAbove = anchor.Y - gap
        let roomRight = width - (anchor.X + anchor.Width) - gap
        let roomLeft = anchor.X - gap
        let side =
            match side with
            | Placement.Below when roomBelow < h && roomAbove > roomBelow -> Placement.Above
            | Placement.Above when roomAbove < h && roomBelow > roomAbove -> Placement.Below
            | Placement.Right when roomRight < w && roomLeft > roomRight -> Placement.Left
            | Placement.Left when roomLeft < w && roomRight > roomLeft -> Placement.Right
            | s -> s
        let along (start: float) (length: float) (size: float) =
            match alignment with
            | FlyoutAlignment.Start -> start
            | FlyoutAlignment.Center -> start + (length - size) / 2.0
            | FlyoutAlignment.End -> start + length - size
        let x, y =
            match side with
            | Placement.Below -> along anchor.X anchor.Width w, anchor.Y + anchor.Height + gap
            | Placement.Above -> along anchor.X anchor.Width w, anchor.Y - gap - h
            | Placement.Right -> anchor.X + anchor.Width + gap, along anchor.Y anchor.Height h
            | Placement.Left -> anchor.X - gap - w, along anchor.Y anchor.Height h
        let clamp (v: float) (size: float) (limit: float) = Math.Max(0.0, Math.Min(v, limit - size))
        Rect.Create(Math.Round(clamp x w width), Math.Round(clamp y h height), w, h)

/// The root of a flyout's (or a tooltip's) layer: fills the window but is never hit itself, and places its one child
/// beside its target, at most the window's size.
module internal FlyoutRoot =
    let private arrange (ctx: ArrangeContext) (size: Size) =
        match ctx.Get FlyoutRootProps.target with
        | ValueSome target ->
            for child in ctx.Children do
                let desired = child.DesiredSize
                let anchor = target.Rect
                let w0 = if target.MatchWidth then max desired.Width anchor.Width else desired.Width
                let w = min w0 size.Width
                let h = min desired.Height size.Height
                ctx.Arrange(child, FlyoutPlacement.place anchor target.Placement target.Alignment target.Gap w h size.Width size.Height)
        | ValueNone -> ()

    let Type =
        Element.define "FlyoutRoot" [
            Element.measure (fun ctx available ->
                for child in ctx.Children do
                    ctx.Measure(child, available) |> ignore
                Size.Create((if Double.IsFinite available.Width then available.Width else 0.0), (if Double.IsFinite available.Height then available.Height else 0.0)))
            Element.arrange arrange
            Element.passThrough
        ]

    let create (target: FlyoutTarget) (child: View) =
        View.Create(Type, [ Setting.create FlyoutRootProps.target (ValueSome target); Setting.create PanelProps.children [ child ] ])

/// What a flyout's content sits in: the theme gives it the surface colour, a border, rounded corners, padding and a
/// floating shadow (`FlyoutPresenter.style` restyles it).
[<AbstractClass>]
type FlyoutPresenter =
    inherit Border

module FlyoutPresenter =
    let Type = Element.extend Border.Type "FlyoutPresenter" []
    let create (settings: Setting<FlyoutPresenter> list) = View.Create(Type, settings)
    let style parts (settings: Setting<FlyoutPresenter> list) = Style.forType Type parts settings

/// What a tooltip's text or view sits in (`TooltipPresenter.style` restyles it).
[<AbstractClass>]
type TooltipPresenter =
    inherit Border

module TooltipPresenter =
    let Type = Element.extend Border.Type "TooltipPresenter" [ Element.defaults [ Layoutable.isHitTestVisible false ] ]
    let create (settings: Setting<TooltipPresenter> list) = View.Create(Type, settings)
    let style parts (settings: Setting<TooltipPresenter> list) = Style.forType Type parts settings
