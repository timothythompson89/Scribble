# Changelog

What changed in each version of the `Scribble.*` packages, newest first. Every package has the same version.
While the version starts with 0, any release may break the API: each break is listed here under **Changed** or
**Removed**, with what to write instead.

## Unreleased

Nothing yet.

## 0.1.0-alpha.1 (not yet released)

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
