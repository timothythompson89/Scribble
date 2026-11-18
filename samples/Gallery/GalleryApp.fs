/// The Gallery: a tour of Scribble (desktop and browser; Program.fs in each host is one line), one page per tab,
/// each keeping its state while another is shown. Each page has its own file next to this one (compiled in this
/// order):
///
/// - Common.fs: what the pages share (the page and section layouts, the theme values and the two themes),
/// - ComponentsPage.fs: an Elmish counter with a command, keyed counters with an animated bar, a slow load
///   (`useElmishWithCmd`, `useState`, `useAnimated`, `useAsync`),
/// - LayoutPage.fs: Grid, DockPanel, Canvas (drag, zIndex), WrapPanel and Separators (M8 step 2),
/// - ControlsPage.fs: CheckBox, Switch, ToggleButton and Slider (M8 step 3a),
/// - ScrollingPage.fs: 10,000 messages in an ItemsView, jumped through with a scroller (M8 step 1),
/// - LayersPage.fs: toasts and a modal dialog (layers, M6),
/// - StylesPage.fs: classes, state blocks and scoped styles (M7),
/// - ImagesPage.fs: ImageView: stretching, pixelated, round, 2×, from pixels, a broken image (M8 step 3b),
/// - GalleryApp.fs (this file): the tabs and the app's settings.
module Scribble.Samples.GalleryApp

open Scribble.Core
open Scribble.Samples.Gallery
open Scribble.Samples.Gallery.Common

/// How a page uses the window's height.
type private Height =
    /// The page scrolls as a whole when the window is short (most pages: sections one under another).
    | Scrolls
    /// The page fills the window and scrolls inside (a `fillingPage`: a list taking the height left), so
    /// scrolling to something in it never moves the page under the pointer.
    | Fills

/// The pages: tab title, the page, and how it uses the height.
let private pages =
    [ "Components", ComponentsPage.view, Scrolls
      "Layout", LayoutPage.view, Scrolls
      "Controls", ControlsPage.view, Scrolls
      "Scrolling", ScrollingPage.view, Fills
      "Layers", LayersPage.view, Scrolls
      "Styles", StylesPage.view, Scrolls
      "Images", ImagesPage.view, Scrolls ]

let private titleOf i = let title, _, _ = pages[i] in title

/// The window title's start; the selected tab's name follows it.
let private appTitle = "Gallery"

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
let gallery =
    Component.define "Gallery" (fun ctx () ->
        // The window's (or browser tab's) title is an app value too; it names the selected page.
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
                    TabControl.onSelectedChanged (fun i -> windowTitle.Set(appTitle + " – " + titleOf i))
                    TabControl.tabs [
                        for title, view, height in pages ->
                            TabItem.create [
                                TabItem.header title
                                // Most pages scroll as a whole when the window is short (a page inside a
                                // StackPanel would get unlimited height and never scroll). The bar sits over the
                                // pages' margin, so a page keeps its width whether or not it scrolls. A filling
                                // page gets the window's height instead.
                                TabItem.content (
                                    match height with
                                    | Scrolls ->
                                        ScrollViewer.create [
                                            ScrollViewer.scrollBarSpace ScrollBarSpace.Overlay
                                            ScrollViewer.content (view ())
                                        ]
                                    | Fills -> view ()
                                )
                            ]
                    ]
                ]
            ]
        ])

/// Components and app values (the theme, the title) hold all the state, so the app has no messages of its own (hence `unit`).
let app: AppSetting<unit> list =
    [ App.title (appTitle + " – " + titleOf 0)
      App.size (720.0, 560.0)
      App.theme darkTheme
      App.view (gallery ())
      // An error from app code (a broken image on the Images page) shows as a toast instead of ending the app.
      App.onError (fun e ->
          System.Console.WriteLine("Gallery: " + string e)
          LayersPage.toast e.Message)
      App.layer [ Layer.name "Toasts"; Layer.view (LayersPage.toasts ()) ]
      App.layer [ Layer.name "Dialog"; Layer.order 200; Layer.modal true; Layer.view (LayersPage.dialog ()) ] ]
