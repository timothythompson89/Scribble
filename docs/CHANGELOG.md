# Changelog

What changed in each version of the `Scribble.*` packages, newest first. Every package has the same version.
While the version starts with 0, any release may break the API: each break is listed here under **Changed** or
**Removed**, with what to write instead.

## Unreleased

### Added

- A browser app's default page says so in plain words when it can't run: "This page needs WebGPU" (or HTTPS)
  in the middle of the page, where it used to be black with a red error box. The .NET runtime isn't downloaded
  there. An app with a `wwwroot/index.html` of its own is unchanged.
- **Stored values**: app values kept between runs, of primitive types: `StoredValue.bool "Compact" false`, and
  `StoredValue.int`, `float`, `string` and `enum`. `ctx.useStoredValue`, and
  everything an app value has. Saved to a JSON file in isolated storage on the desktop and to `localStorage` in
  the browser, half a second after the last change and when the window closes. `App.storeName` names the store;
  `App.valueStore` gives one of your own (`IValueStore`, `MemoryValueStore`). A desktop exe grows by about
  0.45 MB (the JSON reader and writer).
- `App.themes [ "Dark", dark; "Light", light ]` names an app's themes, and `Theme.picked` (a built-in stored
  value) holds the name of the one in use: set it to switch, and the app starts in that theme next time. Without
  `App.themes` an app has the built-in pair under "Dark" and "Light". `App.rememberTheme false` keeps nothing.
- `Window.size`: a built-in app value, the window's size in DIPs, kept current as the window is resized.
- `App.rememberWindow true`: a desktop window opens where it was last time (size, position, maximized or not).
  `App.size` is then the first run's size. For platform authors: `WindowOptions.Position` and `.Maximized`, a
  `Moved` event, and `IWindowPlacement` for a window that knows its place.

### Changed

- A desktop window asked for at a size larger than its display is shrunk to the display (it used to open larger
  than the screen).
- For platform authors: `WindowOptions` has two more fields and `PlatformEvent` one more case (`Moved`). Code
  that builds options with `{ WindowOptions.Default with ... }` is unaffected.
- `ItemsLayout.wrapFill minWidth height`, for an ItemsView or a ListBox: as many cells per row as fit at
  `minWidth`, each widened equally so the row fills the width. (`wrap` keeps its cells' width and leaves the
  space over at the right.)

### Fixed

- A `ListBox` with a `wrap` or `grid` layout: the arrow keys follow the rows. Up and Down move by a row and keep
  the column (they moved by one item, so Down went to the item at the right); Left and Right move by one item
  and carry on to the next row (they did nothing); PageUp and PageDown move by the rows that show. A list in
  the default one-column layout is unchanged.

## 0.1.0-alpha.3 (2026-11-20)

### Changed

- **`ListBox` scrolls on its own**: it holds its own ScrollViewer, so it no longer needs one written around it.
  Take the wrapper away and move its sizes to the ListBox:

  ```fsharp
  // before
  ScrollViewer.create [ ScrollViewer.height 240.0; ScrollViewer.content (ListBox.create [ ListBox.source people ]) ]
  // now
  ListBox.create [ ListBox.height 240.0; ListBox.source people ]
  ```

  Code that keeps its wrapper still works and still realizes only the items that show; it has a ScrollViewer more
  than it needs. `ComboBox` is unchanged. An element found as "the ListBox" in the tree is now the outer box: the
  items are two levels further in (ListBox, ScrollViewer, the list, `ListBoxItem`s).
- **`ListBox` has a frame by default**, like a ComboBox's and a TextBox's: the theme's surface behind the items,
  a line, rounded corners, and a padding of 4 inside. A Border written round a ListBox for this can go. For the
  old look (no frame, items to the edge): `ListBox.background Color.Transparent`, `ListBox.borderThickness 0.0`
  and `ListBox.padding 0.0`.

### Added

- `ListBox.background`, `borderBrush`, `borderThickness`, `cornerRadius` and `padding`: they change a
  ListBox's frame or take it away.
- A Debug build warns once of a `ListBox` with more than 200 items that is given unlimited height with no
  ScrollViewer around it (in a StackPanel, say): it is as tall as all its items and realizes every one. Give it
  `ListBox.maxHeight`.

## 0.1.0-alpha.2 (2026-11-20)

### Added

- `ctx.useImage picture`: a component reads where a picture stands (`load.Status`: loading, ready or failed; its
  size in pixels once known; `load.Image` to show it) and can ask again after a failure (`load.Retry()`), for a
  placeholder while a picture downloads or a retry button when it fails. A failure of a picture asked about this
  way comes to the component and no longer to `App.onError`.

### Changed

- Pictures belong to the app, not the process: two apps in one process no longer share decoded pictures, downloads
  or their six download places. Nothing in the public API changed.

## 0.1.0-alpha.1 (2026-11-20)

The first packages. Windows x64 and the browser (WebAssembly, WebGPU); .NET 10; NativeAOT, trimming and
WebAssembly AOT.

### Added

- `Scribble.Desktop` and `Scribble.Browser`: `App.run` with the same app code on each. The desktop package brings
  its native libraries (wgpu-native v29.0.1.1, SDL 3.4.20, accesskit-c 0.23.2, HarfBuzz).
- Views built from settings lists, diffed and patched; MVU (`App.program`) and components with hooks; async;
  commands.
- Layout: StackPanel, Grid, DockPanel, Canvas, WrapPanel; lengths in px, % and star.
- Elements: Border, TextBlock, Label, IconView, ImageView, Separator; Button, ToggleButton, CheckBox, Switch,
  Slider, TabControl, ListBox, RadioGroup and RadioButton, ComboBox, TextBox (one line or several, clipboard, undo,
  input methods, selection by touch), Menu and context menus, Modal, dialogs, toasts, flyouts and tooltips.
- ScrollViewer and a virtualized ItemsView.
- Styles, light and dark themes, theme values, app values, visual states of your own, transitions.
- Text: Inter built in, HarfBuzz shaping, wrapping and trimming; `Scribble.Text.Emoji` (Twemoji) as a fallback.
- Images: a PNG decoder and encoder (`Scribble.Imaging`), files and URLs, image cursors, rendering a view to an
  image.
- Semantics for assistive technology, and a screen-reader bridge on Windows (AccessKit to UI Automation).
- `Scribble.Inspector` (F12) and `Scribble.Icons.Material` (Material Symbols).
