/// What the Counter demo's tabs share: its own theme values and its two themes.
module Scribble.Samples.CounterDemo.Common

open Scribble.Rendering
open Scribble.Core

// The demo's colors beyond the built-in palette, as theme values: settings take them like
// `Palette.surface` (`Border.borderBrush line`), and each theme gives its own value. The defaults
// are the dark ones.

/// A highlight: hovered cards and their text, selected chips' border.
let highlight = ThemeValue.define "Highlight" (Color.Hex 0x8AB4F8)
/// Error text and a negative counter's bar.
let error = ThemeValue.define "Error" (Color.Hex 0xF7768E)
/// A bar's filled part.
let fill = ThemeValue.define "Fill" (Color.Hex 0x7AA2F7)
/// A bar's empty part.
let track = ThemeValue.define "Track" (Color.Hex 0x2A2A3C)
/// Every other row in a list.
let rowAlt = ThemeValue.define "RowAlt" (Color.Hex 0x2A2A42)
/// Behind something hovered that has no background of its own.
let hover = ThemeValue.define "Hover" (Color.Hex 0x3D3D60)
/// Behind a dialog.
let scrim = ThemeValue.define "Scrim" (Color.Black.WithAlpha 0.55f)
/// Primary and Danger buttons' borders.
let accentBorder = ThemeValue.define "AccentBorder" (Color.Hex 0x5A78C8)
let dangerBorder = ThemeValue.define "DangerBorder" (Color.Hex 0xB04450)
/// A selected chip while hovered.
let accentHover = ThemeValue.define "AccentHover" (Color.Hex 0x4A68B4)
/// Cards' and chips' borders.
let line = ThemeValue.define "Line" (Color.Hex 0x34345A)
/// Behind a hovered chip.
let subtle = ThemeValue.define "Subtle" (Color.Hex 0x2C2C48)
/// Floating things' shadows: toasts, the dialog.
let floatShadow =
    ThemeValue.define "FloatShadow" { Blur = 12.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.5f }
let dialogShadow =
    ThemeValue.define "DialogShadow" { Blur = 24.0; OffsetX = 0.0; OffsetY = 8.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.6f }

/// The dark theme: the theme values' defaults.
let darkTheme = Theme.dark []

/// The light theme, with the demo's light values.
let lightTheme =
    Theme.light [
        Theme.set highlight (Color.Hex 0x2F5BD3)
        Theme.set error (Color.Hex 0xC0392B)
        Theme.set fill (Color.Hex 0x2F5BD3)
        Theme.set track (Color.Hex 0xE1E1EA)
        Theme.set rowAlt (Color.Hex 0xF7F7FA)
        Theme.set hover (Color.Hex 0xE8E8F0)
        Theme.set scrim (Color.Black.WithAlpha 0.3f)
        Theme.set accentBorder (Color.Hex 0x2F4A8C)
        Theme.set dangerBorder (Color.Hex 0x8E2A3A)
        Theme.set line (Color.Hex 0xDCDCE6)
        Theme.set subtle (Color.Hex 0xECECF3)
        Theme.set floatShadow { Blur = 12.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.2f }
        Theme.set dialogShadow { Blur = 24.0; OffsetX = 0.0; OffsetY = 8.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.25f }
    ]
