/// The Gallery: a tour of Scribble (desktop and browser; Program.fs in each host is one line), one page per tab,
/// each keeping its state while another is shown. Each page has its own file next to this one (compiled in this
/// order):
///
/// - Common.fs: what the pages share (the page and section layouts),
/// - RatingBar.fs: a control of the Gallery's own, written with the public API only (the Controls page shows it),
/// - ComponentsPage.fs: an Elmish counter with a command, keyed counters with an animated bar, a slow load
///   (`useElmishWithCmd`, `useState`, `useAnimated`, `useAsync`),
/// - LayoutPage.fs: Grid, DockPanel, Canvas (drag, zIndex), WrapPanel and Separators (M8 step 2),
/// - ControlsPage.fs: CheckBox, Switch, ToggleButton and Slider (M8 step 3a),
/// - ScrollingPage.fs: 10,000 messages in an ItemsView, jumped through with a scroller (M8 step 1),
/// - ListsPage.fs: ListBox in each selection mode (10,000 items), RadioGroup, a view per item (M8 step 4b),
/// - LayersPage.fs: toasts, dialogs, flyouts and tooltips (M8 step 4),
/// - StylesPage.fs: classes, state blocks and scoped styles (M7), and the one theme value of the Gallery's own,
/// - ImagesPage.fs: ImageView: stretching, pixelated, round, 2×, from pixels, a broken image (M8 step 3b); saving
///   as PNG: a captured card, and the Gallery itself rendered with no window,
/// - GalleryApp.fs (this file): the tabs, the two themes and the app's settings.
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
      "Lists", ListsPage.view, Fills
      "Layers", LayersPage.view, Scrolls
      "Styles", StylesPage.view, Scrolls
      "Images", ImagesPage.view, Scrolls ]

let private titleOf i = let title, _, _ = pages[i] in title

/// The window title's start; the selected tab's name follows it.
let private appTitle = "Gallery"

/// Which build this is, for the end of the title: the version and the commit it was built from, as in
/// " · 0.1.0-alpha.3 (6378526)" (DESIGN.md, "The Gallery on GitHub Pages", decision 6). In the title and not on
/// a page: the pages are in the screenshot tests. Read from this assembly's informational version, which the
/// build ends with "+" and the commit; empty when there is none.
let buildSuffix =
    let attribute =
        System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
            System.Reflection.Assembly.GetExecutingAssembly())
    match attribute with
    | null -> ""
    | a ->
        match a.InformationalVersion.Split('+') with
        | [| version; commit |] when commit.Length >= 7 -> " · " + version + " (" + commit.Substring(0, 7) + ")"
        | parts when parts[0] <> "" -> " · " + parts[0]
        | _ -> ""

/// The title while page `i` shows: "Gallery – Components · 0.1.0-alpha.3 (6378526)".
let private titleFor (i: int) = appTitle + " – " + titleOf i + buildSuffix

/// The Gallery's themes. Dark and Light are the built-in ones; the light one also gives the Styles page's own
/// theme value its light colour (the dark one needs nothing, since a theme value's default is its dark value).
/// Pink is the light theme with its colours changed to a neon pink: every colour a theme has is a setting.
let darkTheme = Theme.dark []
let lightTheme = Theme.light [ Theme.set StylesPage.highlight (Scribble.Rendering.Color.Hex 0x2F5BD3) ]

let pinkTheme =
    let hex = Scribble.Rendering.Color.Hex
    // Neon: hot pink lines and accents on light pinks, with dark plum text.
    let neon = hex 0xFF1FC9
    Theme.light [
        Theme.background (hex 0xFFC7EF)
        Theme.surface (hex 0xFFE3F7)
        Theme.control (hex 0xFFF5FC)
        Theme.border neon
        Theme.text (hex 0x3A0030)
        Theme.textDim (hex 0x9C1A80)
        Theme.accent neon
        Theme.focusRing neon
        Theme.hoverTint (neon.WithAlpha 0.14f)
        Theme.pressTint (neon.WithAlpha 0.26f)
        Theme.scrollThumb (neon.WithAlpha 0.55f)
        Theme.scrollTrack (neon.WithAlpha 0.08f)
        Theme.set StylesPage.highlight neon
    ]

/// The themes by name, as `App.themes` takes them: the first is the one a new user starts in.
let themes = [ "Dark", darkTheme; "Light", lightTheme; "Pink", pinkTheme ]

/// The theme dropdown: it shows and sets `Theme.picked`, the name of the app's theme, a stored value the library
/// defines. The window follows wherever this sits (the toast and dialog layers too), and the Gallery starts in
/// the theme picked last time, with no code here for either.
let private themePicker =
    Component.define "ThemePicker" (fun ctx () ->
        let picked = ctx.useStoredValue Theme.picked
        ComboBox.create [
            ComboBox.width 120.0
            ComboBox.source (themes |> List.map fst |> Array.ofList)
            ComboBox.selected (ValueSome picked.Current)
            ComboBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<string, string>) -> e.Key |> ValueOption.iter picked.Set)
            Scribble.Accessibility.Semantics.label "Theme"
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
                    Border.content (themePicker ())
                ]
                TabControl.create [
                    TabControl.margin (10.0, 0.0, 10.0, 0.0)
                    TabControl.onSelectedChanged (fun i -> windowTitle.Set(titleFor i))
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
    [ App.title (titleFor 0)
      App.size (720.0, 560.0)
      // Emoji typed into a TextBox come from Twemoji: Inter has none, and drew its "missing" box.
      App.fonts [ Scribble.Text.Emoji.Twemoji() ]
      // The themes by name: the one picked is remembered (Theme.picked, a stored value the library defines).
      App.themes themes
      // Stored values are kept under this name on the desktop and in the browser.
      App.storeName "Gallery"
      // The window opens where it was last time (its size, its position, maximized or not).
      App.rememberWindow true
      App.view (gallery ())
      // The Images page renders the Gallery itself into a picture (ViewImage).
      App.value wholeGallery (ValueSome(fun () -> gallery ()))
      // An error from app code (a broken image on the Images page) shows as a toast instead of ending the app.
      App.onError (fun app e ->
          System.Console.WriteLine("Gallery: " + string e)
          app.Toaster.Show [ Toast.severity Severity.Critical; Toast.text e.Message ]) ]
