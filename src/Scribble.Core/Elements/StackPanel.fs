namespace Scribble.Core

open System
open Scribble.Rendering

module StackProps =
    let orientation = Prop.define "Orientation" Vertical Affects.Measure
    /// Gap between children in DIPs (hidden children don't get one).
    let spacing = Prop.define "Spacing" 0.0 Affects.Measure

/// Lays children out one after another, vertically (default) or horizontally, with optional spacing.
/// Children are as long as their content along the stacking direction, unless they ask for a Star
/// share (`height (Star 1.0)` or `height "*"` in a vertical panel): then they split the leftover
/// space by weight. That only happens when the panel's own length is known (from the window, a
/// stretching parent, or an explicit size); in unlimited space Star children take their content size.
[<AbstractClass>]
type StackPanel =
    inherit Panel
    static member orientation<'k when 'k :> StackPanel>(value: Orientation) : Setting<'k> = Setting.create StackProps.orientation value
    static member spacing<'k when 'k :> StackPanel>(value: float) : Setting<'k> = Setting.create StackProps.spacing value

module StackPanel =
    /// Whether the last measure had a limited size along the stacking direction (so Star
    /// children have leftover space to share).
    let private bounded = Prop.local "StackBounded" false

    /// A visible child's star weight along the stacking direction, or 0.
    let private starWeight (orientation: Orientation) (child: Element) =
        if not child.IsVisible then 0.0
        else
            match (match orientation with Vertical -> child.RequestedHeight | Horizontal -> child.RequestedWidth) with
            | Star w -> w
            | _ -> 0.0

    let private along orientation (s: Size) = match orientation with Vertical -> s.Height | Horizontal -> s.Width
    let private across orientation (s: Size) = match orientation with Vertical -> s.Width | Horizontal -> s.Height
    let private make orientation (alongValue: float) (acrossValue: float) =
        match orientation with
        | Vertical -> Size.Create(acrossValue, alongValue)
        | Horizontal -> Size.Create(alongValue, acrossValue)

    let private measure (ctx: MeasureContext) (available: Size) =
        let orientation, spacing = ctx.Get StackProps.orientation, ctx.Get StackProps.spacing
        let limit = along orientation available
        let isBounded = not (Double.IsInfinity limit)
        ctx.SetLocal(bounded, isBounded)
        let crossAvailable = across orientation available
        let mutable used, widest, visible, totalStars = 0.0, 0.0, 0, 0.0
        // Children without a star share (and every child when unbounded) get unlimited space.
        for child in ctx.Children do
            let star = if isBounded then starWeight orientation child else 0.0
            if star > 0.0 then
                totalStars <- totalStars + star
            else
                let d = ctx.Measure(child, make orientation Double.PositiveInfinity crossAvailable)
                if child.IsVisible then
                    used <- used + along orientation d
                    widest <- max widest (across orientation d)
            if child.IsVisible then visible <- visible + 1
        let gaps = spacing * float (max 0 (visible - 1))
        // Star children are measured in their share of what's left.
        if totalStars > 0.0 then
            let leftover = max 0.0 (limit - used - gaps)
            for child in ctx.Children do
                let star = starWeight orientation child
                if star > 0.0 then
                    let d = ctx.Measure(child, make orientation (leftover * star / totalStars) crossAvailable)
                    used <- used + along orientation d
                    widest <- max widest (across orientation d)
        make orientation (used + gaps) widest

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let orientation, spacing = ctx.Get StackProps.orientation, ctx.Get StackProps.spacing
        let isBounded = ctx.Get bounded
        let mutable fixedTotal, totalStars, visible = 0.0, 0.0, 0
        for child in ctx.Children do
            if child.IsVisible then
                visible <- visible + 1
                let star = if isBounded then starWeight orientation child else 0.0
                if star > 0.0 then totalStars <- totalStars + star
                else fixedTotal <- fixedTotal + along orientation (ctx.DesiredSize child)
        let gaps = spacing * float (max 0 (visible - 1))
        let leftover = max 0.0 (along orientation size - fixedTotal - gaps)
        let crossSize = across orientation size
        let mutable offset = 0.0
        for child in ctx.Children do
            if child.IsVisible then
                let star = if isBounded then starWeight orientation child else 0.0
                let length = if star > 0.0 then leftover * star / totalStars else along orientation (ctx.DesiredSize child)
                let slot =
                    match orientation with
                    | Vertical -> Rect.Create(0.0, offset, crossSize, length)
                    | Horizontal -> Rect.Create(offset, 0.0, length, crossSize)
                ctx.Arrange(child, slot)
                offset <- offset + length + spacing
            else
                ctx.Arrange(child, Rect.Create(0.0, 0.0, 0.0, 0.0))

    let Type = Element.define "StackPanel" [ Element.measure measure; Element.arrange arrange ]

    let create (settings: Setting<StackPanel> list) : View = View.Create(Type, settings)
