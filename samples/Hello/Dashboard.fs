/// The Hello sample's dashboard, built from elements: a top bar and a grid of cards that reflows
/// with the window. Shared by the desktop and browser hosts and by the rendering tests, which check
/// that it looks the same as the hand-drawn version it replaced.
///
/// Border, StackPanel and TextBlock come from Scribble.Core. CardGrid, Layers, AccentStrip and
/// ProgressBar are defined here, to show what a custom element takes: some properties, an element
/// type (a record of measure/arrange/render functions), and a settings type with setting functions.
module Scribble.Samples.Dashboard

open System
open System.Collections.Generic
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

let background = Color.Hex 0x181825
let topBarFill = Color.Hex 0x11111B
let cardFill = Color.Hex 0x26263A
let cardBorder = Color.Hex 0x38384F
let trackFill = Color.Hex 0x1E1E2E
let textPrimary = Color.Hex 0xE6E6F0
let textSecondary = Color.Hex 0x9A9AB2

/// Title, subtitle and avatar emoji for each card.
let cards =
    [| "Build pipeline", "Compiling 12 projects", "🚀"
       "Text shaping", "HarfBuzz · Inter", "🔤"
       "Glyph atlas", "2048 × 2048 coverage", "🧩"
       "Emoji", "Twemoji · font fallback", "🎉"
       "Layout", "Measure, arrange, rounding", "📐"
       "Controls", "Buttons, inputs, lists", "🎛️"
       "Inspector", "Tree, bounds, styles", "🔍"
       "Hot reload", "Swap views live", "♻️" |]

let accentColor (i: int) = Color.Hsv(float i * 45.0 + 200.0, 0.55, 0.95)

/// The value each progress bar shows at a given animation time.
let progressAt (i: int) (seconds: float) = 0.5 + 0.45 * sin (seconds * 0.8 + float i)

// --- Custom elements -----------------------------------------------------------------------------
// Each one: a Props module (its properties), a type with its settings, and a module of the same
// name with its definition (Type) and create.

module CardGridProps =
    let minColumnWidth = Prop.define "MinColumnWidth" 240.0 Affects.Measure
    let gap = Prop.define "Gap" 20.0 Affects.Measure
    let rowHeight = Prop.define "RowHeight" 136.0 Affects.Measure

/// Equal-width columns (as many as fit at minColumnWidth), fixed-height rows, a gap between both.
[<AbstractClass>]
type CardGrid =
    inherit Panel
    static member minColumnWidth<'k when 'k :> CardGrid>(value: float) : Setting<'k> = Setting.create CardGridProps.minColumnWidth value
    static member gap<'k when 'k :> CardGrid>(value: float) : Setting<'k> = Setting.create CardGridProps.gap value
    static member rowHeight<'k when 'k :> CardGrid>(value: float) : Setting<'k> = Setting.create CardGridProps.rowHeight value

module CardGrid =
    let private columns (ctx: MeasureContext) (width: float) =
        let gap, minWidth = ctx.Get CardGridProps.gap, ctx.Get CardGridProps.minColumnWidth
        if Double.IsInfinity width then max 1 ctx.Children.Count
        else max 1 (int ((width + gap) / (minWidth + gap)))

    let Type =
        Element.define "CardGrid" [
            Element.measure (fun ctx available ->
                let gap, rowHeight = ctx.Get CardGridProps.gap, ctx.Get CardGridProps.rowHeight
                let n = columns ctx available.Width
                let cellWidth =
                    if Double.IsInfinity available.Width then ctx.Get CardGridProps.minColumnWidth
                    else (available.Width - float (n - 1) * gap) / float n
                for child in ctx.Children do
                    ctx.Measure(child, Size.Create(cellWidth, rowHeight)) |> ignore
                let rows = (ctx.Children.Count + n - 1) / n
                Size.Create(
                    (if Double.IsInfinity available.Width then float n * (cellWidth + gap) - gap else available.Width),
                    max 0.0 (float rows * (rowHeight + gap) - gap)))
            Element.arrange (fun ctx size ->
                let gap, rowHeight = ctx.Get CardGridProps.gap, ctx.Get CardGridProps.rowHeight
                let minWidth = ctx.Get CardGridProps.minColumnWidth
                let n = max 1 (int ((size.Width + gap) / (minWidth + gap)))
                let cellWidth = (size.Width - float (n - 1) * gap) / float n
                for i in 0 .. ctx.Children.Count - 1 do
                    let x = float (i % n) * (cellWidth + gap)
                    let y = float (i / n) * (rowHeight + gap)
                    ctx.Arrange(ctx.Children[i], Rect.Create(x, y, cellWidth, rowHeight)))
        ]

    let create (settings: Setting<CardGrid> list) = View.Create(Type, settings)

/// Children on top of each other, each filling the panel within its own margin and alignment.
/// That's what an element does when its definition leaves everything out, so there's nothing to
/// write but a name.
[<AbstractClass>]
type Layers =
    inherit Panel

