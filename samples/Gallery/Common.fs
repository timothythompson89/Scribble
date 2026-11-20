/// What the Gallery's pages share: the page and section layouts. Its colours are the built-in palette's.
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

/// The whole Gallery as a view, for the Images page to render as a picture. GalleryApp sets it (`App.value`): the
/// pages are compiled before it, so they can't name it.
let wholeGallery: AppValue<(unit -> View) voption> = AppValue.define "WholeGallery" ValueNone
