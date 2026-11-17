/// The counter sample, a components demo (desktop and browser; Program.fs in each host is one
/// line), on seven tabs that keep each page's state while another is shown. Each tab has its own
/// file next to this one (compiled in this order):
///
/// - Common.fs: what the tabs share (the theme values and the two themes),
/// - ElmishTab.fs: an Elmish counter inside a component, with a command (`useElmishWithCmd`),
/// - LoadTab.fs: a slow load that restarts when its input changes (`useAsync`),
/// - ListTab.fs: keyed counters with their own state and an animated bar (`useState`, `useAnimated`),
/// - LayersTab.fs: toasts, a modal dialog and a ScrollViewer (layers, M6),
/// - StylesTab.fs: classes, state blocks and scoped styles (M7),
/// - LayoutTab.fs: Grid, DockPanel, Canvas (drag, zIndex), WrapPanel and Separators (M8),
/// - ControlsTab.fs: CheckBox, Switch, ToggleButton and Slider (M8 step 3a),
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
      "Styles", [ StylesTab.stylesPage () ]
      "Layout", [ LayoutTab.layoutPage ]
      "Controls", [ ControlsTab.controlsPage () ] ]

/// The window title's start; the selected tab's name follows it.
let private appTitle = "Counter"

/// The Light/Dark switch: it switches the window's theme (`Theme.current`, an app value) itself,
/// wherever it sits; the whole window follows, the toast and dialog layers too. Controlled: it
/// shows the app value, and a click sets it.
let private themeSwitch =
    Component.define "ThemeSwitch" (fun ctx () ->
        let theme = ctx.useAppValue Theme.current
        let isLight = obj.ReferenceEquals(theme.Current, lightTheme)
        Switch.create [
            Switch.text "Light"
            Switch.isChecked isLight
            Switch.onCheckedChanged (fun light -> theme.Set(if light then lightTheme else darkTheme))
        ])

/// The tabs: a TabControl keeping the selection itself (no `selected` setting). Each page is
/// built the first time it's shown and then kept, hidden, with its state (a load still running
/// keeps running); headers that don't fit (a phone) scroll sideways.
let demo =
    Component.define "Demo" (fun ctx () ->
        // The window's (or browser tab's) title is an app value too; it names the selected tab.
        let windowTitle = ctx.useAppValue WindowTitle.current
        DockPanel.create [
            DockPanel.children [
                Border.create [
                    DockPanel.dock Dock.Top
                    Border.horizontalAlignment HorizontalAlignment.Right
                    Border.margin (24.0, 12.0, 24.0, 0.0)
                    Border.child (themeSwitch ())
                ]
                TabControl.create [
                    TabControl.margin (10.0, 0.0, 10.0, 0.0)
                    TabControl.onSelectedChanged (fun i -> windowTitle.Set(appTitle + " – " + fst pages[i]))
                    TabControl.tabs [
                        for title, content in pages ->
                            TabItem.create [
                                TabItem.header title
                                // Each page scrolls as a whole when the window is short (a page inside a
                                // StackPanel would get unlimited height and never scroll). The bar sits over the
                                // pages' margin, so a page keeps its width whether or not it scrolls.
                                TabItem.content (
                                    ScrollViewer.create [
                                        ScrollViewer.scrollBarSpace ScrollBarSpace.Overlay
                                        ScrollViewer.content (StackPanel.create [ StackPanel.children content ])
                                    ]
                                )
                            ]
                    ]
                ]
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
