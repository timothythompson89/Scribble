/// The counter sample, a components demo (desktop and browser; Program.fs in each host is one
/// line), on five tabs that keep each page's state while another is shown. Each tab has its own
/// file next to this one (compiled in this order):
///
/// - Common.fs: what the tabs share (the theme tokens and the two themes),
/// - ElmishTab.fs: an Elmish counter inside a component, with a command (`useElmishWithCmd`),
/// - LoadTab.fs: a slow load that restarts when its input changes (`useAsync`),
/// - ListTab.fs: keyed counters with their own state and an animated bar (`useState`, `useAnimated`),
/// - LayersTab.fs: toasts, a modal dialog and a ScrollViewer (layers, M6),
/// - StylesTab.fs: classes, state blocks and scoped styles (M7),
/// - Counter.fs (this file): the tabs and the app's settings.
module Scribble.Samples.Counter

open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Icons
open Scribble.Samples.CounterDemo.Common
open Scribble.Samples.CounterDemo

let private heading (text: string) =
    TextBlock.create [
        TextBlock.text text
        TextBlock.foreground Palette.textDim
        TextBlock.textWrapping TextWrapping.Wrap // onto more lines on a phone
        TextBlock.margin (24.0, 16.0, 24.0, 0.0)
    ]

/// The demo's pages: tab title, then the page.
let private pages =
    [ "Elmish", [ heading "useElmishWithCmd, with a command that adds 1 a second later"; ElmishTab.elmishCounter () ]
      "Load", [ heading "useAsync: a one-second load, cancelled when the number changes"; LoadTab.slowLoad () ]
      "List", [ heading "useState and useAnimated, keyed by name"; ListTab.counters () ]
      "Layers", [ LayersTab.layersPage ]
      "Styles", [ StylesTab.stylesPage () ] ]

/// The window title's start; the selected tab's name follows it.
let private appTitle = "Counter"

/// The selected tab's look: the theme's accent, whichever theme is in use.
let private tabStyles = [ Button.style [ Style.cls "selected" ] [ Button.background Palette.accent; Button.foreground Palette.onAccent ] ]

/// The Light/Dark button: it switches the window's theme (`Theme.current`, an app value) itself,
/// wherever it sits; the whole window follows, the toast and dialog layers too.
let private themeSwitch =
    Component.define "ThemeSwitch" (fun ctx () ->
        let theme = ctx.useAppValue Theme.current
        let isLight = obj.ReferenceEquals(theme.Current, lightTheme)
        Button.create [
            // Material Symbols (Scribble.Icons.Material), in the button's text color.
            Button.icon (if isLight then Material.darkMode.filled else Material.lightMode)
            Button.text (if isLight then "Dark" else "Light")
            Button.padding (10.0, 7.0)
            Button.onClick (fun _ -> theme.Set(if isLight then darkTheme else lightTheme))
        ])

/// Tabs made from what's already there: every page stays in the tree and only the selected one
/// is visible (`isVisible false` is neither measured, drawn, hit nor focused), so each page keeps
/// its state, including a load still running, while another is shown.
let demo =
    Component.define "Demo" (fun ctx () ->
        let selected = ctx.useState 0
        // The window's (or browser tab's) title is an app value too; it names the selected tab.
        let windowTitle = ctx.useAppValue WindowTitle.current
        let select i =
            selected.Set i
            windowTitle.Set(appTitle + " – " + fst pages[i])
        StackPanel.create [
            StackPanel.children [
                // The tabs, and the switch at the right end; on a narrow screen (a phone) the tabs
                // wrap onto more rows and the switch goes below them (Adaptive.fs).
                yield SplitRow.create [
                    SplitRow.gap 4.0
                    SplitRow.wrapMargin (0.0, 8.0, 0.0, 0.0)
                    SplitRow.margin (24.0, 16.0, 24.0, 0.0)
                    SplitRow.styles tabStyles
                    SplitRow.children [
                        WrapPanel.create [
                            WrapPanel.spacing 4.0
                            WrapPanel.lineSpacing 4.0
                            WrapPanel.children [
                                for i, (title, _) in List.indexed pages do
                                    Button.create [
                                        yield Button.text title
                                        yield Button.width 72.0
                                        // The selected tab gets a class; the tab row's styles color it.
                                        if i = selected.Current then yield Button.classes [ "selected" ]
                                        yield Button.onClick (fun _ -> select i)
                                    ]
                            ]
                        ]
                        // Left-aligned when it's on its own row.
                        Border.create [ Border.horizontalAlignment HorizontalAlignment.Left; Border.child (themeSwitch ()) ]
                    ]
                ]
                // Pages by position: the same ones in the same places, so switching only flips
                // isVisible and never rebuilds a page.
                for i, (_, content) in List.indexed pages ->
                    let shown = i = selected.Current
                    StackPanel.create [ StackPanel.isVisible shown; StackPanel.children content ]
            ]
        ])

/// Components and app values (the theme, the title) hold all the state, so the app has no messages of its own (hence `unit`).
let app: AppSetting<unit> list =
    [ App.title (appTitle + " – " + fst pages[0])
      App.size (520.0, 520.0)
      App.theme darkTheme
      App.view (demo ())
      App.layer [ Layer.name "Toasts"; Layer.view (LayersTab.toasts ()) ]
      App.layer [ Layer.name "Dialog"; Layer.order 200; Layer.modal true; Layer.view (LayersTab.dialog ()) ] ]
