namespace Scribble.Core

open System
open Scribble.Rendering

/// Which side of a DockPanel a child sticks to.
type Dock =
    | Left = 0
    | Top = 1
    | Right = 2
    | Bottom = 3

module DockProps =
    /// Attached: the side a child docks to (default Left).
    let dock = Prop.attached "DockPanel" "Dock" Dock.Left Affects.Measure
    let lastChildFill = Prop.define "LastChildFill" true Affects.Measure
    /// The gap after each docked child (DIPs).
    let spacing = Prop.define "Spacing" 0.0 Affects.Measure

/// Docks children to its sides in order, each taking a strip of what's left: `DockPanel.dock
/// Dock.Top` on a toolbar, `Dock.Bottom` on a status bar, `Dock.Left` on a sidebar; the last child
/// fills the rest (unless `lastChildFill false`). The usual app shell.
[<AbstractClass>]
type DockPanel =
    inherit Panel
    /// Whether the last child fills what's left (default true) instead of docking too.
    static member lastChildFill<'k when 'k :> DockPanel>(value: bool) : Setting<'k> = Setting.create DockProps.lastChildFill value
    /// The gap between docked children (and before the filling one), in DIPs.
    static member spacing<'k when 'k :> DockPanel>(value: float) : Setting<'k> = Setting.create DockProps.spacing value
    /// Attached, on a child: the side it docks to (default Left).
    static member dock<'k when 'k :> Layoutable>(value: Dock) : Setting<'k> = Setting.create DockProps.dock value

module DockPanel =
    /// The visible children, and the index (among them) of the one that fills, or -1.
    let private visible (children: Collections.Generic.IReadOnlyList<Element>) (fill: bool) =
        let shown = [| for c in children do if c.IsVisible then c |]
        struct (shown, (if fill && shown.Length > 0 then shown.Length - 1 else -1))

    let private measure (ctx: MeasureContext) (available: Size) =
        let spacing = ctx.Get DockProps.spacing
        let struct (shown, filler) = visible ctx.Children (ctx.Get DockProps.lastChildFill)
        // Space used by docked strips so far, and the largest size along the other direction.
        let mutable usedW, usedH, needW, needH = 0.0, 0.0, 0.0, 0.0
        for i in 0 .. shown.Length - 1 do
            let child = shown[i]
            let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - usedW), max 0.0 (available.Height - usedH)))
            let gap = if i < shown.Length - 1 then spacing else 0.0
            if i = filler then
                needW <- max needW (usedW + d.Width)
                needH <- max needH (usedH + d.Height)
            else
                match child.Get DockProps.dock with
                | Dock.Left | Dock.Right ->
                    needH <- max needH (usedH + d.Height)
                    usedW <- usedW + d.Width + gap
                | _ ->
                    needW <- max needW (usedW + d.Width)
                    usedH <- usedH + d.Height + gap
        Size.Create(max needW usedW, max needH usedH)

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let spacing = ctx.Get DockProps.spacing
        let struct (shown, filler) = visible ctx.Children (ctx.Get DockProps.lastChildFill)
        let mutable left, top, right, bottom = 0.0, 0.0, size.Width, size.Height
        for i in 0 .. shown.Length - 1 do
            let child = shown[i]
            let d = ctx.DesiredSize child
            let gap = if i < shown.Length - 1 then spacing else 0.0
            let w, h = max 0.0 (right - left), max 0.0 (bottom - top)
            if i = filler then ctx.Arrange(child, Rect.Create(left, top, w, h))
            else
                match child.Get DockProps.dock with
                | Dock.Left ->
                    let cw = min d.Width w
                    ctx.Arrange(child, Rect.Create(left, top, cw, h))
                    left <- left + cw + gap
                | Dock.Right ->
                    let cw = min d.Width w
                    ctx.Arrange(child, Rect.Create(right - cw, top, cw, h))
                    right <- right - cw - gap
                | Dock.Top ->
                    let ch = min d.Height h
                    ctx.Arrange(child, Rect.Create(left, top, w, ch))
                    top <- top + ch + gap
                | _ ->
                    let ch = min d.Height h
                    ctx.Arrange(child, Rect.Create(left, bottom - ch, w, ch))
                    bottom <- bottom - ch - gap
        for c in ctx.Children do
            if not c.IsVisible then ctx.Arrange(c, Rect.Create(0.0, 0.0, 0.0, 0.0))

    let Type = Element.define "DockPanel" [ Element.measure measure; Element.arrange arrange ]

    let create (settings: Setting<DockPanel> list) : View = View.Create(Type, settings)
    /// A style for DockPanels (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<DockPanel> list) : Style = Style.forType Type parts settings
