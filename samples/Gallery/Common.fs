/// What the Gallery's pages share: the page and section layouts, the Gallery's own theme values and its two themes.
module Scribble.Samples.Gallery.Common

open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// A page's title and the line on what it shows.
let private header (title: string) (line: string) (settings: Setting<StackPanel> list) =
    StackPanel.create [
        yield StackPanel.spacing 4.0
        yield StackPanel.children [
            TextBlock.create [ TextBlock.text title; TextBlock.fontSize 22.0; TextBlock.font (Fonts.SemiBold()) ]
            TextBlock.create [ TextBlock.text line; TextBlock.foreground Palette.textDim; TextBlock.textWrapping Wrap ]
        ]
        yield! settings
    ]

/// The space around a page's content.
let private pageMargin = Thickness.Create(24.0, 16.0, 24.0, 24.0)

/// A page: its title and a line on what it shows, then its sections. The Gallery scrolls a page that doesn't fit.
let page (title: string) (line: string) (content: View list) =
    StackPanel.create [
        StackPanel.margin pageMargin
        StackPanel.spacing 22.0
        StackPanel.children [
            header title line []
            yield! content
        ]
    ]

/// A page that fills the window instead of scrolling as a whole (the Gallery doesn't wrap it in a ScrollViewer):
/// the title and line at the top, and `content` filling the rest, so a list in it gets whatever height is left
/// and what's above it never moves.
let fillingPage (title: string) (line: string) (content: View) =
    DockPanel.create [
        DockPanel.margin pageMargin
        DockPanel.children [
            header title line [ DockPanel.dock Dock.Top; StackPanel.margin (0.0, 0.0, 0.0, 22.0) ]
            content
        ]
    ]

/// A part of a page: a title, a note on what to try, then the demo.
let section (title: string) (note: string) (content: View) =
    StackPanel.create [
        StackPanel.spacing 6.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text title; TextBlock.fontSize 15.0 ]
            TextBlock.create [ TextBlock.text note; TextBlock.foreground Palette.textDim; TextBlock.fontSize 12.0; TextBlock.textWrapping Wrap ]
            content
        ]
    ]

/// A section whose demo fills the height left (in a `fillingPage`).
let fillingSection (title: string) (note: string) (content: View) =
    DockPanel.create [
        DockPanel.children [
            TextBlock.create [ DockPanel.dock Dock.Top; TextBlock.text title; TextBlock.fontSize 15.0; TextBlock.margin (0.0, 0.0, 0.0, 6.0) ]
            TextBlock.create [
                DockPanel.dock Dock.Top
                TextBlock.text note
                TextBlock.foreground Palette.textDim
                TextBlock.fontSize 12.0
                TextBlock.textWrapping Wrap
                TextBlock.margin (0.0, 0.0, 0.0, 6.0)
            ]
            content
        ]
    ]

// The Gallery's colors beyond the built-in palette, as theme values: settings take them like
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

/// The light theme, with the Gallery's light values.
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

/// The whole Gallery as a view, for the Images page to render as a picture. GalleryApp sets it (`App.value`): the
/// pages are compiled before it, so they can't name it.
let wholeGallery: AppValue<(unit -> View) voption> = AppValue.define "WholeGallery" ValueNone
