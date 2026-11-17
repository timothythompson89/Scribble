namespace Scribble.Core

open System
open Scribble.Rendering

module WrapPanelProps =
    let spacing = Prop.define "Spacing" 0.0 Affects.Measure
    let lineSpacing = Prop.define "LineSpacing" 0.0 Affects.Measure
    /// Attached: the next child starts a new row.
    let breakAfter = Prop.attached "WrapPanel" "BreakAfter" false Affects.Measure

/// Children left to right at their own sizes; one that doesn't fit what's left of a row starts the
/// next (WinForms calls this a flow panel). Each row is as tall as its tallest child (the last row:
/// the rest of the panel's height, if it's given more), and every child on it gets that height.
/// `WrapPanel.breakAfter true` on a child starts a new row after it.
[<AbstractClass>]
type WrapPanel =
    inherit Panel
    /// The gap between children on a row.
    static member spacing<'k when 'k :> WrapPanel>(value: float) : Setting<'k> = Setting.create WrapPanelProps.spacing value
    /// The gap between rows.
    static member lineSpacing<'k when 'k :> WrapPanel>(value: float) : Setting<'k> = Setting.create WrapPanelProps.lineSpacing value
    /// Attached, on a child: the next child starts a new row.
    static member breakAfter<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create WrapPanelProps.breakAfter value

module WrapPanel =
    /// Each visible child's slot for a given width and height (hidden ones get none), and the
    /// panel's natural size.
    let private place (spacing: float) (lineSpacing: float) (width: float) (height: float) (children: Collections.Generic.IReadOnlyList<Element>) (sizeOf: Element -> Size) =
        let slots = Array.create children.Count (Rect.Create(0.0, 0.0, 0.0, 0.0))
        let line = ResizeArray<int>()
        let mutable x, y, lineHeight, widest = 0.0, 0.0, 0.0, 0.0
        let mutable forceBreak = false
        // Gives the finished row's children the row's height.
        let finishLine () =
            for i in line do
                slots[i] <- Rect.Create(slots[i].X, slots[i].Y, slots[i].Width, lineHeight)
            line.Clear()
        for i in 0 .. children.Count - 1 do
            let child = children[i]
            if child.IsVisible then
                let s = sizeOf child
                if line.Count > 0 && (forceBreak || x + s.Width > width) then
                    finishLine ()
                    y <- y + lineHeight + lineSpacing
                    x <- 0.0
                    lineHeight <- 0.0
                slots[i] <- Rect.Create(x, y, s.Width, s.Height)
                line.Add i
                widest <- max widest (x + s.Width)
                x <- x + s.Width + spacing
                lineHeight <- max lineHeight s.Height
                forceBreak <- child.Get WrapPanelProps.breakAfter
        let natural = Size.Create(widest, y + lineHeight)
        if not (Double.IsInfinity height) then lineHeight <- max lineHeight (height - y)
        finishLine ()
        struct (slots, natural)

    let Type =
        Element.define "WrapPanel" [
            Element.measure (fun ctx available ->
                let measured = Collections.Generic.Dictionary<Element, Size>(HashIdentity.Reference)
                for c in ctx.Children do
                    measured[c] <- ctx.Measure(c, Size.Create(available.Width, Double.PositiveInfinity))
                let struct (_, natural) =
                    place (ctx.Get WrapPanelProps.spacing) (ctx.Get WrapPanelProps.lineSpacing) available.Width Double.PositiveInfinity ctx.Children (fun c -> measured[c])
                natural)
            Element.arrange (fun ctx size ->
                let struct (slots, _) =
                    place (ctx.Get WrapPanelProps.spacing) (ctx.Get WrapPanelProps.lineSpacing) size.Width size.Height ctx.Children ctx.DesiredSize
                for i in 0 .. ctx.Children.Count - 1 do
                    ctx.Arrange(ctx.Children[i], slots[i]))
        ]

    let create (settings: Setting<WrapPanel> list) : View = View.Create(Type, settings)
    /// A style for WrapPanels (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<WrapPanel> list) : Style = Style.forType Type parts settings
