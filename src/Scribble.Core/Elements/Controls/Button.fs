namespace Scribble.Core

open Scribble.Accessibility
open System
open Scribble.Rendering

module ButtonProps =
    /// An icon before the content (or alone), in the button's text color.
    let icon: Prop<Icon voption> = Prop.define "Icon" ValueNone Affects.Measure
    /// The icon's width and height in DIPs.
    let iconSize = Prop.define "IconSize" 18.0 Affects.Measure
    /// The button stands beside a line of text that isn't its own (a toast's close button): its icon is centred
    /// on that text's optical middle, as an icon beside the button's own text is. Internal until an app needs it.
    let internal iconBesideText = Prop.define "IconBesideText" false Affects.Measure
    /// Where the icon was placed by the last arrange.
    let iconRect: Prop<Rect> = Prop.local "IconRect" (Rect.Create(0.0, 0.0, 0.0, 0.0))

/// A Border with a default look and something to click: `Button.create [ Button.text "+1";
/// Button.onClick (fun _ -> dispatch Increment) ]`. Every Border setting works on it.
/// It takes keyboard focus (Tab; a ring shows), and Enter or Space clicks it. Its hover, pressed
/// and disabled looks come from the theme (`overlay`, `scale`, `shadow`, `opacity` in state
/// blocks), so app styles can change them.
[<AbstractClass>]
type Button =
    inherit Border

    /// A text label, centered (shorthand for a TextBlock child; its color comes from the Button's
    /// `foreground`).
    static member text<'k when 'k :> Button>(value: string) : Setting<'k> =
        Setting.create BorderProps.content (
            ValueSome(
                TextBlock.create [
                    TextBlock.text value
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        )

    /// An icon before the text or content (or on its own), in the button's text color; the two sit
    /// together in the middle: `Button.icon addIcon; Button.text "Add"`.
    static member icon<'k when 'k :> Button>(value: Icon) : Setting<'k> = Setting.create ButtonProps.icon (ValueSome value)

    /// The icon's width and height in DIPs (default 18).
    static member iconSize<'k when 'k :> Button>(value: float) : Setting<'k> = Setting.create ButtonProps.iconSize value

module Button =
    /// Space between the icon and the content.
    let private gap = 8.0

    /// Border's look, the icon, and the focus ring.
    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
        match ctx.Get ButtonProps.icon with
        | ValueSome icon -> IconView.draw ctx list icon (ctx.Get ButtonProps.iconRect) (ctx.Get ButtonProps.iconSize)
        | ValueNone -> ()
        if ctx.IsFocusVisible then FocusRing.draw ctx list (ctx.Get BorderProps.cornerRadius)

    /// Border's measure, with room for the icon before the content.
    let private measure (ctx: MeasureContext) (available: Size) =
        let ix, iy = Border.inset ctx
        let icon = if (ctx.Get ButtonProps.icon).IsSome then ctx.Get ButtonProps.iconSize else 0.0
        let extra = if icon > 0.0 && ctx.Children.Count > 0 then icon + gap else icon
        let mutable w, h = ix + extra, iy + icon
        for child in ctx.Children do
            let d = ctx.Measure(child, Size.Create(max 0.0 (available.Width - ix - extra), max 0.0 (available.Height - iy)))
            w <- max w (d.Width + ix + extra)
            h <- max h (d.Height + iy)
        Size.Create(w, h)

    /// How far below the slot's middle a text's optical middle sits: halfway between the middle of
    /// its capitals and the middle of its lowercase letters, for one line centered in the slot. An icon
    /// beside text is centered there, since lowercase-heavy labels look low beside an exactly centered
    /// icon. For a TextBlock inside the button (`Button.text`), or, with `iconBesideText`, for text beside the
    /// button in the button's own font; other content keeps the icon centered.
    let private textNudge (ctx: ArrangeContext) =
        let ownText = ctx.Children.Count > 0 && obj.ReferenceEquals(ctx.Children[0].Type, TextBlock.Type)
        if not ownText && not (ctx.Get ButtonProps.iconBesideText) then 0.0
        else
            let text = if ownText then ctx.Children[0] else ctx.Element
            let font = text.Get InheritedProps.font
            let face = font.Face
            let em = text.Get InheritedProps.fontSize / float face.UnitsPerEm
            let lineHeight = float (face.Ascender - face.Descender + face.LineGap)
            // From the line box's middle down to the baseline, then up to the optical middle.
            (float face.Ascender - lineHeight / 2.0 - float (face.CapHeight + face.XHeight) / 4.0) * em

    /// Border's arrange; with an icon, the icon and the content form one centered row.
    let private arrange (ctx: ArrangeContext) (size: Size) =
        let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
        let slot =
            Rect.Create(p.Left + t, p.Top + t, max 0.0 (size.Width - p.Horizontal - 2.0 * t), max 0.0 (size.Height - p.Vertical - 2.0 * t))
        match ctx.Get ButtonProps.icon with
        | ValueNone ->
            for child in ctx.Children do
                ctx.Arrange(child, slot)
        | ValueSome _ ->
            let icon = ctx.Get ButtonProps.iconSize
            let content = if ctx.Children.Count > 0 then min (ctx.DesiredSize ctx.Children[0]).Width (max 0.0 (slot.Width - icon - gap)) else 0.0
            let row = if ctx.Children.Count > 0 then icon + gap + content else icon
            // Rounded half-up, like layout, so the icon and the content keep their exact gap.
            let left = Math.Floor(slot.X + max 0.0 ((slot.Width - row) / 2.0) + 0.5)
            ctx.SetLocal(ButtonProps.iconRect, Rect.Create(left, slot.Y + textNudge ctx, icon, slot.Height))
            for child in ctx.Children do
                ctx.Arrange(child, Rect.Create(left + icon + gap, slot.Y, content, slot.Height))

    let Type =
        Element.extend Border.Type "Button" [
            Element.measure measure
            Element.arrange arrange
            Element.render render
            // The dark base look (also without a theme); the theme adds the state looks.
            Element.defaults [
                Semantics.role Role.Button
                // Its look at rest, by theme value (the theme's styles add the state looks).
                Layoutable.foreground Palette.text
                Border.background Palette.control
                Border.borderBrush Palette.border
                Border.borderThickness 1.0
                Border.cornerRadius Palette.cornerRadius
                Border.padding (14.0, 7.0)
                Border.shadow Palette.shadow
                Layoutable.cursor Cursor.Hand
                Layoutable.focusable true
            ]
        ]

    let create (settings: Setting<Button> list) : View = View.Create(Type, settings)
    /// A style for Buttons: `Button.style [ Style.cls "c"; States.hover ] [ Button.margin 4.0 ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Button> list) : Style = Style.forType Type parts settings
