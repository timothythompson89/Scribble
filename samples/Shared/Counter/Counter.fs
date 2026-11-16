/// The counter sample, a components demo (desktop and browser; Program.fs in each host is one
/// line), on five tabs that keep each page's state while another is shown. Each tab has its own
/// file next to this one (compiled in this order):
///
/// - Common.fs: what the tabs share,
/// - ElmishTab.fs: an Elmish counter inside a component, with a command (`useElmishWithCmd`),
/// - LoadTab.fs: a slow load that restarts when its input changes (`useAsync`),
/// - ListTab.fs: keyed counters with their own state and an animated bar (`useState`, `useAnimated`),
/// - LayersTab.fs: toasts, a modal dialog and a ScrollViewer (layers, M6),
/// - StylesTab.fs: classes, state blocks and scoped styles (M7),
/// - Counter.fs (this file): the tabs and the app's settings.
module Scribble.Samples.Counter

open Scribble.Rendering
open Scribble.Core
open Scribble.Samples.CounterDemo.Common
open Scribble.Samples.CounterDemo

let private heading (text: string) =
    TextBlock.create [ TextBlock.text text; TextBlock.foreground dim; TextBlock.margin (24.0, 16.0, 24.0, 0.0) ]

/// The demo's pages: tab title, then the page.
let private pages =
    [ "Elmish", [ heading "useElmishWithCmd, with a command that adds 1 a second later"; ElmishTab.elmishCounter () ]
      "Load", [ heading "useAsync: a one-second load, cancelled when the number changes"; LoadTab.slowLoad () ]
      "List", [ heading "useState and useAnimated, keyed by name"; ListTab.counters () ]
      "Layers", [ LayersTab.layersPage ]
      "Styles", [ StylesTab.stylesPage () ] ]

let private tabStyles = [ Button.style [ Style.cls "selected" ] [ Button.background (Color.Hex 0x3D59A1) ] ]

/// Tabs made from what's already there: every page stays in the tree and only the selected one
/// is visible (`isVisible false` is neither measured, drawn, hit nor focused), so each page keeps
/// its state, including a load still running, while another is shown.
let demo =
    Component.define "Demo" (fun ctx () ->
        let selected = ctx.useState 0
        StackPanel.create [
            StackPanel.children [
                yield StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 4.0
                    StackPanel.margin (24.0, 16.0, 24.0, 0.0)
                    StackPanel.styles tabStyles
                    StackPanel.children [
                        for i, (title, _) in List.indexed pages ->
                            Button.create [
                                yield Button.text title
                                yield Button.width 80.0
                                // The selected tab gets a class; the tab row's styles color it.
                                if i = selected.Current then yield Button.classes [ "selected" ]
                                yield Button.onClick (fun _ -> selected.Set i)
                            ]
                    ]
                ]
                // Pages by position: the same ones in the same places, so switching only flips
                // isVisible and never rebuilds a page.
                for i, (_, content) in List.indexed pages ->
                    let shown = i = selected.Current
                    StackPanel.create [ StackPanel.isVisible shown; StackPanel.children content ]
            ]
        ])

/// Components hold all the state, so the app has no messages of its own (hence `unit`).
let app: AppSetting<unit> list =
    [ App.title "Counter"
      App.size (520.0, 520.0)
      App.view (demo ())
      App.layer [ Layer.name "Toasts"; Layer.view (LayersTab.toasts ()) ]
      App.layer [ Layer.name "Dialog"; Layer.order 200; Layer.modal true; Layer.view (LayersTab.dialog ()) ] ]
