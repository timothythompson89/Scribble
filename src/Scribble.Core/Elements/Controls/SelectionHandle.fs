namespace Scribble.Core

open System
open Scribble.Rendering

/// Which end of a text selection a selection handle moves.
[<RequireQualifiedAccess>]
type SelectionHandleKind =
    /// The selection's start: it hangs to the left of its point.
    | Start
    /// The selection's end: it hangs to the right.
    | End
    /// Nothing is selected: it moves the caret, and hangs under it.
    | Caret

module SelectionHandleProps =
    let kind = Prop.define "SelectionHandleKind" SelectionHandleKind.Caret Affects.Measure
    let fill = Prop.define "SelectionHandleFill" Color.Transparent Affects.Render
    /// A pointer took the handle (0), moved with it (1), let go (2) or was taken away (3); with where the pointer is
    /// in the window (DIPs).
    let internal onDrag: Prop<(int -> Point -> unit) voption> = Prop.define "SelectionHandleOnDrag" ValueNone Affects.Nothing
    /// Read by the layer the handle stands in: where its point goes in the window (DIPs), the foot of the caret
    /// it moves.
    let internal point = Prop.attached "SelectionLayer" "Point" Point.Zero Affects.Measure
    let internal dragging = Prop.local "SelectionHandleDragging" false

/// A mark a finger drags to move one end of a text selection, or the caret (DESIGN.md, "9g: selection by touch").
/// A TextBox shows its own, in a layer above the window's content; `SelectionHandle.style` restyles them:
///
///     SelectionHandle.style [] [ SelectionHandle.fill (Color.Hex 0xE0AF68) ]
[<AbstractClass>]
type SelectionHandle =
    inherit Layoutable
    /// The handle's colour (the theme's accent).
    static member fill<'k when 'k :> SelectionHandle>(value: Color) : Setting<'k> = Setting.create SelectionHandleProps.fill value
    static member fill<'k when 'k :> SelectionHandle>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme SelectionHandleProps.fill themeValue

module SelectionHandle =
    /// How wide the handle is drawn, and the side of the square a finger can take it by (decision 99).
    let internal drawn = 20.0
    let internal grab = 44.0
    /// How far below its point a handle reaches.
    let internal depth = 28.0
    /// How far above its point the box a finger takes it by starts: little, so the text beside the selection can
    /// still be tapped; the rest of the box is below the line.
    let internal reach = 4.0

    /// Where a handle's point is in its own box: the drawing is centred sideways in the box a finger takes it by,
    /// at its top.
    let internal pointIn (kind: SelectionHandleKind) : Point =
        match kind with
        | SelectionHandleKind.Start -> Point(grab / 2.0 + drawn / 2.0, reach)
        | SelectionHandleKind.End -> Point(grab / 2.0 - drawn / 2.0, reach)
        | SelectionHandleKind.Caret -> Point(grab / 2.0, reach)

    /// A drop with its point at the top, in a 24 × 24 box: a circle 18 across with a corner drawn out of it.
    let private drop = Icon.fromPath "M12 0 L18.364 6.366 A9 9 0 1 1 5.636 6.366 Z" [ Icon.name "selection handle" ]

    let private render (ctx: RenderContext) (list: DrawList) =
        let kind = ctx.Get SelectionHandleProps.kind
        let fill = ctx.Get SelectionHandleProps.fill
        let p = pointIn kind
        let r = drawn / 2.0
        match kind with
        // A circle with one corner left square: the corner is the handle's point.
        | SelectionHandleKind.Start ->
            list.FillRoundedRect(Rect.Create(p.X - drawn, p.Y, drawn, drawn), { TopLeft = r; TopRight = 0.0; BottomRight = r; BottomLeft = r }, fill)
        | SelectionHandleKind.End ->
            list.FillRoundedRect(Rect.Create(p.X, p.Y, drawn, drawn), { TopLeft = 0.0; TopRight = r; BottomRight = r; BottomLeft = r }, fill)
        | SelectionHandleKind.Caret ->
            let size = drawn * 24.0 / 18.0
            ctx.Text.DrawIcon(list, Icon.geometry drop, drop.ViewBox, 0.0, false, false, p.X - size / 2.0, p.Y, size, fill)

    let private tell (e: Element) (phase: int) (at: Point) =
        match e.Get SelectionHandleProps.onDrag with
        | ValueSome f -> f phase at
        | ValueNone -> ()

    let Type =
        Element.define "SelectionHandle" [
            Element.defaults [ SelectionHandle.fill Palette.accent ]
            Element.measure (fun _ _ -> Size.Create(grab, grab))
            Element.render render
            Element.pointerDown (fun ctx args ->
                if args.Button = ValueSome MouseButton.Left then
                    args.Handle()
                    ctx.SetLocal(SelectionHandleProps.dragging, true)
                    tell ctx.Element 0 args.Position)
            Element.pointerMove (fun ctx args ->
                if ctx.Get SelectionHandleProps.dragging then
                    args.Handle()
                    tell ctx.Element 1 args.Position)
            Element.pointerUp (fun ctx args ->
                if ctx.Get SelectionHandleProps.dragging then
                    args.Handle()
                    ctx.SetLocal(SelectionHandleProps.dragging, false)
                    tell ctx.Element 2 args.Position)
            Element.pointerCanceled (fun ctx ->
                if ctx.Get SelectionHandleProps.dragging then
                    ctx.SetLocal(SelectionHandleProps.dragging, false)
                    tell ctx.Element 3 Point.Zero)
        ]

    let internal create (settings: Setting<SelectionHandle> list) : View = View.Create(Type, settings)
    /// A style for selection handles: `SelectionHandle.style [] [ SelectionHandle.fill c ]`.
    let style (parts: SelectorPart list) (settings: Setting<SelectionHandle> list) : Style = Style.forType Type parts settings