module Layers =
    let Type = Element.define "Layers" []
    let create (settings: Setting<Layers> list) = View.Create(Type, settings)

module AccentStripProps =
    let color = Prop.define "Color" Color.Transparent Affects.Render
    let cornerRadius = Prop.define "CornerRadius" CornerRadius.Zero Affects.Render
    let thickness = Prop.define "Thickness" 6.0 Affects.Render

/// A rounded rectangle cut off below `thickness`: the colored strip across the top of a card,
/// following the card's own corners exactly.
[<AbstractClass>]
type AccentStrip =
    inherit Layoutable
    static member color<'k when 'k :> AccentStrip>(value: Color) : Setting<'k> = Setting.create AccentStripProps.color value
    static member cornerRadius<'k when 'k :> AccentStrip>(value: float) : Setting<'k> =
        Setting.create AccentStripProps.cornerRadius (CornerRadius.Uniform value)

module AccentStrip =
    let Type =
        Element.define "AccentStrip" [
            Element.render (fun ctx list ->
                let size = ctx.Size
                list.PushClip(Rect.Create(0.0, 0.0, size.Width, ctx.Get AccentStripProps.thickness))
                list.FillRoundedRect(Rect.Create(0.0, 0.0, size.Width, size.Height), ctx.Get AccentStripProps.cornerRadius, ctx.Get AccentStripProps.color)
                list.PopClip())
        ]

    let create (settings: Setting<AccentStrip> list) = View.Create(Type, settings)

module ProgressBarProps =
    /// 0 to 1.
    let value = Prop.define "Value" 0.0 Affects.Render
    let accent = Prop.define "Accent" Color.White Affects.Render
    let track = Prop.define "Track" trackFill Affects.Render

/// A track with a fill that grows with the value. The fill uses Precise edges so it moves smoothly
/// instead of jumping a pixel at a time.
[<AbstractClass>]
type ProgressBar =
    inherit Layoutable
    static member value<'k when 'k :> ProgressBar>(value: float) : Setting<'k> = Setting.create ProgressBarProps.value value
    static member accent<'k when 'k :> ProgressBar>(value: Color) : Setting<'k> = Setting.create ProgressBarProps.accent value

module ProgressBar =
    let Type =
        Element.define "ProgressBar" [
            Element.measure (fun _ _ -> Size.Create(0.0, 8.0))
            Element.render (fun ctx list ->
                let size = ctx.Size
                let radius = CornerRadius.Uniform(size.Height / 2.0)
                let value = Math.Clamp(ctx.Get ProgressBarProps.value, 0.0, 1.0)
                list.FillRoundedRect(Rect.Create(0.0, 0.0, size.Width, size.Height), radius, ctx.Get ProgressBarProps.track)
                list.FillRoundedRect(Rect.Create(0.0, 0.0, size.Width * value, size.Height), radius, ctx.Get ProgressBarProps.accent, Precise))
        ]

    let create (settings: Setting<ProgressBar> list) = View.Create(Type, settings)

// --- Cards and the whole dashboard ---------------------------------------------------------------

/// Settings that pin an element's top-left corner at (left, top) of its parent.
let at<'k when 'k :> Layoutable> (left: float) (top: float) : Setting<'k> list =
    [ Layoutable.margin (left, top, 0.0, 0.0)
      Layoutable.horizontalAlignment HorizontalAlignment.Left
      Layoutable.verticalAlignment VerticalAlignment.Top ]

let private regular, semi = Fonts.Regular(), Fonts.SemiBold()

let private cardShadow hovered =
    { Blur = (if hovered then 28.0 else 16.0); OffsetX = 0.0; OffsetY = (if hovered then 8.0 else 4.0); Spread = 0.0
      Color = Color.Rgba(0.0f, 0.0f, 0.0f, 0.5f) }

