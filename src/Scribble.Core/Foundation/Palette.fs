namespace Scribble.Core

open Scribble.Rendering
open Scribble.Text

/// The built-in theme values. Settings take them in place of values (`Border.background
/// Palette.surface`); themes set them (`Theme.light [ Theme.accent c ]`, or `Theme.set
/// Palette.accent c`); code reads one with `theme.Get Palette.accent`. The defaults are the dark
/// theme's values, used where there is no theme.
module Palette =
    /// Behind everything (the window).
    let background = ThemeValue.define "Palette.background" (Color.Hex 0x181825)
    /// Panels and cards.
    let surface = ThemeValue.define "Palette.surface" (Color.Hex 0x24243A)
    /// Controls such as buttons.
    let control = ThemeValue.define "Palette.control" (Color.Hex 0x3A3A55)
    /// Controls' borders.
    let border = ThemeValue.define "Palette.border" (Color.Hex 0x4A4A68)
    let text = ThemeValue.define "Palette.text" (Color.Hex 0xE6E6F0)
    /// Secondary text.
    let textDim = ThemeValue.define "Palette.textDim" (Color.Hex 0x9A9AB0)
    let accent = ThemeValue.define "Palette.accent" (Color.Hex 0x3D59A1)
    /// Behind something destructive or failed (a Danger button, a critical toast); `onAccent` text reads on it.
    let danger = ThemeValue.define "Palette.danger" (Color.Hex 0x8C2F39)
    /// Text that says something failed, on the background or a surface (`danger` is for behind text, and too dark to read as text).
    let textDanger = ThemeValue.define "Palette.textDanger" (Color.Hex 0xF7768E)
    /// Something to be careful about (a Warning confirmation's button); `onAccent` text reads on it.
    let warning = ThemeValue.define "Palette.warning" (Color.Hex 0x9A6700)
    /// Text on accent- or danger-colored backgrounds (white in both built-in themes).
    let onAccent = ThemeValue.define "Palette.onAccent" Color.White
    let focusRing = ThemeValue.define "Palette.focusRing" (Color.Hex 0x7AA2F7)
    /// Laid over a control while hovered (`Border.overlay`).
    let hoverTint = ThemeValue.define "Palette.hoverTint" (Color.White.WithAlpha 0.08f)
    /// Laid over a control while pressed.
    let pressTint = ThemeValue.define "Palette.pressTint" (Color.Black.WithAlpha 0.2f)
    /// The dimming a modal lays over everything below it.
    let scrim = ThemeValue.define "Palette.scrim" (Color.Black.WithAlpha 0.55f)
    /// The scrollbar's thumb.
    let scrollThumb = ThemeValue.define "Palette.scrollThumb" (Color.White.WithAlpha 0.3f)
    /// The faint track under the scrollbar thumb while the pointer is on the bar.
    let scrollTrack = ThemeValue.define "Palette.scrollTrack" (Color.White.WithAlpha 0.06f)
    /// A raised control's shadow (a button at rest; the theme flattens it when pressed or disabled).
    let shadow =
        ThemeValue.define "Palette.shadow" { Blur = 3.0; OffsetX = 0.0; OffsetY = 1.0; Spread = 0.0; Color = Color.Rgba(0.0f, 0.0f, 0.0f, 0.35f) }
    /// Controls' corner radius, in DIPs.
    let cornerRadius = ThemeValue.define "Palette.cornerRadius" 6.0
    /// The text font.
    let font = ThemeValue.define "Palette.font" (Fonts.Regular())
    /// The text size, in DIPs.
    let fontSize = ThemeValue.define "Palette.fontSize" 14.0

/// Fixed conversions from a theme value's type to a property's, for theme value settings (one function each,
/// so two settings of the same theme value compare equal).
module internal ThemeConvert =
    /// A shadow theme value as a `shadow` property value.
    let someShadow: obj -> obj = fun v -> box (ValueSome(unbox<BoxShadow> v))
    /// The same shadow, flat (pressed or disabled): tighter and straight below.
    let flatShadow: obj -> obj =
        fun v ->
            let s = unbox<BoxShadow> v
            box (ValueSome { s with Blur = s.Blur * 0.4; OffsetY = 0.0 })
    /// The same shadow, floating (a flyout or a tooltip above the window): wider and lower.
    let floatShadow: obj -> obj =
        fun v ->
            let s = unbox<BoxShadow> v
            box (ValueSome { s with Blur = s.Blur * 4.0; OffsetY = s.OffsetY * 4.0 })
    /// The same shadow under a modal: wider and lower still.
    let modalShadow: obj -> obj =
        fun v ->
            let s = unbox<BoxShadow> v
            box (ValueSome { s with Blur = s.Blur * 8.0; OffsetY = s.OffsetY * 8.0 })
    /// A number theme value as a uniform corner radius.
    let uniformCorners: obj -> obj = fun v -> box (CornerRadius.Uniform(unbox<float> v))
    /// Half of a number theme value as a uniform corner radius (a CheckBox's box).
    let halfCorners: obj -> obj = fun v -> box (CornerRadius.Uniform(unbox<float> v / 2.0))
    /// A number theme value as the top two corners' radius, the bottom two square (a TabHeader).
    let topCorners: obj -> obj =
        fun v ->
            let r = unbox<float> v
            box { TopLeft = r; TopRight = r; BottomRight = 0.0; BottomLeft = 0.0 }
    /// A colour theme value as a selection's tint (a selected list item's background).
    let selectionTint: obj -> obj = fun v -> box ((unbox<Scribble.Rendering.Color> v).WithAlpha 0.3f)
    /// A colour theme value behind selected text (stronger than a list item's tint: thin letters sit on it).
    let textSelectionTint: obj -> obj = fun v -> box ((unbox<Scribble.Rendering.Color> v).WithAlpha 0.5f)