/// What a TextBox shows above the window's content for a finger (DESIGN.md, "9g"): its selection handles, each at
/// its point, and its context menu as a row beside the selection. It fills the window and lets the pointer
/// through wherever it shows nothing.
module internal SelectionLayer =
    /// On the row: the part of the selection that shows, in the window (DIPs).
    let anchor = Prop.attached "SelectionLayer" "Anchor" (Rect.Create(0.0, 0.0, 0.0, 0.0)) Affects.Measure
    /// On the row: neither end of the selection shows, so the row goes in the middle of `anchor`.
    let middle = Prop.attached "SelectionLayer" "Middle" false Affects.Measure
    /// On the row: called when it is wider than the window (its owner shows it another way).
    let onOverflow: Prop<(unit -> unit) voption> = Prop.attached "SelectionLayer" "OnOverflow" ValueNone Affects.Nothing

    /// Between the row and the text, and between the row and the window's edge.
    let gap = 8.0
    let edge = 4.0

    let private isHandle (e: Element) = obj.ReferenceEquals(e.Type, SelectionHandle.Type)

    /// Where a row of `w` × `h` goes for a selection showing in `anchor` (decision 86): centred above it; below the
    /// lower handle when there is no room above; in its middle when neither end shows; always inside the window.
    let place (anchor: Rect) (middle: bool) (w: float) (h: float) (window: Size) : Rect =
        let clamp (v: float) (size: float) (limit: float) = max edge (min v (limit - size - edge))
        let above = anchor.Y - gap - h
        let below = anchor.Bottom + SelectionHandle.depth + gap
        let y =
            if middle then anchor.Y + (anchor.Height - h) / 2.0
            elif above >= edge then above
            elif below + h <= window.Height - edge then below
            else above
        Rect.Create(Math.Round(clamp (anchor.X + (anchor.Width - w) / 2.0) w window.Width), Math.Round(clamp y h window.Height), w, h)

    let private measure (ctx: MeasureContext) (available: Size) =
        for c in ctx.Children do
            if isHandle c then ctx.Measure(c, Size.Create(SelectionHandle.grab, SelectionHandle.grab)) |> ignore
            else ctx.Measure(c, Size.Create(Double.PositiveInfinity, Double.PositiveInfinity)) |> ignore
        Size.Create((if Double.IsInfinity available.Width then 0.0 else available.Width), (if Double.IsInfinity available.Height then 0.0 else available.Height))

    let private arrange (ctx: ArrangeContext) (size: Size) =
        for c in ctx.Children do
            if isHandle c then
                let p = c.Get SelectionHandleProps.point
                let k = SelectionHandle.pointIn (c.Get SelectionHandleProps.kind)
                ctx.Arrange(c, Rect.Create(p.X - k.X, p.Y - k.Y, SelectionHandle.grab, SelectionHandle.grab))
            else
                let d = ctx.DesiredSize c
                match c.Get onOverflow with
                | ValueSome tooWide when d.Width > size.Width - 2.0 * edge ->
                    // Out of sight for this frame: its owner shows it another way from the next.
                    ctx.Arrange(c, Rect.Create(-d.Width - 1000.0, 0.0, d.Width, d.Height))
                    try
                        tooWide ()
                    with ex ->
                        c.ReportError ex
                | _ ->
                    let w, h = min d.Width (max 0.0 (size.Width - 2.0 * edge)), min d.Height (max 0.0 (size.Height - 2.0 * edge))
                    ctx.Arrange(c, place (c.Get anchor) (c.Get middle) w h size)

    let Type =
        Element.define "SelectionLayer" [
            Element.measure measure
            Element.arrange arrange
            Element.passThrough
        ]

    let create (children: View list) : View = View.Create(Type, ([ Panel.children children ]: Setting<Panel> list))
