namespace Scribble.Core

open System
open Scribble.Rendering

/// Where a DockPanel puts a child: along one of its sides, or over what the docked children leave.
type Dock =
    | Left = 0
    | Top = 1
    | Right = 2
    | Bottom = 3
    /// What's left once every docked child has its strip, wherever the child is written.
    | Fill = 4

module DockProps =
    /// Attached: the side a child docks to (default Left), or Fill.
    let dock = Prop.attached "DockPanel" "Dock" Dock.Left Affects.Measure
    let lastChildFill = Prop.define "LastChildFill" true Affects.Measure
    /// The gap after each docked child (DIPs).
    let spacing = Prop.define "Spacing" 0.0 Affects.Measure

/// Docks children to its sides in order, each taking a strip of what's left: `DockPanel.dock
/// Dock.Top` on a toolbar, `Dock.Bottom` on a status bar, `Dock.Left` on a sidebar. A child with
/// `Dock.Fill` gets the rest, wherever it's written, so the view can stay in the order it shows
/// (view order is the order Tab and a screen reader take):
///
///     DockPanel.create [
///         DockPanel.children [
///             toolbar [ DockPanel.dock Dock.Top ]
///             content [ DockPanel.dock Dock.Fill ]
///             statusBar [ DockPanel.dock Dock.Bottom ]
///         ]
///     ]
///
/// Several Fill children each get the rest, one over the other. When no child says Fill, the last
/// child fills (unless `lastChildFill false`). The usual app shell.
[<AbstractClass>]
type DockPanel =
    inherit Panel
    /// Whether the last child fills what's left (default true) instead of docking too. Only when
    /// no child says `Dock.Fill`: with one, the last child docks like the rest.
    static member lastChildFill<'k when 'k :> DockPanel>(value: bool) : Setting<'k> = Setting.create DockProps.lastChildFill value
    /// The gap between docked children (and before the filling one), in DIPs.
    static member spacing<'k when 'k :> DockPanel>(value: float) : Setting<'k> = Setting.create DockProps.spacing value
    /// Attached, on a child: the side it docks to (default Left), or `Dock.Fill` for what's left.
    static member dock<'k when 'k :> Layoutable>(value: Dock) : Setting<'k> = Setting.create DockProps.dock value

module DockPanel =
    /// The visible children in the order they're laid out: those that dock (in view order), then
    /// those that fill. The filling ones are the children that say `Dock.Fill`; when none does,
    /// the last child (if `lastChildFill`).
    let private split (children: Collections.Generic.IReadOnlyList<Element>) (lastChildFill: bool) =
        let shown = [| for c in children do if c.IsVisible then c |]
        let asked = shown |> Array.filter (fun c -> c.Get DockProps.dock = Dock.Fill)
        if asked.Length > 0 then
            struct (shown |> Array.filter (fun c -> c.Get DockProps.dock <> Dock.Fill), asked)
        elif lastChildFill && shown.Length > 0 then
            struct (Array.sub shown 0 (shown.Length - 1), [| shown[shown.Length - 1] |])
        else
            struct (shown, [||])

    let private measure (ctx: MeasureContext) (available: Size) =
        let spacing = ctx.Get DockProps.spacing
        let struct (docked, fillers) = split ctx.Children (ctx.Get DockProps.lastChildFill)
        // Space used by docked strips so far, and the largest size along the other direction.
        let mutable usedW, usedH, needW, needH = 0.0, 0.0, 0.0, 0.0
        for i in 0 .. docked.Length - 1 do
            let child = docked[i]
            let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - usedW), max 0.0 (available.Height - usedH)))
            let gap = if i < docked.Length - 1 || fillers.Length > 0 then spacing else 0.0
            match child.Get DockProps.dock with
            | Dock.Left | Dock.Right ->
                needH <- max needH (usedH + d.Height)
                usedW <- usedW + d.Width + gap
            | _ ->
                needW <- max needW (usedW + d.Width)
                usedH <- usedH + d.Height + gap
        for child in fillers do
            let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - usedW), max 0.0 (available.Height - usedH)))
            needW <- max needW (usedW + d.Width)
            needH <- max needH (usedH + d.Height)
        Size.Create(max needW usedW, max needH usedH)

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let spacing = ctx.Get DockProps.spacing
        let struct (docked, fillers) = split ctx.Children (ctx.Get DockProps.lastChildFill)
        let mutable left, top, right, bottom = 0.0, 0.0, size.Width, size.Height
        for i in 0 .. docked.Length - 1 do
            let child = docked[i]
            let d = ctx.DesiredSize child
            let gap = if i < docked.Length - 1 || fillers.Length > 0 then spacing else 0.0
            let w, h = max 0.0 (right - left), max 0.0 (bottom - top)
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
        for child in fillers do
            ctx.Arrange(child, Rect.Create(left, top, max 0.0 (right - left), max 0.0 (bottom - top)))
        for c in ctx.Children do
            if not c.IsVisible then ctx.Arrange(c, Rect.Create(0.0, 0.0, 0.0, 0.0))

    let Type = Element.define "DockPanel" [ Element.measure measure; Element.arrange arrange ]

    let create (settings: Setting<DockPanel> list) : View = View.Create(Type, settings)
    /// A style for DockPanels (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<DockPanel> list) : Style = Style.forType Type parts settings
