namespace Scribble.Core

open System
open Scribble.Rendering

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

    /// Where a flyout's tree puts its root (the Flyout, measured) beside `anchor` in a window of `window` size: its own
    /// placement, alignment and width rule, at most the window's size (`ElementTree.Place`).
    let flyout (anchor: Rect) (gap: float) (root: Element) (window: Size) =
        let desired = root.DesiredSize
        let w0 = if root.Get FlyoutProps.matchAnchorWidth then max desired.Width anchor.Width else desired.Width
        let w = min w0 window.Width
        let h = min desired.Height window.Height
        place anchor (root.Get FlyoutProps.placement) (root.Get FlyoutProps.alignment) gap w h window.Width window.Height

/// Content shown in its own layer next to an element, given to it with `flyout` (DESIGN.md, "M8 step 4", rounds 1,
/// 1b and 1c): its content alone, or the Flyout's settings:
/// ```
/// Button.flyout menu
/// Button.flyout [ Flyout.placement Placement.Right; Flyout.content menu ]
/// ```
/// The Flyout is also the box its content sits in: a Border the theme gives the surface colour, a line, rounded
/// corners, padding and a floating shadow (`Flyout.padding 0.0` on one; `Flyout.style` for all). It's styled as if
/// inside its element: `Button.styles [ Flyout.style ... ]`, `Style.parent Button.Type [ ... ]` and a `theme` around
/// the element reach it.
[<AbstractClass>]
type Flyout =
    inherit Border

    /// Which side of the element it shows on (default Below; flipped when it doesn't fit).
    static member placement<'k when 'k :> Flyout>(value: Placement) : Setting<'k> = Setting.create FlyoutProps.placement value
    /// Where it lines up along that side (default Start).
    static member alignment<'k when 'k :> Flyout>(value: FlyoutAlignment) : Setting<'k> = Setting.create FlyoutProps.alignment value
    /// Makes it controlled: it shows exactly this; clicks and dismissals only call `onOpenChanged`. Without it, the
    /// element it belongs to keeps its own open state, and its click opens and closes it.
    static member isOpen<'k when 'k :> Flyout>(value: bool) : Setting<'k> = Setting.create FlyoutProps.isOpen (ValueSome value)
    /// It opened or closed (or, controlled, asks to): a click, Escape, a press elsewhere.
    static member onOpenChanged<'k when 'k :> Flyout>(handler: bool -> unit) : Setting<'k> =
        Setting.create FlyoutProps.onOpenChanged (ValueSome handler)
    /// False: Escape and presses elsewhere don't close it; only the view or a click on its element does.
    static member lightDismiss<'k when 'k :> Flyout>(value: bool) : Setting<'k> = Setting.create FlyoutProps.lightDismiss value
    /// True: at least as wide as its element (a ComboBox's list).
    static member matchAnchorWidth<'k when 'k :> Flyout>(value: bool) : Setting<'k> = Setting.create FlyoutProps.matchAnchorWidth value

module Flyout =
    let Type = Element.extend Border.Type "Flyout" []
    /// Internal: apps give `flyout` the settings list (a Flyout given as content would sit inside another).
    let internal create (settings: Setting<Flyout> list) = View.Create(Type, settings)
    let style parts (settings: Setting<Flyout> list) = Style.forType Type parts settings

/// `flyout` on every element (here, after Flyout: its settings list form needs the type).
[<AutoOpen>]
module FlyoutSettings =
    type Layoutable with
        /// Its flyout, showing `content` in its own layer next to the element; Escape or a press elsewhere closes it, and
        /// the element's click opens and closes it. For placement, look or control, give the Flyout's settings instead.
        static member flyout<'k when 'k :> Layoutable>(content: View) : Setting<'k> =
            Setting.create FlyoutProps.flyout (ValueSome(Flyout.create [ Setting.create BorderProps.content (ValueSome content) ]))
        /// Its flyout, from the Flyout's settings: `Button.flyout [ Flyout.placement Placement.Right; Flyout.content menu ]`.
        static member flyout<'k when 'k :> Layoutable>(settings: Setting<Flyout> list) : Setting<'k> =
            Setting.create FlyoutProps.flyout (ValueSome(Flyout.create settings))
        /// Its context menu: `TextBlock.contextMenu (Menu.create [ Menu.children [ ... ] ])`. It opens at the pointer on
        /// a right-click, at the element on the Menu key or Shift+F10 while it (or something inside it) has the
        /// focus, and on a long press with a finger; Escape, a press elsewhere or choosing an item closes it. The
        /// nearest element with one, from what was clicked outward, shows its own.
        static member contextMenu<'k when 'k :> Layoutable>(menu: View) : Setting<'k> = Setting.create FlyoutProps.contextMenu (ValueSome menu)

/// What a tooltip's text or view sits in (`TooltipPresenter.style` restyles it).
[<AbstractClass>]
type TooltipPresenter =
    inherit Border

module TooltipPresenter =
    let Type = Element.extend Border.Type "TooltipPresenter" [ Element.defaults [ Layoutable.isHitTestVisible false ] ]
    let create (settings: Setting<TooltipPresenter> list) = View.Create(Type, settings)
    let style parts (settings: Setting<TooltipPresenter> list) = Style.forType Type parts settings