/// One card, showing `progress` (0–1), highlighted when hovered. `onHover` hears about the pointer
/// coming over the card (Some index) and leaving it (None).
let card (index: int) (progress: float) (hovered: bool) (onHover: int option -> unit) : View =
    let title, subtitle, avatar = cards[index]
    let accent = accentColor index
    let thickness = if hovered then 2.0 else 1.0
    Border.create [
        Border.name $"card {index}"
        Border.onPointerEnter (fun _ -> onHover (Some index))
        Border.onPointerLeave (fun _ -> onHover None)
        Border.background cardFill
        Border.cornerRadius 12.0
        Border.borderThickness thickness
        Border.borderBrush (if hovered then accent else cardBorder)
        Border.shadow (cardShadow hovered)
        Border.clipToBounds true
        Border.child (
            Layers.create [
                // Keep the content in place when the border gets thicker: the border is drawn over it.
                Layers.margin (-thickness)
                Layers.children [
                    AccentStrip.create [ AccentStrip.color accent; AccentStrip.cornerRadius 12.0 ]
                    Border.create (
                        at 20.0 26.0
                        @ [ Border.width 36.0
                            Border.height 36.0
                            Border.cornerRadius 18.0
                            Border.background (accent.WithAlpha 0.25f)
                            Border.child (
                                TextBlock.create [
                                    TextBlock.text avatar
                                    TextBlock.fontSize 18.0
                                    TextBlock.foreground textPrimary
                                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                ]) ])
                    TextBlock.create (at 68.0 25.0 @ [ TextBlock.text title; TextBlock.font semi; TextBlock.fontSize 15.0; TextBlock.foreground textPrimary ])
                    TextBlock.create (
                        at 68.0 46.0
                        @ [ TextBlock.text subtitle
                            TextBlock.fontSize 12.5
                            TextBlock.foreground textSecondary
                            TextBlock.textTrimming CharacterEllipsis ])
                    TextBlock.create (at 20.0 82.0 @ [ TextBlock.text "Progress"; TextBlock.fontSize 12.0; TextBlock.foreground textSecondary ])
                    TextBlock.create [
                        TextBlock.text ($"{progress * 100.0:F0}" + "%")
                        TextBlock.font semi
                        TextBlock.fontSize 12.0
                        TextBlock.foreground textPrimary
                        TextBlock.horizontalAlignment HorizontalAlignment.Right
                        TextBlock.verticalAlignment VerticalAlignment.Top
                        TextBlock.margin (0.0, 82.0, 20.0, 0.0)
                    ]
                    ProgressBar.create [
                        ProgressBar.value progress
                        ProgressBar.accent accent
                        ProgressBar.margin (20.0, 104.0, 20.0, 0.0)
                        ProgressBar.verticalAlignment VerticalAlignment.Top
                    ]
                ]
            ])
    ]

let private dot (color: int) =
    Border.create [ Border.width 12.0; Border.height 12.0; Border.cornerRadius 6.0; Border.background (Color.Hex color) ]

/// The top bar: dots, title and subtitle on a 56-high row, and the toolbar (if any) at its right
/// end, or, when the window is too narrow for both (a phone), on its own rows below the title.
let private topBar (toolbar: View option) =
    let titleRow =
        Layers.create [
            Layers.height 56.0
            Layers.children [
                StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 10.0
                    StackPanel.verticalAlignment VerticalAlignment.Top
                    StackPanel.margin (24.0, 22.0, 0.0, 0.0)
                    StackPanel.children [ dot 0xF38BA8; dot 0xF9E2AF; dot 0xA6E3A1 ]
                ]
                StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 12.0
                    StackPanel.margin (106.0, 0.0, 0.0, 0.0)
                    StackPanel.children [
                        TextBlock.create (at 0.0 17.0 @ [ TextBlock.text "Scribble"; TextBlock.font semi; TextBlock.fontSize 17.0; TextBlock.foreground textPrimary ])
                        TextBlock.create (
                            at 0.0 21.0
                            @ [ TextBlock.text "Milestone 4 · views and the differ ✨"
                                TextBlock.fontSize 13.0
                                TextBlock.foreground textSecondary
                                // The rest of the row, with "…" where it runs out (a phone).
                                TextBlock.width "1*"
                                TextBlock.textTrimming CharacterEllipsis ])
                    ]
                ]
            ]
        ]
    Border.create [
        Border.name "top bar"
        Border.background topBarFill
        Border.child (
            SplitRow.create [
                SplitRow.endMargin (0.0, 0.0, 16.0, 0.0)
                SplitRow.wrapMargin (24.0, 0.0, 16.0, 16.0)
                SplitRow.children [
                    titleRow
                    match toolbar with
                    | Some t -> t
                    | None -> ()
                ]
            ])
    ]

/// The whole dashboard at `seconds` of animation: the cards in `order` (card indices), the one at
/// `hovered` highlighted, and an optional toolbar on the right of the top bar. Called again whenever
/// any of these change; Scribble patches only what differs. `onHover` hears which card the pointer is over.
let view (seconds: float) (hovered: int option) (order: int list) (toolbar: View option) (onHover: int option -> unit) : View =
    StackPanel.create [
        StackPanel.name "dashboard"
        StackPanel.children [
            topBar toolbar
            Border.create [ Border.height 1.0; Border.background cardBorder ]
            // The rest of the window; the cards scroll (wheel or finger) when they don't all fit.
            ScrollViewer.create [
                ScrollViewer.name "card scroller"
                ScrollViewer.height "1*"
                ScrollViewer.content (
                    // 24 DIPs below the top bar's bottom edge (the 1 DIP divider sits inside that gap).
                    CardGrid.create [
                        CardGrid.name "cards"
                        CardGrid.margin (24.0, 23.0, 24.0, 24.0)
                        // Keyed by card index, so a card keeps its element when the order changes.
                        CardGrid.items order id (fun i -> card i (progressAt i seconds) (hovered = Some i) onHover)
                    ])
            ]
        ]
    ]

/// The index of the card containing an element (for hover), if any.
let rec cardAt (hit: Element voption) : int option =
    match hit with
    | ValueNone -> None
    | ValueSome e when e.Name.StartsWith "card " -> Some(int (e.Name.Substring 5))
    | ValueSome e -> cardAt e.Parent
