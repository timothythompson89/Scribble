# Changelog

What changed in each version of the `Scribble.*` packages, newest first. Every package has the same version.
While the version starts with 0, any release may break the API: each break is listed here under **Changed** or
**Removed**, with what to write instead.

## Unreleased

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
  than it needs. A ListBox also has a padding of 4 now, so its items sit 4 DIPs in from its edges:
  `ListBox.padding 0.0` gives the old edge-to-edge list. `ComboBox` is unchanged. An element found as "the ListBox" in the tree is now the outer box: the
  items are two levels further in (ListBox, ScrollViewer, the list, `ListBoxItem`s).

### Added

- `ListBox.background`, `borderBrush`, `borderThickness`, `cornerRadius` and `padding`: a frame round a ListBox
  without a Border written around it. No line or background by default; the padding is 4.
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
