# Scribble — design doc

Started 2026-11-14 · Tim. **Part 1** (up to "Part 2") describes Scribble as it is; it was rewritten on
2026-11-17 (project review, decision 17) and is kept current at the end of each milestone. **Part 2** is the
history: the first plan, then every decision and milestone in the words of its time (older words are in
GLOSSARY.md: "token" is now "theme value", "marker type" "settings type"). When the two disagree, Part 1 is right.

## Index

Where each milestone's decisions and results are in Part 2. Most sections are in "Decision log and open
questions", as paragraphs starting with the name given here.

| Milestone | What | Where |
| --- | --- | --- |
| M0 | Win32 window, DPI, WebGPU surface | "Decision log" (the table) |
| M0.5 | Browser (WebAssembly), drawing on request | "Decisions from M0.5" |
| M1 | Draw list (SDF shapes, one draw call) | "Decisions from M1" |
| M2, M2b | Text; emoji and fallback | "Decisions from M2", "Decisions from M2b" |
| M3, M3b, M3c | Elements and layout; F#-style elements; lengths | "Decisions for M3", "Learned in M3", "Decisions for M3b", "Decisions for M3c" |
| M4 | Views, the differ, `App.run` | "Decisions for M4" |
| M5 | Pointer, keyboard and focus, components | "Decisions for M5", "Settings API change", "… M5 step 2", "… M5 step 3", "Click arguments" |
| M5b | Async in components | "Decisions for M5b", "Tabs in the sample" |
| M6 | Layers, ScrollViewer, the inspector | "Decisions for M6", "… M6 step 2b", "… M6 step 3" |
| M7, M7b | Styles, themes, app values; icons and cursors | "Decisions for M7", "… M7 step 2", "… step 2b", "… theme tokens", "… app values", "… the window theme/title as an app value", "Decisions for M7b"; sections "Icon as an F# value", "M7b step 2", "M7b step 3" |
| M7c | Custom title bars: skipped | Part 2, "The first plan", Milestones |
| M8 | Core controls | Section "Decisions for M8" (rounds 1–7; steps 1, 2 and 3a as built); sections "M8 step 1b", "M8 step 3a+", "M8 step 3a++", "Recycling measured", "The inspector inside scrolled content", "Scrolling to an item moves the scroll viewers around it only as needed", "M8 step 3b: images, and the Counter becomes the Gallery", "Review of the image pipeline", "M8 step 4: layers and selection controls" (4a flyouts, 4b ListBox and RadioButton, 4c ComboBox, 4d Modal, dialogs and toasts) |
| M9 | TextBox (9a to 9g: one line, clipboard and undo, several lines, input methods, what waited, the Menu, selection by touch) | Section "M9: TextBox" |
| M10 | Hot reload: a dev host that rebuilds the app's project and swaps it in (planned) | Section "M10: hot reload" |
| Semantics | Roles, names, states and actions for assistive technology | Section "The semantics stage" |
| Reviews | Two looks back at the whole project | Sections "Evaluation 2026-11-16", "Project review 2026-11-17" (decisions 1–25 and what was built) |
| Platform | SDL3 for windows and input, AccessKit for screen readers (planned) | Section "Platform layer: SDL3 and AccessKit" |
| Focus | Tab order and reading order are view order; `Dock.Fill` (built 2026-11-18) | Section "Tab order is view order; `Dock.Fill`" |
| Images out | Rendering a view or an element to an image, PNG, saving (built 2026-11-18) | Section "Rendering to an image and saving it" |
| Imaging speed | The PNG decoder's row loops and mip levels with `Vector128` (built 2026-11-18) | Section "Vectorising the PNG decoder" |
| Defaults | Where each element's defaults live: rest looks as type defaults by theme value (2026-11-19) | Section "Where defaults live" |
| Names | An app names and styles a thing by what it wrote; presenters are internal (2026-11-19) | Section "Names an app sees" |
| Style syntax | A selector says which elements, `Style.onState` and `Style.onProp` say when (2026-11-19) | Section "One way to say when" |
| States of your own | `States.define "name" [ ... ]` and `Border.state`; the inspector lists an element's own states (2026-11-19) | Section "States of your own" |
| The Gallery uses the built-in palette | Its own theme values are gone but for one example; `Palette.textDanger` (2026-11-19) | Section "The Gallery uses the built-in palette" |
| Style.onWhere | `Style.onWhere prop test [ ... ]`; `States.where` replaces `States.test`; `States.ofProp` stays (2026-11-19) | Section "Style.onWhere, and where for a condition on values" (its heading quotes the word) |
| Property tests in selectors | `Style.prop` and `Style.where` are for an ancestor's part only; they throw in a style's own selector (2026-11-19) | Section "Style.prop and Style.where are for an ancestor's part only" |
| Controls written outside Core | `ControlValue` and `Prop.appState` are public, `InputContext.Element` too; the Gallery's `RatingBar` is written with the public API only (2026-11-19) | Section "Controls written outside Core" |
| Publishing | The public repository and the NuGet alpha: natives in packages, one version, the consumer test, CI and release workflows (2026-11-19) | Section "Publishing: GitHub and NuGet" |
| Image state per app | The image cache, downloads and HTTP settings move from the process to the app, reached through the root's app values (built 2026-11-20) | Section "Image state per app, not per process" |
| Stored values | App values kept between runs: `StoredValue.bool`/`int`/`float`/`string`/`enum`, `ctx.useStoredValue`; a JSON file in isolated storage on the desktop, localStorage in the browser (built 2026-11-20) | Section "Stored values" |
| Common app values and stored values | What the library defines itself: `App.themes` and `Theme.picked` (the theme remembered), `Window.size` (built 2026-11-20); the active window is agreed and not built | Section "Common app values and stored values" |
| Following the system's theme | `App.systemThemes`, `Theme.system`, `SystemTheme.prefersDark`; `ISystemTheme` and a `SystemThemeChanged` event on the platforms (built 2026-11-20) | Section "Following the system's theme" |
| A remembered window | `App.rememberWindow`: a desktop window's size, position and maximized state stored and restored; `WindowOptions.Position` and `.Maximized`, a `Moved` event, `IWindowPlacement` (built 2026-11-20) | Section "A remembered window" |
| Image hook | `ctx.useImage picture`: a component holds a picture and reads where it stands (loading, ready, failed, retry); built 2026-11-20 | Section "A hook for a picture's load state" |
| ListBox scrolls | A ListBox holds its own ScrollViewer (with the list, `ListBoxItems`, inside): no wrapper to write; a break in 0.1.0-alpha.3 (2026-11-20) | Section "ListBox scrolls on its own" |
| Other | Tooling and platform fixes | Sections "Serving the browser samples", "Phone-friendly samples", "Live resize lag", "Cursor stutter with G-SYNC", "Rendering test hangs", "One entry point and the sample layout", "Scribble.Serve hub"; "Milestone summaries" |

## What Scribble is

A public, open UI framework for F#, written from the GPU up, with a FuncUI-style DSL. It runs on Windows and in
the browser (WebAssembly) today; macOS and Linux come next, then phones, TVs and consoles. Windows is the
development platform.

- **Low level by intent:** rendering, layout, text, styles and controls are our own code. Borrowed: WebGPU
  (wgpu-native on the desktop, the browser's own WebGPU), HarfBuzz (shaping only), and platform integration: SDL3 for
  desktop windows, input and cursors (and later phones and consoles), AccessKit for screen readers (planned). The
  browser's platform is our own code over the DOM. Section "Platform layer: SDL3 and AccessKit", decision 1.
- **One look everywhere**, not native imitation, and **the same pixels on every platform** (decision 13 of the
  project review): screenshot tests compare exactly, and backends must agree.
- **F# only** (no C# projects), and every library works under NativeAOT, trimming and WebAssembly AOT.
- **Frames on request:** nothing draws while nothing changes, and at most one frame per display refresh while
  something does (the power rule).
- **Frame work proportional to what's visible**, not to what exists (tested: virtualization, culling).
- Licence: MIT (`LICENSE`). Packages will be `Scribble.*` (the bare `Scribble` id on NuGet belongs to someone
  else), all at one version, pre-release (`0.1.0-alpha.1` is the first); a desktop package carries its native
  libraries. The repository is public (https://github.com/timothythompson89/Scribble, CI green) and
  `0.1.0-alpha.3` is on nuget.org (2026-11-20, after `alpha.1` and `alpha.2` the same day); a `v*` tag publishes the next (section
  "Publishing: GitHub and NuGet").

## Status

Done and verified by Tim on Windows and in the browser (every checklist item, the last pass 2026-11-18): M0 to M8.
- **M0 to M7b:** window and GPU, browser, draw list, text, elements and layout, views and the differ, input,
  components, async, layers, the inspector, styles, themes, app values, icons, the Material package, image
  cursors. M7c (custom title bars) was skipped.
- **M8, core controls:** ScrollViewer completed and ItemsView with the scroll anchor (steps 1, 1b); Grid,
  DockPanel (with `Dock.Fill`), Canvas, WrapPanel, Separator, zIndex, dashed borders (2); ToggleButton, CheckBox,
  Switch, Slider, TabControl, and the inspector's tree on ItemsView with sideways scrolling (3a, 3a+, 3a++);
  images: the PNG decoder (`Scribble.Imaging`), images on the GPU, the `Image` value with its cache, `ImageView`,
  files and URLs, image cursors, and the Counter sample becoming the Gallery (3b); flyouts and tooltips (4a);
  ListBox, RadioGroup and RadioButton, with Tab order as view order (4b); ComboBox (4c); Modal, dialogs
  (`ctx.useConfirm`, `useAsk`, `useInform`, `useDialog`, `useDialogs`; `Cmd.inform`, `confirm`, `ask`) and each
  window's own toasts (`ctx.useToaster`, `Cmd.toast`, `Toasts.placement`), with `App.onError` getting the app's
  context (4d).
- **Alongside M8:** the project review's steps 1 to 5 (the platform clock, errors to `App.onError`, exact
  pictures, small Core fixes, the inspector split, these docs); the semantics stage (roles, names, states and
  actions for every element type; the inspector's SEMANTICS section); rendering to an image and saving it
  (`ViewImage.render`, `Capture`, `Image.toPng`, `Files.save`); SDL3 as the desktop platform (Win32 is gone).

Done in M9 so far: **step 9a, the single-line TextBox** (section "9a: the single-line TextBox"; checked by Tim on
the desktop, 2026-11-18; its browser checks wait in `docs/CHECKLIST-BROWSER.md`). Also done:
**step 9b, clipboard and undo** (section "9b: clipboard and undo"; checked by Tim, 2026-11-18). And **step 9c,
the multi-line TextBox** (section "9c: the multi-line TextBox"; checked by Tim, 2026-11-18). Between 9c and 9d the
PNG decoder got `Vector128` loops (section "Vectorising the PNG decoder"; a 1024² picture in about 13 ms, from 26).
And **step 9d, IME composition and the phone keyboard** (section "9d: IME composition and the phone keyboard";
checked by Tim on the desktop and on his Android phone, 2026-11-18; no iPhone tried; Japanese draws as
missing-glyph boxes until fonts for other scripts are decided).

And **step 9e** (section "9e: what waited for M9": `submitOnEnter`, typeahead in ListBox and ComboBox, the Label
element, a TextBox's text for screen readers; checked by Tim, 2026-11-18). The NativeAOT Gallery, with all of M9 and
the vectorised PNG decoder in it, was published and walked through by Tim the same day: it passes.

And **step 9f, the Menu** (section "9f: the Menu": Menu and MenuItem, `contextMenu` on any element, the TextBox's
built-in menu; checked by Tim, 2026-11-18).

And **step 9g, selection by touch** (section "9g: selection by touch": a long press or a double-tap selects a
word, selection handles, the context menu as a row; built 2026-11-18, waiting for Tim's check on a phone). It ends
M9's TextBox work.

And **AccessKit step A1, the screen-reader bridge** (section "AccessKit: the screen-reader bridge": a desktop
window's semantic tree through AccessKit to UI Automation, actions back; built 2026-11-19, checked through UI
Automation by script, waiting for Tim's check with Narrator and NVDA).

And **AccessKit step A2, a TextBox's text** (section "A2: a TextBox's text": its lines as text runs, the caret
and selection, and three text actions; built 2026-11-19, checked through UI Automation's text interface by script,
not yet heard through a screen reader).

Next: **M10 hot reload**, planned (section "M10: hot reload"): step M10a is agreed and not built. AccessKit step A3
(announcements and leftovers) comes after it (Tim, 2026-11-19), as does the pass with a screen reader that speaks. SDL3 is the desktop platform and Win32 is gone (2026-11-18, section "Platform layer: SDL3 and AccessKit"). Later: animation, accessibility, bidi and
complex scripts, IME, macOS and Linux, mobile, a Vulkan backend of our own, game UI hosts.

## Projects

| Project | What it is |
| --- | --- |
| `Scribble.Platform` | Windows, events, DPI and time as interfaces (`IPlatform`, `IWindow`, `PlatformEvent`); the frame clock and timer queue |
| `Scribble.Platform.SDL3` | The desktop platform, on SDL3 (3.4.20, downloaded on first build): windows, input, cursors, the draw-on-request loop, live resize, timers. Replaced the hand-written Win32 platform on 2026-11-18 |
| `Scribble.Platform.Browser` | The browser: a full-page canvas, JS interop in pure F# |
| `Scribble.Accessibility.AccessKit` | The screen-reader bridge on the desktop: a window's semantic tree to AccessKit (accesskit-c 0.23.2, downloaded on first build; Windows UI Automation for now) and its actions back; files in compile order: Native, Translation (no native calls: unit-tested), Bridge |
| `Scribble.Rendering` | The draw list, colors, geometry, glyph atlases, the image atlas (pages for decoded images, shared by both GPU backends), `shapes.wgsl` |
| `Scribble.Rendering.WebGPU` | `webgpu.h` bindings and the renderer over wgpu-native (downloaded on first build) |
| `Scribble.Rendering.WebGPU.Browser` | The renderer over the browser's `navigator.gpu` |
| `Scribble.Text` | TrueType parser, rasterizer, text system, line breaking, wrapping and trimming, icon paths; Inter built in |
| `Scribble.Text.HarfBuzz`, `.Browser` | Shaping: native HarfBuzz, harfbuzzjs in WebAssembly (identical output) |
| `Scribble.Text.Emoji` | Opt-in Twemoji colour font (CC-BY 4.0 art: apps that use it must credit it) |
| `Scribble.Imaging` | Image files to decoded images (premultiplied RGBA with mip levels) and back: our own PNG decoder and encoder (zlib from the BCL), the decoder's row loops and the mip levels with `Vector128` and a scalar twin; no GPU, no elements |
| `Scribble.Core` | Views and the differ, elements, layout, input, styles, themes, components, app values, layers, the element types, `App`; folders in compile order: Foundation, Framework, Elements (Basic, Panels, Scrolling, Controls), Themes, Hosting |
| `Scribble.Desktop`, `Scribble.Browser` | `App.run` for each: one reference brings platform, GPU and shaping (and on the desktop the screen-reader bridge) |
| `Scribble.Inspector` | Opt-in devtools-style inspector; files in compile order: InspectorTypes, Format, InspectorState, Highlight, Elements, Views, Inspector (the public type) |
| `Scribble.Icons.Material` | Opt-in Material Symbols (© Google, Apache 2.0); `Material.fs` is generated by `tools/GenerateMaterial.fsx` |
| Samples | Hello (the dashboard), Gallery (a page per tab for everything: Components, Layout, Controls, Scrolling, Lists, Layers, Styles, Images), Counter (the README's first example, one file), Icons (every Material Symbol); each a library with Desktop and Browser hosts (the same one-line Program.fs; host defaults in `samples/Directory.Build.props`); `Samples.Shared` |
| Tests | `Scribble.Tests` (unit, on a fake platform), `Scribble.Tests.Rendering` (GPU screenshots) |
| Tools | `Scribble.Serve` (the browser samples on the local network over HTTPS), `Scribble.Bench` (ItemsView measurements), `GenerateMaterial.fsx`, `MakePngReferences.fsx` (PngSuite through WIC, for the decoder tests), `TestPackages.fsx` (the consumer test: apps built from the packed packages) |

Each layer depends only on those below it; only the `Platform.*` and `Rendering.*` backends hold platform code,
so a new platform means new backends, not changes above them. Native calls use `DllImport` with blittable
signatures (F# can't use the `LibraryImport` generator); browser interop uses `JSFunctionBinding`.

## Platform

- `IPlatform` creates windows, runs the loop and owns time: `Elapsed` (monotonic) and `After(delay, action)`
  (a one-shot timer that fires only when asked). Nothing in Core reads a stopwatch; tests use a fake platform whose
  time moves only when a test says.
- `IWindow`: size in physical pixels, scale, `RequestFrame()`, `SetCursor`, a native handle for the GPU surface,
  and one event union (`PlatformEvent`: size, scale, pointer with kind and id, keys, text, focus, close).
- Windows draw only when asked. On Windows a live resize draws at most once per display refresh; D3D12 presents
  through a DirectComposition visual (smooth cursor with G-SYNC).
- Per-monitor DPI v2; layout in DIPs, rendering in physical pixels.

## Rendering

- Everything is **one instanced draw call per frame**: shapes are 96-byte instances (SDF rounded rectangles,
  borders, dashed and dotted borders, Gaussian shadows, a per-shape pixel clip); glyphs and icons are shape kinds
  that read a coverage atlas (2048²) or a colour atlas (1024²). Edges snap to physical pixels unless `Precise`.
- Backends: wgpu-native on the desktop (D3D12 by default on Windows, then Vulkan or Metal; OpenGL only if nothing
  else gives an adapter), the browser's WebGPU in the browser. Desktop surfaces keep one frame in flight.
- **Same pixels:** the shader avoids built-ins whose precision WGSL leaves open (`atan2` is a polynomial) and
  rounds colours to 8 bits itself; D3D12 and Vulkan agree except 2 pixels in one test picture (a written
  exception).
- Elements outside the clip (plus 64 DIPs for shadows) aren't drawn; a draw list can roll back what a failed
  render drew.

## Text

- Own TrueType parser (cmap 4/12, glyf with composites, COLR v0/CPAL), an accumulation rasterizer with 4
  horizontal subpixel positions and no hinting, glyph caches per size.
- HarfBuzz shapes (cached per string); fallback per grapheme cluster (Twemoji opt-in); line breaking, wrapping,
  ellipsis trimming, alignment.
- Icons are SVG path data drawn through the same rasterizer and atlas, tinted like text.
- Not yet: bidi and complex scripts, IME, editing (M9).

## Core

- **Views and elements.** `X.create [ settings ]` returns a View: an element type and its settings. The element
  tree realizes it once and then patches it setting by setting; children match by position or key; an unchanged
  View is skipped. Properties are typed keys (`Prop<'T>`) that say what they affect (measure, arrange, render);
  an element type is a list of stage functions and defaults (`Element.define`, `Element.extend`); settings are
  static members on settings types, so a setting that doesn't belong to an element doesn't compile.
- **Layout:** WPF-style measure and arrange, rounding to physical pixels, `Length` (auto, px, %, star).
  Panels: StackPanel, Grid, DockPanel (`Dock.Fill` keeps a shell in view order), Canvas, WrapPanel; attached settings (`Grid.row`, `DockPanel.dock`);
  `zIndex`. Every element stretches unless it says otherwise (CheckBox and Switch align left).
- **Scrolling:** ScrollViewer (bars, thumb drag, wheel, keys, touch with fling) and ItemsView (virtualized lists
  and grids over any source, recycling, a scroll anchor by key; scrolling to item 9,000 realizes only what's around
  it).
- **Elements:** Border, TextBlock, Label, IconView, ImageView, Separator, FocusRing, Flyout (and the boxes the host makes for tooltips and toasts); Button, ToggleButton, CheckBox, Switch, Slider,
  TabControl, ListBox (it scrolls on its own), RadioGroup and RadioButton, ComboBox, TextBox (one line or several, with clipboard, undo and input methods; M9 steps 9a to 9d), Label, Menu and MenuItem (as a flyout or a context menu, or a row; 9f), SelectionHandle (a TextBox's, for a finger; 9g), Modal, dialogs, toasts (controls are controlled when the view gives their value,
  else keep their own).
- **Input:** hit testing, routed pointer events (preview, then bubble), capture, clicks, hover and pressed states,
  cursors (also image cursors); keyboard focus, Tab order (view order, as is a screen reader's reading order),
  focus-visible.
- **Styles and themes:** selectors (type, class, name, descendant and child) say which elements, blocks in the
  settings say when (`Style.onState`, `Style.onProp`; section "One way to say when"); precedence own value over
  style over theme; transitions; dark and light themes with theme values (`Palette.surface`); app values that any
  view can read; visual states that apps can define and tools can force.
  An element type's look at rest is its type defaults, colours and shadows by theme value, so a tree without a
  theme shows the dark look at rest; a theme's styles add only the state looks, transitions and class variants
  (section "Where defaults live"; two tests hold the rule).
- **Layers:** the app's view and more (dialogs, toasts, the inspector) over it, each with its own tree and focus.
  An element can ask for a layer of its own above the flyouts (`IElementRoot.ElementLayer`: a TextBox's selection
  handles), which never takes the keyboard.
  Flyouts (`flyout content` or `flyout [ ... ]`: content anchored to an element, light dismiss, flip and clamp) and tooltips each show
  in a layer of their own. Section "M8 step 4".
- **Images:** an `Image` is a value (a key and where it comes from: a resource, bytes, pixels, a file, a URL); decoded
  pictures, their recently-used pool (32 MB), downloads (six at once) and the HTTP client belong to the app: one
  image cache on its app values, shared by its windows and layers; a bare tree has its own, and two apps in one
  process share none. Section "Image state per app, not per process".
- **Errors:** app code (handlers, views, components, render functions, items) is guarded; with `App.onError` an
  error is reported and only what failed is left out; without it the app ends.
- **Semantics** (namespace `Scribble.Accessibility`, seen only after `open`): what each element is to assistive
  technology: a role from ARIA's set, a name (a label, or the text inside a control), a value (a range), states
  and actions, given as settings (`Semantics.label "Close"`) by element types' defaults or by views. The
  semantic tree is built only when asked for (`host.SemanticTree()`); `node.Perform` takes the same paths as
  input. On the desktop a screen-reader bridge carries it to AccessKit (`ScreenReader`, `App.screenReader`); the
  browser has none yet. Sections "The semantics stage" and "AccessKit: the screen-reader bridge".

## Programming model

- **MVU is built in:** `App.program init update view` (Elmish; an equal model doesn't rebuild), with commands
  (`App.programWithCmd`); or a view of **components** with hooks (`useState`, `useEffect`, `useMemo`,
  `useElmish`, `useAsync`, `useImage`, `useAnimated`, `useAppValue`, `useScroller`, `useSubscription`, `useToaster`, `useDialogs`,
  `useInform`, `useConfirm`, `useAsk`, `useDialog`), or both.
- **Settings lists everywhere:** views (`Border.create [ ... ]`), apps (`App.run [ App.title ...; ... ]`), element
  types, themes, hosts. No computation-expression or modifier-chain syntax. Exceptions are listed in "Settings
  everywhere" (Part 2).
- **One UI thread:** a dispatcher brings work back to it; `task` and `async` resume on it.
- **Naming:** a control's value setting is named for what it is (`isChecked`, `value`, `selected`), its change
  event `on<Name>Changed`, and the end of a drag `on<Name>Committed` (decision 8). Settings types and their
  element share a name; `create` and `Type` live in a same-named module.
- **Styles set looks and behaviour, never app state:** no handlers, controlled values, content, flyouts or
  tooltips from a style (it has no model and no idea which element it's on; "M8 step 4", round 1f). Broken, it throws when
  the style or state block is made, saying why (round 1h: not a compile error).

## Tooling and testing

- **Inspector** (opt-in, F12): the element tree of every layer (virtualized), pick mode, a devtools-style
  highlight with margins and Grid lines, properties with where each value came from, forcing states, frame stats;
  docked, floating, collapsed, or popped out into its own window. It refreshes at most about 10 times a second.
- **Tests:** unit tests on a fake platform with fake time (1–2 s; wall-clock tests skipped unless
  `SCRIBBLE_TIMING=1`), and GPU screenshot tests compared exactly with references made on Tim's machine, plus a
  test that renders the same scenes on every backend.
- **Tools:** `Scribble.Serve` (browser samples on the phone), `Scribble.Bench` (ItemsView costs).
- **Packages and CI:** `tools/TestPackages.fsx` (the consumer test) packs every library and builds, runs and
  publishes apps that know Scribble only as packages; `.github/workflows` builds, unit-tests and runs it (without
  starting the apps) on each push, and publishes to NuGet on a `v*` tag. Rendering tests stay on Tim's machine.
- Hot reload is M10 (a dev host with a collectible `AssemblyLoadContext`, never in AOT builds).

## Engineering rules

1. NativeAOT and trimming everywhere: no reflection, `Activator`, emitted code or `dynamic` in shipped code; no
   F# printf family (`printfn`, `sprintf`, `failwithf`, `%A`).
2. Native calls: `DllImport` with blittable signatures, callbacks through `Marshal.GetFunctionPointerForDelegate<'T>`
   with the delegate kept alive, struct layouts checked against the C headers; never `fixed` on a string.
3. Native resources are `IDisposable`; nothing native is freed by a finalizer alone.
4. Platform code only in `Scribble.Platform.*` and `Scribble.Rendering.*` backends.
5. Elements are UI-thread only (checked in Debug builds); shared state between threads lives in holder types
   with plain static fields, never `static let`.
6. No static state keyed on user types; what an app registers will belong to its host (decision 4, for hot reload).
7. Time comes from the platform; app code is guarded; never add frames.
8. Public API gets XML docs; every break goes in `CHANGELOG.md` (started with the first package). No nulls in the
   public API.

## Deferred work

| Item | When | Room left now |
| --- | --- | --- |
| Screen readers | Windows through AccessKit next (section "AccessKit: the screen-reader bridge"); macOS, Linux and the browser's ARIA bridge with those platforms | The semantic tree and its actions; change notifications come with the first bridge |
| Hot reload | M10: planned, section "M10: hot reload" | Pure views, keyed state, no static caches keyed on user types; apps in a library project apart from their exe |
| Bidi and complex scripts | Later | Shaping through HarfBuzz; layout works on runs |
| IME | With TextBox polish | Text input events can carry composition |
| Several windows per app | With the multi-window design (section "4d: Dialog and Toast", "Several windows per app") | Toasts and dialogs are per host and the hooks find their window through the tree; open: commands, `Toasts.placement` and `App.onError` go through app values or the one app context, and nothing blocks every window |
| macOS, Linux, mobile | After M10 | Everything goes through `IPlatform`/`IWindow` |
| Responsive layout | A round of its own (the Gallery wants a sidebar on wide windows, tabs on narrow ones) | The TabControl's header row scrolls sideways; views can't read the window size yet |
| Scroll only the nearest viewer | When an app nests a list in a scrolling page and wants the page to stay put (the browser's `scrollIntoView({ container: "nearest" })`; DESIGN.md, "Scrolling to an item ...", Tim, 2026-11-17) | `BringRectIntoView` already treats the nearest viewer apart from the ones around it; the scroller's requests carry their options |
| Tab stops numbered in the Inspector | When a Tab order is hard to see (section "Tab order is view order; `Dock.Fill`", decision 8) | Tab order is one list per layer (`Input`) |
| A magnifier while dragging a selection handle | When selecting by touch proves hard to aim on a phone (section "9g: selection by touch", decision 90) | A view or an element can be rendered to an image; handles stand in their own layer |
| Density: theme values for metrics (padding, control sizes, disabled opacity, transition times) | When a compact or touch theme is wanted (section "Where defaults live", decisions 5 and 12) | Every metric is a type default or a constant in its element's module; none is in a theme's styles |
| Element types carrying their own state looks | A round of its own (section "Where defaults live", decisions 6 and 12): third-party controls can't add a built-in look, and "text inside a disabled X doesn't fade twice" needs a line per control | Looks at rest are already type defaults; `ThemeLooks` holds only state blocks, transitions and class variants |
| Game UI hosts | Not planned | The draw list stays plain data; the core never assumes it owns the window or the loop; low per-frame allocations |

---

# Part 2: history

## The first plan (2026-11-14, kept as written)

The opening sections as first written, before any code. Much has changed since (Part 1 has the current state):
the projects, LibraryImport (we use DllImport), MVU (built in now), the panels, the milestones.

### Vision and goals

Scribble is a public, open UI framework written in F# from the GPU up, with a declarative DSL in the spirit of Avalonia.FuncUI. It should run anywhere .NET NativeAOT runs: desktops first, then phones, TVs and consoles.

- **Audience:** F# developers building apps; the framework is public, so the API is a product.
- **Platforms:** Windows, macOS and Linux first; Android, iOS, TVs and consoles later. Windows is the development platform.
- **Scope:** UI only. Platform-specific features (notifications, tray icons, file pickers) wait until the core is solid.
- **Low level by intent:** windowing, rendering, layout and text are our own code. We borrow only a GPU abstraction (WebGPU) and a text shaper (HarfBuzz).
- **Look:** one custom Scribble look on every platform, not native imitation.

### Non-goals and deferred work

These are deliberately later, but the design must leave room for each so they can be added without a rewrite.

| Item | When | Room we leave now |
| --- | --- | --- |
| Screen-reader accessibility | After core controls | Every element can expose a role, name and state; a semantic tree can be derived from the element tree |
| Hot reload | After the DSL and components | Pure views, keyed component state, framework and app code in separate assemblies, no static caches keyed on user types |
| Complex scripts (Arabic, Indic) and bidi | After basic text | Shaping goes through HarfBuzz; text layout works on runs, not strings |
| CJK input (IME) | With TextBox polish | Text input events carry composition state, not just characters |
| Animation | After layout and styles | A frame clock exists from Milestone 0 |
| Touch, gamepad and remote input | With mobile, TV and console ports | Routed events and focus support pointer kinds and directional navigation |
| Native platform features | Not planned for v1 | None |

### Architecture and solution layout

Scribble is a stack of portable layers over three swappable backends; porting to a new platform means writing backends, not touching the layers above.

&#91;embedded content: Scribble layers · portable projects above, backends at the bottom\]

Shaded boxes are backends. Each layer depends only on layers below it, and the backends are the only projects with platform-specific code.

```text
Scribble/
├─ src/
│  ├─ Scribble.Platform            window/input/DPI interfaces, events
│  ├─ Scribble.Platform.Win32      Win32 via LibraryImport
│  ├─ Scribble.Rendering           render interface + 2D draw list
│  ├─ Scribble.Rendering.WebGPU    webgpu.h bindings, WGSL shaders
│  ├─ Scribble.Text                fonts, atlas, bidi, line breaking, layout
│  ├─ Scribble.Text.HarfBuzz       shaping backend
│  ├─ Scribble.Core                elements, layout, input, focus, styles
│  ├─ Scribble.Controls            Button, TextBox, Grid, ...
│  ├─ Scribble                     DSL, components, differ
│  └─ Scribble.Inspector           debug overlay
├─ samples/Scribble.Samples.Hello
└─ tests/
   ├─ Scribble.Tests               unit tests
   └─ Scribble.Tests.Rendering     screenshot tests
```

### Platform layer

Win32 comes first, hand-written over P/Invoke, behind a small platform interface that later backends (Cocoa, Wayland/X11, Android, iOS, consoles) implement.

- **Interface:** `IPlatform` creates windows and runs the event loop; `IWindow` exposes size in physical pixels, scale factor, a native handle for the GPU surface, and an event stream.
- **Events:** one discriminated union (`PlatformEvent`) for resize, DPI change, pointer, keyboard, text input, focus and close. The core never sees Win32 messages.
- **DPI:** per-monitor DPI awareness v2 from day one. Layout works in device-independent pixels (DIPs); the renderer works in physical pixels. `WM_DPICHANGED` applies the suggested window rect and re-raises the scale.
- **Pointer model:** events carry a pointer kind (mouse, touch, pen) and id, so multi-touch fits later without new event types.
- **Focus model:** keyboard Tab order now; directional focus (up, down, left, right) designed in for gamepads and TV remotes.
- **Frame loop:** the platform drives frames (vsync through the swap chain); a frame clock gives `delta` and `total` time to the core.

### Rendering

Scribble draws through its own small render interface; the first backend is WebGPU via wgpu-native, and a raw Vulkan backend follows as the low-level path and the route to Switch.

- **Two levels.** A backend-neutral GPU interface (buffers, textures, pipelines, command encoding, surface present) and, above it, a 2D **draw list** that controls and the inspector write to: rects, rounded rects, borders, shadows, images, glyph runs, clips and transforms.
- **Shapes by signed-distance fields.** Rounded rects, borders and shadows are one instanced quad pipeline with SDF shaders, so they stay crisp at any DPI. Arbitrary paths come later.
- **WebGPU specifics.** Own `LibraryImport` bindings over the core `webgpu.h`, no implementation extensions, so wgpu-native can be swapped for Dawn. Shaders in WGSL.
- **Redraw strategy.** Redraw the whole window each frame at first; dirty-region tracking only if profiling demands it.
- **Headless mode.** The renderer can target an offscreen texture and read it back, which powers screenshot tests.
- **Later backends:** Vulkan, then Metal or D3D12 only if WebGPU falls short; console backends only with platform-holder access.

### Text

HarfBuzz does shaping only; everything else in the text stack is Scribble's own code behind interfaces.

| Piece | Owner | Notes |
| --- | --- | --- |
| Font loading and metrics (TrueType/OpenType tables) | Scribble | Parse `cmap`, `hmtx`, `glyf`/`CFF`, `OS/2`, `name` |
| Shaping | HarfBuzz | Thin P/Invoke layer; an `IShaper` interface keeps it swappable |
| Rasterization and glyph atlas | Scribble | CPU coverage rasterizer into a GPU atlas first; SDF or GPU glyphs later |
| Font fallback | Scribble | Per-character fallback chain, emoji font included |
| Bidi (UAX #9) and line breaking (UAX #14) | Scribble | Basic line breaking first; bidi when complex scripts arrive |
| Paragraph layout | Scribble | Wrapping, alignment, trimming, line height |
| Editing | Scribble | Caret, selection, clipboard, undo; IME composition later |

Emoji and basic Unicode are in scope early. HarfBuzz's experimental GPU rendering library (since 14.0) is worth evaluating later, not depending on now.

### Core: elements, layout, input and styles

The core is a retained tree of elements that the DSL's virtual tree is patched into; it owns layout, hit testing, routed events, focus and styling.

- **Elements:** mutable, long-lived nodes with typed properties. Each has a measure/arrange pass, a render pass into the draw list, and an optional semantic description for later accessibility.
- **Layout:** WPF/Avalonia style. `Measure(available)` returns desired size; `Arrange(rect)` places children. Units are DIPs. Panels: StackPanel, Grid, DockPanel later, Canvas later. Layout invalidation is incremental (dirty flags up the tree).
- **Input:** hit testing on arranged bounds and clips; routed events tunnel then bubble (preview and normal), with pointer capture.
- **Focus:** a focus manager handles Tab order now and directional navigation later.
- **Styles:** selectors and cascading styles, as in Avalonia or CSS: type, class, name and state selectors (`:hover`, `:pressed`, `:focus`, `:disabled`), descendant and child combinators. Precedence: inline attribute over style over theme default. Built in stages: inline and a theme record first, selectors once controls exist.

### Programming model

Apps are components with local state, written FuncUI-style as settings lists, checked at compile time so each setting belongs to its element (decided 2026-11-15; this replaced the computation-expression DSL first planned here).

A component (M5 step 3; see "Decisions for M5 step 3"):

```fsharp
let counter = Component.define "Counter" (fun ctx (start: int) ->
    let count = ctx.useState start
    StackPanel.create [
        StackPanel.spacing 8.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"Count: {count.Current}" ]
            Button.create [ Button.text "+1"; Button.onClick (fun _ -> count.Set(count.Current + 1)) ]
        ]
    ])
```

- **Virtual tree:** the DSL builds an immutable view tree; a differ compares it with the previous one and patches the element tree. Children can carry keys so reordered lists move rather than rebuild.
- **Attributes:** each is a key plus a typed setter, so patching needs no reflection. A builder only exposes the attributes its control supports.
- **Event handlers:** never compared; always rebound on patch. This keeps diffing correct when closures are recreated on every render, and suits hot reload.
- **Components:** hooks-style state (`useState`, `useEffect`, `useMemo` and similar), identified by position and key.
- **Effects and async:** `task` and `async` inside components; results resume on the UI thread.
- **Threading:** one UI thread. A dispatcher marshals work from other threads.
- **MVU:** not built in; a thin Elmish-style layer can live on top of components later.

### Tooling and testing

An inspector arrives early because layout bugs are hard to see; hot reload comes later; tests cover the logic and the pixels.

- **Inspector:** a toggleable overlay (F12) that shows the element tree, layout bounds, margins, computed styles and which selector set each value. Later: frame-time graph and draw-call counts.
- **Hot reload (later, desktop only):** a dev host watches source files, recompiles UI code with FSharp.Compiler.Service, loads it into a collectible `AssemblyLoadContext`, re-runs the root component and lets the differ patch. View-only swap first; state serialization later. Never shipped in AOT builds.
- **Unit tests:** differ and patcher, layout (measure/arrange results for each panel), style selector matching and precedence, text line breaking.
- **Screenshot tests:** render scenes headless through WebGPU, compare to reference PNGs with a small tolerance. When the Vulkan backend exists, both backends render the same scenes.
- **IDE:** developed in Rider on Windows; nothing should depend on a particular IDE.

### Engineering rules

Every library must work under NativeAOT and trimming, because iOS and consoles allow no JIT.

1. No reflection, `Activator`, `System.Reflection.Emit`, `dynamic` or runtime code generation in shipped code.
2. Native calls use `DllImport` with blittable signatures only (F# can't use the C#-only `LibraryImport` generator): pointers instead of `ref`/`out`/`string`, sequential structs of primitive fields, callbacks through `Marshal.GetFunctionPointerForDelegate` with the delegate kept alive. Struct layouts are checked against the C headers.
3. Native resources are wrapped in types that implement `IDisposable`; nothing native is freed by a finalizer alone.
4. Platform-specific code lives only in `Scribble.Platform.*` and `Scribble.Rendering.*` backend projects.
5. The UI thread is the only thread that touches elements; violations throw in debug builds.
6. No static state keyed on user types (it breaks hot reload's assembly unloading).
7. Public API gets XML docs; breaking changes are noted in a changelog once the first package ships.
8. Each project targets the latest .NET LTS with `IsAotCompatible` set, so the analyzers flag violations.

Learned from the first NativeAOT build (2026-11-14):

- **No F# printf family in shipped code**: `printfn`, `sprintf`, `failwithf`, `%A`, and `$"..."` strings that use `%d`-style specifiers build formatters through runtime reflection and crash under AOT. Use .NET interpolation (`$"{x}"`, `$"{x:F1}"`) and `Console.WriteLine`; write a literal `%` as a separate plain string.
- **Callbacks use `Marshal.GetFunctionPointerForDelegate<'T>`** (the generic overload), so AOT can generate the marshalling stub for that exact delegate type.
- FSharp.Core itself reports trim/AOT warnings; they are harmless as long as we avoid the features above. The Linux AOT build of the WebGPU backend renders pixel-identically to the JIT build, in a 3.6 MB executable.

### Milestones

Each milestone ends in something runnable on Windows; the counter app working (M4) is the point where Scribble starts to feel like a framework.

M0.5 (browser spike, delivered 2026-11-14) runs the Hello sample in the browser via WebAssembly and moves both platforms to drawing on request.

M1 (draw list) delivered 2026-11-14: verified on a software GPU (all unit, pixel and screenshot tests pass); waiting on a Windows and browser test.

M2 (text) delivered 2026-11-14: 36 unit tests and 18 rendering tests pass on a software GPU; native and WebAssembly HarfBuzz give identical shaping; a NativeAOT build renders pixel-identically to JIT (4.25 MB). Waiting on a Windows and browser test.

M3 (elements and layout) delivered 2026-11-14: new `Scribble.Core` with Element, ElementTree, ElementHost, Border, StackPanel and TextBlock; text wrapping, ellipsis trimming and alignment in `Scribble.Text`. 66 unit tests and 24 rendering tests pass. The sample dashboard is now an element tree and renders with 0 pixels different from the hand-drawn version at 1x and 2x. Release layout: 10,000 elements in 3.5 ms, one change in 0.03 ms. Waiting on a Windows and browser test.

M3b (F#-style elements) delivered 2026-11-14: typed property keys, element types as records of measure/arrange/render functions, settings lists checked at compile time through marker-type inheritance, and an `Element` handle with no nulls. Border, StackPanel, TextBlock and the dashboard were rebuilt on it; the dashboard is still pixel-identical to the hand-drawn version at 1x and 2x. 67 unit and 24 rendering tests pass. Measured at steady state (after .NET swaps in optimized code; the M3 figure of 3.5 ms was a single cold run): 10,000 elements lay out in 0.86 ms, one change takes 0.003 ms. Waiting on a Windows and browser test.

M3c (lengths) delivered 2026-11-15: widths, heights and their min/max take a `Length` (Auto, Px, Percent, Star), a plain number (Px) or a string ("45px", "45", "45%", "2\*", "\*", "auto"). StackPanel shares leftover space among Star children. 79 unit and 24 rendering tests pass; the dashboard is still pixel-identical; 10,000 elements lay out in about 1 ms. Waiting on a Windows and browser test.

M4 (views, differ, App.run) delivered 2026-11-15: `create` returns a View; ElementTree.Update builds elements the first time and patches them after that (by position, or by key), skipping unchanged View objects and re-laying out only what changed. App.program (Elmish), onClick, Button, App.onKey/onFrame/onPointerMove, and the Scribble.Desktop / Scribble.Browser packages with App.run / App.runInBrowser. The Hello sample is now a model, an update and settings; the new Counter sample's Program.fs is one line on each host. The experimental builder syntax draws the counter pixel-identically (panel modifiers go before the block: F# can't chain after a closing brace). 95 unit tests (including the App runtime against a fake platform) and 25 rendering tests pass; the dashboard is still pixel-identical; building, diffing and laying out a 1,000-element view with one change takes about 0.45 ms. Fixed on the way: an update patched in during a frame no longer asks for a second, wasted frame. Waiting on a Windows and browser test.

M5 step 1 (pointer) delivered 2026-11-15: platform events carry modifiers and report the pointer leaving the window (`PointerExited`) and lost presses (`PointerCanceled`: Win32 WM_CAPTURECHANGED/WM_KILLFOCUS, browser `pointercancel`); `IWindow.SetCursor` (Win32 cursors, CSS cursors). OS capture already existed on both. In the core, `InputRouter` (Input.fs) does enter/leave on the chain under the pointer, Preview-then-bubble routing for down/up/wheel, bubbling moves, capture by the pressed element (or by whoever handled the Preview press), clicks, element states (`IsPointerOver`, `IsPressed`, inherited `IsEnabled`) and the cursor; it re-checks after each layout so hover follows things that move under a still pointer. While an element holds the press, only it, its contents and its ancestors count as under the pointer. Button lightens/darkens/fades and shows a hand; disabled TextBlocks dim to 45 percent. `App.onPointerMove` is gone (Hello uses enter/leave on the cards). Fixed on the way: the shared glyph-outline cache in FontFace is now a ConcurrentDictionary (parallel rendering tests raced on the built-in Inter faces). 108 unit tests and 29 rendering tests pass (new reference `buttons@1x.png`, made on Tim's GPU, not lavapipe). Verified by Tim on Windows and in the browser (2026-11-15).

- [x] **M0 — Window and GPU.** Win32 window, per-monitor DPI v2, WebGPU surface clears to a color, clean resize and DPI change, frame clock. Code delivered 2026-11-14; waiting on a Windows test.
- [x] **M1 — Draw list.** SDF quad pipeline: filled and rounded rects, borders, shadows, clips; first screenshot test.
- [x] **M2 — Text basics.** Font parsing, CPU rasterizer and atlas, HarfBuzz shaping, single-line text drawn.
- [x] **M3 — Elements and layout.** Element tree, measure/arrange, Border, StackPanel, TextBlock; unit tests for layout.
- [x] **M4 — DSL and diffing.** Virtual tree, differ and patcher; a trial of a computation-expression syntax on a few elements; an App.run entry point that hides the platform, GPU, text and host wiring (goal: the counter app, main included, fits on one screen); the counter app runs.
- [x] **M5 — Input and components.** Routed pointer events (tunnel and bubble), pointer capture, element states and `isEnabled`, cursors, portable `Key` type and modifiers, focus and Tab order, components with hooks (`useState`, `useMemo`, `useEffect`, `useElmish`), an animate helper, the tactile Button.
- [x] **M5b — Async in components.** UI-thread dispatcher (`SynchronizationContext`); `task`/`async` inside components resume on the UI thread; `useAsync`, `Cmd`, `App.onError` (2026-11-15, verified by Tim on Windows and in the browser).
- [ ] **M6 — Inspector.** Layers above the app (pass-through, modal, per-layer focus), a basic ScrollViewer, and an opt-in `Scribble.Inspector`: tree of all layers, pick mode, highlight, properties with their source, frame stats (decisions agreed 2026-11-15, below).
- [ ] **M7 — Styles.** Theme record, then selectors and cascading styles.
- ~~**M7c — Custom title bars**~~ **Skipped (Tim, 2026-11-16)** after planning it: the plan (our own caption buttons reported to Windows as its own, so snap layouts work) and the alternatives (recoloring the system title bar with `DWMWA_CAPTION_COLOR`; extending the frame so Windows draws its own caption buttons over our content, untested over a wgpu swap chain; the Windows App SDK title bar API, ruled out as a borrowed runtime) were weighed and the feature dropped. The popped-out inspector keeps the system title bar. Original entry, for if it comes back: (added 2026-11-16, Tim; was to come right after M7 step 2b, before M7b). Windows whose title bar is drawn by the app, so the popped-out inspector looks exactly like the docked panel (its header as the title bar, × pops it back in, no maximize). Win32 first: a window option (`titleBar Custom`, default `System`) that removes the system caption but keeps DWM's frame, shadow and Windows 11 corners (`WM_NCCALCSIZE`, `DwmExtendFrameIntoClientArea`), with an inset while maximized; hit testing (`WM_NCHITTEST`) answered from the element tree through a setting that marks regions (`windowPart Caption/Minimize/Maximize/Close`), so dragging, double-click to maximize, Aero snap, Win+arrows, the system menu (Alt+Space, right-click on the caption) and Windows 11's snap-layout flyout on the maximize button keep working; resize edges; DPI changes across monitors. The browser is unaffected (no windows); macOS (content under the traffic lights) and Linux/Wayland (client-side decorations) come with those backends. Names, the exact settings and how the inspector's header doubles as a caption are decided when it's planned. Watch the known black-edge flash when resizing on NVIDIA.
- [ ] **M7b — Icons.** SVG path icons drawn through the glyph rasterizer and atlas; an opt-in Material Symbols package (typed names, "star-filled-rotate90" strings, your own path data), modeled on the CustomAvaloniaControls MaterialIcon design. After styles, so icons can inherit the text color. Open: element and name casing, pluggable icon sets, .svg file import. Also custom cursors (agreed 2026-11-15): a `Cursor.Image` case (bitmap + hotspot, e.g. made from an icon) that the OS shows as a hardware cursor (Win32 `CreateIconIndirect`, re-made per scale on `ScaleChanged`; browser `cursor: url(data:…) x y` / `image-set`). Not drawn in our frame: that lags a frame or two, forces a frame on every mouse move, stops at the window edge and ignores the user's cursor settings. Things that follow the pointer (drag ghosts) are drawn in-frame, with drag and drop.
- [x] **M8 — Core controls.** Grid, DockPanel, Canvas (added 2026-11-16, Tim: children placed with `Canvas.left/top/right/bottom` settings that only the Canvas reads, like WPF's attached `Canvas.Left`; left and right together stretch; replaces the Hello dashboard's `at` margin+alignment helper; names and whether margin still applies decided when M8 is planned), ScrollViewer (the rest: horizontal, draggable thumb, keyboard, touch), CheckBox, Image, Slider, ListBox, ComboBox, Dialog, Toast, the "show this in a layer" element (Popup/portal; also ComboBox's drop-down), TabControl (API, controlled or internal selection, lazy pages, keep-alive policy and arrow keys to be decided; after M7 so the selected tab has a proper look).
  - **Dashed lines in the renderer** (added 2026-11-16, Tim): a dash pattern on the border shape in `shapes.wgsl` (dash, gap, an offset that can animate for "marching ants"), computed per pixel along the outline, so dashes follow rounded corners and a border stays one instance in the one draw call; exposed as a dash option on `DrawList.StrokeRect` and a `Border` setting (CSS `border-style: dashed`/`dotted`; name decided when M8 is planned). For drop targets, selection boxes, dotted focus rings. Chosen over a DrawList helper that draws each dash as a rectangle (square corners only, hundreds of instances on long edges). The inspector's selection outline (now corner marks, which need no dashes) could use it if it ever goes back to a full outline.
- [ ] **M9 — TextBox.** Multi-line layout, caret, selection, clipboard, undo.
- [ ] **M10 — Hot reload.** Dev host with view-only swap.
- [ ] **Later:** animation, accessibility, bidi and complex scripts, IME, macOS and Linux backends, mobile, Vulkan backend.
- [ ] **Later: Scribble as game UI** (noted 2026-11-15, not planned). Godot first (it runs .NET in-process and has a low-level GPU API), as the cheap proof; MonoGame is similar. Then Unreal, through a NativeAOT shared library with a C API: the game passes input in (close to `PlatformEvent`) and takes the draw list out (shape instances plus glyph-atlas updates); an Unreal plugin draws it on Unreal's RHI with `shapes.wgsl` ported to its shader language, so the game never loads the .NET runtime. Embedding .NET in Unreal (UnrealSharp and similar) is the quicker prototype but ties us to a third-party plugin. Hard parts: sharing the GPU (a backend on the host's GPU API is the clean way; shared textures are fiddly; CPU readback only for a prototype), Unreal's game/render threads (run Scribble on the game thread and hand over the draw list), GC stutter, input arbitration between game and UI, and consoles (NativeAOT on consoles isn't publicly supported). **Constraints to keep now so this stays possible:** the draw list stays plain data any backend can consume; the core never assumes it owns the window or the main loop (everything goes through `IPlatform`/`IWindow`); per-frame allocations stay low.

## Decision log and open questions

| Date | Decision | Why |
| --- | --- | --- |
| 2026-11-14 | WebGPU (wgpu-native) as first render backend, behind our own interface; Vulkan later | Every desktop and mobile platform early, far less boilerplate; Vulkan keeps the low-level and Switch path |
| 2026-11-14 | HarfBuzz for shaping only | Industry-standard, portable to consoles; the rest of the text stack stays ours |
| 2026-11-14 | Hand-written Win32 platform layer first | Development happens on Windows; low-level by intent |
| 2026-11-14 | Components with local state as the core; CE DSL with typed attributes | Public API ergonomics and compile-time safety |
| 2026-11-14 | WPF-style layout; selectors and cascading styles | Familiar to .NET developers; theming for a public framework |
| 2026-11-14 | NativeAOT-safe core, no reflection | iOS and consoles forbid JIT |
| 2026-11-14 | Claude writes the code; the project owner tests and steers | Agreed working model |
| 2026-11-15 | FuncUI-style settings lists are the only syntax; the builder trial is removed (replaces "CE DSL" above) | Tim's preference after trying both |

Known issue (M0, 2026-11-14): shrinking a window briefly flashes black at the edge on Windows with Vulkan (NVIDIA RTX 4070 Ti). Direct3D 12 reduces it only slightly, so wgpu's default backend stays; compositor sync, low-latency present and stale-copy prevention made no difference and were removed. `SCRIBBLE_GPU_BACKEND` selects a backend for testing.

Decisions from M0.5 (2026-11-14):

- **Draw only on request.** Windows call `RequestFrame()`; the platform answers with a `Frame` event at the next refresh, after every resize, and when the OS needs a repaint. The Win32 loop sleeps in `WaitMessage` when nothing is due. `IPlatform.Run()` blocks on desktop and returns immediately in the browser.
- **Browser interop in pure F#.** F# can't run the `[JSImport]` generator, so `Scribble.Platform.Browser/Js.fs` calls the `JSFunctionBinding` API that the generator emits; the call shape was checked against the generator's real output. No C# projects.
- **JavaScript ships inside the DLLs.** Each browser library embeds its ES module and imports it as a `data:` URL, so apps copy no files.
- **Browser GPU through `navigator.gpu`,** not Emscripten's `webgpu.h`: no extra toolchain. Cost: a second, small WebGPU backend (`Scribble.Rendering.WebGPU.Browser`).
- **Async device creation in the browser** (`BrowserGpuDevice.CreateAsync`), because the browser answers adapter and device requests only after control returns to its event loop.

Decisions from M1 (2026-11-14):

- **One shader for all shapes, shared by both backends.** `shapes.wgsl` (embedded in Scribble.Rendering) draws fills, inside borders, per-corner radii and Gaussian shadows from a signed-distance field; native wgpu and browser WebGPU compile the same source.
- **One draw call per frame.** Each shape is a 96-byte instance in a storage buffer, read by `instance_index`; clips are a per-shape pixel rectangle tested in the fragment shader, so clipping never splits the batch.
- **Anti-aliased everywhere, snapped by default.** Rect edges, clip edges and border widths round to whole physical pixels (`EdgeMode.Snapped`); animated shapes opt into `Precise`. Transforms are scale + translate only for now, so rects and clips stay axis-aligned.
- **Screenshot tests compare against reference PNGs with a tolerance** (channel difference 6, 0.5% of pixels) because GPUs differ slightly at anti-aliased edges; `SCRIBBLE_UPDATE_SNAPSHOTS=1` accepts new output. References were produced on a software Vulkan GPU (lavapipe).

Decisions from M2 (2026-11-14):

- **Inter ships inside Scribble.Text** as static Regular and SemiBold TrueType instances (wght 400/600, opsz 14), subset to Latin, Greek, Cyrillic and common symbols (\~200 KB each). SIL OFL 1.1.
- **Our own font parser and rasterizer.** head/hhea/maxp/hmtx/loca/glyf, simple and composite glyphs; exact-area accumulation rasterizer (font-rs approach). CFF (.otf) and variable fonts are not read yet. No hinting: glyphs keep their designed shapes and are rasterized at their exact pixel size with 4 horizontal subpixel positions.
- **HarfBuzz for shaping, two backends with identical output.** Desktop: native HarfBuzz via P/Invoke (Windows DLL from the `HarfBuzzSharp.NativeAssets.Win32` NuGet package, found by a library-name resolver). Browser: the official harfbuzzjs `harfbuzz.wasm`, loaded by our own small JavaScript (it needs five trivial imports). Shaping results are in font units and cached per string, so one result serves every size.
- **Glyphs are a shape kind in the same shader.** A 2048² R8 atlas (shelf packing, 1 px padding) is uploaded by dirty region; glyph instances read texels 1:1, so text and shapes still share one draw call and keep drawing order. When the atlas is full it is cleared and refilled (multi-page atlas later).
- **API:** `DrawList.DrawText(textSystem, text, font, size, x, y, color)` with (x, y) the top-left of the line box, and `TextSystem.Measure`. Lines split on '\\n'; wrapping comes with layout (M3). Emoji and font fallback are M2b.

Decisions from M2b (2026-11-14):

- **Emoji font: Twemoji (COLR v0), opt-in** in `Scribble.Text.Emoji` (\~1.4 MB, from the npm package `twemoji-colr-font` 15.0.3). Font under SIL OFL 1.1; art under CC-BY 4.0, so apps must credit it (`Emoji.Attribution`). COLR v0 reuses our outline rasterizer (each layer is a glyph in one palette color); Windows' Segoe UI Emoji uses the same format, so system emoji can come later. COLR v1, CBDT and sbix are not read.
- **Font fallback per grapheme cluster.** Text is split with .NET's extended grapheme clusters, so ZWJ sequences, skin tones, flags and keycaps stay whole. Each cluster uses the requested font if its cmap covers it, else the first fallback that does; clusters with emoji presentation (U+FE0F or emoji ranges) prefer a color font. Neighbouring clusters with the same font are shaped together.
- **Color glyphs:** composited on the CPU into premultiplied RGBA and stored in a second, 1024² RGBA8 atlas; a third shape kind in the same shader samples it, so emoji still share the one draw call. Text color tints only their alpha; foreground-color layers are drawn black.
- Verified: cmap and COLR/CPAL parsing match fontTools on 109 lookups; 43 unit and 21 rendering tests pass; NativeAOT build with emoji has no warnings and renders identically to JIT (5.8 MB).

Decisions for M3 (agreed 2026-11-14, before implementation):

- **Elements:** a WPF-style class hierarchy (`Element` with overridable measure, arrange and render) and plain typed properties whose setters say what they invalidate (measure, arrange or render). No dependency-property registry; the property type is shaped so M7 can add a style layer under local values.
- **Layout:** two passes, `Measure(available)` → desired size (infinity allowed) and `Arrange(rect)`. Every element has Margin, Width/Height, Min/Max and horizontal/vertical alignment (Stretch, Start, Center, End); containers have Padding. Layout rounding on by default: positions and sizes snap to whole physical pixels at the window's scale.
- **Invalidation:** incremental layout (dirty flags up the tree, measure cached per available size); drawing stays redraw-all, but only on requested frames.
- **M3 elements:** Border (background, border, corner radius, padding, shadow, one child), StackPanel (orientation, spacing), TextBlock (font, size, color, alignment, wrapping, ellipsis trimming, max lines), ClipToBounds on every element. Grid, ScrollViewer and the rest stay in M8; group opacity waits for offscreen rendering.
- **Line breaking:** simplified rules (spaces, after hyphens, between CJK characters, mid-word as a last resort) behind an interface, so UAX #14 can replace it.
- **Host:** a new `Scribble.Core` project with an `ElementHost` joining window, render device, text system and root element; it schedules layout and drawing and handles resize and DPI. Same code on Win32 and in the browser.
- **Before the DSL (M4):** trees are built with plain F# object syntax; the dashboard is rebuilt from elements and must look the same as the hand-drawn version.
- **Rules:** UI-thread-only access (checked in debug builds); every element has an optional Name and lists its children (for the inspector). Targets: full layout of 10,000 elements under 5 ms; a single change re-lays out only its subtree.
- **Icons:** deferred until path rendering exists; then SVG icons rather than an icon font.

Learned in M3:

- The base element's child list is `VisualChildren` (as in WPF); `Children` is the editable collection on panels. F# can't have both under one name.
- Layout rounding uses round-half-up, not .NET's default banker's rounding, so half-pixel positions snap the same way everywhere.
- Border thickness is one value for all sides for now; per-side borders need a draw-list change.
- Open: `Scribble.Core.Size` overlaps `Scribble.Platform.DipSize`, and both share field names with `PixelSize`, so record literals sometimes need a type annotation. Consolidate before the M4 DSL.

Decisions for M3b (F#-style elements), 2026-11-14. Reviewing M3, the element classes read like C# translated to F#; we keep the fast mutable engine but hide it behind F# values:

- Properties are typed keys (`Prop<'T>`) that declare what they affect (measure, arrange, render); the engine handles change detection and invalidation for all of them.
- An element type is a record of functions (measure, arrange, render) with stage-specific contexts, not a subclass. Panels are measure/arrange functions.
- Elements are created from lists of settings written as functions, FuncUI style: `Border.create [ Border.background cardFill; Border.child (...) ]`.
- Settings are checked at compile time through inheritance, as in FuncUI, so shared settings such as margin appear under every element (`TextBlock.margin`).
- No `null` in the public API; `Element` is an opaque handle. `el.Set(prop, value)` covers hover and animation until components (M5).
- Lists are the first syntax. Every syntax compiles to the same settings, so a modifier-chain computation-expression flavor (Fabulous v2 style) gets a trial on a few elements in M4.
- This rework includes the settings layer; M4 becomes the differ plus the counter app.

Decisions for M3c (lengths), 2026-11-15:

- One `Length` type for width, height, min and max: Auto, Px (DIPs, like CSS px), Percent, Star. Grid will reuse it for columns and rows.
- Strings parse to the same values: "45px" or "45", "45%", "2\*" or "\*", "auto". A bad string throws when the element is created.
- Percent is of the space the parent offers, margin included (as in CSS); in unlimited space it acts like Auto. Allowed for min and max too.
- Star is opt-in. StackPanel children are content-sized along the stacking direction unless they ask for a Star share; then they split the leftover by weight, but only when the panel's length is known. In unlimited space Star quietly falls back to content size (the inspector can flag it later). Other parents treat Star as stretch.
- This goes beyond WPF's StackPanel (closer to flexbox grow). DockPanel joins Grid in M8 for docking layouts.

Decisions for M4 (differ and App.run), 2026-11-15:

- `create` returns a `View` (type + settings + children, cheap to make); Scribble builds and patches real elements from it. Same element type in the same place is patched setting by setting; a different type is rebuilt. An unchanged View object is skipped entirely.
- Children match by position by default. An optional `key` setting (needed only for data-driven lists that insert, remove or reorder) matches by key instead; a `Panel.items list key view` helper takes the key from the collection.
- State in M4 is Elmish-style: `App.program init update view`. Several messages in one frame cause one rebuild; an unchanged model causes none. Components with local state arrive in M5 on top of this.
- Minimal input in M4: an `onClick` setting (press and release on the same element), a simple `Button`, `App.onKey`, `App.onFrame` and a temporary `App.onPointerMove` (for the dashboard's hover until M5).
- `App.run` (desktop) and `App.runInBrowser` hide the platform, GPU, text and host wiring, in two packages, Scribble.Desktop and Scribble.Browser. App settings use the same settings-list style as elements.
- A small trial of the modifier-chain computation-expression syntax on StackPanel, Border and TextBlock, with the counter written both ways.
- Element types must be defined once (as static values): the differ recognizes a type by reference.

Decisions for M5 (input and components), agreed 2026-11-15, before implementation:

- **Routing: tunnel then bubble, as the architecture planned.** The route (target up to the root) is computed once, walked down for preview handlers and then up for normal ones; a handler can mark the event handled to stop it. Preview settings exist only where interception is needed (`onPreviewPointerDown`, `onPreviewPointerUp`, `onPreviewWheel`, `onPreviewKeyDown`), for the inspector (M6) and ScrollViewer (M8). Enter and leave don't route: they fire on each element that joins or leaves the hovered chain.
- **Pointer capture:** the element that gets a press captures the pointer until release, also at the OS level so a release outside the window still arrives. Dragging off a button and releasing cancels the click. Replaces `App.onPointerMove`.
- **Platform additions:** pointer-left-window, OS capture, modifier keys on pointer and key events, a cursor shape per element (`Layoutable.cursor`).
- **Element states:** the core tracks hover, pressed, focused and disabled. Built-in elements (Button) react to them in their own rendering, and element authors can read them. Views don't read states directly: app code uses enter/leave events or component state; declarative state-dependent settings are M7 styles (`:hover` and the rest).
- **`isEnabled` now,** inherited down the tree: disabled elements get no pointer or keyboard input and are skipped by focus.
- **Keys:** a portable `Key` type of logical keys (letters, digits, named keys, `Key.Other` as an escape hatch) plus modifiers; the browser moves from `keyCode` to `e.key`. Physical scan codes may come later as a second field.
- **Focus:** a focus manager built around "move focus in a direction", with Tab and Shift+Tab as the first two (arrows for gamepads and remotes later). The focus ring shows only after keyboard navigation (like `:focus-visible`); Enter and Space activate.
- **Components:** hooks are the core, as planned. `component "name" (fun ctx -> view)` is defined once at module level (identity by reference, like element types); state is identified by position and key. M5 hooks: `useState`, `useMemo`, `useEffect` (dependencies, cleanup on removal) and `useElmish init update` on top of `useState` (the thin MVU layer). A state change re-runs only that component and patches only its subtree, on the next frame. Debug builds check that hooks are called in the same order every render.
- **Async is M5b:** a UI-thread dispatcher and `task`/`async` inside components.
- **Animation helper:** eased tweens (duration plus easing) on the frame clock, for visual-only element properties and as a `useAnimated` hook; springs later.
- **Tactile Button:** included. A visual-only scale around the center (layout and hit testing unaffected); depth and timing hardcoded until M7.
- **Syntax:** decided 2026-11-15, see below: FuncUI-style settings lists only.
- **Order:** pointer, then keyboard and focus, then components and animation; each step is tried on Windows and in the browser before the next.

Settings API change (agreed 2026-11-15, during M5 step 1): `create` moved from a static member on each marker type to a function in a module of the same name (`module Button = let create settings = ...`, same file as the type). Static members are inherited, so `Button.create [ Button.width 100.0 ]` (only generic settings) was ambiguous between Button's and Border's `create`, and M8's deeper hierarchies (CheckBox : ToggleButton : Button : Border) would have made it common. Modules aren't inherited; app code is unchanged, and `create` is now an ordinary function (`List.map TextBlock.create`). Checked first in a script: an extension member is just as ambiguous. Lowercase functions (`button [ ... ]`) were the alternative, left for the syntax decision after M5.

Decisions for M5 step 2 (keyboard and focus), agreed 2026-11-15:

- **Key:** a DU of logical keys in Scribble.Platform (aliased in Core): `Char of char` (printable keys as the layout gives them, letters lowercase, so Ctrl+S is `Char 's'` + Control on any layout), named keys (Space, Enter, Escape, Tab, Backspace, Delete, Insert, arrows, Home/End/PageUp/PageDown, `F of int`, Shift/Control/Alt/Meta/CapsLock) and `Other of string`. `KeyDown`/`KeyUp` carry key, modifiers and repeat. Win32 maps virtual keys through the active layout (`MapVirtualKeyW`); the browser uses `e.key`. App's raw-code `Key` module is gone.
- **Element key events:** `onKeyDown`, `onKeyUp`, `onPreviewKeyDown`, `onPreviewKeyUp`, routed like pointer events from the focused element (the root when nothing is focused). `onTextInput` waits for M9.
- **App.onKey runs last,** only for keys nothing handled (so a focused button's Space doesn't also hit the app's Space shortcut); `closeOnEscape` likewise. Signature `App.onKey (fun key mods -> ...)`; repeats filtered.
- **Focusable:** a `focusable` setting (default false; Button's type defaults make it true). `onClick` alone doesn't make an element focusable.
- **Pointer and focus:** a press focuses the nearest focusable element under it (no ring) or clears focus; disabling, hiding or removing the focused element clears it.
- **Tab order:** view order, wrapping; built as `Move(direction)` with Next/Previous now, arrows later. Focusing from code later; a tab index is not planned (section "Tab order is view order; `Dock.Fill`", 2026-11-18).
- **Focus ring: elements draw it** (`ctx.IsFocusVisible` and a shared `FocusRing.draw` helper; Button follows its corners): 2 DIPs, accent color, 2 DIPs outside. Shown after Tab, hidden on the next pointer press and while the window is inactive.
- **Activation:** Enter clicks on key down; Space shows pressed while held and clicks on release; Escape while Space is held cancels; repeats never click. Built-in key behavior (Tab, Enter, Space) runs after the route, so handling the key in an element handler suppresses it.

M5 step 2 (keyboard and focus) delivered 2026-11-15: `Key` and the new `KeyDown`/`KeyUp` shapes in Scribble.Platform (aliased in Core). Win32 maps named virtual keys directly and everything else through `MapVirtualKeyW` (so letters follow the layout); the browser sends `e.key` through the text channel and modifiers in the record. In the core, `RoutedEventArgs` is the new base (`IsHandled`, `Handle`, `Modifiers`), `InputEventArgs` adds positions, `KeyEventArgs` adds `Key` and `IsRepeat`. `InputRouter` gained the focus manager (`MoveFocus Next/Previous` over focusable, visible, enabled elements in view order, wrapping; press focuses or clears; invalid focus cleared in the after-layout refresh; ring state follows Tab, presses and window activation), key routing from the focused element (else the root), and built-ins after the route (Tab, Enter, Space press/release, Escape cancel). Elements keep `IsFocused`/`IsFocusVisible` and a key-pressed flag that feeds `IsPressed`. `FocusRing.draw` (Elements/FocusRing.fs) draws the ring; Button uses it and is focusable by default. `ElementHost.HandleEvent` returns whether a key was handled; `App.onKey` is `Key -> Modifiers -> 'Msg option` and runs only for unhandled, non-repeat keys (closeOnEscape too). 119 unit tests (8 new keyboard tests) and 30 rendering tests pass (new reference `button-focus@1x.png`, made on Tim's GPU). Verified by Tim on Windows and in the browser (2026-11-15).

Decisions for M5 step 3 (components and animation), agreed 2026-11-15:

- **Defining:** `let counter = Component.define "Counter" (fun ctx (start: int) -> view)` returns a function `'P -> View`, used as `counter 0` (no props: `fun ctx () -> ...`, used as `counter ()`). Defined once at module level (identity by reference). Component views are ordinary Views, so `Panel.items` keys them.
- **Re-running:** a component re-runs on the next frame (before layout) when its own state changes, patching only its subtree; when its parent re-runs, only if its props changed (F# equality; props holding functions always re-run). Setting an equal value does nothing; setting state while the component runs throws.
- **State:** `ctx.useState 0` returns an object with `.Current`, `.Set v` and `.Update f` (Update avoids stale reads in handlers).
- **Dependencies:** one value (usually a tuple) compared with F# equality and passed in: `ctx.useEffect((id, page), fun (id, page) -> ...)`, `ctx.useEffect((), ...)` runs once. `ctx.useSubscription(deps, fun d -> IDisposable)` disposes on change and on removal. `ctx.useMemo(deps, fun d -> ...)`. Effects run after the frame that rendered the component is drawn; state they set asks for another frame.
- **useElmish:** `let model, dispatch = ctx.useElmish(init, update)` on top of useState; no commands until M5b. `App.program` stays as is.
- **Animation, two layers:** `ctx.Animate(key, target, duration, ?easing)` in render functions (cheap: no view re-runs; Button uses it) and `ctx.useAnimated(target, duration, ?easing)` in components (re-runs the component each frame while moving). `float` and `Color`; easings Linear, EaseIn, EaseOut, EaseInOut as plain `float -> float` functions. Springs later.
- **Visual-only transform:** a new element part `Element.transform (fun ctx -> Transform)`, applied around the element and its children when drawing; layout and hit testing unaffected. A public `renderScale` setting waits. Text drawn under an extra scale rasterizes at quarter-pixel sizes so a tween adds few atlas entries.
- **Tactile Button:** scales to 97% around its center while pressed (about 120 ms), plus the existing darkening, and gains a subtle shadow (1 DIP down, 3 DIP blur) that flattens on press. Reference images with buttons are regenerated.
- **Checks:** hooks must run in the same order every time (the error names the component); state set from another thread throws (dispatcher in M5b).
- **Sample:** the Counter samples (desktop and browser) become the components demo: several keyed counters (add, remove, reorder), a `useAnimated` bar, a `useElmish` panel, tactile buttons. Hello stays the `App.program` example.

M5 step 3 (components and animation) delivered 2026-11-15, verified by Tim on Windows and in the browser: `Component.define` (Component.fs) returns `'P -> View`; the view carries one setting of a new kind (`PropKind.Component`) holding the function and props, compared by F# equality, so an equal input doesn't re-run. The element that hosts a component keeps its `ComponentInstance` (hook slots, checked for order and count) and patches the component's output in as its only child. State changes schedule the component through `IElementRoot.ScheduleComponent`; the tree runs scheduled components shallowest first at the start of `UpdateLayout`. Effects, subscriptions and "still animating" requests go through `IElementRoot.AfterFrame`, which `ElementHost` runs after each frame, outside the in-frame window, so the changes they make ask for the next frame. Removing a component's element disposes its subscriptions. Animation.fs has `Easing` and the internal `Tween` (first target taken at once; a new target starts from the current value); `ctx.Animate` on render contexts and `ctx.useAnimated` in components use it, and the tree's `Time` (set by the host each frame) drives both. New element part `Element.transform` and `Transform.ScaleAround`; Button sinks to 97% over 120 ms and gains a shadow (1 DIP down, 3 DIP blur, 35% black) that flattens when pressed or disabled. `Border.draw` takes the shadow to draw. Text under an extra scale rasterizes at quarter-pixel sizes. The Counter samples became the components demo (useElmish counter, keyed list of counters with animated bars). 133 unit tests (14 new) and 31 rendering tests pass; references regenerated on Tim's GPU: `buttons@1x`, `counter@1x`, `hello@1x`, new `counter-demo@1x`. Found on the way: an app made only of components has no message type, so its settings need `AppSetting<unit> list` (open item).

Click arguments (agreed 2026-11-15, after M5): `onClick` takes `ClickEventArgs -> unit` like every other handler: `e.Modifiers` (held at release for a pointer, with the key for Enter/Space, so Shift-click and Shift+Enter work) and `e.Source` (`ClickSource.Pointer kind` or `ClickSource.Keyboard`). Clicks stay unrouted. Call sites write `fun _ -> ...`. Overloading `onClick` for both `unit -> unit` and `ClickEventArgs -> unit` was rejected after a check in F# Interactive: `fun _ ->` and `fun e -> e.Modifiers` fail to resolve (FS0041/FS0072).

Decisions for M5b (async in components), agreed 2026-11-15:

- **Dispatcher:** the queue lives in Core and is the same everywhere (`Post`, `InvokeAsync`, `CheckAccess`, `RunPending`); a platform only provides "wake up and drain" (Win32: a hidden message-only window that receives `PostMessage`, because thread messages are dropped during the move/resize modal loop; browser: `setTimeout(0)` calling back into .NET). A game host can call `RunPending()` itself each tick. A Scribble `SynchronizationContext` on the UI thread makes `task { }` and `await` resume there.
- **Cross-thread state:** `State.Set`/`Update`, Elmish `dispatch` (components and `App.program`) queue themselves onto the UI thread when called from another thread. Elements stay UI-thread only (debug check); reading `.Current` off the UI thread isn't supported.
- **useAsync:** `ctx.useAsync(deps, fun d ct -> task)` returns `AsyncValue.Loading | Loaded v | Failed e`; starts after the frame; a change of `deps` or removal cancels the token and ignores any late result; reloading shows Loading again. An F# `async` overload if F# resolves it (checked in F# Interactive), else `Async.StartAsTask`.
- **Commands:** our own small `Cmd` (`none`, `ofMsg`, `batch`, `map`, `ofTask`, `ofAsync`, `ofSub`); new forms `App.programWithCmd` and `ctx.useElmishWithCmd` where `init` and `update` return `'Model * Cmd<'Msg>`; the plain forms stay. No Elmish package dependency.
- **Errors:** `useAsync` failures become `Failed e`; exceptions from posted work and commands go to `App.onError` (default: write to the console, keep running). Unobserved task exceptions are routed there too.
- **Left for later:** timers/`useInterval`, keeping the old value while reloading, progress reporting, multi-threaded WebAssembly.
- **Sample:** the components demo gains a useAsync section (fake one-second load, changing the number cancels) and a "+1 later" `Cmd.ofAsync` button on the Elmish counter. Hello unchanged.
- **Tests:** the fake platform's wake is drained by hand; background threads posting state, continuations on the UI thread, cancellation and late results, errors to onError, Cmd order. Wake during window drag/resize and the browser are checked by hand.

M5b delivered 2026-11-15 (verified by Tim on Windows and in the browser): `IPlatform` gains `SetWakeHandler` and `Wake` (Win32: a message-only window, `HWND_MESSAGE`, receiving `WM_SCRIBBLE_WAKE` via `PostMessageW`; browser: `wake()` in scribble-platform.js, coalesced `setTimeout(0)` calling back into .NET). Dispatcher.fs: `Dispatcher` (a `ConcurrentQueue`; one wake per burst via an interlocked flag; failures go to `OnError` and the queue keeps running), `DispatcherSynchronizationContext`, and `dispatcher.Install()` (sync context plus `TaskScheduler.UnobservedTaskException` routed to `OnError`), called by `App.run`/`App.runInBrowser`. `IElementRoot.Dispatcher` lets components reach it; `State.Set`/`Update` and dispatch post when off the UI thread (with no dispatcher, as in bare `ElementTree` tests, they run inline as before). Cmd.fs: `Cmd<'Msg>` is a list of `('Msg -> unit) -> unit` effects; `Cmd.ofTask`/`ofAsync` map success and failure to messages. `AsyncValue`, `ctx.useAsync` (Task and Async overloads; a version counter ignores late results, a `CancellationTokenSource` per load; both are bumped/cancelled as soon as the component runs with new deps, not when the new load starts), `ctx.useElmishWithCmd` (init command runs after the first frame), `App.programWithCmd`, `App.onError`. The Counter sample: "+1 in a second" (`Cmd.ofAsync`) and a useAsync row (n², one second, cancelled by − and +); window 520×680. 147 unit tests (12 new, AsyncTests.fs) and 31 rendering tests pass; `counter@1x` (now 480 wide) and `counter-demo@1x` (now 520×680) regenerated on Tim's GPU.

Tabs in the sample (agreed 2026-11-15, after M5b): the Counter demo is on three tabs (Elmish, Load, List), built as an app-level component from existing pieces: every page stays in the tree and only the selected one has `isVisible true` (hidden elements are not measured, drawn, hit-tested or focused, and lose focus), so each page keeps its component state; pages keep their positions, so switching only patches `isVisible`. Hidden pages keep running (a load on Load finishes while another tab is shown). Window 520×520. `TabsTests.fs` (the sample linked into the unit tests) checks visibility, layout and state across switches; `counter-demo@1x` regenerated on Tim's GPU. A library `TabControl` goes to M8.

Element definitions, settings-style (agreed 2026-11-15, after M5 step 1): an element type is written as a list, like a view: `Element.define "ProgressBar" [ Element.measure ...; Element.render ... ]`, or `Element.extend Border.Type "Button" [ ... ]` to start from another type. Parts left out behave like a plain container. `Type` moved from a static member on the settings type into the element's module, next to `create` (inherited statics again). New: **per-type defaults** (`Element.defaults [ ... ]`, plain values only): an element reads its own value, else its type's default, else the property's; a setting removed from a later view returns to the type's default. Button's look moved there, so Button views no longer carry seven prepended settings; M7 themes will sit alongside this layer. Every setting keeps the generic `<'k when 'k :> X>` form (one pattern, even for elements nobody extends). The `ElementType` record stays as the stored form (now with `Defaults`); `ElementType.Default` became internal. Pixel output unchanged.

Syntax decided (2026-11-15, Tim): **FuncUI-style settings lists** (`Button.create [ Button.text "+"; Button.onClick f ]`) are the one syntax. The experimental builder syntax from M4 (`Scribble.Core.Builders`, `VStack(...) { ... }`) and the `CounterBuilders` sample were removed. This replaces the earlier plan for a computation-expression DSL (decision log, 2026-11-14): compile-time checking of which settings belong to which element comes from the marker types instead.

Settings everywhere (Tim, 2026-11-15, after M5b): the settings-list style is the house style for **any** API an end user interacts with, not only views. New public APIs default to it (`Thing.create [ Thing.option v ]`, settings as static members on the type they configure, like `App.title` and `Border.background`) instead of records with `{ Default with ... }`, optional arguments or builders; when it doesn't fit, say why in the design discussion. Internal code is free to use anything. Decided the same day (Tim agreed to all recommendations):

1. **`App.program init update view` stays positional** (and `App.programWithCmd`). Separate `App.init`/`App.update`/`App.view` settings can't tie the model type together (a settings list only carries `'Msg`; adding `'Model` to every setting makes the `AppSetting<unit>` problem worse), and the triple is the Elmish convention. It's already one setting in the list.
2. **GPU devices take settings:** `WebGpuDevice.Create [ WebGpuDevice.backend BackendType.D3D12; WebGpuDevice.powerPreference GpuPower.LowPower; WebGpuDevice.onError f ]` and `BrowserGpuDevice.CreateAsync [ BrowserGpuDevice.powerPreference ... ]`; `WebGpuOptions` became internal. `GpuPower` (Default, LowPower, HighPerformance) lives in Scribble.Rendering so both backends share it. Choosing the backend from `App.run` would need Core to know WebGPU: later (`SCRIBBLE_GPU_BACKEND` stays). The settings sit on the device type (like `App.title`), not on a separate `WebGpu` type as first sketched.
3. **`WindowOptions` and `IPlatform` stay records/interfaces:** they're the contract between Scribble and a platform implementation, not app configuration. Rule: settings for configuring things, plain types for implementing them.
4. **`ElementHost.create window device text [ ElementHost.background c; ElementHost.dispatcher d; ElementHost.beforeFrame f ]`** for custom hosts (game engines); the constructor is internal. `Background` stays settable at run time; `Dispatcher` and `BeforeFrame` are set only through settings.
   - **`TextSystem.create device shaper [ TextSystem.fallbacks [ ... ] ]`** (Tim asked the same day): replaces the constructors with optional fallbacks; the atlas-based constructors are internal (tests, via InternalsVisibleTo). `Fallbacks` stays settable while running. `App.fonts` passes its list through.
5. **Component props stay `'P -> View`** (a typed value compared by F# equality to skip re-runs). Settings lists hold functions, which never compare equal, so components taking settings need their own comparison story: a later design item, if third-party reusable components should take settings. Controls Scribble ships are elements, which already do.
6. **Hooks, `Cmd` and `ctx.Animate` stay calls** (`ctx.useAsync(deps, f)`, `Cmd.ofTask ...`): code inside your code, not configuration.

Still to keep in mind for new APIs: font registration, TabControl, Grid definitions, M7 styles, M7b icons.

Decisions for M6 (inspector), agreed 2026-11-15:

- **Scope:** a tree of every layer's elements (type, Name, a hint such as a TextBlock's text; components by name; hidden elements dimmed; collapsible); pick mode (hover highlights, click selects, the click doesn't reach the app); a devtools-style highlight (bounds, margin, size and position in DIPs); a properties panel for the selected element (every property with a value and its source: set by the view, type default or property default; desired size, arranged bounds, offered space; hovered/pressed/focused/enabled; a component's hook values); frame stats (layout and render ms, shapes). Later: live editing, the M7 "which style set this" column, a frame-time graph.
- **A floating panel in the app's window**, docked right (switchable to left), drawn over the app: the app's layout never changes while it's inspected. Works on desktop, in the browser and in game hosts. A separate window was rejected (desktop only, needs multi-window).
- **Built from Scribble's own elements and components** (dogfooding), not drawn by hand.
- **Layers in Core** (widened from "one overlay" after discussing dialogs and toasts): an ordered stack of element trees above the app, each with its own focus scope and two options: *pass-through* (the pointer reaches lower layers wherever this layer has nothing) and *modal* (everything below is blocked, keyboard included). Order: app, popups/toasts, modal dialogs, inspector on top. When the pointer is over a layer's content, the layers below see it leave. Public (`App.overlay`-style settings), because the inspector is a separate package and because M8's drop-downs, dialogs and toasts use the same machinery. The inspector shows every layer.
- **`Scribble.Inspector` is a separate, opt-in project:** `Inspector.enable [ Inspector.side Left ]` in the app's settings, built on the public layer setting. The samples enable it; the README shows `#if DEBUG` for leaving it out of release builds.
- **A basic public `ScrollViewer` comes forward from M8:** vertical, mouse wheel, clipping, a thin non-draggable scrollbar. M8 adds horizontal scrolling, a draggable thumb, keyboard and touch.
- **Keys:** F12 toggles the inspector, Ctrl+Shift+C enters pick mode; configurable (`Inspector.toggleKey`). Whether the browser lets us keep F12 from opening its own developer tools is checked in step 2; if not, the browser gets a different default.
- **Values are formatted by our own code**, never by `ToString()`/`string` on F# records or unions (`%A` underneath, which crashes under NativeAOT): known types (numbers, strings, Color, Thickness, Length, enums, Views, handlers as "handler") get proper text, anything else shows its type name.
- **Fast with big trees:** rows only for expanded nodes (by default just the path to the selection), refreshed after app frames at most about 10 times a second; the Hello dashboard must stay smooth with the inspector open (measured).
- **Steps, each tried by Tim before the next:** (1) layers (pass-through, modal blocking, per-layer focus) and the basic ScrollViewer, with unit and screenshot tests; (2) the inspector panel, tree, pick mode and highlight, F12 checked on desktop and in the browser, samples enable it; (3) the properties panel, frame stats, the AOT-safe formatter and the dashboard performance check.
- **Left for M8:** the element that puts view content into a layer (WPF's Popup, React's portal), `Dialog` (backdrop, focus trap, Escape, focus restore) and `Toast` (placement, stacking, auto-dismiss), next to ComboBox's drop-down.

M6 step 1 delivered 2026-11-15, verified by Tim on Windows and in the browser: `Layer` (Layer.fs; settings `Layer.name`, `Layer.order` (default 100; the app is 0), `Layer.modal`, `Layer.view`), `ElementHost.AddLayer`/`RemoveLayer`/`Layers`/`App`/`KeyboardLayer`, `App.layer [ ... ]`. Each layer has its own `ElementTree` and `InputRouter` (so its own focus); the host owns which layer gets each pointer (`LayerPointer`: over, capture until all buttons are up) and the keyboard. Settled while building:
- **"Pass-through" and "modal" became one switch**, `Layer.modal`: a non-modal layer always lets the pointer through where it has nothing, so a separate pass-through flag had nothing left to mean.
- **A modal layer blocks only while it shows something** (`ElementTree.HasVisibleContent`): an empty or hidden dialog layer can stay in place without freezing the app. Likewise a layer that shows nothing doesn't keep the keyboard, so closing a dialog hands keys back to the app.
- **A component's host element isn't hit itself**, only through what the component shows (it fills its slot but draws nothing); otherwise a component as a layer's root would cover the window.
- **A press keeps its layer until release**; dragged over another layer, the holding layer's router sees the pointer over nothing (`InputRouter.IsCovered`), so releasing there clicks nothing. A pointer that stands still is reassigned after layout when a layer appears or goes (`InputRouter.PointerAt`: hover and cursor update, no move event); only the layer that has the pointer sets the cursor.
- **Keyboard:** keys go to the layer last pressed in, unless a modal layer showing something is above it. Layers that don't have the keyboard get `WindowFocusChanged false` (their ring hides, a held Space is dropped) and `true` when they get it back.
- **ScrollViewer** (Elements/ScrollViewer.fs): vertical, 48 DIPs per wheel notch, clamped to the ends (also after resizes), clips (`clipToBounds` type default), a 4-DIP position bar at 30% white; at an end it leaves the wheel unhandled so an outer scroller or handler gets it. Two new element parts for it: `Element.wheel` (a type's own wheel behavior, run as the event bubbles after each element's `onWheel`, with an `InputContext`: Get, SetLocal, Size, InvalidateArrange, InvalidateVisual) and `Element.renderAfter` (drawn over the children, outside their clip).
- The Counter demo gained a fourth tab, Layers: a 40-row ScrollViewer, a toast layer and a modal dialog layer, driven by F# events until M8's portal element.
Tests: 165 unit tests (15 new in LayerTests.fs: pointer fall-through, hover moving between layers, a layer appearing under a still pointer, capture across layers, modal blocking pointer and keys, focus per layer, order, removal stopping components, ScrollViewer scrolling/ends/hit testing/shrinking, empty modal layers, component hosts, the keyboard returning after a dialog) and 32 rendering tests (new `layers-scroll@1x`; `counter-demo@1x` regenerated for the fourth tab, both on Tim's GPU).

M6 step 2 delivered 2026-11-15, verified by Tim on Windows and in the browser: `src/Scribble.Inspector` (Inspector.fs). `Inspector.enable [ Inspector.side DockSide.Left; toggleKey; pickKey; isOpen ]` (an `AppSetting`), or `Inspector.attach (host, settings)` for custom hosts; all four samples enable it. Settled while building:
- **Two layers on top:** the panel (order `Int32.MaxValue`) and below it the highlight (`MaxValue - 1`), whose full-window root only takes the pointer in pick mode (new `Layoutable.isHitTestVisible`, default true: false makes the pointer go through an element and everything inside it). Picking hit tests the other layers top-down itself, so the click never reaches the app. The inspector doesn't list its own layers.
- **Hooks into Core, all public and general:** `App.onStart (fun host -> ...)` (runs once the host exists, after `App.layer`s), `ElementHost.AddKeyPreview` (sees every key before any layer, whatever has focus; true stops it), `ElementHost.LayerChanged` (any invalidation of a layer, also during a frame) and `ElementHost.FrameDrawn`, `Element.ComponentName`, and `IWindow.ClaimKey(key, modifiers)`: the browser prevents its default for that combination (F12 and Ctrl+Shift+C stop opening the browser's tools), Win32 does nothing (no menus, so no system shortcuts to suppress).
- **The highlight is an element that reads its target's position while drawing** (every frame renders the whole tree), so it's exact even between tree refreshes: a blue box, margins in orange (four bands), a label with the type and size above (or below) it.
- **Refresh:** the inspector marks itself dirty when any other layer changes and refreshes after the next frame, at most every 100 ms (a timer posts to the dispatcher for the rest); its own changes never mark it dirty, so an idle app stays idle with the inspector open. Rows are made only for expanded elements; roots start expanded, selecting (by row or pick) expands the path to it.
- **Rows:** a triangle (drawn: Inter has no triangle glyphs) toggles, a press elsewhere selects, hovering highlights; labels are the type or component name, `#Name`, and a TextBlock's text (or the text of a single TextBlock child, like a Button's) clipped to 28 characters. All text is made by our own code (`Format`), no `string` on unions.
- Pick mode selects the deepest element (a Button's TextBlock, as in browser devtools); its row's parent is one click up.
Tests: 170 unit tests (5 new in InspectorTests.fs: F12 toggles without moving the app, keys claimed and kept from layers, pick selects without the click reaching the app, Escape cancels, the panel takes only its area) and 33 rendering tests (new `inspector-pick@1x`: the Counter demo after picking "+", checked by eye; on Tim's GPU).

Decisions for M6 step 2b (inspector panel modes), agreed 2026-11-15 after Tim found the docked panel covering most of the 520-wide Counter window (the inspector is the top layer, so its area blocks the app):
- **Four panel modes sharing one state** (selection, expanded rows, scroll position survive switching): docked right, docked left, floating, popped out (desktop). Highlight and pick mode always work in the app's window.
- **Undocking:** dragging the title area makes the panel float; the Left/Right buttons dock it again. A floating panel is kept inside the window (also after resizes). No snapping to edges for now.
- **Resizing:** a grip at a floating panel's bottom-right corner; a docked panel's inner edge can be dragged to change its width.
- **Collapsing:** a "–" button or a double-click on the title area shrinks the panel to its title bar.
- **Pop-out (desktop only):** a second OS window ("Inspector – <app title>"), same thread, GPU device and text system; F12/Ctrl+Shift+C work in both windows; closing it returns the panel to where it was; closing the app closes it too. The browser hides the button (a real popup there needs its own page, canvas and WebGPU context: later).
- **Core for the pop-out:** `App.onStart` handlers take an `AppContext` (host, platform, title); `ElementHost.Closed` (plus `Close()`, `Device`, `Text`); `IPlatform.SupportsMultipleWindows`. `Inspector.attach` takes `Inspector.platform` to offer the pop-out in custom hosts.
- **Not now:** remembering mode, position and size between runs (no storage API yet).

M6 step 2b delivered 2026-11-15, verified by Tim on Windows and in the browser, as agreed above. Last addition: the pop-out is an owned window (`WindowOptions.Owner`; Win32 passes the owner's handle to CreateWindowExW), so it stays above the app after clicks there, minimizes with it and has no taskbar button; `ElementHost.Close()` now raises `Closed` just before the window goes, so owned windows close while their owner still exists. Notes from building it:
- `InspectorState` holds the mode (`PanelMode.Docked | Floating | PoppedOut`), docked width, floating rectangle and a `Drag` (kind, start point, start rectangle); the views only read it. Drags use pointer capture: the title area, the grip and the docked edge each handle down/move/up, and moves keep coming to them wherever the pointer goes. The title area holds only the title (the buttons are its siblings), because moves bubble from the pressed element.
- A docked panel comes off after 4 DIPs of movement (a click doesn't undock it), keeping its width and taking 60% of the window's height (at least 160). Two presses on the title within 400 ms and 4 DIPs fold it. Floating rectangles are clamped when stored and again when shown, so a window resize brings the panel back in. Minimum size 300 × 160.
- The pop-out is a second `ElementHost` on a window from `ctx.Platform`, sharing the app host's device, text system and dispatcher, showing the same panel component with `IsPoppedOut = true` (no drag, no fold, "Pop in" instead of the dock buttons). Its close button, "Pop in" and F12 all go through `PopIn`; `host.Closed` (raised by `ElementHost.Close()`, which App.start now uses for both the close button and `closeOnEscape`) closes it with the app.
- New: `Cursor.ResizeDiagonal` (Win32 `IDC_SIZENWSE`, browser `nwse-resize`), for the grip.
- Fixed after Tim's first try: undocking mid-drag rebuilt the title element (docked, the content sat in a row with the resize edge; floating, it didn't), so pointer capture was lost and the drag only continued while the pointer was back over the new title. The panel now has one structure in every mode (left edge, content, right edge; the edges hidden when unused), and the tests draw frames between drag moves, which is how they missed it. General lesson: an element holding the pointer must survive the rebuilds its own drag causes; changing the shape of a view mid-drag loses capture.
- Drag lag (Tim, 2026-11-15: the dragged panel trails the mouse), agreed fixes:
  - **Frame latency 1 by default on the desktop** (`WebGpuDevice.frameLatency n`, wgpu-native's `WGPUSurfaceConfigurationExtras.desiredMaximumFrameLatency`; wgpu's default is 2). Expected to save a frame of lag; **Tim saw no difference**, and tracing showed why: each frame lays out first and then blocks ~6 of ~7 ms getting the swap-chain image, so input is nearly a frame old whatever the latency. Tried next: waiting for the refresh before taking input (`DwmFlush` in the Win32 loop, then pumping messages, then drawing); it cut the measured block in one run, but Tim felt no difference either, and automated runs were unreliable (windows behind Rider never drew; frame intervals varied between ~7 and ~10 ms, perhaps a variable-refresh monitor). **Left as is (2026-11-15, Tim: "it doesn't feel that terrible")**; the experiment was removed, frame latency 1 kept (harmless, and the usual recommendation). If drag lag comes back as a problem: measure with a visible window first, compare with other apps (DWM composition adds a frame or two to every windowed app relative to the hardware cursor), then look at frame pacing (waitable swap chains, presenting late in the refresh). CPU and GPU no longer overlap, which only matters for heavy frames (ours are one draw call). The browser has no equivalent: its compositor decides.
  - **Measured, not changed:** a drag re-runs the whole panel component, but on the Hello dashboard that adds about 0.1–0.2 ms per frame (37 rows: 0.52 → 0.71 ms median CPU per frame; 61 rows: 0.26 → 0.33 ms; fake platform, Release, after warm-up). Not worth a special path.
  - **Rejected for now:** Mailbox presentation. It shows the newest frame without waiting for vsync, so animations would draw as fast as the GPU allows unless we paced frames ourselves.
  - What remains is about one frame (16.7 ms at 60 Hz) behind the OS pointer, inherent to drawing anything that follows the mouse. The pop-out window, moved by Windows itself, has none.
- **Rule (power):** frame pacing changes must keep "no frames while idle, at most one per display refresh while something changes". Draw on request is the main power saving; latency settings must not add frames (frame latency 1 doesn't: it changes when a frame is shown, not how many are drawn).
Tests: 176 unit tests (6 new: undocking by drag and the app getting the uncovered area back, clamping, double-click folding, the docked edge, the corner grip and docking again, and the pop-out on a fake platform with several windows: title, panel moving out and back, closing with the app) and 34 rendering tests (new `inspector-floating@1x`; `inspector-pick@1x` regenerated for the new header buttons; both on Tim's GPU, checked by eye).

Decisions for M6 step 3 (properties panel), agreed 2026-11-16:

- **Layout:** the properties sit below the tree, split by a draggable divider (devtools style; tabs were the alternative). Header: type and #name; then states (hovered, pressed, focused, enabled, visible), layout (offered, desired, arranged bounds, position in the window), the properties, and a component's hooks. Nothing selected: "Click a row or pick an element". Footer: frame stats.
- **Which properties:** everything with a value, tagged with its source: set by the view, set by code (`element.Set`), type default. Also the layout properties every element has, at their property default, dimmed. A **"Show defaults" checkbox** (off by default, kept while the app runs; Tim's idea) hides or shows the property defaults; type defaults always show, since they give the element its look. `Prop.local` values are hidden; child properties show as a count. Listing every property a type *could* have (an unset `fontSize`) needs a per-type property list: left for M7, where styles need one too.
- **The checkbox** is drawn by the inspector itself (private, like its chevrons), until M8's CheckBox.
- **New public read-only API in Core:** `element.Values` (property, value, source), `element.OfferedSize` (the space last offered to measure), `element.Hooks` (kind and current value: useState/useMemo/useAnimated their value, useElmish its model, useAsync Loading/Loaded/Failed, effects and subscriptions just their name). Public rather than internal so other tools and tests can use them. *Hooks pass, 2026-11-19:* every hook is listed under the name the component called it by. The ones that keep an object (`useScroller`, `useCapture`, `useClipboard`, `useToaster`, `useDialogs`, and `useConfirm`/`useAsk`/`useInform`, which kept a `useDialogs` slot) all read "useMemo", being kept in memo slots; they have a slot type of their own now (`HandleSlot`, name only, no value). `useDialog` takes one slot, not two. A slot's name (`Kind`) is apart from what the inspector shows (`Label`: `useAppValue CompactMode`), and the hook-order check compares the name as well as the slot's type, so `useState 0` swapped with `useElmish` on an int model is caught.
- **Formatter (NativeAOT-safe):** hand-written text for numbers, strings, bools, enums, Color (hex), Thickness, CornerRadius, Length, BoxShadow, Size/Rect/Point, Scribble's own unions (matched case by case: `ToString()` on an F# union is `%A`), options/voptions/lists (count plus the first few), views (type name), handlers ("handler"); anything else is its type name. `Inspector.format (fun (v: obj) -> string voption)` lets an app format its own types (an Elmish model). Long values end with "…".
- **Frame stats without extra frames:** the stats line only changes together with a refresh caused by the app's layers (at most 10 a second), never on the inspector's own frames, or showing the stats would make new stats forever. An idle app stays idle with the inspector open. The numbers include the inspector's share of the frame (labelled so); frames/s counts frames actually drawn in the last second (0 while idle).
- **Refresh:** the selected element's values refresh with the tree (at most ~10/s, only while the app changes).
- **Performance:** a permanent test that an idle app with the inspector open and an element selected draws no frames; a one-off steady-state measurement of the animating Hello dashboard with and without the inspector, recorded here. More than ~0.5 ms extra per frame needs work.
- **Not in step 3:** live editing, the "which style set this" column, a frame-time graph.

M6 step 3 delivered 2026-11-16, verified by Tim on Windows and in the browser (M6 is complete), as agreed above. Tim noticed the browser using about 4% CPU against 1.5% on the desktop while Hello animates: expected for now (the debug browser build runs .NET on the WebAssembly interpreter, and every frame crosses into JavaScript for WebGPU); a wasm AOT build should close most of the gap. Notes from building it:
- Core: `Element.Values` (`PropertyValue` = prop, value, `ValueSource.View | Code | TypeDefault | Default`), `Element.OfferedSize`, `Element.Hooks` (`HookValue` = kind, value, status). `Prop.local` properties are marked (`IsLocal`) and left out; `LayoutProps.common` is the list shown at its defaults. A view setting equal to the current value isn't stored on the element, so the source is decided by asking the view first for every entry (found by a test: Button's `horizontalAlignment Left` showed as "type"). Each hook slot reports its value (`HookSlot.Inspect`); useElmish's state slot is now named "useElmish".
- Inspector: the panel is header, tree, divider (`DragKind.Split`; the properties keep at least 60 DIPs and leave 170 for the header, footer and some tree in the app's window), properties (a `ScrollViewer`), footer with the stats. The selection line moved from the footer into the properties. "Show defaults" is a Button holding a private `CheckMark` element. Property names are shown as settings are written (`horizontalAlignment`).
- Formatter (`Format.value`): hand-written cases for Scribble's types and plain values; handler voptions matched by their concrete types (a boxed generic voption can't be read without reflection, which trimming would break); any `FSharpFunc` is "handler"; `IEnumerable` shows count and three items; anything else `typeName` (F# names: `voption<Color>`). Inter has no ∞, so unlimited sizes read "any".
- Stats: `FrameDrawn` records the last stats every frame, but the text is only rebuilt in a refresh. A frame counts as the app's when one of its layers changed before it. After an app-driven refresh, a check 1.1 s later refreshes once more if the app has drawn nothing for a second ("app idle"); that refresh's frame is the inspector's own and schedules nothing.
- **Measured** (temporary test, removed; animating Hello dashboard at 960×600, a card selected, per frame: view update, layout of every layer, draw list; best of 7 × 200 frames after warm-up):

  | | Release | Debug |
  | --- | --- | --- |
  | No inspector | 0.088 ms | 0.264 ms |
  | Inspector open, card selected | 0.107 ms | 0.475 ms |
  | Same, panel refreshed every frame (really ≤10/s) | 0.173 ms | 0.688 ms |

  Well within the 0.5 ms budget in Release (+0.02 ms per frame, +0.085 ms on a refresh frame). Shapes: 447 without, 889 with the panel.
- Tests: 182 unit tests (6 new: value sources, hook values, the formatter and `Inspector.format`, the properties and "Show defaults" in the panel, no frames while idle with a selection, the divider); the two inspector screenshots were regenerated (34 rendering tests).

Decisions for M7 (styles), agreed 2026-11-16:

- **Steps, each tried by Tim before the next:** (1) the styles engine: classes, selectors, scopes, precedence, re-matching on state changes, the inspector's style source; (2) themes: `Theme.dark`/`Theme.light`, the built-in looks (Button, TextBlock, ScrollViewer, focus ring) as theme styles, transitions for appearance properties, inherited text properties, a theme switch in the Counter demo.
- **Syntax (Tim chose A + D + E + F of six options, 2026-11-16):**
  - A, typed selector lists: `Button.style [ Style.cls "primary"; States.hover ] [ Button.background accent ]` (as changed later the same day; see "States and classes simplified" below). Each element module has a one-line `style` next to `create`, so a style only takes its type's settings. `Style.any [ ... ] [ Layoutable.margin 4.0 ]` matches every element. Combinators: `Style.inside Border.Type [ Style.cls "card" ]` (any ancestor), `Style.parent Border.Type [ ... ]` (the direct parent).
  - D, nested state blocks: `Style.on States.hover [ Button.background light ]` inside a style's settings (a typed setting itself; blocks nest, so states combine).
  - E, classes were defined values (`Class.define`); since replaced by plain strings (below).
  - F, state settings inline in a view: `Button.on States.hover [ Button.background light ]`, a style scoped to that one element (one block per state on an element; a later one replaces it).
  - Rejected: a left-to-right pipeline (B; reads like CSS but isn't a settings list), CSS strings (C; no compile-time checks).
- **States:** `States.hover`, `pressed`, `focused`, `focusVisible`, `disabled` (they are selector parts themselves). Element authors add their own with `States.define "checked" (fun e -> ...)` and call `element.RefreshStates()` when it may have changed (M8's CheckBox, ListBox).
- **A type selector matches exactly that type** (`Border.style` doesn't touch Buttons), as in Avalonia.
- **Scopes:** `App.styles [ ... ]` (every layer, unless `Layer.appStyles false`, which the inspector uses so an app's styles don't restyle it); a `styles` setting on any element (it and everything inside it). A theme switch is a model change in the view.
- **Precedence (no CSS specificity):** the view's or code's own setting; then the element's inline `on` blocks; then styles, the nearest scope first (the element's own `styles`, its ancestors' outward, then the app's), and within one list a later style beats an earlier one; then the type's default; then the property's.
- **What styles can't set:** children, `classes`, `styles`, `name` and `key` (they decide matching or identity); creating such a style fails at once.
- **Styles are compared by reference** (like element types): define them once, at module level; a styles list rebuilt on every update re-matches every time.
- **Engine:** each element keeps the values its matching styles give (a second small list next to its own values; `Get` looks at its own, then the styles', then the defaults). Matching is queued and done at the start of layout, after components run: when an element joins a tree, when its classes, name, styles, `isEnabled` or inline blocks change, when one of its states changes (its subtree too when a style tests an ancestor's state, e.g. TextBlock inside a hovered Button), or when the app's styles change. Only properties whose value changes invalidate (measure, arrange or render, per property). A setting the view drops is now removed from the element (it used to be stored at its default), so a style below it shows through.
- **Inspector:** a `style` source with the selector written out (`Button.primary:hover`, `inline :hover`); `PropertyValue.Selector` carries it.
- **Themes (step 2):** a `Theme` record (palette, corner radius, font sizes), `Theme.dark`/`Theme.light`, `Theme.styles theme` returning the built-in looks as ordinary styles; no named-resource system unless we find we need one. Button's hover/press tint moves from its render function into theme styles, with `Style.transition` for appearance properties (colors, shadow) so it still fades; layout properties don't animate in M7. `foreground`, `font` and `fontSize` become inherited settings on every element. The default theme must render the existing dashboard, counter and button screenshots pixel-identically.
- **Not in M7:** M8's controls, animating layout properties, styles loaded from files.

Decisions for M7 step 2 (themes), agreed 2026-11-16 (they replace the "Themes (step 2)" bullet above where they differ):

1. **A theme is made with settings**, not a record ("settings everywhere"): `Theme.dark [ Theme.accent c; Theme.cornerRadius 8.0 ]`, `Theme.dark []` for the plain one. The result is a value apps read their palette from (`theme.Accent`, `theme.Surface`, ...): background, surface, raised surface, border, text, dim text, accent, danger; corner radius; body font and size; focus ring color.
2. **Where a theme applies:** theme styles are their own precedence level, below every other style: own value > inline blocks > styles > app styles > theme > type defaults. `App.theme t` for the app; a `theme` setting on any element for its subtree (the nearest theme applies), so switching is the view returning a different theme and a light panel can sit in a dark app. The inspector keeps its own dark theme. A theme builds its styles once (styles compare by reference).
3. **State looks are general, not Button's** (Tim asked to double-check; options compared: a Button-only overlay, a state layer on Border, relative colors like `lighten`, fixed colors per state):
   - Hover and press: `Border.overlay color`, a translucent layer over the whole box (Material's "state layer"), set by the theme's `:hover`/`:pressed` blocks. It's a separate property from `background`, so a class color (`.primary`) keeps its state looks: a hover that set `background` would lose to the app's class style. Relative colors were rejected for the same reason; fixed colors per state are repetitive. Visible change: a hovered button's border lightens 8% (was 12%), like its background.
   - Disabled: a general `opacity` setting (the element and everything inside it; each shape's alpha is multiplied, so overlapping parts show through each other, as today's disabled button does; true group opacity needs offscreen layers, later). The theme sets `Button:disabled [ opacity 0.5 ]`, `TextBlock:disabled [ opacity 0.5 ]`, and 1.0 for text inside a disabled Button so it doesn't fade twice; TextBlock's dimming leaves its render function. Visible change: disabled button text 45% → 50%.
   - Press (Tim: depth and timing are a style decision, not Button properties): a general `scale` setting (drawing only, around the center; layout and hit testing don't move) and `shadow`, set in a `:pressed` block with transitions. Button's own `Element.transform` and render tweaks go: Button is Border plus click, focus and its theme style.
4. **Inherited text settings:** `foreground`, `font` and `fontSize` on any element reach the text inside. An element's value: own > styles > type default > the nearest ancestor's > the theme's base value > the property default. The theme gives text color, font and size as inherited base values, not as a TextBlock style (a type style would beat inheritance, so `Border.foreground red` wouldn't reach the text: the WPF trap). `Button.text` stops hard-coding its text color; Button's type default `foreground` is inherited by the label.
5. **Transitions:** `Style.transition prop duration` (and `Style.transitions [ ... ]`), a setting inside any style; each property's transition is its own value, so several styles combine. Runs on any change of the effective value except an element's first; only properties that affect drawing alone (colors, numbers, shadows, corner radius, scale, opacity), others throw when the style is made. Frames only while one runs. The dark theme fades Button's overlay (100 ms) and its press (scale and shadow, 120 ms).
6. **Without a theme**, type defaults keep today's dark base look, so a bare `ElementTree` (tests, the dashboard screenshots) looks as before, without state looks. `App.run` without `App.theme` uses `Theme.dark []`.
7. **Two sub-steps:** 2a: the theme and its precedence level, inherited text settings, `overlay`/`opacity`/`scale`, transitions, Button/TextBlock/focus ring/scrollbar looks through `Theme.dark` (nothing should look different at rest); 2b: `Theme.light` and a Light/Dark switch in the Counter demo (the Styles tab reading the theme's palette).
- Not in step 2: group opacity, M8's controls, themes from files.

M7 step 2a built 2026-11-16, verified by Tim on Windows and in the browser the same day (with every inspector change below). Notes from building it:
- Files: `Theme` (palette, settings, `IThemeSource`) and `StyleEngine.themeStyles` in Style.fs; `Theme.dark` and the looks (`ThemeLooks`) in the new Theme.fs, after the elements (it needs their `style` functions); `InheritedProps` (font, fontSize, foreground), `LayoutProps.opacity/scale/theme`, `Prop.Inherits/IsTheme/Lerp/TransitionProp` and the interpolators (float, Color, CornerRadius, `BoxShadow voption`) in Props.fs. `TextProps.font/fontSize/foreground` are now the inherited ones, so `foreground`/`font`/`fontSize`/`opacity`/`scale`/`theme` are `Layoutable` settings (`Border.foreground`, `Button.foreground`).
- Precedence as agreed: theme styles are matched first in the same pass, so everything else wins; the inspector's selector text says `theme Button:hover`. Inherited lookup: own > styles > type default > the element's own `theme` base > parent's resolved value > the tree's theme base > prop default; a change re-invalidates the descendants that take the value from above (`InheritedChanged`), a theme change re-matches and re-invalidates the subtree.
- **Hosts get the dark theme by default**, not only `App.run`: `ElementHost.create` without `ElementHost.theme` uses one shared `Theme.dark []`. Found by the counter screenshot: a custom host without a theme lost the disabled look. A bare `ElementTree` still has no theme (unit tests rely on black text and no state looks); rendering tests that show states set `tree.Theme`.
- Disabled text on its own fades to 45% (`TextBlock:disabled [ opacity 0.45 ]`), as before, instead of the planned 50%: same pixels; text in a disabled Button is at 1.0 inside the Button's 0.5.
- Transitions: a tween per property per element (`Tween`, the same as `ctx.Animate`), started when a new effective value is first read, normally at render, so it runs on the frame clock; while moving it asks for one more frame after each frame (`AfterFrame` → `InvalidateVisual`), then stops. The element's first value never fades.
- `ScrollViewer.thumb` (color) added so the theme sets the scrollbar; `FocusRing.colorProp` is inherited and the theme gives its base value (`Theme.focusRing`).
- Screenshots: all identical except `buttons@1x` (max channel difference 12: hover border tint 12% → 8%, pressed border darkened by the same 20% overlay as the background, disabled label 45% → 50%), regenerated on Tim's GPU. `ButtonTests` and the counter demo test now set `tree.Theme`.
- **The inspector always shows themed properties** (Tim, 2026-11-16): public `element.ThemedProperties` (an extension in Style.fs, like `ActiveStates`) lists every property the element's theme can set on its type, in any state or class, with the rules (`Button:hover`). The properties panel shows those even at their default (sorted after set values, before plain defaults), with "theme sets it in Button:hover · Button:pressed" under the row; other defaults stay behind "Show defaults". No toggle: a handful of rows per element, and the ones worth seeing. **Changed the same day (Tim: crowded):** the "theme sets it in …" line is gone; themed properties still always show, and a "from theme Button:hover" line appears under one only while a theme style sets it. `ThemedProperty.Selectors` stays in the API for other tools.
- Inspector layout fixes (Tim, 2026-11-16): the "from …" and "theme sets it in …" lines under a value wrap within the value column (right margin keeps the source column clear; before, they ran under it); the selected element's name and Deselect button sit above the scrolling properties instead of scrolling away (the divider still sets the whole area's height; the scroll area fills what the name row leaves). Screenshots unchanged.
- Dragging the inspector lagged in the browser with an element picked (Tim, 2026-11-16): every pointer move re-ran the whole panel, tree rows and properties included (values, states, themed rules, formatting), which is slow under the browser's .NET. Now `InspectorState.ContentVersion` changes with everything except the panel's geometry (move, corner and edge resize, the divider use `moved ()` instead of `notify ()`), and the tree, the selection's name row and the properties are components (`InspectorTree`, `InspectorSelection`, `InspectorProperties`) whose props are `{ State; Version }`: a drag re-places the panel without re-running them. Setting `Own` notifies, so the tree leaves out the inspector's own layers from the first build.
- Still slower with an element picked (Tim): elements are now skipped while rendering when their bounds, grown by `Element.RenderOverflow` (64 DIPs, for shadows and focus rings), lie outside the current clip (`DrawList.CanShow`); elements with `scale` above 1 or their own transform are always drawn. Rows scrolled out of the tree and properties no longer cost anything (their shapes were already dropped, but each text row still laid out its glyphs). Measured (fake device, Release, best of 5×100 drag frames): inspector render with a button picked 0.22 → 0.11 ms; a whole drag frame 0.12 ms unpicked vs 0.16 ms picked, layout equal (0.047 ms), the rest is drawing the extra visible text. Screenshots unchanged; 221 unit tests (1 new: rows scrolled out aren't drawn).
- **The selection is only outlined** (Tim, 2026-11-16: changes under the highlight were hard to see; option A of A outline / B fainter fill / C hide while forced, without the size label): the full highlight (fill, margins, label) shows only for what is pointed at (a tree row or pick mode); the selection alone gets an outline just outside its bounds: first 1 DIP solid, then (Tim: didn't stand out) dashed, 2 DIPs thick, 4 on / 3 off, with dark (0x11111B) in the gaps so it shows on light and blue too; then (Tim: the outline covered the element's outer edge) **corner marks**: L-shaped marks 3 DIPs outside the element, 2 thick, arms up to 8 (shorter on small elements so they never meet), blue on a 1-DIP dark edge; the sides stay clear, so the edge, border and shadow show. `inspector-pick@1x` regenerated. 227 unit tests (1 new).
- **Every state is always listed** (Tim, 2026-11-16: he wanted all states shown, not a toggle): STATES lists the built-in states and every custom state (`States.customStates ()`) on every element, off ones dim, so any can be forced (e.g. `selected` on an unselected chip; a style that tests the `selected` *property* with `Style.prop`, like the chip text's, doesn't follow a forced state). Expected to need the "states some style tests on this type" filter once M8 adds many custom states. Tests that make custom states remove them again (internal `States.remove`), since every inspector panel in the run would list them.
- **STATES stays in view** (Tim, 2026-11-16): the STATES section moved from the scrolling properties into the pinned part with the name and Deselect (the `InspectorSelection` component), above a faint line where scrolling starts; the scroll area gets what's left of the divider's height. `inspector-pick@1x` regenerated. The properties area now starts at 300 DIPs (was 240; Tim: a little taller), about what the pinned STATES took; it's still limited so the tree keeps 170 DIPs, and the divider changes it.
- **`theme` as a value source** (Tim, 2026-11-16, instead of highlighting themed rows): `ValueSource.Theme` for values from the theme's styles (public, in `element.Values`), `Style` stays for the app's and elements' styles. The style engine marks theme selectors internally (`ThemeMark`); `Values` strips it, so `Selector` reads `Button:hover`. The inspector's source column says `theme` and the line under it "from Button:hover". Themed properties at their default stay `default`. 224 unit tests (1 new).
- **`ctx.Animate` stays** (Tim, 2026-11-16): after 2a nothing built in uses it (Button's press moved to theme styles), but it animates values inside render functions with no property behind them (a tick drawing in, a chevron turning), which `Style.transition` can't. It lost its only coverage with Button, so it has its own test now (moves over its duration, a new target midway starts from where it is, frames while moving, none after). 223 unit tests.
- **`overlay` keeps its name** (Tim asked about `tint`, 2026-11-16): it says what it does (a layer over the box), Flutter uses `overlayColor` for the same job, and `tint` means other things elsewhere (iOS accent color, Android image recolor, a multiply) and is the likely name for icon color in M7b. Its doc comments and the README now say plainly that it is the hover and press layer.
- **Forcing states from the inspector** (Tim asked how to inspect hover and pressed when the mouse must leave the element to scroll the panel; options: A force states as Chrome's ":hov", B freeze the panel, C keyboard scrolling; Tim chose A, 2026-11-16). Decisions: (1) looks only: styles and the flags render functions read (`ctx.IsPointerOver`, `IsPressed`, `IsFocused`, `IsFocusVisible`, `IsEnabled`) see a forced state; input reads the real ones (a forced `pressed` doesn't click, a forced `disabled` doesn't block; Tim asked again after the evaluation and kept it, with no extra hint in the panel). (2) Every state, built-in and custom: `ElementState.Test` is "forced or the real test". Public Core API for tools (extensions in Style.fs): `element.ForceState(state, on)`, `element.ForcedStates`, `element.ClearForcedStates()`; `States.customStates ()` is now public. Forcing applied to the element only at first; **changed the same day (Tim): forced states spread like real ones**: a forced `disabled` disables everything inside (as `isEnabled false`), a forced `hover` hovers the ancestors (as the pointer over a child); pressed, focus and custom states stay on the element (they don't spread for real either). Process-wide counts of forced hovers/disableds skip the walks while none is forced; forcing re-matches the subtree (disabled) or the ancestors (hover). 226 unit tests (2 new). (3) The states are their own section (a STATES heading like LAYOUT, Tim's choice after trying the toggles in the value column and a full-width row starting with the label), a wrapping row of toggles across the panel's width: built-in states always, custom ones when on or forced (an "all states…" toggle listing every custom state was tried and dropped by Tim, so a custom state that is off can't be forced from the panel); forced ones are teal (Tim: color instead of the words "(forced)"; teal because blue is the selection, purple components and yellow names), ones on for real are bright, off ones dim. The tree marks an element with forced states "• forced" in teal. (4) Forced states last until untoggled, "Clear forced" (next to Deselect while any are forced), closing the inspector (F12 or ×) or its removal. (5) They stay when another element is selected. The inspector has a small private wrapping panel (`InspectorWrapRow`) until M8. `inspector-pick@1x` regenerated (on Tim's GPU). Tests: 222 unit tests (1 new: forcing hover, pressed and disabled, clearing, closing).
- Tests: 220 unit tests (12 new: 11 in ThemeTests.fs, themed properties, theme below app styles, state looks, disabled text, no theme, element theme, inherited text values, inherited font size re-measures, transitions and their frames, transitions only for render properties, opacity and scale in rendering; 1 in DrawListTests for nested opacity). Changed: the click test waits for the 100 ms hover fade before checking it goes idle; the label in a Button now inherits the Button's light text; the inspector reports Button's background as "style" (the theme's).

Decisions for M7 step 2b (light theme), agreed 2026-11-16 (Tim took every recommendation):

1. **`Theme.light [ ... ]`**, same settings as `Theme.dark`. Palette: background `#F3F3F7`, surface `#FFFFFF`, control `#FFFFFF`, border `#C9C9D6`, text `#1E1E2E`, dim text `#5E5E72`, accent `#3D59A1`, danger `#B3364A`, focus ring `#2F5BD3`. The dark palette is unchanged.
2. **`OnAccent`** (`Theme.onAccent`): text on accent- or danger-colored backgrounds, white in both themes (Material's "on primary"). Apps use it next to `Accent` (the sample's `.primary` style).
3. **The state looks are palette colors** (option A, over a dark/light flag): `HoverTint` (dark: white 8%, light: black 5%), `PressTint` (black 20% / black 10%), `ScrollThumb` (white 30% / black 30%) and `Shadow` (a button's resting shadow; flat when pressed or disabled is derived from it), each with a setting (`Theme.hoverTint c`). The theme now also sets Button's resting shadow (type default unchanged for a bare tree).
4. **The window background follows the theme** at the app's root (the root element's `theme`, else the host's) unless set: `App.background` / `ElementHost.background` are now overrides, with no default of their own (the dark theme's background is the old default, so nothing changes for dark apps).
5. **Switching: no new API.** The view sets `theme` on its root from the model; it holds the theme value, so it reads colors directly (`theme.Surface`). `App.theme` is the starting theme. No `ctx.useTheme` hook until passing the theme down gets tedious.
6. **No animated switch:** only the existing transitions (hover, press) animate.
7. **Following the OS setting is later** (Windows registry `AppsUseLightTheme` + `WM_SETTINGCHANGE`, browser `prefers-color-scheme`): a platform API of its own.
8. **Counter demo:** a Light/Dark button at the right of the tab row, kept in the model; tabs get the theme as a prop and read the palette (no hex colors left in the tabs); the Styles tab has a style list per theme, each built once.
9. **Unchanged:** the inspector stays dark; a bare `ElementTree` has no theme, so the existing screenshots stay as they are. A light-theme buttons screenshot is added.

M7 step 2b first built 2026-11-16 (not tried by Tim: he asked for theme tokens instead after reading the code, see the next decisions; the Counter demo parts below were replaced). Notes from building it:
- Core: the palette gained `OnAccent`, `HoverTint`, `PressTint`, `ScrollThumb`, `Shadow` (with settings); `ThemeLooks.build` reads them (and now sets Button's resting `shadow` from the theme; flat = blur × 0.4, no offset); `Theme.light` in Theme.fs. `ElementHostOptions.Background` and `host.Background` are `Color voption` (ValueNone = follow the theme); `host.ClearColor` is the color used: the override, else the `theme` on the app tree's root element (looking through components at the root, so a component that sets `theme` on its output counts), else the tree's theme, else black. `App.background` no longer defaults to `#181825` (the dark theme's background is the same color).
- Counter demo: `Common.fs` holds `darkTheme`/`lightTheme` (made once), `extras theme` (the demo's own colors beyond the palette: card highlight, error, bar fill/track, alternate rows, hover, scrim, button borders, chip colors; chosen so dark looks exactly as before), `setTheme` (an F# event) and `useTheme ctx start`. `demo` takes its starting theme as a prop (`App.view (demo darkTheme)`); it sets `theme` on its root and passes the theme to the pages as a prop; the toast and dialog layers use `useTheme` and set it on their roots. Tabs are 72 wide (was 80) so a 64-wide switch fits after a `1*` spacer. No hex colors are left in the tabs; white text became the theme's text color (`#E6E6F0`), a visible but tiny change in dark.
- The Styles tab builds its normal and compact lists once per theme (`stylesFor`).
- Tests: the light screenshots are the whole demo (`counter-demo-light@1x`) and the Styles tab (`styles-page-light@1x`) instead of the planned buttons-only one (they cover buttons and more). The demo is started light rather than switched: the switch is a process-wide event and the inspector screenshot tests keep demos alive on other threads (a switch from one test reached their trees and threw "UI thread"). Regenerated: `counter@1x` (count text color, and the test's tree now has the dark theme), `counter-demo@1x`, `inspector-floating@1x`, `inspector-pick@1x` (the new tab row), `styles-page@1x` (within tolerance). Unit tests: 230 (2 in ThemeTests: the light looks, the new settings; 1 in AppTests: the clear color follows the root's theme, through a component, and an override wins). Rendering tests: 37.

Decisions for theme tokens, agreed 2026-11-16 (Tim, after trying 2b's code: holding a theme value, reading colors from it and passing it to every page didn't follow the settings style; option A of A tokens / B a `useTheme` hook / C only model-driven switching). They replace 2b's palette members, the demo's `Extras`/`stylesFor`/`useTheme`/event and passing the theme down:

1. **Tokens are typed and declared like props:** `ThemeValue.define "CardLine" (Color.Hex 0x34345A)` gives a `ThemeValue<Color>`; the default is used when a theme doesn't set it and when there is no theme. Generic, so colors, shadows, numbers and fonts work.
2. **Built-in tokens in `Palette`:** `background`, `surface`, `control`, `border`, `text`, `textDim`, `accent`, `danger`, `onAccent`, `focusRing`, `hoverTint`, `pressTint`, `scrollThumb` (colors), `shadow`, `cornerRadius`, `font`, `fontSize`. Their defaults are the dark values, so `Theme.dark []` sets nothing.
3. **Themes set tokens; code reads them with `Get`:** `Theme.dark [ Theme.set cardLine c; Theme.accent c ]` (shorthands for the built-ins stay: `Theme.accent c` = `Theme.set Palette.accent c`); `theme.Get Palette.accent` replaces `theme.Accent`; a custom element's render function reads one with `ctx.Theme Palette.accent`. The nearest theme decides: a token it doesn't set has its default (themes are complete; an outer theme isn't consulted).
4. **Settings accept tokens:** every Core setting of a color, shadow, corner radius, font or font size has an overload taking a token (`Border.background Palette.surface`, `Border.cornerRadius Palette.cornerRadius`, `Border.shadow Palette.shadow`). Overloads, not implicit conversions (FS3391). Element authors: **`Setting.fromTheme prop token`** (changed from the proposed `Setting.create prop token`: `Setting.create` is a module function and can't be overloaded). Internally a token setting stores a reference to the token and, where the setting's type differs from the token's (a shadow is `BoxShadow voption`, a corner radius from a number), a fixed conversion; two references are equal when token and conversion are, so re-rendering a view doesn't count as a change. The pressed/disabled "flat" shadow is `Palette.shadow` with a conversion, not a token of its own.
5. **Resolution:** when a value is read (`Get`, inherited lookups, the inspector), from the nearest theme (own and ancestors' `theme` settings, then the tree's), else the token's default. Inherited properties resolve where they're set, then the resolved value is inherited (as CSS computes `var()`). Style selectors (`Style.prop`) see the token itself. A theme change re-measures and redraws everything below it and restarts transitions without fading (switching doesn't animate). The inspector shows the resolved value with the token under it (`Palette.surface`), above the "from …" line.
6. **The built-in looks are one style list of tokens**, shared by every theme (built once); `Theme.dark` and `Theme.light` differ only in values.
7. **The `theme` on the app view's root applies to the whole window:** every layer that takes app styles (toasts, dialogs; not the inspector) and the window's background. The view reads the model, so switching is model-driven; `App.theme` stays the starting theme (a model-driven `App.theme (fun model -> ...)` doesn't fit: settings know only `'Msg`, so it would need a runtime cast). A `theme` on any other element still applies only inside it.
8. **Counter demo:** the Light/Dark choice moves into an app model (`App.program`); `Common.fs` is just the demo's tokens and its two themes; styles are written once.
9. 2b isn't committed: `Theme.light`, its colors and the window following the theme stay; the rest is replaced.

Theme tokens built 2026-11-16, verified by Tim on Windows and in the browser the same day (with 2b). Notes from building:
- Files: `ThemeToken`/`ThemeValue<'T>`/`ThemeValue.define`, the internal `ThemeRef` (token + optional conversion) and `Setting.fromTheme` in Props.fs; `Palette` and `ThemeConvert` (`someShadow`, `flatShadow`, `uniformCorners`) in the new Palette.fs; `Theme` (a token → value dictionary, `Get`, `set` and the shorthands) in Style.fs; `ThemeLooks` (one token style list and the inherited bases, made once) and `Theme.dark`/`Theme.light` in Theme.fs. `IThemeSource` gained `Resolve token`.
- Token overloads: `Border.background/borderBrush/overlay/shadow/cornerRadius`, `ScrollViewer.thumb`, `Layoutable.foreground/font/fontSize`. `Palette.cornerRadius` is a number (the setting makes it uniform), `Palette.shadow` a `BoxShadow` (the setting wraps it).
- Elements store the `ThemeRef`; `Get`, `GetBoxed`, inherited lookups (`Resolved`) and `Values` resolve it (`Element.TokenValue`: own/ancestors' `theme`, then the tree's, then the default). `Prop.Same` compares a `ThemeRef` only with a `ThemeRef`; `SetValue`/`ClearValue` compare values as read, so setting the same token again is no change (found by a test: comparing the read old value with the stored reference re-measured on every patch). `Element.ThemeChanged()` (a `theme` setting changed, or the tree's) clears transitions and re-measures the subtree. `PropertyValue.Token` names the token; the inspector shows it under the value.
- Host: `host.WindowTheme` (the root's `theme`, looking through components at the root, else the host's) is given to every layer with app styles each frame, after the app layer's layout; `ClearColor` reads its `Palette.background`.
- Counter demo: `App.program` with `{ Light: bool }` and `ToggleTheme`; `view` puts `Border.theme` on the root around `demo (light, dispatch)`. Common.fs has the demo's tokens (highlight, error, fill, track, rowAlt, hover, scrim, button borders, chip colors, two shadows) and `darkTheme`/`lightTheme`. The pages, toasts and dialog no longer take or listen for a theme. Tests show the demo through `view { Light = ... } ignore`.
- **Measured (Release, steady state: the whole set run twice, the second counted; a first run was misleading again, 41 µs instead of 2):** one token read costs 22 ns one level below the nearest theme, 50 ns at 10 levels, 110 ns at 30 (a plain read 5 ns): it walks up to the nearest `theme`. Drawing the Counter demo (light, Elmish tab) makes 51 token reads per frame: 11.5 µs per frame against 9.2 µs with the same values set plainly, +2.3 µs; the Styles tab 41 reads, +0.9 to 1.4 µs (16.5 against 15.5 µs). Layout reads none here. Then (Tim's request) each element remembers its nearest theme (`Element.NearestTheme`, found through the parent's remembered one): a global theme epoch, bumped by `SetParent`, `SetRoot` and `ThemeChanged` (a `theme` setting or a tree's theme), makes everything look again after a change; one counter for all trees, so a change elsewhere only costs a lookup. **Measured again:** one read 9 ns at 1, 10 or 30 levels (plain 5 ns); the Counter demo +0.2 to 0.4 µs a frame (9.4 against 9.0 µs), the Styles tab +0.1 to 0.4 µs. The dark and light screenshots are unchanged by the switch to tokens (both match the 2b references); `inspector-pick@1x` regenerated (the tree has the root Border). Unit tests: 237 (6 new in TokenTests.fs, 1 in AppTests: layers follow the root's theme, a layer without app styles doesn't). Rendering tests: 37.

Decisions for app values (agreed 2026-11-16; Tim asked for "app level hooks that can be referenced anywhere"):
1. Shared state declared once, like theme tokens: `let lightMode = AppValue.define "LightMode" false` (a name for the inspector, a default). Not React-style providers: part-of-the-tree values are settings (`theme`) or props.
2. Values live in one store per app (`App.run`/`runInBrowser`/`App.start`), shared by its windows and layers (toasts and dialogs see the same values). A bare `ElementTree` has its own store, so tests stay apart. Never one store per process (the first 2b build's process-wide theme event leaked between tests).
3. Components: `ctx.useAppValue v` returns the usual `State<'T>` (`Current`, `Set`, `Update`; from any thread). Only components that read the value re-run; an equal value changes nothing.
4. Outside components: `AppContext` (`App.onStart`) gets `Get v` / `Set v x`; Elmish gets `Cmd.setAppValue v x`; the starting value is a setting, `App.value v x`.
5. Documented split: the Elmish model holds the app's data; app values hold UI-wide things (light/dark, the selected tab, the signed-in user's name). Both can be used together.
6. Inspector: a component's hooks show `useAppValue LightMode` with its value. A panel of every app value is later.
7. Derived values (`AppValue.derive`) later, if needed.
8. Counter demo: light/dark becomes an app value; `Model`/`ToggleTheme` and the `(light, dispatch)` prop go; back to `App.view` with a root component that reads it and sets `theme`.

App values built 2026-11-16, verified by Tim on Windows and in the browser the same day (with 2b). Notes from building:
- Files: `AppValue.fs` (`AppValue<'T>`, `AppValue.define`, the `AppValues` store: `Get`, `Set`, `Update`, internal `Subscribe`); `ctx.useAppValue` and its slot in Component.fs; `IElementRoot.AppValues`; `ElementTree.AppValues` (its own until a host gives it one); `ElementHost.appValues` (a setting; every layer's tree gets the host's) and `host.AppValues`; `App.value v x` (curried, like `App.program`), `AppContext.Get`/`Set`/`AppValues`; `Cmd.setAppValue`.
- `State<'T>` is now backed by functions (read, set, run on the UI thread), so `useState` and `useAppValue` return the same type. A component subscribes on its first run and unsubscribes when it goes (its slot is disposed); a different value at the same hook place swaps the subscription.
- A component runs the first time before its element joins the tree, so it couldn't find its tree's values. Fixed with a thread-static "tree being built" (`Element.RealizingIn`), set by `ElementTree.Update` and by an attached element while it makes new children; a component's `AppValues` uses its tree, else that one.
- `Cmd` effects now receive the app's values (`AppValues -> dispatch -> unit`, internal); `Cmd.execute` takes them (the app's, or a component's tree's for `useElmishWithCmd`).
- Sets off the UI thread are posted to it through the app's dispatcher (`App.start` gives the store one); a bare tree's store runs them where they're made.
- Counter demo: `Common.lightMode`; a `ThemeSwitch` component (the button) and a `Root` component (sets `theme` on its Border around `demo ()`) both use it; `App.view (root ())`. `inspector-pick@1x` regenerated (the tree shows Root above the Border; it had passed within the tolerance, so it was regenerated to match). Unit tests: 243 (5 new in AppValueTests.fs). Rendering tests: 37.

*App values pass, 2026-11-19* (the mechanism and the six built-in values gone through; none removed):
- A listener is guarded: `Subscribe` is public and runs app code, and one that threw stopped the listeners after it (components didn't re-run) and landed in whoever called `Set`. Now each is caught; the error goes to `App.onError` through the store's dispatcher, and without one (a bare tree) the first is thrown to the setter once every listener has run.
- `Get` off the UI thread warns in a Debug build, once per value (`DebugWarnings`): the store is a plain dictionary, so such a read races a set.
- The id counter moved to `Ids.NextAppValue()` (it was a module-level `let`), and `WindowTitle.current` is made on first use like the other built-in values: `WindowTitle` is now a type with a static member, written the same in app code.
- The host's subscription to `Toasts.placement` is disposed with the host, as the theme's was. No host shares an app's values and closes before it today (the inspector's pop-out has its own), so nothing leaked yet. `AppValues.ListenerCount` (internal) is for the test.
- `ScreenReader.enabled` is read when a window opens; its comments said "starting value", as if a later set did something. `ScreenReader.bridge` holds a factory, not UI state: kept, as the way a platform package hands Core a service through `App.value`.
- **Left for the multi-window design:** `AppValues` also carries the dispatcher, the named scrollers and the host's toasts and dialogs (`Services`), because a `Cmd` effect is given the store and nothing else. Each host writes itself into `Services`, so with shared values the last window opened would get every `Cmd.toast` (Gap 1 under "Several windows per app"). What a command is given is that round's decision; the store isn't renamed or split before it.
- Not built, nothing has asked: reading an app value from an Elmish program (`update` and `view` can set one through `Cmd.setAppValue` but only components can read or follow one; an `App.onAppValue value (fun v -> Some msg)` would do it), the inspector's panel of every value, `AppValue.derive`.
- Tests: 5 new (a throwing listener in an app and on a bare store, `Update` and a disposed subscription, the off-thread warning, the placement subscription).

Decisions for the window theme as an app value (agreed 2026-11-16; Tim: "can't we remove the root component and use App.theme to initialize the AppValue?"):
1. The window's theme is itself a built-in app value, `Theme.current : AppValue<Theme>` (not a `lightMode` bool mapped to themes): one concept, any number of themes. The switch compares the current theme by reference.
2. The rule "the `theme` on the app view's root applies to the whole window" is dropped (it existed only for switching): an element's `theme` applies inside it only; `host.WindowTheme` reads `Theme.current`.
3. `ElementHost.theme t` sets the starting value in the host's store, so hosts made without `App.run` work as before; a bare `ElementTree` keeps its `Theme` property.
4. Outside components: `AppContext.Set(Theme.current, t)` and `Cmd.setAppValue Theme.current t`, for free. `App.theme t` sets the starting value (an `App.value Theme.current t`), which also answers the model-driven `App.theme` request: an app value is typed on its own.
5. Counter demo: `Root` and `Common.lightMode` go; `App.view (demo ())` with `App.theme darkTheme`; `ThemeSwitch` sets `Theme.current`.

Built 2026-11-16, verified by Tim the same day (with 2b): `Theme.current` is a static member on `Theme` in Theme.fs (default `Theme.dark []`). The host sets its store's value from `ElementHost.theme` when given, gives every layer with app styles `ValueSome (Get Theme.current)` when added, and subscribes: a change sets every such layer's `Tree.Theme` (which re-styles it) and asks for a frame; the subscription goes when the host is disposed. `host.WindowTheme` is now a `Theme` (no longer a voption); `ClearColor` reads its `Palette.background`. The root lookup and the per-frame theme sync in `DrawFrame` are gone. `App.theme t` adds `Set(Theme.current, t)` to the starting values (`AppConfig.Theme` removed). Counter demo: `Root` and `Common.lightMode` removed; `ThemeSwitch` reads `Theme.current` and compares it with `lightTheme`. The generic app-value examples in docs now use `compactMode`. Tests: the two host theme tests rewritten (`Theme.current` drives the clear color and the layers; a root's `theme` no longer reaches the window), one new (clicking the demo's switch in a running app makes the window, background and every layer light); `inspector-pick@1x` regenerated (the tree starts at Demo). 244 unit tests, 37 rendering tests.

Decisions for the window title as an app value (agreed 2026-11-16; Tim: "can we also make the window (or browser tab) title an AppValue?"):
1. A built-in `WindowTitle.current : AppValue<string>` (default "Scribble"), named like `Theme.current`; "window" says which title (the inspector has its own). Set from anywhere like any app value (`ctx.useAppValue`, `AppContext.Set`, `Cmd.setAppValue`); in the browser it's the tab title.
2. `App.title "Counter"` stays and sets its starting value (like `App.theme`).
3. `App.showStats` appends the stats to the current title; the window title is refreshed when either changes.
4. `App.start` follows the value (sets `window.Title`); `ElementHost` doesn't, so a host made without `App` never changes a title nobody asked it to.
5. `AppContext.Title` reads the current value; the popped-out inspector ("Inspector – " + title) renames itself when it changes. The inspector can't see Core's internals, so `AppValues.Subscribe(value, listener)` becomes public (the listener gets the new value).
6. Counter demo: the title shows the selected tab ("Counter – Styles").

Built 2026-11-16, verified by Tim on Windows and in the browser the same day: `WindowTitle.current` is a module value in AppValue.fs, next to the public `AppValues.Subscribe(value, 'T -> unit)` (the internal id-based one stays for components and the host). `App.title t` adds `Set(WindowTitle.current, t)` to the starting values (`AppConfig.Title` removed); `App.start` creates the window with the current value and subscribes for the app's lifetime; the stats suffix is kept apart and appended (refreshed once a second, as before). `AppContext` no longer takes a title. The inspector's internal `AppTitle` option became `FollowAppTitle` (set by `Inspector.enable`); with it the pop-out is named after the value and renamed through a subscription disposed with the inspector; without it (a bare host) it uses the window's title as before. Counter demo: `Demo` reads the value with `useAppValue` and sets it when a tab is selected; `App.title "Counter – Elmish"`. Test platforms: the fake now creates its window with the requested title, as real ones do. Tests: 3 new in AppValueTests.fs (start, sets including from another thread, the default "Scribble", stats after a renamed title, the demo's tabs), the pop-out test checks the rename. 247 unit tests, 37 rendering tests (screenshots unchanged).

Fix (Tim, 2026-11-16: "the hello dashboard doesn't start animating until my mouse enters the window"; option 1 of three, agreed): the first frame's `Delta` is zero (`FrameClock` has no earlier frame), so an `onFrame` animation driven by time (Hello's `Tick delta`) left the model unchanged, which meant no rebuild and no next frame; any input frame unstuck it. Present since at least step 2a (checked by running the 2a build with the mouse away), not caused by 2b or the title. `App.start` now asks for one more frame after the first when the app has an `onFrame` handler: one extra frame at start, nothing while idle (power rule kept). Rejected: a made-up first `Delta` (reports time that didn't pass); fixing only the sample (leaves the trap for apps). 2 new tests (a time-driven animation keeps going on its own; without `onFrame` the first frame asks for no other). 249 unit tests.

M7 step 1 delivered 2026-11-16 and verified by Tim on Windows and in the browser the same day (the whole Styles tab checklist, including property selectors, custom states in the inspector and deselecting), as agreed above. Notes from building it:
- Files: `Style.fs` (ElementState and `States`, the internal `StateBlock`, `Selector`/`SelectorPart`, flattened `Rule`s, `Style` with its selector parts, `StyleProps.styles`, `StyleEngine`), between Element.fs and Layoutable.fs; `Layoutable.classes/styles/on` and `Style.any` (a static extension member in Layoutable.fs, since Style.fs comes before Layoutable). Each element module has `style`.
- Element: a second small list of style values (keys, values, selector texts), replaced as a whole by `ApplyStyleValues`, which invalidates only for properties whose effective value changed (and refreshes the layout copy). Own values are now always stored when set (even when equal to the style's), and a setting the view drops is removed (`ClearValue`) instead of stored at the default: before styles there was no difference, now the style below must show through. All 182 earlier tests passed unchanged after that switch.
- Inline blocks: one property per state (`ElementState.BlockProp`), so `on hover` and `on pressed` sit side by side; a block compares its settings with each property's equality, so a rebuilt view with the same block doesn't re-match.
- Queue: `IElementRoot.RequestRestyle(element, deep, stateChange)` (the element is passed as `obj`: the interface is declared before Element). A tree ignores requests until it has app styles or an element with `styles` or an inline block (`StylesUsed`, which then re-matches the whole tree once). A state change re-matches the subtree only after some rule with a state on an ancestor part has been tested in that tree. Run after components, before measure, parents first.
- Styles are flattened when made: the base settings, then each nested block with its state added (in order, so blocks win over the base).
- Measured (temporary test, removed; Hello dashboard, 104 elements, 11 styles of the kind a theme will have, Release): a full re-match of the whole tree including layout (a theme switch) 0.1 ms; a hover change on a button with an `inside Button:hover` rule 0.003 ms; an animating frame with styles present is within noise of one without (both 0.05–0.12 ms across runs).
- Counter demo: a fifth tab, Styles (classes, a hover block on a card, text matched inside the card and inside the hovered card, an inline block, a button that swaps the page's style list, a disabled button); the selected tab is now a class and a style (same pixels).
- **Property selectors** (Tim's question, agreed and built 2026-11-16, part of step 1): `Style.prop p v` (equal by F# equality) and `Style.where p (fun v -> ...)` select on a property's value; `States.ofProp "checked" CheckBoxProps.isChecked id` makes a state from one (usable in selector lists, `Style.on` and inline `X.on`), for M8's CheckBox, Slider, TabControl. Decisions: (1) selectors read the **unstyled** value (own, else type default, else prop default; `Element.UnstyledValue`), never a style's, so a style can't switch itself on and off; (2) building a selector marks the property (`Prop.TestedByStyles`, and `TestedByAncestorStyles` for `inside`/`parent` parts); a change of a marked property's unstyled value queues a restyle of the element (its subtree when an ancestor part tests it); unmarked properties cost nothing; (3) `States.ofProp` uses the same path; (4) names `Style.prop`/`Style.where`. Inspector text: `CheckBox[IsChecked=true]`, `TextBlock[FontSize?]` for `where`; only simple values (bool, numbers, strings, enums) are written out, others show `…` (Core can't use the inspector's formatter, and `ToString()` on F# types crashes under AOT). The Styles tab has three chips selected through a sample-defined `Selected` property.
- **States and classes simplified** (Tim, 2026-11-16): (1) `ElementState` inherits `SelectorPart`, so one value is used everywhere: `Button.style [ States.hover ] [ ... ]`, `Style.on States.hover [ ... ]`, `Button.on States.hover [ ... ]`, and custom states (`States.define`, `States.ofProp`) read the same way. `Style.hover`/`pressed`/`focused`/`focusVisible`/`disabled` and `Style.state` were removed (one spelling; `States` is where custom states live too). F# can't have internal virtual members, so `SelectorPart.Apply` checks whether it is a state itself. (2) Classes are plain strings (`X.classes [ "primary" ]`, `Style.cls "primary"`, ordinal match); `Class`/`Class.define` removed because defining them felt clunky. Typo safety is now opt-in (`let primary = "primary"`). Considered and rejected: strings converted implicitly to selector parts (`[ ".primary"; States.hover ]`): F# warns FS3391 for `op_Implicit` in list elements, even for method arguments, which fails apps that treat warnings as errors; a parsed `Style.css ".primary:hover"` part (Tim: not wanted). Not now: a block form that adds classes or property tests (`Style.within [ ... ] [ ... ]`), until a theme or control needs it. The inspector shows classes as `.primary .wide`. Screenshots unchanged.
- **Custom states in the inspector** (Tim, 2026-11-16, option A of three): `States.define`/`ofProp` add each state to a list in Core (locked; states are made at module level, possibly on several threads); public `element.ActiveStates` (an extension in Style.fs, since Element.fs comes first) tests the built-ins and every custom state and returns those that are on, skipping a custom test that throws (one written for another element type). The inspector's states line appends custom ones by name. Only called by tools, so apps pay nothing. Rejected: B, types declaring their states (`Element.states [ ... ]`; extra work for authors, misses app-level states; could still be added later for showing "off" states); C, only states styles test (a state not yet styled would be invisible). The Styles tab's chips now use `States.ofProp "selected" selected id` for their own look (their text keeps `Style.prop`).
- **Clearing the inspector's selection** (Tim, 2026-11-16): clicking the selected tree row again deselects it, and a "Deselect" button sits at the right of the selected element's name (labelled in words, not "×": the title bar's × closes the inspector). Escape was rejected for this: the inspector sees keys before the app, so it would take Escape from the app (`App.closeOnEscape`, dialogs). With nothing selected the panel shows its "Click a row, or Pick…" hint again. `inspector-pick@1x` regenerated (on Tim's GPU) for the button.
- The inspector's states line wraps instead of ending in "…" (custom states can make it long); one line tall it is pixel-identical to the other rows.
- Tests: 208 unit tests (1 more for the wrapping states line, 1 for deselecting; 1 before that for `ActiveStates`; 2 before that for string classes and states as selector parts; 6 before that for property selectors: matching and re-matching from views and code, a style setting the tested property doesn't change matching, `where`, an ancestor's property, `States.ofProp` in styles and inline blocks, selector text). Before that, 197 (15 new in StyleTests.fs: exact type matching, classes, precedence of own/inline/scopes/order, hover blocks, nested states, inside and parent, an ancestor's state, disabled through an ancestor, re-layout from a style, scoping, inline blocks alone, what styles can't set, `Values` with selectors, no restyle work without styles; plus formatter cases for classes and styles); 35 rendering tests (new `styles-page@1x`, regenerated on Tim's machine's GPU after the chips were added; `counter-demo`, `inspector-pick` and `inspector-floating` regenerated for the fifth tab only).

Decisions for M7b (icons), agreed 2026-11-16 (Tim: "go with your recommendations"). Three steps, each tried by Tim before the next: (1) icons in Core: an SVG path-data parser (M/L/H/V/C/S/Q/T/A/Z, relative and absolute) feeding the font rasterizer (which gains cubics and arcs), icons in the R8 coverage atlas tinted at draw time (inherited text color, themes, styles, `opacity`; the same single draw call), rasterized per physical pixel size (quarter-pixel sizes under an extra scale, as text); (2) the opt-in `Scribble.Icons.Material` package; (3) image cursors (`Cursor.Image`, bitmap + hotspot, shown by the OS).
1. Names: an `Icon` value and an `IconView` element (`IconView.create [ IconView.icon Material.home; IconView.size 20.0 ]`), like the Avalonia `MaterialIcon`/`MaterialIconView`.
2. The value: path data, a viewBox, rotation and flips; made settings-style (`Icon.fromPath "M2 12h20" [ Icon.viewBox (0.0, 0.0, 24.0, 24.0) ]`); changed by members (`icon.Rotated 90.0`, `icon.FlippedHorizontally`; replaced by functions on 2026-11-16, see "Icon as an F# value"); equal icons compare equal. No color on the value: `IconView.foreground`, the inherited text color or styles give it.
3. No registry of icon sets: a set is a module of icon values; Material is the first.
4. Material names: camelCase static members (`Material.home`, `Material.arrowUpward`, `Material.star.Filled`; a leading digit gets `_`: `Material._10k`), and `Material.icon "star-filled-rotate90"` for strings (the Avalonia spec format; no implicit conversion: FS3391).
5. Material data: one Brotli-compressed block decoded on first use (every app using the package carries the set; revisit if a browser app needs it smaller).
6. Source: Tim's generated `MaterialIconData.cs` (CustomAvaloniaControls), converted by an F# script.
7. Fill rule: nonzero only (the accumulation rasterizer can't do even-odd); documented.
8. `Button.icon`: an icon before the text (or alone), in the button's text color.
9. No `.svg` file reader now.
10. Image cursors stay in M7b (step 3).
11. Tests: path parsing, viewBox, rotation/flips, spec strings both ways, names, equality in the differ; rendering tests for icons at 1x/2x, inherited color, a Button with an icon; wasm AOT size with and without the Material package.

M7b step 1 built 2026-11-16, verified by Tim on Windows and in the browser the same day. Notes from building:
- Scribble.Text: `PathGeometry.fs` (public `PathGeometry.Parse`, internal contours of line/quad/cubic segments; arcs become cubics of at most 90°, per SVG 1.1 F.6.5/F.6.6, radii scaled up when too small; numbers like "1-2", ".5.5", "1e1" and run-together arc flags parse). The rasterizer's line filling is shared (`rasterizeLines`); `rasterizePath path map` flattens curves by Wang's formula at 0.1 px (0.2 px shrank a 40 px circle by about 1% of its area: the lines run inside the curve). `TextSystem.DrawIcon(list, path, viewBox, rotation, flipH, flipV, x, y, size, color)`: one icon cache next to the glyph cache (key: path id, size in 1/16 px, rotation, flips, viewBox), cleared with the coverage atlas; the box snaps to whole pixels; quarter-pixel sizes under an extra scale, as text.
- Scribble.Core: `Icon.fs` (`Icon`, a sealed class: settings `Icon.fromPath d [ Icon.viewBox; Icon.name; Icon.rotate; Icon.flipHorizontal; Icon.flipVertical ]`, members `Rotated`, `FlippedHorizontally`, `FlippedVertically`, `Description` ("name-rotate90-fliph"; also `ToString`, AOT-safe as a class), equality on path data, viewBox, rotation, flips and name; parsed lazily). `Elements/IconView.fs` (props `icon`, `size` default 20; `IconView.draw` internal for Button). Button: `Button.icon`, `Button.iconSize` (default 18), its own measure/arrange (Border's plus the icon and an 8 DIP gap; icon and content form one centered row, its start rounded half-up like layout so the gap stays exact); Border's `inset` became internal.
- Inspector: `Format.value` shows icons by `Description`.
- Counter demo: the Light/Dark switch has a sun/moon icon made from path data (padding 10 instead of a fixed width 64). `counter-demo`, `counter-demo-light`, `inspector-pick`, `inspector-floating` regenerated for it (the switch only).
- Tests: 262 unit tests (13 new in IconTests.fs), 39 rendering tests (new `icons@1x`, `icons@2x`, generated on Tim's GPU and checked by eye).

Inspector toolbar with icons (Tim, 2026-11-16, after step 1; agreed recommendations):
1. Icons: the 8 Material Symbols paths copied into the inspector (`InspectorIcons`, outlined weight 400, credited in the file and README), not a reference to the coming Material package (it would make every app with the inspector carry the whole set).
2. Labels: no tooltips yet (M8's popups); hovering a toolbar button replaces the title with what it does. Only shown while the hovered button still exists (a click can remove it before the pointer leaves).
3. Only the header: "Deselect", "Clear forced" and "Show defaults" stay as words.
4. Icons: pick `arrow_selector_tool`; dock left and right one shape (Material's `dock_to_right`, whose narrow pane is on the left: Material names it for where the content goes) and its horizontal flip; pop out `open_in_new`; pop in `close_fullscreen`; fold `minimize` and restore `crop_square` (Tim, 2026-11-16: a minimize symbol rather than the first try's up/down arrows; the panel folds to its title bar like a window minimizes, and the square restores it, as on a window's title bar); close `close`.
5. Tests find toolbar buttons by name. Hints are short ("Pick (Ctrl+Shift+C)", "Dock left", "Pop out", "Minimize", "Restore", "Close (F12)", "Pop back in", "Stop picking (Esc)"; Tim, 2026-11-16: "Open in a window of its own" didn't fit the default panel); longer ones trim with "…" when the panel is made narrow. While picking, the title shows "Picking… (Esc stops)" whenever no button is hovered (Tim: it showed only on the pick button, but the pointer is out in the app while picking).

Built 2026-11-16, verified by Tim the same day (with the minimize icon, short hints and the picking status): the header is now its own component (`InspectorHeader`, `HeaderProps`: picking, floating, side, folded, can pop out, popped out), so a hover re-runs only it; buttons are ghost buttons (28 × 28, 18 px icons, no border or shadow, transparent until the theme's hover overlay; accent-filled while picking) named `pick`, `dockLeft`, `dockRight`, `popOut`, `popIn`, `fold`, `close`, in groups split by thin lines (pick | placement | fold and close). The title keeps its drag and double-click (it stays the first child, so a re-run patches it). Tests: one new (hover hint, and no stale hint after a click removes the button); 263 unit tests; `inspector-pick`, `inspector-floating` regenerated and checked by eye. NativeAOT publish of the Counter sample: no new warnings; the exe runs. (From the agent's shell the link step needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH for vswhere.)

Open questions:

- [x] License: MIT (Tim, 2026-11-17; "Project review 2026-11-17").
- [x] The name: Scribble, packages `Scribble.*` (the bare `Scribble` id is taken; Tim, 2026-11-17).
- [x] Exact DSL syntax: FuncUI-style settings lists (2026-11-15).
- [x] Test framework: xUnit (2026-11-15: the real runner passes every test).

Idea for M5 (agreed 2026-11-15, see the M5 decisions): a tactile pressed look for the default Button. It sinks (scales to about 97% around its center), its shadow flattens and it darkens slightly, then springs back over 100–150 ms. Needs the pressed state and a visual-only render transform (M5), and a small "animate this value" helper for the spring-back; the depth and timing become style settings in M7.

- [x] Apps made only of components have no messages, so `App.view (root ())` needs `AppSetting<unit> list`. Perhaps `App.run` overloads or non-generic view-only settings. **Closed** (project review 2026-11-17, decision 7): it stays, and README explains it (needed only for a settings list kept in a value).
- [x] `RenderOffscreen` must become async for the browser (readback uses `mapAsync`). **Closed** (2026-11-18): `RenderOffscreenAsync`, "Rendering to an image and saving it".
- [x] Browser key codes: `e.key` mapped to the portable `Key` (M5 step 2, 2026-11-15).

## Serving the browser samples to the local network (2026-11-16)

Tim asked for a way to open the browser samples from other devices. WebGPU needs a secure context (HTTPS, or
http://localhost), so plain HTTP on a LAN address can't work. Agreed (recommendations):
1. HTTPS through a local certificate authority the tool makes once (`artifacts/serve/certificates`), installed once
   per device from a plain-HTTP page on port 5279 with instructions; server certificate re-made each run for the
   machine name, `.local` name and current IPv4 addresses (397 days, the most Apple accepts). `--self-signed`
   as a fallback (click through warnings; not certain every browser grants WebGPU then).
2. Two ports as in development: Hello 5280, Counter 5281.
3. Interpreted by default, `--aot` for WebAssembly AOT.
4. Publishes every run (Release, into `artifacts/serve/<name>`); `--no-build` serves the last publish.
5. No firewall changes by the tool; it prints a reminder.

Built as `tools/Scribble.Serve` (F#, ASP.NET Core Kestrel, in the solution under /tools/; a dev tool, not shipped).
Serves the publish's `.br`/`.gz` copies when accepted, `application/wasm`, no caching. Checked here: both samples
answer over https://192.168.x.x with the CA verifying the chain (curl), `.wasm` arrives brotli-compressed with the
right type, the certificate page and download work. Verified by Tim with his phone (2026-11-16).

WebGPU on phones (found on Tim's Android phone, 2026-11-16): the samples showed only a black page (errors went
to the console, which phones don't show), so both samples' `main.js` now also print errors on the page. The cause
was "no suitable GPU adapter": GPUs with only OpenGL ES get WebGPU's reduced *compatibility* feature level, and
only when asked. `scribble-webgpu.js` now asks for it when the normal request returns nothing, and requests one
storage buffer per shader stage (compatibility's default allows none; a GPU offering none gets a clear error).
Compatibility mode also rejects `@interpolate(flat)` (first-vertex sampling), so `shapes.wgsl` uses
`@interpolate(flat, either)` (each shape's six vertices carry the same index, so nothing changes; desktop
rendering tests are unchanged). Verified by Tim on the phone.

## Phone-friendly samples (2026-11-16)

Agreed (recommendations):
1. Touch on the page: the canvas already had `touch-action: none`; the page also gets `overscroll-behavior: none`
   (no pull-to-refresh or bounce).
2. Touch drag scrolling brought forward from M8 (only that; momentum, the draggable bar and horizontal scrolling
   stay in M8): a new element-type stage `Element.pan (fun ctx dx dy -> moved)`, beside `Element.wheel`. The input
   router remembers, for a touch press, the elements under it whose type pans; once the finger is 8 DIPs from where
   it went down, the deepest one that takes the drag (given the movement so far, then each move) gets the
   finger: it becomes the capture, and the press under it is canceled (no click, pressed looks go). Not for the
   mouse, and not when a view's `onPointerDown` handled the press (it wants the drag). Changed after Tim tried
   it (the cards a finger slid over at the end of the list reacted): while a finger pans, nothing inside the
   panning element gets hover or pressed states; and a ScrollViewer takes any mostly vertical drag when it has
   something to scroll, even at an end (as phones do), so a sideways drag or content that fits stays a press.
3. Hello: the toolbar goes below the title when both don't fit on the top bar's row (`SplitRow`), the toolbar
   wraps (`WrapPanel`), the subtitle trims, and the cards are in a ScrollViewer taking the rest of the window.
   This also fixes the old overlap below ~800 DIPs wide.
4. Counter: the tabs wrap (`WrapPanel`), and the Light/Dark switch goes below them when they don't fit
   (`SplitRow`); headings and the Styles tab's texts wrap, its button and chip rows are WrapPanels.
5. Checks: unit tests for touch panning; rendering tests of both samples at 390 × 844 DIPs, 3×.

Built 2026-11-16, verified by Tim on his phone and the desktop the same day. `WrapPanel` and `SplitRow` are sample code
(`samples/Shared/Adaptive.fs`), written as an app would write its own elements; a real WrapPanel may join M8. A
WrapPanel row is as tall as its tallest child and every child on it gets that height (the last row takes any extra
height it's given), which kept Counter's tab row pixel-identical at its usual size. All existing snapshots
unchanged (the dashboard still matches the hand-drawn version). Tests: 5 new unit tests (269), 6 new rendering
tests (45: `hello-phone@3x`, `counter-phone-<tab>@3x`, checked by eye; each Counter tab also checks nothing
reaches past the right edge).

## Icon as an F# value (Tim, 2026-11-16)

`Icon` was a class (private constructor, `Rotated`/`FlippedHorizontally`/`FlippedVertically` methods, hand-written
equality, settings as static members): the C#-in-F# shape M3b removed from elements. Agreed (recommendations):
1. `Icon` is a plain record with public fields (`Name`, `PathData`, `ViewBox`, `Rotation`, `FlipHorizontal`,
   `FlipVertical`): structural equality for free, `{ icon with ... }` works. Building one by hand skips
   `fromPath`'s empty-data check; bad data fails when first drawn, as malformed data does anyway.
2. Settings are plain `Icon -> Icon` functions in a module `Icon` (`name`, `viewBox`, `rotate`, `flipHorizontal`,
   `flipVertical`), so the same functions configure (`Icon.fromPath d [ Icon.rotate 90.0 ]`) and transform
   (`arrow |> Icon.rotate 90.0`). `IconSetting` and the methods are gone.
3. Flips set (mirrored), they don't toggle.
4. The parsed path isn't in the value (a lazy field would break equality): an internal, thread-safe cache keyed by
   path data (`Icon.geometry`), so equal data shares one `PathGeometry` and its id in the rasterized-icon cache.
5. `ToString` is overridden (a record's default uses printf, which crashes under NativeAOT); `Icon.describe` gives
   the same text, and the inspector uses it.
6. Only `Icon` and its users changed; `PathGeometry` and `TextSystem.DrawIcon` stay as the lower-level API. M7b step
   2's `Material.star.Filled` (member access) is to be revisited in step 2's discussion.

Built 2026-11-16, verified by Tim the same day: no pixel changes (all 45 rendering tests unchanged); 269 unit tests pass (the toggle test now
checks that flips set).

## M7b step 2: the Material Symbols package (agreed 2026-11-16)

Measured from Tim's `MaterialIconData.cs`: 4,304 icons, outlined and filled (431 KB and 362 KB Brotli; 3.0 M and
2.1 M characters of path data), in Google's 960 × 960 box; no camelCase collisions; 7 names are F# keywords.
Agreed (recommendations; these replace point 4 of "Decisions for M7b" where they differ):
1. `Material.home`: static members on a `Material` type, each made when used (not module values, which would create
   all 4,304 icons, and decode the set, on first use of any).
2. Filled: `Material.star |> Material.filled` (and `Material.outlined`), not `Material.star.Filled`. A filled icon's
   name is "star-filled", so `Icon.describe` writes the Avalonia spec format ("star-filled-rotate90").
3. Keyword names in double backticks (``Material.``class`` ``).
4. `Material.icon "star-filled-rotate90"` reads the spec format (options from the end; '-' or ' ' in the name means
   '_'); unknown names throw an ArgumentException saying where names are; `Material.tryIcon` gives a voption;
   `Material.names ()` lists them.
5. Data: two embedded Brotli resources (no base64), each decoded on first use into UTF-8 bytes plus a name index;
   a path's string made (and cached) when first asked for; thread-safe.
6. `tools/GenerateMaterial.fsx` reads Tim's file and writes the resources and `Material.fs` (no doc comment per
   member).
7. Samples: Counter's switch uses `Material.lightMode`/`darkMode`; Hello's toolbar buttons get icons. The inspector
   keeps its own eight copied paths.
8. Credit (© Google, Apache 2.0) in the generated file, the project description and the README.
9. Tests: all 8,608 paths parse; spec text both ways; filled/outlined; keywords; unknown names; a rendering grid.
10. Naming (Tim asked whether the namespace should be `Scribble.Icons.Material`; kept as is, 2026-11-16): an icon
    set's package is `Scribble.Icons.<Set>`, and inside it the type `<Set>` sits in the namespace `Scribble.Icons`,
    so the type's full name is the package name. Apps write `open Scribble.Icons`, then `Material.home`. A
    `Scribble.Icons.Material` namespace would clash with the type, forcing a rename (`Icons.home`); several sets
    would then each bring a same-named type; and the `.filled`/`.outlined` shortcuts would need the longer open.
    One `open Scribble.Icons` lets later sets sit beside it (`Lucide.home`).

Changed the same day (Tim tried it in the browser: "IOCompressionBrotli_PlatformNotSupported"): the browser's .NET
has no `BrotliStream`, so point 5 became **plain text** resources (`Data/outlined.txt`, `filled.txt`), Tim's choice
of three (measured: Deflate would have made the browser download 1.37 MB and the desktop data 1.37 MB; plain text
gives a 0.58 MB browser download, since the publish Brotli-compresses the DLL, and +5 MB on the desktop; carrying
both formats would have put 5.8 MB in every app). Reading a set is now a copy and an index: under 1 ms at steady
state. The Counter browser download is 2.44 MB (1.86 MB without icons). Desktop and NativeAOT checks had passed
because they have Brotli; the browser is where the package must be tried.

Changed again the same day (Tim: "just for icons, I'm okay with the member access shortcuts like
Material.home.filled"): point 2 now also has `.filled` and `.outlined` as extension properties on `Icon`
(`MaterialExtensions.fs`, an AutoOpen module, so `open Scribble.Icons` brings them), lowercase as Tim wrote them.
They're the same functions as `Material.filled`/`outlined`, which stay for piping; on a non-Material icon they
return it unchanged. Only for icons: elsewhere settings stay functions.

Found while testing this: 108 unit tests failed now and then (first seen after a full build; reproduced about one
run in four with `DOTNET_TieredCompilation=0`): "static data being accessed recursively" in `$Palette`'s
initializer. `ThemeToken`'s id counter was a `static let mutable`, which F# initializes with Props.fs's file
initialization and checks on use; when two threads first used tokens and Props.fs at once, the runtime let one
see the file half done and the check threw (a framework bug, not only a test one: since M7 step 2b). Fixed by
moving the counters of `ThemeToken`, `Prop` and `Font` into small holder types (`Ids`, `FontIds`) with plain
`[<DefaultValue>] static val mutable` fields, incremented under a lock (F# wouldn't take those fields' address for
`Interlocked`). 15 stressed runs in a row then passed.

Icon and text alignment (Tim: "are the icons and text not perfectly aligned in the center on the hello sample
buttons"; measured at 3×: the icon was exactly centered and as tall as the capitals, but "Pause"'s lowercase
middle sat 1.5 DIPs lower, so the label looked low). Options: leave it (Material's own rule), an optical nudge, or
align to the lowercase. **Tim chose the nudge**: beside a TextBlock (`Button.text`) the icon's middle goes to
halfway between the middle of the capitals and of the lowercase, for one line centered in the slot:
`(ascender - lineHeight / 2 - (capHeight + xHeight) / 4) × size / unitsPerEm` below the middle (Inter: 93/2048 em,
0.64 DIP at 14 px). An icon alone or beside other content stays centered. `FontFace.CapHeight`/`XHeight` are new
(OS/2 version 2+, else the tops of 'H' and 'x', else 70% / 50% of the em). Measured after: the Pause icon's middle
went from 218.5 to 220.5 px (target 221.25; the rest is pixel snapping). Two unit tests (276); the pictures with
icon-and-text buttons regenerated (hello, hello-phone, counter ones, icons@1x/2x). Regenerating on Tim's GPU also
changed the dashboard and inspector pictures by anti-aliasing only (made on lavapipe; no icon-and-text buttons in
them); those were restored.

Icons sample (Tim asked for "an Icon sample project that just shows all of the material icons in a
scrollviewer"; recommendations agreed): `samples/Shared/IconGallery.fs` with a desktop host
(`Scribble.Samples.Icons`) and a browser host (`.Icons.Browser`, port 5282, also served by `tools/Scribble.Serve`).
Tiles of 112 × 76 DIPs (icon at 24, Google's name below, trimmed) in the samples' WrapPanel inside a ScrollViewer;
Outlined/Filled switch; a click shows the F# member (`memberName` repeats the generator's rule; a unit test checks
it against `Material`'s members). The grid is a `useMemo` on Filled, so picking an icon re-runs the component but
the differ skips the identical grid View. Measured at steady state (best of 5, Debug build, RTX 4070 Ti):
17,228 elements; building it 90 ms, Outlined/Filled 28 ms, a pick under 0.1 ms, the draw list 1.05 ms (only what's
visible is drawn), two re-layouts 6 ms. Fast enough without paging or virtualization (ListBox in M8 will virtualize).
Tests: `icon-gallery@1x` picture and the timings (IconGalleryTests.fs); 277 unit, 49 rendering tests.

Scrolling cost (Tim: the Icons sample was "super slow on browser and phone"; publishing with WebAssembly AOT
(`Scribble.Serve -- --aot`) "definitely made a difference", scrolling was where he still felt it). Measured on the
desktop, a scroll step cost 0.48 ms of layout plus about 1.2 ms of drawing, both growing with the element count
(17,228): about 0.78 ms of the drawing was skipping the 4,300 off-screen tiles, partly because every tile read
`scale`, and the theme's transition on `scale` made each read a second lookup. Agreed (option A, Core, before any
sample-only fix):
1. **An element that only moves isn't arranged again.** Children are positioned relative to their parent and an
   arrange stage sees only the size, so `Element.Arrange` with an unchanged size (after pixel rounding) and nothing
   invalidated updates the bounds and skips the stage. Scrolling moves the content: its subtree is no longer
   re-arranged (the WrapPanel no longer re-places 4,304 tiles). Layout per scroll step: 0.48 ms → 0.00.
2. **Drawing tests the clip first**, then (only for an element off screen) its plain `scale` without the transition
   lookup, and the transitioned value only if it has a tween. Elements scaled above 1, or easing back, are still
   drawn off screen as before. Off-screen skipping: about 0.78 → 0.59 ms (measured alone; full runs are noisy). The
   rest is visiting each tile: only virtualization (M8) removes it.
Two unit tests (279). Still to try: whether that's enough on Tim's phone with AOT; if not, the next step is the
sample's own `IconGrid` element drawing only the visible rows (option B), until M8's virtualizing ListBox.
A dragging scrollbar thumb stays in M8 (Tim, 2026-11-16).

**For M8's virtualization** (Tim, 2026-11-16, left for M8 after the scrolling work): "it really sucks to work
with in html-based web UIs. Like scrolling to an item is not usually a thing you can do if virtualization is turned
on, at least not from the blazor libraries I've used." So a virtualized list must keep what a plain one can do:
scroll to an item by index or key (also one never realized), bring the focused item into view, keyboard navigation
across unrealized items, and a stable scrollbar (sizes estimated, then corrected without jumps). Plan it from those
requirements rather than as a later add-on.

Built 2026-11-16, verified by Tim the same day (Windows, browser and phone, with the browser published AOT).
`src/Scribble.Icons.Material` (`MaterialData.fs` hand-written,
`Material.fs` generated: the `Material` type plus a same-named module with `filled`, `outlined`, `icon`, `tryIcon`,
`names`, as for elements). Found while testing: with an option given twice ("star-filled-outlined") the later one now
wins (reading from the end, the Avalonia parser let the earlier win). Measurements: the generated file compiles in
seconds; a set decodes in about 5 ms on the desktop at steady state (first run 6 ms); the outlined set takes 3.3 MB
of memory; the Counter browser sample's Brotli download went from 1.86 MB to 2.74 MB (the package's wasm file is
0.88 MB compressed; unused, the package is trimmed away entirely); a NativeAOT Counter publish has only FSharp.Core's
two warnings, and the exe runs. Hello's toolbar with icons needs about 1,040 DIPs beside the title, so at its default
960 DIPs it now sits on its own row below the title (the SplitRow at work). Tests: 5 new unit tests (274), 2 new
rendering tests (47: `material@1x`, `material@2x`); `hello@1x`, `hello-phone@3x`, `counter-demo@1x`,
`counter-demo-light@1x` regenerated and checked by eye. Wasm AOT size not measured (the interpreted publish was).

## M7b step 3: image cursors (agreed 2026-11-16)

Agreed (recommendations; the groundwork from 2026-11-15 stands: the OS shows the cursor, never our frame):
1. Icon cursors only for now; bitmap cursors wait for Image (M8), though the platform contract already takes pixels.
2. `Cursor.fromIcon icon [ Cursor.size; Cursor.hotspot (x, y); Cursor.fill; Cursor.outline; Cursor.outlineWidth ]`,
   a `Cursor` value used with `Layoutable.cursor` like the built-in ones. The settings are static extension members
   on the platform's `Cursor` (in Core), so they read `Cursor.size` with no module clashing with the alias.
3. Look: white fill, 1-DIP black outline (the shape grown by the width, softer on diagonals), 24 DIPs, hotspot at
   the center unless set.
4. Rasterized at the window's scale, made again on a scale change. Following the Windows cursor-size setting:
   later (Tim agreed).
5. Platform contract: `Cursor.Image of CursorImage` with `Key` and `Render: scale -> CursorBitmap` (premultiplied
   BGRA, size, hotspot in pixels); equality by key, so moves never re-make one. The key holds the icon's
   description, view box, settings, and the path data's length and hash (not the data: a 4,304-tile list of
   cursors would carry megabytes of keys).
6. Win32: `CreateIconIndirect` from a 32-bit top-down DIB (straight alpha) and an all-zero mask; cached per
   (key, DPI) per window and destroyed with it; on WM_DPICHANGED the one for the new DPI is shown.
7. Browser: JavaScript makes PNG data URLs (canvas) of the image at the page's density and at 1x, and uses
   `image-set(url(sharp) Nx, url(1x) 1x) hx hy, auto` when `CSS.supports` takes it (hotspot in CSS pixels, from
   the 1x image), else the 1x image. Re-made on ScaleChanged.
8. Demo: in the Icons sample every tile's icon is its cursor.
9. Tests: unit tests for the bitmap (sizes, hotspots, fill and outline pixels at 1x and 2x, no outline) and equality,
   and that hovering gives the window the image cursor and moves keep the same value.

Verified by Tim on Windows and in the browser 2026-11-16. Leak check (Tim asked, same day): the Win32 cache had
no limit, and each cursor costs 1 USER and 3 GDI handles of the process's 10,000 each, so hovering about 3,300
different tiles would have exhausted GDI for the whole process (measured: 3,000 cursors = +2,999 USER, +8,997 GDI).
Both platforms now keep the 32 cursors used last (`Scribble.Platform.RecentCache`: releases the least recent, never
the one just asked for, i.e. the one showing) and release the rest (DestroyIcon; the browser forgets the data URLs
in JS). Measured again with a real window and 4 rounds of 3,000 cursors: USER +31, GDI +93, managed heap flat,
everything freed on close. A cursor made again after eviction costs about 0.3 ms. The icon placement (center, flip, rotate, scale) moved from TextSystem into
`Rasterizer.rasterizeIcon`, shared by the screen and cursors. The inspector shows an image cursor as "icon <name>".
Checked: cursor images by eye (colorize and edit, 1x and 2x: white, black outline, hotspot), a NativeAOT publish of
the Icons sample (no new warnings; it runs). Not checked by me: the cursors on screen (Win32 and the browser,
including a monitor with another scale and the browser's hotspot with image-set). Cost: giving each of the
gallery's 4,304 tiles a cursor value made building it about 15% slower (113 → 130 ms, Debug); fine for a demo.
283 unit tests, 50 rendering tests.

## Evaluation 2026-11-16: have we kept to our philosophies?

Tim asked for a check of where we are against the project's principles, run as a grilling (the
`grill-with-docs` skill: questions in rounds, each with a recommendation; Tim accepted every recommendation).

**Audit (code checked, 2026-11-16).** Held: no C# anywhere; borrowed only wgpu-native and HarfBuzz (packages:
HarfBuzzSharp native assets, xUnit in tests); no printf family in shipped code; no computation expressions or
builders; no nulls in the public API (only at boundaries: null strings become "", JS interop); the power rule
(the only timers: the browser's wake `setTimeout 0` when work is queued, and Win32's 10 ms timer during a live
resize that only delivers frames already requested). Broken or unrecorded: three `static let mutable` counters in
`Element.fs` (shared by every tree and thread, against the rule written after the static-init crash);
"frame work proportional to what's visible" (drawing visits all 4,304 gallery tiles, building realizes 17,000
elements; known, deferred to M8); `DrawList`'s optional arguments (an exception to "settings everywhere" never
written down); CLAUDE.md at 324 lines, mostly status history repeated here.

Decided:
1. **Docs:** decisions stay in this file (no ADR files); a `GLOSSARY.md` at the root defines the project's words
   (`grill-with-docs` would have used ADRs; our per-milestone decision lists fit how we work).
2. **Static counters fixed now:** `ElementCounts` (Props.fs), a holder type like `Ids`, changed under a lock.
3. **Frame work proportional to what's visible** is a rule we break today; virtualization is M8's first step.
4. **`DrawList` keeps optional arguments**, recorded as an exception to "settings everywhere": it's the low-level
   drawing API, called per shape per frame, and a settings list would allocate each time.
5. **CLAUDE.md trimmed** to a working summary (rules, commands, projects, architecture, adding an element, a short
   status); its milestone summaries moved below, unchanged.
6. **"Low level by intent" stays**, clarified: OS APIs (windows, cursors, IME, accessibility) are the platform,
   not borrowing. Image decoding (own or borrowed) is decided in M8 planning.
7. **Words (GLOSSARY.md):** *theme value*, not "token" (the public type is `ThemeValue<'T>`, paired with
   `AppValue`); the internal `ThemeToken` is now `ThemeKey`, `ThemeRef.Token` `.Key`, `Element.TokenValue`
   `ThemeValueOf`, the public `PropertyValue.Token` field `PropertyValue.ThemeValue`, parameters named `token`
   `themeValue`, `TokenTests.fs` `ThemeValueTests.fs`. Older sections here keep "token" as history.
8. **Property / setting / value:** a property is the typed key (`Prop<'T>`), a setting pairs it with a value in a
   list, a value is what an element has now plus its source.
9. **View / element / element type / settings type / component** defined; "marker type" is now *settings type*
   in prose.
10. **Virtualization's bar, as rendering-test assertions** (Release): 100 vs 10,000 items realize the same number
    of elements (within a screen-dependent margin); a scroll step's layout and draw list for 10,000 within 1.5× of
    100; scrolling to item 9,000 by index or key works without realizing the items between.
11. **CLAUDE.md's shape:** keep how we work, commands, projects, architecture, rules, adding an element; status in
    a few lines; point to GLOSSARY.md.
12. **Props** means only a component's input; *property* is never shortened to "prop" in prose (`Prop<'T>` keeps
    its name: element authors see it, and a rename would touch every element).
13. **Visual state** (hover, pressed, focused, disabled, registered ones; the `States` module keeps its name),
    *state* (a component's `useState`), *model* (an Elmish app's).
14. **Layer, element tree, host, style, theme** defined; "popup", "portal" and "overlay" are avoided in favour of
    layer.

Not changed, noted: the immutable `static let` values left in `Element.fs` (an empty array, two properties) and
Win32Platform's class name hold no shared state; `Inspector.fs` is 1,719 lines in one file (worth splitting when
it next grows).

## Decisions for M8 (core controls)

Planned 2026-11-16 in rounds (questions with recommendations; Tim accepted each). Each step gets its own round
before it is built, and Tim tries each step before the next.

**Round 1 (2026-11-16): steps and virtualization.**
1. **Four steps:** (1) scrolling and virtualization: the rest of ScrollViewer (draggable thumb, keyboard,
   horizontal, scroll-into-view), a virtualizing `ItemsView` with scroll-to-item, the Icons gallery rebuilt on it,
   and the agreed bar as tests; (2) layout: Grid, DockPanel, Canvas, WrapPanel (promoted from the samples), dashed
   borders; (3) simple controls: CheckBox (on a ToggleButton), Slider, Image (image decoding decided there),
   TabControl; (4) layers: the "show this in a layer" element (Popup) and tooltips, ListBox (selection on
   ItemsView), ComboBox, Dialog, Toast.
2. **Virtualization lives in the items element, not the scroller** (WinUI ItemsRepeater style, over WPF's
   virtualizing panel that must be the ScrollViewer's direct content and Flutter's ListView that is the scroller
   itself): a ScrollViewer tells its descendants which part of them is visible (the *viewport*, passed down during
   layout), and an `ItemsView` anywhere inside realizes only the items in it. Headers above a list in the same
   scroller, several lists on one page and nested scrollers all keep virtualizing.
3. **`ItemsView`: data plus a view function**, never a list of Views: `ItemsView.source` (indexable: `'T array` or
   `IReadOnlyList<'T>`; an F# list is copied into an array once), `ItemsView.key`, `ItemsView.view` (called only for
   realized items), `ItemsView.layout` (`ItemsLayout.stack` default, `ItemsLayout.wrap w h`, `wrapFill` since 2026-11-20, `ItemsLayout.grid
   columns`). Name `ItemsView` (Tim: yes to the recommendation over Items, VirtualList, Repeater).
4. **Sizes variable by default**: measured sizes remembered by key, unmeasured ones estimated from the average;
   when an estimate is corrected the first visible item stays where it is on screen (scroll anchoring); the
   scrollbar's length may change a little as items are measured. **Exact fast paths**: a fixed item height for
   stack, fixed cells for wrap and grid (laid out by arithmetic, exact scrollbar from the start).
5. **Scrolling to an item is an action, through a controller**: `let scroller = ctx.useScroller ()`,
   `ItemsView.scroller scroller` (and `ScrollViewer.scroller`), then `scroller.ScrollTo(key, align)`,
   `ScrollToIndex`, `ScrollToOffset`, `ScrollBy`, `ScrollToTop`; `scroller.Offset` and `scroller.FirstVisible`
   (key plus offset within it, to restore a position exactly). Requests apply at the next layout, so scrolling to
   an item added in the same handler works; a key not in the source then does nothing (logged in debug builds).
   Unrealized items: positioned by estimate (exact with fixed sizes), realized, measured, corrected in the same
   frame. Elmish: `Cmd.scrollTo name key align` finds the element by name and uses its controller. Rejected: a
   declarative `scrollTo (key, version)` setting (re-renders would fight the user's scrolling). **Automatic:**
   focus moving to an element (Tab, arrow keys) brings it into view through every scroller it is in, including
   items not yet realized; element authors call `ctx.BringIntoView()`. **`ScrollAlign.Nearest` is the default**
   (no move when already fully visible, else the least scroll), with `Start`, `Center`, `End`. **`ScrollTo`
   animates** briefly (about 200 ms) by default; `animate = false` jumps; long distances always jump (animating
   past thousands of items would realize them all). Controller methods are calls, like hooks (agreed exception
   to settings lists).
6. **Items scrolled out are recycled**: their elements are reused for items scrolling in when the element type
   matches (the differ patches them with the new item's view); component state inside an item that scrolled out
   is dropped, as in every virtualizing list; state that must survive belongs in the model, keyed by item. The
   **focused item stays realized** while it is scrolled out (focus never disappears). No `keepRealized` setting:
   that is a plain panel.

**Round 2 (2026-11-16): step 1 details.** Before: a 4-DIP thumb over the content, no track, not draggable; a wheel
notch jumps 48 DIPs; touch drags without a fling; no keyboard or horizontal scrolling.
7. **Scrollbar:** thumb drag scrolls proportionally; a click on the track pages toward it (Windows; jumping to the
   spot, as macOS can, would be a later setting); no auto-hide (fading needs frames after the user stops, and hidden
   bars are hard to find). **Space for it is reserved by default** (Tim asked whether an overlay bar hides items:
   it does, so the first recommendation, overlay, was revised): `ScrollViewer.scrollBarSpace Reserved` keeps a
   12-DIP gutter on the scrolling side whenever that direction is `Auto`, whether or not the content overflows
   (a gutter that appears only on overflow narrows the content, which can re-wrap under the threshold and loop, the
   classic WPF and browser problem); the thumb is 4 DIPs in it and widens to 10 with a faint track under the
   pointer or while dragged. `Overlay` draws over the content (image viewers, maps, phone-style lists; the app pads).
   The same layout on every device (no per-device switch). Width and colors are theme values.
8. **Keyboard:** keys nothing handled bubble from the focused element to the nearest ScrollViewer: ↑/↓ one wheel
   step, PageUp/PageDown the viewport minus one step, Home/End, ←/→ when horizontal scrolling is on; Space is left to
   buttons. A click on empty space inside a ScrollViewer focuses it (not a Tab stop; accessibility may revisit).
9. **Horizontal:** `ScrollViewer.horizontal` and `ScrollViewer.vertical` take `ScrollBarMode.Auto | Hidden |
   Disabled` (Hidden scrolls without a bar; Disabled gives the content the viewer's size, so text wraps); defaults
   vertical Auto, horizontal Disabled (today's behaviour). Shift+wheel and sideways deltas scroll horizontally.
10. **Feel:** a touch fling after release slows to a stop (frames only while moving; a touch stops it); no bounce at
    the ends (later, mobile polish); wheel notches animate over about 100 ms, precise touchpad deltas apply directly
    (notch vs pixel deltas: browser `deltaMode`, Win32 multiples of 120; to check while building).
11. **An ItemsView outside any ScrollViewer realizes everything** (its viewport is unlimited: a plain panel, not an
    error). The inspector shows "realized 40 of 4,304" on an ItemsView.
12. **Samples:** the Icons gallery becomes an ItemsView with fixed cells, plus a search box that scrolls to the first
    match; the Counter demo's Layers tab list becomes 10,000 messages of varying length (variable sizes) with
    "Jump to #…" and "Add message" (add and scroll to it).

**Step 1 as built (2026-11-16; verified by Tim on Windows the same day).** What the decisions above became, and choices made while building:
- **Element types got input hooks** (`Element.pointerDown`/`pointerMove`/`pointerUp`, `keyDown`, `panEnded`,
  `clipChildren`), run as events bubble after each element's own handler, like `Element.wheel`. A type hook that
  handles a press captures the pointer to its element (a thumb drag). An internal `ScrollHooks` record
  (`MakeVisible`, `ViewportChanged`, `ScrollToKey`) is how ScrollViewer, ItemsView and `BringIntoView` talk.
- **The viewport** is found by walking up from the ItemsView through the ScrollViewers (intersecting what each
  shows); a ScrollViewer whose offset or size changed tells the elements inside (`ViewportChangedBelow`, a walk
  of the realized elements: proportional to what's visible). An ItemsView whose realized range no longer covers
  what shows (or covers far more) measures again, and **layout repeats** (up to 6 passes) while a pass asks.
- **Variable heights:** measured heights by key, summed by index in a Fenwick tree (O(log n) positions; unmeasured
  items count as the average), so a scroll step doesn't grow with the item count. Anchoring keeps the first
  visible item (or the item scrolled to) where it is when measurements correct estimates above it.
- **Recycling** keeps up to 64 elements per element type; elements with a component inside aren't reused (its
  state would show in another item), only dropped.
- **`ItemsView.source` takes any `seq<'T>`** (arrays and `IReadOnlyList`s used directly, lists and others copied
  once): separate overloads for arrays and `IReadOnlyList` were ambiguous in F#.
- **`isTabStop`** (new setting, default true): a ScrollViewer is focusable by a press but not a Tab stop.
- **Theme:** `Palette.scrollTrack` added; `ScrollViewer.barSize`, `thumb` and `track` settings.
- **Samples:** the Icons search is a focusable field taking keys (a stand-in until M9's TextBox; no phone keyboard).
  The Hello dashboard's ScrollViewer uses `Overlay` (its cards' 24-DIP margin holds the bar, and it must match
  the hand-drawn dashboard pixel for pixel).
- **Screenshots:** three references changed (icon gallery, layers-scroll, the Counter Layers tab on a phone),
  regenerated on Tim's machine (RTX 4070 Ti, Vulkan), not on lavapipe.
- **A crash found in checking (fixed):** dragging the thumb hard through 10,000 messages killed the process with
  "Internal CLR error". A crash dump showed a background GC scanning the stack of the thread inside `hb_shape` and
  reading the `fixed` pin of the string being shaped (a pinned `char&` into its characters) as an object start.
  Pinning with a `GCHandle` instead stopped it (stress test: about 1 crash in 5 runs before, 0 in 40 after); the
  Win32 window title pins changed the same way. Scrollers named for `Cmd.scrollTo` are registered without making
  a new weak reference at every arrange.
- **Not built:** keyboard paging that repeats while the track is held; bounce at the ends; keys reaching a
  ScrollViewer when nothing has focus.

**Round 3 (2026-11-16): step 2, layout panels and dashed borders** (a grilling: questions with recommendations;
open ones are marked).
13. **Attached settings:** what a child tells its parent (`Grid.row`, `DockPanel.dock`, `Canvas.left`) are static
    members of the parent's settings type that any element accepts (`'k :> Layoutable`), ignored under any other
    parent. New glossary term "attached setting". Rejected: wrapping each child (`Grid.cell (1, 2) view`: an extra
    element per child, in the way of keys and recycling).
14. **Grid rows and columns:** `Grid.columns "auto, *, 200"` (the `Length` strings, comma-separated) for simple
    grids; when a column needs more, a settings list per column: `Grid.columns [ GridColumn.create [ GridColumn.width
    "*"; GridColumn.minWidth 120.0; GridColumn.maxWidth 480.0 ] ]` (rows: `GridRow.height/minHeight/maxHeight`); both
    make the same definitions. No `[ Auto; Star 1.0 ]` overload, no CSS `minmax()` in the strings. Children:
    `Grid.row`, `column`, `rowSpan`, `columnSpan`, `Grid.cell (row, column)`. No definitions = one cell (children
    overlap). Min/max with stars follow WPF: a star column held at its min or max, the other stars share the rest;
    space left when every column is held stays empty; min/max apply to Auto and fixed columns too (Tim asked about
    min/max, 2026-11-16).
15. **Gaps:** `Grid.rowSpacing`/`columnSpacing`, `DockPanel.spacing`.
16. **Grid sizing:** fixed and Auto rows first, stars share the rest by weight; stars act as Auto in unlimited
    space; a child spanning Auto and star rows sizes only the Auto ones; `Percent` is a share of the Grid's size;
    rounding leftovers handed out so cells tile with no gaps; no auto-placement (unplaced children go in (0, 0)).
17. **DockPanel:** WPF semantics (`dock` per child in order, `lastChildFill` default true). Later: `Dock.Fill` (section "Tab order is view order; `Dock.Fill`").
18. **Canvas:** `left/top/right/bottom` per child at its natural size; the Canvas's size is its children's extent
    (not 0 as in WPF), so it scrolls inside a ScrollViewer.
19. **`Layoutable.zIndex`** (default 0) on every panel, for drawing and hit testing; ties keep view order.
20. **WrapPanel into Core**, moved as it is (`spacing`, `lineSpacing`, rows as tall as their tallest child), plus
    the attached setting `WrapPanel.breakAfter true` (the next child starts a new row: WinForms' flow break). Same
    idea as WinForms' FlowLayoutPanel (Tim asked); no orientation or flow direction (right-to-left comes with bidi,
    for the whole tree; vertical wrapping is rare, ItemsView's `wrap` covers big grids). SplitRow and CardGrid stay
    in the samples.
21. **Dashed and dotted borders:** `Border.borderStyle Solid | Dashed | Dotted`, `Border.borderDash (dash, gap)`;
    defaults 3× and 2× the thickness, dots spaced by the thickness; dashes follow rounded corners and are evened out
    so the pattern closes; same shader and draw call, instance stays 96 bytes; `DrawList.StrokeRect ?dash`.

**Round 4 (2026-11-16): step 2, the rest** (Tim: yes to all).
22. **Inspector:** a selected (or picked-over) Grid shows its row and column lines dashed and its gaps shaded in the
    highlight layer; attached settings appear in a child's properties, and one its parent doesn't read shows
    "ignored: parent isn't a Grid".
23. **`Separator`** now: a horizontal or vertical 1-DIP line, solid by default, `Separator.style Dashed`, color
    `Palette.border`; drawn as a thin rectangle. A general line primitive and shapes wait for a later shapes step.
24. **Samples:** a Layout tab in the Counter demo: a Grid form (Auto label column, star field column with a
    `minWidth`, a button column, a span), a DockPanel app shell with Separators, a Canvas with draggable boxes
    (pointer capture; a click brings one to the front with `zIndex`; the selected one gets a dashed outline), and
    WrapPanel chips with a `breakAfter`. Hello and Icons only switch to Core's WrapPanel.
25. **Tests:** unit tests for Grid (stars, min/max with stars, spans, unlimited space, `Percent`, exact tiling at
    1.25× and 1.5×), DockPanel (order, `lastChildFill`), Canvas (extent, right/bottom), zIndex (draw and hit-test
    order), WrapPanel `breakAfter`, ignored attached settings; screenshots of dashed and dotted borders at 1× and 2×
    (square and rounded, thin and thick), the Layout tab on desktop and phone, the inspector's Grid overlay.
26. **Existing code** (the inspector panel, the Counter tab page) moves to the new panels only where it gets clearly
    simpler and every existing screenshot stays identical; otherwise it stays, and the report says where.

**Step 2 as built (2026-11-16; not yet checked by Tim).** What decisions 13–26 became, and choices made while building:

- **Elements:** `Elements/Grid.fs`, `DockPanel.fs`, `Canvas.fs`, `WrapPanel.fs` (moved from `Samples.Shared/Adaptive.fs`,
  which keeps only `SplitRow`) and `Separator.fs` in Core. Attached settings are `Prop.attached owner name default
  affects` (sets `Prop.AttachedTo`; the name reads "Grid.Row"); the settings live on the panel's settings type but
  accept any `Layoutable` (`Grid.row<'k when 'k :> Layoutable>`), so `Button.create [ Grid.row 1 ]` compiles.
- **Grid:** `Grid.columns "auto, *, 200"` (bad parts throw with the part and the accepted forms) or
  `Grid.columns [ GridColumn.create [ GridColumn.width "*"; GridColumn.minWidth 120.0 ] ]`; same for rows. Tracks
  are `GridTrack { Size; Min; Max }`. `Grid.cell (r, c)` wins over `row`/`column`; places outside the grid are
  clamped into it. Column and row lines are snapped to physical pixels by cumulative position, so cells tile
  exactly at 1.25×, 1.5× and 1.75× (tested). `Grid.edges element` (public, for tools) gives the laid-out lines.
- **Change to decision 16 (agreed by Tim, 2026-11-16):** "a child spanning Auto and star rows sizes only the Auto ones" made
  the Layout tab's notes field (spanning the star field column and the Auto button column) push the Auto column to
  the text's full width, and the Grid past the phone's edge. Now, as in CSS Grid, **a child that spans a star track
  doesn't size Auto tracks**: the stars take what it needs (it counts toward the Grid's natural size through the
  stars). A child spanning only Auto and fixed tracks still grows the Auto ones. Test: "a child spanning a star
  column doesn't widen an auto column it also spans".
- **DockPanel:** `DockPanel.dock Dock.Top` (an enum: Left, Top, Right, Bottom, and later Fill; default Left), `lastChildFill`
  (default true), `spacing` between strips. Hidden children take no strip.
- **Canvas:** `Canvas.left/top/right/bottom` (`left` wins over `right`, `top` over `bottom`); unplaced children at
  (0, 0); its size is the children's extent.
- **zIndex:** `Layoutable.zIndex` on every element; `Element.DrawOrder` (stable sort, cached and checked against the
  children and their values) is used for drawing and hit testing; focus order and `Children` keep view order.
- **Separator:** `orientation`, `thickness` (1), `lineStyle` (not `style`: `Separator.style` is the module's style
  function, as on every element), `color` (themed: `Palette.border` in the base styles).
- **Dashes:** `StrokeStyle` (Solid | Dashed (dash, gap) | Dotted) on `DrawList.DrawBox`/`StrokeRect`; `Border.borderStyle`
  (`BorderStyle.Solid | Dashed | Dotted`) and `Border.borderDash (dash, gap)` (default 3× and 2× the thickness).
  The pattern rides in the shape instance's last word (dash and gap in 1/16 px; 0 = solid), so it's still one draw
  call. Dashes are evened out to meet around the shape; a box no thicker than its line is drawn as an open line
  with a dash or dot at both ends.
- **Inspector:** a Grid's lines (dashed, violet) and its gaps (shaded) over the highlighted Grid; attached settings
  listed as written (`Grid.row`), with "ignored: parent isn't a Grid" under one whose parent isn't that panel.
  The rendering tests now see the inspector's internals (to draw the highlight over the Layout tab's form).
- **Counter demo:** a sixth tab, Layout (`samples/Counter/LayoutTab.fs`). With six tabs the Light/Dark switch no
  longer fits beside them at 520 DIPs and moves to its own row (SplitRow, as on a phone). Existing code: nothing
  else moved (decision 26); the tab row stays a WrapPanel in a SplitRow.
- **Tests:** 321 unit tests (26 new in `PanelTests.fs`), 56 rendering tests (`dashes@1x/2x`, `layout-tab@1x`,
  `layout-grid-overlay@1x`, `counter-phone-layout@3x`); `counter-demo`, `counter-demo-light`, the five other
  `counter-phone-*` and both inspector pictures regenerated for the sixth tab (looked at; the inspector still picks
  "+"). Note: `SCRIBBLE_UPDATE_SNAPSHOTS=1` rewrites pictures that differ within tolerance too (it rewrote both
  dashboard pictures, which were restored: Hello looks the same with Core's WrapPanel).

**Round 5 (2026-11-17): step 3, simple controls** (a grilling; Tim: yes to all ten). Facts it rests on: none of
these exist yet, but `States.ofProp`, `Style.prop` and the inspector's own drawn checkbox were made for them; the
draw list has no images (one instanced draw call over two glyph atlases); `dispatch` runs `update` at once on the
UI thread and the view is rebuilt before the next layout, so a model-owned value shows a click in the next frame;
the differ resets a setting a new view leaves out.
1. **Step 3 splits:** **3a** ToggleButton, CheckBox, Switch, Slider, TabControl; **3b** Image (decoding and how
   images reach the GPU get their own round). Tim tries each before the next.
2. **Who owns a value** (CheckBox, Slider, TabControl now; ListBox, ComboBox later): when the view gives the value
   setting, the control shows exactly that, and a click raises the change event and changes nothing until the view
   says so; when it doesn't, the control keeps its own value in a local (so a rebuild doesn't reset it) and still
   raises the event.
3. **Names:** the value is named for what it is, the event is `on<Value>Changed` with the new value:
   `CheckBox.isChecked` / `onCheckedChanged (fun (v: bool) -> ...)`, `Slider.value` / `onValueChanged`,
   `TabControl.selected` / `onSelectedChanged` (not FuncUI's `onChecked`/`onUnchecked` pair).
4. **Toggles in 3a:** `ToggleButton` (public base: a Button with `isChecked` and a `checked` visual state),
   `CheckBox` and `Switch` on it (the Counter's hand-made Light/Dark switch moves onto Switch). RadioButton waits
   for step 4, with ListBox's selection.
5. **`CheckBox.isIndeterminate`**: a dash, display only ("select all" with some selected); a click reports `true`;
   no three-state cycling.
6. **Slider reports** `onValueChanged` on every change (each drag move, each key step) and `onValueCommitted` when
   a drag ends or a key step lands.
7. **Tab pages are realized on first visit, then kept** (hidden, state intact); `TabItem.keepAlive false` lets a
   page go when left.
8. **TabControl in a view:** settings, `TabControl.create [ TabControl.selected 0; TabControl.tabs [ TabItem.create
   [ TabItem.header "Elmish"; TabItem.content (...) ] ] ]`; `TabItem.header` takes a string or a View. A
   data-driven form (dynamic tabs) can come later.
9. **The selected tab is an index** in 3a (keys can come with dynamic tabs).
10. **Arrow keys select** (WAI-ARIA's automatic activation): ←/→ move the selection, Home/End the ends; Tab moves
    into the page.

**Round 6 (2026-11-17): step 3a details** (Tim: yes to all seven).
11. **"Control" is a word we use**, for an element the user interacts with to change a value or trigger an action
    (glossary); "element" stays the general word.
12. **Glossary terms "Controlled" and "Own value"** for decision 2's two ways (not React's "uncontrolled", and not
    "internal state", which would clash with a component's state).
13. **Slider:** `minimum` 0, `maximum` 100, `step` 1 (`step 0.0` continuous; values snap to the step); ←/↓ →/↑ one
    step (1% when continuous), PageDown/PageUp 10%, Home/End the ends; a click on the track jumps there and starts
    a drag (a slider's track is its scale, unlike a scrollbar's, which pages).
14. **`Slider.orientation Vertical`** in 3a (minimum at the bottom).
15. **CheckBox and Switch take Button's `text` and `child`** as their label: the box (or track and knob) first, then
    the label; a click anywhere on either toggles; the focus ring goes round both.
16. **Tabs that don't fit scroll sideways** in one row (a horizontal ScrollViewer with hidden bars; arrow keys and
    selection bring the selected tab into view); no wrapped tab rows, no squeezing.
17. **Samples and inspector:** the Counter's tabs move onto TabControl and its Light/Dark switch onto Switch; a new
    Controls tab shows CheckBox (with the indeterminate "select all"), Switch and Slider (both orientations,
    controlled and own-value side by side); the inspector's "Show defaults" box becomes a CheckBox.

**Round 7 (2026-11-17): step 3a looks and tests** (Tim: yes to all three).
18. **Looks from existing theme values only** (`accent`, `onAccent`, `control`, `border`, `text`, `textDim`,
    `shadow`, `focusRing`), reacting to hover, pressed and disabled as Button does. CheckBox: an 18-DIP box,
    corners half of `cornerRadius`; off `control` with a `border` edge, on `accent` with an `onAccent` tick,
    indeterminate an `onAccent` dash (tick and dash are path icons inside Core, not the Material package).
    Switch: a 36 × 20 track and a 14-DIP knob; off `control` and `textDim`, on `accent` and `onAccent`. Slider: a
    4-DIP `control` track filled with `accent` up to the value, an 18-DIP `onAccent` thumb with `shadow` and a thin
    `border` edge. Tabs: text headers, unselected `textDim`, selected `text` with a 2-DIP `accent` underline, a faint
    `border` line under the row.
19. **Motion:** the Switch knob slides (120 ms) and the tab underline slides to the new tab (150 ms); nothing else
    animates (the tick appears at once; the slider thumb follows the value exactly, easing would lag a drag).
20. **Tests:** unit tests for controlled and own values (a rebuild doesn't reset an own value), events with the new
    value, Slider snapping, clamping, keys and `onValueCommitted` (once per drag, once per key step), TabControl's
    realize-on-first-visit, keep and `keepAlive false`, arrow/Home/End, the selected tab scrolled into view, and
    disabled controls ignoring input; screenshot tests for each control's states (off, on, indeterminate, hover,
    focused, disabled) in dark and light, the Controls tab, and the phone layout with scrolling tabs; and the
    visible-work rule: unvisited pages cost nothing (measure and draw-list counts the same with 2 or 20 unvisited
    tabs).

**Step 3a as built (2026-11-17; not yet checked by Tim).** As decided, with these details and additions:
- **Own value = a value set by code.** The differ resets only settings a previous view had, so a control that isn't
  controlled keeps its value with `element.Set` (`ControlValue.change` in `Elements/Control.fs`: "controlled" means
  the current view has the setting). The inspector lists it as set by code. Consequence: an ItemsView row recycled
  for another item keeps the own value of the item it was made for; in an ItemsView, give the value (README says so).
- **New element part `Element.click`**: the type's own click behavior, after the element's `onClick`; elements whose
  type has one are clickable without an `onClick` (ToggleButton toggles this way, so Enter and Space work as for
  Button). Input: a press taken by a type's `pointerDown` now makes that element alone look pressed (`States.pressed`
  on a Slider's thumb; a ScrollViewer while its thumb is dragged); nothing clicks.
- **States** (in Core, from properties declared in Props.fs): `States.isChecked`, `States.isIndeterminate`,
  `States.isSelected`. Not `States.checked`: F# reserves `checked` (warning FS0046, an error here). The Styles tab's
  own chip state was renamed "chosen" so the inspector doesn't list two "selected".
- **`element.Focus()`** (public): moves keyboard focus from code (`IElementRoot.Focus`, served by the layer's input
  router) and brings the element into view. TabControl's arrow keys use it.
- **Looks:** ToggleButton has Button's look and the accent while on. CheckBox and Switch use `background` and
  `borderBrush` for the box or track and a new `markColor` for the tick, dash or knob (the tick is a path icon in
  Core). Slider: `track`, `fill`, `thumb`, `thumbBorder`, `thumbShadow`, `overlay`; a white thumb can't lighten on
  hover, so hover gives its edge the accent instead; the focus ring goes round the thumb (`FocusRing.drawAround`).
  TabHeader (a Button made by the TabControl) is dim text, `text` when hovered or selected, its focus ring inside its
  edge (the header row clips); TabControl draws the row's line (`line`) and the underline (`underline`), sliding
  150 ms. Only the selected header is a Tab stop.
- **TabControl internals:** `tabs` holds the TabItem views as plain values; the TabControl realizes the header row
  (a ScrollViewer, horizontal `Hidden`, with a StackPanel of TabHeaders) and the visited pages itself (managed
  children, like ItemsView), styling them as it adopts them. The selected header is brought into view after the
  frame: asked during arrange, the ScrollViewer's request was swallowed when its ancestors finished arranging (found
  by the phone picture; the unit test had passed only because hover kept causing layouts).
- **Counter:** a DockPanel with the Light switch (a Switch, controlled by `Theme.current`) above a TabControl that
  keeps its own selection (`onSelectedChanged` sets the window title); seven tabs with the new Controls tab. The
  Load page now starts its one-second load when first shown, so `IdleTests` waits for it in real time.
- **Tests:** 17 in `ControlsTests.fs`; `TabsTests` rewritten for the TabControl; the theme-switch and title tests
  updated. Rendering: `controls@1x`, `controls-light@1x`, `counter-phone-controls@3x` new; the phone test checks the
  selected header is fully visible and stops its "nothing past the right edge" check at scrollers. Regenerated
  (looked at): `counter-demo`, `counter-demo-light`, the six other `counter-phone-*`, `layout-tab`,
  `layout-grid-overlay`, `inspector-pick` (its pick moved onto the "+" button's padding), `inspector-floating`.
  368 unit tests, 61 rendering tests.
- **Follow-ups from Tim's first look (2026-11-17):** the Layers tab was slow to open in the interpreted browser
  build: it made all 10,000 message texts up front (about 220,000 words; 8 ms on the desktop, far more interpreted).
  A message is now its number; its text is made when its row is shown. Pick mode showed an element far from the
  pointer: it only looked again when the mouse moved, so a list scrolled under a still pointer (rows recycled
  elsewhere) left the highlight on the old row. It now looks again after every frame while picking (one hit test;
  a change costs one more frame). The pointer-and-row mismatch Tim then narrowed down (browser only, only under
  Rider's debugger, after a pick click) was a **lost pointerup**: a temporary pointer trace showed the picking press
  with no release after it, so the host kept the button down and the highlight layer kept the pointer (no hover
  anywhere; every later click picked again, the button count climbing). Chrome drops input that arrives while a page
  is paused, as it can be briefly under a debugger; losing pointer capture can do the same without one. First guess,
  wrong but kept: pick mode reacted to moves after it had ended (it now only picks in pick mode).
  **Decision (platform contract): every press ends with a release or a cancel.** The browser page tracks the pointers
  it saw pressed and sends PointerCanceled (no click: where the button came up isn't known) when a move arrives with no
  buttons down, a new press comes for a pointer still pressed, or capture is lost without a pointerup
  (`scribble-platform.js`). Win32 already cancels on WM_CAPTURECHANGED. 
  Also asked for: a pick scrolls the tree to the selected row (named `inspectorSelectedRow`; `BringIntoView` after the
  first frame that has the row, so the tree's ScrollViewer takes it), and the wheel scrolls the app while picking.
  **Decision: `Layer.letWheelThrough`** (default false): the wheel skips such a layer even where it has an element;
  the inspector's highlight layer sets it. Not a general "unhandled wheels fall through" rule: a ScrollViewer at the
  end of its range leaves the wheel unhandled, and scrolling past the end of the inspector's tree would then have
  scrolled the app behind the panel. 376 unit tests.

## M8 step 3a+: the inspector tree on ItemsView (planned and built 2026-11-17)

The inspector's tree built an element for every row of every expanded element (a keyed `StackPanel.items` in a
ScrollViewer), against the rule that frame work follows what's visible. Decided with Tim (grilling, 2026-11-17),
before 3b:

1. **The tree only** goes on an ItemsView; the properties list stays a StackPanel (bounded by one element's
   properties, not by the app).
2. **`ItemsLayout.fixedStack 22.0`**: every row 22 DIPs, layer headers too (they were 24), so positions and the
   scrollbar are exact with no measuring.
3. **Recycling and the pointer: already right, now guarded by tests** (no Core change). Planned as a Core fix on the
   belief that a recycled row under a still pointer kept the old item lit; tests disproved it. In one layout an
   ItemsView realizes what's in range first (reusing elements released in earlier layouts) and releases what's out
   of range after, so an element can't change items within a layout, and the refresh after it moves the hover to
   whatever is under the pointer (40 jumps, never stale). A fix was built and reverted: it changed nothing a test
   could see. `InputTests` "hover follows a still pointer through jumps that recycle rows" keeps the order honest.
   GLOSSARY "Recycling" states the behavior.
4. **Scrolling to the selection: the scroller's `ScrollTo(key, Nearest)`**, also for a row never realized; it replaces
   3a's named row revealed after a frame (which only worked once the row existed).
5. **Tests:** the same realized rows for 100 and 10,000 expanded rows; a wheel scroll under a still pointer moves the
   hover (the inspector, and a plain ItemsView in Core); a pick of an element whose row was never realized scrolls it
   into view; existing inspector tests pass; pictures change only by the layer header height.
6. **A press on an element given to another item mid-press:** no click on release. Also already so: when the element
   holding a press leaves the tree, the refresh after that layout ends the press (`endPress`), so the element coming
   back later for another item can't be clicked by it. `InputTests` "a pressed row given to another item before the
   release doesn't click" follows the element through jumps until it's back, moves onto it and releases. (That
   refresh also ends a touch pan that started on the row; not looked at here.)
7. **The tree's row data is still rebuilt on each refresh** (a walk over what's expanded, plain data, no elements;
   up to 10 times a second while the app changes): measured in the test at 10,000 rows and recorded here; made
   incremental only if it shows up in a profile.
8. Done now, as its own step with "as built" notes, README, checklist and a commit.

**As built (2026-11-17, not yet checked by Tim).**

- **Tree:** `InspectorTree` is an `ItemsView` (`ItemsLayout.fixedStack 22.0`, keyed by `rowKey`, the state's
  `TreeScroller`); `Rows()` returns an array and notes whether the selected element is listed. Layer header rows are
  22 DIPs. The properties list is unchanged.
- **Reveal:** after a frame where the selected element's row is listed (and a reveal is pending, set by `Select`),
  `TreeScroller.ScrollTo(element, Nearest, animate = false)`. The named row (`Names.SelectedRow`) and its search
  through the inspector's layers are gone.
- **No Core change** (decisions 3 and 6 above): two `InputTests` guard what ItemsView already does.
- **Found on the way:** the reveal's layout makes the input router look again under a still pointer, so after a pick
  the app element under the pointer gets its hover look at once (it used to wait for the next move). Correct; the
  inspector tests' `pickAppButton` now moves off the button and waits for the fade before checking looks.
- **Timing (decision 7):** a tree refresh (the dispatcher's wake, the row walk, the ItemsView's new source and the
  panel's frame) takes **1.97 ms with 10,000 listed rows, 0.45 ms with 100** (Release, fake platform, best of 20
  after 5 warm-ups). Kept as is.
- **Tests:** 380 unit tests (4 new: the two Core guards; the tree realizes the same rows for 100 and 10,000
  elements, fewer than 60; a wheel over the tree moves the hover to the row under the still pointer); the reveal
  test adapted (it finds the selected row by its color, and fails with the reveal turned off). 61 rendering tests;
  `inspector-pick@1x` regenerated (rows 2 DIPs higher; regenerated on Tim's GPU, not lavapipe).

## M8 step 3a++: sideways scrolling for stacked ItemsViews; the inspector tree scrolls sideways (planned and built 2026-11-17)

Tim (2026-11-17): "should the inspector view have a horizontal scroll bar for deep trees?" Rows indent 12 DIPs per
level in a panel about 380 wide, so names get cut off from about 25 levels down. Decided with Tim (grilling, the same
day):

1. **In Core, not inspector-only:** a stacked ItemsView in a ScrollViewer that scrolls sideways gets as wide as its
   widest row (rejected: the inspector guessing a width from the deepest row, which cuts long names and pads short
   ones).
2. **Now, before 3b**, as its own step; 3a+ is committed first, separately.
3. **Inspector layer header rows scroll sideways with everything else** (not pinned at the left edge).
4. **A pick also scrolls the tree sideways** so the selected row's chevron and name are in view (a little indent kept
   to its left), only when the name isn't already visible; hovering never scrolls sideways.
5. **The extent's width:** the ItemsView keeps each item's natural width by key when it measures it (as it keeps
   measured heights); the width is the largest of those for items still in the source. It grows when a wider item
   is first measured, doesn't shrink while scrolling, shrinks when that item leaves the source; unmeasured items
   don't count (rejected: the widest realized item only, which jumps on every vertical scroll).
6. **Rows are arranged as wide as the extent or the viewport, whichever is larger**, so backgrounds span the width.
7. **Stacked layouts only** (`stack`, `fixedStack`); wrap and grid keep flowing to the viewport's width.
8. **Automatic** when the ItemsView's ScrollViewer has `ScrollViewer.horizontal ScrollBarMode.Auto`; no new
   ItemsView setting.
9. **Items are then measured at unlimited width** (text no longer wraps); README says so. Vertical-only stays the
   default.
10. **Tests:** a wide row makes the bar appear and rows span the extent; vertical scrolling without a wider row keeps
    the width; removing the widest row shrinks it; Shift+wheel scrolls sideways; the cost rule (100 vs 10,000 items,
    scroll step within 1.5×, same realized count); the inspector: a deep pick scrolls the name into view sideways, a
    deep tree shows the bar and a shallow one doesn't.
11. **GLOSSARY: Extent** added.

**As built (2026-11-17, not yet checked by Tim).**

- **Key store:** natural widths by key next to the heights (`RecordWidth`), and `MaxWidth` over the keys in the
  source: kept as a running maximum, recomputed (over what was measured, not over the source) only when keys left
  or arrived or the widest got narrower. Cleared with the heights on a width change.
- **ItemsView measure:** at unlimited width, stacks record each measured item's width and report `MaxWidth` (it
  used to report the widest realized item: the jumping rule decision 5 rejected); wrap and grid take the
  viewport's width (they used to measure one column at unlimited width, twice as tall in the test).
- **Found on the way (Core bug):** scrolling a stack to an item brought its slot into view, and at unlimited width
  the slot is infinitely wide: the ScrollViewer's sideways offset became NaN. Scrolling to an offset used x 0, so it
  would have snapped a sideways scrolled list back to the left. Vertical scroll requests on stacks now use a 0-wide
  rect where the viewport already is sideways (`rowsAt`).
- **Inspector:** the tree's ScrollViewer scrolls sideways. Rows keep the last 24 DIPs of their indent inside an
  inner Border around the chevron and type name, named `inspectorSelectedLabel` on the selected row; the frame
  after a pick's scroll to the row, that Border is brought into view (Nearest: nothing moves if it shows; animated
  sideways, the vertical scroll before it isn't).
- **Tests:** 389 unit tests (9 new: the extent rule (stack and fixedStack), the width over scrolling, removing and
  re-adding, wrap and grid flowing to the viewport, the cost rule sideways (same realized count, scroll step within
  1.5×), Shift+wheel, scrolling by key and offset keeping the sideways position, the inspector's bar for deep and
  shallow trees, a deep pick bringing the name into view). The new ItemsView tests fail on the old ItemsView; the
  NaN test and the inspector reveal test fail without the `rowsAt` fix. 61 rendering tests, no pictures changed.
- **Also (Tim, 2026-11-17): forced property states draw.** A CheckBox forced to `checked` in the inspector got the
  checked colours but no tick (render functions read the real `isChecked`). Now `ctx.Get` in a render function reads
  `true` for a bool property behind a forced state (`States.ofProp` with a test that passes for `true`; the state
  carries that forced value); `element.Get`, layout and input still read the real one. Forcing only adds a state:
  a checked look can be shown on an unchecked box, not the other way. 2 unit tests (CheckBox tick, dash, Switch
  knob draw exactly as the real ones; clearing takes the tick away), failing without the change. 391 unit tests.
- **Forcing a state off (Tim, 2026-11-17: "can we make it so a 'forced' state is the opposite of the current
  state?").** Decided the same day ("yes to all seven"):
  1. A STATES chip forces the opposite of what it shows when clicked (on if off, off if on); clicking again stops
     forcing. Fixed at the click, not a live opposite. A state is real, forced on or forced off.
  2. A forced-off `disabled` reaches everything inside (a disabled panel shown enabled), except what has its own
     `isEnabled false`; a forced-off `hover` and the others apply to the element only.
  3. Render functions see forced-off states (`ctx.IsPointerOver`, `IsPressed`, `IsFocused`, `IsFocusVisible` false,
     `IsEnabled` true; a bool property behind a state reads `false`); input still sees the real states.
  4. A `States.define` state forced off works for styles only (render functions see it only through a property).
  5. Inspector: forced on keeps the accent fill; forced off is the accent outline with the name struck through;
     "• forced" covers both; "Clear forced" clears both.
  6. API: `element.ForceState(state, on)` forces on or off; `element.UnforceState state` stops one;
     `ClearForcedStates()` as before; `ForcedStates` lists `(state, on)` pairs.
  7. Tests: a checked CheckBox forced off draws unchecked and stays checked; a disabled Button forced off looks
     and draws enabled; forced-off `disabled` on a disabled panel reaches inside except a child disabled itself;
     the chip goes off → forced → real; forced-off focus-visible draws no ring.
  **As built:** `Element` keeps `(state, on)` pairs and on/off flags for the built-in states; `ctx.IsPointerOver`,
  `IsPressed`, `IsFocused`, `IsFocusVisible` read false under a forced off; `LooksDisabled` walks up to the first
  element with `disabled` forced (on or off) or its own `isEnabled false` (it replaces `HasForcedDisabledAbove`;
  without any forced `disabled` it's just `not IsEnabled`). `States.ofProp` carries the forced on and off values
  (`true`, `false` for a bool state whose test is the value itself). The STATES chip draws its strike-through as a
  1-DIP line over the name in a one-cell Grid (Core has no text decorations yet). 5 new unit tests (396); each
  forced-off branch turned off fails one. 61 rendering tests, no pictures changed.

## M8 step 1b: non-uniform virtualization (2026-11-16)

Tim (2026-11-16): "we need to add a series of tests for non-uniform virtualization". Decisions (agreed the same day,
"yes to all"):

1. **Where:** `tests/Scribble.Tests/VirtualizationTests.fs` (bare element tree, seeded generators, no new library;
   a failure names its seed and step) plus a GPU stress variant in `ScrollStressTests.fs`.
2. **Invariants after every step:** realized items consecutive and back to back; the viewport covered; every
   realized item near the viewport and the count bounded by it, not by the item count; the reported first visible
   item at the top with the right offset; the offset inside the content; once everything was measured, the content
   exactly the rows' sum. Positions compared with the (fractional) offset may differ by half a pixel, since rows are
   drawn on whole pixels.
3. **Scenarios:** mixed heights 20–400 with 3,000 outliers down and back; small-then-large (a wrong first estimate);
   a deep jump then all the way up; a row taller than the viewport; rows resized; rows inserted or removed above,
   inside and below; width changes that re-wrap; zero-height and hidden rows; seeded random sequences of all of
   these; the cost rule (100 vs 10,000 rows within 1.5× per scroll step, same realized count).
4. **Scroll anchor:** the first visible item, **by key**, keeps its place on screen whenever anything above it
   changes size or is inserted or removed; after a width change it stays at the top (the same fraction of it
   scrolled past). (Before: by index, so inserts above jumped by what was inserted.)
5. **Zero-height runs:** write the test; if it realizes everything, bring options (below).
6. **Bugs found:** fixed in the step and reported; behaviour questions go to Tim.
7. "M8 step 1b"; GLOSSARY gets **scroll anchor**.

**As built (not yet checked by Tim).** 20 tests (one a theory of 3 seeds) and one GPU stress test. Run against the old
code, 14 failed; after the fixes all pass. Fixes:

- **Anchor by key** (ItemsView measure): taken before the source or width change (the key, how far into it the
  viewport's top is, its height, and the next 16 keys in case it's removed); after a scroll request, taken again
  where the view now is. Placement: the anchor is realized and measured first (after a width change every height
  is new, and one measured row moves the average), the view moved to it as estimated, the rows around it realized,
  then moved again to it as measured; never above 0.
- **ItemsView's viewport from the live offset**: it was computed from the content's last arranged position, so a
  second measure in the same pass, or one after a non-animated scroll request, used a stale viewport (the anchor
  was corrected twice; a jump realized around the old place).
- **ScrollViewer `makeVisible`** converts back to content coordinates with the content's arranged position (what
  `BringRectIntoView` added), not with -offset: the arranged position is rounded to pixels, and `ScrollToTop`
  after a fractional offset (160428.43) left 0.43.
- **Estimates on whole physical pixels** (the average rounded with the element's scale), so estimated positions and
  corrections don't move what shows by fractions (it caused 1-pixel jumps when an estimate changed).
- One older test ("the first visible item stays put while estimated sizes above it get measured") scrolled its
  item 300 DIPs down, to the very edge of the realized range: it passed only because the viewport was read a step
  late. It now takes 6 steps instead of 10.
- "A row below the first visible grows" moves the *offset* (rows above were only estimated and the average
  changed) but nothing on screen: the test checks the screen.

Screenshots unchanged (57 rendering tests pass with the same references).

**Zero-height runs: option A (Tim, 2026-11-16).** 10,000 consecutive zero-height rows are all realized in one measure: each
starts above the viewport's bottom, so the realize loop never stops. Options:

- A. **Document it** (recommended): hidden items belong out of the source (filter the list); `isVisible false` on
  many items is the app's choice. Add a debug-build warning when one measure realizes over 1,000 items.
- B. **Stop after a number of zero-height items** (say 200) in one pass and estimate the rest at 0; scrolling further
  realizes the next part. Keeps the cost bounded, at the price of positions below the run being estimates until then.
- C. **A cap on items realized per measure** with the rest next frame: extra frames, against the power rule. Not
  recommended.

Built: realizing over 1,000 items in one range (inside a ScrollViewer) writes a `Debug.WriteLine` warning naming
the rows and saying to filter hidden items out of the source. The test ("a long run of zero-height rows is
realized at once, with a warning in debug builds") documents the behaviour: 3,000 zero-height rows realized, the
other invariants hold, the warning written (debug builds).

## Live resize lag (2026-11-17)

Tim: with a game on the left monitor and an app on the right, the app "lags like crazy" when a resize starts or
changes direction, and the game's monitor flickers; both backends. A trace (temporary, `SCRIBBLE_TRACE_SURFACE=1`
in `WebGpuSurface.fs`: frames, swap chain rebuilds and waits per second) of a real drag on Tim's machine showed the
cause in our code: Windows sends a `WM_SIZE` for every mouse movement, and each one rebuilt the swap chain at once
(`ElementHost` → `surface.Resize` → `wgpuSurfaceConfigure`) and drew a frame. 85–127 rebuilds a second at about
5.6 ms each on Vulkan: up to 72% of every second, more than a 144 Hz frame's 6.9 ms with the drawing, so Windows'
size-move loop couldn't follow the mouse. (Scripted resizes in 16 ms steps hadn't shown it.) So many rebuilds are
also the likely cause of the other monitor's flicker (the driver re-deciding how windows reach the screen); to be
confirmed by Tim with the game running.

Decisions (Tim, "yes to all"):

1. **Rebuild only when a frame begins:** `IRenderSurface.Resize` records the size; `TryBeginFrame` rebuilds when
   it differs from the configured one. `Size` reports the recorded size (what layout uses). Like the browser
   surface already did.
2. **During a live resize, at most one frame per display refresh:** between `WM_ENTERSIZEMOVE` and
   `WM_EXITSIZEMOVE`, `WM_SIZE` draws now only when a refresh period (read from the window's monitor when the
   resize starts; 90% of it, so frames don't alternate between refreshes) has passed since the last frame, else it
   requests one and the size-move timer draws it (Windows timers tick every 15.6 ms by default, so the timer alone
   would have meant about 64 frames a second). Other size changes (maximize, snap, DPI) still draw at once.
   Trade-off: the content can trail the window edge by up to one refresh while dragging.
3. **D3D12 by default on Windows** (then Vulkan/Metal/D3D12, then all backends): it presents through DXGI's flip
   model and rebuilt its swap chain in 1.7–2.5 ms against 4.5–7.6 ms on Vulkan (RTX 4070 Ti). All 57 rendering
   tests pass on it (6 rebuild + test cycles, none aborted). This replaces "wgpu's default backend stays" (M0); the
   black flash when shrinking was a Vulkan observation, to be rechecked on D3D12.
4. **Two frames in flight:** tried, rejected: a scripted drag went from 4.8 to 3.7 s (ideal 0.8 s) for a frame more
   of input lag. One stays.
5. **Render thread:** not needed for this bug; an open item for heavy apps (the window's thread still waits for the
   screen image in `TryBeginFrame`; with the GPU shared with a game that wait grows, and input waits with it).
6. **The trace** stays until Tim has checked the fix with the game running, then goes (frame timings in the stats
   overlay are a separate, later item). Removed 2026-11-17 (Tim), after it found the G-SYNC cause below; the
   live resize with the game running is still to be rechecked (CHECKLIST.md).

Not verified here: a real mouse drag (scripts can't run Windows' size-move loop without moving Tim's cursor; in a
script every size change still gets a frame, through the main loop), and anything with the game running.

## Cursor stutter with G-SYNC: D3D12 presents through DirectComposition (2026-11-17)

Tim: with a game on the left monitor, the mouse cursor stuttered over a Scribble window on the right monitor
(Counter's Layout tab), only over our window, only while nothing animated: a hover fade, a box drag, or a longer
`tintTime` made it smooth for as long as they lasted. Same on Vulkan and D3D12.

**Cause:** NVIDIA's G-SYNC (variable refresh rate) applied to our window. With VRR the monitor refreshes when the
app presents, and the cursor only moves on screen at a refresh; we draw on request, so over still content the
monitor fell to its minimum rate and the cursor stepped. Games present constantly and never show it. Found by
elimination: the trace showed our frames were cheap (layout under 1 ms) and the frames came from hover fades (a
temporary test counting what invalidates); turning G-SYNC off ended it; "G-SYNC for full screen mode only" did
not (the driver or Windows still applied VRR to the window). The 11-15 build (`48eaf47`, run from a worktree)
stutters too: nothing we changed caused it; most likely the driver Tim installed that day made it show. Pointer
moves have never requested frames, and the Win32 loop has always drawn on request.

Decisions (Tim):

1. **No settings for users to change** (Tim: "I should not have to update computer settings for a UI library").
2. **Not "draw while the pointer moves"** (frames every refresh while the mouse moves over the window, stopping
   ~150 ms after): rejected by Tim; it would bend the power rule. It stays the fallback only for a platform where
   nothing else works.
3. **D3D12 presents through a DirectComposition visual** (`WGPUInstanceExtras.dx12PresentationSystem =
   DxgiFromVisual`, offset 68, checked against wgpu.h v29.0.1.1), as browsers do: the image goes through Windows'
   compositor, which the driver doesn't treat as a game, so the monitor keeps its refresh rate. Verified by Tim
   with G-SYNC on (windowed and full screen): smooth. Always on; no setting (none needed so far).
4. **D3D12 stays the default on Windows.** Tim asked for Vulkan back as the default; not done, because the fix
   is D3D12-only and Vulkan stutters with G-SYNC (and rebuilt swap chains 2–3× slower). Vulkan on Windows stays
   available (`SCRIBBLE_GPU_BACKEND=vulkan`) with this known issue.
5. **WebGPU stays** (Tim asked about binding Vulkan directly, and about OpenGL): no API runs everywhere (browser:
   WebGPU/WebGL; Apple: Metal, OpenGL deprecated; Xbox: D3D12; PlayStation: its own), and neither WebGPU, DXGI
   nor Vulkan has a "not a game" or "no VRR" flag (checked in wgpu.h/webgpu.h: present modes, alpha modes, frame
   latency and instance flags don't touch it). A direct backend stays possible later if wgpu blocks us somewhere.
6. **Open items:** Vulkan on Windows with G-SYNC (would need Vulkan to render into an image shared with a DXGI
   composition swap chain, outside wgpu; or `VK_EXT_full_screen_exclusive` "disallowed", effect unknown, not
   exposed by wgpu). macOS: expected fine (Core Animation composes every window; ProMotion raises the refresh on
   input), to check on the first Mac build: hover over still content on a ProMotion screen and on an
   Adaptive-Sync monitor; if it stutters, try `CAMetalLayer`/`CADisplayLink` preferred frame rates first.
7. **Who can still hit it on Windows:** every GPU that supports G-SYNC has D3D12 drivers (G-SYNC: Kepler, GTX 650
   Ti Boost and newer; G-SYNC Compatible: Pascal, GTX 10 and newer), so the Vulkan stutter only affects someone
   who chooses Vulkan or whose D3D12 fails (e.g. a broken driver install). Rendering in Vulkan into an image
   shared with a composition swap chain would fix that too, at little frame cost (one window-sized copy, about
   0.02–0.05 ms on an RTX 4070 Ti, a few tenths of a millisecond on a weak integrated GPU, and a GPU-side fence
   between the APIs), but it's days of native interop outside wgpu: left until Vulkan on Windows matters to users.

**How Avalonia does it** (checked in Avalonia.Win32 12.1.6's XML docs, 2026-11-17): the same way. Its default
rendering is ANGLE (OpenGL ES over Direct3D 11), then software; its default presentation (`CompositionMode`) is
WinUI Composition (Windows 10 1803+, "recommended ... allows window acrylic effects and high refresh rate
rendering"), then DirectComposition, then a redirection surface (compatibility only). A plain DXGI swap chain on
the window (`LowLatencyDxgiSwapChain`, what we had before) is opt-in, recommended only for low input latency.
Its `Vulkan` and `Wgl` rendering modes are opt-in and can't use the composition modes ("can only be applied with
AngleEgl"), so like our Vulkan path they present without the compositor.

| | Avalonia (default) | Scribble |
| --- | --- | --- |
| Draws with | Direct3D 11 (ANGLE) | Direct3D 12 (wgpu) |
| Presents through | WinUI Composition, else DirectComposition | DirectComposition |
| Vulkan on Windows | Opt-in, without the compositor | Opt-in, without the compositor |

The difference: Avalonia prefers WinUI Composition for acrylic and blurred window backgrounds. If we want those
later, we'd build our own composition tree and give wgpu our visual (wgpu.h says that's supported); wgpu-native
itself offers only DirectComposition (one visual over the window) and the WinUI `SwapChainPanel`.

## The inspector inside scrolled content: `VisibleBounds` (Tim, 2026-11-17)

The highlight drew an element's whole rectangle, so an element inside a ScrollViewer (an ItemsView is as tall as
the whole list) went past the viewport, over the header, and off the window as it scrolled. Agreed:

- **`Element.VisibleBounds` in Core** (public): the element's rect in tree coordinates, cut by the clip of every
  ancestor that clips its children (a ScrollViewer's viewport, `clipToBounds`, an element type's
  `Element.clipChildren`); empty when it's clipped out or it or an ancestor isn't visible. `VisiblePart rect`
  does the same for any rect in the element's own coordinates (the inspector uses it for the margin bands).
  Transforms aren't applied, as in `PositionInTree`. In Core rather than the inspector because only Core knows
  each type's clip; later users: scroll into view, tooltips, accessibility.
- **The highlight draws only the visible part.** A cut edge gets a dashed line ("it goes on past here"):
  blue dashes on a dark edge like the corner marks, 3 DIPs outside the viewport's edge, in line with the marks
  (inside, it blended with the list's text; Tim), or just inside when outside would be off the window. The
  selection's corner marks show only at real corners. The label keeps the element's full
  size, placed above, else below, else just inside the top of what shows. Scrolled out entirely: nothing is
  drawn (no off-screen pointer, for now).
- The tree shows an ItemsView's "realized N of M" whether its row is open or not (it used to vanish on open
  rows), and the details panel has an "items" line with it.

## Recycling measured; styles on realization; new sources (Tim, 2026-11-17)

Measured with `tools/Scribble.Bench` (Release, Tim's desktop; Counter-like rows: a Border with wrapping text,
HarfBuzz; 10,000 items, 800 × 600, 48-DIP steps, best of 5): a scroll step costs 0.09 ms (0.14 ms with text never
shaped before), of which the draw list is about 80 µs and layout 12 µs (42 µs with new text). Recycling an element
(view, attach, patch) costs about 1 µs per row; dropping the pool (creating every row) measured the same; the
theme-epoch bumps cost about 24 extra theme lookups per step, too little to show. New text: about 29 µs per row to
measure (shaping), 4.6 µs once shaped. "Add message" (a new array of 10,000): 0.5 ms mean, p95 3 ms, 1.5 MB
allocated, nearly all of it taking in the new source (every key computed and boxed, the key index and the height
sums rebuilt); patching the 32 realized rows again is 21 µs. And every realized row's restyle was still queued
after its frame (see the first decision). Decided:

1. **A realized element gets its styles when it's realized, before it's measured** (not in a second restyle
   pass): ItemsView realizes during layout, after that frame's restyle pass, and the host ignores what a frame
   asks for, so rows were measured unstyled and restyled only when something else asked for a frame (a style
   that changes size would also have measured them wrong).
2. **A recycled element always gets a full (deep) restyle**: correct whatever the patch changed; not measurably
   slower.
3. **Taking a new source stays O(items), made lean, with no new API**: unboxed keys, the key index updated rather
   than rebuilt, height sums built in one pass. "Proportional to what's visible" is about frames (scrolling);
   a model update is O(items) in the app too (a new array). Telling ItemsView what changed (a change-tracking
   collection or change hints) was rejected: it breaks "items are data, a new array is a change".
4. **"Recycling"** is the term (glossary).
5. **`tools/Scribble.Bench` and Core's `ItemsProfile` counters stay** (internal, off unless the benchmark sets
   them; one static bool read per realized item).
6. **Not doing:** cheaper recycling itself (it's about 1 µs per row). **Later:** the draw list's cost per frame,
   measured first on Tim's phone with an AOT build.
7. **Checking 1:** unit tests: rows styled through a class (padding) have it in their height in the same
   `UpdateLayout` that realizes them, with no restyle left queued; a themed Button in a row that just scrolled in
   has its theme background in its first frame. The benchmark's "left queued after their frame" goes to 0.
8. **Work left over after a frame** (a restyle still queued): a warning in debug builds naming the element, no
   extra frame (asking for one would break the power rule).
9. **Checking 3:** a unit test on allocations: 10,000 int-keyed items scrolled to the middle, a new array with one
   more item: at most 64 KB allocated by ItemsView (about 1.4 MB before; the app's array not counted). The
   benchmark's "Add message" reports the time; the goal is the source-dependent part of an update (its layout,
   without the draw list) under 100 µs on Tim's desktop in Release (about 430 µs before). Restated by Tim on
   2026-11-17: first set as the whole update under 100 µs against 516 µs, which included the draw list.

**Decision 1 as built (2026-11-17):** `ItemsView`'s `realize` calls the tree's internal `RunStylesWithin item`
after attaching and patching: it matches what's queued inside that item now and leaves the rest of the queue for
the next layout (no public API change). The debug warning is in `ElementHost` (after `DrawFrame`). Benchmark: the
rows left queued after their frame went from about 1.2 per step to 0 in every scenario; restyling now shows inside
layout (0.7 µs per step for the Counter's rows, 2.3 µs with a Button in each row); step times unchanged. Two unit
tests (`ItemsViewTests`); both fail with the call removed.

**Decision 3 as built (2026-11-17):** a `KeyStore` per ItemsView, made by the `key` setting (which knows the item
and key types; `ItemsView.key` now requires `'K : equality`, which a usable key always had): keys in their own
type in a buffer that doubles, the key index (`Dictionary<'K,int>`, built on the first lookup) updated in place,
measured heights by key, and the height sums kept for their whole capacity, so an append leaves them as they are
(anything else rebuilds them in one pass). A key is boxed only when ItemsView asks for it as an object (realized
items, the anchor), once per position. `ItemsSource` exposes the typed collection, read in one tight loop per kind
(array, `IReadOnlyList`, other). Tests: appending one item to 10,000 allocates 11.6 KB in Scribble (limit 64 KB;
about 1.4 MB before); a randomized test checks the store updated in place against one built from scratch over 400
edits (appends, front inserts, removals, swaps, trims).

Benchmark, "Add message" (whole update: tree update, layout, draw list): 516 → 148 µs mean, p95 3.2 → 0.17 ms, max
6.8 → 0.44 ms, 1.5 MB → 136 KB allocated (80 KB of that is the benchmark's own new array). The first goal (the
whole update under 100 µs) was set against a figure that included the draw list (about 78 µs, the same in every
scenario), so it couldn't be met; decision 9 now names the source-dependent part, which went from about 430 µs to
about 64 µs of layout (**met**): reading 10,000 keys (about 35 µs, the O(items) part decision 3 accepted) and
patching the 32 realized rows again (about 21 µs).

## Rendering test hangs (2026-11-16)

Tim asked to look into "partial" rendering test runs (35–38 of 57 reported as "Passed!"). They were hangs:
`--blame-hang-timeout` killed the test host, saved a dump and aborted the run, while the minimal summary line
still said "Passed!" for the tests that had finished. Reproduced in 2 of 4 runs right after a rebuild (0 of 14
without one); it's also the old "hangs when chained after other dotnet commands" known issue.

**Cause (from the hang dump):** a deadlock between the Windows loader lock and the Vulkan loader's lock. One of
wgpu's "WGL Instance Thread"s was inside NVIDIA's `nvoglv64.dll` `DllMain` (holding the loader lock), waiting for a
thread to exit; exiting threads need the loader lock; another thread held the Vulkan loader's lock while waiting to
load that DLL; 9 more threads waited on the Vulkan lock (rendering, `DeviceRelease`, `InstanceRelease`), one on the
loader lock while unloading a D3D12 driver during adapter enumeration. What set it off on our side: every test class
had its own fixture and GPU device (15, created and destroyed on parallel threads; the fixture's comment wrongly said
"one for the whole run"), and every instance enabled every backend, OpenGL included (a WGL thread and the OpenGL
driver for nothing).

Decisions (Tim, "yes to all"):

1. **Tests:** creating and disposing GPU devices is serialized (`GpuLifetime.Lock`); drawing stays parallel. (One
   shared device would share one glyph atlas across threads, not safe today; no parallelism would be slower.)
2. **Instances:** `WGPUInstanceExtras` chained to the instance descriptor (struct checked against wgpu.h v29.0.1.1:
   112 bytes; only chain, `backends` and `flags` set, flags `Default | WithEnv` as before). Backends: the forced
   one (`SCRIBBLE_GPU_BACKEND` / `WebGpuDevice.backend`), else primary (Vulkan, Metal, D3D12, browser); with no
   adapter there, once more with all (OpenGL). Measured on Tim's machine (fresh processes, instance + adapter, 3 runs
   each): all backends 147–154 ms + adapter 294–389 ms, 7 more threads; primary 98–100 ms + adapter 254–264 ms,
   2 more threads. (`opengl32.dll` still loads: NVIDIA's Vulkan driver imports it.)
3. **Proof:** 20 cycles of rebuild + rendering tests: 20 of 20 ran all 57, none aborted. CLAUDE.md now says how to
   read a test run (look for "Test Run Aborted" and the total).
4. **Dumps:** the 6 hang dumps (13 GB) deleted; `TestResults/` git-ignored.

## One entry point and the sample layout (2026-11-16, before M8 step 1)

Tim: "I only want the user to have to worry about swapping out the dependencies of Scribble.Desktop and
Scribble.Browser", and the sample project names were inconsistent (Hello's browser host was
`Scribble.Samples.Browser`). Agreed (recommendations, Tim: yes to all):
14. **`App.run` everywhere**, `AppSetting list -> int`, defined by both packages in `namespace Scribble.Core` (opened
    by every app), so app code has no platform `open`. Desktop: blocks until the window closes. Browser: starts the
    app and returns 0 (the page keeps it running; start-up errors go to the console and the page's error box).
    `App.runInBrowser` removed. Referencing both packages makes `App.run` ambiguous, which is right: an app is one or
    the other. Rejected: one `Scribble` package choosing at run time (desktop apps would ship browser code, browser
    bundles Win32 code that trimming might not fully remove).
15. **No web files in browser apps**: `Scribble.Browser` brings `index.html` and `main.js` (`build/page/`, defined as
    static web assets by `build/Scribble.Browser.targets`, which NuGet imports by itself; the repo's samples import
    it in `samples/Directory.Build.targets`), used when the app has no `wwwroot/index.html`. The SDK's own discovery
    assumes the project's `wwwroot` (a linked file published fine but the development server looked for it in the
    project and answered 404), so the targets call `DefineStaticWebAssets` with the page folder as content root.
    The swap is two lines of the project file: the SDK (`Microsoft.NET.Sdk.WebAssembly` can't be set by a package)
    and the package. **Later (Tim): installable project templates** (`dotnet new`), which will rely on this.
16. **Each sample's app is a library with two thin hosts**: `samples/Hello`, `Counter`, `Icons` (libraries),
    `X.Desktop` and `X.Browser` (Program.fs identical: `App.run (app @ [ Inspector.enable [] ])`), and
    `Samples.Shared` (Adaptive.fs). Project names drop the `Scribble.Samples.` prefix (module names inside stay);
    solution folders per sample; host defaults (not packable, invariant globalization, NativeAOT for `.Desktop`,
    `-p:BrowserAot=true` for `.Browser`) in `samples/Directory.Build.props`, by project-name suffix. Tests reference
    the sample libraries instead of linking their files. Ports unchanged (5280, 5281, 5282). Hello's browser host no
    longer prints "interpreted"/"WebAssembly AOT" (its Program.fs is the shared one now). `HelloApp.settings` is now
    `HelloApp.app`, like the others.
17. **Done before M8 step 1**, as its own change, so step 1's diff is only about scrolling.

## Scribble.Serve hub (2026-11-16)

Tim: a landing page in the serve tool, as a hub on another device. Agreed (recommendations, Tim: yes to all):
1. **Port 5279 becomes the hub** (it was the certificate page): plain HTTP, so a device opens it before it trusts
   our certificate. Sample cards (title, one line, Open) whose links use the address the device reached the hub by
   (one link each instead of every address); a live check (a `no-cors` fetch to a sample over HTTPS succeeds only
   once the device trusts the certificate) shows "✓ trusted" or opens the certificate download and install steps;
   the build (WebAssembly AOT or interpreted, and when published: `artifacts/serve/build.txt`). In `--self-signed`
   mode it says each sample warns once instead.
2. **QR code of the hub in the terminal**, with the **QRCoder** package in this tool only (never in Scribble or what
   apps ship; "borrow only WebGPU and HarfBuzz" is about the framework). Half-block characters, light modules drawn
   (reads right on a dark terminal like Rider's).
3. **The hub's address is the best guess**: an up IPv4 adapter with a default gateway that isn't virtual (Hyper-V,
   WSL, VPN, ...); the others are listed under it.
4. **No link back to the hub inside the samples** (they're drawn on a canvas; serve-only UI would clutter them).

## Project review 2026-11-17

Claude reviewed the whole project at commit `475f929` (findings: `.scratch/project-review/review.md`, 22
items graded A/B/C), then went through them with Tim as a grilling (`grill-with-docs`). Holding: the rules in
CLAUDE.md (printf, `fixed`, `static let`, no C#, no public nulls), the power rule, the inspector on Core's public
API only, and NativeAOT (Counter published and run again 2026-11-17: only FSharp.Core's IL2104/IL3053).

Round 1 (Tim accepted every recommendation):

1. **Errors in handlers, views and render functions go to `App.onError`** when the app sets one, and the app
   keeps running (Debug builds also write them to the debugger). Without `onError`, the app still crashes loudly
   as today. Applies to the desktop and the browser alike.
2. **A clock tests can control:** `ElementHost` takes frame time from a replaceable clock, and the inspector's
   delayed refresh uses it instead of `Task.Delay`, so `IdleTests` (40 of the unit tests' 40 seconds) and the
   inspector tests stop waiting on real time and `Thread.Sleep`. Built before step 3b.
3. **A checking pass before more building:** new work pauses until Tim has gone through CHECKLIST.md (M8 steps
   2, 1b, 3a, 3a+, 3a++); meanwhile only the review's small fixes (1, 2 and the docs drift).
4. **Registries belong to the app's host, not the process** (engineering rule 6, for hot reload): whatever an
   app registers (custom visual states today, anything like them later) is dropped with the app. Decided now,
   built with M10.
5. **A `semantics` stage for element types before step 4:** role, name and states (no screen reader yet), filled
   in for the 3a controls too, so step 4's controls are built with it rather than retrofitted.

Round 2 (Tim accepted these; the reference pictures and the name went to round 3):

6. **One property set twice in one settings list warns in Debug builds** (any element). `Button.text` stays as
   the shorthand and `Button.content` the general form; the inherited `Button.child` can't be hidden.
7. **`AppSetting<unit>` stays:** apps made only of components annotate `let app: AppSetting<unit> list`
   (README explains it). Non-generic app settings would cost `App.onKey`/`App.onFrame` their typed messages.
   Closes the open item.
8. **Control naming pattern:** the value setting is named for what it is (`isChecked`, `value`, `selected`), its
   change event `on<Name>Changed`, and where a drag is involved its end `on<Name>Committed`.
   `Separator.lineStyle` stays (Separator isn't a Border).
9. **`Inspector.fs` is split during the pause** into `Format`, `InspectorState`, the highlight, the small
   elements and the views; its `WrapRow` gives way to Core's `WrapPanel`. No change in behaviour.
10. **A full glyph atlas:** Debug builds warn when it clears twice in one frame; the multi-page atlas stays for
    later.
11. **An element leaving its tree drops its forced states** (so the process-wide counts come back down);
    `Element.RenderOverflow` becomes internal.
12. **Wall-clock assertions get a `Timing` trait,** left out of the default run and run in Release before each
    milestone report; count-based checks stay in the normal run.
15. **License: MIT.** A LICENSE file is added now; the repository stays private until Tim decides to push.

Round 3 (Tim accepted every recommendation):

13. **Same pixels on every platform is a rule** (Tim: pixel exactness across platforms matters more than
    anything else in rendering). Measured 2026-11-17 on Tim's NVIDIA GPU: D3D12 and Vulkan gave identical results
    in all 41 picture comparisons, and 31 of them matched the lavapipe references bit for bit; the other 10 differ
    (inspector up to 214 per channel, dashboard 69, Layout tab and a phone layout 25), most likely stale
    references the tolerance hid (0.5% of pixels may differ by any amount). So: look at the 10 and regenerate
    the stale ones, treating a real GPU difference as a bug; compare exactly (channel difference 0, no share of
    pixels), with a written per-test exception only where a backend truly can't match; a new test renders the
    same scenes on D3D12 and Vulkan and requires identical output; the browser (Dawn/Tint) and Metal get the
    same check once they can produce pictures.
16. **The name stays Scribble.** The bare NuGet id `Scribble` belongs to someone else (prereleases), and another
    owner publishes `Scribble.Mail.*`, `Scribble.Identity.*` and so on; `Scribble.Core`, `Scribble.Desktop`,
    `Scribble.Browser` are free. Packages are `Scribble.*`; README says the bare `Scribble` package isn't ours;
    apply for the `Scribble.*` prefix reservation (likely refused; not needed). Closes the open question.
17. **DESIGN.md's opening sections are rewritten** to describe the project as it is (projects, DllImport,
    finished milestones, MVU, panels), with an index at the top from each milestone to its section; later
    sections stay as history. During the pause.
18. **README's "What to check" moves to `docs/what-to-check.md`** (README links to it) at the first public push,
    not now; this revises Tim's 2026-11-16 note that README keeps them.
19. **CI waits until just before the public push,** planned together with decision 13's backend checks (unit
    tests on GitHub's Windows runners; rendering tests on a software GPU, lavapipe or WARP).

Round 4 (step 1's design and Core's layout; Tim accepted every recommendation, Q22 after a closer look):

21. **One clock and delayed work come from the platform:** `IPlatform` gets a monotonic `Elapsed: TimeSpan`,
    and `Dispatcher.PostAfter(delay, action)` runs work later through the platform's own one-shot timer (fires
    only when asked, so the power rule holds). `App.start`, `ElementHost` and the inspector read time from it;
    the tests' fake platform moves time only when a test says so. `ElementHost.create` used without `App` gets
    an `ElementHost.clock` setting (default: a stopwatch). `AsyncTests`' sleeps that test real threads stay.
22. **Where errors are caught** (decision 1 made precise). Catching only per frame would freeze the whole window
    at its last good frame when one element's render throws every time, so: each event handler call on its own
    (`update` runs inside `dispatch`, so a throwing update leaves the model as it was); the Elmish `view` keeps
    the elements of the last view; each component run keeps the component's last output and rolls back its hook
    slots; each element's `render` skips that element and its children for the frame; measure and arrange
    (custom elements only) drop the frame; each effect on its own. An error identical to the last (type,
    message, place) is counted, not reported again, until a frame finishes without it. The render `try` is
    checked with `Scribble.Bench` for frame-time cost.
    **Without `App.onError`, every error is rethrown on the UI thread** (the app ends), posted work included:
    this changes the dispatcher's log-and-continue default so all errors behave alike (loud while developing; the
    debugger stops there). Apps that should survive set `App.onError`.
23. **Core's folders follow compile order**, each folder one unbroken run (Rider shows a split folder twice):
    `Foundation/` (Primitives, InputTypes, Props, Icon, Cursor, Palette, Animation, Dispatcher, AppValue, Cmd),
    `Framework/` (Element, Style, Layoutable, ElementTree, Input), `Elements/Basic` (FocusRing, Border,
    TextBlock, IconView, Separator), `Elements/Panels` (StackPanel, Grid, DockPanel, Canvas, WrapPanel),
    `Elements/Scrolling` (ScrollViewer, ItemsLayout, KeyStore, ItemsView), `Elements/Controls` (Control, Button,
    ToggleButton, CheckBox, Switch, Slider, TabControl), `Themes/` (Theme), `Hosting/` (Component, Layer,
    ElementHost, App). Namespaces stay `Scribble.Core`. `Scroller` stays in Foundation: app values keep named
    scrollers, before any element exists.
24. **Big files split:** `ItemsView.fs` into `ItemsLayout.fs` (the layout kinds), `KeyStore.fs` (the items
    source, size sums and per-key stores) and the element. **As built:** the `Element` module couldn't move to an
    `ElementDefine.fs`: F# rejects a module and a type of the same name in two files of one namespace (FS0250),
    so it stays at the end of `Element.fs`, with the rest of it (one recursive type group).
25. **The reorganisation is its own commit,** with nothing else in it; step 1 lands in the new layout.

**As built (2026-11-17, not yet checked by Tim).**

- *Reorganisation (23–25):* commit `5d7686f`; files moved unchanged (git sees renames).
- *The clock (21):* `IPlatform.Elapsed` and `IPlatform.After(delay, action)`; `TimerQueue` (Scribble.Platform)
  keeps the delayed work and the platform one OS timer for the earliest (Win32: `SetTimer` on the wake window,
  created on first use; browser: one `setTimeout` through a new `setTimer` JS function and an `onTimer`
  callback). `Dispatcher(wake, after)` and `PostAfter`; `Dispatcher(wake)` alone waits on `Task.Delay` (hosts
  without a platform). `FrameClock(now)`; `ElementHost.clock`, `ElementHost.Elapsed`; the inspector reads
  `host.Elapsed` and waits with `PostAfter`. The tests' fake platforms share a `FakeTime`: a delivered frame
  moves it 16 ms (a 60 Hz display), `AdvanceMs` moves it further. No unit test sleeps any more except
  `AsyncTests`' real threads; the unit tests take 1–2 s instead of 40 s. Left on real time: ScrollViewer's
  fling speed (it measures the finger, it doesn't wait; no test covers it), frame statistics.
- *Errors (22):* `IElementRoot.ReportError` → `Dispatcher.ReportUiError` (counts a repeat: same type, message
  and first stack line, until `FrameEnded` sees a frame without errors). Guards: Input's `invoke` (every routed
  handler, enter and leave), element-type input hooks (pointer, key, wheel, pan: a failed pan takes nothing),
  `click`; App's `onKey`, `onFrame` and the Elmish view (a throwing view keeps the last elements);
  `ComponentInstance.Run` (rolls back hook slots added and effects queued by the failed run, returns the last
  output or an empty Border; hook-rule breaks are reported the same way); each component effect and each
  after-frame action; `Element.Render` (new `DrawList.Mark`/`RollBack` take back what the element drew; the
  drawing moved to `RenderShown`); ItemsView's per-item `view` (no view: a new item is left out, a realized one
  keeps its element, which also keeps layout from failing every frame); `ElementTree.UpdateLayout` (sets
  `LayoutFailed`; ElementHost then doesn't begin a frame). The dispatcher's default is now `ExceptionDispatchInfo.Throw`
  (posted work, `PostAfter`, `ReportError`, and with `Dispatcher.Install` unobserved task exceptions too, which
  arrive when the GC finalizes the task, so possibly long after). Cost: `Scribble.Bench`'s draw-list times with and
  without the render guard are the same within noise (72–98 µs a step either way). Tests: `ErrorTests.fs`
  (8: handler and update, no onError, view, render repeating, measure, component and hook rules, effects,
  ItemsView items); the render and component tests were checked to fail with their fix taken out.
  *Hooks pass, 2026-11-19:* the rollback missed what a hook had remembered on the strength of a queued effect:
  a run that changed the dependencies of `useEffect`, `useSubscription` or `useAsync` and then failed left the
  new dependencies recorded and the effect dropped, so it never ran (a `useAsync` stayed Loading, its old load
  already cancelled). Now `EffectSlot.Changed` registers an undo (`ComponentInstance.OnFailedRun`) that puts the
  old dependencies back, and `useAsync` forgets it ever ran, so the next run loads whatever its dependencies
  are. Tests: three in `ErrorTests.fs`; the effect one was seen to fail before the fix.
- *Same pixels (13, step 2):* compared exactly, 10 pictures failed. Stale (the tolerance hid them): the
  inspector's two (the tree's sideways scroll bar from 3a++ reserves a gutter, so the tree shows about 8 DIPs
  less and a partly cut row; see the open point below), the Layout tab's three (the Canvas outline is now
  `Palette.text`). Not a GPU difference but a test that couldn't pass exactly: the dashboard's hand-drawn and
  element versions differed only in the element version's scroll bar (520 tall, the cards overflow); the test
  now renders 760 tall and the two match to the pixel. A real backend difference the tolerance also hid: D3D12
  and Vulkan on the same GPU disagreed by one step in 473 pixels (dashes along rounded corners, one text pixel).
  Two shader changes fix nearly all of it: `atan2` replaced by a polynomial of + − × ÷ (`atan2_exact`, error
  about 1e-5 rad; WGSL leaves the built-in's precision to each backend), and the fragment's colour rounded to
  the 8-bit step in the shader (`exact8`). Left: 2 pixels of 588,816 in `dashes@2x`, one step, where WGSL's
  allowed division error and each compiler's fusing of a*b+c land on either side of a rounding step; that is
  `BackendTests`' one written exception (at most 4 pixels, 1 step). The references were regenerated on Tim's
  machine (D3D12, RTX 4070 Ti): 40 pictures changed, 34 of them by at most 2 steps per channel (the two shader changes and rounding against lavapipe); the 6 larger ones were looked at; `Snapshot` compares exactly (channel
  difference 0, no share of pixels). `BackendTests` renders shapes, text, emoji, dashes and the element dashboard
  at 1× and 2× on every backend the machine offers (Windows: D3D12 and Vulkan; a Mac: Metal and Vulkan) and
  compares them; with one backend it says so and passes. The test fixture now honours `SCRIBBLE_GPU_BACKEND`, so
  the whole suite can run on Vulkan (it passes except that same `dashes@2x` exception, against D3D12's
  references). The inspector tree's gutter: Tim chose to overlay the bars (`ScrollBarSpace.Overlay`, which covers
  both bars: the vertical one now sits over the rows' right edge too); the tree shows full rows again.
- *Core fixes (6, 10, 11, 12; step 3):* (6) `View.Create` checks, in Debug builds, for a property set twice in one
  list and writes "Border: its settings set BorderThickness more than once; the last one wins." once per type and
  property, through a new internal `DebugWarnings` (once per process; tests listen with `DebugWarnings.Listener`).
  The whole test suite and every sample view built by the rendering tests set off one: the Layout tab's `chip`
  helper set its border thickness and let callers set it again (`yield! settings`); it now takes the thickness as
  a parameter. That helper idiom (defaults, then the caller's settings) is the one the warning catches in app code
  too; README says to take what callers may change as parameters. (10) `GlyphAtlas.Overflows` counts clears that
  come twice before one upload (`TakeDirtyRegion`, once per frame on both backends); Debug builds warn on the
  first. (11) An element leaving its tree (`SetRootInSubtree` to none) drops its forced states and takes them
  out of `ElementCounts`; `Element.RenderOverflow` is an internal constant. (12) `Timing.TimingFact` (skipped
  unless `SCRIBBLE_TIMING=1`) with `Trait("Category", "Timing")` on the 5 wall-clock tests; three of them were
  split so their counts (realized, measured) stay in the normal run. In Release all 5 pass. Tests: 3 new
  (property set twice, atlas overflow, forced states on leaving), the forced-states one checked to fail without
  its fix; 409 run, 5 skipped.
- *Button alignment (Tim, 2026-11-17, after noticing Grid buttons didn't fill their cells):* Button's
  `horizontalAlignment Left` default had come with the first Button (M4) without a recorded reason. Buttons now
  stretch like every other element (a sized one is centred in its space); CheckBox and Switch keep `Left`
  (`ToggleButton.labeledDefaults`) so their click area stays the box and its label. ToggleButton and the tab
  headers follow Button. Changed: the Layout tab's Grid buttons fill their column (3 pictures), and the
  inspector no longer lists the Button's alignment as a type default (`inspector-pick`, below the fold). 4 unit
  tests moved their pointer to the centred button; the defaults test checks Stretch for Button and Left for
  CheckBox.
- *The inspector split (9; step 4):* `Inspector.fs` (1,896 lines) is now seven files in compile order:
  `InspectorTypes.fs` (options, panel modes, drags, sizes, rows, names), `Format.fs`, `InspectorState.fs`,
  `Highlight.fs`, `Elements.fs` (Chevron, Grip), `Views.fs` (props, icons, the views) and `Inspector.fs` (the
  public `Inspector` type), moved unchanged (a stray doc comment of `Format` went back above it). `WrapRow` is
  gone: the states line is a `WrapPanel` (spacing and line spacing 4, as before; its children were already
  centred, so a row's full height changes nothing). Every picture identical, all tests pass unchanged except
  one that found the row by its type name.

## The semantics stage (planned and built 2026-11-17)

Decision 5 of the project review, planned as a grilling. What every element is to assistive technology (role,
name, value, states, actions), filled in for the existing element types before M8 step 4, so new controls are
built with it. Words: "Accessibility" in GLOSSARY.md.

Round 1 (Tim accepted every recommendation; Q4 and Q5 changed in the discussion, see below):

1. **Scope:** the data model, every built-in element type filled in (Border, TextBlock, IconView, Separator,
   FocusRing, Button, ToggleButton, CheckBox, Switch, Slider, TabControl, ScrollViewer, ItemsView), settings for
   apps, the semantic tree on request, working actions, an inspector section, tests. Not now: a screen-reader
   bridge (UI Automation, macOS, browser ARIA), keyboard changes, announcements.
2. **Roles are ARIA's** (what browsers map onto every platform; Chromium, Flutter and AccessKit model theirs on
   it): a closed F# union of the roles we use, growing with the controls, no custom role (readers only know theirs).
3. **A semantic node holds** a role, a name, a value (none, text, or a range: current, minimum, maximum, step),
   states (checked on/off/mixed, selected, expanded, disabled, focusable, focused), the actions it supports
   (click, increment, decrement, focus, scroll into view), its bounds in window coordinates, and for list items
   their position and set size ("3 of 10,000" with only some realized). Descriptions, live regions and heading
   levels wait for a need.
4. **Semantics are settings** (changed from a stage function, Tim: "why is semantics a function instead of just
   accepting a list of Semantics settings?"): an element type gives them in `Element.defaults` like any other
   value (`Semantics.role Role.CheckBox`); what varies per element points at the property to read when the tree
   is asked for (`Semantics.checkedFrom ToggleProps.isChecked ToggleProps.isIndeterminate`, `rangeFrom`,
   `selectedFrom`), with `Semantics.computed (fun ctx -> [ ... ])` as the escape hatch. So `Element.extend`
   inherits them, a view overrides them as it overrides a background, the inspector shows where each came from,
   and nothing is allocated when asked except the escape hatch. They affect nothing (no layout or drawing).
   Disabled, focusable, focused and bounds come from the element itself; name from content is one shared rule.
5. **Out of sight unless wanted** (Tim: "can we put this in its own package/project/namespace so that authors
   who don't care about this stuff don't have to see it in autocomplete?"): the types and settings live in
   **namespace `Scribble.Accessibility` inside Core**, used like attached settings:
   `Button.create [ Button.icon Material.close; Semantics.label "Close" ]` after `open Scribble.Accessibility`;
   nothing is added to `Button.` or `Element.`, and `tree.Semantics()` / `host.SemanticTree()` are extensions in
   that namespace. Not a separate project: Core's own controls declare their semantics, and a table kept outside
   Core would be static state keyed on element types (rule 6) and split each control in two. The screen-reader
   bridges, later, are separate opt-in projects. App settings: `Semantics.label`, `Semantics.hidden`,
   `Semantics.role`, and plain-value forms (`Semantics.checked model.IsOn`) for an app's own composites.
6. **Name from content** (ARIA's rule): a button, checkbox, switch, tab or list item is named by the text inside
   it, joined with spaces, and that text is not a node of its own; a TextBlock's name is its text.
7. **The tree** comes from the element trees, leaving out elements with nothing to say (layout panels, plain
   Borders without a role), hidden ones (`isVisible false`), `Semantics.hidden` subtrees, decorative IconViews
   (no label), FocusRing. Layers are its top-level children in order; a modal layer hides those below. Virtualized
   lists hold their realized items, with positions.
8. **On demand only:** a fresh snapshot each time it's asked for; nothing changes in a frame. Change
   notifications come with the first bridge.
9. **Labels by text only** (`Semantics.label`); "labelled by another element" waits for forms and TextBox (M9).
10. **Actions work now** and take the input paths: click runs the type's click and `onClick` (guarded),
    increment and decrement step a Slider as its arrow keys do, focus focuses, scroll into view uses
    `BringIntoView`. A CheckBox toggles by click (no separate toggle action).
11. **Real states only:** states forced from the inspector don't change semantics.
12. **Inspector:** a SEMANTICS section for the selection (role, name and where it came from, value, states,
    actions; "not exposed" with the reason). A semantic tree view later.
13. **Tests:** each element type's node; and a check over every Counter tab, Hello and the Icons gallery that
    whatever can be focused or clicked has a role and a non-empty name (the samples are fixed until it passes).

Round 2 (Tim accepted every recommendation):

14. **One checked state** (on, off, mixed from `isIndeterminate`), the role deciding how it's spoken: ToggleButton
    is `Role.Button` with it ("pressed"), CheckBox `Role.CheckBox`, Switch `Role.Switch` (on or off only).
15. **Tabs:** the TabControl has no node; its header row is a `TabList`; each header a `Tab` (selected, "2 of 4",
    click selects); the page shown a `TabPanel` named after its header; kept hidden pages left out. The link from
    tab to page waits with "labelled by".
16. **Slider:** `Role.Slider`, a range from `value`, `minimum`, `maximum`, `step`; increment and decrement; the
    node gains an **orientation**. Value text ("50 percent") waits for a need.
17. **Scrolling** (adds to 3): a ScrollViewer that can scroll is a `Role.ScrollArea` node with its offset, extent
    and viewport, and two more actions, **scroll forward** and **scroll backward** (a page, through its
    scroller), so a reader can make a virtualized list realize more. One that fits has no node. The role is one
    step past ARIA, as Chromium and AccessKit take it.
18. **ItemsView:** one node, `Role.List` with the scroll information; each realized item's root a `ListItem` with
    its position and set size (from the source's count); a root with its own role keeps it and the position. The
    grid layouts are lists too (ARIA's `grid` is a 2D interactive table).
19. **The rest:** Button `Button` (click, name from content); TextBlock `Text`; IconView `Image` only with a
    label; Separator `Separator`; Border and panels none unless given a role; FocusRing never. Disabled is the
    element's real enabled state (a disabled parent included); focusable means it can take focus now.
20. **The first roles:** Button, CheckBox, Switch, Slider, Tab, TabList, TabPanel, List, ListItem, ScrollArea,
    Text, Image, Separator, Group (an app's container with a name). Heading waits for levels; Dialog, RadioButton,
    ComboBox come with step 4.
21. **The inspector layer is left out** (`Semantics.hidden` on its root).
22. **Bounds:** layout bounds in DIPs, window coordinates, visual transforms ignored (as hit testing does); a row
    half scrolled out reports its full bounds.
23. **Reading it:** `host.SemanticTree()` returns an immutable `SemanticNode` snapshot; each node keeps an internal
    handle to its element, and `node.Perform action` returns false when the element has left its tree or doesn't
    support the action. UI thread only; the tests drive controls through it as a bridge would.

Build order: the model and settings; the built-in types; actions; the inspector section; the sample check.

**As built (2026-11-17).** As agreed, with these details and departures:

- **Files:** `Framework/Semantics.fs` (the types, `SemanticText`, the `Semantics` settings type; after
  Layoutable, so element types can use them) and `Hosting/SemanticTree.fs` (`SemanticNode`, `SemanticTree`,
  the builder, actions, and the extensions `host.SemanticTree()`, `tree.Semantics()`, `element.SemanticNode()`).
  `Role`, `CheckState` and `SemanticAction` are `RequireQualifiedAccess` (`Role.List` beside `List.map`) and write
  out their `ToString`: a union's generated one uses `%A`, which NativeAOT can't run; `SemanticText.value` formats
  the settings' values for the same reason.
- **Settings** follow decision 8: `Semantics.role`, `label`, `hidden`, `isChecked` (bool or `CheckState`),
  `isSelected`, `isExpanded`, `range`, `orientation`, `position (index, count)`, and for element types
  `labelFrom`, `checkedFrom`, `selectedFrom`, `rangeFrom`, `orientationFrom` (each names the properties it
  reads), `actions` with `onAction` (Slider's increment and decrement), and `computed (fun element -> [ ... ])`.
  Click, focus, scroll into view and a scroll area's scrolling come by themselves, so `actions` lists only the
  rest. A text value (Q3) isn't built: nothing has one before TextBox.
- **Q18 changed in the building:** an ItemsView doesn't scroll itself; the ScrollViewer around it does. So the
  ScrollViewer is the `ScrollArea` (with the scroll actions) and the ItemsView the `List` inside it. An item whose
  root is only content (a TextBlock) becomes a `ListItem` named by its text.
- **Modal layers** hide those below only while they show something, as input does (the Counter keeps an empty
  modal "Dialog" layer, which first hid the whole app).
- **`ClickSource.Assistive`**: a click asked for by assistive technology reaches `onClick` with it. The click
  path moved into `Clicks` (Input.fs), shared by input and actions.
- **A debug warning** for `Semantics.label` without a role (the element isn't in the tree).
- **The samples' check** found the Counter's five Sliders without a name (Controls tab): they now have labels.
  Hello and the Icons gallery had none to fix. Containers (scroll areas, lists, tab lists, tab panels, groups)
  may go without a name.
- **Inspector:** SEMANTICS sits after PROPERTIES (above it, it pushed "Show defaults" out of view in the tests'
  small window); the element's own `Semantics.*` values are listed there with their sources, not under
  PROPERTIES. The inspector's layers set `Semantics.hidden`. One reference picture changed: `inspector-pick`,
  whose properties thumb is shorter (more lines).
- **Tests:** 23 in `SemanticsTests` (every type's node, name from content, what's left out, states and actions,
  tabs, lists and scrolling, layers, reasons, the samples' check) and one inspector test: 433 unit tests (5
  timing tests skipped), 62 rendering tests. NativeAOT publish of the Counter: no new warnings.

## Scrolling to an item moves the scroll viewers around it only as needed (Tim, 2026-11-17)

Found once the Counter's pages scrolled: on the Layers tab, "#5,000" scrolled the page as well as the message
list. Two causes:

1. **Every scroll viewer got the requested alignment.** `ScrollTo(key, Start)` put the item at the top of the
   list *and* of the page around it, as the browser's `scrollIntoView()` does by default. **Decided** (Tim
   agreed): only the nearest scroll viewer (one with a viewport; an ItemsView leaves that to its ScrollViewer,
   `ScrollHooks.HasViewport`) uses the alignment; those around it use Nearest: no move when the item already
   shows, else just enough (as WPF's `BringIntoView` and the browser's `block: "nearest"`). `BringIntoView` and
   focus already used Nearest everywhere, so only scroller requests change.
2. **A bug:** a ScrollViewer told those around it how far it moved after a jump had already changed its offset,
   so a far jump (no animation) reported no move, and the page thought the item was still thousands of DIPs
   down: it scrolled to its end. The move is now measured before moving.

As built: `BringRectIntoView` (Element.fs) and `makeVisible` (ScrollViewer.fs); a test with a list on a
scrolling page (`ItemsViewTests`), which fails without either fix. 434 unit tests, 62 rendering tests.

**A third cause, found on the Gallery's Scrolling page (Tim, 2026-11-17):** "Add message" showed only the top
30 of the new 46-DIP message; the page stopped 16 DIPs short. A ScrollViewer turned the rect into content
coordinates by undoing its content's arranged bounds, which sit inside the content's margin, so the content's top
margin (the page's 16) was lost. `makeVisible` now adds the margin back (still undoing the rounded bounds the rect
came through: undoing the exact slot instead put "scroll to the top" 0.43 DIPs off in `ScrollStressTests`). Tests:
`ItemsViewTests` (an item added and scrolled to, on a page with a margin, jumping and animated) and
`GalleryPagesTests` (the Gallery's "Add message" at three window heights); all five fail without the fix.
440 unit tests, 61 rendering tests.

**Then the page moving at all (Tim, 2026-11-17):** with the fix, "Add message" scrolled the page, and the button moved
out from under the pointer. That is what every framework does by default (the browser's `scrollIntoView()`, WPF's
`BringIntoView`, Flutter's `ensureVisible`: every scrollable ancestor moves), and apps avoid it by layout: a list
screen's list fills the height left, so nothing around it scrolls. **Decided** (Tim, option 1 of three): the
Gallery's Scrolling page fills the window (`Common.fillingPage`, `fillingSection`; `GalleryApp`'s pages say
`Scrolls` or `Fills`), the list taking the height left instead of a fixed 340, so the page never scrolls; the
behaviour in Core stays. Turned down for now: an option to scroll only the nearest viewer (the browser's newer
`scrollIntoView({ container: "nearest" })`), in Deferred work until an app needs it; leaving it as it was.
`GalleryPagesTests` now also checks the button stays where it was; `gallery-phone-scrolling@3x` regenerated (the
list down to the page's bottom margin).

## M8 step 3b: images, and the Counter becomes the Gallery (planned 2026-11-17)

A grilling in three rounds plus the sample overhaul (Tim: yes to all, Q7 refined). Facts it rests on: the draw list
had no images (one instanced draw call over a 2048² one-channel glyph atlas and a 1024² colour one); glyphs are read
texel for texel, never filtered; `Bitmap` is taken (`RenderOffscreen`'s pixels); `Icon`/`IconView` set the
value-plus-element pattern; image cursors' platform contract already takes pixels; views can't read the window size.

**Decoding and the GPU**
1. **Our own decoders** (the BCL's `ZLibStream` for PNG's compression: the BCL is the platform, like OS APIs). OS
   decoders (WIC, `createImageBitmap`) were turned down: their pixels differ (JPEG rounding, browser colour
   management), the browser path is async through a canvas, Linux has none.
2. **PNG in this step**; JPEG the next; GIF, WebP, animation and SVG files later.
3. **Every static PNG**: all colour types and bit depths, `tRNS`, Adam7; 16-bit rounded to 8; an APNG shows its
   first frame.
4. **sRGB assumed**, embedded colour profiles and gamma (iCCP, gAMA) ignored; alpha premultiplied on decode.
5. **RGBA atlas pages**, drawn as a new instance kind in the same shader (one draw call per page); an image bigger
   than a page gets a page to itself. A per-image texture and draw call was turned down: it breaks the batch.
6. **Filtering in our shader**: bilinear from `textureLoad` with fixed weights and `exact8`, so backends agree (the
   GPU sampler's maths isn't pinned down); mip levels built on the CPU for shrinking past 2×.
7. **Too big for the GPU** (over 8192 on a side, WebGPU's default limit): reduced at decode to the largest mip level
   that fits, same size in DIPs, a debug warning.
8. **`Scribble.Imaging`**, a new project: bytes in, premultiplied RGBA out, no GPU, no elements; Core references it.
   The `Image` value, its cache and `ImageView` are in Core; atlas pages and the instance kind in Rendering.
9. **Shared:** decoded pixels process-wide (a thread-safe holder, like the font caches); atlas pages per GPU device.

**The value and the element** (words in GLOSSARY.md: image, ImageView, density)
10. **`Image`** (an immutable value, keyed: decoded once however many places show it) and **`ImageView`** (the
    element), after `Icon`/`IconView`.
11. **Sources:** `Image.fromResource (assembly, name)`, `fromBytes (key, bytes)`, `fromPixels (key, width, height,
    rgba)`, `fromFile path` (desktop; an error in the browser), `fromUrl url` (`fetch`; `HttpClient` on the
    desktop), each with an optional settings list (`Image.density 2.0`).
12. **Density:** one image pixel per DIP unless `Image.density` says otherwise; resolution sets (1×/2×/3× picked by
    the window's scale) later.
13. **Sizing:** the element stretches like any other (its slot is its hit area and its border's box); the image is
    drawn in it with `ImageView.stretch` (`None`, `Fill`, **`Uniform`** (default), `UniformToFill`), centred by the
    usual alignment settings, and never enlarged unless `ImageView.stretchDirection` (**`DownOnly`** (default),
    `UpOnly`, `Both`) says so: CSS's `object-fit: scale-down`. Desired size is the image's own, so Auto rows and
    content-sized panels get it right. (Tim's refinement: stretch and centre by default rather than take the image's
    size; not enlarging keeps a small logo in a StackPanel from blowing up to the panel's width.)
14. **`ImageView.scaling`**: `ImageScaling.Smooth` (default) or `Pixelated` (nearest; CSS `image-rendering`).
15. **`DrawList.DrawImage(rect, image, ?radius, ?scaling, ?source)`**, the image instance using the shape shader's
    rounded-rect edge, and **`ImageView.cornerRadius`**: avatars without general rounded clipping.
16. **Size known at once** for images in memory (resource, bytes, pixels: the PNG header), so the first layout is
    right; files and URLs are 0 × 0 until loaded (unless sized), then lay out again. Decoding is off the UI thread
    on the desktop, on it in the browser for now (a web worker later).
17. **While loading** an `ImageView` draws nothing but its background; a **failure** (missing resource, 404, corrupt
    file) goes through `ReportError` to `App.onError` and draws nothing. Placeholders and `onLoaded` when an app
    needs them.
18. **Lifetime:** kept (pixels and atlas space) while any `ImageView` shows it, then in a ~32 MB recently-used pool
    (oldest out first) so switching tabs back doesn't decode again; an emptied atlas page is given back.
19. **Only visible images decode:** decoding starts on realization, not when the value is made. Tests: an
    ItemsView of 10,000 rows with an image each decodes only the realized rows' (also after scrolling to 9,000), and
    a timing test for decoding a 1024² PNG in Release.
20. **Semantics** as `IconView`: role Image, named by `Semantics.label`, decorative (left out) without one.
21. **`Cursor.fromImage image [ Cursor.hotspot ... ]`** in this step (held back "until Image").
22. **The inspector** shows an ImageView's image: source, pixel size and density, loading/ready/failed (with the
    error), reduced to fit, in the atlas.
23. **Tests:** PngSuite (Willem van Schaik's conformance set, ~175 small files, free to use) in `tests/`, checked
    against raw RGBA decoded once with WIC on Tim's machine (test-only); its corrupt files must fail cleanly.
    Screenshot tests for the stretch modes, smooth and pixelated up and down, alpha and mip levels, with
    `BackendTests` agreeing.

**The Counter becomes the Gallery** (Tim: "it's no longer just a counter")
24. **`Gallery`** (`Gallery`, `Gallery.Desktop`, `Gallery.Browser`; tests, pictures, Scribble.Serve, Scribble.Bench
    and `.run/` renamed), and a new one-file **`Counter`** (the Elmish counter, the README's first example: what the
    name promised in M4).
25. **Pages**, each opening with its title and a line on what it shows: **Components** (Elmish, useState and
    useAnimated, useAsync as three demos), **Layout**, **Controls**, **Scrolling** (the 10,000 messages, out of
    Layers), **Layers** (toast, modal), **Styles**, **Images**. "Show the code" waits for selectable text.
26. **Navigation stays a TabControl** for now. A sidebar on wide windows and tabs on narrow ones needs views to
    choose by available width: deferred to a responsive-layout round. A drawer at every size was turned down (worse
    on the desktop).
27. **720 × 560** window; pages scroll when they don't fit.
28. **Images page:** the stretch modes side by side, smooth against pixelated, a rounded avatar, a 2× image, a URL
    load, a broken image reporting its error, image cursors. Pictures are procedural (made by a small PNG writer in
    the tests and tools): no licence questions.

**Order:** (a) the Gallery overhaul, its own commit, checked by Tim before any image code; then (b) the decoder,
(c) the GPU and atlas, (d) `ImageView`, (e) sources and loading, (f) cursors and the inspector, with the Images page
growing alongside.

**(b) decisions (Tim: agreed, 2026-11-17):** the PngSuite references are made by a test-only F# script,
`tools/MakePngReferences.fsx`, decoding through **WIC** (COM interop) on Tim's machine and written once into the repo;
not System.Drawing, whose GDI+ applies gAMA. If WIC applies something we decided to ignore on some files, those
are listed and checked by hand. Reduction for images over 8192 (decision 7) needs mip levels, so it moves to (c);
(b) is decoding only. PngSuite lives in `tests/Scribble.Tests/PngSuite/` with its licence.

**(b) as built (2026-11-17):** `src/Scribble.Imaging` (files `Pixels.fs`, `Crc32.fs`, `Png.fs`, `ImageDecoder.fs`):
`ImageDecoder.decode bytes` gives a `DecodedImage` (`Width`, `Height`, `Pixels`: premultiplied RGBA, no padding);
`ImageDecoder.readInfo bytes` gives an `ImageInfo` from the header alone (signature plus IHDR, 33 bytes). Failures
throw only `ImageFormatException`, with a message saying what is wrong. Nothing references the project yet: Core
does from (d). Choices made while building:
- **Rounding:** a 16-bit channel goes to 8 by `(v·255 + 32767) / 65535`, and premultiplying is `(c·a + 127) / 255`.
  Both are exact round-to-nearest, tested for every input, and every format shares them.
- **What we accept or refuse:** a bad CRC on a critical chunk or an unknown critical chunk fails; an ancillary chunk
  with a bad CRC is skipped, as libpng does. A missing IEND is fine when the image data is all there. Palette
  indices past the palette's end are opaque black, as browsers show them. A suggested palette on a true-colour image
  and bKGD, sBIT, sPLT, text and time chunks are ignored. APNG's acTL, fcTL and fdAT are skipped, so its default
  image shows.
- **WIC references:** WIC decoded all 161 valid PngSuite files. It agrees with us exactly, gamma files included, so
  it doesn't apply gAMA either. The one exception, checked by hand: for 16-bit greyscale WIC returns some samples
  one 16-bit step off (basn0g16 (13, 6) holds 0x8100, WIC says 0x8101), which crosses an 8-bit rounding boundary on
  6 pixels per file. Our value is the file's; the test allows that one step for `*0g16` files only. WIC accepts two
  corrupt files (xhdn0g08, a bad IHDR CRC, and xcsn0g01, a bad IDAT CRC); we refuse all 14.
- **Fuzzing found one bug:** zlib can throw `ZLibException`, an `IOException`, not only `InvalidDataException`.
  Both now become `ImageFormatException`. The tests cut every file short at 40 points and corrupt bytes behind
  rewritten CRCs (seeded), so damaged data reaches zlib, the filters and the colour code.
- **Speed:** unfiltering and the 8-bit RGBA and RGB rows run on pointers into arrays pinned once (`fixed` on arrays,
  sizes checked first). Paeth is branch free with the spec's tie order. The CRC slices by 8. The two large arrays
  are allocated uninitialized because every byte gets written. A 1024² noisy RGBA picture (2.5 MB, Paeth on every
  row) decodes in **24 ms in Release**, about 8 ms of it the BCL's zlib, which costs about 7 ms per 4 MB however well
  the data compresses. Started at 33 ms; the timing test's guard is 40 ms. (About 13 ms and a 25 ms guard since
  2026-11-18: section "Vectorising the PNG decoder".)
- **Tests (`PngTests.fs`, 351 of them):** PngSuite against WIC; `readInfo` agreeing with `decode`; the 14 corrupt
  files; non-images; cut-short and corrupted files failing only with `ImageFormatException`; the rounding formulas
  checked exhaustively; round trips through a small PNG writer in the tests (all five filters, mixed by row); an
  APNG; a file without IEND; unknown critical and ancillary chunks; 1024² correct, plus the timing test.

**(c) decisions (grilled in two rounds; Tim: yes to all, 2026-11-17):** images on the GPU.
1. **The backend places images:** the draw list records "this decoded image, this rectangle". The backend places
   it in its atlas while encoding, uploading what's missing, and writes the atlas position into the draw list's
   data through an internal method. The draw list stays free of any device; Core never holds one at draw time.
   The glyph pattern, where the caller reserves atlas space, was turned down.
2. **Element authors draw decoded images** (GLOSSARY.md). `DrawList.DrawImage` takes a `DecodedImage`; Core's render
   context turns an `Image` into one, or nothing while it loads (d).
3. **`DecodedImage` stays in Scribble.Imaging**, now a class with identity, mip levels and `Release`; Rendering
   references Imaging. Moving it into Rendering would have made Imaging depend on Rendering.
4. **Whole-number filtering:** bilinear in integers in the shader (fixed-point positions and 8-bit weights), so
   every backend gives the same pixels by construction. That needs image rectangles snapped to whole pixels: a moving
   image steps a pixel at a time. A precise mode with fixed-point origins comes when an animation needs it.
5. **Mip levels:**
   - **Building:** every level is built when the image is decoded, off the UI thread where decoding is, by exact
     2×2 averaging. Built lazily at draw time, a grid of big photos scrolling in would stutter. An image made from
     pixels gets its levels when first drawn small.
   - **Drawing:** the first level shrunk by less than 2×, judged by the axis that shrinks more. No trilinear
     blending.
   - **Uploading:** only the levels drawn are uploaded. A 4000 × 3000 photo shown as a 120-pixel thumbnail puts
     190 KB on the GPU, not 48 MB.
6. **`Pixelated` changes enlarging only**, as CSS's `image-rendering: pixelated` does; shrinking is smooth.
7. **Pages:**
   - **Size and creation:** 2048² RGBA, created when the first image is drawn, with no CPU copy (the decoded image
     holds the pixels).
   - **Big images:** an image level over 2048 on a side gets a texture of its own size.
   - **Packing:** shelves, reusing a freed slot an image fits, and giving an emptied page back. A better allocator
     only if a real app shows waste; the inspector's page view (f) will show it.
8. **Too big:** a decoded image over 8192 on a side is halved when it is made, until it fits, and marked "reduced".
   A device whose own limit is lower (WebGPU's compatibility mode guarantees 4096) draws the first level that fits.
9. **Lifetime:**
   - **Releasing:** `DecodedImage.Release()` is called by Core alone (decision 18). Each device clears released
     images out of its pages at its next frame. Devices never evict on their own.
   - **The pool:** images in the recently-used pool keep their atlas space, and the pool's 32 MB counts CPU pixels
     only.
10. **Drawing order:** one draw call per run of images on the same page; shapes and glyphs between them join the
    current run. A texture array (one draw call always) was turned down: all its layers must be the same size, and
    growing it copies every page.
11. **Drawing a released image** throws `ObjectDisposedException` from `DrawImage`, which reaches `ReportError` and
    rolls back the element's drawing.

**(c) as built (2026-11-17):**
- **`DecodedImage`** (Scribble.Imaging, `Pixels.fs`) is now a class: `Width`, `Height`, `Pixels`, `Level k` (an
  `ImageLevel`, built under a lock from the level above), `LevelCount`, `LevelsBuilt`, `BuildLevels()`, `Reduced`
  with `OriginalWidth`/`OriginalHeight`, `Release()`/`IsReleased`, and a static `ReleaseCount` so devices sweep
  only after a release. A level is half the one above, rounded up, `(a + b + c + d + 2) / 4` per channel, an odd
  edge counted twice. `ImageDecoder.decode` builds every level; the 1024² timing test now reads about 27 ms, up from
  24. Rendering references Imaging, so the (b) note's "nothing references it yet" no longer holds.
- **`DrawList.DrawImage(rect, image, ?radius, ?scaling, ?source)`** snaps the rectangle, skips what's clipped
  away, picks the level ((c) 5), and records an `ImageDraw` beside the instance (kind 3); `Mark`/`RollBack` cover
  images. `ImageScaling` (`Smooth`, `Pixelated`) is in Rendering, for Core to reuse.
- **The 24-float layout is unchanged.** For an image, fill holds the origin's whole texel x, y and the steps (1/65536
  texel per pixel), border holds the texels it may read (x0, y0, x1, y1), the pattern word holds the origin's 1/256
  fractions, and params x and y hold opacity and pixelated. Every value fits a float exactly, and the pattern is read
  as a real `u32`, so no bit pattern passes through float arithmetic.
- **The shader** (`image_pixel` in `shapes.wgsl`) computes the sample position in `i32`, splits it into texel and
  8-bit weight with shifts, blends four `textureLoad`s in integers, then applies opacity and the rounded-corner
  coverage (the shape code's SDF) before `exact8`. Binding 4 is the run's page, or a 1 × 1 placeholder.
- **`ImageAtlas`** (Rendering, `ImageAtlas.fs`) does the device-independent part for both backends: shelf packing
  (`ShelfPacker`: a shelf of about the right height first, then a new shelf, then any tall-enough one; freed
  regions reused, a shelf's freed end and empty bottom shelves given back), pages through the backend's
  `IImagePages` (`Create`, `Destroy`, `Upload`), placement per (image, level), the device limit, the sweep, and the
  runs (`ImageRun`: first, count, page). The native renderer keeps a bind group per (format, page); the browser
  keeps one per page and gets the runs as int32 triples; its limit comes from `device.limits.maxTextureDimension2D`.
- **Tests:**
  - **Unit (`ImageAtlasTests.fs`, 16):** halving exact; levels lazy, all built by decoding; reduction; level choice;
    released images throwing; rollback; snapping; shared pages and only drawn levels uploaded; 1,000 images made
    and 3 drawn giving 3 uploads; runs and order across small pages; own-size pages; a 4096 device; release and an
    emptied page going; the values written; packing reuse.
  - **GPU (`ImageTests.fs`, 11):**
    - an exact 1:1 copy;
    - a source part exact, reads kept inside it;
    - filtering equal to the same formula computed on the CPU (enlarged, pixelated, shrunk, mixed by axis);
    - mip level 2 at 5×;
    - drawing order across three pages;
    - opacity and corners;
    - the pictures `images@1x` and `images@2x`, also in `BackendTests`, where D3D12 and Vulkan match exactly.
- **Checked:** a NativeAOT publish of Hello starts. In the browser, the Gallery still renders with no WebGPU
  errors, but nothing there draws an image yet, so the browser's image path is first seen in (d).

**(d) decisions (grilled in two rounds; Tim: yes to all, 2026-11-17):** the Image value, its cache and `ImageView`.
1. **The split from here:**
   - **(d)** the `Image` value, its cache and pool, decoding off the UI thread, the in-memory sources (`fromResource`,
     `fromBytes`, `fromPixels`), `ImageView`, images in the render context, the "reduced" debug warning, and the
     Gallery's Images page without the URL demo;
   - **(e)** `fromFile` and `fromUrl`;
   - **(f)** cursors and the inspector.
   `ImageView` can't be tested without a source, and decision 16 decodes even in-memory images off the UI thread.
2. **Identity:**
   - **Keys:** two images are the same when their source keys are: a resource's assembly name plus resource name, or
     the key given to `fromBytes`/`fromPixels`.
   - **Bytes:** never compared; a second, different array under the same key gets the first one's pixels. Cheap for
     views that build `fromBytes ("avatar-42", bytes)` on every update.
   - **Density:** part of the value, not of the identity: one decoded image serves every density.
3. **Elements hold images, not just ImageView.** An element holds every image it asks for through its context
   (`ctx.Image`, `ctx.ImageSize`, in measure or render). It lets one go when it is detached (pooled by an ItemsView
   included), or when a render no longer asks for it. At zero holders an image enters the recently-used pool; when the
   pool drops it, `DecodedImage.Release()` is called. A custom element drawing images gets the same lifetime; there is
   no public attach/detach hook.
4. **Decoding:**
   - **Where:** `Task.Run` on every platform: the thread pool on the desktop; in single-threaded WebAssembly, later
     on the same thread, so the current frame isn't blocked.
   - **When it's done:** the result comes back through `Dispatcher.Post`. The holders are re-measured if the size
     wasn't known, else redrawn, and one frame is requested.
   - **Many askers:** one decode however many elements ask.
   - **Tests:** unit tests decode inline through an internal setting; one test runs the real path and waits.
5. **`fromPixels` takes straight RGBA**, premultiplied when the image is made. It is ready the frame it is first asked
   for, and its levels are built when first drawn small ((c) 5).
6. **ImageView is as like IconView as it can be** (Tim). It inherits `Layoutable`, sits beside IconView in
   `Elements/Basic`, and has the same parts:
   - **Settings:** `ImageView.image`, the counterpart of `IconView.icon`.
   - **Drawing:** centred in its space, and nothing when it has no image (or while loading, or after a failure).
   - **Semantics:** role Image, named by `Semantics.label`.
   - **For other elements:** an internal `ImageView.draw`, as Button uses `IconView.draw`.
   - **Its module:** `Type`, `create` and `style`.
   Where an image must differ:
   - **No `size`:** its own size comes from the image (pixels ÷ density), and `width`/`height` set the box.
   - **Fitting:** `stretch` and `stretchDirection` fit a shape into another shape (decision 13).
   - **`scaling`** keeps pixel art square when enlarged (decision 14).
   - **`cornerRadius`** takes a float or a `CornerRadius`, like Border's, and rounds the drawn image itself.
   - **No foreground tint.**
   Inheriting Border was turned down: it would have brought `child` and `overlay`, and its `cornerRadius` would have
   clashed. A frame, background or padding comes from wrapping the ImageView in a Border.
7. **Failure is reported once per image, not per element:** through `ReportError` by the first element that asked.
   Every holder then draws nothing, and the image isn't tried again until it has left the cache.
8. **Context members for element authors:** `ctx.Image image` gives `DecodedImage voption` (none while loading or
   failed), and `ctx.ImageSize image` gives its size in DIPs, known before decoding for in-memory sources. Both are
   on the measure and render contexts, and both hold the image (3).
9. **Names:**
   - **`ImageStretch`:** `None`, `Fill`, `Uniform`, `UniformToFill`, qualified access required (`None` would clash
     with Option's, and `Stretch` with the alignment case).
   - **`StretchDirection`:** `DownOnly`, `UpOnly`, `Both`.
   - **`ImageScaling`:** Rendering's type, aliased in Core as Cursor is.
10. **The pool** is fixed at 32 MB of CPU pixels, mip levels included (about four 1080p photos); an `App` setting
    when an app needs more.
11. **The Gallery's Images page:**
    - **Pictures:** procedural PNGs written by `tools/MakeGalleryImages.fsx` and embedded as resources, plus a live
      `fromPixels` pattern, and a broken image (`fromBytes` with corrupt bytes) reporting its error.
    - **What it shows:** the stretch modes and directions, smooth against pixelated, a rounded avatar and a 2× image.
    - **Later:** the URL demo in (e), cursors in (f).

**(e) decisions (grilled in one round; Tim: yes to all, 2026-11-17):** `Image.fromFile` and `Image.fromUrl`.
1. **One shared `HttpClient`** on the desktop and in the browser, where .NET's runs on `fetch` (decision 11 said
   `fetch` and `HttpClient`: one path does both, with no JS of ours). It lives in a holder type. What it adds to a
   NativeAOT exe is measured; over about 1.5 MB, revisit.
2. **URLs:**
   - **Forms:** `http` and `https`; in the browser a relative URL resolves against the page; on the desktop a
     relative one is an error. `file:` goes to `fromFile`.
   - **Key:** `"url:"` plus the absolute URL. A change on the server isn't noticed while cached; a new URL
     (`?v=2`) is a new picture.
3. **Files:**
   - **Relative paths** resolve against the app's folder (`AppContext.BaseDirectory`), not the current directory,
     which changes between Rider, a terminal and a shortcut.
   - **Reading and key:** read off the UI thread, then decoded as in (d). The key is `"file:"` plus the full path.
   - **In the browser** it fails through `App.onError`: no file system; use `fromResource` or `fromUrl`.
4. **Downloads:**
   - **How many:** at most 6 at once for the process (the browser's limit per server); the rest wait in the order
     asked.
   - **Cancelling:** one waiting or running while nothing holds it is cancelled and leaves the cache, so scrolling
     past 9,000 avatar rows downloads about what's shown.
   - **Decoding:** a finished download is always decoded and pooled.
5. **Failures,** once per image ((d) 7):
   - **What counts:** a response that isn't 2xx ("404 Not Found"), a 30-second timeout, a response over 64 MB
     (decoding 8192² needs no more), and a network error.
   - **CORS:** in the browser a cross-origin URL without CORS headers fails with no detail, so the message says it
     must send Access-Control-Allow-Origin.
   - **Retrying:** as in (d), tried again only after it has left the cache; no retry timer.
   - **The timeout** runs on real time, not the platform clock: it's a network deadline, not something a frame waits
     on. Tests shorten it through an internal setting.
6. **No headers or credentials:** a signed-in image is downloaded by the app and given to `fromBytes`, which keeps
   tokens out of the process-wide cache.
7. **The Gallery:**
   - **The picture:** Wikimedia's `Example.png` (172 × 178, public domain, served with
     `Access-Control-Allow-Origin: *`): empty while loading, then the layout settles.
   - **The failure:** a URL that gives 404, giving one toast.
   - **Offline,** the first fails with a toast.
8. **Tests:**
   - **HTTP:** a fake handler behind an internal switch answers per URL (bytes, status, delay, error) and records
     cancellations.
   - **What they cover:** the limit of 6, cancelling, 404, oversized responses, the timeout, re-measuring when a
     size arrives, and a 10,000-row scroll downloading only what's shown.
   - **Files** go in a temp folder. No real network: the Wikimedia picture is on the checklist.

**(f) decisions (grilled in one round; Tim: yes to all, 2026-11-17):** image cursors (decision 21) and the inspector
(decision 22).

1. **An element holds its image cursor's image** while it's in the tree, like an ImageView, so decoding starts on
   realization (any source, URLs too). If the pointer gets there first, the arrow shows, then the cursor changes
   when the picture is ready: a cursor change, no frame.
2. **Failures** are reported once through `App.onError` (the same `ImageLoadException`, once per image); the arrow
   shows.
3. **Size:** the image's own size in DIPs (pixels ÷ density) unless `Cursor.size` gives the width (the aspect ratio
   kept). Each window scale gets a bitmap resampled to whole pixels on the CPU (our code: the same pixels
   everywhere); `Cursor.scaling` (`ImageScaling`, default Smooth) picks smooth or nearest. When the size in pixels
   already matches, the pixels are used as they are.
4. **The hotspot** defaults to the centre, as `fromIcon` does.
5. **One settings type:** `Cursor.fill`, `outline` and `outlineWidth` don't apply to an image cursor; using them warns
   once (`DebugWarnings`).
6. **At most 128 DIPs** a side (browsers ignore larger cursor images): larger ones shrink to fit, with a debug warning.
7. **Inspector:** an **IMAGES** section for any selected element holding images (ImageView, image cursors, custom
   elements using `ctx.Image`): per image its source, pixel size, density and DIP size, loading / ready / failed
   (with the error), "reduced from W × H", how many elements hold it and whether it's pooled, and a thumbnail up to
   96 DIPs. An `Image` property shows its source; an image cursor shows as `image <source>`. "In the atlas" is
   dropped: atlas places belong to each GPU device, which Core doesn't see.
8. **Gallery:** an "Image cursors" section on the Images page: the potion at 32 DIPs, pixelated, pointing from the
   bottle's mouth, and a target made with `Image.fromPixels`, smooth, pointing at its centre.
9. **Tests:** the resampled bitmaps byte for byte (1×, 1.5×, 2×; smooth and pixelated), default and given sizes, the
   hotspot, the 128 cap, the element holding its cursor's image, the arrow until ready then the change, one report
   for a failure, the inspector's IMAGES lines; a screenshot of the inspector showing the section.

**(f) as built (2026-11-17):**
- **`Cursor.fs`:**
  - `Cursor.fromImage` and the `Cursor.scaling` setting. The cursor options gained `SizeSet`, `Scaling` and
    `IconOnlySet` (fill, outline and outlineWidth set it; an image cursor warns once in Debug).
  - Each image cursor's picture and settings (`ImageCursorSpec`) are found from its `CursorImage` through a
    `ConditionalWeakTable`, by reference. The spec keeps the decoded picture once an element has seen it ready, so
    the platform can ask for a bitmap again later (another scale) without the cache.
  - Resampling is ours (`ImageCursor.weights`/`resample`), separable, in doubles, rounded once:
    - pixels used as they are when the size matches;
    - nearest when enlarging pixelated, bilinear when enlarging smooth;
    - the area each pixel covers when shrinking (smooth either way, as `DrawList.DrawImage` does).
  - The key is "image|<image key>|density|size|hotspot|scaling".
- **Holding:**
  - An element whose `cursor` is an image cursor holds its picture in `RenderShown` (decoding starts on
    realization), and reports a failed picture then, not only on hover.
  - Pointer input (`InputRouter.updateCursor`) checks the picture is ready. If it isn't, it shows the arrow and
    waits on the image (`ImageHold.CursorWaiting`), then shows the mouse's cursor again: no frame drawn (a test
    checks).
  - Changing only an element's cursor draws no frame (power rule), so the old picture is let go at the element's
    next render, not at once.
- **Inspector:**
  - `Element.HeldImages` (public; `HeldImage`, `ImageStatus`) gives the source image, the status, the pixel size,
    the decoded picture and the number of holders.
  - IMAGES (after PROPERTIES) shows source, size in pixels and DIPs, state (failed in red, with the message),
    "reduced to", "held by", and a thumbnail. The thumbnail is drawn from the decoded picture without holding it, so
    the count stays true.
  - **"Pooled" is left out:** the pool only has images nothing holds, so for a held image it would always say no.
  - An `Image` value and an image cursor show by their source key.
- **Gallery:** "Image cursors" on the Images page, with the potion (32 DIPs, pixelated, hotspot 16, 3) and a 32-pixel
  target from `Image.fromPixels`.
- **Checked in Chrome:** both cursors show as 32 × 32 CSS cursor images with the right hotspots, and there are no
  console errors.
- **Tests:**
  - 13 new unit tests (12 in `ImageCursorTests.fs` and the downloaded-cursor test in `ImageLoadingTests.fs`): the
    bitmaps byte for byte at 1×, 1.5× and 2×, the sizes, the hotspot, the cap, the icon-only warning, equality,
    holding and letting go, one report for a failure, the arrow until a download arrives, and the IMAGES lines with
    the thumbnail.
  - The `inspector-images` screenshot; Vulkan matches. It loads the Gallery's styles page first: custom states are
    process-wide, and the STATES row showed "chosen" only when another test had run first.
  - The Images page screenshot is taller (1560) for the new section.

**(e) as built (2026-11-17):**
- **`Image.fs`:**
  - `Image.fromFile` (relative paths against `AppContext.BaseDirectory`) and `Image.fromUrl`. A path starting with
    "/" is kept relative, since `Uri` reads it as a file on Unix; a `file:` URL becomes `fromFile`.
  - A relative URL's key is "url:" plus the URL as given: there is one page, so it names one picture.
  - `ImageHttp` (internal): the client (its own `Timeout` off; ours covers the body too), the test handler, the
    timeout, the page's address, and `Get`.
- **Only `fromUrl` links `HttpClient`.** The URL source carries `ImageHttp.Get` as its download function, so the cache
  never names `HttpClient`. Measured as NativeAOT Gallery exes: 10,851,840 bytes with `fromUrl`, 8,330,752
  without (512 bytes more than with no download code at all). So **2.4 MB, but only for apps that call `fromUrl`**.
  That is over decision 1's 1.5 MB line; **Tim accepted it** (2026-11-17): the standard client, and opt-in by use. Hello (no images) is unchanged.
- **`ImageCache.fs`:**
  - A queue of entries with 6 places. Each running download has a `CancellationTokenSource` cancelled after the
    timeout, or by `Release` when the last holder goes (outside the lock).
  - A dropped entry leaves the cache at once, and its download's end is ignored.
  - Files are read and decoded in one `RunDecode`.
  - Failures: "404 Not Found", "The download took longer than 30 s.", "The response is over 64 MB.", the
    relative-URL and scheme messages, and the browser's "no file system" and CORS hints.
- **A `User-Agent` on the desktop** (`Scribble/1.0`): Tim got 403 from Wikimedia, which refuses requests without one
  (curl: none gives 403, any gives 200). Browsers send their own and forbid setting it, so it's left alone there. This
  is our own header, not one set by apps, so decision 6 stands. A test checks it is sent; a real desktop download was
  checked once with a throwaway test.
- **Awaits use `ConfigureAwait(false)`.** Otherwise, on the desktop, the download's continuation (and its decoding)
  came back through the UI thread's synchronization context.
- **Found in Chrome:** reading the response stream in chunks fails in single-threaded WebAssembly ("Cannot wait on
  monitors on this runtime"). In the browser the body is read whole (`ReadAsByteArrayAsync`) after the length check.
  Checked afterwards: Wikimedia's `Example.png` downloads and draws, and the 404 gives one error.
- **The browser's page address:** `scribble-platform.js` `pageUrl()` (`document.baseURI`), `BrowserPlatform.PageUrl`,
  set into `ImageHttp.BaseUrl` by Scribble.Browser's `App.run` (Core's internals are now visible to Scribble.Browser).
- **Gallery:** "From a URL" on the Images page, behind a "Load from Wikimedia" button, so nothing is downloaded until
  asked (and the tests never touch the network).
- **Tests:** 10 in `ImageLoadingTests.fs`, with a fake `HttpMessageHandler`:
  - **Behaviour:** size after download, 404 once, over 64 MB, timeout, relative and ftp URLs, the limit of 6 and the
    order, cancelling, files, and keys.
  - **The long list:** 10,000 rows asking only for realized rows, with the first rows' 6 downloads cancelled after the
    jump.
  - **Taking down:** each test takes its tree down at the end, so its downloads don't fill the next test's places.
  - **The Images page picture** was reshot taller for the new section (the same on Vulkan).

**(d) as built (2026-11-17):**
- **`Foundation/Image.fs`:** `Image` (sealed; `Key`, `Density`; equal by key and density), `Image.fromResource`,
  `fromBytes`, `fromPixels`, `Image.density`; `ImageStretch`, `StretchDirection`, and `ImageScaling` aliasing
  Rendering's.
- **`Foundation/ImageCache.fs`:**
  - `ImageCache` (internal; `Shared`, a locked static field): entries by key with a holder count, a recently-used pool
    counting width × height × 16/3 bytes (levels included), oldest out first, `DecodedImage.Release()` on the way out.
    A failed entry leaves as soon as nothing holds it.
  - Resources and bytes read their header at once (`ImageDecoder.readInfo`), so the size is known before decoding.
    Decoding runs through `Task.Run`; `ImageCache.DecodeInlineOnThisThread` (thread-static) runs it inline in unit
    tests. `fromPixels` premultiplies at once (rounded, `(c·a + 127) / 255`).
  - `ImageLoadException` (public: `Key`), the debug warning for reduced images, and a missing resource's message
    listing what the assembly has and how to embed one.
- **`Element`:** a lazily made list of `ImageHold`s. `ImageOf`/`ImageSizeOf` (behind `ctx.Image` and `ctx.ImageSize`
  on the measure and render contexts) hold, report a failure once per image (`ClaimReport`), and while loading ask the
  cache to post back through the root's dispatcher, then measure again (size unknown) or redraw. After an element's
  render, the holds it didn't touch since the last render are let go; leaving the tree lets go of all.
- **`Elements/Basic/ImageView.fs`:** as IconView: `ImageView.image`, `stretch`, `stretchDirection`, `scaling`,
  `cornerRadius` (float or `CornerRadius`); internal `fit` and `draw`. What sticks out of the box is left out by
  drawing only the visible part of the image (a `source` rectangle), not by clipping. The fitted image is placed on
  whole pixels, keeping its size: a 1-pixel image centred on a half pixel vanished under `Math.Round`'s
  round-to-even.
- **The Gallery:**
  - The Images page (`ImagesPage.fs`, a seventh tab). `tools/MakeGalleryImages.fsx` draws `landscape.png`,
    `potion.png` and `avatar.png` into `samples/Gallery/Images`, embedded by file name.
  - `App.onError` shows errors as toasts (`LayersPage.toast`), so the broken image doesn't end the app.
- **Tests:**
  - 15 unit tests (`ImageViewTests.fs`), among them 10,000 rows decoding only the realized ones, also after jumping
    to 9,000 (none of rows 100–8,900 decoded).
  - The `images-page@1x` picture, the same on D3D12 and Vulkan.
  - The Gallery's six tab-row pictures, regenerated for the new tab (the only difference).
- **Checked in Chrome:** the browser Images page decodes, uploads and draws, the slider makes new pictures, and the
  broken image gives one toast with no console errors.

**(a) as built (2026-11-17), waiting for Tim's check:** `samples/Gallery` (`Gallery`, `Gallery.Desktop`,
`Gallery.Browser`, port 5281; `.run/Gallery.*`), files `Common.fs` (now also `page title line sections` and
`section title note demo`, the one look every page uses), `ComponentsPage.fs` (the Elmish, List and Load tabs as three
sections), `LayoutPage.fs`, `ControlsPage.fs`, `ScrollingPage.fs` (the 10,000 messages, list 340 tall),
`LayersPage.fs` (toast and modal buttons only), `StylesPage.fs`, `GalleryApp.fs` (`module
Scribble.Samples.GalleryApp`: `gallery` and `app`, 720 × 560, title "Gallery – <page>"); pages are in the namespace
`Scribble.Samples.Gallery`. Each page module has `view ()`. **No Images page yet**: a placeholder would have meant
seven phone pictures now and again at (d); it arrives with `ImageView`. The new `samples/Counter` is the README's
first example in one file (with a "−" button too, and the README's example gained it), `App.size (360, 200)`,
hosts `Counter.Desktop`/`Counter.Browser` (port 5283, no inspector). Scribble.Serve serves the Gallery on 5281 and the
Counter on 5283. Tests: `TabsTests.fs` became `GalleryPagesTests.fs` (state kept across Components and Styles; six
headers; new: every page opens with its title and a line); `CounterTests.fs` became `GalleryTests.fs` (`counter@1x`
is now the new Counter's view; `gallery@1x`, `gallery-light@1x`; the styles pictures show the page's demo without
the page title), `LayoutTabTests.fs` became `LayoutPageTests.fs` (`layout-page@1x`, 720 × 1100), phone pictures
`gallery-phone-<page>@3x`; the inspector pick test finds the "+" instead of a fixed point. Found on the way: the
fake window didn't draw after `Resized` (real platforms do), so tests that resized laid the app out at 400 × 300
until something else asked for a frame; `FakeWindow.Send (Resized _)` now asks for one. 435 unit tests (+5 timing
skipped), 61 rendering tests (one phone page fewer), all pictures looked at.

## Review of the image pipeline (M8 step 3b), 2026-11-17

Tim asked whether the image work needed more tests and a review for edge cases. I read the decoder, the
decoded image, the atlas, the cache and downloads, and the element side again. Then I wrote tests that go past
single cases (`ImageRobustnessTests.fs`):
- hostile headers;
- chunks dropped, repeated, swapped, inserted or given wrong lengths;
- many random byte changes behind valid CRCs, with allocation budgets as well as "fails cleanly";
- the shelf packer over 20,000 random steps;
- the atlas over 400 random frames;
- the cache on 8 threads holding and letting go while pictures decode;
- downloads finishing, failing and being cancelled on 6 threads.

They found three bugs. Tim: "yes, fix all three".

1. **The decoder allocated for the size the header claims.**
   - **Found:** a 1 KB file claiming 20,000 × 20,000 allocated 1.6 GB before failing. A file under 1 MB that
     really inflates to 140 million pixels decoded in full, because there was no limit below .NET's array size.
   - **Fixed in three ways:**
     - Image data that can't fill the claimed size fails first: deflate packs at most about 1,032 to 1, and the
       check allows 1,040.
     - The inflate buffer starts at 32 times the compressed size (at least 64 KB, at most the claimed size) and
       doubles as data actually arrives.
     - Pictures over **100 million pixels** (`Pixels.maxDecodePixels`: 400 MB decoded, a 100-megapixel camera)
       fail as "too big to decode". `readInfo` still reads their size.
   - Failing now costs about what the file holds.
2. **A download function whose task faulted kept its download place forever,** and its picture stayed loading.
   `ImageHttp.Get` catches everything itself, so it couldn't happen yet; another download path would have hit it.
   Fixed: `ImageCache.Fetch` treats a throw like a returned error.
3. **A hidden element kept its pictures.** Pictures were let go only while drawing, and hidden elements aren't
   drawn.
   - **Fixed:** the first time a drawn element is found hidden, it and everything inside it let go (once: a flag,
     not a walk every frame). Showing it again usually finds the picture in the cache's 32 MB pool.
   - Elements merely scrolled out of view keep theirs (they come back soon, and the ItemsView recycles them anyway).

**What held up:**
- **Decoder:** no exception other than `ImageFormatException`.
- **Packer:** regions never overlapped and stayed inside the page, and every one came back.
- **Atlas:** every upload was inside its page, and every page was given back.
- **Cache:** the holder counts came back to 0, the pool stayed under its limit, and a released picture was never
  handed out.
- **Downloads:** every download place came back.

**Left as they are (minor):**
- A custom host without a dispatcher never re-lays out an element whose picture finishes loading.
- `Image.fromPixels` premultiplies on the UI thread.
- In the browser, a response with no length header is read whole before the 64 MB check.
- In single-threaded WebAssembly a big decode blocks the UI (by design, (d)).

**Results:**
- **Unit tests:** 868 passed, 6 timing tests skipped (11 new).
- **Rendering tests:** 76 passed.
- **Timing tests:** 6 passed.
- **NativeAOT Gallery publish:** only the FSharp.Core warnings already excepted.

## M8 step 4: layers and selection controls (planned 2026-11-17)

What the step covers (M8 round 1, decision 1): content shown in a layer from the view, tooltips, ListBox and
RadioButton (selection), ComboBox, Dialog and Toast. Facts it rests on: layers can be added and removed at any time
(`ElementHost.AddLayer`/`RemoveLayer`), each with its own focus; a modal layer blocks only while it shows something;
the Gallery's toasts and dialog still listen to F# events because a view can't put content into a layer yet.

**Round 1 (grilled 2026-11-17; Tim: yes to all, Q3 reshaped by Tim):**
1. **Four parts, each tried by Tim before the next:** **4a** the flyout and tooltips; **4b** ListBox and RadioButton;
   **4c** ComboBox; **4d** Dialog and Toast (the Layers page and `App.onError` move onto them). 4b, 4c and 4d each
   get their own round.
2. **Flyout** is the name (WinUI's): content shown in its own layer, anchored to an element. GLOSSARY: Flyout.
3. **Flat settings on every element** (Tim: "instead of a Flyout element or a `flyout [ ... ]` list"), inherited
   from `Layoutable` like `cursor` and `tooltip`, so styles can set them per type:
   - `flyoutContent view`: what shows; giving it is what makes the element have a flyout;
   - `flyoutPlacement` (`Placement.Below` default, `Above`, `Left`, `Right`);
   - `flyoutAlignment` (`FlyoutAlignment.Start` default, `Center`, `End`) along the anchor;
   - `isFlyoutOpen` and `onFlyoutOpenChanged`;
   - `flyoutLightDismiss` (default true);
   - `flyoutMatchAnchorWidth` (ComboBox).
   The content is part of the view: it diffs with it, its components keep their state, and it goes when the anchor
   leaves.
4. **Its own layer:** each open flyout (and later each dialog) gets a layer added on top when it opens and removed when
   it closes, so stacking follows opening order (a flyout from inside a dialog or another flyout shows above it),
   always below the inspector. Toasts (4d) get one fixed layer. `App.layer` stays for app-specific layers.
5. **Controlled or not** (decision 8): given `isFlyoutOpen`, it shows exactly that and reports `onFlyoutOpenChanged`;
   left out, the element's click toggles it (a Button's click, Enter and Space included; any other element's press
   and release on it). A controlled flyout opens only when the view says, so a model can open it from anywhere.
6. **Light dismiss:** Escape, or a press outside both the content and the anchor, closes it; that press is consumed
   (WinUI, WPF, Windows menus: closing a menu never clicks what's behind it). Focus moves into the content when it
   opens and back when it closes. `flyoutLightDismiss false` keeps it open until the view closes it.
7. **Placement:** the side, flipped to the opposite one when it doesn't fit; pushed inside the window; never larger
   than the window (tall content scrolls if it's in a ScrollViewer); at least the anchor's width with
   `flyoutMatchAnchorWidth`. It follows the anchor at the next frame; an anchor scrolled out of view closes it.
8. **Tooltips:** `Layoutable.tooltip "text"` and `Layoutable.tooltipView view` (inherited: `Button.tooltip`). Shown
   once the pointer rests 500 ms (platform time, no frames while waiting); within 300 ms of another tooltip the next
   shows at once. Hidden on leave, press, wheel or a key. Below the pointer, flipped and kept inside the window; never
   takes focus or input. None on touch; keyboard-focus tooltips later. The inspector's toolbar hints move onto them.
9. **Tests and Gallery for 4a:** unit tests for opening and closing (controlled and not), each dismissal, focus in and
   back, flipping and clamping at each edge, following the anchor and closing when it scrolls away, stacking by
   opening order (a flyout from a flyout), and tooltip timing on fake time with no waiting frames; screenshots of an
   open flyout and a tooltip. The Layers page gets a Flyout section (a menu-like one, a flipped one, a pinned one) and
   tooltips on a few buttons.
10. **FlyoutPresenter:** the content sits in a themed element (the theme's surface, rounded corners, a shadow) that
    styles can restyle, so content needn't bring its own background.

**4a as built (2026-11-17):**
- **Settings** (`Framework/Flyout.fs`): `FlyoutProps` with the settings above on `Layoutable`, plus `Placement` and
  `FlyoutAlignment`. An uncontrolled flyout keeps its state in a local property. `Flyouts.request` changes it, or
  for a controlled one only calls `onFlyoutOpenChanged`; the handler is guarded.
- **How the host hears:** a property can be *watched* (`Prop.Watched`, only `isFlyoutOpen` today). When an
  element's own value of it changes, or an element with one joins a tree, it calls `IElementRoot.Watch`. The host
  queues the element with its layer. An uncontrolled toggle notifies the same way.
- **Opening and closing:** at the start of each frame the host opens or closes the layers of queued anchors. It
  lays the layers out bottom to top and checks the queue again after each one, so a flyout opened by a view update
  shows in that same frame.
  - Each flyout layer is named "Flyout of …" and has order `Int32.MaxValue - 2`: above the app's own layers,
    below the inspector, stacked in opening order.
  - It takes app styles if the anchor's layer does; otherwise it copies that layer's theme, so the inspector's
    flyouts stay inspector-themed.
- **Following the anchor:** before a flyout's layer is laid out, the host reads the anchor's window rect,
  placement, alignment, width matching and content. If any changed, it gives the layer a new view: `FlyoutRoot`
  (fills the window, passes the pointer through, places its one child) around a `FlyoutPresenter` holding the
  content.
  - When the anchor's `VisibleBounds` is empty (scrolled out, hidden), the presenter hides and the host asks to
    close, after the frame.
  - A detached anchor, or one with no content, closes at once.
- **Placement** (`FlyoutPlacement.place`): 4 DIPs from the anchor. It flips when the content doesn't fit and the
  other side has more room, then clamps into the window and rounds to whole DIPs.
- **New element part:** `Element.passThrough`, an element that is never hit itself, only its children
  (`ElementType.HitsSelf`).
- **Light dismiss:**
  - On a press (first button), the host walks open flyouts from the top. It stops at the first one whose content or
    anchor is under the press, and dismisses the dismissable ones above it.
  - If any was dismissed, the press and its release go nowhere.
  - Presses in a layer above the flyouts (the inspector) dismiss nothing.
  - Escape that nothing handled dismisses the top dismissable flyout, unless keys are in a layer above.
- **Focus:**
  - **Opening:** after the flyout's first layout, its layer takes the keyboard and focus goes to its first focusable
    element (`InputRouter.FocusFirst`). The ring shows only if the anchor's layer was in keyboard mode.
  - **Closing:** if the flyout's layer had the keyboard, focus goes back to what had it in the anchor's layer,
    without scrolling it into view (`InputRouter.Restore`).
  - Found by a test: `Focus()` scrolled an anchor that had scrolled away back into view.
- **Clicks:** an element with `flyoutContent` counts as clickable. `Clicks.click` toggles its flyout after `onClick`
  and the type's click.
- **Tooltips** (in `ElementHost`, mouse only):
  - **Finding the owner:** on each move with no button down, the host finds the nearest element under the pointer
    (in the topmost layer there) with `tooltip` or `tooltipView`.
  - **Waiting:** while it waits, each move restarts `Dispatcher.PostAfter` 500 ms (a generation counter drops stale
    waits). Moving from a shown tooltip to another element, or arriving within 300 ms of one hiding, shows the next
    at once.
  - **Showing:** a "Tooltip" layer (order `Int32.MaxValue`, added last, so above the inspector) holds `FlyoutRoot`
    around a `TooltipPresenter` (not hit-testable). It's anchored to a 16-DIP-tall rect at the pointer, so it shows
    below the pointer.
  - **Hiding:** a press, the wheel or a key hides it and suppresses that element until the pointer leaves it. Leaving
    the window or the owner going away hides it too.
  - **Without a dispatcher** (a custom host with none) there are no tooltips.
- **Theme:** `FlyoutPresenter` and `TooltipPresenter` extend Border. The theme gives both the surface colour, a
  1-DIP border line, the theme's corners and `ThemeConvert.floatShadow` (the theme shadow ×4). Padding is 4 on the
  flyout and 8×4 on the tooltip.
- **Inspector:** the toolbar buttons use `Button.tooltip`. The title stays "Inspector", or shows the picking
  status.
- **Gallery:** the Layers page has Flyout (a controlled Actions menu whose items close it and toast), Placement
  (four sides and alignments), Pinned (a slider and a check box) and Tooltips (two text tooltips and a
  `tooltipView`).
- **Tests:** 18 unit tests (`FlyoutTests.fs`, 13 flyout and 5 tooltip); the inspector's hint test is rewritten for
  tooltips; screenshots `flyout-open@1x` (opened by keyboard, ring on the first item) and `tooltip@1x`, the same on
  Vulkan. The phone Layers picture was updated for the new sections.
- **Left for later:**
  - an element's own `theme` above the anchor doesn't reach its flyout (the flyout gets its layer's theme);
  - flyout content taller than the window is cut to the window, and scrolls only if it's in a ScrollViewer;
  - transforms on the anchor are ignored, as in `PositionInTree`;
  - keyboard-focus tooltips.

**Round 1b: the flyout becomes a value (Tim, 2026-11-17).** Tim asked whether `Button.flyout (Flyout.create [...])`
would be better. It changes decisions 3, 5 and 10. The flat settings were chosen in round 1 and built in 4a; what
tipped it now is what comes next:
- menus (`MenuFlyout`), context menus (`contextFlyout`) and ComboBox's list all want a flyout as one value that can be
  passed around;
- seven settings on every element's autocomplete were noise;
- one flyout's own look wasn't reachable (only `FlyoutPresenter.style`, for all of them).

FuncUI on Avalonia uses the same shape. Tim: yes to all.
1. **`Layoutable.flyout (Flyout.create [ ... ])`** replaces `flyoutContent`, `flyoutPlacement` and
   `flyoutAlignment`. **Flyout is the themed box itself:** it inherits Border and replaces FlyoutPresenter, so one
   flyout can say `Flyout.padding 0.0`, and `Flyout.style` restyles them all.
2. **On the Flyout:** `Flyout.placement`, `Flyout.alignment`, `Flyout.isOpen`, `Flyout.onOpenChanged`,
   `Flyout.lightDismiss` and `Flyout.matchAnchorWidth`. A reused Flyout value stays controlled the same way wherever
   it's put. Without `Flyout.isOpen`, the element with the flyout keeps its own open state and its click toggles it.
3. **`Flyout.content view`** is what it shows.
5. **One word everywhere: `content`** (Tim, the same day). Border's `child` becomes `content`, which Button, Flyout
   and every other Border inherit (the separate `Button.content` is gone, so no element accepts two words for one
   property). It already matched `ScrollViewer.content` and `TabItem.content`. Considered: WPF's split (`child` on
   plain containers, `content` on controls), rejected because Button then took both words. A panel's several views
   stay `children`. Element authors' `Prop.child` becomes `Prop.content` too (Tim: "I don't understand Prop.child
   staying"); "child" is kept only for the element tree itself (parent and child elements, `ctx.Children`).
4. **Tooltips stay flat:** `tooltip "text"` and `tooltipView view`. A `Tooltip.create` can come if tooltips need
   their own settings.

**As built (round 1b, 2026-11-17):**
- `FlyoutProps.flyout` holds the Flyout view on the element; it's watched, so the host hears a new one. The host reads
  `isOpen`, `onOpenChanged` from the view's own settings (`Flyouts.valueIn`), since the Flyout element exists only
  while it's open; an uncontrolled flyout's state is a local on its element.
- The Flyout view itself is the layer's one child: `FlyoutRoot` reads placement, alignment and `matchAnchorWidth` from
  it (so a change re-places an open flyout), and light dismiss is read from the Flyout element (styles count).
  `FlyoutTarget` keeps only the anchor's rect and the gap.
- The flyout screenshot is pixel-identical to 4a's. Three new tests: one flyout's own look, placement changed while
  open, one Flyout value on two buttons. The phone Layers picture changed only for the Pinned section's longer note;
  that flyout now shows its own look (accent line, 12-DIP corners and padding).
- `content` everywhere: `BorderProps.content` (the inspector shows "Content"), `Prop.content` and `PropKind.Content`, every sample, test and doc. The Layers
  page now defines its flyouts as values (`actionsMenu`, `sideFlyout`, `pinnedSettings`) and its buttons attach them;
  the page is a component, keeping whether the Actions menu is open. Pictures unchanged.

**Round 1c: `flyout` takes content or settings, and a flyout is styled inside its element (Tim, 2026-11-17).** Tim
wasn't sure about `Button.flyout (Flyout.create [...])`. Considered:
- a second setting, `flyoutSettings`: rejected. It's two settings that only work together, there's no single value
  to reuse, and every flyout slot would need a pair;
- one setting that takes any view and wraps it unless it's a Flyout: works, but it's a little magic.

Tim proposed overloads, and they infer cleanly (checked: a list in a `let`, an `if`, inherited Border settings). His
real aim was styling specific elements' flyouts, which a flyout's separate tree didn't allow in any syntax. Tim: yes
to both.
1. **`flyout content` or `flyout [ Flyout settings ]`:** `Button.flyout menu` for most flyouts, and
   `Button.flyout [ Flyout.placement Placement.Right; Flyout.content menu ]` when it needs settings. The settings list
   is the value to keep and reuse. `Flyout.create` becomes internal: given as content, a Flyout would sit inside
   another. `Flyout.style` stays.
2. **A flyout is styled as if inside its element.** This is how WPF and Avalonia treat a popup's owner. Its tree
   belongs to the element:
   - `Style.parent` and `Style.inside` look past the flyout to the element;
   - `styles` on the element or anything around it apply;
   - a `theme` around the element is the flyout's theme.

   So `Button.styles [ Flyout.style ... ]`, `Flyout.style [ Style.parent Button.Type [ Style.cls "toolbar" ] ]` and
   `Flyout.style [ Style.inside ComboBox.Type [] ]` all work. Inherited values (text colour, font) cross only
   through the theme: a flyout from a Button with white text doesn't get white text. When the element or something
   around it restyles (a class, the pointer over it, `styles` or `theme` changing) while the flyout is open, the
   flyout restyles. Tooltips are styled the same way.

**As built (round 1c, 2026-11-17):**
- `flyout` is an extension on Layoutable in `Elements/Basic/Flyout.fs` (module `FlyoutSettings`, auto-opened), so the
  list overload can name `Flyout`; `Button.flyout` finds it through inheritance. The content form wraps the view in a
  Flyout.
- `ElementTree.Owner` (and `IElementRoot.Owner`): the host sets it on flyout and tooltip trees. `Element.StyleParent`
  is the parent, except that the owner stands in for the tree's root. Selector matching, `StyleEngine.scopesAbove`,
  `themeStyles` and `NearestTheme` use it. `FromAbove` takes a root's base values from the owner's theme, not the
  owner's values.
- `ElementTree.RestyleHandler` hears every restyle request. The host's `restyleOwned` restyles an open flyout's tree
  when the request is for its element or something containing it.
- 5 new tests:
  - the element's `styles` reach its flyout and no other;
  - `Style.parent` with a class, and `Style.inside` by name;
  - a theme around the element, without the element's text colour;
  - a class added while open;
  - a tooltip styled through its element.

  All 5 fail with the owner links off, and the open-flyout one fails with the hook off.
- Gallery: the Layers page's placement panel has a `styles` giving its four flyouts square corners. The phone
  picture changed only for that section's longer note.

**Round 1d: no FlyoutRoot (Tim, 2026-11-17).** Tim asked why FlyoutRoot was needed. It placed the Flyout: placing
needs the Flyout's measured size and the window's, and only a parent arranges an element. It was also never hit
itself. Its costs:
- a line of noise in the inspector;
- the one special case in round 1c's `StyleParent`, which skipped the tree's root.

Decision: **a tree can place its root.** The Flyout (or the TooltipPresenter) is the layer's root. The tree measures
it against the window, as any root, and arranges it where a placement function says. Presses outside it already
fall through a layer that isn't modal. Tim: yes.

**As built (round 1d, 2026-11-17):**
- `ElementTree.Place: (Element -> Size -> Rect) voption`, internal until a layer outside Core needs it (dialogs,
  toasts). It's given the measured root and the window size; ValueNone fills the window as before.
- `FlyoutPlacement.flyout` reads the root Flyout's `placement`, `alignment` and `matchAnchorWidth`; the host
  closes over the anchor's current rect. Tooltips place their presenter below the pointer the same way.
- Removed: `FlyoutRoot`, `FlyoutRootProps`, `FlyoutTarget`. `StyleParent` is now the parent, else the tree's owner.
  `lightDismissOf` reads the root.
- Re-placing when the anchor moves needs nothing extra: the tree lays out its root every frame, and a moving anchor
  brings one. A test confirmed this; an explicit `InvalidateArrange` was redundant and left out.
- `Element.passThrough` stays for element authors (an overlay that lets the pointer through), though nothing in Core
  uses it now.
- Pictures are pixel-identical, flyout and tooltip screenshot included. A test checks the Flyout has no parent.

**Round 1e: content and flyouts from styles, an open item (Tim, 2026-11-17).** Tim asked whether the new syntax lets
a style set content. It doesn't: styles can't set `content` or `children` on any element (`Rule.check` refuses
non-value properties), and `flyout`, `isOpen` and `onOpenChanged` are marked not stylable.

I first called style-set content a bad idea. That was wrong. WPF and Avalonia both allow a style to set `Content`
(and WPF a `ContextMenu`). Their catch is that a setter's value is one instance: a control given that way can have
only one parent, so the second element fails ("already the logical child of another element"). The workarounds are
`x:Shared="False"`, `ContentTemplate`, or Avalonia's `<Template>` in a setter. Scribble's views are descriptions,
and each element builds its own elements from one, so that catch can't happen here.

The real reason is how Scribble applies styles. Children come from the view, and styles set values on elements that
already exist. Allowing it means:
- building children from the resolved content (after styles), not only from the view;
- comparing old and new content and rebuilding what differs when a style starts or stops setting it (a class, a
  state), during the style pass;
- styling the new children before layout (as ItemsView does with `RunStylesWithin`);
- for `flyout`: the host hearing styled changes (it hears only the view's today), and `isOpen` and `onOpenChanged`
  refused in styles (a styled flyout is uncontrolled).

Decision (Tim): **open item, not now.** It's a moderate change to Element, and it fits the model well. It waits for
a use that a view function can't serve as well: control templates, or the theme giving controls default content.
Until then, "these buttons get this menu" is a function or a shared settings list in the view. Listed under
"Deferred work". (Superseded by round 1f: not planned.)

**Round 1f: styles never set app state (Tim, 2026-11-17).** Tim saw that `isOpen` and `onOpenChanged` and content
are unstylable for the same reason: a style has no context. It's defined once, applies to many elements, and can't
see the model, `dispatch` or which element it's on. So anything bound to app state means nothing from a style:
- a controlled value would make every matched element controlled, with one constant;
- a handler can't tell which element fired;
- content can only be the same plain view everywhere.

WPF needed `TemplateBinding` and data bindings to make style-set content useful. Scribble didn't follow the rule:
styles could set `onClick`, `isChecked`, `Slider.value` and `onValueChanged`. The refused list was only what decides
matching, plus 4a's flyout trio for a technical reason.

Decision (Tim: yes): **styles set how things look and behave, never app state.** Not stylable:
- every handler;
- every controlled value;
- content, children, a flyout and tooltips;
- what decides matching, as before.

Each kind gets an error saying why. Round 1e is **not planned** rather than open: it would come back only with
something like template bindings. It's removed from "Deferred work".

**As built (round 1f, 2026-11-17):**
- `Prop.IsHandler` comes from the value's type: a function, or an optional one (`'a -> unit`, `voption`, `option`).
  Every handler is covered, an element author's own included.
- `Prop.IsAppState` (`AppState.mark`) is set on:
  - `isChecked`, `isIndeterminate` and the tab header's `isSelected`;
  - `Slider.value`;
  - `TabControl.selected`, `tabs`, and `TabItem.header`/`headerView`;
  - ItemsView's source, key and view functions;
  - ItemsView's and ScrollViewer's `scroller`;
  - `flyout`, `Flyout.isOpen`, `tooltip` and `tooltipView`.
- `NotStylable` is back to its first meaning: classes, name, key and theme.
- `Rule.check` gives three messages: handlers, content and app state, and matching.
- Nothing in the theme, the samples or the tests set any of these from a style.
- Left stylable as look rather than state: `TextBlock.text`, `IconView.icon`, `ImageView.image`.
- 2 new tests: every kind refused with its reason, and looks still allowed.

**Round 1g: the rule at compile time (Tim, 2026-11-17; reverted in round 1h).** Tim asked whether round 1f could be a compilation error.
It can. A view's settings list and a style's had one type (`Setting<Button> list`), so they need two. Settings
types come in pairs:
- `ButtonStyle` holds Button's settings and inherits `BorderStyle`;
- `Button` inherits `ButtonStyle` and implements `InViewsOnly`, an empty interface.

A setter for a handler, app state, content or what decides matching asks for `'k :> InViewsOnly`. `create` takes
`Setting<Button> list`; `style` takes `Setting<ButtonStyle> list`. So `Button.style [] [ Button.onClick … ]` gives
"The type 'ButtonStyle' is not compatible with the type 'InViewsOnly'".

Checked first in a scratch project:
- views and inline styles compile unchanged;
- a list kept in a `let` infers its type;
- `Button.flyout`, an extension, is found through inheritance.

App code rarely writes the `…Style` names. Element authors write the pair. Tim: yes, with the plain names for views.

`BorderStyle` was taken by the line-style enum. Tim chose to rename the enum to **`LineStyle`** (my
recommendation), which matches `Separator.lineStyle`: `Border.borderStyle LineStyle.Dashed`.

**As built (round 1g, 2026-11-17):**
- **Converted to pairs:** Core's 24 settings types, and the samples' CardGrid, Layers, AccentStrip, ProgressBar and
  SplitRow.
- **Left as they were:** the inspector's 4 internal types and the tests' `Box`. They inherit the view types, still
  compile and work, and their styles are checked when made.
- **Marked `InViewsOnly`:**
  - every `on…` setter;
  - `name`, `classes`, `styles`, `key`, `theme`;
  - `content`, `children`, `items`, `Button.text`;
  - `flyout`, `tooltip`, `tooltipView`, `Flyout.isOpen`;
  - `isChecked`, `isIndeterminate`, `Slider.value`;
  - `TabControl.selected` and `tabs`, `TabItem.header`;
  - ItemsView's `source`, `key`, `view` and `scroller`, and ScrollViewer's `scroller`;
  - `Semantics.isChecked` and `isSelected`.
- **Smaller changes:**
  - `Element.defaults<'k>` takes an explicit type: `Element.defaults<Slider> [ … Semantics.onAction … ]`, since a
    list mixing view-only and plain setters can't pick a type alone.
  - `Style.any` takes `Setting<LayoutableStyle> list`.
- **What still checks at runtime:**
  - **State blocks** (`Button.on States.hover [ … ]`): a block in a view has the view's type, so F# can't refuse its
    handler. `Style.on` now checks its settings when the block is made, rather than when the element is styled.
  - **Raw `Setting.create`** and element authors' own properties keep `Rule.check`. The refusal tests use raw
    settings now; the setters no longer compile there.
- **Seen at once:** while this was being built, Tim tried `Flyout.content` in the Layers page's `placementStyles`.
  It stopped compiling, with the error at that line.

**Round 1h: back to runtime-only (Tim, 2026-11-17).** Tim asked whether 1g introduced anything that would confuse
users. It did, mostly type errors that name types the user never wrote:
- a generic helper setting handlers: "missing a constraint 'when 'k :> InViewsOnly'";
- one list used in both a view and a style: "Expecting 'Setting<BorderStyle>' but given 'Setting<Border>'";
- `Element.defaults` with a handler: an ambiguous instantiation, needing `Element.defaults<Slider>`.

Then there was the noise: 29 more names in autocomplete, longer signatures, and the compiler's sentence instead of
ours. Tim asked whether a different design would be simpler. Considered:
- one generic settings type per element (`Button<View>`): worse annotations for everyone;
- a separate return type for view-only setters: needs implicit conversions in list literals, which F# warns about
  (FS3391) and warnings-as-errors fails on;
- an analyzer: an extra tool, uncertain in Rider;
- the runtime check alone.

Only the last is simpler, and it gives up the editor error. The mistake is still caught the first time the style is
made, which for a module-level style list is the first run, with our own message.

Decision (Tim): **runtime-only**. Kept from 1g:
- `LineStyle` (it reads better: `Border.borderStyle LineStyle.Dashed`);
- `Style.on` checking a state block's settings when the block is made. A handler in an inline
  `Button.on States.hover [ … ]` throws when the view is built, not when the element is first styled.

**As built (round 1h):**
- The pairs, `InViewsOnly`, the extra constraints and `Element.defaults<'k>` are undone. Code is back to 1f, plus
  `LineStyle` and the `Style.on` check.
- The refusal tests use the setters again (they compile in a style), plus a state block in a view.
- Unit tests: 878 passed, 6 skipped.
- Rendering tests: 76, pixels unchanged.
- Timing tests: 6.


### 4b: ListBox and RadioButton (grilling, 2026-11-18)

Facts: `ItemsView` takes `source`, `key` (a function from item to key) and `view`, and scrolls to an index or a
key; the semantics roles `List`, `ListItem`, `TabList` and `Tab` exist, no radio roles; states come from
`States.define` and `RefreshStates()`; decision 8 says a control's value is controlled when the view gives it. 4c's
ComboBox will likely show a ListBox in its flyout.

**Round 1 (Tim: yes to all, Q2 amended):**
1. **ListBox extends ItemsView:** `source`, the key, `view` and `layout` inherited, selection added, each realized
   item wrapped in a `ListBoxItem`. It virtualizes, and ComboBox can reuse it.
2. **The selection is held as item keys** (the index when no key is given), not indices or items: keys survive
   inserts, sorting and filtering, and are what `ScrollTo key` takes. Tim: the key setting is named
   **`keySelector`** (round 2, decisions 9 and 10).
3. **Selection modes None, Single and Multiple now** (`ListBox.selectionMode`): Ctrl+click toggles, Shift+click
   selects a range over the source's indices (items never realized included), Ctrl+A selects all.
4. **Keyboard:** one Tab stop; focus lands on the selected item, else the first. Up/Down, Home/End and
   PageUp/PageDown move focus, realizing and scrolling to the target only. Single: selection follows focus.
   Multiple: arrows move focus, Space toggles, Shift+arrows extend. Typeahead waits for M9.
5. **`RadioGroup` owns the radios' selection:** `RadioGroup.selected v`, `onSelectedChanged`, `RadioButton.value v`
   on each child; controlled or not; one Tab stop with arrow keys; a panel (StackPanel by default). The value is a
   `'T` with equality, read back through the handler's type.
6. **`ListBoxItem` is a public element type** (extends Border; `ListBoxItem.style`) with a `selected` state beside
   hover, pressed and focused; the theme gives the selected item an accent background. **RadioButton is a
   ToggleButton** like CheckBox, with the `checked` state and a round mark.
7. **Semantics:** `List`/`ListItem` with `isSelected` and a multi-select flag; new roles `RadioGroup` and
   `RadioButton` (checked).
8. **Gallery and tests:** a Lists page (a 10,000-item ListBox per mode showing the selection, a RadioGroup for its
   options, a small templated list); unit tests for selection per mode, controlled and not, the keyboard, a Shift
   range over unrealized items, selection kept through inserts and sorting, the same elements realized for 100 and
   10,000 items; screenshot tests of their states in both themes.

**Round 2 (Tim: yes to all, 2026-11-18).** Fact: every view already has a `key` (a plain value the differ matches
children by: `TextBlock.key todo.Id`), so `ItemsView.key` meant a second thing under the same name.
9. **`ItemsView.key` becomes `ItemsView.keySelector`** (14 uses; no alias, nothing is published). `key` is always a
   value, `keySelector` how to get one from an item.
10. **`ItemsView.view` becomes `ItemsView.viewSelector`, with two overloads,** `'T -> View` and `'T -> string` (Tim:
    "what is displayed for each item", e.g. `fun p -> p.Name`). F# picks the overload by the lambda's return type
    (tried in a script: lambdas, named functions and `string` all resolve, through `ListBox.` too). Both live on
    ItemsView (a derived type can't add an overload to an inherited static). The text form is a themed TextBlock
    and the item's accessible name; the view form's name comes from its own semantics; with neither, `string item`.
    A view item has no separate text; typeahead (M9) or ComboBox (4c) adds one if they need it. Considered: a
    `valueSelector` meaning WPF's `SelectedValuePath` (a value that sets the selection must pick one item, so it is
    a key), and a separate `textSelector` beside `view` (two settings for one job). The selection speaks in keys,
    and its change event carries the selected items too.
11. **A selected key that leaves the source:** uncontrolled, the ListBox drops it and raises `onSelectedChanged`;
    controlled, it leaves the app's list alone, shows nothing selected for it and raises nothing. A key that comes
    back is selected again if still in the list.
12. **Clicks and invoking:** in Single mode clicking the selected item keeps it, Ctrl+click deselects; in None mode
    items take focus and can be invoked, not selected. **`ListBox.onItemInvoked`** (double-click, Enter, a tap in
    None mode) gives the item and its key; ComboBox will close its flyout on it.
13. **RadioGroup extends StackPanel** (vertical, `orientation` and `spacing` inherited). Arrows in both directions
    move the selection and wrap; Tab lands on the checked radio, else the first; disabled radios are skipped;
    radios may sit anywhere below the group (found in view order).

**Round 3 (Tim: yes to all, 2026-11-18).**
14. **One selection pair for every mode:** `ListBox.selected (keys: 'K list)` (empty: none) and
    `ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<'T, 'K>) -> ...)` with `Keys`, `Items`, `Added`,
    `Removed`, and `Key`/`Item` (`voption`, for Single). `'K` comes from `keySelector`, else `int` (the index).
15. **ListBox's look (WinUI's ListView):** borderless, transparent; items padded 8 × 12, a subtle background on
    hover, darker pressed; selected: an accent-tinted background and a 3-DIP accent bar at the left edge; the focus
    ring for keyboard focus only.
16. **RadioButton's look follows CheckBox:** an 18-DIP circle, 1.5-DIP outline, text to the right; checked: accent
    outline and an 8-DIP accent dot growing in with CheckBox's timing; hover, pressed and disabled as CheckBox;
    aligned left.
17. **One step, tried before 4c:** the renames first as their own commit, then ListBox, RadioGroup and
    RadioButton, the Lists page and the tests; screenshot references of the page and the states at 1× and the
    phone pass at 390 DIPs.
18. **GLOSSARY:** selection, key selector, view selector, invoke, item container (avoid `SelectedIndex`, "value
    member", "display member").
19. **Invoking an item** (Tim asked how it works): selecting picks an item, invoking acts on it ("open this").
    A double-click or Enter invokes in every mode; in None mode a click, tap or Space invokes; in Single a
    double-click selects, then invokes; in Multiple Space toggles. On touch a tap selects in Single and Multiple
    (a list that opens things on a phone uses None, or a button in each item). `ListBox.onItemInvoked (fun (e:
    ItemInvokedEventArgs<'T, 'K>) -> ...)` gives the item and its key; it is the `ListItem`'s default action for
    screen readers; ComboBox (4c) selects and invokes on a click or Enter and closes on the invoke.

Status: **agreed (Tim, 2026-11-18: "yes, record it and build").** Built 2026-11-18.

**As built (2026-11-18):**
- `ListBox.fs` and `RadioButton.fs` in `Elements/Controls`, after TabControl. The renames (decisions 9, 10) were
  their own commit (e3d7c0f).
- **Item containers:** an internal ItemsView hook, `ItemsViewProps.container` (key, item, view → view), wraps
  each item's view in `realize`. ListBox sets it in its defaults to wrap in a `ListBoxItem`, so recycling, keys
  and measuring stay ItemsView's. After ItemsView's measure, ListBox walks the realized containers and sets each
  one's selected state and Tab stop. Restyling runs at once, so a change draws in the same frame.
- **The `selected` state reuses `TabHeaderProps.isSelected`,** which `States.isSelected` already reads; its doc
  now names both.
- **Keys:** without `keySelector` the key is the index (`int`), as decided; the screenshot test selects
  `[ 1; 4; 5 ]` that way.
  *Corrected 2026-11-19:* that was never so. Without `keySelector` the key is the item itself (`ItemsViewProps.keySelector`
  defaults to `id`); the screenshot test's source is the numbers 0 to n - 1, so its items are its indices. Keys must be
  unique within a source: realized items, the index and the selection are all by key, so of two items with one key only
  one is shown, found and selected. Not made to work (a key that isn't unique isn't a key); a Debug build says so once
  per key through `DebugWarnings.Once`, naming the key and the two positions (`KeyStore.FirstDuplicate`, a pass over the
  keys per new source with a set kept between calls, so it allocates nothing at steady state).
- **Double-clicks are the ListBox's own:** the input layer has no click count, so two clicks on the same item
  within 500 ms of platform time (`IElementRoot.Time`) make one. A Shift or Ctrl click never counts.
- **Tab target:** the item last focused, else the first selected item that is realized, else the first realized.
  A selected item scrolled out of view isn't the Tab target until it is realized again (keeping it realized the
  way `containsFocus` does was more than this step needed).
- **Keys that move focus** to an unrealized item scroll it into view (nearest edge, no animation) and focus it
  after the frame that realizes it. A Shift range is built from the source's keys, so Shift+End over 10,000
  items realizes only what shows (tested).
- **Semantics:** a new `Semantics.isMultiSelectable` (and an internal `multiSelectableFrom`), carried on
  `SemanticNode.IsMultiSelectable`. `Describe()` adds "multi-select" and the inspector shows a line. ListBox
  reads it from `selectionMode`. Roles `RadioButton` (named by its content, like CheckBox) and `RadioGroup`. A
  screen reader's default action on an item selects it (when the mode allows) and invokes it.
- **RadioGroup** (decision 13) syncs its radios in measure (`isChecked`, and the Tab stop on the chosen or first
  enabled radio). Radios and groups are recognised by marker properties, so types extended from them still
  count. Space and clicks choose through the radio's click; arrows are the group's `keyDown`. A RadioButton
  outside a group turns itself on and stays on.
- **Decision 16, not quite:** the dot doesn't grow in. CheckBox has no mark animation to share (its tick
  appears), so the dot appears too, while the ring's colour fades with the theme's 100 ms transition. Growing
  marks can come for both together.
- **Look:** square items (Tim, after the first screenshots: no rounded corners; decision 15 said borderless, and
  the theme had given them its corner radius). The theme's selected item is the accent at 30% (`ThemeConvert.selectionTint`) with the 3-DIP accent
  bar. The focus ring draws inside the item's edge, because the list's ScrollViewer clips outside it.
- **Gallery:** a Lists page (it fills the window). A RadioGroup picks the mode: the selection starts again on a
  change, since Single can't show several. "Reverse order" shows the selection kept by key. A status line shows
  the selection and the last invoked item. A small list of colour swatches with a view per item keeps its own
  selection.
- **The focus ring after arrow keys** (found driving the browser build with a real pointer and keyboard): the ring
  showed only after Tab, so after a click, focus moved by a ListBox's or RadioGroup's arrows had none. Like a
  browser's `:focus-visible`, any key but a modifier now puts input in keyboard mode (`Input.KeyDown`): focus a key
  moves shows the ring; the element focused at the time keeps its look; a pointer press puts the ring away. Tested.
- **Fixed along the way:** `ItemsView.realizedCount` recognised only the exact ItemsView type; it now answers
  for any element that laid out items (ListBox).
- **Tests:** 16 unit tests (ListBox 11, RadioButton 5). 3 new screenshots: `lists@1x`, `lists-light@1x` and
  `gallery-phone-lists@3x`. 8 Gallery references changed only in the tab row (checked pixel by pixel). Unit:
  1,062 passed plus 6 timing skipped. Rendering: 88. Timing: 6. Driven in Chrome (browser build): click, hover,
  double-click, arrows, End, Multiple's Ctrl/Shift+click and Ctrl+A, radio arrows, None mode, the swatch list.

### 4c: ComboBox (planned and built 2026-11-18)

Facts: its list is a flyout (GLOSSARY) and `Flyout.matchAnchorWidth` exists for it; ListBox virtualizes and was
built to be reused (4b, decision 1); 4b's decision 19 says a ComboBox selects and invokes on a click or Enter and
closes on the invoke; typeahead waits for M9 (4b, decision 4).

**Round 1 (Tim: yes to all, 2026-11-18).**
1. **Select-only now.** Typing into it waits for TextBox (M9): an editable ComboBox is a TextBox with a list.
2. **Its own element type extending Border, not Button** (`Button.text` and `onClick` would mean nothing on it). It
   takes focus and the hover and pressed states itself and builds its flyout.
3. **Settings mirror ListBox:** `ComboBox.source`, `keySelector`, `viewSelector`, passed to the list inside. It
   can't inherit them from ItemsView (it isn't the list), so it declares the same names.
4. **The selection is one key:** `ComboBox.selected (key: 'K voption)` and `ComboBox.onSelectedChanged` with
   ListBox's `SelectionChangedEventArgs<'T, 'K>`. `ValueNone` shows the placeholder. Controlled when given, else it
   keeps its own. Considered: `selected (key: 'K)` plus a second setting for "none" (two settings for one job).
5. **The closed box shows the selected item through the same `viewSelector`;** `ComboBox.placeholder "Pick one"`
   when nothing is selected. A selector of its own for the box waits for a case that needs it.
6. **Closed keyboard:** Space, Enter, Alt+Down and F4 open it; Up/Down and Home/End change the selection without
   opening (Windows, WinUI). The wheel over a closed box does nothing.
7. **Open:** focus goes to the selected item, scrolled into view; the arrows move the highlight only; Enter or a
   click commits and closes; Escape, Tab or a press elsewhere closes with no change; focus returns to the box.
   Committing on every arrow would raise `onSelectedChanged` for items only passed through. Inside, the ListBox is
   in None mode showing the current key as selected, and the invoke is the commit.
8. **Flyout:** below (flipping above), at least as wide as the box, at most 320 DIPs tall
   (`ComboBox.maxListHeight`), then it scrolls. Not "dropdown": the glossary retires the word.
9. **No `isOpen`/`onOpenChanged` on ComboBox yet:** nothing needs to open one from code; adding them breaks nothing.
10. **Look: a field, not a button.** Surface background, a 1-DIP border, Button's corner radius and height, a
    chevron at the right drawn by the element (Core has no icon package), an accent border when focused or open.
    It stretches by default.
11. **Semantics:** a new role `ComboBox`, expanded or collapsed, its value the selected item's name, Expand and
    Collapse actions; the open list is the `List` in its own layer.
12. **A selected key that leaves the source** follows ListBox (4b, decision 11).
13. **Gallery:** three on the Controls page: text items, the swatches (a view per item), 10,000 items.
14. **Tests:** unit tests for opening and closing, both keyboards, commit and cancel, controlled and not, focus
    coming back, the same elements realized for 100 and 10,000 items; screenshots closed and open in both themes
    and the phone pass at 390 DIPs.
15. **GLOSSARY:** no new term (its list is "its flyout"; the closed part is "the box" in prose).

Status: **agreed (Tim, 2026-11-18: "yes to all").** Built 2026-11-18. Passed by Tim 2026-11-18.

**As built (2026-11-18):**
- `Elements/Controls/ComboBox.fs`, after RadioButton. The element manages its own content as TabControl does: in
  measure it works out the box's view (the selected item's, through `viewSelector`, or the placeholder as dim
  text), patches or realizes it, and sets its own `flyout`: a Flyout holding a ScrollViewer (`maxHeight` from
  `maxListHeight`) and a ListBox with the same source and selectors, in None mode, showing the selected key.
  `ListBox.onItemInvoked` is the pick: it selects and closes.
- **The flyout is controlled by the ComboBox:** `ComboBoxOpenProps.isOpen` (in `Props.fs`, so `States.isOpen` can
  read it) is given to `Flyout.isOpen`, and `Flyout.onOpenChanged` sets it. So every way a flyout opens or closes
  (the box's click, Space and Enter, Escape, a press elsewhere, the anchor scrolling away) goes through one place,
  and `ComboBox.isOpen`/`onOpenChanged` (decision 9) would be two short settings later.
- **Opening on the selected item** (decision 7): on opening, the list's scroller is asked for the selected index
  (no animation) before the flyout's first layout, so the item is realized and the host's "focus the first Tab
  stop" lands on it (ListBox makes the selected item its Tab stop). Tested with item 500 of 1,000.
- **Keys:** the box's own (`Element.keyDown`) are the closed ones: Alt+↓ and F4 open; ↑/↓, Home and End pick
  through a key store kept for the source (one pass over the keys when the source changes, as ItemsView does, then
  each press is a lookup). Space and Enter are the box's click. The open list's keys are ListBox's, in the flyout's
  layer; Tab there is handled by the list's `onKeyDown` (close, nothing changed).
- **The list is as wide as the box, not "at least"** (decision 8 amended while building): an ItemsView takes all
  the width it's offered, which for a flyout is the window's, so the first picture showed a list across the
  window. The Flyout is given the box's width. Longer items wrap or clip as the item's view decides.
- **Look** (theme): `Palette.surface`, the border taking `Palette.accent` on `States.focused` and the new
  `States.isOpen`, hover and press tints, the chevron and the placeholder in `Palette.textDim`
  (`ComboBox.chevron`, `ComboBox.placeholderForeground`). The box's copy of the item isn't hit-testable, so a
  button inside a view item doesn't take the click that opens the list.
- **Semantics:** `Role.ComboBox`, named by its content (the selected item's text, or the placeholder), `expanded`
  read from the open flag (`Semantics.expandedFrom`), and the new actions `SemanticAction.Expand` and `Collapse`.
  **Not as decided:** the selected item is the node's *name*, not a separate value, because nodes have no text
  value yet; a ComboBox given a `Semantics.label` loses the item from its node. To settle with AccessKit.
- **Gallery:** a ComboBox section at the end of the Controls page: sizes (controlled, with the line under it
  following), the swatches (a view per item), 10,000 items, and a disabled one, in WrapPanels so they stack on a
  phone.
- **Tests:** 13 unit tests (`ComboBoxTests`): picking by click, closing by the box and by a press elsewhere, focus
  on the selected item of 1,000, the open keyboard, Escape and Tab, the closed keyboard, Alt+↓ and F4, controlled,
  a key leaving the source (own and controlled), the list's width and height and the same elements for 100 and
  10,000 items, a view item, semantics with Expand and Collapse, disabled. 2 new screenshots: `combobox@1x` and
  `combobox-light@1x` (one open by keyboard, a hovered one, the placeholder, a view item, a disabled one). 3
  references changed, each looked at: `inspector-pick@1x` and `inspector-images@1x` gained the "open" chip in
  STATES; `gallery-phone-controls@3x` has a shorter scrollbar thumb (the page is longer). The phone picture doesn't
  reach the ComboBox section (it's below the fold): not covered by a picture at 390 DIPs. Unit: 1,081 passed plus
  6 timing skipped. Rendering: 90. Timing: 6.

### 4d: Dialog and Toast (planned and built 2026-11-18)

Facts: a modal layer already blocks pointer and keyboard below it, has its own focus and Tab order and hides the
layers below from the semantic tree; step 4's decision 4 gives each dialog its own layer in opening order and the
toasts one fixed layer; the Gallery faked both with two fixed `App.layer`s listening to F# events, and timed its
toasts with `Task.Delay` (against "time comes from the platform").

**Round 1 (Tim: yes to all, 2026-11-18).**
1. **A `Dialog` element in the view,** not a setting on another element: `Dialog.create [ Dialog.isOpen
   model.Confirming; Dialog.onOpenChanged ...; Dialog.content view ]` sits anywhere, takes no space, and shows its
   content in its own modal layer. A flyout belongs to its anchor; a dialog has none.
2. **Always controlled:** `isOpen` and `onOpenChanged` (Flyout's names). Nothing clicks a dialog open, so an own
   open state would have no trigger.
3. **The Dialog is also the box** (as Flyout is): the theme's surface, corners, padding and a shadow, centred.
   `Dialog.title "Delete file?"` is optional: a heading, and its name for screen readers. Buttons are the app's
   content; a helper for an OK/Cancel row waits for two real dialogs.
4. **Closing:** Escape asks to close by default; a press on the scrim does not. `Dialog.lightDismiss true` adds
   the scrim press; `Dialog.closeOnEscape false` is for a dialog that must be answered.
5. **Focus:** on opening, the first Tab stop inside; Tab stays inside; on closing, back to what had it. Choosing
   another first element waits for focusing from code.
6. **The scrim** is a theme value (`Palette.scrim`); scrim and box fade in over 120 ms.
7. **Size:** at most 560 DIPs wide and never closer than 16 DIPs to the window's edges; content too tall scrolls
   if the app puts it in a ScrollViewer.
8. **Semantics:** a new role `Dialog`, named by its title.
9. **A `Toaster` the app makes and gives to the app:** `let toaster = Toaster()`, `App.toaster toaster`, then
   `toaster.Show "Saved"` from anywhere, on any thread. The Scroller pattern (a controller for an action); it
   works where there is no component (`App.onError`, `update`). Rejected: a static `Toast.show` (static state
   shared between hosts: engineering rule 6).
10. **Two forms:** `toaster.Show "text"` and `toaster.Show [ Toast.text ...; Toast.kind ToastKind.Error;
    Toast.duration ...; Toast.action ("Undo", undo) ]`. Kinds Info (default) and Error now; `Toast.content view`
    in place of the text.
11. **A toast stays 4 seconds,** waits while the pointer is over it, and has a close button; an Error stays until
    closed. Platform time, no frames while waiting.
12. **Placement is fixed:** bottom right, newest lowest; across the bottom on a window narrower than 480 DIPs. At
    most three show; the rest wait.
13. **They slide and fade** in and out over 150 ms.
14. **Toasts never take keyboard focus** and Tab doesn't reach them. Known gap: a toast's action is pointer-only
    until the keyboard has a way there (F6 on Windows). A new role `Alert` names them; announcing them waits for
    the first screen-reader bridge.
15. **Stacking:** app layers, dialogs, flyouts, toasts, the inspector. A flyout opened in a dialog is above it; an
    error toast stays visible and closable over a dialog.
16. **`App.onError` keeps its meaning** (no handler: an error ends the app). The Gallery's handler shows an Error
    toast.
17. **Gallery:** the Layers page's dialog and toasts move onto `Dialog` and the `Toaster`; the two `App.layer`
    lines and the F# events go. No `App.layer` example stays in the Gallery (the README documents it).
18. **GLOSSARY:** Dialog, Toast, Toaster, Scrim.
19. **Two commits** (Dialog, then toasts), one checking pass.

Status: **agreed (Tim, 2026-11-18: "yes to all").** Built 2026-11-18.

**As built (2026-11-18):**
- **Dialog rides on the flyout machinery.** `Dialog.create settings` makes a `DialogAnchor` (not visible, so
  panels give it neither room nor a gap) whose `flyout` is the Dialog box, built from the settings; `isOpen` and
  `onOpenChanged` are Flyout's own properties. The host sees the anchor's type and, for a dialog, makes the layer
  modal at the dialogs' order, wraps the box in a `Scrim` instead of placing it by an anchor, never closes it for
  an anchor "scrolled out of view", and reads `closeOnEscape` for Escape and `lightDismiss` for a press that lands
  on the scrim itself. Focus in and back, restyling "as if inside its element" and closing when it leaves the view
  come with the flyout code.
- **`Elements/Controls/Dialog.fs`** (Controls, not Basic: the title needs StackPanel and TextBlock): `Scrim`
  (measures and centres the box, confined to its own size, on whole DIPs), `DialogAnchor`, `Dialog`. The box has
  `maxWidth 560` and `margin 16`; `Dialog.title` becomes an 18-DIP heading stacked over the content.
- **The fade** (decision 6): the scrim's first view has opacity 0; after its first layout the host gives it
  opacity 1, and the theme's 120 ms transition on the scrim's opacity does the rest. No fade out.
- **Layer orders:** dialogs `MaxValue - 5`, flyouts `- 4` (were `- 2`), toasts `- 3`; the inspector's highlight
  and panel stay above.
- **`Toaster`** (`Hosting/Toaster.fs`; `ToastPresenter` and `ToastPanel` in `Elements/Controls/Toast.fs`, so the
  theme can style them): a list of toasts with a stage each (waiting, coming, shown, going), all on the UI thread;
  `Show` posts to the dispatcher (or queues until `ElementHost.AddToaster`/`App.toaster` binds it). Its layer's
  view is built from the list and patched on every change. A toast comes in clear and 12 DIPs low, then the next
  posted turn shows it: the theme's 150 ms transition fades it and `ToastPresenter`'s transform slides it
  (`ctx.Animate`). Going is the same backwards, then it's removed and a waiting one takes the place. Timers are
  `Dispatcher.PostAfter` with a generation number, so a hover or a close makes an old timer stale. Nothing asks
  for frames while a toast waits (tested).
- **`Layer.keyboard false`** (new): a press in such a layer doesn't bring the keyboard to it. The toasts' layer
  uses it, and the toast's buttons aren't focusable, so a click on Close leaves focus and Tab where they were.
- **`ToastPanel`** passes the pointer through except on its toasts, stacks them from the bottom up, 16 DIPs from
  the edges and 8 apart; at most 360 wide, or the window's width less the edges under 480 DIPs.
- **The toast's buttons** (the action, then Close) sit in one StackPanel docked `Right`: two children docked Right
  stand in the opposite order to the one they're written in (the same lesson as Bottom strips, in "Tab order is
  view order").
- **Semantics:** `Role.Dialog` named by the title; `Role.Alert` named by the toast's text through a label (named
  by content, the Close button's name joined it: "Offline Close"). A toast given only `Toast.content` has no name:
  give it `Toast.text` too.
- **Theme:** `Palette.scrim` (`Theme.scrim`; 55% black, 30% in the light theme), `Scrim`, `Dialog` (surface, line,
  corners, 20 DIPs of padding, a deep shadow) and `ToastPresenter` (surface, line, floating shadow; `error` class:
  the danger colour with `onAccent` text).
- **Gallery:** the Layers page has a Toasts section (a toast, an error, one with Undo) and a Dialog section (one
  that Escape or a click outside closes and that can show a toast above itself, one that must be answered, with
  the answer shown on the page). `Common.toaster` is the Gallery's; `App.onError` and the Images page show Error
  toasts through it. The two `App.layer` lines, the F# events, `Task.Delay` and the Gallery's own scrim and dialog
  shadow values are gone.
- **Found along the way:** the rendering tests' Images page came out with empty boxes in 2 runs of 5 once the run
  had more classes: the classes that use the process's image cache ran in parallel, and one could find a picture
  another's thread had only begun to decode. `ImageViewTests`, `ImageOutTests` and `InspectorTests` now share an
  xunit collection (one after another); six runs clean.
- **Tests:** 22 unit tests (Dialog 9: layer and scrim, size, blocking, focus, Escape, the scrim press, a flyout
  inside, following the view, semantics; toasts 13: showing and going, no frames while waiting, before start,
  another thread, errors, durations, three at a time, hover, the action, the keyboard staying, narrow and wide,
  pass-through, stacking and semantics). 2 new screenshots: `dialog-toasts@1x` and `dialog-toasts-light@1x`.
  `gallery-phone-layers@3x` changed (the page's new sections; looked at). Unit: 1,103 passed plus 6 timing
  skipped. Rendering: 92. Timing: 6.
- **Not done:** no fade when a dialog closes; no keyboard way to a toast's action (decision 14's gap); toasts
  aren't announced (waits for the first bridge).


**Round 2: message dialogs as actions (Tim, 2026-11-18).** Tim asked whether dialogs should work like toasts and
not be part of the view. Settled: `Dialog.create` stays for content that follows the model (a form, a picker:
shown once by a call, its view would be a snapshot; in the view it re-renders, closes with what owns it, and comes
back after a hot reload); questions with fixed content become actions that return the answer.
20. **A `Dialogs` controller**, the Toaster's shape: `let dialogs = Dialogs()`, `App.dialogs dialogs`, callable from
    a handler, `update`, `App.onError` or another thread. No `dialogs.Show(view)`: that case is `Dialog.create`'s.
21. **Three calls, each returning what the caller wants:** `dialogs.Message` (`Task<unit>`; a text, or
    `Message.title`, `text`, `button`), `dialogs.Confirm` (`Task<bool>`; `Confirm.title`, `text`, `accept`,
    `cancel`, `severity`) and `dialogs.Ask` (`Task<'T voption>`; `Ask.title`, `text`, and `Ask.option (value,
    label)` with the app's own values).
22. **`Confirm.severity`** (Tim, in place of `Confirm.danger true`): `Severity.Informational` (default),
    `Warning` or `Critical`.
23. **Behaviour:** Escape closes a Message, is cancel for a Confirm (`false`) and no choice for an Ask
    (`ValueNone`); a press on the scrim does nothing; two asked at once queue; toasts show above; a call from
    another thread is posted to the UI thread, where the task completes; the app closing cancels what's open or
    waiting. They are the same `Dialog` box and scrim, so `Dialog.style` and the theme apply.
24. **GLOSSARY:** no new term ("message dialogs" in prose; `Dialogs` is the controller).

Status: **agreed (Tim, 2026-11-18: "yes to all but for Confirm.danger let's make it Confirm.severity with Critical,
Warning and Informational").** Built 2026-11-18.

**Round 2 as built (2026-11-18):**
- **`Hosting/Dialogs.fs`:** `Severity`, the three settings types (`Message`, `Confirm`, `Ask`, each a struct
  setting over an options record), and `Dialogs`. Each call makes a `TaskCompletionSource` and a request whose view
  is a `Dialog.create` (title, text, a WrapPanel of buttons at the right) with `isOpen true`; Escape arrives as
  `onOpenChanged false` and answers as decided. The host gives a `Dialogs` a layer of its own ("Dialogs", which
  shows nothing and never takes the keyboard) for that Dialog to stand in; the dialog itself opens in a modal
  layer like any other. After an answer the view goes back to nothing, and the next request shows on the next
  posted turn, so it opens afresh (its own focus, its own fade).
- **Severity:** the accept button takes `Palette.accent`, the new `Palette.warning` (`Theme.warning`; amber, with
  `onAccent` text) or `Palette.danger`. Focus starts on accept, or on cancel for Critical.
- **`Dialog.initialFocus true`** (new, attached, public): on something focusable inside a dialog, it takes focus on
  opening in place of the first Tab stop. Confirm needed it (cancel is written first, so it would always have had
  focus), and it settles decision 5's "waits for focusing from code" for dialogs.
- **The focus ring in a dialog** now follows the layer that had the keyboard when it opened, not the anchor's
  layer: a message dialog's anchor stands in the Dialogs layer, which never has the keyboard, so a dialog opened
  with Enter showed no ring.
- **One controller, several hosts** (found by a test that failed once in four runs): the Gallery has one `Toaster`
  and one `Dialogs` at module level, and every test host that started the Gallery took them over from the last, so
  a toast could be posted to another test's UI thread. Both controllers now keep a stack or queue per host; a call
  from a host's UI thread goes to that host, a call from any other thread to the host given last, and a closing
  host lets go (`Unbind`). Right for an app with two windows too. 14 unit runs clean afterwards.
- **`Message` as a type name** can meet an app's own `Message` type (two of our samples have one, in other
  modules): such an app writes `Scribble.Core.Message.title`. Kept as agreed; `Notice` would avoid it.
- **Gallery:** the Dialog section has "Open a dialog" (the view form) and five actions: Confirm, Warning, Critical,
  Three choices, Message; a line under them shows what came back.
- **Tests:** 9 unit tests (`DialogsTests`: each call's answers and Escape, severity colours and focus, the scrim,
  queueing, before start and another thread, the keyboard back in the app, cancelling on close) and one for two
  hosts sharing a Toaster. 2 new screenshots: `confirm-critical@1x` and `confirm-warning@1x`.
  `gallery-phone-layers@3x` changed again (the section's buttons). Unit: 1,113 passed plus 6 timing skipped.
  Rendering: 94. Timing: 6.

**Round 3: toasts and message dialogs belong to the window (Tim, 2026-11-18).** Tim asked how a `Toaster` or
`Dialogs` declared for the app knows which window to show in, and whether they should be hooks. They didn't know:
routing by thread (round 2 as built) tells tests apart, each on its own thread, but an app's windows share one UI
thread, so every call would have gone to the window set up last.
25. **Every host owns its toasts and message dialogs.** No `Toaster()` or `Dialogs()` made by the app, no
    `App.toaster`/`App.dialogs`; their constructors aren't public, so there can't be one that belongs to no window.
26. **Hooks in components:** `ctx.useToaster ()` and `ctx.useDialogs ()` give the ones of the window the component
    is in.
27. **Where there's no component:** commands for an Elmish `update` (`Cmd.toast`, `Cmd.inform`, `Cmd.confirm`,
    `Cmd.ask`, run by the program's host); `App.onError (fun app e -> ...)` gets the app's context
    (`app.Toaster`, `app.Dialogs`); `AppContext` for `App.onStart` and packages; `host.Toaster`/`host.Dialogs` for
    custom hosts and tests. From another thread: take the toaster on the UI thread, call `Show` later.
28. **`Message` becomes `Inform`** (Tim): `dialogs.Inform`, `Inform.title`/`text`/`button`. It also ends the clash
    with apps' own `Message` types.
29. **Shortcut hooks** (Tim): `ctx.useInform ()`, `ctx.useConfirm ()` and `ctx.useAsk ()` give the call itself:
    `let confirm = ctx.useConfirm ()`, then `let! ok = confirm [ Confirm.title "Delete?" ]`.
What goes: the per-thread routing and "the host given last"; toasts and dialogs asked for before the app starts
(nothing holds them); the Gallery's module-level `toaster` and `dialogs`.

Status: **agreed (Tim, 2026-11-18: "yes to all. Change Message to Inform. Can we create a shortcut hook for Inform,
Confirm and Ask?").** Built 2026-11-18.

**Round 3 as built (2026-11-18):**
- **The host owns them.** `ElementHost.Toaster` and `ElementHost.Dialogs`; the state behind each (`ToastStack`,
  `DialogQueue`) and its layer are made the first time something shows, so a window that never shows a toast has
  no Toasts layer. `Toaster` and `Dialogs` have internal constructors taking how to find the UI dispatcher and
  the state; `Show` and the three calls post to the dispatcher first and look the state up there, so the layer is
  only ever made on the UI thread. `AddToaster`, `AddDialogs`, `App.toaster`, `App.dialogs`, the per-thread
  routing, `Bind`/`Unbind` and "asked for before the app starts" are gone.
- **How a call finds its window:** the host puts one `HostServices` on every layer's tree (`ElementTree.Services`)
  and on its app values (`AppValues.Services`), both untyped fields because those types come earlier in the
  compile order. A hook looks at the tree its component is in (the app's, a flyout's or a dialog's: all the same
  window's); a command looks at the values its program runs with, as `Cmd.scrollTo` does.
- **Hooks** (`Hosting/Component.fs`; `Toaster.fs` and `Dialogs.fs` moved before it in the compile order):
  `ctx.useToaster ()` and `ctx.useDialogs ()` (one object per component, kept), and the shortcuts
  `ctx.useInform ()`, `ctx.useConfirm ()` and `ctx.useAsk<'T> ()`, which give the call as a function
  (`useAsk` is for one type of choice per hook).
- **Commands** (`DialogCommands`, a second `Cmd` module, opened automatically): `Cmd.toast text`,
  `Cmd.toastWith [ ... ]`, `Cmd.inform [ ... ] msg`, `Cmd.confirm [ ... ] ofAnswer`, `Cmd.ask [ ... ] ofChoice`.
  The message is sent on the thread that answers (the UI thread), at once; a cancelled dialog sends nothing.
- **`App.onError` takes `AppContext -> exn -> unit`** (17 callers changed, 16 of them tests). `AppContext` has
  `Toaster` and `Dialogs`. An error before the host exists has no context and ends the app, as with no handler.
- **Outside any window** (a test's bare tree, a view rendered to an image): a toast shows nothing and a message
  dialog's task is cancelled, each with a Debug warning once. The Gallery's pages are drawn that way by many
  tests, so the hooks must not throw there.
- **`Message` is `Inform`** everywhere (`dialogs.Inform`, `Inform.title`/`text`/`button`, `Cmd.inform`).
- **Gallery:** no module-level `toaster` or `dialogs`. The Layers page uses `useToaster`, `useConfirm`,
  `useAsk<string>` and `useInform`; the Images page `useToaster`; `App.onError (fun app e -> app.Toaster.Show ...)`.
- **What multi-window still needs:** today one app is one window plus the inspector's pop-out, and app values are
  per app. If an app's windows come to share their values, commands will need a per-window way in (the hooks
  already have one, through the tree). Noted here, not built.
- **Tests:** unit tests now use `host.Toaster` and `host.Dialogs`. New: each host has its own toasts and its own
  dialogs; `useToaster` (the same object every run; the host's own); the shortcut hooks giving back answers;
  `useDialogs` from inside a Dialog's content reaching the same window; a bare tree (nothing shows, the task is
  cancelled); `Cmd.toast`/`toastWith`; `Cmd.confirm`/`ask`/`inform`; `App.onError` showing a toast through its
  context. Removed: "before the app starts" and the two-hosts-one-toaster test. `gallery-phone-layers@3x` changed
  (the "Inform" button, the section's note). Unit: 1,121 passed plus 6 timing skipped (four runs). Rendering: 94.
  Timing: 6.

**Round 4: the shortcut hooks take the settings and give back a function (Tim, 2026-11-18).** Tim: "I'd love to
get rid of the 'asked' function and maybe define the settings (like Confirm.Title) as part of the shortcut". The
Gallery needed a helper to run each task and put its answer somewhere, because the shortcuts handed back tasks.
30. **Handlers are settings, and the hook returns a function that opens the dialog:**
    `let confirmDelete = ctx.useConfirm [ Confirm.title "Delete the draft?"; Confirm.onAccept (fun () -> ...) ]`,
    then `Button.onClick (fun _ -> confirmDelete ())`. No task, no helper; the dialog is declared at the top of
    the component, and its settings are built on every run, so they can follow the model.
31. **`Confirm.onAccept` and `Confirm.onCancel`** (not one `onAnswer`): most confirmations care only about accept;
    Escape counts as cancel.
32. **`Ask.choice (label, handler)`** and **`Ask.onDismiss`** (Escape): each choice carries what it does, so
    `useAsk` needs no type argument and nothing to match on. `Ask.option (value, label)` stays for the task form.
33. **`Inform.onClosed`**, optional.
34. **A form with an argument** for all three, for when the dialog depends on what was clicked (hooks can't sit in
    a loop): `ctx.useConfirm (fun (file: File) -> [ ... ])` gives a function taking the file.
35. **The task-returning shortcuts of round 3 are replaced.** `ctx.useDialogs ()` keeps the task forms
    (`dialogs.Confirm`, `Ask`, `Inform`) for a sequence of questions and for code outside a component; a handler
    given to a task form runs too, just before the task completes.

Status: **agreed (Tim, 2026-11-18: "yes to all").** Built 2026-11-18.

**Round 4 as built (2026-11-18):**
- **Settings:** `Confirm.onAccept`/`onCancel`, `Inform.onClosed`, `Ask.onDismiss`, and `Ask.choice (label,
  handler)`, which is an `AskSetting<unit -> unit>`: the option's value is its handler, and an internal `OnChoice`
  runs the one picked. So the hook form of Ask is the task form at one type, with nothing new underneath.
- **One path for every answer** (`Dialogs.Show`'s `answer`): the dialog closes, the handler runs, and the task
  completes in a `finally`, so a handler that throws still leaves the dialog closed and the task answered. The
  error goes where the click's or the key's would (the element's `ReportError`, then `App.onError`): handlers run
  inside the button's click or the Dialog's Escape, which are already guarded. Tested.
- **Hooks** (`Component.fs`): `useConfirm`, `useAsk` and `useInform`, each in two forms: a settings list giving
  `unit -> unit`, and a function from a value to the settings giving a function of that value. Each is
  `useDialogs` plus a closure over this run's settings, so the function a handler holds is always the run's that
  made it (tested: the text follows a counter). The task-returning shortcuts of round 3 are gone.
- **Gallery:** the Layers page declares five message dialogs at the top of its component (two confirmations, one
  with an argument, a choice, an inform) and its buttons call them; the `asked` helper and every `task { }` on
  the page are gone.
- **Tests:** handlers on the task forms and their order against the task; a handler that throws; the three hooks;
  the form with an argument; settings following state; a bare tree. Unit: 1,125 passed plus 6 timing skipped
  (three runs). Rendering: 94. Timing: 6. `gallery-phone-layers@3x` changed (the section's note).

**Round 5: Modal and Dialog (Tim, 2026-11-18).** Tim: "should we change the Dialogs that are part of the view into
Modal and leave the term Dialog for Modals requiring input?" `Dialog` (the element) and `Dialogs` (the controller)
differed by a letter and were two mechanisms.
36. **The element in the view is a `Modal`:** content written in the view, shown over a scrim in its own layer,
    taking all input until it closes; the view says whether it's open. `Modal.create`, `Modal.isOpen`,
    `onOpenChanged`, `title`, `lightDismiss`, `closeOnEscape`, `initialFocus`, `Modal.style`. The word says what it
    does, as `Layer.modal true` already did.
37. **A Dialog is a modal with fixed content that tells or asks and gives back an answer:** Inform, Confirm, Ask,
    opened as an action. Defined by its fixed content and its answer, not by "requiring input" (a Modal holding a
    form requires input too; an Inform only wants acknowledging). `Dialogs`, `ctx.useDialogs`, the hooks, the
    commands and the settings types keep their names; "message dialogs" in the docs become "dialogs".
38. **The semantic role stays `Role.Dialog` for both:** role names are the ones screen readers know (ARIA,
    UI Automation).
39. **A dialog's box is a Modal element,** so `Modal.style` restyles dialogs too; the box has the class `dialog`,
    so `Modal.style [ Style.cls "dialog" ] [ ... ]` styles them apart.
40. **Now, before rounds 2 to 4 are committed:** nothing is published, and only the first Dialog commit has the
    old name.
Decisions 1 to 8 above say "Dialog" for what is now the Modal; they are left as written.

Status: **agreed (Tim, 2026-11-18: "yes to all").** Built 2026-11-18.

**Round 5 as built (2026-11-18):**
- **Renamed:** `Elements/Controls/Dialog.fs` is `Modal.fs` (`ModalProps`, `Scrim`, `ModalAnchor`, `Modal`); the
  element type's name is "Modal" (what the inspector shows); the host's layer for one is named "Modal", and its
  order `modalOrder`; `ThemeConvert.modalShadow`; `tests/Scribble.Tests/DialogTests.fs` is `ModalTests.fs`.
- **Kept:** `Role.Dialog`; `Dialogs`, `DialogQueue`, `DialogRequest`, `DialogCommands`, `ctx.useDialogs`, the
  hooks, the commands; the "Dialogs" layer a host's dialogs stand in; `DialogsTests.fs`, `DialogToastTests.fs`.
- **A dialog's box** is `Modal.create [ ...; Modal.classes [ "dialog" ] ]`. Tested.
- **Words:** GLOSSARY has *Modal* (new) and *Dialog* (redefined; "message dialog" is on its avoid list); *Scrim*
  says "a modal". Code comments, the README and the Gallery say "modal" for the element and "dialogs" for
  Inform, Confirm and Ask. Decisions 1 to 8 and the round 1 to 4 notes above are left as written.
- **Gallery:** the section is "Modal and dialogs"; its first button is "Open a modal".
- **Tests:** unit 1,126 passed plus 6 timing skipped (two runs). Rendering: 94. Timing: 6.
  `gallery-phone-layers@3x` changed (the section's heading and button).

**Round 6: `ctx.useDialog`, a dialog with content of your own (Tim, 2026-11-18).** Tim: "let's add ctx.useDialog
that accepts a custom view". As a hook it needn't be the snapshot `dialogs.Show(view)` would have been (round 2):
a hook runs on every render.
41. **`ctx.useDialog (fun close -> [ Dialog.title "..."; Dialog.content view ])`** gives a function that opens it;
    `close ()` closes it. The hook owns whether it's open: no flag in the app's state, nothing to place in the view.
42. **Live content:** while it's open, each run of the component updates it, so it follows state as a Modal in the
    view does.
43. **Its own settings, `Dialog.*`, not the Modal's** (Tim: "Otherwise, we'd see things like Modal.isOpen"):
    `Dialog.title`, `Dialog.content`, `Dialog.closeOnEscape` (default true), `Dialog.lightDismiss` (default
    false), `Dialog.onClosed` (however it closes: `close ()`, Escape, a press outside), and
    `Dialog.initialFocus true` on something inside the content (the same as `Modal.initialFocus`, under a name
    that keeps `Modal` out of dialog code).
44. **No result type:** `close` takes nothing; the content's handlers do the work first.
45. **A form with an argument,** as the other hooks: `ctx.useDialog (fun (file: File) close -> [ ... ])`.
46. **Through the window's dialog queue:** one at a time with Inform, Confirm and Ask; its box has the `dialog`
    class.
47. **`Dialog.style parts settings`** (Tim): `Modal.style` for boxes with the `dialog` class, so styling dialogs
    needn't say `Modal.style [ Style.cls "dialog" ]`.
48. **GLOSSARY: Dialog widens** to "a modal opened as an action: inform, confirm, ask, or content of your own".
    The line between Modal and Dialog is how it's opened.

Status: **agreed (Tim, 2026-11-18: "yes to all, and add Dialog.style").** Built 2026-11-18.

**Round 6 as built (2026-11-18):**
- **`Dialog`** (`Hosting/Dialogs.fs`): a settings type like `Inform`'s (`DialogSetting` over `DialogOptions`) with
  `title`, `content`, `closeOnEscape`, `lightDismiss`, `onClosed`; plus two members of another kind:
  `Dialog.initialFocus` (an element setting, the same property as `Modal.initialFocus`) and `Dialog.style parts
  settings` (`Modal.style` with the `dialog` class first; its settings are Modal's, since the box is one).
- **`LiveDialog`**, kept by the hook's slot: `Open` queues a request in the window's dialogs whose view is built
  from the settings function when shown; `Close` answers it and calls `onClosed`; `Refresh`, called on every run of
  the component, asks the queue (`DialogQueue.Refresh`, posted to the UI thread) to show the view again if it's
  the one showing. The view is a `Modal.create` patched in place, so what's inside keeps its elements, focus and
  state (tested). The slot is disposable: a component that goes away closes its dialog without calling `onClosed`.
- **The hook** (`Component.fs`): `useDialog (fun close -> [ ... ])` giving `unit -> unit`, and
  `useDialog (fun arg close -> [ ... ])` giving a function of the argument (kept on the dialog while it's open,
  so later runs build the content for the same argument). F# tells the two apart by the lambda's shape.
- **A bug this found in Modal:** with a `title`, the content was given twice (the app's `content` setting was left
  in the list beside the stack that also holds it), and the differ made what was inside again on every update:
  focus and state were lost each time a titled Modal's view changed. The app's `content` setting is now taken
  out when the stack is made. Tested on a Modal written in the view.
- **Gallery:** a "Custom" button on the Layers page opens a `useDialog` whose text counts taps kept in the page's
  state.
- **Tests:** opening and closing with content; `initialFocus`; the `dialog` class; content following state with
  focus kept; Escape, `closeOnEscape false`, `lightDismiss`; the argument form; waiting its turn; closing when the
  component leaves; `Dialog.style` reaching dialogs and not a Modal in the view. Unit: 1,134 passed plus 6 timing
  skipped (three runs). Rendering: 94. Timing: 6. `gallery-phone-layers@3x` changed (the new button).

**Round 7: toasts keep their shape; `Severity` and `Toast.style` (Tim, 2026-11-18).** Tim asked whether toasts
should change like the dialogs did. They keep `ctx.useToaster ()` and `Show`: a toast is usually one line whose
text is only known when it happens, so declaring it with a hook would add ceremony to the common case.
49. **`Toast.severity Severity.Critical` replaces `Toast.kind ToastKind.Error`,** and `ToastKind` goes: one
    vocabulary for toasts and confirmations. Toasts gain Warning (the theme's warning colour).
50. **How long each stays:** Informational 4 seconds, Warning 8, Critical until closed (`Toast.duration` overrides).
51. **`Toast.style parts settings`,** to match `Dialog.style`: it is `ToastPresenter.style`. A Warning toast has the
    class `warning`, a Critical one `critical` (was `error`).
Left alone: `Toast.action`, `Cmd.toast`/`toastWith`, `app.Toaster`; no `useToast` shortcut (it would only save
typing `.Show`, and a returned function can't offer both the text and the settings form); no `Toast.onClosed`
until something needs it.

Status: **agreed (Tim, 2026-11-18: "yes to both").** Built 2026-11-18.

**Round 7 as built (2026-11-18):** `Severity` moved to `Hosting/Toaster.fs` (compiled before `Dialogs.fs`), its
doc comment covering both uses. The theme styles `ToastPresenter` with the `warning` and `critical` classes; the
toast's own buttons take `onAccent` text on both. The Gallery's Toasts section has "Show a warning". Tests: a
Warning toast's class, colour and eight seconds, and `Toast.style` reaching toasts by class. Unit: 1,135 passed
plus 6 timing skipped (two runs). Rendering: 94. Timing: 6. `gallery-phone-layers@3x` changed (the new button).

**Styling them without the element's name (Tim, 2026-11-18).** `Toast.style` and `Dialog.style` take the settings of
the box underneath (`ToastPresenter`, `Modal`), but nothing has to say so: a box's look is Border's settings, and
those work on anything derived from Border, so the docs and tests write
`Toast.style [] [ Border.cornerRadius 12.0 ]` and `Dialog.style [] [ Border.padding 24.0 ]`. Considered:
`Toast.cornerRadius`/`Dialog.cornerRadius` (making `Toast` and `Dialog` element settings types too would set
`Dialog.title` and `Dialog.content` against the Modal's settings of the same names). No code changed.

**A toast's close icon sat above its text (Tim, 2026-11-18: "the same issues our button used to have").** Two causes,
measured in the screenshot test's host. (1) The icon was centred in its button, the text's letters lower in their
line: the optical nudge Button gives an icon beside its *own* text (M7b) now also applies to an icon-only button
that stands beside text, through an internal `ButtonProps.iconBesideText` (the toast's close button sets it;
public when an app needs it). (2) The button was 24 DIPs tall against a 17-DIP line, so their middles were half a
DIP apart whatever the nudge: the close button's padding is now `(5, 5.5)`, 25 DIPs like the action button, which
also makes toasts with and without an action the same height. After both, the icon's centre and the text's
optical middle are the same number (323.14 in the test). Tested (`ToastTests`); `dialog-toasts@1x` and `-light`
regenerated (the × one DIP lower, the toasts one DIP taller). Unit: 1,136.

**Many toasts (Tim, 2026-11-18: "should only 3 toasts be allowed even if they are all errors?").** The cap of three
stays, for errors too: Critical toasts stay until closed, so without it a failure that repeats would stack up the
window. What the cap hid is fixed instead (Tim: all four):
52. **Repeats are one toast with a count.** A toast with the same text and severity as one showing or waiting
    adds to it ("Couldn't reach the server ×5") and starts its time over. Not when either has an action or a view
    of its own (a merge would lose them), nor when the text is empty.
53. **What waits is said.** While toasts wait, a line above the stack reads "+N more" with a "Close all" button,
    which drops what waits and closes what shows.
54. **Critical first.** A waiting Critical toast gets the next place before waiting ones that aren't.
55. **At most 50 wait.** Past that the oldest go, those that aren't Critical first.
As built: `ToastStack` (`Hosting/Toaster.fs`). Each toast has a count and the order it got its place in (they
stand in that order, newest lowest, now that places aren't given strictly oldest first). The "+N more" line is a
`ToastPresenter` with the class `more`, first among the panel's children (so topmost); N counts repeats too.
Tests: a repeat counted and its time starting over, what doesn't merge, forty errors as one toast, the line and
Close all, Critical first, the limit of fifty keeping an early error. New screenshot `toasts-many@1x`. The Gallery
has "Many at once". Unit: 1,141 passed plus 6 timing skipped (three runs). Rendering: 95. Timing: 6.

**Two changes after Tim's look (2026-11-18).** (1) A toast now goes *upwards* as it leaves: it comes up from 12
DIPs below its place and carries on 12 DIPs above it while it fades (it went back down before). (2) The "+N more"
line is at the *bottom* of the stack, under the newest toast (decision 53 said above). Tests: the offset's sign on
leaving; the line under the toasts. `toasts-many@1x` regenerated. Also: `VirtualizationTests`' trace collector
kept its lines in an unguarded list that other tests' threads write to (trace listeners are the process's); it
failed once here and now locks. Unit: 1,142.

**Where the toasts stack (Tim, 2026-11-18: "default to bottom right but allow us to set .horizontalAlignment and
.verticalAlignment"; all five, "and make it an app value").**
56. **`App.toasts [ Toasts.horizontalAlignment ...; Toasts.verticalAlignment ... ]`** gives the starting place
    (`ElementHost.toasts` for a custom host). Not a hook or something on the Toaster: it's one choice per window,
    and two components could disagree.
57. **It is an app value, `Toasts.placement`** (a `ToastPlacement` record), so an app can change it while running:
    `ctx.useAppValue Toasts.placement`, `Cmd.setAppValue Toasts.placement p`. Toasts showing move at once.
58. **Horizontal: Left, Center, Right (default) or Stretch; vertical: Top or Bottom (default).** Vertical Center and
    Stretch count as Bottom, with a Debug warning: toasts in the middle of the window would cover what they're about.
59. **At the top everything mirrors:** the newest toast is topmost, the "+N more" line above it at the edge, and
    toasts come down from above and leave downwards. One rule for both edges: new things arrive at the edge, the
    waiting line stands at the edge, and a toast leaves the way it was going.
60. **A narrow window** (under 480 DIPs) shows them across its width whatever the horizontal alignment says; the
    vertical one still applies.
As built: `ToastPlacement` and `ToastPanel`'s props beside the panel (`Elements/Controls/Toast.fs`); `Toasts`
(the settings and the app value, made lazily like `Theme.current`) in `Hosting/Toaster.fs`. The panel's children
are now in the order they stand in, top to bottom, and `ToastStack` builds that order for the edge. The host
subscribes to the value once its toasts exist and shows the stack again when it changes. The Gallery's Toasts
section has a ComboBox of eight places. Tests: each place, the mirror at the top (order, the line, the exit's
direction), the value changing while a toast shows (the same element, moved), a narrow window. Unit: 1,146 passed
plus 6 timing skipped. Rendering: 95. `gallery-phone-layers@3x` changed (the ComboBox).

**Several windows per app: where toasts and dialogs stand (noted 2026-11-18, nothing built).** An app is one
window today (`App.run`; the inspector's pop-out is the only second host). What is ready and what isn't:
- **Ready:** each host owns its `Toaster` and `Dialogs` (made on first use; neither can be constructed by an app).
  The hooks (`useToaster`, `useConfirm`, `useAsk`, `useInform`, `useDialog`, `useDialogs`) find them through the
  tree their component is in, so a component shows its toast or dialog in its own window, also from inside that
  window's flyouts and modals. A `Modal` shows in the window whose view holds it. `host.Toaster` and
  `host.Dialogs` serve code that holds a host.
- **Gap 1, commands:** `Cmd.toast`, `toastWith`, `confirm`, `ask` and `inform` find their window through the app
  values their program runs with (`AppValues.Services`), as `Cmd.scrollTo` finds named scrollers. If an app's
  windows share one set of app values (wanted for the theme), a command can't tell which window it came from.
  Commands need a per-window way in.
- **Gap 2, placement:** `Toasts.placement` is an app value, so with shared values every window's toasts move
  together. That may be right (one preference for the app) or not (a tool window wanting its own); to decide
  with the multi-window design. Not flagged when the app value was recommended.
- **Gap 3, `App.onError`:** its handler gets the app's context, and `app.Toaster` is the app's one window. An
  error from another window needs to say which window it came from.
- **Undecided:** a dialog that blocks every window ("unsaved changes" on quitting). Dialogs and modals are modal
  to their own window only.

**`ctx.useDialogs` kept public, with an example (Tim, 2026-11-18).** After round 4 nothing in the Gallery used it:
the shortcut hooks each declare one dialog. It stays for what they can't do neatly, a sequence of questions where
the next depends on the last answer (and for keeping the object to call from a task). The Layers page has "In
sequence": `dialogs.Ask`, then `dialogs.Confirm` only when the answer was Discard. Tested (`DialogsTests`).
Unit: 1,147.

**4d passed by Tim, 2026-11-18** ("this step seems to be a pass"), after rounds 1 to 7 and the changes that
followed his looks at the Gallery.
## Tab order is view order; `Dock.Fill` (grilling and built 2026-11-18)

Found driving the Gallery's Lists page with a real keyboard: Tab went from the Reverse order button to the swatches
at the bottom of the page, and only then to the long list above them. The page was a DockPanel, whose filling child
had to be written last, so the swatches (docked `Bottom`) came before the list in the view.

1. **The rule stays: Tab order is view order**, the order elements are written in the view (a parent before its
   children, children as listed), wrapping, per layer; never where they end up on screen. Position order is
   ambiguous once things overlap, wrap, span Grid cells or flip for right-to-left, and it would change when a
   window resizes and a WrapPanel reflows. HTML, WPF and Avalonia do the same. The view's author owns the order.
2. **Words:** *view order*, *Tab stop* and *Tab order* are in the glossary. "Tree order" is retired everywhere
   (README, this file, `Input.fs`, `Layoutable.fs`, `RadioButton.fs`, tests): an app author writes views and never
   sees a tree, and there are two trees now (elements, semantics).
3. **`DockPanel.dock Dock.Fill`:** a child that says it fills is laid out after every docked child, wherever it is
   written, and gets what's left. So the shell is written as it shows: toolbar (`Top`), content (`Fill`), status
   bar (`Bottom`). Rejected: leaving DockPanel alone and sending authors to Grid (it documents around the cause);
   letting a panel hand the focus manager its own order (it contradicts 1).
4. **`lastChildFill` is kept** with its default. Three cases: a child says `Fill` and it fills; none does and the
   last child fills, as before; `lastChildFill false` and no `Fill` child, nothing fills. Once any child says
   `Fill`, the last child docks to its side like the rest (otherwise the status bar above would fill too).
5. **Several `Fill` children** each get the whole rectangle left, one over the other in view order, like two
   children in one Grid cell; no warning. Useful for an empty-state message or a veil over the content.
6. **A tab-index setting is not planned** (it was "later" in M5 step 2). It would fix Tab and leave a screen
   reader reading in the old order; the answer to a wrong Tab order is to write the view in the right order. We
   look again only when a real layout can't be written that way.
7. **Reading order is view order too:** the semantic tree's nodes are in view order, so Tab and a screen reader
   can't disagree. It already was; a test pins it before AccessKit arrives.
8. **No warning when Tab order runs against position:** a check by position misfires on sidebars docked right,
   multi-column pages and right-to-left layouts. An Inspector overlay that numbers the Tab stops shows the order
   without judging it: noted in "Deferred work", not needed now.
9. **Directional focus** (gamepads, remotes; the first plan's focus model) goes by position: a nearest-neighbour
   search, not a sequence. It gets its own term when it is built.

**As built.**

- `Dock.Fill` (`Elements/Panels/DockPanel.fs`): `split` gives the visible children that dock, in view order, and
  those that fill; measure and arrange take the docked ones first. `spacing` puts its gap after each docked child
  that has something laid out after it, so a `Fill` child written in the middle gets the gaps it would get written
  last (tested: the two forms give the same rectangles).
- **Bottom strips stack upwards from the edge**, so two of them (a line over a status bar) are in view order only
  inside a panel of their own. The Gallery's shell puts both in one StackPanel docked `Bottom`.
- **Samples:** the Lists page (top, the list as `Fill`, the swatches `Bottom`); the Layout page's shell (toolbar,
  the middle as a `Fill` DockPanel of sidebar and content, then the status strip); the Bench's row (text as `Fill`,
  then the button `Right`). The other DockPanels were already in view order.
- **Tests:** 6 new unit tests: Fill written in the middle, the same rectangles as written last (with spacing),
  several Fill children, natural size (`PanelTests`); Tab order with a Fill child and against position
  (`KeyboardTests`); the semantic tree in view order, with DockPanel and `zIndex` (`SemanticsTests`). Unit: 1,068
  passed plus 6 timing skipped. Rendering: 88, every reference unchanged, so the rewritten pages draw the same
  pixels. Driven in the desktop Gallery: Tab from the radio group reaches Reverse order, the list, then the
  swatches.

## M9: TextBox (planned 2026-11-18)

Facts at the start. `TextLayout` holds each line's text and width and nothing that maps a character index to an x
position or a point to an index; shaping does give each glyph's cluster (the character it belongs to). Both
platforms send `TextInput` and Core drops it (`onTextInput` was deferred to here). The platform contract has no
clipboard (SDL has one; the browser's is asynchronous and asks permission). No composition: SDL's text input is on
for every window, with no editing events and no candidate-window position. A phone's browser shows its keyboard only
for a focused DOM input, and there is none. Parked for M9: typeahead in ListBox and ComboBox, a text value on
semantic nodes, "labelled by another element", and two stand-in fields (the Icons search, the Layout page's form).

**Round 1 (Tim: yes to all, 2026-11-18).**
1. **Five steps, each with its own round and Tim's pass:** **9a** a single-line TextBox (caret, selection by
   mouse and keyboard, editing keys, sideways scrolling, placeholder, controlled or own value; the stand-ins
   replaced); **9b** clipboard and undo/redo; **9c** multi-line (wrapping, Enter, up/down and page keys,
   scrolling); **9d** IME composition on the desktop and in the browser, and the on-screen keyboard on a phone;
   **9e** what waited for M9 (typeahead, the semantic role and text value, labels).
2. **One element, `TextBox`,** for single and multi-line (`TextBox.multiline true`); no TextArea. The editing is
   the same; layout and a few keys differ.
3. **Plain text only:** no rich text, per-range styling, spell check or input masks. The caret is left-to-right
   only; bidi stays in "later".
4. **The caret moves by grapheme cluster** (what a reader sees as one character), so an emoji or an accented
   letter is never split. Positions in the API are string indices. .NET's text elements; fine under NativeAOT.
5. **`TextBox.text` and `onTextChanged`** (decision 8 of M8's round 5: controlled when the view gives it). The
   element keeps the caret and selection; if the view answers with other text than was typed (a filter), the caret
   is clamped into it.
6. **The editing engine is a module with no UI in it:** a buffer, a selection, the edit operations and the undo
   history, plain data in and out, so it is tested by properties (any edits, then as many undos, give the
   original text).
7. **The caret blinks, and stops after 10 seconds without input** (one frame per half second while it blinks;
   solid after, so an idle window with a focused TextBox draws nothing: the power rule).
8. **Clipboard in the platform contract, as asynchronous calls** (the browser can only answer that way).
9. **IME and the phone keyboard are in M9** (step 9d, after editing works). Tim's PC had no IME to test with
   (platform round); Windows can add a Japanese or Chinese keyboard in Settings, else that part is checked by
   tests only.
10. **`TextBox.isPassword true` is in 9a; an editable ComboBox is not in M9** (a control of its own once TextBox
    is settled).

Status: **agreed (Tim, 2026-11-18: "yes to all").**

**Added to 9e (Tim, 2026-11-18: "let's add that to 9e").** A chat-style Enter for multi-line boxes: a setting that
makes Enter call `onSubmitted` and Shift+Enter make the new line (today both make a new line and Ctrl+Enter
submits: 9c, decision 38). It is what people expect of a message box; the name and the details are for 9e's round.
Noted with it, for the same round: a one-line box outside a dialog with no `onSubmitted` does nothing on Enter and
nothing tells the app's author.

### 9a: the single-line TextBox (planned 2026-11-18)

**Round 2 (Tim: yes to all, 2026-11-18).**
11. **Caret positions come from the text system:** for a line of text, the x of every place a caret can stand,
    from the shaped glyphs (so kerning and emoji are right). Inside a ligature the width is split evenly between
    its characters; exact ligature carets (HarfBuzz) later.
12. **Typed text is a routed event:** `onTextInput` on every element and an element-type hook, routed from the
    focused element like keys (promised in M5).
13. **Keys** (Windows conventions; macOS's with the macOS platform): Left/Right by character, with Ctrl by word;
    Home/End; Shift with any of them extends the selection; Ctrl+A; Backspace/Delete a character, a word with
    Ctrl, or the selection; Enter calls `TextBox.onSubmitted`; Escape and Tab are left alone (a dialog still
    closes, focus still moves).
14. **Mouse:** a click places the caret, a drag selects, Shift+click extends, a double-click selects a word, a
    triple-click all; the text cursor over the box. Touch: a tap places the caret; selection handles wait for 9d.
15. **Tabbing into a TextBox selects all its text; clicking into it doesn't** (Windows: "Tab, type" replaces a
    field's value).
16. **Text wider than the box scrolls sideways** to keep the caret in view, clipped, no scrollbar.
17. **Look:** a field like ComboBox (surface, a thin border that takes the accent while focused, its padding and
    height); a dim placeholder; the selection an accent tint behind the text; the caret one DIP wide in the text
    colour, on whole pixels.
18. **Settings:** `text`, `onTextChanged`, `placeholder`, `onSubmitted`, `isReadOnly` (selectable, not editable),
    `maxLength`, `isPassword` (bullets). No controlled selection or caret yet.
19. **Semantics now:** a new role `TextBox`, named by its label or placeholder (a focusable control with no role
    would fail "every sample control has a name"). Its text value waits for 9e.
20. **The engine has no undo in 9a but is shaped for it:** every change goes through one function that returns
    the new state, which 9b records.
21. **Gallery and tests:** a TextBox section on the Controls page (controlled with an echo, its own value, a
    password, read-only, disabled, a `maxLength`); the Icons search and the Layout page's form on real TextBoxes;
    unit tests for the engine's properties, every key and mouse gesture, caret positions, the blink stopping after
    10 seconds with no frames after, controlled text; screenshots of the states in both themes.

Status: **agreed (Tim, 2026-11-18: "yes to all").**

**As built (2026-11-18; checked by Tim on the desktop the same day: "9a is all good").**
- **The engine** is `TextEdit` (`Foundation/TextEditing.fs`): an `EditState` (text, anchor, caret) and functions
  from one state to the next (`insert`, `backspace`, `delete`, the word forms, `move`, `moveTo`, `selectAll`,
  `selectWordAt`, `withText`). A word is a run of one kind (letters and digits, or punctuation); Ctrl+→ stops at
  the start of each. Typed text is cleaned for one line (a line break or tab becomes a space, other control
  characters go). `maxLength` counts grapheme clusters, so an emoji is one.
- **The TextBox** (`Elements/Controls/TextBox.fs`) extends Border and has no children: it draws its own text,
  selection and caret, clipped to the padding box. Every change goes through one `apply` (decision 20).
- **Controlled text and frames.** What is typed shows at once; at the next frame (views are applied before it
  draws) the box shows what the `text` setting says, so a view that answers with other text wins and one that
  doesn't follow puts the old text back. Keys that arrive between two frames all land: input only takes the
  setting's value when the view has changed it since the last look.
- **New for element authors:** `Element.textInput` and `Layoutable.onTextInput` (decision 12);
  `Element.focusChanged (fun ctx isFocused byTab -> ...)`, which is how the box knows Tab brought the focus.
- **Input sees the input's time:** the host sets each tree's time when a press, a key or text arrives, not only
  at a frame. In an idle window the last frame may be long ago, and two slow clicks would have counted as a
  double-click (a ListBox's too).
- **The blink** is twenty half-second timers on the platform's clock (`Dispatcher.PostAfter`) after the last
  input, each asking for one frame; the last leaves the caret on. Losing focus stops it. A tree without a
  dispatcher has a caret that stays on.
- **Not in the round, chosen while building:** ↑ and ↓ go to the start and the end (so they don't scroll the page
  behind the box); keys that type are marked handled (Space would have scrolled a ScrollViewer around the box);
  a password is one word (Ctrl+arrows and a double-click don't show where its words are); a drag on from a
  double-click selects whole words; out of focus the selection is hidden and the text shows its start; a touch
  places the caret without taking the press, so a finger can still scroll the page; a read-only box shows no
  caret; the selection's tint is stronger than a list item's (`ThemeConvert.textSelectionTint`, half the
  accent); it asks for 160 DIPs of text width when nothing stretches it.
- **Left as is:** a drag past the box's edge scrolls only while the pointer moves (no timer); the Layout page's
  Notes field is one line until 9c; Shift+Delete and the other clipboard keys do nothing until 9b.
- **Tests:** 34 unit tests (the engine's rules on 200 random sequences of 120 edits; every key and gesture;
  controlled text; the blink ending with no frames after); two screenshots (`textbox@1x`, `textbox-light@1x`).
  Totals: 1,181 unit (+6 timing), 97 rendering, 6 timing.

### 9b: clipboard and undo (planned 2026-11-18)

Facts at the start. Nothing in the code mentions a clipboard: not `IPlatform`, not the SDL bindings, not the
browser platform's JavaScript. SDL's clipboard is a synchronous call. The browser writes text freely during a key
press; reading through its API asks the person for permission, but its own `paste` event hands the text over with no
prompt. Every edit already goes through one `apply` (decision 20).

**Round 3 (Tim: yes to all, 2026-11-18).**
22. **The platform contract:** `IPlatform.SetClipboardText(text)` and `GetClipboardText() : Task<string>`. Text
    only. The desktop answers at once; the tests' fake holds a string.
23. **Paste in the browser comes from the browser's own paste event:** on Ctrl+V the browser fires `paste` and the
    pending read is answered from it (no permission prompt; works where the reading API is restricted). The
    permission-asking API is only for a read with no key press behind it.
24. **An API for apps:** a hook, `ctx.useClipboard()`, giving `SetText` and `GetText`; element authors get the same
    through their input context.
25. **Keys** (Windows conventions): Ctrl+C, Ctrl+X, Ctrl+V and the old forms Ctrl+Insert, Shift+Delete,
    Shift+Insert; Ctrl+Z undoes; Ctrl+Y and Ctrl+Shift+Z redo.
26. **At the edges:** Copy or Cut with no selection does nothing (the clipboard keeps what it had). Paste replaces
    the selection; line breaks and tabs become spaces (one line until 9c); `maxLength` keeps what fits. A read-only
    box copies but doesn't cut, paste or undo. A password box pastes but doesn't copy or cut.
27. **No right-click menu in 9b:** it comes with a Menu control; touch selection in 9d.
28. **One undo step:** a run of typing, or a run of Backspaces or Deletes. A new step starts when the caret is
    moved, when the kind of edit changes, or after a one-second pause (the platform's clock). A cut, a paste and
    typing over a selection are each a step of their own.
29. **Undo puts the selection back too:** each step restores the text, caret and selection from before it; redo
    those from after.
30. **The history belongs to the box:** up to 100 steps, kept in the element, gone with it. A new edit after an undo
    drops the redo steps.
31. **Undo and controlled text:** an undo goes to the app through `onTextChanged` like any edit. When the view
    answers an edit with other text (a filter), the history records what the view answered. When the view sets text
    for another reason (a Clear button, loaded data), the history is cleared.
32. **The browser's own shortcuts:** the TextBox claims Ctrl+Z, Ctrl+Y, Ctrl+C and Ctrl+X while it has focus;
    Ctrl+V is left to the browser so its paste event fires.
33. **Gallery and tests:** a "Copy" button beside the Hello line (the hook); engine properties (any edits, then as
    many undos, give the original text and selection; as many redos give the result back); unit tests for every
    key and edge rule, step grouping on the fake clock, the limit, controlled text, the hook. No screenshots:
    nothing new is drawn.

Status: **agreed (Tim, 2026-11-18: "Yes to all").**

**As built (2026-11-18; checked by Tim on the desktop the same day, and copy and paste in the browser on localhost:
no permission question).**
- **The contract:** `IPlatform.SetClipboardText` and `GetClipboardText` (decision 22). SDL: `SDL_SetClipboardText`
  and `SDL_GetClipboardText` (freed with `SDL_free`), a finished task. The fake platform holds a string and can
  answer late, as the browser does.
- **The browser** (`scribble-platform.js`): a write is `navigator.clipboard.writeText`. A read after a paste key
  (Ctrl+V, Shift+Insert, within a second) waits for the page's `paste` event and is answered from it, whichever
  comes first; with no event in half a second, or with no paste key, it is `navigator.clipboard.readText`, which
  asks. The answer comes back as an event record (kind 12) like every other, so nothing new is marshalled. A page
  without the clipboard API (plain http that isn't localhost) copies nothing, and a read with no paste key gives
  ""; a paste key still pastes, since the paste event needs no API.
- **For apps and element authors:** `Clipboard` (`Foundation/Clipboard.fs`) with `SetText` and `GetText`, from
  `ctx.useClipboard ()` in a component and `ctx.Clipboard` in an element type's input handlers. It finds the
  platform through the thread's app (`RenderServices`, as `Files.save` does); a read that isn't finished at once
  finishes on the UI thread. Without a running app it does nothing.
- **The history** is `TextHistory` (`Foundation/TextEditing.fs`), beside `TextEdit` and as free of UI: steps of
  the state before and after, a kind (`Typing`, `Deleting`, `Single`) and the time of the last edit. An edit joins
  the newest step when it has the same joining kind, starts in exactly the state that step ended in, comes within
  a second of it, and no caret move came between. `amend` is decision 31's filter answer.
- **In the TextBox** every edit still goes through one function (`applyWith`), which now records it; a move of the
  caret or selection closes the step being typed, and so does focus coming back. A paste that answers late lands
  only if the box still has focus and can take it.
- **Controlled text:** the box remembers that it told its view of an edit. Other text from the view while that is
  pending is the answer (the step ends in it; the old text back removes the step); other text at any other time,
  or as the answer to an undo, clears the history.
- **Claimed keys** (decision 32): the box claims Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z, Ctrl+C and Ctrl+X the first time
  it takes focus, and they stay claimed (`ClaimKey` has no way back; on a canvas page the browser has nothing of
  its own to undo or copy).
- **Not in the round, chosen while building:** a paste of nothing (an empty clipboard, or only control
  characters) changes nothing and keeps the selection; Ctrl+Backspace and Ctrl+Delete join a run of deleting.
- **Gallery:** a "Copy" button beside the Hello line on the Controls page (`ctx.useClipboard`).
- **Tests:** 18 new unit tests (`TextBoxClipboardTests.fs`): the history's rules and its property on 200 random
  sequences of 60 edits; every key and edge rule; a clipboard that answers late; controlled text; the claimed
  keys; the hook. One reference regenerated (`gallery-phone-controls@3x`: the page is longer, so its scrollbar
  is shorter). Totals: 1,201 unit (+6 timing), 97 rendering, 6 timing.

### 9c: the multi-line TextBox (planned 2026-11-18)

Facts at the start. The text system wraps (`TextSystem.Layout`, what TextBlock uses), but a laid-out line holds only
its text and width: not where in the string it came from, and without the spaces at a wrap. Caret places exist for
one line at a time (`CaretStops`). The TextBox draws itself and has no children, so it can't sit in a ScrollViewer:
scrolling up and down is its own, as sideways scrolling is. 9a left a drag past the edge scrolling only while the
pointer moves.

**Round 4 (Tim, 2026-11-18: "for 39, can we add .acceptsTab? yes for everything else").**
34. **Multi-line always wraps:** `TextBox.multiline true` wraps at the box's width, with no sideways scrolling. A
    no-wrap mode (for code) waits until something needs it.
35. **Height:** the box asks for the height of its text, never less than `TextBox.minLines` (default 3) and never
    more than `TextBox.maxLines` (default: no limit). A height from the layout wins. Text that doesn't fit scrolls.
36. **Scrolling is the box's own:** it scrolls to keep the caret in view; the wheel scrolls it while there is more
    to see and passes to the page at either end; a scrollbar like ScrollViewer's shows while the text overflows and
    can be dragged.
37. **A drag past the edge keeps scrolling** on a timer while the button is held outside the box (frames only for
    as long as that lasts), in single-line boxes too.
38. **Enter makes a new line; Ctrl+Enter submits:** in a multi-line box Enter and Shift+Enter insert a line break
    and `onSubmitted` moves to Ctrl+Enter. A dialog's default button doesn't fire on Enter while such a box has
    focus.
39. **Tab moves focus unless `TextBox.acceptsTab true`** (Tim's addition): by default no tab characters are typed
    and a pasted tab becomes a space; with `acceptsTab` Tab types one and pasted tabs are kept. Pasted line breaks
    are kept in a multi-line box; the text always uses `\n` (a pasted `\r\n` becomes one).
40. **Moving by line:** ↑/↓ a line at a time, keeping the horizontal position they started from across short
    lines; ↑ on the first line goes to the start, ↓ on the last to the end; Home/End to the ends of the line as
    drawn, Ctrl+Home/End to the ends of the text; PageUp/PageDown by the box's height; Shift with any of them
    selects.
41. **The caret at a wrap:** the position where a long line wraps is both the end of one line and the start of the
    next. End shows the caret at the line's end; otherwise it shows at the start of the next line.
42. **Mouse:** a click goes to the nearest caret place on the line under it (below the last line: the end of the
    text); a drag selects across lines; a triple-click selects the paragraph in a multi-line box.
43. **Layout lines know where they came from:** each line of a text layout gains its start and end in the string,
    and spaces at a wrap stay on their line (hanging past the edge, undrawn). TextBlock uses the same code, so a
    TextBox wraps where a TextBlock would; TextBlock's pictures don't change.
44. **Size it is built for:** a text area, not a code editor. Only visible lines are drawn and an edit re-shapes
    only the paragraph it is in. Tests: the same drawing work for 100 and 10,000 lines; a timing test for typing
    into a 10,000-line text.
45. **Smaller rules:** a typed line break starts a new undo step; `maxLength` counts a line break as one
    character; the placeholder wraps; `isPassword` with `multiline` stays single-line and warns once in Debug.
46. **Single-line boxes don't change:** the same keys, look and pixels; their screenshots pass untouched.
47. **Gallery and tests:** the Layout page's Notes field becomes multi-line; the Controls page gets a multi-line
    box growing from 3 to 6 lines with a line and character count. Unit tests for every key and gesture, wrap
    positions matching TextBlock's, the caret at a wrap, growing and scrolling, the drag timer stopping,
    controlled text, undo across lines. Screenshots: a wrapped box with a selection across lines and one scrolled,
    in both themes.

Status: **agreed (Tim, 2026-11-18).**

**As built (2026-11-18; checked by Tim on the desktop the same day).**
- **Layout lines** (`TextLine`, decision 43) have `Start` and `End`: indices into the text that was laid out, the
  spaces at a wrap inside the line and the line break outside it. `Text` is still what is drawn, so TextBlock's
  pictures are unchanged. **A tab** is drawn as nothing, four spaces wide (no tab stops), wherever text is shaped.
- **The engine:** `TextEdit.cleanFor multiline tabs` and `insertInto` (the one-line `clean` and `insert` are
  those with both off); a line break is a kind of its own for words, so Ctrl+←/→ stop at a line's end and a
  double-click never selects across lines; `paragraphStart`, `paragraphEnd`, `selectParagraphAt`.
- **Lines in the TextBox** (`TextLines`, `MultiLine` in `TextBox.fs`): the text split into paragraphs, each laid
  out by `TextSystem.Layout` (so it wraps as a TextBlock does), with where each starts in the text and among the
  lines. After an edit the paragraphs unchanged at the start and the end are kept and only those between are laid
  out: one for a typed character. Caret places are measured per line when first needed. Drawing walks only the
  lines that show.
- **Height** (decision 35): the measure wraps at the width the box was last drawn with (before its first frame,
  the width it is offered). When a resize makes the drawn width wrap to another number of lines, the draw asks for
  one more measure, so the height follows a frame later. `TextBoxProps.text` now affects measure.
- **Scrolling** (decision 36): the box's own offset. The caret's line is brought into view when the caret moves or
  the text changes, not on every frame, so the wheel and the scrollbar can leave the caret out of view. The
  scrollbar uses ScrollViewer's thumb sizes and the theme's `Palette.scrollThumb`; a press beside the thumb
  pages. Less than a DIP of overflow (layout rounding) is not overflow.
- **The drag timer** (decision 37): while the button is held past the edge the text scrolls along (above or below
  several lines, left or right of one), every 50 ms the selection is taken to the pointer's place again, which
  scrolls. Nothing is drawn once nothing more can be selected.
- **Not in the round, chosen while building:**
  - Tabbing into a multi-line box keeps its caret and selection (selecting all would let one key wipe the notes);
    one line still selects all.
  - With `acceptsTab`, Shift+Tab still moves the focus back, so the keyboard can always leave. `acceptsTab` works
    in one-line boxes too.
  - A read-only multi-line box doesn't take Enter: it goes on to a dialog's default button.
  - The wheel scrolls at once, not eased as ScrollViewer's notches are.
  - A selected line break shows as a little width after its line.
  - Out of focus a multi-line box stays where it was scrolled; one line still shows its start.
  - PageUp and PageDown keep one line in view, and like ↑/↓ go to the text's start or end from its first or last
    line.
- **Gallery:** the Layout page's Notes is multi-line (2 to 4 lines); the Controls page has "TextBox, several
  lines": a controlled box (3 to 6 lines) with a paragraph and character count, and one with `acceptsTab`.
- **Tests:** 21 new unit tests (`TextBoxLinesTests.fs`) and one timing test: typing a character into 10,000 lines
  and drawing its frame took 0.26 ms (Release, Tim's machine). Two new screenshots (`textbox-lines@1x`,
  `textbox-lines-light@1x`); `textbox@1x` and `textbox-light@1x` passed untouched (decision 46). Regenerated after
  looking: `layout-page@1x`, `layout-grid-overlay@1x`, `gallery-phone-layout@3x` (the Notes field is higher) and
  `gallery-phone-controls@3x` (a longer page: its scrollbar). Totals: 1,222 unit (+7 timing), 99 rendering,
  7 timing.
- **Left as is:** no sideways scrolling or no-wrap mode in a multi-line box; no tab stops; the placeholder is laid
  out on every frame it shows (shaping is cached).

### 9d: IME composition and the phone keyboard (planned 2026-11-18)

Facts at the start. SDL's text input is on for every window all the time; only committed text is handled (no
composing event), and SDL is never told where the caret is, so an IME's candidate window can't follow it. In the
browser typed text comes from each key press's character: an IME, a phone keyboard, swipe typing and some dead keys
send none, so none of them work; a phone shows its keyboard only for a focused editable DOM element, and the page has
none. Left for 9d by earlier steps: selection by touch (decision 14) and finger scrolling inside a multi-line box.
A risk reading can't settle: an iPhone shows the keyboard only when the page focuses its input inside the touch
handler itself, and our events reach .NET a moment later.

**Round 5 (Tim: yes to all, 2026-11-18).**
48. **Touch selection moves out of 9d:** long-press to select, drag handles and the copy/paste bubble are a step of
    their own after 9e, with the Menu control. 9d keeps composition, the phone keyboard and finger scrolling inside
    a multi-line box.
49. **A phone spike first:** a throwaway page on Tim's phone (through `tools/Scribble.Serve`) to find a way of
    focusing a hidden input that brings the keyboard up on iPhone and Android, before the browser design is fixed.
50. **The window is told when text is wanted:** a call on the window says "text input on, the caret is in this
    rectangle, this kind of keyboard" or "off"; Core calls it when focus moves and when the caret moves. Desktop:
    SDL's text input starts and stops with it and the candidate window follows the caret. Browser: a hidden text
    field is focused and placed at the caret.
51. **Which elements want text:** the focused element wants text when it, or something around it, listens for
    typed text (a TextBox, or any element with `onTextInput`).
52. **Composition is a new event:** besides "text was typed", the platform sends "this text is being composed"
    (with the IME's caret in it; empty when it ends). Element types get a hook; no app-facing setting.
53. **In the browser, typed text comes from the hidden field while one is focused:** its input and composition
    events replace the per-key characters. Autocorrect and predictive text are off in 9d.
54. **Composing text is drawn in the box, underlined, and isn't part of `text` yet:** it replaces the selection
    when it starts; `onTextChanged` hears nothing until it is committed; a commit is one undo step; `maxLength` and
    read-only apply to the commit.
55. **Keys belong to the IME while it composes:** arrows, Enter, Escape and Backspace act on the composition, not on
    the box.
56. **A click elsewhere or losing focus commits what is composed.**
57. **`TextBox.keyboard`:** Text (default), Number, Decimal, Email, Url, Phone, Search. Only which on-screen
    keyboard shows; nothing is validated. A password box gets the password keyboard by itself.
58. **When the phone keyboard shows, the app shrinks to the space above it** and the focused box scrolls into
    view; a finger drag on an overflowing multi-line box scrolls its text, then the page at its ends.
59. **Checking it:** the fake platform records the "text wanted" calls and sends composition events, so the
    TextBox's side is unit tested, with one screenshot of composing text. Tim adds a Japanese keyboard to Windows
    for the desktop IME, and checks on his phone during this step, not at the later browser pass.

Status: **agreed (Tim, 2026-11-18: "Yes to all").**

**The spike (decision 49; Tim's Android phone, Edge 157, 2026-11-18).** A throwaway page
(`artifacts/serve/spike`, served by `tools/Scribble.Serve` under `/spike/`, which also takes the page's report)
tried nine ways of focusing a hidden text field after a tap, and logged what the keyboard sends.
- Focusing it inside `pointerdown` or `pointerup`, at once or in a microtask (which is where .NET runs), left it
  **not focused**; from a timeout, an animation frame or `click` it took the focus and the keyboard came up. The
  early focus isn't refused: the tapped element takes the focus back when the touch's `mousedown` arrives. So the
  canvas prevents that `mousedown` while text is on, and no longer focuses itself on a press then.
- Android is lenient about when: even 100 ms after the touch works.
- With suggestions off, each character is an `input` event (`insertText`); its `keydown` says "Unidentified"
  (code 229). Backspace and Enter come as ordinary key events, also with the field empty, so the field can stay
  empty and needs no stand-in character.
- The keyboard shrinks the visual viewport only (723 to 382 px here); the page's own height stays.
- **No iPhone was tried.** Safari is stricter about focusing outside the touch's own handler; the microtask after
  `pointerdown` may or may not count there.

**As built (2026-11-18; checked by Tim the same day on the desktop with the Japanese IME and on his Android phone
in Edge and Chrome).**
- **The contract:** `IWindow.SetTextInput (request voption)` (the caret's box in pixels, a `KeyboardKind`,
  whether Enter makes a line) and `IWindow.ClearComposition ()`; the event `TextComposition (text, caret)`.
  `KeyboardKind` is Text, Number, Decimal, Email, Url, Phone, Search, Password.
- **Core:** an element type that takes text says where its caret is with `ctx.SetTextInput` (input and render
  contexts); `Element.textComposition` is the hook for composing text, routed to the first type from the focused
  element up that has it. The host tells the window when the focus moves (at once) and after every frame, and
  only when the request has changed. An element with `onTextInput` and no caret of its own counts as its whole box.
- **SDL:** text input is no longer on for every window: it starts and stops with the request
  (`SDL_StartTextInputWithProperties`, `SDL_StopTextInput`), `SDL_SetTextInputArea` places the candidates, and
  `SDL_HINT_IME_IMPLEMENTED_UI = "composition"` makes SDL send `SDL_EVENT_TEXT_EDITING`, so Scribble draws the
  composing text and the system draws only its candidate list. Checked against SDL 3.2's headers.
- **The browser:** a hidden `<textarea>` (made on first use) holds the DOM focus while text is on; its `input`
  events are the typed text (the per-key characters are used only while it isn't focused), its composition
  events the composing text, and it is emptied after each. Keys an input method takes (`isComposing`, code 229)
  aren't sent as keys. `inputmode` and `enterkeyhint` follow the keyboard kind. While text is on and the visual
  viewport is clearly shorter than the page, the canvas is fitted to it; Core brings the focused element into
  view when the window resizes with text on.
- **The TextBox:** what is composing lives beside the text (`TextBoxState.Composition`); `Shown` is the text with
  it in place of the selection, and is what is measured, wrapped and drawn, with a one-DIP underline. A commit
  after a composition is an undo step of its own. While composing every key is marked handled and ignored. A
  press in the box or the focus leaving types what was composing and calls `ClearComposition`.
- **Touch:** a finger's tap places the caret when it lifts (it used to on the press, which fought a scroll); a
  finger drag on overflowing lines scrolls them when there is more that way at its start, else it is the page's.
- **Not in the round, chosen while building:** the composing text's caret is where the input method says on the
  desktop and at its end in the browser (browsers don't say); a composition in a read-only box is dropped at
  once; `TextBox.keyboard` is not applied to the desktop (SDL is told, and ignores it there).
- **Gallery:** two boxes on the Controls page with `TextBox.keyboard` (Number, Email).
- **Tests:** 12 new unit tests (`TextBoxInputMethodTests.fs`) and two screenshots (`textbox-composing@1x`,
  `textbox-composing-lines@1x`). Regenerated after looking: `gallery-phone-controls@3x` (a longer page).
  Totals: 1,240 unit (+7 timing), 100 rendering, 7 timing. The PNG timing test failed one run in three while
  the Serve tool was running (29 ms against its bound); the other runs passed.
- **Not verified by any test:** the SDL calls and events, and all of the browser's JavaScript.
- **The strip above the phone's keyboard (Tim's phone, 2026-11-18).** With the keyboard up, Edge on Android
  showed about 29 CSS px of the black page below the canvas. A second spike page (`artifacts/serve/spike/viewport.html`)
  measured every size the page can ask for: with `interactive-widget=resizes-content` they all agree (inner
  height, visual viewport, a bottom-fixed element, 100%, dvh and svh: 434.7 px), and the screenshot shows 463 px
  of page: the browser shrinks its bottom bar while the keyboard is up and tells the page nothing. So nothing
  can size the canvas right there. What was done: the page asks to be resized with the keyboard
  (`interactive-widget=resizes-content`); the canvas ends at a bottom-fixed probe, not at 100% (which is the
  page with the browser's bars showing); and the page behind the canvas takes the app's background colour
  (`IWindow.SetBackground`, told when the clear colour changes; also the page's `theme-color`), so a strip the
  browser leaves is more background, not a black bar. Chrome on the same phone shows no strip at all (Tim): it is
  Edge's own, and looks like the space Edge keeps above the keyboard for its autofill suggestions (empty here: the
  hidden field asks for none).
- **Tried by Tim (2026-11-18), desktop, Japanese IME:** every composing step, the candidates and the commit
  arrive (`SCRIBBLE_LOG_TEXT=1` writes what SDL sends to the console). The IME starts in "A" mode, which composes
  nothing in any app: あ is Alt+` or a click on the taskbar's A.
- **`SCRIBBLE_LOG_TEXT=1` stays** (Tim asked, 2026-11-18): off unless set, AOT-safe, and the only way to see what an
  input method sends on a machine. It prints what is typed, passwords included: a developer's switch.
- **Known limit, left for later (Tim, 2026-11-18): Japanese draws as missing-glyph boxes.** Scribble draws with
  the fonts an app bundles; the Gallery has Inter and Twemoji, neither with Japanese. Fonts for other scripts are
  a topic of their own (which scripts, download size, a font package like the emoji one, or the system's fonts,
  which would break "same pixels on every platform").

### 9e: what waited for M9 (planned 2026-11-18)

Facts at the start. The chat-style Enter was added to this step by Tim (above, after round 1). Typeahead was put
off twice (M8's ListBox and ComboBox); an item drawn by a view has no text of its own to search. Labels are text
only (`Semantics.label`). Since 9d the window's text input is on whenever the focused element takes typed text: a
ListBox taking letters would bring a phone's keyboard up.

**Round 6 (Tim: yes to all, 2026-11-18).**
60. **`TextBox.submitOnEnter true`:** in a multi-line box Enter calls `onSubmitted` and Shift+Enter makes the new
    line; Ctrl+Enter still submits. Off by default (9c's behaviour stays).
61. **The box doesn't clear itself after a submit:** a message box gives its text through `TextBox.text` and clears
    it in its own state (a failed send keeps the text).
62. **On a phone the rule is the same:** the on-screen Enter submits; an app that wants new lines there adds a Send
    button and leaves the setting off.
63. **No warning for a one-line box without `onSubmitted`:** Enter goes on to whatever is around the box.
64. **ListBox typeahead:** typed letters move the selection to the next item whose text starts with them. They
    build up and start again after a one-second pause; the same letter again steps through the items starting with
    it; Space is part of the search while letters are building up, else it selects as before. It searches the
    source, so it finds items that aren't realized.
65. **ComboBox typeahead:** closed, typing picks the matching item; open, it moves the highlight in the list.
66. **An item's text:** `ItemsView.textSelector (fun p -> p.Name)`, optional. Without it the text form of
    `viewSelector`, else `string item`. Also the item's name for screen readers when a view draws it.
67. **A list takes typed letters without asking for a keyboard:** an element that takes text but reports no caret
    gets letters from a physical keyboard; no on-screen keyboard shows and no input method composes into it. Only
    an element that reports a caret (a TextBox) asks for those.
68. **A `Label` element names another control:** `Label.create [ Label.text "Email"; Label.target "email" ]` points
    at the control named "email" (`TextBox.name`). The target takes the label's text as its name for screen readers
    unless it sets `Semantics.label` itself; clicking the label focuses the target; the target is the nearest
    element with that name, searching outward from the label. A TextBox also reports its text, whether it is
    read-only and whether it is a password (whose text is not reported).

Status: **agreed (Tim, 2026-11-18: "Yes to all").**

**As built (2026-11-18; checked by Tim on the desktop the same day).**
- **`TextBox.submitOnEnter`** changes one key in a multi-line box; a one-line box ignores it.
- **Typeahead** is one rule (`Typeahead.find`, with a `TypeaheadState`, in `KeyStore.fs`) used by ListBox and by a
  closed ComboBox; an open ComboBox's list is a ListBox, so it has it already. A single letter, or the same letter
  again, goes to the next match after the current item; more letters stay on the current item while it matches.
  Matching ignores case and nothing else (an accented letter is not its plain one). In a Single list the selection
  follows; in Multiple and None only the focus moves.
- **An item's text** (`ItemsView.itemText`): `textSelector`, else the text of a text `viewSelector` (the function is
  kept on the view selector), else the item when it is a string, a number or an enum. A record with neither has no
  text: typing finds nothing. With a `textSelector`, a ListBox gives each realized item that text as its
  `Semantics.label`.
- **`KeyboardKind.None`** (decision 67): the host asks with it for a focused element that takes text but has
  reported no caret. The browser treats it as "off" (letters come from key presses while the hidden field has no
  focus); SDL starts text input, which is how a desktop gets letters at all. This also changes 9d: an element with
  only `onTextInput` no longer raises a phone's keyboard.
- **`Label`** (`Elements/Basic/Label.fs`) extends TextBlock, so it draws as one (the Gallery's form pictures did not
  change). `Label.findTarget` looks beside the label, then beside each thing that holds it, never looking at the
  same subtree twice. The semantic tree collects every Label's target once per build. A name comes from the
  element's own fixed `Semantics.label`, else the Label for it, else what its type reads its name from (a
  TextBox's placeholder), else its content.
- **Semantics:** `SemanticNode.Value`, `IsReadOnly`, `IsProtected`; `Semantics.value`, `valueFrom`, `isReadOnly`,
  `readOnlyFrom` for element types of one's own. `Describe()` shows them (`TextBox "Name" value "Ada"`).
- **Not in the round, chosen while building:** a press on a Label whose target is missing takes the focus away,
  as a press on any text does; typeahead on a closed ComboBox keeps Space as a letter while letters build up, so
  it doesn't open the list then.
- **Gallery:** a message box with `submitOnEnter` and a Send button on the Controls page; the Layout page's form
  uses Labels, and its Email field asks for the email keyboard. The "Sent:" line shows a line break as ↵
  (U+21B5): Inter has it, and not the return symbol U+23CE first used there, which drew as a missing-glyph box
  (Tim saw it in the NativeAOT run).
- **A line break in a one-line box (Tim, 2026-11-18: "do it now").** What is typed or pasted has none (they become
  spaces on the way in), but text the app gives is never altered, and a one-line box drew what followed a break on
  a second, clipped line with the caret placed as if on one. Now a one-line box draws `\n` and `\r` as spaces
  (`\r\n` as two); the text is untouched, and the caret steps over a break as one character. Considered and not
  done: stripping breaks from the value (an HTML input does; it changes the app's text behind its back) and
  showing only the first line (WPF does; it hides text).
- **Tests:** 11 new unit tests (`TypeaheadAndLabelTests.fs`), one of them reaching item 9,000 of 10,000 with at
  most 60 items realized. Regenerated after looking: `gallery-phone-controls@3x` (a longer page). Totals: 1,252
  unit (+7 timing), 100 rendering, 7 timing.

### 9f: the Menu (planned 2026-11-18)

Facts at the start. Flyouts do the hard part already (their own layer, placement that flips to stay in the window,
closing on a press outside or Escape), and menus and context menus were the reason the flyout became a value (M8
step 4, round 1b). The Gallery's Layers page fakes a menu from a StackPanel of buttons, with no arrow keys. Missing:
a flyout placed at the pointer; anything that opens on a right-click, a long press or the Menu key; Menu and
MenuItem roles. In the browser the canvas already stops the browser's own right-click menu.

**Round 7 (Tim, 2026-11-18: yes to all but 71, which he reshaped).**
69. **Two steps:** **9f** the Menu, context menus and the TextBox's right-click menu; **9g** selection by touch
    (long press, drag handles, the copy/paste bubble), which reuses 9f's menu.
70. **No submenus in 9f:** items, separators and check marks. Submenus come with a menu bar; `MenuItem.items` can
    be added then without changing what exists.
71. **`flyout` and `contextMenu`** (Tim: "what would a programmer using this library prefer? maybe .flyout and
    .contextMenu?"). A Menu is a kind of flyout: `Button.flyout (Menu.create [ Menu.items [ ... ] ])` opens it on a
    click, as any flyout; `TextBox.contextMenu (Menu.create [ ... ])` opens it at the pointer. One name for each
    thing, and the names FuncUI and Avalonia use. Considered: `menu` beside `flyout` (two settings for what a
    click opens), `menu` as another name for `flyout`'s slot (two ways to say one thing), and `contextFlyout`
    (general, but nobody calls it that).
72. **The shortcut text is only text:** `MenuItem.shortcut "Ctrl+C"` shows at the item's right and registers no key.
73. **What opens a context menu:** a right-click, the Menu key or Shift+F10 (at the focused element), and a long
    press with a finger (half a second without moving).
74. **Inside a menu:** ↑/↓ move through the items and wrap, Home/End go to the ends, disabled items are skipped;
    Enter or Space invokes; a typed letter goes to the item starting with it; Escape or Tab closes it and gives
    the focus back; a click on an item invokes it and closes the menu; a press outside closes it. Opened by the
    keyboard the first item is highlighted; by the mouse, none.
75. **The TextBox's menu is built in and replaceable:** Undo, Redo, Cut, Copy, Paste, Select all, each enabled only
    when it applies. A `contextMenu` the app sets on the box replaces it.
76. **Its six labels can be translated:** they live in one app value an app can replace.
77. **Paste from the menu in a browser may ask permission** (a click is not a paste key: 9b, decision 23). Chrome
    asks once; where the browser refuses, Paste does nothing. Ctrl+V keeps working everywhere.

Status: **agreed (Tim, 2026-11-18); built and checked by Tim the same day.**

**As built (2026-11-18):**

- **A Menu's items are its `children`**, not `Menu.items` as decision 71's example has it: Menu is a StackPanel,
  and Panel already has `items source key view` (the same name with other arguments is an error in F#, FS0816).
  So `Menu.create [ Menu.children [ MenuItem.create [ ... ]; Menu.separator ] ]`, and `Menu.items` makes items
  from a list as for any panel.
- **Menu and MenuItem** (`Elements/Controls/Menu.fs`). A MenuItem is a Border that draws itself: an icon or a
  check mark, the text, the shortcut text at the right in the theme's dim colour. When any item of a menu has an
  icon or can be checked, every item leaves room for one, so the texts line up. `MenuItem.isChecked value` makes
  an item one that is on or off; the click handler changes the app's value. A click runs `onClick`, then the menu
  closes: the item asks the flyout whose layer it is in to close (`ElementTree.Owner`), so it works the same in a
  button's flyout and a context menu, controlled or not. MenuItems are Tab stops, because a flyout gives the
  focus to its first Tab stop when it opens; Tab itself closes the menu.
- **`contextMenu` is a setting on every element** (`Layoutable.contextMenu view`; any view, usually a Menu), and
  an element type can give one of its own (`Element.contextMenu`, what the TextBox uses). The host looks from the
  element under the pointer outward for the first that has either; the app's setting wins over the type's.
- **A context menu stands in a layer of its own**, as the flyout of a 1 × 1 element placed at the pointer (or at
  the caret, or the focused element's bottom-left, for the Menu key and Shift+F10). So the flyout's placement,
  flipping, light dismiss and focus are reused whole. Two consequences: a style or a `theme` set around the target
  element does not reach its context menu (the window's theme does), unlike `flyout`; and the focus goes back to
  where it was when the menu closes, because that layer never took the keyboard from the app's.
- **A right-click opens it on release**; the right press itself still reaches the element (a TextBox moves the
  caret there unless the press is inside the selection, and takes the focus).
- **Long press:** 500 ms within 8 DIPs, touch and pen. The press is cancelled (no click on lift) and the rest of
  that touch is swallowed. A pan that starts first cancels the wait.
- **`Key.Menu`** is new in the platform contract (SDL scancode 101, the browser's `ContextMenu`).
- **Roles** `Menu` and `MenuItem`; a checkable item reports its check state.
- **The TextBox's menu** is built on each open from the box's state: Undo, Redo | Cut, Copy, Paste | Select all,
  with the shortcut texts. A password box offers no Cut or Copy; a read-only one only Copy and Select all. The
  labels are the app value `TextBoxMenu.labels` (a `TextBoxMenuLabels` record; `TextBoxMenuLabels.English` is the
  default). The key handlers and the menu call the same commands. The box keeps its selection on show while its
  menu is open with no extra state: focus is per layer.
- **Tests:** 11 unit tests (`MenuTests.fs`: keys, wrap, skipping disabled items, typeahead, checked items,
  closing, right-click, Menu key, long press, the TextBox's menu and its enabled states, replaced labels); two
  screenshots (`menu@1x`, `menu-light@1x`). Regenerated after looking: `gallery-phone-layers@3x` (the Layers page
  is longer, so the scrollbar's thumb is shorter). Totals: 1,264 unit (+7 timing), 102 rendering, 7 timing.
  `ImageViewTests` "the Gallery's Images page finds its embedded pictures" failed once in six full runs and
  passed alone; not looked into yet.
- **Not checked by a test:** how a right-click and the menus feel in a real window, the Menu key on a real
  keyboard, Paste from the menu in a browser, a long press on a phone.

### 9g: selection by touch (grilling, 2026-11-18)

Facts at the start. A tap places the caret; a long press (500 ms within 8 DIPs, owned by the host) opens the
TextBox's context menu at the finger; there are no selection handles and no way to select a word by touch.

**Round 8 (Tim, 2026-11-18: yes to all, in three rounds of questions).**
78. **Only the TextBox** (editable, read-only, password). Selectable text in a TextBlock is a feature of its own;
    a read-only TextBox covers "select and copy" until then.
79. **One look on every platform, close to Android's:** a caret handle after a tap, two selection handles round a
    text selection, a row of commands above it; drawn in the theme's colours. Considered: iOS's (no caret handle,
    a long press moves the caret) and a design of our own.
80. **The row is the TextBox's context menu, shown by touch**: the same commands, enabled rules and
    `TextBoxMenu.labels`; an app's `contextMenu` replaces it by touch as by mouse. No new term ("bubble" is not
    one). Considered: a separate thing an app can't replace.
81. **Only touch gets handles and the row**, by pointer kind, not platform (a touchscreen laptop too). A pen keeps
    the mouse's drag-to-select and 9f's long press for the vertical menu.
82. **A long press in a TextBox:** on a word, it selects the word and shows the handles and the row; on blank
    space or past the text's end, it places the caret and shows the row with what applies. Keeping the finger down
    and dragging extends the selection by words.
83. **A double-tap selects a word**, as the long press does.
84. **Only a TextBox's context menu becomes a row.** Any other element's opens on a long press as 9f built it: the
    vertical Menu at the finger.
85. **The built-in row leaves out Undo and Redo** and shows Cut, Copy, Paste, Select all, each only when it
    applies (hidden, not disabled). The mouse's menu keeps all six, greyed when they don't apply.
86. **Where the row sits:** centred above the selection or the caret, clear of the text; below the lower handle
    when there is no room above; in the middle of what's visible when both ends are out of view; always inside the
    window.
87. **Handles stand in a layer of their own**, so nothing clips them; a handle whose end is scrolled out of view
    (the box's own scroll or a ScrollViewer round it) is hidden until it comes back.
88. **The caret handle** shows after a tap, hides on typing and goes after 4 s untouched (the platform clock). The
    two selection handles stay while there is a selection.
89. **Dragging a handle** moves its end by character, the handle's point offset above the finger; the ends may
    cross (they swap); near the box's edge the box scrolls as for 9a's mouse drag; the row hides during the drag
    and comes back on lift.
90. **No magnifier in 9g** (Deferred work).
91. **Taps with a selection:** inside it, the row shows or hides; elsewhere in the box, the selection goes and the
    caret is placed (with its handle); outside the box, the handles and the row go and the box keeps its selection,
    as for a mouse click outside.
92. **A long press or double-tap takes the focus**: an editable box brings the phone keyboard up as a tap does; a
    read-only one doesn't.
93. **Scrolling** (a finger pan in the box or a ScrollViewer round it): handles follow their ends; the row hides
    and comes back about ¼ s after the scrolling stops (the platform clock).
94. **A Menu as a row is `Menu.orientation Horizontal`** (inherited from StackPanel, public, for any flyout): an
    item shows its text only (its icon when it has no text), no shortcut text; a checked item keeps its check mark;
    separators are thin upright lines; ←/→ move in place of ↑/↓.
95. **An app's `contextMenu` on a TextBox, by touch:** a Menu is shown as a row with all its items; any other view
    as it is, above the selection.
96. **A row wider than the window** falls back to the vertical Menu in the same place. Considered: Android's
    overflow item.
97. **After a command:** Cut and Paste leave a caret (row and selection handles go, the caret handle shows); Copy
    closes the row and keeps the selection with its handles; Select all keeps the row open with what now applies.
98. **Password boxes:** a long press or double-tap selects everything (no word boundaries); the row has Paste and
    Select all only.
99. **Handles:** a filled teardrop in the accent colour, about 20 DIPs across, below the line (the start's leans
    left, the end's right, the caret's is centred); the area a finger grabs is 44 DIPs and wins over the text
    under it.
100. **`Element.longPress`**, an element part: the host tells the element under a long press first; when it marks
     the press handled, no context menu opens and that element keeps the touch (the drag by words), else 9f's
     behaviour. For element authors; no `onLongPress` setting for apps yet.
101. **During a composition**, a long press, double-tap or handle drag commits it first, as a tap does (9d).
102. **A second finger** in the box is ignored while one drags a handle or extends a selection.

GLOSSARY: **Selection handle** added; **Context menu** says a finger shows a TextBox's as a row.

Status: **agreed (Tim, 2026-11-18); built the same day; waiting for Tim's check on a phone.**

**As built (2026-11-18):**

- **A layer an element asks for** (`IElementRoot.ElementLayer(element, content)`, `ElementHost`'s "Layers
  elements ask for"). The host makes a layer above the flyouts and below the toasts (modals, flyouts, these,
  toasts: `Int32.MaxValue - 6` to `- 3`), which never takes the keyboard and is styled as if inside its element,
  as a flyout is. `content` is asked for the view before every frame, just before that layer is laid out (the
  element's own layer is below, so laid out already): the handles stand where the frame being drawn puts the
  text, with no frame's lag and no frame added. It answers the same View while nothing changed (nothing is
  patched), and null when there is nothing to show: the layer goes. It also goes when its element leaves its tree
  or a modal opens above it. A press in such a layer closes no flyout.
- **The row is not a flyout.** Decision 80 holds (it is the box's context menu: same commands, same labels, an
  app's `contextMenu` in its place), but it stands in the box's own layer beside the handles, in the box a Flyout
  has (so the theme's look), placed by `SelectionLayer.place`. A flyout would have taken the focus and the
  keyboard's layer when it opened, and a phone's keyboard would have gone down with every long press; a press
  outside it would have been swallowed (decision 91 wants a tap elsewhere in the box to place the caret). One
  consequence the other way from 9f's context menu: a `theme` or style set around the box does reach the row.
- **`Element.longPress`** (decision 100) and, new with it, **`Element.pointerCanceled`**: the element that holds
  a press hears when it is taken away (the system took the touch; a context menu opened), so a drag ends
  instead of waiting for a release that never comes. The TextBox and the handles use it. The host asks the
  layer's router first (`InputRouter.LongPress`: the types under the finger, deepest first); unhandled, 9f's
  context menu opens as before.
- **A pen's long press now opens context menus**, as 9f's notes said it did: the host timed fingers only. In a
  TextBox a pen gets the menu as a menu and no handles (decision 81).
- **`SelectionHandle`** (`Elements/Controls/SelectionHandle.fs`), a public element type so `SelectionHandle.style`
  can recolour it; apps don't make one. The start's and end's are a 20 DIP circle with one corner left square (a
  rounded rectangle with three round corners); the caret's is a drop drawn from an icon path. The box a finger
  takes one by is 44 × 44 DIPs but starts only 4 DIPs above the handle's point and hangs below the line: centred
  on the drawing, as first built, it covered the lower half of the text line for 32 DIPs beside the selection, and
  a tap there (to place the caret) took the handle instead. So "wins over the text under it" (decision 99) is true
  of the lines below and of those 4 DIPs.
- **`Menu.orientation Horizontal`** (decision 94) as decided; a row menu also wants `Menu.minWidth 0.0`, because a
  Menu's least width (160) is a type default. Separators stand upright through a local the Menu sets on them.
  The TextBox gives its row's items more padding (14 × 11: about 41 DIPs high) through a style on the row's box.
- **Where the row sits** (decision 86): 8 DIPs above the selection; on the first line that shows, above the
  box's own edge (it stood on the box's border otherwise); below, 8 DIPs under the handles.
- **A row too wide** (decision 96) is found when the layer arranges it: it is kept out of sight for that frame
  and the box shows a menu from the next.
- **"On a word"** (decisions 82 and 83) is: the finger is within 16 DIPs of a caret place on a line of text and
  the character under it is not white space. The character under the finger, not the nearest caret place, picks
  the word (a finger on the right half of a word's last letter selects that word; a mouse's double-click still
  takes the nearest place, as 9a built it).
- **What puts handles and row away:** a key, typed text, a composition starting, a mouse or pen press in the
  box, and the focus leaving (a tap anywhere else in the box's layer). The caret's handle shows only in a box
  that can change. Dragging the caret's handle leaves the row as it was; a tap on that handle shows or hides it.
- **The row hides** while a handle is held, and for a quarter of a second after the text last scrolled or the
  box last moved in the window (a scroller around it; also a phone's keyboard coming up).
- **`render`'s scrolling was moved** into `scrollLine` and `scrollLines`, called by `render` and by the layer's
  content just before it: same arithmetic, same pictures (the 102 older screenshots are unchanged).
- **Tests:** 17 unit tests (`TextBoxTouchTests.fs`: long press on a word and on blank space, dragging on by
  words, double-tap, taps inside and outside the selection, the caret's handle and its four seconds, dragging
  each handle and crossing the ends, the row's four commands, password and read-only, what puts them away, a
  second finger, an app's menu as the row, the too-wide row, scrolling inside and around the box, a pen, the
  row's placement, a Menu as a row); four screenshots (`textbox-touch@1x`, `-light@1x`, `@2x`,
  `textbox-touch-caret@1x`). One 9d test moved its finger away from the caret's new handle. Totals: 1,281 unit
  (+7 timing), 106 rendering, 7 timing.
- **The keyboard on a long press** (Tim on his phone, 2026-11-19: it closed and reopened). Not measured, reasoned: a
  long press moves the DOM focus from the hidden text field to the canvas with no `mousedown` to prevent (a tap has
  one), and the next frame's `setTextInput` took it back, the keyboard going down and up between. Now the field
  takes the focus back at once in its own `blur` (unless the blur is ours or the page lost the focus), and the
  canvas hands it over in its `focus`, while text is wanted. Checked in a desktop browser (focus stays with the
  field; typing and leaving the box work), and by Tim on his phone the same day: the keyboard stays up.
- **Not built:** a magnifier (decision 90); the row's show and hide are not animated; selection by touch in a
  TextBlock (decision 78).
- **Not checked by a test:** all of it under a real finger: whether 44 DIPs is easy to take, whether the handles
  sit well under the text, the row's place over a phone's keyboard, and whether a browser's own selection menu
  or magnifier appears over ours.

## Platform layer: SDL3 and AccessKit (planning round, 2026-11-18)

Status: **agreed (Tim, 2026-11-18: "yes to all, do the spike now")**. Decision 11 changes: the spike comes now, before M8 step 4b. The spike is built (as built below); Win32 stays the default until Tim judges it.

Tim asked for opinions on AccessKit and SDL3, then for this round. He would like to **get rid of the Win32
platform if SDL3 covers what we need**, and noted that SDL3 has console ports under NDA. Both libraries break the
rule "borrowed: WebGPU and HarfBuzz only" (Part 1, "What Scribble is"), so the first decision is the rule itself.

### What was found (2026-11-18)

**SDL3**
- **Consoles:** SDL3 runs on Switch (and Switch 2), PS4 and PS5 through separate, NDA-only repositories. They are free
  under zlib to developers with the console maker's NDA, and commercial games ship on them. Xbox goes through
  Microsoft's GDK, which is in SDL's public repository. Sources: the SDL wiki's
  [SupportedPlatforms](https://wiki.libsdl.org/SDL3/SupportedPlatforms), [README-switch](https://wiki.libsdl.org/SDL3/README-switch),
  [README-ps5](https://wiki.libsdl.org/SDL3/README-ps5/raw) and [README-gdk](https://wiki.libsdl.org/SDL3/README-gdk).
- **.NET on consoles:** FNA, a C# game framework on SDL, ships on Xbox and Switch **through NativeAOT** from its
  public branch, with no console-specific code of its own: the platform code is in SDL and NativeAOT. PlayStation is
  in progress or just working; the sources disagree
  ([FNA on Consoles](https://fna-xna.github.io/docs/appendix/Appendix-B:-FNA-on-Consoles)). So NativeAOT F# on SDL on a
  console is a proven path for everything except rendering.
- **Rendering on consoles is the gap.** wgpu has no console backends. SDL's own GPU API (SDL_GPU) has a
  "private" shader format for NDA platforms, but which console backends exist isn't public
  ([SDL_GPU](https://wiki.libsdl.org/SDL3/CategoryGPU)).
- **Live resize on Windows:** SDL sends `SDL_EVENT_WINDOW_EXPOSED` during the modal size-move loop. Its `data1` is
  1 for a live-resize expose, and the event can be drawn from an event watcher. With SDL's main callbacks,
  `SDL_AppIterate` keeps running while Windows moves or resizes the window. Sources:
  [SDL_events.h](https://skia.googlesource.com/external/github.com/libsdl-org/SDL/+/b3dca03ebf7bee554eb93010ced563f8748a3ad0/include/SDL3/SDL_events.h),
  [AppFreezeDuringDrag](https://wiki.libsdl.org/SDL3/AppFreezeDuringDrag),
  [the modal-loop patch](https://discourse.libsdl.org/t/sdl-allow-the-application-to-draw-while-windows-is-in-a-modal-move-resize-loop/47255).
  Not yet known: how this compares with our own one-frame-per-refresh handling (section "Live resize lag").

**Our own code**
- **DirectComposition presentation doesn't depend on Win32.** The G-SYNC fix (section "Cursor stutter with
  G-SYNC") is wgpu's `Dx12SwapchainKind.DxgiFromVisual`, set in `WebGpuDevice.fs` and handed an HWND. SDL3 gives us
  the HWND (`SDL_PROP_WINDOW_WIN32_HWND_POINTER`), so the fix should carry over unchanged.
- **The Win32 platform is about 1,000 lines** (`Native.fs`, `Win32Platform.fs`, `Win32Window.fs`). It has:
  - windows: per-monitor DPI, `AdjustWindowRectExForDpi`, owned tool windows (`WindowOptions.Owner`);
  - our live-resize pacing: `WM_ENTERSIZEMOVE`, the refresh rate read from the monitor, a size-move timer;
  - mouse pointers: capture, leave tracking, wheel and horizontal wheel. It has no touch or pen, since it doesn't use
    `WM_POINTER`;
  - keys and `WM_CHAR` text, with no IME;
  - system and image cursors, made per DPI;
  - `Wake` and `After` through a hidden window's timer.
- **SDL3 has an equivalent for each:** windows with display scale; `SDL_SetWindowParent` and utility windows for
  owned windows; pointer capture; mouse, touch and pen events; keyboard and text input with IME and
  `SDL_SetTextInputArea`; system cursors and `SDL_CreateColorCursor`; `SDL_PushEvent` to wake a waiting loop.
  It also has hit testing for custom title bars (M7c) and the clipboard. Whether each one behaves as well as ours is
  what the spike (decision 4) is for.
- **Tests don't touch the platform:** unit tests use the fake platform, and screenshot tests render offscreen. Both
  are unaffected.

**AccessKit**
- AccessKit takes tree updates (ids, roles, names, states, bounds) and calls back with actions. It has adapters for
  Windows UI Automation, macOS and Linux AT-SPI, a C API (accesskit-c), and an SDL example. It is still at 0.x
  versions. On Windows it can attach to a window it doesn't own by subclassing it, which suits an SDL window.
- **Doing it ourselves would be hard:** UI Automation is COM, whose NativeAOT support needs a C#-only source
  generator; AT-SPI needs D-Bus; macOS needs Objective-C protocols.

### Decisions

1. **The borrowing rule.** Change "Borrowed: WebGPU and HarfBuzz" to also borrow platform integration: **SDL3**
   (windows, input, text input, cursors, clipboard, app lifecycle) and **AccessKit** (screen readers). Rendering,
   layout, text, styles and controls stay ours. The reason: windowing on five or more platforms, two of them under
   NDA, is not where Scribble's value is. FNA, egui and Bevy draw the same line.
   **Recommendation: yes.**

2. **SDL3 replaces Win32 on Windows, if the spike passes.** One desktop platform (`Scribble.Platform.SDL3`) for
   Windows, macOS, Linux, and later phones and consoles. If every pass criterion in decision 4 holds, delete
   `Scribble.Platform.Win32`; git keeps it. Keeping it as a fallback would mean two platforms to keep equal on
   Windows, and only one of them tested.
   **Recommendation: yes. Delete Win32 once the spike passes; if a criterion fails, we decide then whether to fix it
   in SDL, work around it, or keep Win32.**

3. **The browser keeps its own platform.** SDL's browser port is built on Emscripten, which doesn't fit .NET's
   WebAssembly. `Scribble.Platform.Browser` stays, and screen readers there get our own ARIA bridge.
   **Recommendation: yes.**

4. **A spike first, on Windows, with written pass criteria.** Build `Scribble.Platform.SDL3` behind `IPlatform` and
   run the Gallery, Hello and the popped-out inspector on it. Measure each criterion against Win32 on your machine:
   - **Power rule:** idle draws zero frames (counted) and uses about zero CPU. While something animates, at most one
     frame per refresh.
   - **Live resize:** at most one frame per refresh, the content keeps up with the edge, no lag at the start of a
     drag, and no flicker on the other monitor with a game running.
   - **G-SYNC:** the cursor is smooth over still content, so DirectComposition still applies.
   - **DPI:** right size and scale at creation, and when dragged between monitors of different DPI.
   - **Pointers:** capture while dragging outside the window, leave, the wheel (high-resolution and sideways), and
     touch and pen, which are new.
   - **Keyboard:** every `Key`, repeat, Alt and F10 not eaten by the system menu, typed characters including emoji
     and dead keys, and focus in and out.
   - **Cursors:** every system cursor, and image cursors sharp at 100, 150 and 200 percent.
   - **Windows:** the owned inspector window stays above its owner, has no taskbar button, and closes with it.
     Also the title, and what the frame looks like with the dark theme.
   - **Shipping:** NativeAOT publish with `SDL3.dll` beside the exe, startup time, and the size added.
   - **Tests:** all unit, rendering and timing tests unchanged.
   **Recommendation: yes. You judge the spike on your machine; I report the counts and timings.**

5. **Binding SDL3: our own F# P/Invoke,** only the functions we use, with `DllImport`, blittable signatures, and
   struct layouts checked against the headers, as with wgpu. Not the C# binding packages: F# only, and less to depend
   on. **Recommendation: yes.**

6. **Getting SDL3: download official release binaries** on first build into `native/`, at a pinned version (the
   latest stable 3.x at the time), as `build/WgpuNative.targets` does. Windows x64 and arm64 first, macOS with the Mac
   build. Linux (distribution package or our own build) is decided with the Linux build.
   **Recommendation: yes.**

7. **The event loop stays ours.** `IPlatform.Run` waits with `SDL_WaitEventTimeout` until the next `After`
   deadline. `Wake` pushes a user event, which is thread-safe and wakes the wait. `After` keeps its own queue rather
   than SDL timer threads. Live resize runs through an event watch on the live-resize expose events, keeping our
   one-frame-per-refresh pacing. SDL's main-callback style (`SDL_AppIterate`) waits until iOS needs it.
   **Recommendation: yes.**

8. **IME goes in with M9 TextBox, on SDL** (`SDL_StartTextInput` while a text box has focus,
   `SDL_SetTextInputArea` for the candidate window). This is a reason to do the spike **before M9**: Win32 then never
   gets an IME of its own. **Recommendation: yes.**

9. **Consoles: record the path, decide nothing yet.** SDL3 plus NativeAOT covers windows, input and lifecycle there
   (FNA shows it). Rendering is the open question, with three answers for when a console is in reach:
   - wgpu gains console backends, which we don't control;
   - a second renderer on SDL_GPU, whose console backends are private. It would need our WGSL shaders translated and
     a check that the pixels match;
   - our own console backend.
   Console code is under NDA, so it can't live in Scribble's public repository: like FNA and SDL, console support
   would be private repositories for licensed developers. Gamepad input and focus navigation come with game UI
   hosts. **Recommendation: record it as above; revisit when you have a devkit.**

10. **AccessKit, after the spike, on the SDL window.** An opt-in project (name to settle: `Scribble.Accessibility`)
    that turns the semantic tree into AccessKit updates and AccessKit's actions into `node.Perform`, through the
    dispatcher, since actions arrive on the screen reader's thread. Windows first, tested with Narrator and NVDA.
    Change notifications (round "The semantics stage") come with it. Doing it after the spike means wiring it to one
    platform once, not to Win32 and then again to SDL.
    **Recommendation: yes. The binary comes from accesskit-c releases, pinned, in the same way as SDL3.**

11. **Order:** finish M8 step 4 (4b to 4d), then the SDL3 spike and the switch, then M9 TextBox (with IME), then
    AccessKit; M10 hot reload as planned. Alternatively, do the spike now, before 4b, if you'd rather know early.
    **Recommendation: after M8 step 4. The controls don't depend on the platform, and the spike shouldn't sit beside
    half-built controls.**

### Not decided here
- **The project name:** `Scribble.Platform.SDL3` versus `Scribble.Platform.Desktop`. I'd name it for what it is.
- **Single-window platforms:** what `SupportsMultipleWindows` means on phones and consoles. Decided with those
  platforms.
- **Whether `Scribble.Desktop` changes:** it picks the platform and the renderer, so it would change only to create
  the SDL platform.

**As built: the spike (2026-11-18).** `src/Scribble.Platform.SDL3` (about 850 lines: `Native.fs`, `SdlWindow.fs`,
`SdlPlatform.fs`) implements `IPlatform` and `IWindow` on SDL 3.4.20. `build/Sdl3.targets` downloads the official
Windows x64 zip into `native/`, as wgpu-native's target does. `App.run` uses it when `SCRIBBLE_PLATFORM=sdl`; Win32
stays the default until you judge the spike. Rider has "Gallery.Desktop (SDL)" and "Hello.Desktop (SDL)" run
configurations.

How it works:
- **SDL's events are handled as they happen,** in an event filter, like a Win32 window procedure, and are dropped
  from SDL's queue. The queue carries only one event of ours. It wakes the loop for `Wake`, a new earliest timer, or
  a frame requested from inside the filter.
- **The loop** pumps SDL, delivers due frames (vsync paces it), and otherwise blocks in `SDL_WaitEventTimeout` until
  the next timer.
- **Live resize:** while Windows runs its modal size-move loop, SDL's 10 ms timer sends live-resize expose events
  (read in SDL's source: `WM_ENTERSIZEMOVE` → `SetTimer` → `SDL_OnWindowLiveResizeUpdate`). On each one we run what
  our loop would: the wake handler, due timers, due frames. Resizes draw at most once per display refresh, as on
  Win32. `LiveResize true` comes with the first tick, and `false` when our loop runs again.
- **Wakes from another thread** push our event from a thread-pool item. SDL holds its event-watch lock while it
  runs the filter, and this way an image decoder or a task never waits on the UI thread.
- **Owned windows** use `SDL_SetWindowParent`, which sets the Win32 owner. SDL destroys them with their owner and
  sends each its DESTROYED event.
- **Cursors:** image cursors are made per scale from straight-alpha pixels, the 32 most recent kept, as on Win32.
  SDL's cursor belongs to the app, not the window, so a window shows its cursor while the pointer is over it.
- **Touch and pen** come through SDL's mouse events, as `Touch` and `Pen` pointer kinds. Multi-touch waits for the
  phone port.
- **Text input** is on for every window, which also turns on the IME. M9 turns it on and off with focus.
- **Hints:** the screensaver allowed (SDL turns it off by default); Alt+Space opens the system menu (SDL blocks it
  by default).
- **The surface** is still `Win32Handle` (HWND from SDL's window properties), so D3D12 keeps presenting through
  DirectComposition.

Measured here (Release NativeAOT Gallery, 3 runs each, 96 DPI monitor):
- **Window up:** about 0.5 s on both platforms.
- **CPU over 5 s idle:** 0–16 ms on SDL, 16–63 ms on Win32. Both are at the clock's resolution: idle on both.
- **Working set:** about 168 MB on SDL, 171 MB on Win32.
- **Size:** `SDL3.dll` adds 2.8 MB; the exe grew by 72 KB.
- **Same window:** client 720×560 on both.
- **Scripted:** a resize by `SetWindowPos` redraws at the new size, and `WM_CLOSE` closes the app.
- **Tests:** unit (878 + 6 skipped), rendering (76) and timing (6) unchanged. The browser build is unaffected.

Not verified here (your machine, the checklist): every pass criterion that needs real input or hardware. That is
live resize by dragging, G-SYNC, DPI changes, keyboard and text (a posted key doesn't reach SDL without keyboard
focus, which a script can't take), touch and pen, image cursors at each scale, and the inspector's popped-out window.

**Keyboard, tested with real input (2026-11-18, Tim focusing the window, keys sent by `SendInput`):**
- **On SDL, all of these work:**
  - Tab and Shift+Tab move focus: the theme switch, the tab row, then the buttons, and back.
  - Space and Enter press a focused button.
  - The Right arrow on the tab row switches the tab, and the title follows.
  - F12 opens the inspector.
  - Ctrl+Shift+C starts pick mode, and Escape stops it.
  - Alt+Space opens the system menu.
  - Alt+F4 closes the window.
- **Alt+F4 straight after closing the system menu with Escape** missed once when scripted (keys a few hundred ms
  apart), while Win32 closed. With a real keyboard Tim closed the SDL window with Alt+F4 on the first try: a
  scripting artefact, not a difference.
- **Alt or F10 alone, then Down or Space** (Tim): neither Win32 nor SDL opens the system menu, so neither goes into menu
  mode; the next key reaches the app on both. No keyboard difference is left.

**Live resize, dragged by Tim (2026-11-18):** fine on SDL, no lag, with the game running on the other monitor and no
flicker on the game's monitor.
**G-SYNC (Tim, 2026-11-18):** the cursor is smooth over still content with the game running: DirectComposition
presentation carries over to SDL's window, as expected.
**Idle (Tim, 2026-11-18):** 0% CPU and 0% GPU in Task Manager. Animations are smooth and drop back to 0% when they finish:
the power rule holds on SDL.
**Window states (Tim, 2026-11-18):** maximize, snap, minimize and restore all redraw at once, at the right size.
**Owned window (Tim, 2026-11-18):** the popped-out inspector stays above the Gallery, has no taskbar button,
minimizes and restores with it, pops back in, and closes with it (`SDL_SetWindowParent`).
**Also checked by Tim (2026-11-18):** mouse, wheel and cursors good; title and title-bar colour right. His monitors
share one scale, so a DPI change across monitors wasn't exercised. One difference: SDL opens the window centred on
the screen, where Win32 let Windows place it (cascaded).

**Verdict and switch (Tim, 2026-11-18: "yes, make SDL the only platform and delete Win32").** Tim checked the rest
himself:
- the AOT exe runs on SDL;
- a held key repeats.

He can't test these on this PC: touch and pen, another keyboard layout, an IME, monitors with different scaling.

As built:
- `src/Scribble.Platform.Win32` is deleted; git keeps it.
- `App.run` creates `SdlPlatform`. `SCRIBBLE_PLATFORM` and the "(SDL)" run configurations are gone.
- Comments in the SDL platform no longer point at Win32 code.
- **Docs:**
  - Part 1 ("What Scribble is", Projects, Next), CLAUDE.md and the README describe SDL3 as the desktop platform and
    the new borrowing rule.
  - The README also says how to get SDL3 by hand if the download fails.
- **One thing that isn't SDL's:** the samples are console apps (`OutputType Exe`), so an AOT sample opens a console
  window too. It did on Win32 as well.

## AccessKit: the screen-reader bridge (grilling, 2026-11-19)

Status: **agreed (Tim, 2026-11-19: yes to all, on the condition that the power rule holds; see "Power" below). A1
is built (as built below) and waits for Tim's check with Narrator and NVDA; A2 and A3 are not built.** It carries
out decision 10 of "Platform layer: SDL3 and AccessKit".

Facts at the start. accesskit-c 0.23.2 (2026-11-01) is the latest release: one zip of 68 MB for every platform (the
Windows DLL's size is not known yet). On Windows AccessKit can subclass a window it doesn't own; that adapter must be
made on the window's thread before the window is first shown (it panics on a visible one), which our SDL window
allows (made hidden, shown later). AccessKit asks for the tree only when an assistive technology connects, and an
update call does nothing until then. The call for the tree comes on the window's thread; actions may come on another.
It wants a stable 64-bit id for each node; elements have none. A text box a screen reader can follow needs "text
run" nodes with each character's length and place, and the selection. It has live regions (polite, assertive). The
window's HWND is already in `IWindow.Surface` (`Win32Handle`). The semantic tree already leaves out the layers
below an open modal. Our semantic actions are nine; AccessKit has more (set a value, set the text selection, show
the context menu).

**Decisions**
1. **The project is `Scribble.Accessibility.AccessKit`**, as `Scribble.Text.HarfBuzz` is named: our concept, then
   the library. (`Scribble.Accessibility` is the namespace of the semantics types in Core.) The browser's would be
   `Scribble.Accessibility.Browser`.
2. **On by default on the desktop.** `Scribble.Desktop` references the bridge and `App.run` attaches it;
   `App.screenReader false` leaves it out. This reverses decision 10's "opt-in": an app whose author forgot would be
   unusable with a screen reader and nobody would notice, and AccessKit does nothing until something connects. The
   cost is one more native DLL beside the exe.
3. **Three steps, Windows only, checked with Narrator and NVDA.** **A1, the bridge:** bindings, the adapter on the
   SDL window, the whole semantic tree (roles, names, states, bounds, focus), actions through `node.Perform`,
   updates on change. **A2, text:** a TextBox's text runs, caret and selection. **A3, announcements and
   leftovers:** toasts as live regions, a ComboBox's value, a keyboard way to a toast's action, and the actions
   left out of A1. macOS and Linux adapters come with those platforms.
4. **Updates are the whole tree, after a frame in which a layer changed, only while something is connected.**
   AccessKit compares it with the last and raises the events. The tree holds realized elements only. Measured in
   A1 on the Gallery and a 10,000-item list; partial updates (change tracking in Core) only if the numbers ask.
5. **The translation is apart from the native calls:** our semantic tree to AccessKit's roles, states, ids and
   actions is plain F#, unit-tested on the fake platform; the native layer under it is thin. What a screen reader
   says is for the checklist.
6. **"Screen-reader bridge"** is the term (GLOSSARY); "adapter" is AccessKit's word for its own per-platform part.
7. **Every window with a host gets a bridge,** the inspector's pop-out too.
8. **Each element gets an id when it is made; a recycled list item gets a new one when it shows another item.** To
   a screen reader, one item leaving and another arriving are two things, not one whose name changed.
9. **The glossary's "never per frame" goes:** the semantic tree is built only while something asks for it (a
   tool, a test, a connected screen reader).
10. **At most one update per 100 ms while changes keep coming:** the first after a quiet spell goes at once, and a
    last one when they stop. A change of focus always goes at once.
11. **A1 maps our nine actions.** Setting the text selection and a text box's value come with A2; "show the
    context menu" and setting a slider's value with A3 (each is new Core surface).
12. **The focused node** is the focused element's; without one, that of the nearest element around it that has
    one; else the window. Only the layer with the keyboard counts.
13. **Roles map to the closest of AccessKit's,** the table written here with A1. Of note: a TextBox is a text
    input, a multi-line text input or a password input by its settings; a MenuItem that can be checked is a menu
    item check box; Text is a label; the window is a root node of role window, named by its title, holding the
    layers' nodes in layer order.
14. **Without the native library the app runs with no bridge,** and says so once through `DebugWarnings`.
15. **A custom host attaches a bridge by one public function** of the new project (a host in, something to dispose
    out), to be called before the window is first shown.
16. **A screen reader sees only realized list items** (each says "40 of 10,000"): a known limit, as in browsers'
    virtual lists. Realizing on a screen reader's request is a feature for later.

**Power** (Tim, 2026-11-19: "it seems like all of these questions are going to violate our lower power consumption
rule"). They don't, and A1 has to show it:
- **No frames are added, ever.** The bridge draws nothing and asks for no frame.
- **Nothing connected: no work.** No tree is built, no timer runs. To check in A1: whether anything on a plain
  Windows 11 machine connects by itself (UI Automation clients are not only screen readers), and how often.
- **Connected and idle: no work.** Updates follow "a layer changed"; an idle window changes nothing.
- **Connected and changing: work, capped** (decision 10): a tree build at most every 100 ms, with one timer wake
  to send the last. A blinking caret is two changes a second for its ten seconds.
- **A1's report gives:** updates sent and trees built while idle (must be 0), with nothing connected (0), and per
  second under an animation (at most 10); the time one tree build takes on the Gallery and on 10,000 items.

### A1 as built (2026-11-19)

- **`Scribble.Accessibility.AccessKit`**: `Native.fs` (the 40 or so functions used, `DllImport`, blittable; enums
  are bytes; strings go as UTF-8 with a length), `Translation.fs` (a semantic tree to `AkUpdate`, plain data, and
  `UpdatePacer`: no native calls), `Bridge.fs` (`AccessKitBridge.attach host`: writes an `AkUpdate` out call by
  call, owns the adapter and the two callbacks). `build/AccessKit.targets` downloads the release zip once and
  unpacks only the Windows x64 DLL, the header and the licences. `accesskit.dll` is 351 KB beside the exe.
- **Core's seam** (`Hosting/ScreenReader.fs`): two app values, `ScreenReader.enabled` (`App.screenReader` starts
  it) and `ScreenReader.bridge` (what makes a bridge for a host; none: the platform has no bridge), and
  `ScreenReader.attach host`. Scribble.Desktop's `App.run` puts the AccessKit bridge in the second and attaches
  in `App.onStart`, which runs before the window is first shown (AccessKit's rule). The inspector's pop-out
  window calls `ScreenReader.attach (appHost, itsHost)` (decision 7). Core and the inspector know nothing of
  AccessKit.
- **Ids** (decision 8): `SemanticNode.Id`. An element gets a number the first time a semantic tree asks for it (an
  element nobody asks costs nothing); an ItemsView counts each time it takes an element from its pool to show
  another item; a node's id is the element's number and the sum of those counts on the way down to it, so what
  is inside a recycled item changes id with it. 1 is the window.
- **`SemanticNode.IsMultiline`** and `Semantics.isMultiline` are new (a TextBox's role depends on it), and
  `SemanticTree.NodeAround element` (decision 12).
- **Roles** (decision 13), ours to AccessKit's: Button → button; CheckBox → check box; Switch → switch;
  RadioButton → radio button; RadioGroup → radio group; Slider → slider; ComboBox → combo box; TextBox → password
  input if protected, multi-line text input if several lines, else text input; Tab, TabList, TabPanel → the
  same; List → list box when its items can be selected, else list; ListItem → list box option when it can be
  selected, else list item; Menu → menu; MenuItem → menu item check box when it can be checked, else menu item;
  ScrollArea → scroll view (clips its children, with its scroll offsets); Image → image; Group → group; Dialog →
  dialog (modal); Alert → alert; the window → window. Two without a straight match: **Text → label, with its
  text as the node's value**, because AccessKit reads a label's text from its value (found through UI
  Automation: with only a label set, every text was nameless); **Separator → splitter**, since AccessKit has no
  separator and its splitter is what platforms show as one.
- **Actions** (decision 11): Click, Focus, Increment, Decrement, ScrollIntoView, Expand and Collapse map to
  AccessKit's of the same names; ScrollForward and ScrollBackward to its scroll down and scroll up. An action
  comes on any thread, is posted to the dispatcher, and is performed only if the node of the last tree sent has
  it.
- **Updates** (decisions 4 and 10): the bridge listens to the host's `LayerChanged` (marks it dirty) and
  `FrameDrawn` (sends, paced). AccessKit calls back for the tree on the window's thread, the first time something
  asks and from `update_if_active` after. Until that first call the bridge does nothing after a frame: no
  pacer, no timer.
- **Bounds** are the semantic node's, in physical pixels in the window; AccessKit turns them into screen
  coordinates. Visual transforms aren't applied (as in the semantic tree).
- **`SCRIBBLE_LOG_ACCESSIBILITY=1`** writes when something first asks, and each tree built.
- **Measured (the power checks above), on Tim's machine, the Gallery:**
  - Nothing connected by itself in 30 s of an idle window, twice (20 s and 30 s). Zero trees built.
  - With a UI Automation client connected and the window idle: 0 trees in 5 s, twice.
  - Two button presses through UI Automation built 4 trees (each press: one at once, one for its animation's
    end).
  - One tree, built and translated (Release, without the native calls): the Gallery's Controls page 0.26 ms
    (54 nodes); a 10,000-item list 0.07 ms (13 nodes: only realized items).
  - The pacer under a 60 Hz animation for a second: 9 or 10 updates (unit test).
  - Not measured: the native calls' own cost per tree, and a blinking caret with a screen reader connected
    (two trees a second for its ten seconds, by the numbers above about half a millisecond a second).
- **Checked end to end through UI Automation** (a PowerShell script as the client, Debug and the NativeAOT
  publish): the window and its title, text with its words, buttons (one disabled), tabs, check boxes (on, off,
  mixed), bounds; pressing a button (the count went up) and selecting a tab (the page changed). Sliders were
  not found by control type in that script; not looked into (for Tim's pass, or A3).
- **Tests:** 10 unit tests and one timing test (`AccessKitTests.fs`: the root and bounds, roles and states, a
  selectable list, realized items only, ids and recycling, focus, actions, the pacer, how a window gets its
  bridge and `App.screenReader false`, a bridge that can't start). Totals: 1,291 unit (+8 timing), 106 rendering,
  8 timing.
- **Not built in A1** (as decided): a TextBox's text runs, caret and selection (A2); live regions, a ComboBox's
  value, set-value and show-context-menu actions (A3); macOS, Linux, the browser.
- **Tim's first pass (2026-11-19):** Narrator's blue focus box lands on each control as Tab moves, so the bridge
  reaches it; Narrator itself speaks in no app on his machine (Windows 11 Pro N; cause not found), so nothing has
  been heard yet. A listener on UI Automation's events sees the focus and property changes arrive; it saw no
  focus event for the first change of focus while the window was coming to the front (not looked into).
  Left for later: a pass with NVDA and its Speech Viewer, or Narrator once it speaks.
- **Not checked by a test or by script:** what Narrator and NVDA say, their own navigation (item by item, by
  control type), focus following in a real screen reader, and whether anything on another Windows machine
  connects by itself.

### A2: a TextBox's text (planned 2026-11-19)

Facts at the start. AccessKit wants a text box's text as "text run" nodes under it, one for each line as drawn,
holding the line's text; for each character (what the editor selects as one) its length in bytes; where each
word starts (it won't guess: they must be the editor's own); and, optionally, each character's place and
width. A hard line break belongs to the end of its line and counts as one character. The selection is an anchor
and a focus, each a run and a character in it. Three actions come back: set the selection, replace the selected
text, set the value. A run's word starts are bytes, so a run holds at most 255 characters: a longer line is
several runs, linked as being on one line. Our TextBox holds all of it (lines as wrapped, caret places, words,
the selection); none of it is in the semantic node.

**Decisions (Tim, 2026-11-19: yes to all)**
17. **The text description is in Core's semantic node** (`SemanticNode.Text`): its lines, each line's characters,
    their places, its word starts, and the selection. The TextBox fills it in, as an element type of an app's own
    that edits text could. The browser's bridge will want the same.
18. **Every line up to 1,000; beyond that, the 1,000 around the caret.** Updates are whole trees (decision 4), so a
    10,000-line box would be 10,000 nodes more with every key. In a longer text "read to the end" stops at the
    cap until the caret moves on. If that annoys, the answer is partial updates, a step of its own.
19. **A password gives one bullet for each character**, never its text.
20. **While an input method composes, the lines are what is drawn**, the composing text included.
21. **Each character's place and width are given**, so a screen reader's highlight and a magnifier can follow the
    caret.
22. **Three semantic actions more, each with a value:** set the text selection, replace the selected text, set
    the value. Each takes the path the matching input takes (a replace is as pasted text: `maxLength`, nothing
    in a read-only box, one undo step, `onTextChanged`).
23. **An update equal to the last one sent isn't sent** (a blinking caret changes nothing a screen reader is
    told). The tree is still built; the send is what is saved.

Status: **agreed (Tim, 2026-11-19); built the same day; not yet heard through a screen reader.**

**As built (2026-11-19):**

- **Core** (`Semantics.fs`): `SemanticTextLine` (its number, text, whether a break ends it, where each character
  starts, each one's x, the characters that start a word, its bounds), `SemanticTextPosition` (a line and a
  character in it), `SemanticTextInfo` (the lines and the selection's anchor and caret), and
  `SemanticNode.Text`. An element type gives it with `Semantics.textFrom`. Places are counted in characters (what
  the caret steps over as one), so a password's bullets and an emoji need no special case downstream.
- **Actions with something to work with** (decision 22): `SemanticAction.SetTextSelection`,
  `ReplaceSelectedText` and `SetValue`; `SemanticArgument` (`Text`, `TextSelection`); `node.Perform(action,
  argument)`; an element type performs them in `Semantics.onActionWith`. The nine older actions are as they were.
- **The TextBox** gives its lines from what it already keeps (`semanticText`): several lines from the wrapped
  paragraphs, one line from what it draws. `TextEdit.characters` finds a line's characters and word starts in one
  pass. Its three actions go through `apply`, `applyEdit` and `inserted`, as a drag and a paste do; a
  composition is committed first.
- **The bridge** (`Translation.fs`): each line becomes one or more text runs (role text run; the text, with
  "\n" for the break; each character's bytes in UTF-8; word starts; places and widths in pixels from the run's
  left edge), children of the text box's node after its own. A run's id is made from its text box's element, its
  line's number and its part of the line, so an action's place (a run and a character) maps back with no table
  (`Translation.placeOf`). **A line of more than 255 characters is several runs**, linked as previous and next on
  the line: AccessKit keeps a run's word starts in bytes. A read-only box is not said to support replacing or
  setting text.
- **Decision 23** (`Bridge.fs`): the tree is built as plain data (`AkUpdate`), compared with the last one given
  to AccessKit, and written out only if it differs. `AccessKitBridge.UpdatesSent` counts those written.
- **Found while checking, and left as it is for now: AccessKit hides what a scroll area clips.** With "clips its
  children" set on a scroll area (A1), AccessKit leaves out the children that are scrolled out of view, all but
  the first on each side, which can be asked to scroll into view. So UI Automation saw 30 of the Controls page's
  81 nodes, and A1's "sliders not found" was this: they were below the fold. It is AccessKit's design (made for
  long lists), and a screen reader that walks item by item brings the next one in; one that reads the whole page
  at once stops at the fold. The other way is not to say that scroll areas clip, which shows everything, off
  screen or not. **To decide with Tim, with a screen reader that speaks.**
- **Measured** (Release, our side only, without the native calls): the Gallery's Controls page 0.32 ms (70 nodes,
  its text boxes' runs among them); a text box of 3,000 lines, 1,000 given, 2.1 ms (1,003 nodes; 16.7 ms before
  `TextEdit.characters`). With a text box focused and its caret blinking, 0 updates were sent in 5 s.
- **Checked through UI Automation's text interface** (a script as the client): the three text boxes on the
  Controls page, their text (a password's is empty); the first character's rectangle inside the box; the first
  word; the selection; `Select` on a range (the box's selection followed); `SetValue` (the text changed).
- **Tests:** 9 unit tests and one timing test more in `AccessKitTests.fs` (a line as a run with bytes, places
  and word starts; several lines and the break; a password; a line over 255 characters; the cap; a composition;
  the three actions; a read-only box; a blinking caret). Totals: 1,300 unit (+9 timing), 106 rendering, 9 timing.
- **Not checked:** what a screen reader says as the caret moves; dictation itself; the native calls' cost for a
  thousand runs; right-to-left text (runs are always said to run left to right).

## M10: hot reload (planning round, 2026-11-19)

Status: **round 1 agreed (Tim, 2026-11-19: "I agree with the recommendations"); nothing built. To be built in a
fresh session.** The questions under "Still to ask" come before the code.

Facts at the start.
- **F# has no hot reload of its own.** `dotnet watch` restarts the process on any `.fs` edit (.NET 8, 9 and 10, by
  the newest report found). Compiler work exists as a draft pull request behind a test flag
  ([dotnet/fsharp#19941](https://github.com/dotnet/fsharp/pull/19941)); the SDK's side is an open issue
  ([dotnet/sdk#55464](https://github.com/dotnet/sdk/issues/55464)). The sources disagree on how far a prototype
  got. Not something to wait for.
- **So it is ours to build, as the first plan said** (Part 2, "Tooling and testing"): recompile the app's code,
  load it into a collectible `AssemblyLoadContext`, run the view again, and let the differ patch the elements.
- **The samples are shaped for it already:** the app is a library project (`samples/Gallery`, with
  `GalleryApp.app`, a list of app settings) and the exe (`samples/Gallery.Desktop`) is a few lines calling
  `App.run`.
- **A rebuild takes 2.0 s:** `dotnet build samples/Gallery/Gallery.fsproj --no-restore --no-dependencies` after a
  one-line edit, three times, on Tim's machine (2,000 lines of F#).
- Waiting since the project review (decision 4, engineering rule 6): what an app registers (custom visual
  states) belongs to its host, not the process, so it goes with the app's code. "Decided now, built with M10."

**Decisions, round 1**
1. **Recompile by running `dotnet build` on the app's project,** in the background. No new dependency, and the
   compiler and flags of a real build. Considered: FSharp.Compiler.Service inside the dev host (nearer a second
   once warm, but tens of megabytes whose version must follow the SDK's). It can take `dotnet build`'s place
   later behind the same seam if two seconds feels slow.
2. **A dev host tool apart from the app:** `dotnet run --project tools/Scribble.Dev -- samples/Gallery`. It builds
   that project, loads its DLL into a collectible context, starts the app, watches the sources and swaps on each
   good build. The app's own exe project doesn't change, and nothing of hot reload is in a shipped or AOT build.
   Considered: a package the app's exe references and switches on.
3. **The tool finds the app by its type:** the one public value in the library that is a list of `AppSetting`.
   With none or several it lists what it found and asks for `--app Some.Module.app`. No attribute, no
   registration in app code.
4. **Step M10a: what is on screen survives; the model starts again.** Kept across a swap: the window, its size and
   place, the element tree (patched by the differ), so scroll positions, focus and text typed into boxes that keep
   their own. Reset: the Elmish model (the new code's `init` runs) and components' hook state, because their
   types belong to the code that was unloaded. **Step M10b,** with a round of its own: carrying the model and hook
   state across, by copying values of the same shape into the new types.
5. **A build that fails keeps the app running** and shows the compiler's errors (file, line, message) in a layer
   over it and in the console; they go with the next good build.
6. **Watched: the app's project and the projects it references, except Scribble's own** (for the Gallery:
   `samples/Gallery` and `samples/Samples.Shared`). Scribble's assemblies are loaded once by the tool; an edit to
   the framework itself still means a restart.

**Still to ask (round 2, before building M10a)**
- What a swap does to what the old code left running: tasks (`useAsync`, `Cmd.ofTask`), timers and
  subscriptions, and handlers the elements still hold until the differ rebinds them.
- Moving the custom visual-state registry from the process to the host (rule 6), and what else is static and
  keyed on app types (43 `static let` or `static val mutable` in Core to go through).
- What the old context needs before it can unload (everything of the old code let go), and whether to check
  that it did.
- How the swap is tested: two builds of a small app loaded in turn on the fake platform; what is for the
  checklist.
- Layers the app adds (`App.layer`), `App.onStart` handlers (the inspector) and app values across a swap.
- Where the tool lives for an app outside this repository (a `dotnet tool` later).

## Publishing: GitHub and NuGet (planned and built 2026-11-19)

Status: **agreed (Tim, 2026-11-19: "Accept all"); the files are built and the consumer test passes on Tim's
machine and on GitHub. Public since 2026-11-19 at https://github.com/timothythompson89/Scribble; the first CI run
passed. Released 2026-11-20: `0.1.0-alpha.1` and `0.1.0-alpha.2`, all 17 packages on nuget.org, pushed by the release workflow through
trusted publishing.** Nothing here is left to do.

Tim asked whether enough is finished to publish. The answer given: enough for a public repository now; for NuGet
an alpha, once the packages carry what an app needs. Two stages.

Facts at the start.
- **History is clean:** 96 commits in five days, one author, no keys, certificates or logs ever committed.
- **`master` and the three `m5`/`m6` branches** are all behind `m7-styles` with nothing of their own.
- **Licence files:** Inter, Twemoji, harfbuzzjs and PngSuite had theirs; Material Symbols had none, and nothing
  covered the native libraries.
- **`dotnet pack` worked but the desktop package was hollow:** right dependencies, no `wgpu_native.dll`,
  `SDL3.dll` or `accesskit.dll` (the download targets only run inside this repository), and no version, licence,
  readme or tags on any package.
- **The libraries' `IsAotCompatible` and `IsTrimmable` never applied:** they sat in `Directory.Build.props` under
  `'$(IsScribbleLibrary)' == 'true'`, which is read before a project sets that property. Found here by asking
  MSBuild for the values.

**Decisions, stage 1 (GitHub)**
1. **Keep the whole history** (not one squashed commit): it is clean, and this document refers to it by date.
2. **Commit email:** rewrite to a GitHub noreply address before the first push if Tim minds his address being
   public; it can't be done after others clone. *Done 2026-11-19 (Tim: "Yes, rewrite"): author and committer of all
   97 commits are his GitHub noreply address (`git filter-branch --env-filter`); trees, dates and messages are
   unchanged, every hash is new, and the four hashes this document quotes were updated. The old history stays
   under the local tags `backup/main` and `backup/publishing` and `refs/original/` until Tim deletes them: never
   push those (`git push --tags` would).*
3. **One default branch, `main`:** `master` is fast-forwarded to the current commit and renamed, the old milestone
   branches go, and work happens on short-lived branches.
4. **CI on GitHub's Windows runners: build, unit tests, the consumer test.** Rendering tests stay out at first:
   their references are exact D3D12 pictures from Tim's GPU, and the lavapipe comparison (project review,
   decision 13) already differed. This revises project review decision 19 (which planned them on a software GPU);
   trying WARP is a later experiment.
5. **Issues open; pull requests not taken yet** (`CONTRIBUTING.md` says why). No code of conduct or templates yet.
6. **`CLAUDE.md`, `docs/agents/` and `.run/` stay public;** `.claude/` is git-ignored.

**Decisions, stage 2 (NuGet)**
7. **A native library goes in the package of the project that binds it,** under `runtimes/win-x64/native`: wgpu in
   `Scribble.Rendering.WebGPU`, SDL3 in `Scribble.Platform.SDL3`, AccessKit in `Scribble.Accessibility.AccessKit`.
   Considered: separate `Scribble.Native.*` packages (this can become that later without breaking apps, when
   macOS and Linux make packages large); shipping the download targets (builds would need the network).
8. **One version for all 17 packages, starting `0.1.0-alpha.1`,** set in `Directory.Build.props`; every break is
   noted in `CHANGELOG.md` while the version starts with 0.
9. **A consumer test is the gate for every release:** fresh apps that know Scribble only as packages.
10. **Releases are pushed by a workflow on a `v*` tag** with NuGet's trusted publishing (no stored key). The first
    alpha by hand is fine if the account setup drags.
11. **SourceLink and `.snupkg`,** so an app's debugger steps into Scribble.
12. **The alpha comes before M10,** not after (the first answer said after; packing turned out half done already,
    the work touches no Core code, it claims the ids and makes the README's first example true). M10 ships in
    `alpha.2`.

**As built**
- `Directory.Build.props`: version, MIT licence expression, tags, copyright, SourceLink settings, symbols.
  New `Directory.Build.targets` (read after each project): the AOT flags, now applied; `IsPackable` false for
  everything that isn't a library; the package readme (`src/PACKAGE.md`) and `THIRD-PARTY-NOTICES.md` into every
  package. The whole solution still builds with no warnings with the AOT flags on.
- `build/*.targets` have a pack-only mode (`ScribbleNativePackOnly`), set by the three binding projects: fetch
  the library, put it in the package, don't copy it to the output. Inside the repository `Scribble.Desktop` still
  copies them to apps, as before. Sizes: wgpu 9.1 MB (3.5 MB packed), SDL3 2.8 MB, AccessKit 0.4 MB.
- Licences: `MaterialSymbols-LICENSE.txt` (Apache 2.0) added and packed; SDL's and AccessKit's licence files are
  packed from their downloads; `THIRD-PARTY-NOTICES.md` lists everything with version, package and licence.
  wgpu-native's release zip holds no licence file, so the notices link to its repository.
- `tools/TestPackages.fsx`, the consumer test: rebuilds and packs (17 `.nupkg` and 17 `.snupkg`, counted), then
  in a temporary folder outside the repository, with a package cache of its own: a desktop app (the README's
  counter) referencing only `Scribble.Desktop` builds, has the four native libraries, runs, draws and closes
  itself; the same published with NativeAOT; a browser app referencing only `Scribble.Browser` publishes with
  Scribble's default page. `--no-run` starts nothing; `--version` tests the packages a release will push.
- **Learned: a change of version alone needs a full rebuild.** An incremental `dotnet pack -p:Version=...` left
  an assembly compiled against the old version of another ("Could not load Scribble.Platform, Version=0.1.0.0"
  in the packaged app). The script builds with `--no-incremental` before packing.
- `.github/workflows/ci.yml` (push to `main`, pull requests) and `release.yml` (a `v*` tag: version from the tag,
  unit tests, the consumer test on the packages it then pushes). Both fail if a hang dump was written.
- **Departure from decision 9:** in the workflows the consumer test runs with `--no-run`. A hosted runner has no
  GPU to count on, and a first run that fails for that reason would say nothing about the packages. The full
  script is run on Tim's machine before a tag (`release.yml` says so at its top).
- README: pre-release notice, status to M9, packages paragraph, licences; its "What to check" list moved to
  `docs/what-to-check.md` (project review, decision 18). New: `CONTRIBUTING.md`, `CHANGELOG.md`.
- **Pushed and CI green (2026-11-19).** GitHub had made the repository with an initial commit of its own (a short
  README and a LICENSE, authored with the address decision 2 removed, no history in common); Tim chose to replace
  it, so `main` was force-pushed over it, pinned to that commit. CI run 1 passed in 11 minutes: `wasm-tools` 4,
  build 2.5, unit tests 16 s, the consumer test 3.7 (pack, NativeAOT publish and browser publish all work on a
  hosted runner; apps not started). Packages link to the repository (`PackageProjectUrl`; the repository element
  comes from the remote).
- **CI names the tests that fail (2026-11-20).** Run 2 failed in the unit tests on a commit that changed a
  document and one metadata line, and the log is shown only to signed-in users. Both workflows now write a
  `.trx` and run `.github/report-test-results.ps1`: an error annotation per failed test (name, message, the
  first lines of the stack) and totals in the job summary, both public; a run that wrote no results, or half a
  file, is reported as that. Tried on a passing file, a failing one and a truncated one. The failure itself
  did not show in 40 Release runs on Tim's machine.
- **The test that failed, found by that reporting in run 4:** `the Gallery's Images page finds its embedded pictures
  and draws every one` ("Expected: 13, Actual: 1"). The image cache is one per process and the Gallery's pictures
  have fixed keys: when another test drawing the Gallery on another thread (decoding in the background) had
  started them, this test's inline decoding had nothing to do and its one frame came too early. On Tim's machine
  the background decodes end before the first frame even when forced, so it never showed there. Fixed in the
  test: it draws again until the 13 are there (5 s at most). A cache per tree for tests would make it
  deterministic; that is a Core change, not made.
- **The avatars test's own race, found and ended 2026-11-20** (after image state went per app, it still timed out
  about one Release run in five, also on untouched code: "downloads running 6, waiting 8"). No website is asked:
  a fake handler holds every request open. The test waited for exactly 12 requests after a jump to row 9,000.
  The jump lays the list out in two passes, the first realizing a few rows more than the second keeps, and a
  cancelled download gives its place back on a thread-pool thread. When that thread is quick, a row of the first
  pass gets the place, is asked for, and is cancelled as the second pass lets it go (seen: rows 0–5, then 9000,
  8991, …; 8991 cancelled): 13 requests, so "= 12" was never true and the wait ran out. The product is right
  (a request near the target, cancelled at once); the test asserted more than is promised. It now waits for six
  requests open (asked and not cancelled) with the first six cancelled, and checks that six downloads run and
  that every other request is within 20 rows of 9,000. The hook's 10,000-row test read "running" after waiting
  for a count of requests, the same mistake in a smaller window: it waits for six open too. 60 Release runs of
  the whole suite in a row passed after the change.
- **It failed again in CI run 17, the same day, and that showed two more things.** The message was a timeout in
  the scene's teardown ("downloads running 3, waiting 0"), which waited 5 s for the cancelled downloads to hand
  their places back and asserted it. (a) **The teardown was hiding the test's own failure**: an exception from
  `Dispose` replaces the one the body threw. With the teardown's check gone the test failed fast, 3 runs in 25,
  on its own assertion: the fix above had kept "every request after the first six is near 9,000", and a row of
  the first screen still waiting for a place (6, seen) can get one for a moment as the rows before it are let
  go one by one. It now says what holds: the six open requests are near 9,000; anything else asked for on the
  way was a first-screen row or one near 9,000 and was cancelled; nothing between was asked. (b) **The 5 s were
  too short for a hosted runner**, where three of six cancellations had come back by then. Not reproduced on
  Tim's machine (limited to 1, 2 and 4 processors, 35 runs; and a temporary report of any teardown slower than
  half a second stayed empty over 50 runs), so the cause is inferred: xUnit runs test bodies on the thread
  pool, as many as there are processors, which is also how many threads the pool starts without waiting, so a
  continuation queues behind them and gets a thread only as the pool adds one. Done for it
  (`tests/Scribble.Tests/ImageSetup.fs`): the pool's minimum is raised to 32 for the test process; waits in
  real time have 30 s (`RealTime.patience`: a wait ends the moment its condition holds, so a passing test pays
  nothing); a timeout's message says what the pool was doing (`RealTime.poolNow`), so the next one is not a
  guess. The scenes' teardown cancels and checks nothing (each scene's cache is its own since image state went
  per app, so no other test needs its places); that places do come back is held by one test (`a download
  nothing shows any more is cancelled ...`). 70 Release runs in a row passed after these changes.
- **A second test failed the same way in run 7:** `10,000 rows of downloaded avatars ...` timed out waiting for 6
  requests ("downloads running 6, waiting 9"). The HTTP handler and the six download places are also one per
  process, and the Gallery's Images page has two pictures from real URLs: a test drawing the Gallery at the same
  moment takes two places (and asks the real network when no fake handler is set). Interim fix (Tim,
  2026-11-20): the eight test modules that draw the Gallery or use the shared image state are one xUnit
  collection, `Shared image state`, so they run one after another (ImageView, ImageLoading, ImageCursor,
  GalleryPages, AppValue, Idle, Inspector, Semantics; the run still takes 6 s). **Still open:** a download a
  finished test left running keeps its place, so this narrows the race and may not end it; the cure is image
  state that belongs to a tree or host, a design round of its own (with the cache per tree above), which would
  also keep unit tests off the real network. **Done the same day:** section "Image state per app, not per process".
- **The first release run failed at the push (2026-11-20), with nothing published:** on Windows `dotnet nuget push`
  finds no files for a pattern written with forward slashes. The pattern now has backslashes, and a rehearsal
  step pushes the same pattern into a folder and counts 17 before the sign-in. Tests, pack, the consumer test
  and the trusted-publishing sign-in had all passed. The tag `v0.1.0-alpha.1` was moved to the fixed commit.
- **Released 2026-11-20: `0.1.0-alpha.1`, all 17 packages on nuget.org.** The second release run passed every
  step; NuGet's index showed all 17 about six minutes after the push. Checked from outside: an app in an empty
  folder, with nuget.org as its only source, restored `Scribble.Desktop` and built with the four native
  libraries in its output (built, not run).
- **Released 2026-11-20: `0.1.0-alpha.2`** (`ctx.useImage`; pictures belong to the app). The tag alone did it all:
  tests, pack, the consumer test, the push to NuGet and, on its first run, the GitHub release step. Before the
  tag: the full consumer test at that version, 1,351 unit tests and 107 rendering tests on Tim's machine.
  NuGet's version lists caught up one package at a time (16 of 17 for a few minutes, and two answers that
  disagreed in between: its servers don't update together). Then an app in an empty folder, with nuget.org as
  its only source, restored `Scribble.Desktop` 0.1.0-alpha.2 and built a component using `ctx.useImage` and
  `load.Image`, with the four native libraries in its output (built, not run).
- **Released 2026-11-20: `0.1.0-alpha.3`** (ListBox scrolls on its own and has a frame; section "ListBox scrolls
  on its own"). Before the tag: the full consumer test at that version, 1,360 unit tests in Release and 107
  rendering tests on Tim's machine, and CI run 15 green on the code. Release run 4 did the rest. NuGet listed
  none of the 17 for some minutes after the workflow ended, then all. An app in an empty folder, with nuget.org
  as its only source, restored `Scribble.Desktop` 0.1.0-alpha.3 and built a framed ListBox of 10,000 items with
  no ScrollViewer around it (`maxHeight`, `cornerRadius`, `layout`), with the four native libraries in its
  output (built, not run). The docs-only push of the morning (`c6c0754`) started no CI run: the `paths` filter
  below works.
- **The Gallery on GitHub Pages (grilling, 2026-11-20).** Tim: a push should put the browser Gallery on a GitHub
  page. Decided (yes to all nine):
  1. **A `pages` job in `ci.yml`, after the build-and-test job**, not a workflow of its own: only tested code
     goes live, and a push that breaks the tests leaves the last good page up. CI's `paths` filter applies.
  2. **The page shows `main`**, so it can be ahead of the packages on NuGet.
  3. **Built with WebAssembly AOT**: the public page is where speed is judged. The job's time and the
     download's size are measured on the first run; interpreted is the fallback if the time is unreasonable.
  4. **The Gallery alone, at the root** (https://timothythompson89.github.io/Scribble/). The other samples can
     go in sub-folders later without moving it.
  5. **A link near the top of the README**, saying it needs WebGPU.
  6. **Which build it is goes in the title**, on the desktop too ("Gallery – Components · 0.1.0-alpha.3
     (6378526)"), and nothing is drawn in the app: the Gallery's pages are in the screenshot tests, and a commit
     drawn on them would change every picture with every commit. Read from the assembly's informational
     version, which SourceLink already ends with the commit.
  7. **A readable notice where there is no WebGPU**, in Scribble's default page, so every browser app has it:
     centred text in place of a black page with a red box (which stays for real errors). The runtime isn't
     started there, so nothing is downloaded to no purpose.
  8. **A failed publish fails the CI run**, the tests' own result still showing on their job.
  9. **A push to `main` or a manual run on `main` publishes**; pull requests and other branches never do.

  Tim's one step: repository Settings, Pages, Source "GitHub Actions". Until then the job fails.

  As built: the `pages` job (`ci.yml`) publishes `samples/Gallery.Browser` with `-p:BrowserAot=true`, writes the
  page's size to the run's summary and deploys `wwwroot` with GitHub's `upload-pages-artifact` and
  `deploy-pages`. No `.nojekyll` (said to be needed in the planning): with "GitHub Actions" as the source, Pages
  serves the uploaded files as they are and Jekyll never runs. `GalleryApp.buildSuffix` reads the assembly's
  informational version; the title test holds its form. The default page (`build/page/main.js`) shows the
  notice for no WebGPU and for a page that isn't secure, and imports the runtime only when it can run. Seen in
  the browser pane on the development server: the Gallery runs, its tab reads "Gallery – Components ·
  0.1.0-alpha.3 (6534fef)", and the notice shows when WebGPU is taken away.

  **First deploy: CI run 18 (2026-11-20, commit `8f6c984`).** The whole run took 13 min 49 s: build and tests
  7 min 30 s, then `pages` 6 min 10 s, of which the AOT publish was 4 min 25 s and installing the WebAssembly
  tools 1 min 17 s. AOT stays (decision 3): the time is reasonable. The page at its real address, opened in the
  browser pane: it runs from the sub-folder with no error, the tab reads "Gallery – Components · 0.1.0-alpha.3
  (8f6c984)", and the Lists page shows the framed ListBoxes. A first visit downloads 6.8 MB (19.7 MB once
  unpacked) in 30 files; the largest is `dotnet.native.wasm`, 4.2 MB sent for 13.2 MB. (Run 17, the first with
  the job, failed in the tests and published nothing: see the avatars test above.)
- **CI skips documentation (Tim, 2026-11-20).** `ci.yml` has a `paths` filter: a push or pull request that changes
  only `docs/`, `CLAUDE.md` or `LICENSE` starts no run. `docs/PACKAGE.md` and `docs/THIRD-PARTY-NOTICES.md` still
  do: they are packed into every package. Several of the day's 11-minute runs were for DESIGN.md alone. Seen
  working only once pushed: the commit adding it changes a workflow, so it runs; the next documentation-only
  push is the test.
- **GitHub releases (Tim, 2026-11-20).** `.github/github-release.ps1` publishes a release for a tag with that
  version's section of `docs/CHANGELOG.md` as its notes (a pre-release when the version has a hyphen).
  `release.yml` runs it last, after the packages are on NuGet; `github-release.yml` runs it by hand for a tag
  that exists already (Actions, "GitHub release", Run workflow), which is how `v0.1.0-alpha.1` gets its own. The
  notes were tried locally (`-NotesOnly`). The by-hand workflow ran once, from `main`, and published
  "Scribble 0.1.0-alpha.1" as a pre-release (a first try was refused by GitHub before it started, most likely
  with the tag chosen under "Use workflow from": the workflow isn't in the tag's commit). The step in
  `release.yml` has not run: the next tag is its first.
- **The markdown files moved into `docs/` (Tim, 2026-11-20):** README, CHANGELOG, CONTRIBUTING, GLOSSARY,
  THIRD-PARTY-NOTICES and the package readme (`docs/PACKAGE.md`, from `src/`). GitHub shows a README and a
  CONTRIBUTING file from `docs/` as it does from the root. Two stay at the root: `LICENSE` (GitHub reads the
  licence there) and `CLAUDE.md` (Claude Code loads project instructions only from the root; in `docs/` they
  would silently stop applying). Older sections of this document name the files where they were.
- **Not verified:** `wasm-tools` is installed in both because Tim's machine has
  it and a browser publish without it wasn't tried; trusted publishing's setup; that the ids are still free on
  nuget.org (not queried).

**Tim's part, in order**
1. ~~Say which GitHub account and repository name~~ (done: `timothythompson89/Scribble`).
2. ~~Create the repository, push `main`, check the CI run~~ (done 2026-11-19, green).
3. Done 2026-11-20. On nuget.org: create the account, add a trusted publishing policy for the repository and `release.yml`, and
   put the profile name in the repository secret `NUGET_USER`. Apply for the `Scribble.` prefix if wanted
   (project review, decision 16: likely refused, not needed).
4. Done 2026-11-20 (the tag was moved once, after the first run failed at the push). `dotnet fsi tools/TestPackages.fsx --version 0.1.0-alpha.1`, then tag `v0.1.0-alpha.1` and push the tag.

## Image state per app, not per process (planning round, 2026-11-20)

Status: **agreed (Tim, 2026-11-20: "Accept all"), answering his "how do we make our library not share process wide image
state"; built the same day (as built, below); checked by Tim on the desktop, 2026-11-20 ("images can be checked
off").**

Why now. Two unit tests failed on GitHub's runners and never on Tim's machine (section "Publishing: GitHub and
NuGet", as built): each assumed it had the process's image state to itself. The interim fix runs eight test
modules one after another; it narrows the race and leaves unit tests able to reach the real network. M10's round 2
already lists going through Core's statics (engineering rule 6), and this is part of that work.

What is process-wide today.

| State | Where | Who uses it |
| --- | --- | --- |
| The cache and its 32 MB pool (`ImageCache.Shared`) | `ImageCache.fs` | 14 places in `Element.fs`, 1 in `ViewImage.fs` |
| The download queue and its six places | inside the cache | the same |
| The HTTP client, the tests' handler, the timeout and the base URL (`ImageHttp`) | `Image.fs` | the cache; `Scribble.Browser` sets the base URL |
| Image cursors (`ImageCursors`) | `Cursor.fs` | `Cursor.fs` (5 places), `Input.fs` (1) |
| "Decode inline" for tests (`ImageCache.DecodeInlineOnThisThread`) | thread-static | tests only |

The first three rows were read closely; how the image cursors' registry works was not, and is to be read before
that part is built.

The shape. One internal `ImageServices` object holds the cache, the download queue, the HTTP client and the decode
mode. An app makes one, and every host, window and layer of that app shares it. `IElementRoot` gains an `Images`
member beside `AppValues`, and the 15 places change from `ImageCache.Shared` to the root's. An `Image` value stays
what it is (a key and a source), so no app code changes and nothing in the public API breaks.

**Decisions (Tim accepted every recommendation)**
1. **Scope: one per app.** Considered: one per tree (the same picture decoded once for each layer and window) and
   one per process (today). A bare `ElementTree` makes its own, as it does for app values.
2. **Reached through the root** (`IElementRoot.Images`). Considered: handing it to each element, which touches far
   more code.
3. **Everything in the table moves together,** the HTTP client and the six download places included: moving the
   cache alone would leave the avatars test's failure where it is.
4. **"Decode inline" becomes a setting of the service,** in place of the thread-static flag: a test makes a service
   that decodes inline, and nothing depends on which thread a test runs on.
5. **Each test gets a service of its own,** with a handler that fails any request it wasn't told to answer, so
   unit tests can't reach the real network. The `Shared image state` collection and the Gallery test's waiting
   loop come out. A new test holds the rule: two trees showing a picture with the same key don't see each other's
   decodes or downloads.
6. **When: now, as the first piece of M10's audit of statics.** It also ends the CI failures properly.

**Costs and what isn't known**
- Two apps in one process each have a 32 MB pool. That happens in the hot-reload dev host and in tests.
- `ViewImage.render` and `DesktopApp.headless` need a service too; `Headless.start` would own one.
- Image cursors may be awkward where a cursor is looked up with no root in reach (`Input.fs`): to be reported
  before that part is built.
- Drawing isn't touched, so the screenshot references shouldn't change; the 107 rendering tests are run to confirm.

**As built (2026-11-20)**
- **The cache is the service: no `ImageServices` wrapper.** `ImageCache` already held the pool and the download
  queue; it now also owns its `ImageHttp` (client, the tests' handler, timeout: an instance, no longer statics)
  and its decode mode (`DecodeInline`, in place of the thread-static flag). `ImageCache.Shared` is gone.
- **It hangs on the app values, and elements reach it through their root's** (`root.AppValues.Images`), not a new
  `IElementRoot.Images` (a change to decision 2's wording): the interface is public and the cache internal, and
  the app values are already the one object an app's windows, layers and trees share, with a bare tree owning
  its own. So no host, layer or `App.start` code changed: what shares app values shares pictures. It is made on
  first use.
- **A hold remembers its cache** (`ImageHold.Cache`): a picture is asked about and given back where it was taken,
  whatever tree the element is in by then.
- **A download function is given the cache's `ImageHttp`** (`ImageSource.Url`'s `fetch`), so `Image.fromUrl`
  still is the only thing that names `HttpClient` (the NativeAOT Counter, which downloads nothing, grew 5 KB, from
  8,847,360 bytes to 8,852,480; linking `HttpClient` would add 2.4 MB).
- **Rendering to an image:** inside an app, a render's tree takes the app's values and so its pictures; in a
  tool the headless environment owns one cache (`RenderEnvironment.Images`) for every render.
- **What stays the process's, on purpose:**
  - `ImageHttp.BaseUrl`, the page's address for relative URLs: a browser tab has one page whatever runs in it.
  - `ImageHttp.Detached`, for `Image.toPng` of a URL image: a public call with no app in reach. It caches nothing
    and has no places to wait for.
  - `ImageCursors`, read before building as promised: it is a weak table from a cursor value to what it was made
    from (its picture and options), not cache state, and stays. One thing in it is shared: `spec.Decoded`, the
    decoded picture an element found ready. Two apps in one process using the *same* cursor value would both
    see it; left, and noted for M10's audit.
  - `ImageCache.ConfigureNew`, a default every new cache is given first: how the tests say "inline, no network"
    once for their process. Nothing sets it in an app.
- **Tests:** `tests/Scribble.Tests/ImageSetup.fs` sets that default from an xUnit test framework class (the one
  place that runs once per test process): every cache decodes inline and has a handler, `NoNetwork`, that refuses
  each request by name. A test that downloads gives its tree's cache a handler (`tree.Images.Http.Handler`). The
  `Shared image state` collection and the Gallery test's waiting loop are gone. New tests: a tree's cache is its
  app values' and two bare trees share none; the same picture in two trees is decoded by each; one tree's
  downloads take none of another's six places (the CI failure, as a test); a new cache in the unit tests decodes
  inline and refuses the network. The rendering tests set inline decoding on their own tree's or host's cache
  and are otherwise as they were (their collection also keeps the process's render environment to one class).
- **Results:** 1,340 unit tests (4 new) and 9 timing tests skipped; 107 rendering tests, no reference changed; the
  consumer test passes (17 packages, the desktop and NativeAOT apps run); the 9 timing tests pass in Release
  (three runs of three; the first run after the build failed one, the PNG decoder's, which this work does not
  touch: a cold run).
- **Not verified:** the browser (relative URLs, downloads through `fetch`): a line waits in
  `docs/CHECKLIST-BROWSER.md`. Two apps in one process outside tests (M10's dev host will be the first).

## A hook for a picture's load state: `ctx.useImage` (planning round, 2026-11-20)

Status: **agreed (Tim, 2026-11-20: "Accept all"); built the same day; checked by Tim on the desktop, 2026-11-20
("images can be checked off")** (as built, below), after the per-app image work (section "Image state per app, not
per process") was committed (decision 7). Its browser checks wait in `docs/CHECKLIST-BROWSER.md`.

Tim asked whether images should work through a hook. The answer given: not as how pictures are shown (the
isolation the tests lacked comes from where the cache lives, not from a hook; a hook per row would make virtualized
lists heavier; image cursors, `ViewImage.render` and an element type's own render stage have no component to call
one from; and `ImageView.image picture` is the settings-list shape everything else has), but yes as a way for a
component to *read* where a picture stands. This is what M8 step 3b's decision 17 left for later ("placeholders
and `onLoaded` when an app needs them").

```fsharp
let avatar = Component.create "Avatar" (fun ctx (url: string) ->
    let picture = Image.fromUrl url []
    let load = ctx.useImage picture
    match load.Status with
    | ImageStatus.Ready -> ImageView.create [ ImageView.image picture ]
    | ImageStatus.Loading -> Border.create [ Border.background Palette.control ]
    | ImageStatus.Failed _ -> Button.create [ Button.text "Retry"; Button.onClick (fun _ -> load.Retry()) ])
```

Facts at the start.
- Apps can't see load state today: `ImageView` has no events, and the public `ImageStatus` (loading, ready, failed)
  is read only by the inspector (`Element.HeldImages`).
- A component reaches its app's image cache through its tree's app values (the per-app work).
- A failed picture leaves the cache at once and is tried again only when shown again.

**Decisions (Tim accepted every recommendation)**
1. **The hook holds the picture** for as long as the component lives, and lets go when the component goes or the
   picture changes. Reading alone would never start the load in a view that shows a placeholder *instead of* the
   `ImageView`. A row of a virtualized list holds only while realized.
2. **It returns a small handle, `ImageLoad`:** `Status`, the size in pixels once known (a resource's or bytes'
   header gives it while still loading), and `Retry()`. Considered: `AsyncValue<_>` as `useAsync` gives, and the
   bare `ImageStatus`; neither has room for retry.
3. **A picture held by a hook reports its failure to the hook only,** not also to `App.onError`: the component has
   said it handles it.
4. **`Retry()` is in from the start** (lets go and asks again): a retry button is the main reason to want the
   failed state.
5. **No `ImageView.onLoaded` / `onFailed` in this round.** A plain `App.program` view can't call hooks and would
   need them to dispatch messages; added when an MVU-only app asks.
6. **The name is `ctx.useImage`.**
7. **Built after the per-app image work is committed** (two sessions were in one folder), for `0.1.0-alpha.2`.

**To build:** the hook and its slot (`Component.fs`), with the cache telling the component when loading ends as it
tells elements; the inspector shows the hook's state as it does `useAsync`'s; the Gallery's Images page gets a
placeholder for the web picture and a retry for the 404; README, `CHANGELOG.md` (Added).

**Tests (fake platform):** loading then ready re-runs the component once and nothing draws while idle; a failure
reaches the hook and not `App.onError`; unmounting lets go (holders at zero, a running download cancelled);
changing the picture swaps what is held; retry after a failure asks again and can succeed; 10,000 rows with a hook
each hold only the realized rows.

**Not known yet (at planning):** whether a hook and an `ImageView` holding the same failed picture agree on who
reports (decision 3) whatever the order: to be settled with a test first. The browser (decoding on the UI thread):
for `docs/CHECKLIST-BROWSER.md`.

**As built (2026-11-20)**
- `ImageLoad` (public, `ImageCache.fs`): `Status` (the existing `ImageStatus`), `PixelWidth`, `PixelHeight` (0
  until known), `Retry()`. `ctx.useImage` and its `ImageSlot` are in `Component.fs`. The slot takes the picture
  from its app's cache (`instance.AppValues.Images`), asks to be told when loading ends (through the dispatcher,
  as elements do) and runs the component again then; it lets go when the component goes or asks for another.
- **Who reports (decision 3), settled:** the hook claims the picture's one report when it takes it, on every run.
  A component runs before the elements of its view draw, so in the usual order (the hook, then an `ImageView` of
  the same picture inside it) nothing reaches `App.onError`; a test holds that, beside the same view without the
  hook reporting once. An element *elsewhere* that drew the failed picture before any component asked has
  reported already: both then know, which seemed right.
- **Retry restarts the picture in place** (`ImageCache.Retry`), not "let go and ask again" as decision 4 put it:
  letting go does nothing while an `ImageView` still holds the failed picture, which is the common case (a retry
  button over the picture). The entry goes back to loading and is started again; its holders keep their hold. An
  element that met the failure asks to be told of a retry (`AddRetryWaiter`, in `ReportImageFailure`) and
  measures again when it comes, so it draws the picture when it arrives. Not done for an image cursor (it shows
  the arrow until the pointer next moves).
- **Hidden, the hook lets go** (new: `ComponentState.Hidden`/`Shown`, passed to each `HookSlot`): an element lets
  go of its pictures when it is hidden with what is around it (a page behind another), and a hook that kept
  holding would keep a download's place for something not shown. Drawn again without having run, it takes the
  picture back and runs the component only if the picture now stands differently. This refines decision 1 ("for
  as long as the component lives"): for as long as it is shown. Rows of an `ItemsView` needed nothing: a row with
  a component isn't pooled, it goes, and its hooks with it.
- **`load.Image`** (Tim, 2026-11-20, after asking why not a `ctx.useImageFromUrl`): the handle gives back the
  picture it was asked about, so a view names it once: `let load = ctx.useImage (Image.fromUrl url [])`, then
  `ImageView.image load.Image`. A hook per source was turned down: a URL is one of five sources and load state
  matters for the others too, the view needs the `Image` value anyway, `Image.fromUrl` already carries the
  options, and a picture stays one kind of value however it is used.
- **The inspector** shows it with the other hooks: `useImage url:…`, "loading 172 × 178", "ready …", "failed".
- **The Gallery's Images page, "From a URL":** both pictures go through a `WatchedPicture` component: a box says
  "Loading…", then the picture; the 404 says "Couldn't load" with a Retry button (it fails again: the address is
  wrong), and no longer raises a toast. One reference was regenerated, `images-page@1x`: the section's two lines
  of description changed (looked at: nothing else differs; the pictures stay behind the section's toggle).
- **Results:** 1,351 unit tests (11 new) and 9 timing tests skipped; 107 rendering tests; the 9 timing tests pass
  in Release warm (twice; the first run after the build failed the PNG decoder's, a cold run, as before). Not
  run: the consumer test (no package or build file changed).
- **Tests** (`ImageHookTests.fs`, 11; the eleventh holds `load.Image`): ready at once with its size, one run; loading then ready, with the
  header's size while loading, and no runs while idle; the hook on `Element.Hooks`; a failure to the hook and not
  the error handler; held with no `ImageView` and let go when the component goes (download cancelled, cache
  empty); another picture swaps the hold; retry after a 404 asks again, and when it succeeds the hook is ready
  and the `ImageView` draws; retry on a ready picture does nothing; 10,000 rows hold only the realized rows, also
  after a jump to 9,000; hidden lets go and shown takes back without a run. The 10,000-row test doesn't count
  requests exactly: how many rows are asked for on the way depends on how many frames the list takes to settle.
- README (the hooks list), `CHANGELOG.md` (Unreleased: Added, and the per-app change under Changed).

## ListBox scrolls on its own (grilling, 2026-11-20)

Tim: a ListBox that has to be wrapped in a ScrollViewer is not what anyone expects of a ListBox. Why it was so:
ListBox extended ItemsView (4b, decision 1), and an ItemsView leaves scrolling to the ScrollViewers around it (M8
round 1, decision 2). Two wider designs were talked through and dropped (a public `ListView` inside a scrolling
ListBox; selection moved into ItemsView): Tim wanted only the ListBox to behave as expected.

**Decisions (Tim: yes to all; 8 to be discussed again once built):**
1. **Only ListBox changes.** ItemsView stays the part that is composed (headers that scroll with it, several lists
   in one scroller, sideways scrolling: the inspector's tree, the Icons gallery) and still needs a ScrollViewer.
2. **The scroller inside is a real ScrollViewer element**, so an app's `ScrollViewer.style` reaches a ListBox's bar
   (a style matches a type exactly), and `ScrollViewer.style [ Style.inside ListBox.Type [] ]` styles it alone.
3. **No scroll settings on ListBox yet**: `scroller` and the sizes every element has. `vertical`, `horizontal` and
   `scrollBarSpace` can be added without breaking anything.
4. **A debug warning** when a ListBox of more than 200 items is laid out with unlimited height and so can't
   scroll: it names the fix (`ListBox.maxHeight`).
5. **A plain break in 0.1.0-alpha.3**: no setting to turn the scroller off. The changelog has the migration (take
   the ScrollViewer from around a ListBox).
6. **Screen readers get what a hand-wrapped ListBox gave:** a scroll area holding one list of items. The outer
   element adds no node. Held by a test.
7. **Settings are written on ListBox and go to the part they are about:** size, margin, alignment, `isEnabled`,
   styles, classes and input handlers to the outer element; `source`, the selectors, `layout`, `selected`,
   `selectionMode`, the two events and `Semantics.label` to the list inside.
8. **ListBox stays borderless** (4b, decision 15) in this change. Whether it gets `background`, `border` and
   `cornerRadius` now that it is a box is open: Tim wants to discuss it after the change is built.
   **Settled once built (Tim, 2026-11-20): the settings exist and are off by default.** `ListBox.background`,
   `borderBrush`, `borderThickness`, `cornerRadius` and `padding`; a ListBox with none of them looks as before.
   Over framed by default (as TextBox and ComboBox are, and a WPF ListBox): a list is used without a frame about
   as often as with one (a navigation pane, a flyout's list), ComboBox would have had to turn it off, and adding
   a default later breaks nothing while taking one away would.
   **Its padding is 4 by default (Tim, the same day): "match ComboBox".** 4 is what a flyout keeps round a
   ComboBox's open list, not the 12 × 7 inside a ComboBox's box (with a `ListBoxItem`'s own 12 × 8, text would
   have started 24 in). So a ListBox given a line and corners looks like that open list with nothing more to
   set. It applies with or without a frame: `ListBox.padding 0.0` for a list that goes edge to edge.
   **And framed by default after all (Tim asked again; Claude changed its recommendation; 2026-11-20):** the
   theme's surface behind the items, a line of 1 in the border colour and the theme's corner radius, as a
   ComboBox and a TextBox have, so the three read as one family. What turned it: with the padding of 4 an
   unframed list sat 4 in from nothing; a ListBox with a frame is what people expect (WPF, Avalonia, HTML, and
   both of the Gallery's); and the lists that want none (a navigation pane, a flyout's) are styled on purpose
   anyway: `ListBox.borderThickness 0.0`, `ListBox.background Color.Transparent`, `ListBox.padding 0.0`.
   As built: type defaults on `ListBox.Type`, all theme values. ComboBox gives its flyout's list those three
   settings (the flyout is the frame), and its pictures are unchanged. The Gallery's lists dropped their frame
   settings. Regenerated on purpose, each looked at first: `lists@1x`, `lists-light@1x` (the frame round the
   list) and `gallery-phone-lists@3x` (the surface behind both lists, nothing else). Two tests: the line and
   padding round the scrolling part and the three settings that take the frame away; the frame's four values
   equal a ComboBox's and a TextBox's in both themes.
9. **The list inside is internal and named `ListBoxItems`** (the inspector shows it, with "realized 40 of
   10,000"). It keeps `ListView` and the like free for the day it is made public.
10. **The list screenshots stay byte-identical**: no reference is regenerated; a difference is a bug.

**Thought given up, and wasn't:** a selectable list sharing a scroller with a header above it. Claude said in the
planning that a ListBox inside a ScrollViewer of the app's would realize every item, and that it would come back
as a `ListBox.header` setting or a public `ListBoxItems`. Built, it turned out to need neither (see below).

**As built (2026-11-20):**
- **Three elements.** `ListBox.Type` is now a plain element (`Element.define`, no longer an ItemsView) that holds
  one child it makes itself, as ComboBox and TabControl make theirs: a `ScrollViewer` whose content is a
  `ListBoxItems` (internal; what `ListBox.Type` was: an ItemsView with the `ListBoxItem` container, the keys and
  typeahead). The settings type `ListBox` still inherits `ItemsView`, so every setting kept its name.
- **Settings (decision 7).** At each measure the ListBox builds the view of its parts from its own values and
  patches it: `source`, the selectors, `layout`, the scroller, `selected`, `selectionMode` and `Semantics.label`
  go to the list. Everything else stays where the app wrote it, on the outer element; keys and clicks bubble up
  to its handlers from the items (ComboBox's Tab handler on its flyout's list still hears Tab).
- **The selection lives on the ListBox**, not the list: `ListBoxLogic.owner` goes from the list up two parents to
  the ListBox (told by its `ListBoxParts`), and reads and writes `selected`, the mode and the two handlers there.
  A value changed by a key is so seen by the next key before any layout. The list is given `selected` and the
  mode as well only so that a change lays it out again and a screen reader hears "multi-select".
- **The scroller** is the list's (it scrolls to items): the app's `ListBox.scroller`, else one the ListBox keeps.
  The ListBox registers it under its own `name`, as an ItemsView does.
- **Inside a ScrollViewer of the app's it still virtualizes.** An ItemsView's viewport is what every ScrollViewer
  around it shows, intersected. A ListBox given unlimited height makes its own ScrollViewer as tall as all the
  items, so that one cuts nothing off and the app's outer one decides what shows. So a header above a ListBox
  in one scroller works, the keys move the outer scroller, and code written for alpha.2 (a ListBox wrapped by
  hand) behaves as it did, with one ScrollViewer more than it needs. Decision 5's break is therefore soft: what
  changes for old code is the tree (the items are two levels further in).
- **The warning (decision 4)** is narrower than agreed, for that reason: more than 200 items, unlimited height
  **and no ScrollViewer anywhere around it**. Its key has the ListBox's name, so it is said once per name.
- **Screen readers (decision 6):** the outer element has no role, so it gives no node; the ScrollViewer and the
  list give what they gave. `SemanticTree` knows a ListBox in two places: a `Semantics.label` on it is not the
  "label without a role" mistake (it is passed on), and a `Label` whose target is a ListBox names the list.
- **ComboBox** makes a ListBox with `maxHeight` for its flyout; its own ScrollViewer is gone.
- **Tests** (`ListBoxTests.fs`, 8 new): the three elements, and scrolling in the window's height; the semantic
  tree equal to a `ListBoxItems` wrapped by hand, in Single and Multiple, with the label on the list; a `Label`
  for a ListBox; `ScrollViewer.style` reaching its bar, and `Style.inside ListBox.Type` reaching only its bar;
  the scroller to item 9,000, found by the ListBox's name; `maxHeight` in a StackPanel; 10,000 items under a
  header in an outer ScrollViewer realize under 40, also after End; the warning said for 201 items in a
  StackPanel and for none of four other arrangements. The wrappers came off the existing ListBox, typeahead,
  AccessKit and screenshot tests and the Gallery's Lists page, with no other change to them.
- **The frame (decision 8).** The five settings are declared on `ListBox` and set Border's properties
  (`BorderProps`), which are neutral by default. The element measures and arranges its ScrollViewer inside the
  line and the padding as a Border does (`Border.inset`) and draws with `Border.draw`. The settings type still
  inherits `ItemsView`, not `Border` (Claude had said it would switch): inheriting Border would have offered
  `ListBox.content`, which would put a second child beside the ScrollViewer. **Corners:** a Border doesn't cut
  its content to its rounded corners, and neither does this; with `cornerRadius` give `padding` too, or an
  item's highlight shows past the corner (the Gallery uses 6 and 4). The Gallery's two lists use the settings
  and lost their wrapper Borders: its screenshots are the same bytes. One test (frame off by default; with a
  line of 1 and padding of 4 the ScrollViewer sits 5 in on every side).
- **The padding of 4** is a type default on `ListBox.Type` (a metric, by "Where defaults live"). ComboBox gives
  its flyout's list `ListBox.padding 0.0` (the flyout has the gap), so ComboBox's pictures are unchanged; the
  Gallery's two lists dropped their `padding 4.0` and are unchanged too. **`lists@1x` and `lists-light@1x` were
  regenerated**, setting decision 10 aside for them on purpose: their unframed list sits 4 in on every side,
  and nothing else in them moved (looked at before regenerating). The frame test now holds the 4 and that 0
  takes it away.
- **Results (before the padding default):** 1,360 unit tests and 9 timing tests skipped; 107 rendering tests with **no reference changed**
  (decision 10: every picture, the two of the lists and those with a ComboBox or the Gallery among them, is the same
  bytes). The timing tests pass run on their own; `decoding a 1024 square PNG is fast` read 32 ms against its
  25 ms bound when run among the whole Release suite, twice, and 3 of 3 passed alone (the decoder is untouched).

## A ListBox's arrow keys in wrap and grid layouts (grilling, 2026-11-20)

The keys were written for one column: Up and Down moved by one item, PageUp and PageDown by "items per page"
from the average row height, and Left and Right weren't the list's at all. With `ItemsLayout.wrap` or `grid`
Down went to the item at the right and Left and Right did nothing. No ListBox in the repository had such a
layout, so nothing existing changes.

**Decisions (Tim: yes to all):**
1. **Up and Down move by a row**, staying in the column.
2. **Left and Right move by one item in source order and carry on to the next or previous row** at a row's
   end (over stopping there, as Explorer's icon view does): the items are one sequence that wraps, as text
   does, and Shift+Right extends a selection the way it reads.
3. **Down from the last full row goes to the last item** when nothing is directly below (a short last row);
   Down in the last row and Up in the first do nothing.
4. **PageUp and PageDown move by the whole rows that fit the visible height**, staying in the column, stopping
   at the first or last row with decision 3's rule.
5. **Home and End stay the first and the last item** in every layout.
6. **A stack's Left and Right stay unhandled**, so they still reach the ScrollViewer (sideways scrolling).

Settled by Claude, said to Tim: no remembered column (after Down lands on the last item of a short row, Up goes
straight up from there); a wrap layout's columns are what fits the width now, so a resize changes where a key
lands. Left and Right take the modifiers Up and Down have (Ctrl moves focus only; Shift extends in Multiple).

**As built:** `ItemsView.columns` and `ItemsView.rowsPerPage` (internal) read the layout's cells; one rule in
`ListBoxLogic.keyDown` moves by rows for every layout (with one column it is what the keys did before), and
Left and Right are taken only when there is more than one column. Four tests (`ListBoxTests.fs`): a grid of ten
in four columns (rows, the short last row, no remembered column, Left and Right over a row's end and stopping at
both ends); a wrap of 10,000 in three columns (the page keys by five rows keeping the column, past the top, the
last row holding one item, under 60 realized); Ctrl and Shift in a grid; a stack's Left and Right left alone.
1,365 unit tests, 107 rendering tests, no picture changed. **Not seen in a window:** no sample has a ListBox
with a wrap or grid layout, so how it feels (the focus ring moving over tiles, scrolling as it goes) is
untried; the tests hold where each key lands.

**A Gallery example (grilling, 2026-11-20; Tim: yes to all):** the Lists page's swatches list became tiles
instead of a third list being added (the page fills the window, and a third list would take height from the
long one): `ItemsLayout.wrap`, so the columns follow the window; 24 colours, so there are several rows at any
width and the list scrolls in its 150 of height; a tile is the chip above the name, and the note moved to the
line above ("Picked Lagoon: teal, for calm"); single selection kept by the list, as before. **As built:** cells
of 96 by 64 (104 gave a phone two columns and an empty third; 96 gives three); the section's hint says "Left
and Right too in the tiles"; `gallery-phone-lists@3x` regenerated (the only picture of the page). 1,365 unit
tests, 107 rendering tests. The keys in a window and in the browser are on the checklists.

**Tiles that spread over the width: `ItemsLayout.wrapFill` (grilling, 2026-11-20; Tim: yes to all).** Tim, of
the tiles' empty space at the right: "make the tiles spread over the width". `wrap` has fixed cells and `grid`
a fixed count of columns; neither reflows and fills. Decisions: (1) a new layout beside them, `wrap` unchanged
(the Icons sample uses it for fixed cells); (2) `ItemsLayout.wrapFill minWidth height`: as many cells of at
least `minWidth` as fit, widened equally so the row fills the width, the height fixed; (3) a short last row's
cells keep that width, at the left, so the columns line up; (4) with fewer items than a row holds the cells
are still that width (it follows the list's width alone: a filtered list's tiles don't change size, one item
is not a tile as wide as the list); (5) the Icons sample stays on `wrap` for now. Settled by Claude, said to
Tim: the arrow keys need no new rule; with unlimited width it flows to the viewport's width, as wrap and grid.
**As built:** a fourth case in `ItemsView.cells` (the columns `wrap` would have, the cell `width / columns`);
a width under `minWidth` gives one column as wide as the list. Cell edges land on whole pixels with no gaps
(288 over five: 58, 57, 58, 57, 58). Tests: the cells, the right edge, three items, a short last row, a narrow
list, under 60 of 1,000 realized (`ItemsViewTests.fs`); the keys in a `wrapFill` ListBox (`ListBoxTests.fs`).
The Gallery's tiles use `wrapFill 96.0 64.0`; `gallery-phone-lists@3x` regenerated (three cells of 100 where
there were three of 96 and a gap). 1,367 unit tests, 107 rendering tests.

## A control in an item that leaves input to the item (grilling, 2026-11-20)

Tim: "can we add something to make controls ignore input? Like I put checkboxes in a listbox and clicking on the
checkbox doesn't click the listboxitem". Nothing was added: two settings every element has do it together,
`isHitTestVisible false` (the pointer goes through to the item) and `focusable false` (Tab doesn't stop on it).
Measured with a throwaway test first: with the first alone a click selects the item but Tab still stops on
every box. **Decisions (Tim: yes to all):** (1) no third setting that does both (it would overlap these two,
with `isEnabled false` already nearby): the pair is in the README's ListBox section, held by a test; (2) the
box shows its item's selection from the view (`CheckBox.isChecked (List.contains p.Id model.Picked)` in a
controlled list). Checked, not assumed: an item's view is made again when only the selection changes (the view
selector is a new function on each update, which re-views the realized items), so the box follows.
**As built:** no framework change. Two tests (`ListBoxTests.fs`): a Multiple list of such boxes (click and
Ctrl+click on a box select the item and tick it, the box's own handler never runs, Tab lands on the item and
never on a box, Space selects); a plain CheckBox in an item takes the click itself. 1,369 unit tests.
**Not looked at:** what a screen reader says of a checkbox inside a list item that is itself selectable.

## Stored values (grilling, 2026-11-20)

Tim: "can we add Settings to the library? maybe something like ctx.useAppSetting? Allow it to be name by string
and provide a default value. Use the IsolatedStorage system for saving." An app value was already a named, shared
value with a default; this is that, kept between runs.

**Measured before deciding** (a scratch console app, removed): `IsolatedStorage` works in a NativeAOT exe with no
publish warnings. Its store is found by the exe's path (`%LOCALAPPDATA%\IsolatedStorage\…\Url.<hash of the
path>\`): the same exe copied to another folder started with an empty store, and Debug, Release and published
builds each have their own.

**Decisions (Tim):**
1. **The name is "stored value"**, `ctx.useStoredValue` (yes): "setting" is an entry of a settings list in the
   glossary and `AppSetting` is what `App.run` takes.
2. **Defined once, then used by reference** (yes): `StoredValue.define "Compact" false`, as app values are; not
   a string at each use, where a typo makes a second value and two uses can disagree on the default.
3. **Shared like an app value** (yes), and reached outside components the way app values are.
4. **Saving is behind a contract each platform fills in** (yes); in the browser `localStorage` (Tim: "use
   localStorage for browser").
5. **Built-in types `string`, `bool`, `int`, `float` and enums**; any other type gives two functions, to text
   and from text (yes). NativeAOT rules out reflection serializers, and the JSON source generator is C# only.
6. **Written after half a second of quiet and at close**, never on every set (yes).
7. **`IsolatedStorage` on the desktop** (Tim: "I want to use isolated storage for desktop"), over Claude's
   recommendation of a file of our own found by name, made because of the path rule above. The rule is in the
   README and the type's documentation, for app authors.
8. **`App.storeName "Gallery"`**, the assembly's name without it (yes): the file's name on the desktop, the
   prefix of the keys in the browser, where it keeps two apps at one web address apart (GitHub Pages serves
   every repository of an account from one).
9. **The desktop file is JSON** (Tim asked "should we not use JSON?", then yes): one object, a member per value,
   each of its own JSON type, through the low-level reader and writer only. Claude's opinion, asked for: right
   for the desktop, mildly (escaping and numbers for free, room for lists later); to be reconsidered if it added
   more than about half a megabyte to an AOT exe.
10. **No JSON in the browser** (yes): `localStorage` is a key-value store already, so plain text per key, and
    `System.Text.Json` stays out of the download.
11. **Core holds the contract, the hook and a memory store; `Scribble.Desktop` and `Scribble.Browser` hold
    theirs** (yes). An app with no store keeps its values for the run and warns once in Debug.
12. **The Gallery's Light/Dark switch is a stored value** (yes).

Settled by Claude, said to Tim: what can't be read gives the default and a Debug warning, never an exception;
reading happens before the first view; no reset or delete; two running copies don't watch each other (the last
to save wins); the file is written under a temporary name and renamed; the per-assembly user store
(`GetUserStoreForAssembly`).

**As built.**
- **A stored value is an `AppValue<'T>` with a text form** (`AppValue.IsStored`), so every way of using an app
  value works on it unchanged, and `ctx.useStoredValue` is `ctx.useAppValue` (it warns in Debug when given a
  plain app value). `StoredValue.define` (built-in types; another type throws where it is defined, naming
  `defineWith`) and `StoredValue.defineWith name default toText fromText`. Not read at start, which can't know
  what is defined: **read the first time something asks for it**, which for a view is before that view.
- **`IValueStore`** (`TryRead name`, `Write(name, kind, text)`, `Flush`), on `AppValues.Store`.
  `MemoryValueStore` is public (tests, an app that wants nothing kept). `App.valueStore (fun name -> store)` and
  `App.storeName`; `App.run` puts the platform's store first, so one in the app's settings replaces it.
- **Saving:** a set writes the text to the store at once and asks for a flush half a second later; a timer
  that finds another change since waits again, so the flush comes between half a second and a second after the
  last change. Closing the window (and, on the desktop, the end of `App.run`) flushes. A bare `AppValues` with
  no dispatcher flushes at each change.
- **Desktop:** `IsolatedValueStore` over `JsonValueFile` (the JSON part, apart so the unit tests reach it
  without touching real storage; the test project compiles that file too). Members it doesn't know, and
  hand-written lists or objects, are kept in their order; a float that JSON has no number for (NaN, infinity)
  is stored as text; a missing or damaged file is an empty one; quotes and letters are written as they are.
  If the last run stopped between deleting the file and renaming the new one, the temporary file is read.
- **Browser:** `readStored`/`writeStored` in `scribble-platform.js`; storage that throws (turned off, full)
  reads as nothing stored and keeps nothing.
- **Guarded:** a store, or an app's `toText`/`fromText`, that throws gives the default (or leaves the store as
  it was) with a Debug warning; two stored values of one name are warned of.
- **Gallery:** `StoredValue.define "LightTheme" false`, set by the switch beside `Theme.current`;
  `App.onStart` sets the theme from it; `App.storeName "Gallery"`. (Replaced the same day by the library's
  `Theme.picked`: section "Common app values and stored values".)

**Results.** 20 tests (`StoredValueTests.fs`): each type through a second run, floats exact and the same in a
German culture, what can't be read, a store that throws, duplicate names, a value shared by two components, the
flush timing (ten changes in a second: none during, one after; none for an equal value), closing, the store's
name, no store, the JSON file (types, unknown members kept, four damaged files, no write when nothing changed)
and the Gallery's theme. 1,389 unit tests (Release 1,388), 107 rendering tests.
**Checked by hand, in scratch apps:** the real isolated-storage file written and read back by a second and third
run, under the JIT and as a NativeAOT exe (`{ "Volume": 0.75, "Name": "Ada \"A\"", "Dark": false }`); the files
were removed again.
**Size:** Hello.Desktop as a NativeAOT exe, 15,843,328 bytes before and 16,293,888 after: 450,560 more (2.8%),
for isolated storage, the JSON reader and writer and the feature together, paid by every desktop app whether it
has stored values or not. Under the half megabyte of decision 9, not by much.
**Not verified:** the browser store has not run (the built-in browser pane has no WebGPU and refuses
`localStorage`); it compiles, and is on the browser checklist. The browser never flushes at close, which it
doesn't need (each write is kept at once). The Gallery's switch in a real window is on the checklist.
**Found by Tim in the browser (2026-11-20):** the desktop worked as described; the browser stored the value and
didn't load it on reopening. `WriteStored` joins the key and the text with a line feed for one call into
JavaScript, and its F# string held a real line break where `"\n"` was meant (written by a script that expanded
the escape). Once the file had Windows line endings, the key was stored with a carriage return at its end
(`Gallery/LightTheme\r`, which looks the same in DevTools) and read without one. Fixed by writing the escape.
A key stored by the broken build stays in localStorage, unread and harmless.

**Primitive values only (grilling, 2026-11-20).** Tim, after seeing a window's size stored as a `Size` with a
text form of its own: "Let's enforce primitive values for stored values. storing WindowSizeX and WindowSizeY is
fine to me." This replaces decision 5's second half and its as-built note. Decisions (Tim: yes to all):
(a) enums stay (kept by the case's name: no code from the app, and without them a choice is a string or a
number converted by hand); (b) enforced by the compiler, not by an exception at start: one function per type,
`StoredValue.bool`, `int`, `float`, `string` and `enum` (F# can't overload a curried function by type), in
place of the generic `StoredValue.define`; (c) `int` and `float` are the only number types. Settled by Claude,
said to Tim: `Theme.picked` stays a stored string.
**As built:** `StoredValue.define` and `StoredValue.defineWith` are gone (never released). The module is
`RequireQualifiedAccess` (its functions are named `int`, `string`, ...). `StoredValue.enum` takes a .NET enum
(`'T : struct`, `'T :> Enum`), read with `Enum.TryParse<'T>`; a name or number that is no case of it gives the
default. The text form is still an internal `StoredCodec` per value. Tests: the type test now stores a window's
size as `WindowSizeX` and `WindowSizeY`, and the tests of an app's own text form that throws are gone with it.
1,395 unit tests (Release 1,394), 108 rendering tests.

## Common app values and stored values (grilling, 2026-11-20)

Tim: "can we define some common items for our useAppValue functionality and our useStoredValue functionality".
Scribble had five built-in app values (`Theme.current`, `WindowTitle.current`, `Toasts.placement`,
`ScreenReader.enabled`, the TextBox menu's labels) and no built-in stored value; the platform reports a window's
size, but not its position, whether it is active, or the system's dark or light setting.

**Decisions, round 1 (Tim: yes to all):**
1. **A small set the library defines**, in two groups: stored values for what nearly every app wants
   remembered, and app values for facts about the window and system that views read.
2. **A remembered theme**, for every app, with no code in the app (the Gallery needed a stored value, an
   `App.onStart` and a switch that set two things).
3. **A "System" choice** that follows the system's dark or light setting: a second step (platform work on both
   sides).
4. **A remembered window** (size, position, maximized), opt-in with one setting: needs the platform to report
   and set a position; nothing in the browser.
5. **App values for the window's size, whether it is active, and whether the system prefers dark.** Size needs
   no platform work.
6. **Left out until something asks:** a remembered text size or zoom, a language, recent files, reduced motion.

Order agreed: the remembered theme and the window's size (this step), then the platform work for 3, 4 and the
rest of 5.

**Decisions, round 2 (Tim: yes to all):**
7. **`App.themes [ "Dark", dark; "Light", light ]`** names an app's themes, the first the default. `App.theme t`
   stays for one theme. An app that gives neither has the built-in pair under "Dark" and "Light".
8. **`Theme.picked`**, a built-in stored string holding the name, which the library keeps `Theme.current` in
   step with. Setting `Theme.current` directly works for the run and isn't remembered.
9. **A stored name the app no longer has** gives the first theme and stays stored until the user picks again.
10. **Remembered by default**, `App.rememberTheme false` to turn it off: an app with a theme switch always
    wants it, and one without never changes the value, so nothing is written.
11. **`Window.size`**, an app value: the client area in DIPs, updated on every resize. It reports only.
12. **The Gallery moves to `Theme.picked`** and drops its own stored value and `App.onStart`; its stored key
    changes, so a remembered choice resets once.
13. Tim, with the yes: **the Gallery's switch becomes a dropdown, with a third, pink theme.**

Settled by Claude, said to Tim: the stored name is `Scribble.Theme`.

**As built.**
- `App.themes`, `App.rememberTheme`, `Theme.picked` (default "Dark") and `Window.size` (`Size`, zero in a bare
  tree). At start the stored name's theme is set, else the first; when the name is unknown, `Theme.picked`
  reads as the first theme's name for the run without being written (`AppValues.Prime`). From then on a
  listener sets `Theme.current` from the name; a name the app doesn't have changes nothing. With `App.themes`
  and `App.theme` both, the named themes win.
- `App.rememberTheme false` makes `Theme.picked` a plain app value for that app (`AppValues.Unkept`): not read,
  not written.
- An app without a store hears no warning for the library's own stored values (names starting `Scribble.`),
  only for its own.
- `Window.size` is set by `App.start` from the window's client size and scale, at the start and on `Resized`
  and `ScaleChanged`, so only the app's own window reports (not the inspector's).
- **Cost to every app:** the stored theme name is read at start, so a desktop app opens its JSON file in
  isolated storage once at start whether or not it offers a theme switch. Not timed.
- **Gallery:** `themes` (Dark, Light, Pink) through `App.themes`; `themePicker`, a ComboBox on `Theme.picked`
  labelled "Theme" for screen readers. Pink was first `Theme.light` with twelve colours changed (a soft pink
  on pale backgrounds); Tim: "I was thinking more of a neon pink", then "Brighter", then "I want
  lighter background colors". It went from neon on near-black, to neon on deep magenta, to what it is: `Theme.light`
  with twelve colours changed, a neon accent, focus ring and border 0xFF1FC9 on light pinks (backgrounds 0xFFC7EF
  and 0xFFE3F7, controls 0xFFF5FC, text 0x3A0030). The colours are Claude's, seen only in the `gallery-pink@1x`
  screenshot.

**Results.** Six new unit tests (first theme and the one picked last time; an unknown stored name; remembering
off; the built-in pair and `App.theme` alone; no warning without a store; `Window.size` and a component that
follows it), and the Gallery's two theme tests rewritten for the dropdown. 1,395 unit tests (Release 1,394). One
new screenshot, `gallery-pink@1x`; thirteen regenerated because the dropdown replaced the switch (`gallery`,
`gallery-light`, `layout-page`, `layout-grid-overlay`, `inspector-pick`, `inspector-floating` and the seven
phone pages), each looked at; 108 rendering tests. In `gallery-light@1x` the dropdown says "Dark": that picture
is drawn on a bare tree given the light theme (and by `ViewImage.render`, which shares the reference), where
nothing sets the name.
**Not verified:** a real window (the dropdown open, the pink theme on every page, restart in Pink), and the
browser. Steps 3, 4 and the rest of 5 are not built.

## A remembered window (grilling, 2026-11-20)

Step 4 of "Common app values and stored values": a desktop window that opens where it was. Agreed there: opt-in
through `App.rememberWindow true`; nothing in the browser. An app's stored values exist before its window is
made, so the window can be opened at its place. The platform could not say or set a window's position or
maximize it: `WindowOptions` had a title, a size, resizable and an owner, and the SDL bindings had no such calls.

**Decisions (Tim: yes to all):**
1. **Five built-in stored values** under `Scribble.Window.`: `X`, `Y`, `Width`, `Height`, `Maximized`. Width and
   height in DIPs, as `App.size` is (the window keeps its visual size if the display's scaling changes); x and y
   in the screen coordinates the platform reports.
2. **The stored size and position are always the normal window's**, with maximized a flag beside them: a window
   closed maximized reopens maximized, and un-maximizing gives back the size it had.
3. **A window closed while minimized reopens at its normal place**, never minimized.
4. **A position that is on no display any more** (a monitor unplugged) gives the default place with the stored
   size, shrunk to fit the display; "on a display" means enough of the title bar shows to grab.
5. **`App.size` is the first run's size**; the stored one wins afterwards.
6. **The five values stay the library's own**: no public `Window.position` or `Window.isMaximized` (screen
   coordinates differ per platform). `Window.size` stays.
7. **The Gallery turns it on.**

Settled by Claude, said to Tim: only the app's own window (not the inspector's popped-out one); saving rides on
stored values (half a second after the last move or resize, and at close); the contract grows by a position and
a maximized flag in `WindowOptions`, and the window reports moves and maximizing.

**As built.**
- **Contract (`Scribble.Platform`):** `WindowOptions.Position` (none: the platform's place) and `.Maximized`;
  a `Moved` event (moved, maximized, or restored); `IWindowPlacement` (`Position`, `IsMaximized`,
  `IsMinimized`), which the SDL window and the tests' window implement and the browser's doesn't, so nothing is
  stored there; `WindowPlacement.fit displays position size`, the rule of decision 4 as a pure function: the
  position is kept when at least 80 units of the top edge are on a display and at least 80 down from its bottom;
  the size is shrunk to the display, less 64 of height for a title bar.
- **SDL:** bindings for `SDL_SetWindowPosition`, `SDL_GetWindowPosition`, `SDL_GetWindowSize`,
  `SDL_MaximizeWindow`, `SDL_GetDisplays`, `SDL_GetDisplayUsableBounds`. The window is made hidden, sized, placed
  and then maximized (SDL applies that when it is shown). Every window's size is now fitted to its display, not
  only a remembered one's; a window resized after creation with no position is centred again.
- **Core:** `App.rememberWindow`. `App.start` reads the five values into the options before `CreateWindow`
  (a stored size under 100 by 100 is not used). On `Resized`, `ScaleChanged`, `Moved` and at close it stores the
  maximized flag, and the size (from `Window.size`) and position only while the window is neither maximized nor
  minimized. A position beyond -16000 is never stored (Windows parks a minimized window at -32000).
- **Gallery:** `App.rememberWindow true`.

**Results.** Eleven unit tests (`RememberedWindowTests.fs`): the first run; moved and resized, then the next
run's options; maximized and restored; minimized, and a parked window on a platform that doesn't say minimized;
closing; a drag saved once; off by default with stored values ignored and untouched; stored nonsense; and
`fit` (still on a display, on a second one, hanging over an edge, negative coordinates, the display gone, a
window larger than its display, no displays known). 1,406 unit tests (Release 1,405), 108 rendering tests.
**Checked on a real window** (a scratch program on the SDL platform, four windows of 0.6 s on Tim's display,
2560 wide at scale 1): a window asked for at 300,200 and 500 by 400 is there; one asked for maximized reports
maximized from creation and at every event while it grows (so its normal size is never overwritten), ending at
2560 by 1369; a position of 90000,90000 is left out and the window is centred; a window asked for at 9000 by
9000 is shrunk to 2560 by 1328 and on the screen. That last case found a bug, fixed: the shrunk window kept
the corner SDL gave the 9000 one, off the screen.
**Not verified:** the Gallery itself remembering across a restart; un-maximizing after a maximized start
returning to the stored size (the probe could not un-maximize); two displays, a display unplugged for real,
and displays of different scaling (the size is made at the primary display's scale and then set again for the
display the window opens on, as before); NativeAOT with the new bindings.
**Checked by Tim on the desktop (2026-11-20):** "those checks passed" (the checklist's items for this step).

## Following the system's theme (grilling, 2026-11-20)

Step 3 of "Common app values and stored values", with the app value of its decision 5. Agreed there: a "System"
choice that follows the system's dark or light setting, and an app value for which it prefers. SDL reports the
system's theme and sends an event when it changes; a browser has `prefers-color-scheme`. Scribble doesn't know
which of an app's freely named themes is its dark one and which its light one.

**Decisions (Tim: yes to all):**
1. **`App.systemThemes "Dark" "Light"`**: the app names the theme for a system set to dark, then the one for
   light. An app that names no themes has the built-in pair, which maps by itself.
2. **"System" is one more name `Theme.picked` takes**, written `Theme.system` in code. A dropdown lists it beside
   the others; picking it is remembered like any other choice.
3. **A new user starts on System only where the app called `App.systemThemes`**; any other app keeps starting on
   its first theme, so no existing app turns light on a light-mode machine without being asked.
4. **`SystemTheme.prefersDark`**, a `bool` app value that reports and changes with the system's setting.
5. **Dark where the platform can't say**: Scribble's own default theme is dark.
6. **The Gallery's dropdown is System, Dark, Light, Pink**, System the starting choice.

Settled by Claude, said to Tim: while System is picked the window changes theme the moment the system's setting
does; an app theme named "System" gets a Debug warning; the window's title bar is the system's and follows the
system's setting, not the app's picked theme (Pink on a dark system has a dark title bar, as before).

**As built.**
- **Contract (`Scribble.Platform`):** `ISystemTheme.PrefersDark` (`bool voption`) on a platform that can say,
  and a `SystemThemeChanged` event sent to its windows. Separate from `IPlatform`, so other platforms and the
  tests' second fake need no change.
- **SDL:** `SDL_GetSystemTheme` (1 light, 2 dark) and `SDL_EVENT_SYSTEM_THEME_CHANGED`, which is no window's
  event and goes to every window. **Browser:** `prefersDark()` and a `change` listener on
  `(prefers-color-scheme: dark)` in `scribble-platform.js`, as event kind 14.
- **Core:** `SystemTheme.prefersDark` (true in a bare tree), `Theme.system` (the string "System"),
  `App.systemThemes`. `App.start` sets the value at the start and on the event. The name is resolved when it is
  picked and again whenever the system changes while it is picked. A user who has picked nothing is one with
  nothing in the store under `Scribble.Theme` (`AppValues.HasStored`): they start on System where the app set
  it up and System resolves, else on the first theme. This also fixed a quirk of the step before: such a user
  started on a theme named "Dark" wherever it was in the list, because that was the value's default.
- **Debug warnings:** a theme named "System"; `App.systemThemes` naming a theme `App.themes` doesn't have (the
  half that exists still works).
- **Gallery:** `App.systemThemes "Dark" "Light"`, and `Theme.system` first in the dropdown.

**Results.** Eight unit tests (`SystemThemeTests.fs`): the value follows the system and is dark when it doesn't
say; a new user on a light and on a dark system; a named theme staying put while the system changes, and System
remembered across runs; an app that hasn't set it up; the built-in pair; remembering off; a component running
again; names that can't work. The Gallery's theme test expects System at the start. 1,414 unit tests (Release
1,413), 108 rendering tests, no picture changed (a bare tree's dropdown still reads "Dark").
**Checked on the real platform:** `SDL_GetSystemTheme` through `ISystemTheme` says dark on Tim's machine, where
Windows' own setting (`AppsUseLightTheme` = 0) says dark.
**Not verified:** the change event on Windows (it needs the system's setting switched, which is Tim's to do);
the browser side, which has not run; the Gallery in a real window.

## Vectorising the PNG decoder (2026-11-18)

Status: **built** (agreed by Tim, 2026-11-18: "yes to all"); the Gallery's Images page checked by Tim on the desktop
(2026-11-18: "looks right"). The NativeAOT exe and the browser are not yet checked. Tim asked for the PNG code to
use `Vector128` before M9 step 9d.

Measured first (1024² noisy picture, Release, warmed up, best of 20, ms; the first row is the timing test's case):

| Picture (one filter on every row) | CRC | zlib | Unfilter | To RGBA | Mip levels | Total |
| --- | --- | --- | --- | --- | --- | --- |
| RGBA, Paeth | 0.6 | 5.8 | 10.9 | 3.7 | 2.0 | 22.9 |
| RGBA, Average | 0.7 | 7.7 | 2.4 | 3.7 | 1.8 | 16.1 |
| RGBA, Sub | 0.6 | 5.7 | 1.7 | 3.7 | 1.8 | 13.3 |
| RGBA, Up | 0.6 | 6.3 | 1.6 | 3.7 | 1.8 | 13.9 |
| RGB, Paeth | 0.3 | 2.0 | 6.8 | 0.5 | 1.8 | 11.6 |

zlib is the BCL's (and checks Adler-32 itself: there is none of ours).

1. **Unfiltering with `Vector128`:** Up 16 bytes at a time for every format; Sub, Average and Paeth a pixel at a
   time in one register (libpng's method: each pixel needs the one before) for 3, 4, 6 and 8 bytes a pixel. 1 and 2
   bytes a pixel (grey, palette, grey with alpha) stay scalar.
2. **To RGBA with `Vector128`:** 8-bit RGBA premultiplied four pixels at a time, `(c·a + 127) / 255` written as
   `t = c·a + 128; (t + (t >> 8)) >> 8` (the same for every input, tested for all 65,536); 8-bit RGB by a shuffle.
   16-bit, grey, palette, colour keys and Adam7 passes stay scalar.
3. **Mip levels (`Pixels.halve`) with `Vector128`:** 2 ms of every decode, and JPEG will share it.
4. **CRC-32 stays** slicing by 8: 0.6 ms of 23, and faster needs carry-less multiply, which `Vector128` doesn't
   have (separate x86 and ARM code, nothing for wasm).
5. **The encoder stays:** its time is zlib at `SmallestSize`.
6. **Fallback and tests:** the scalar code stays, picked when `Vector128.IsHardwareAccelerated` is false; both are
   internal (`Rows.fs`, `Pixels.fs`) with no runtime switch, and tests run both over the same inputs (every filter,
   every bytes-a-pixel step, widths 1 to 40 so every tail) and compare exactly. A vector load never reads past a row.
7. **Browser:** .NET turns wasm SIMD on by default and no project of ours turns it off: no project changes. The
   timing test's guard comes down once the new number is in.

**As built (2026-11-18):**
- **`src/Scribble.Imaging/Rows.fs`** (new, internal, after `Pixels.fs`) holds the row loops, each twice:
  `unfilterScalar` and `unfilterVector`, `rgba8Scalar` and `rgba8Vector`, `rgb8Scalar` and `rgb8Vector`, and the
  unsuffixed `unfilter`, `rgba8`, `rgb8` that `Png.decode` calls, which pick by `Rows.useVectors ()`
  (`Vector128.IsHardwareAccelerated`, and little-endian: pixels are loaded as integers). The scalar loops are the
  old ones moved out of `Png.fs`, now able to start part way along a row, so they also finish each vector loop's
  tail. `Png.decode` refuses a filter byte over 4 itself. `Pixels.halveWith vectors level` is the mip level both
  ways; `Pixels.halve` picks.
- **How each works:**
  - Up: 16 bytes added at a time.
  - Sub, Average, Paeth: the pixel before stays in a register, starting as zeros (what the filters take to the left
    of a row), so the first pixel needs no special case. A pixel is loaded as 4 bytes (3 or 4 a pixel) or 8 (6 or
    8) and exactly its own bytes are stored; the last pixel of a 3- or 6-byte row is left to the scalar loop, since
    its load would reach a byte past the row. Average is `(a & b) + ((a ^ b) >> 1)`, which needs no ninth bit. Paeth
    runs in 16-bit lanes: the three distances by `Abs`, the smallest by `Min`, then a where its distance is the
    smallest, else b where b's is, else c (the scalar loop's tie order).
  - RGBA: alpha spread beside its colours by a shuffle, 255 beside the alpha itself, then decision 2's formula in
    16-bit lanes. RGB: a shuffle of 12 of 16 loaded bytes, alpha set to 255. Both only for packed pixels (not Adam7
    passes); the last pixels of a row are scalar.
  - Mip levels: red and blue, and green and alpha, of four pixels in 16-bit lanes, two rows added, then each pixel
    added to its neighbour through a 64-bit shift; two output pixels a step.
- **Only portable `Vector128` calls** (no `Sse2`, `AdvSimd` or `PackedSimd` types), so one path serves x64, ARM64 and
  wasm. `Shuffle` needs SSSE3 to be fast on x86 (every x64 processor since 2006 has it); without it .NET's own
  fallback gives the same bytes, slower.
- **Measured** (the table's pictures, same machine, ms, scalar → vector): Paeth unfiltering 11.5 → 2.4 (RGB 6.8 →
  2.3), Average 1.6 → 0.95, Sub 1.7 → 0.33, Up 1.6 → 0.11; RGBA to premultiplied 3.9 → 0.36, RGB to RGBA 0.57 →
  0.13; mip levels 1.4 → 0.2. The whole decode with levels: RGBA Paeth 22.9 → 9.4, RGB Paeth 11.6 → 5.0; what is
  left is zlib (5.8) and the CRC (0.6). **The timing test** (`decoding a 1024 square PNG is fast`; its file is
  2.5 MB, so more zlib): 26.0 ms on the code before, **12 to 15 ms** after; its guard went from 40 to 25 ms.
- **Tests (`ImagingVectorTests.fs`, 6):** every filter × steps 1, 2, 3, 4, 6, 8 × 1 to 40 pixels × three kinds of
  bytes (any, small values for Paeth ties, only 0 and 255) × a first row and a later one, scalar against vector,
  with guard bytes around the row and the row above exactly its length; odd byte counts at step 1; RGBA and RGB at
  widths 1 to 40, packed and spread; the vector premultiply against `Pixels.premultiply` for all 65,536 inputs; mip
  levels for 1 to 24 by 1 to 9 pixels, both ways and against the formula. PngSuite and the other PNG tests pass
  unchanged (on this machine they now run the vector path). Totals: 1,228 unit (+7 timing), 99 rendering.
- **Wasm:** the SDK's targets set `WasmEnableSIMD` from `WasmEnableExceptionHandling`, on by default; nothing in the
  repository sets either. Not run in a browser here: a line waits in `docs/CHECKLIST-BROWSER.md` (interpreted and
  AOT; interpreted is the one to watch, where a vector call the interpreter doesn't know runs as ordinary code).

## Rendering to an image and saving it (built 2026-11-18)

Status: **built** (agreed by Tim, 2026-11-18: round 1 "yes to all, PNG it is", round 2 "yes to all"); waiting for Tim to try it. Before M8 step
4b.

Tim asked whether we can render to a PNG, then wanted every use: tools and scripts (the README's pictures), an app
feature ("export this chart"), and the browser. Facts it rests on:
- `IRenderDevice.RenderOffscreen` draws into a texture and reads the pixels back, on the desktop only. The browser
  throws: `mapAsync` returns a Promise (the open item "RenderOffscreen must become async").
- PNG writing lived only in the rendering tests (`Png.encode`, rows unfiltered); `Scribble.Imaging` only decoded.
- App code can't reach an element; the nearest pattern is `Scroller`, an object the app makes and gives through a
  setting.
- SDL3 has a save dialog (`SDL_ShowSaveFileDialog`); the browser saves by starting a download.

Considered: BMP instead of PNG (Tim). It saves about 20 lines of an encoder that already existed, and costs file size
(no compression: 1.6 MB for a 720×560 picture), transparency (only the V4/V5 header has alpha, and many viewers ignore
it) and use (GitHub doesn't show it inline). The hard parts (async readback, capturing a live element, saving) don't
depend on the format.

**Round 1 (Tim: yes to all):**
1. **`Png.encode` in `Scribble.Imaging`,** beside the decoder, replacing the tests' copy: an adaptive filter per row
   (the smallest-sum heuristic), `ZLibStream`; a round-trip test through our decoder and WIC.
2. **A render gives an `Image`** (from pixels, its density the render's scale): shown at once in an `ImageView`, or
   `Image.toPng` for the file's bytes. No new public pixel type; `Bitmap` stays the renderer's.
3. **Async everywhere:** every render returns `Task<Image>`, already finished on the desktop.
   `IRenderDevice.RenderOffscreen` becomes `RenderOffscreenAsync`, which closes the open item and allows browser
   screenshot tests later.
4. **`ViewImage.render view [ ViewImage.size (w, h); ViewImage.scale; ViewImage.theme; ViewImage.background ]`:**
   any view at a size, no window. In an app it uses the app's device and fonts; in a tool, `Scribble.Desktop` makes
   a device and the HarfBuzz text system on first use. One layout and one frame: components fresh, nothing hovered,
   scrolled to the start, loading images waited for (with a timeout).
5. **`Capture`, the Scroller pattern:** `let capture = Capture()`, `Border.capture capture` (on `Layoutable`),
   `capture.ToImage [ settings ] : Task<Image>`. It draws the element and everything inside it as on screen
   (component state, hover, scroll), at the window's scale or a given one, without the layers above it. It runs
   between frames on the UI thread and never asks for a frame of its own (the power rule). A capture not attached to
   a live element fails its task with a clear message.
6. **Transparency kept:** colours are un-premultiplied when encoded (PNG stores straight alpha), checked by a round
   trip. `ViewImage` defaults to the theme's background, as a window does; `Capture` to transparent.
7. **`IPlatform.SaveFile(suggestedName, bytes) : Task<bool>`:** SDL's save dialog on the desktop, a download in the
   browser. The Gallery gets an "Export PNG" button. Opening files can come later the same way.
8. **Now, as one step, before 4b:** encoder, async readback (desktop and browser), `ViewImage`, `Capture`,
   `SaveFile`, the Gallery's button. Tests: PNG round trips; `ViewImage` pixel-identical to the screenshot
   references; a capture of a scrolled, hovered element; transparency. Tim tries it, then 4b.

**Round 2 (Tim: yes to all).** Facts: `Image.fromPixels` takes a key, its cache identity; we ask WebGPU for its
default limits (8192 pixels a side); app code can't reach the platform.
1. **Each render its own key** (a counter): its pixels live in the Image; the cache keeps a decoded copy only while
   something shows it (the holding rule).
2. **`Image.toPng : Image -> Task<byte[]>` for any image:** rendered or from pixels at once; from a file or URL
   decoded first.
3. **Loading images in a `ViewImage`:** waited for up to `ViewImage.imageTimeout` (default 10 s); past it, drawn
   without them and a debug warning (`DebugWarnings.Once`), not a failed task. `ViewImage.waitForImages false` skips
   the wait.
4. **Over 8192 pixels a side:** the task fails with a message naming the limit. Tiling waits for a real use.
5. **A Capture covers the element's own box,** not clipped by what's around it (half scrolled out comes out whole);
   an ItemsView inside draws only its realized items (as on screen: realizing everything would break "frame work
   proportional to what's visible"); transforms on ancestors ignored (as `PositionInTree`); a focus ring and hover
   drawn if showing. The docs say so.
6. **`Capture.margin`** (default 0) adds room for shadows; the picture is exactly the element unless asked.
7. **Settings lists:** `capture.ToImage [ Capture.scale; Capture.background; Capture.margin ]` (statics on
   `Capture`), like `ViewImage.render`.
8. **`Files.save name bytes : Task<bool>` in Core,** through the running app's platform (set by the host, as the image
   fetcher is). `false` when the person cancels; always `true` in the browser (a download can't tell). SDL's callback
   comes on another thread; the task finishes on the UI thread. The dialog's filter comes from the name's extension.
   `Files.open` can come later beside it.
9. **Tools start `use _ = DesktopApp.headless ()`** (`Scribble.Desktop`): a device and the HarfBuzz text system,
   disposed at the end. `ViewImage.render` with neither an app nor headless running fails, saying to call it.
10. **The Gallery's Images page:** an "Export PNG" button capturing a card with a shadow (with `margin`) and saving
    it, and a `ViewImage` of a small scene shown in an `ImageView` at 1× and 2×.

**As built (2026-11-18).** Everything above, with these differences:
- `Png.encode (width, height, straightRgba)` writes RGB when every pixel is opaque, else RGBA; the best of the five
  filters per row, `ZLibStream` at `SmallestSize`. The WIC check runs no WIC: every PngSuite picture, as WIC decoded
  it into the references, is encoded and read back unchanged. A 256² gradient comes out under 2 KB.
- `IRenderDevice.RenderOffscreenAsync` returns `Task<Bitmap>` (premultiplied); `IRenderDevice.MaxTextureSize` (8192)
  gives the limit its message. The browser renders into an `rgba8unorm` texture, `mapAsync`s a padded copy, and
  .NET takes the bytes in a second call (`takeReadback`). The tests' `device.RenderOffscreen` waits on a finished task.
- `ViewImage.render` sizes to its content when `ViewImage.size` is left out. In an app it runs on the UI thread and
  waits for images through `Dispatcher.PostAfter`; headless it blocks the calling thread (a tree is one thread's).
  The theme is `ViewImage.theme`, else the app's, else the default.
- `Capture` keeps a weak reference to its element (a tracked property tells it when it's set or joins a tree) and
  checks it's still showing when asked; `ctx.useCapture ()` keeps one across renders. Settings: `Capture.scale`
  (default the window's), `Capture.background`, `Capture.margin`.
- `Image.toPng` encodes pixels; an image from bytes, a resource, a file or a URL whose bytes are already a PNG is
  returned as it is (a file fails in the browser).
- `IPlatform.SaveFile(owner, suggestedName, bytes)` takes the owning window (the dialog is modal to it). SDL starts
  in Pictures for an image extension, else Documents, and adds the extension if the person typed none.
- The render environment is per UI thread (`RenderServices`): an app's on its own thread, the headless one
  elsewhere, else the last app started. Parallel tests' apps no longer find each other's.
- `DesktopApp.headless [ Headless.fonts [...] ]` takes a settings list (the fallback fonts, Twemoji for emoji);
  `Headless.start device shaper settings` is in Core for other hosts.
- The Gallery's Images page has "Saving as PNG": a card with a shadow and "Export PNG" (`Capture.margin 16`, 2×);
  then "Render the Gallery", the whole Gallery as `ViewImage.render (gallery ()) [ size (720, 560); scale 2.0 ]`,
  shown at half size, with "Save as PNG" (`gallery.png`). It first rendered a small card made only for the purpose,
  at 1× and 2×, and Tim expected a picture of the Gallery: a view image should show something recognisable
  (2026-11-18). The page can't name `GalleryApp.gallery` (compiled later), so the app sets an app value,
  `Common.wholeGallery`. The picture's frame is there from the start, so the page doesn't grow out of sight
  below the buttons. Rendering happens only on a press, so the page's own screenshot is unaffected
  (`images-page@1x` is now 720 × 2200 to show the section).
- Tests: 21 encoder tests, 3 `Files.save` tests (fake platform), 9 image tests on the GPU (view images identical to
  `counter@1x` and `gallery-light@1x`, own size and scale, transparency through PNG, the size limit, a capture
  hovered and scrolled against a crop of the full render, margin and scale, a detached capture's message, and
  "Render the Gallery" clicked in a host with a dispatcher and app values, the picture decoded off the UI thread,
  filling its frame without the page growing), on D3D12 and Vulkan. Checked in Chrome (before the Gallery
  render): the view images and an exported 504 × 346 RGBA `card.png`.

## Where defaults live (inventory and grilling, 2026-11-19)

Tim asked for a pass on themes and styles: are the defaults of every element and control in the right place? The
inventory found a value could come from five places (property default, type default, the theme's styles, the
theme's base values, private constants in an element's module) with no rule for which:

- The dark palette was written out about 45 times outside `Palette`: as type defaults (Button, the ToggleButton
  family, ComboBox, TextBox, TabHeader, Toast, Modal, Scrim) and as property defaults (Slider, Separator,
  TabControl, ComboBox, MenuItem, ScrollViewer, ListBoxItem, TextBox, SelectionHandle), and set again by the
  theme's styles from theme values. Nothing kept the copies equal. (No property was left dark under `Theme.light`.)
- Padding and border thickness were type defaults for Button, ComboBox, TextBox, ListBoxItem and MenuItem, but
  came from the theme's styles for Flyout, TooltipPresenter, Modal and ToastPresenter (Modal's padding in both).
- TabHeader's and MenuItem's corners were fixed numbers that ignored `Theme.cornerRadius`.
- A bare tree's look was not the themed look at rest (no Slider thumb shadow; no border or shadow on Toast and Modal).

Decisions (Tim took every recommendation; "it's ok for screenshots to change where sensible"):

1. **Words** (GLOSSARY.md): property default, type default, built-in look, base value.
2. **A bare tree keeps a look:** M7 step 2 decision 6 stands. Without a theme an element has its built-in look at
   rest, without state looks.
3. **Rest colours are type defaults, as theme values** (`Border.background Palette.control`): one place says what
   colour a control is at rest. A theme value's default is the dark value, so a bare tree is unchanged, and under
   a theme the type default reads the nearest theme. The inspector's source for these reads `type`, with the
   theme value's name under it.
4. **Metrics are type defaults, never theme styles:** padding and border thickness for Flyout, TooltipPresenter,
   Modal and ToastPresenter leave the theme. A theme supplies only values, so they never differed between themes.
5. **No theme values for metrics yet** (density: a compact or touch theme). Decision 4 puts every metric in one
   kind of place, which a density round needs.
6. **Element types carrying their own state looks is a later round.** Only Core can add a built-in look today
   (`ThemeLooks` is internal; a third-party control ships a style list for `App.styles`). The full fix dissolves
   `ThemeLooks` into the element files; it changes how styles match and what the inspector reports.
7. **The built-in look at rest is entirely type defaults:** shadows (Button, Slider's thumb, the floating ones of
   Flyout, Tooltip, Toast, Modal) and transparent backgrounds (ListBoxItem, MenuItem) too. A theme style holds
   only state blocks, transitions and class variants (a toast's `warning` and `critical`), and rules about another
   type's elements inside one (text in a disabled Button).
8. **Coloured property defaults become neutral** (`Color.Transparent`): a dark colour was wrong under a light
   theme anyway. Two stay: inherited `foreground` (black; unit tests rely on it) and the focus ring's colour,
   which is `Palette.focusRing`'s default in place of a second literal.
9. **Corners follow the theme, small parts at half:** Toast, Modal and TabHeader (top corners) take
   `Palette.cornerRadius`; MenuItem takes half, as CheckBox does (4 becomes 3). A true concentric corner would
   need the flyout's padding, which a style can change.
10. **The inspector always shows a type default that holds a theme value**, as it does what the theme's styles
    can set.
11. **Two tests hold the rule:** every built-in element type reads the same at rest on a bare tree and under
    `Theme.dark []`; no built-in type default or property default is a fixed colour other than transparent
    (the exceptions of decision 8 listed in the test).
12. **Left alone:** the nine "text inside a disabled X doesn't fade twice" styles (they need a line per control
    and miss an app's own element extended from Button; a container saying it fades its own content belongs with
    decision 6's round), and numbers a theme can't reach (disabled opacity 0.5 and 0.45, transition times, control
    sizes; they belong with density).
13. **Order:** a test that a theme value in a type default resolves, follows a theme change and is inherited;
    colours; metrics and shadows; corners; the inspector; the rule tests.
14. **Screenshots:** a mismatch these decisions predict is regenerated and listed with its reason; anything else
    is a bug.

Also noted for a later discussion (Tim): a ModalPresenter, to match ToastPresenter and TooltipPresenter. Discussed
the same day: section "Names an app sees".

As built (2026-11-19):

- A theme value in a type default needed no new code: `Element.Resolve` already reads one wherever a value comes
  from, and `Values` reports it as `TypeDefault` with the theme value's name. A test now holds that (nearest theme,
  a theme change, inherited text inside).
- Type defaults by theme value: Button, the ToggleButton family (`ToggleButton.labeledDefaults`), Slider, Separator,
  ScrollViewer, TabControl, TabHeader, ListBoxItem, MenuItem, ComboBox, TextBox, SelectionHandle, Flyout and
  TooltipPresenter (a shared `Flyout.floating` list), Modal, Scrim, ToastPresenter. Property defaults that were dark
  colours are transparent (17 of them, and `markColor`, which was white); `FocusRing.color` is
  `Palette.focusRing.Default`. No colour literal is left in `Elements/`.
- `ThemeLooks` (Theme.fs) holds state blocks, transitions, a toast's `warning` and `critical`, and the disabled-text
  rules; the file went from 275 lines to 198. The styles for TabControl, ScrollViewer, Separator, SelectionHandle, Flyout,
  Modal and TooltipPresenter are gone (nothing left to say).
- `ThemeConvert.topCorners` for TabHeader; MenuItem uses `halfCorners`.
- **Found: a property given twice in one `Element.defaults` list took the first.** RadioButton and Switch append
  their own values to the shared list, so on a bare tree a radio's dot was white and a switch's knob the wrong
  colour (the theme's styles hid it), and RadioButton's agreed 1.5-DIP ring ("M8 step 4", decision 16) had always
  drawn at 1. Now the later one wins, with a test. **The ring is 1 DIP, the same as a CheckBox's box** (Tim,
  2026-11-19, after seeing both: "make the radio button match the check box"; what had bothered him at first was
  the chosen dot's contrast on the dark theme, below). RadioButton's own thickness is removed and it takes the
  shared 1. This replaces the 1.5 in "M8 step 4", decision 16.
- **A chosen RadioButton fills with the accent and its dot is `Palette.onAccent`** (Tim, 2026-11-19; option 1 of
  accent fill / a dot in the text colour / a lighter accent as a new theme value): an accent dot (`#3D59A1`) on the
  control colour (`#3A3A55`) was hard to see on the dark theme. It now reads as a checked CheckBox does, on both
  themes. The fill is a state block in the theme's styles; the dot takes the shared mark colour, so RadioButton has
  no mark colour of its own. This replaces "accent ring and dot" in "M8 step 4", decision 16. Without a theme a
  chosen radio shows a white dot on the control colour (no state looks there).
- **The inspector needed no change** (decision 10): a type default always showed. A Button's `background` reads
  `type` with `Palette.control` under it; `ThemedProperties` no longer lists rest colours.
- Screenshots: 5 regenerated. `menu@1x`, `menu-light@1x` (MenuItem's corner 4 to 3: 32 and 56 pixels); `lists@1x`,
  `lists-light@1x`, `gallery-phone-lists@3x` (the chosen radio's accent fill; its ring is 1 DIP, as it always drew). No other picture changed: none shows a
  bare-tree Slider, Flyout, Toast or Modal, whose looks at rest gained what the theme used to add.
- Tests: 1,304 unit tests (4 new: a theme value as a type default; the two rule tests in `DefaultsTests.fs`; later
  wins) plus 9 timing tests skipped; 106 rendering tests. Three tests changed for the new sources (`type` where
  `theme` was). Timing: 9 pass; "decoding a 1024 square PNG is fast" failed twice in three runs at about 29 ms
  (the imaging code is untouched by this pass).

## Names an app sees (discussed and built 2026-11-19)

Tim asked to discuss "bringing back" a ModalPresenter, to match ToastPresenter and TooltipPresenter. There never
was one: what was removed is FlyoutPresenter ("M8 step 4", 2026-11-17), because with a presenter one flyout's own
look couldn't be reached (`Flyout.padding 0.0`), only all of them through the presenter's style. Modal was built
afterwards in the Flyout's shape, for the same reason.

Tim: "I just want consistency for users of the library." Seen from there the boxes were not the inconsistency, the
names were: a tooltip was styled with `TooltipPresenter.style`, a type no app otherwise writes; a toast had two
public names for one style function; and the inspector showed `ToastPresenter`, `TooltipPresenter` and
`ModalAnchor`, none of which an app wrote. A ModalPresenter would have added a third such name and taken
`Modal.padding` away from a single modal.

Decisions (Tim: "Yes, all four"):

1. **`Tooltip.style`** is how tooltips are restyled (a new `Tooltip` type holding it), so every floating thing is
   styled as what the app wrote: `Flyout.style`, `Modal.style`, `Dialog.style`, `Toast.style`, `Tooltip.style`.
2. **One public name each:** `ToastPresenter.style` and `TooltipPresenter.style` (and `TooltipPresenter.create`) are
   internal. The two types stay public only because they are in `Toast.style`'s and `Tooltip.style`'s signatures;
   the README and samples don't name them, and their settings are written as Border's
   (`Tooltip.style [] [ Border.padding 6.0 ]`; "Styling them without the element's name", 2026-11-18).
3. **Tools show the app's words:** the element types' names are "Toast" and "Tooltip", and a Modal's place in the
   view is "Modal" (was "ModalAnchor"), so the inspector's tree and selector text (`Toast.warning`) match what was
   written. Two element types are now named "Modal" (the place in the app's tree, the box in its own layer); styles
   match a type by reference, so nothing confuses them.
4. **The rule:** an app names and styles a thing by what it wrote. Where what it wrote is an element (Flyout,
   Modal), that element is the box its content sits in, so one of them can have its own look. Where it wrote a
   setting or an action's options (a tooltip, a toast, a dialog), the host makes the box: a presenter, an internal
   detail (a dialog's is a Modal with the class `dialog`). **No ModalPresenter.**

As built: no screenshot changed (no reference shows these names). Unit tests 1,305 (1 new: the three names and
the styles' descriptions); the tooltip tests use `Tooltip.style`. 106 rendering tests.

## One way to say when (2026-11-19)

Tim asked whether the selector syntax for styles could be simpler. Counting the samples and the README (37 styles):
9 with an empty selector, 9 with one class, 10 with a state, 9 with an ancestor, 1 with a property test. The syntax
is near its floor for typed lists. Turned down: CSS-like strings (a misspelt type or class would compile, and
resolving type names needs a registry keyed on user types, against engineering rule 6); short names by `open`
(`cls "primary"; hover`: two spellings for every part, and names like `hover` collide with an app's own);
dropping the empty `[]` (`style` is a module function and can't be overloaded, and it matches `X.create []`).

What was heavy was having two ways to say a state: as a selector part (`Button.style [ States.hover ] [ ... ]`)
and as a block in the settings (`Style.on States.hover [ ... ]`).

Decisions (Tim: "Let's do the one way but make it Style.onState and let's add Style.onProp"):

1. **A selector says which elements; blocks say when.** Selector parts are the type, classes, a name and what the
   element is inside (`Style.inside`, `Style.parent`). When is a block in the settings.
2. **`Style.onState state [ ... ]`** replaces `Style.on`, and in a view `X.onState` replaces `X.on` (Claude's
   call, so the two keep one name: Tim named only the style's).
3. **`Style.onProp prop value [ ... ]`** is new: settings while the element's own value of `prop` (else its default;
   never a style's) equals `value`, matched again when it changes. It is `Style.prop` as a block. A style may hold
   any number; blocks nest with `onState`. For styles only: a view knows the value and gives the settings
   themselves (an element's own settings could hold only one such block).
4. **A state or property test stays a selector part in one place, an ancestor's**
   (`Style.inside Button.Type [ States.disabled ]`, `Style.inside Border.Type [ Style.prop selected true ]`): there
   is nowhere else to say it. The parts still work on the element itself (tests keep covering them); the README,
   samples and doc comments write the one way. `Style.where` has no block form until something needs one.

As built: a block (`StateBlock`) carries a state or a property test and narrows its style's selector with it
(`Narrow`); `Style.onProp` blocks share one key (`PropBlocks.prop`), which a style's list may repeat. The theme's
styles already wrote blocks; its one own-state selector (`TextBlock:disabled`) is a block now, the same rule. The
selector text tools show is unchanged (`Button:hover`, `Slider[Step=5]`; a union value prints as "…", as before).
Screenshots: 3 regenerated (`styles-page`, `styles-page-light`, `gallery-phone-styles`: the Styles page's own label
quotes the syntax, now "Hover me: Border.onState States.hover [ ... ], right in the view"). Tests: 1,307 unit
(2 new, for `onProp`), 106 rendering.

## States of your own (2026-11-19)

Tim: "creating custom states seems confusing. Can we make it more user friendly?" The Gallery's chips needed three
declarations and four element-author ideas for one look: a property (`Prop.define "Selected" false
Affects.Render`), a setting for it (`Setting.create selected v`), and a state read from it (`States.ofProp "chosen"
selected id`), under two names, then used two ways in the styles (the state, and `Style.prop selected true`).

Rounds: a one-call state plus a setting (Claude); what becomes of the function form, `States.define name (fun e ->
...)`, which nothing in Core used and whose `element.RefreshStates()` an element type's own code couldn't reach
(its contexts expose neither the element nor a refresh); `States.ofProps "full" [ a; b ] (fun v m -> ...)` (Tim:
a list of properties of different types can't type the lambda; `ofProp2` proposed instead); then Tim's settings
list, which was agreed with two changes.

**Decisions (Tim, 2026-11-19: "Let's build it")**
1. **`States.define name settings` is the one way to make a state** (a settings list, as every user-facing API).
2. **No settings: the view switches it.** `let chosen = States.define "chosen" []`, then `Border.state chosen
   isPicked`. `Layoutable.state` is the setting (so `Button.state`, `Border.state`); it throws for a state that
   isn't switched by a view (`States.hover`, an `ofProp` state, one with a test). A style can't set it either.
   Considered: `inState` (too like `onState`), a member on the state (`chosen.Is v`: not the settings style).
3. **A state that follows properties says which and when:** `States.dependsOn prop` for each, and `States.test
   (fun v -> ...)`. Both or neither: a test with nothing to depend on, or the reverse, throws when the state is made.
4. **The test gets a reader, not the element** (the first change to Tim's form, which had `fun e -> e.Get ...`):
   `v.Get prop` reads the element's own value, never a style's, as `ofProp` always did, so a style can't switch the
   state that applies it; and in a Debug build reading a property the state doesn't depend on warns once (a state
   that silently stops following a property is what the list form risks: each property is named twice).
5. **The settings are `States.dependsOn` and `States.test`** (the second change: `State` is what `ctx.useState`
   returns).
6. **`States.ofProp name prop test` stays** as the short form for one property: the four built-in custom states
   use it, and it carries what lets a forced "checked" draw the tick.
7. **The function form and the public `element.RefreshStates()` are gone.** Nothing needs a hand-made refresh now.
8. **Docs say which to reach for:** an app's own state is `States.define "name" []`; a class does it too when a
   name is all that's needed (`Border.classes [ "chip"; if picked then "chosen" ]`).

**The inspector lists an element's own states** (Tim, the same day: "Can we make it so the inspector only shows
states that are actually on the element?"). This takes back "Every state is always listed" (2026-11-16, in the
inspector's notes), which put `checked`, `indeterminate`, `selected`, `open` and every app state on every
element's STATES row. Rule (`element.ApplicableStates`, public like `ActiveStates`): the five built-in states
always (hover on anything is worth forcing), and a custom state when the element is in it or has it forced, has a
value of its own for a property the state reads (`Border.state chosen false` counts), or is given a look for it by
a style that reaches it: its theme's, the app's, the scopes above it, its own `styles`, its inline blocks. That last
part is what keeps `checked` on a CheckBox that isn't checked and was never set: the theme styles it. A style
"reaches" an element when its selector matches with its states and property tests left out. Not decided by
element type: there is no list of the properties a type has (still open from M6).

**Stricter, the same day** (Tim: "I want the states to show if they are a possible state... the 'high' state on
that slider is never going to apply to another slider because no other slider is tied to it in any way"). The rule
above listed `high` on every Slider with a value, since each has the property it reads. Now a custom state is
listed only when something ties it to the element: the view switches it there (the element has a value of its own
for the state's hidden property, so `Border.state chosen false` counts), a style that reaches the element gives it
a look, or a tool has forced it. Having a property the state reads no longer counts, and neither does being in the
state: a Slider at 90 that no style for `high` reaches is in it to no effect, and `ActiveStates` still says so.
Considered: keeping "is in it" (a row that changes as a slider is dragged, for a state that does nothing there),
and a `States.appliesTo` setting naming element types (every state would list its types, `checked` four of them,
and forgetting one hides it silently).

**As built (2026-11-19)**
- `Style.fs`: `ElementState` holds the properties it reads (an array; none for the built-in five) and, for a
  switched state, its hidden bool property (`"State " + name`, `Affects.Nothing`, `NotStylable`, tested by styles).
  `StateValues` is the reader (a struct over the element and the declared properties), `StateSetting` the setting.
  An ancestor part that tests a state marks every property it reads, where it marked the one.
- `Layoutable.state`; `Element.HasOwnValue` (internal); `ApplicableStates` at the end of `ElementTree.fs` (it needs
  the tree's styles). The inspector's STATES row uses it; nothing else of the inspector changed.
- Gallery: the Styles page's chips are `States.define "chosen" []` and `Border.state`; the chosen look is a block
  in the chip's style and the text follows through `Style.inside Border.Type [ Style.cls "chip"; chosen ]`. Same
  pixels for the chips; the hint beside them reads "Border.state chosen". Under them (Tim: "can you also add a
  demo of State.ofProp"), a Slider whose filled part turns to `Palette.danger` from 80 up: `States.ofProp "high"
  SliderProps.value (fun v -> v >= 80.0)` and `Slider.style [] [ Style.onState high [ ... ] ]` in the page's styles.
  The inspector lists "high" on that slider alone (the stricter rule above): the page's style reaches no other.
- Still as it was: custom states are kept per process (`States.customStates`); moving that to the host is on M10's
  list (rule 6).
- Tests: 8 new in `StyleTests.fs` (a switched state on the element and inside it; what can't be switched; a state
  from two properties; own values, not a style's; both-or-neither; the Debug warning; `ApplicableStates`; a state
  read from a Slider's value), and the
  three tests that used the function form rewritten (the inspector's two now also hold that another element's
  state isn't listed). The two Debug-warning tests added today add their listener beside any other and take it
  away, after one that set it outright made `ImageCursorTests`' warning test fail once in a parallel run.
  References regenerated, each looked at: `styles-page@1x`, `styles-page-light@1x` (now 520 × 440: the
  hint's words and the slider), `gallery-phone-styles@3x` (those and the page's first line), `inspector-pick@1x`, `inspector-images@1x` (a TextBlock's and an ImageView's STATES row: the five
  built-in states, where there were ten).

## The Gallery uses the built-in palette (2026-11-19)

Tim: "let's update our gallery to not have the themes redefined in Common.fs", then "go with A plus" (A: the
Gallery's colours are the built-in palette's; plus: one small theme value of its own, kept as the example of
`ThemeValue.define` and `Theme.set`). `Common.fs` had twelve theme values and a light theme setting eleven of them:
a sample that showed every app needing a palette beside ours.

1. **`Common.fs` holds layouts only.** The twelve values and the two themes are gone from it.
2. **One theme value stays, on the Styles page:** `StylesPage.highlight` (what lights up under the pointer, a
   chosen chip's border). `GalleryApp.fs` has `darkTheme = Theme.dark []` and `lightTheme = Theme.light [ Theme.set
   StylesPage.highlight ... ]`, as an app would write them.
3. **What replaced the rest:** card, chip and box borders `Palette.border`; boxes' backgrounds `Palette.surface`;
   the counters' bar `Palette.border` with `Palette.accent` (below zero `Palette.danger`); Primary and Danger
   buttons' borders their background's own colour; every other row of the Scrolling page `Palette.surface`, the
   rest none; hovered things `Border.overlay Palette.hoverTint`, which serves a chosen chip too, so its own hover
   block is gone.
4. **New built-in theme value `Palette.textDanger`** (`Theme.textDanger`; dark 0xF7768E, light 0xC0392B): text that
   says something failed. `Palette.danger` is for behind text and too dark to read as text on the dark theme; the
   Gallery's failed load needed one, and so will most apps.
5. **The Images page's card has a fixed shadow** (blur 12, 4 down, black at 35%): `Palette.shadow` is a button's,
   too slight for "shadow and all", and a floating shadow as a theme value isn't worth a palette entry for one card.

**As built (2026-11-19)**
- Considered for the bar's empty part: `Palette.control`, which the Slider's track uses; on the light theme it is
  white on a near-white window, so the bar took `Palette.border`. The Slider's own track has that same low contrast
  on the light theme: left as it is, for Tim to judge.
- Tests name the themes as `GalleryApp.darkTheme` and `lightTheme`. One new unit test: `Palette.textDanger` has a
  contrast of at least 4.5 on each built-in theme's background and surface, and a theme can set it.
- References regenerated, each looked at beside the old one: `gallery@1x`, `gallery-light@1x`, `styles-page@1x`,
  `styles-page-light@1x`, `layout-page@1x`, `layout-grid-overlay@1x`, `images-page@1x`, `inspector-pick@1x`,
  `inspector-floating@1x`, and the phone's Components, Scrolling, Lists and Styles pages. What changed in them:
  border and bar colours, the Primary and Danger buttons' borders, the alternate rows, the card's shadow.

## Style.onWhere, and "where" for a condition on values (2026-11-19)

Tim: "Let's update Styles.onProp to also have an overload that takes a test. With that, should we get rid of
States.onProp" (meaning `States.ofProp`).

1. **`Style.onWhere prop test [ ... ]`**: `onProp` with a test where it has a value.
   `Slider.style [] [ Style.onWhere SliderProps.value (fun v -> v >= 80.0) [ Slider.fill Palette.danger ] ]`.
   Not an overload of `onProp`: F# refuses overloads of a curried member (FS0816). Named for `Style.where`, as
   `onProp` is for `Style.prop`. Considered: `onPropWhere` (longer), tupled overloads (would change every `onProp`,
   and are ambiguous for a property whose type is a function).
2. **`States.ofProp` stays public** (Tim asked not to be agreed with for its own sake; Claude's first answer had
   it go internal). The framework's own four states are made with it, and an element author's `Expander` wants the
   same; it is a named state (several styles share it, the inspector lists and forces it), where `onWhere` is a
   condition inside one style; and removing it saves no code. The docs steer: a look that follows a value is
   `Style.onWhere`; a state is for a condition that needs a name.
3. **One word for a condition on values: "where".** `States.test` is now `States.where` (it read as a verb that
   does something now); with `Style.where` and `Style.onWhere` that is the same word in a selector, a block and a
   state. Parameter names stay `test`. Considered: `predicate` (the functional-programming term, not a plain
   word); `when`, the better word for something that changes over time, but reserved in F# (it would be written
   ``States.``when`` ``), and compounds such as `Style.onWhen` and `States.activeWhen` give one idea two words.
4. **Two `onWhere` blocks are the same only when their test is the same function.** A style list made once is
   unaffected; a test written inside a view's inline block is new on every run and restyles its element each
   time. Documented on the member, as `onProp` already says it is for styles; no warning.

**As built (2026-11-19)**
- `Style.fs`: `Style.onWhere`; an `onWhere` block is kept under the same key as `onProp` blocks and compared by its
  test (`WhereTest`), an `onProp` block by its value as before. `States.where` replaces `States.test` (the Debug
  warning's key and both messages with it). A tool reads the rule as `Slider[Value?]`, as for `Style.where`.
- Gallery: the Styles page's slider is `Style.onWhere`; its `high` state is gone, so the slider's STATES row is the
  five built-in states. The hint reads "Drag past 80 and it turns red: Style.onWhere on its value". References
  regenerated and looked at: `styles-page@1x`, `styles-page-light@1x`, `gallery-phone-styles@3x` (the hint's and the
  page's first line's words).
- Tests: four new in `StyleTests.fs` (follows the value and nests with `onState`, beside `onProp` blocks; own
  value, not a style's; can't set app state; block equality by test function).
- Found on the way: a unit test failed twice in some thirty runs. One cause fixed: five tests set
  `DebugWarnings.Listener` outright and cleared it, taking away the listener of any warning test running beside
  them; all now add and remove their own. One cause left and not looked into: `the Gallery's Images page finds its
  embedded pictures and draws every one` failed once (no message kept); `ImageCache.DecodeInlineOnThisThread` is
  process-wide and one test turns it off, which is the first place to look.

## Style.prop and Style.where are for an ancestor's part only (2026-11-19)

Tim, after asking what a style written with `Style.where` looks like: "should we get rid of Style.where and have
them create a state instead?", then "make them ancestor only". Since "One way to say when", a condition on the
element itself is a block, but `Border.style [ Style.where p test ] [ ... ]` still worked, and the two members' doc
comments showed that form: two ways to write one thing.

1. **Kept, not replaced by states.** A state for an ancestor's one-off condition would have the app invent a name
   and register a process-wide state for it, and would allow an anonymous condition on the element (`onProp`,
   `onWhere`) but not on an ancestor. `Style.prop` would have had to go with it.
2. **In a style's own selector they throw when the style is made** (`X.style`, `Style.any`), naming the property
   and the block to write: `Style.onProp prop value [ ... ]` or `Style.onWhere prop test [ ... ]`. Inside
   `Style.inside` and `Style.parent`, at any depth, they are as before.
3. **One way to write each:** a block for the element's own condition, a selector part for an ancestor's, a state
   when the condition needs a name.
4. **A state in a style's own selector is left as it is** (`Border.style [ States.hover ] [ ... ]` still works):
   not asked, and the theme's own styles would need a look first.

**As built (2026-11-19)**
- `Selector.buildOwn` in `Style.fs`, used by `Style.forType` and `Style.any`; the ancestor parts keep `build`. Both
  members' doc comments show the ancestor form.
- Tests: the three that used the own-selector form are now blocks or ancestor parts (the selector-text test reads
  `Border[IsChecked=true] TextBlock`, `Button.primary[Level=2]:hover > TextBlock`, `Border[Level?] TextBlock`); one
  new test holds the rule. No picture changed.

## Controls written outside Core (2026-11-19)

Found while sketching a control for Tim ("an example of what it might look like for a user to author their own
control"): the two things every built-in control with a value uses, `ControlValue` and `AppState.mark`, were
internal, though CLAUDE.md's pattern told authors to use them. Tim: "Yes, build it" to the four recommendations.

1. **`ControlValue` is public as it was** (`isControlled`, `change`): its names are the glossary's, and it does
   nothing an author couldn't do with `Element.Set`.
2. **`Prop.appState name default affects`** makes a property that holds app state, beside `Prop.define`,
   `Prop.attached` and `Prop.local`. Not a public `AppState` module: one function in a top-level module of that
   name would read as where an app keeps its state. The built-in controls use it too (about 25 properties); the
   internal `AppState` module is gone.
3. **"App state" is a glossary term**: what an element gets from the app through its view and never from a
   style. The glossary had it only under App value's "avoid"; that line now says why.
4. **A control written with the public API alone proves it:** `samples/Gallery/RatingBar.fs`, shown on the
   Controls page ("A control of your own"). The samples can't see Core's internals; the tests can, so they
   couldn't show the surface was enough.

**As built (2026-11-19)**
- **One more thing had to open: `InputContext.Element`.** An input handler had no way to set a value of its own
  or to call `ControlValue.change`, which takes the element; every built-in control reaches it as `ctx.Element`.
  Now public on `InputContext` alone (measure, arrange and render contexts keep theirs internal: those stages
  read). Nothing else was missing: `Element.define` and its stages, `Setting.create` and `fromTheme`,
  `Semantics.rangeFrom`, `actions` and `onAction`, `FocusRing.drawAround`, `States.define`, `Style.forType`.
- `RatingBar`: a row of dots out of `maximum` (5); a click gives that many (the first again, none), the arrow keys
  move by one; controlled when the view gives `value`; a slider to a screen reader; its own state
  `RatingBar.full` (`States.define` with two `dependsOn` and a `where`: the runnable example of that form, which
  the code had only in tests). The Controls page shows one controlled, one keeping its own value out of 8, and a
  style turning a full one amber.
- Tests: three unit tests drive it on the fake platform (own value by click and keys; controlled; its state and a
  style setting its value throwing), one screenshot test (`rating-bar@1x`: part filled, full, another maximum, both
  themes). `gallery-phone-controls@3x` regenerated: the page is longer, so its scrollbar thumb is shorter.
- Not done: the control isn't in a screen reader's hands yet (checklist), and no AOT publish was run after this.

## Milestone summaries (moved from CLAUDE.md, 2026-11-16)

The working summaries kept in CLAUDE.md while each milestone was built, moved here unchanged when CLAUDE.md
was trimmed (evaluation, 2026-11-16). Older sections of this file use the words of the time ("token" is now
"theme value", "marker type" "settings type"; see GLOSSARY.md).

### Status as of 2026-11-16


Done and verified by Tim on Windows and in the browser: M0 (window, GPU), M0.5 (browser, draw on request), M1
(draw list), M2/M2b (text, emoji, fallback), M3 (elements, layout, wrapping), M3b (F#-style elements), M3c
(lengths), M4 (views, differ, App.run, Counter sample, builder trial), M5 (pointer, keyboard and focus, components and animation), plus the Hello toolbar (Pause/Restart/
Add/Remove/Shuffle cards, keyed grid). M5b (async), the tabbed Counter demo and "settings everywhere" are done and verified by Tim on Windows and in the browser (2026-11-15). M6 (layers, ScrollViewer, inspector with panel modes and properties) is done and verified (2026-11-16). M7 (step 1 styles; step 2a themes, with the inspector's forcing states, themed properties and selection corner marks; step 2b light theme, theme values and app values) is done and verified (2026-11-16). Last counts: 284 unit tests,
50 rendering tests (after M7b step 2, the Material package).

Remaining milestones (M7c custom title bars was skipped, 2026-11-16; see DESIGN.md): M7b icons (decisions in
DESIGN.md, "Decisions for M7b"; step 1, icons in Core, done and verified 2026-11-16: `PathGeometry` in Text,
`TextSystem.DrawIcon`, `Icon`/`IconView`/`Button.icon` in Core; plus the inspector's icon toolbar with hover hints,
also verified; step 2, the Material package `Scribble.Icons.Material` and the Icons sample, done and verified by Tim 2026-11-16;
step 3, image cursors (`Cursor.fromIcon`, `Cursor.Image` shown by the OS), done and verified by Tim 2026-11-16), M8 core controls
(Grid, DockPanel, Canvas, dashed lines in the renderer, rest of ScrollViewer (draggable thumb, keyboard, horizontal), virtualization (must keep scroll-to-item, see DESIGN.md), CheckBox, Image, Slider, ListBox, ComboBox, Dialog, Toast, Popup/portal, TabControl), M9 TextBox, M10 hot reload; later:
animation, accessibility, bidi/complex scripts, IME, macOS/Linux backends, mobile, Vulkan backend.

#### M5: agreed 2026-11-15 (full list in `docs/DESIGN.md`, "Decisions for M5")

Three steps, each tried by Tim before the next:
1. **Pointer** (done, verified by Tim on Windows and in the browser 2026-11-15; `src/Scribble.Core/Input.fs`): platform gaps (pointer left window, OS capture, modifiers, cursors); tunnel+bubble routing (preview
   settings only for pointer down/up, wheel, key down); enter/leave on the hovered chain; capture (drag off a button
   cancels the click); `isEnabled` (inherited); Button hover/pressed/disabled looks; `App.onPointerMove` removed.
2. **Keyboard** (done, verified by Tim on Windows and in the browser 2026-11-15): portable logical `Key` + modifiers (browser uses
   `e.key`, Win32 `MapVirtualKeyW`); routed `onKeyDown/Up` + Preview from the focused element (else the root);
   `focusable` (Button by default); focus manager in `InputRouter` (`MoveFocus Next/Previous`, view order,
   wraps; press focuses, invalid focus cleared after layout); `ctx.IsFocusVisible` + `FocusRing.draw`; Enter/Space
   activate (Space pressed while held, Escape cancels); `App.onKey (fun key mods -> ...)` only for unhandled keys;
   `ElementHost.HandleEvent` returns "handled".
3. **Components** (done, verified by Tim on Windows and in the browser 2026-11-15; `src/Scribble.Core/Component.fs`, `Animation.fs`):
   `Component.define "Name" (fun ctx props -> view)` returns `'P -> View`; the component's element hosts its
   state (`ComponentInstance`, hooks in slots checked for order) and its output as a child. Re-runs: own state
   (scheduled via `IElementRoot.ScheduleComponent`, run shallowest first at the start of `UpdateLayout`) or changed
   props (`PropKind.Component` setting compared by F# equality). Effects/subscriptions and "keep animating" go
   through `IElementRoot.AfterFrame`, run by `ElementHost` after the frame (outside `inFrame`, so they can ask for
   frames). Hooks: useState/useMemo/useEffect/useSubscription/useElmish/useAnimated. Element authors:
   `ctx.Animate` (tweens, `Easing`) and `Element.transform`; Button sinks to 97% with a flattening shadow. Text
   under an extra scale rasterizes at quarter-pixel sizes. Counter sample = components demo.

Views don't read element states directly: state-dependent looks are styles (M7).

#### M5b: async (decisions in `docs/DESIGN.md`, "Decisions for M5b")

`Dispatcher.fs`: one queue in Core (`Post`/`InvokeAsync`/`CheckAccess`/`RunPending`); platforms only `Wake`
(Win32 message-only window + `PostMessageW`, so it works during the move/resize modal loop; browser
`setTimeout(0)`). `dispatcher.Install()` (done by `App.run`/`runInBrowser`) sets a `SynchronizationContext` and
routes unobserved task exceptions to `OnError` (= `App.onError`). `State.Set/Update` and Elmish dispatch post
when off the UI thread (inline when the tree has no dispatcher, as in bare `ElementTree` tests). `Cmd.fs`: own
`Cmd` (`none/ofMsg/batch/map/ofSub/ofTask/ofAsync`); `App.programWithCmd`, `ctx.useElmishWithCmd`;
`ctx.useAsync(deps, fun d ct -> task|async)` → `AsyncValue.Loading|Loaded|Failed` (version counter drops late
results). Tests drain the fake platform's wakes by hand (`AsyncTests.fs`; use a real `Thread` for "other thread",
since `Task.Run(...).Wait()` can run inline).

#### M6: agreed 2026-11-15 (full list in `docs/DESIGN.md`, "Decisions for M6")

Three steps, each tried by Tim before the next:
1. **Layers** (done, verified by Tim on Windows and in the browser 2026-11-15; `Layer.fs`, `Elements/ScrollViewer.fs`; `Layer.modal` is the only switch, a modal layer blocks only while it shows something, component hosts aren't hit themselves) in Core: an ordered stack of element trees above the app (app, popups/toasts, modal, inspector on
   top), each with its own focus scope; options *pass-through* (pointer falls through where the layer has nothing)
   and *modal* (blocks everything below, keyboard too); public setting so packages can add layers. Plus a basic
   public vertical `ScrollViewer` (wheel, clip, thin scrollbar) brought forward from M8.
2. **Inspector** (done, verified by Tim on Windows and in the browser 2026-11-15, with step 2b's panel modes and the owned pop-out window; `src/Scribble.Inspector/Inspector.fs`; step 2b added panel modes: docked/floating/folded/popped out (desktop), see DESIGN.md; Core gained `App.onStart (AppContext)`, `host.Closed`/`Close()`, `IPlatform.SupportsMultipleWindows`, `host.AddKeyPreview`/`LayerChanged`/`FrameDrawn`, `isHitTestVisible`, `Element.ComponentName`, `IWindow.ClaimKey`) (`Scribble.Inspector`, opt-in: `Inspector.enable [ ... ]`, samples enable it; built from Scribble
   elements): floating panel docked right/left over the app (the app's layout never changes), tree of all layers,
   pick mode (Ctrl+Shift+C; the click doesn't reach the app), devtools-style highlight; F12 toggles (check the
   browser lets us keep F12).
3. **Properties panel** (done, verified by Tim on Windows and in the browser 2026-11-16; decisions and measurements in DESIGN.md, "Decisions for M6
   step 3"): below the tree behind a divider; states, layout numbers, properties tagged view/code/type (property
   defaults behind a "Show defaults" checkbox), component hooks, frame stats. Core gained public `element.Values`
   (`PropertyValue`, `ValueSource`), `element.OfferedSize`, `element.Hooks` (`HookValue`). Values are formatted by
   `Format.value` (never `string`/`ToString()` on F# records or unions: `%A`, crashes under NativeAOT);
   `Inspector.format` lets apps format their own types. Stats only change in an app-driven refresh (≤10/s) plus one
   "app idle" refresh a second after the app stops, so the inspector never keeps itself drawing.

Dialog, Toast and the "show this in a layer" element are M8.

#### M7: agreed 2026-11-16 (full list in `docs/DESIGN.md`, "Decisions for M7")

Two steps, each tried by Tim before the next:
1. **Styles engine** (done, verified by Tim on Windows and in the browser 2026-11-16; `src/Scribble.Core/Style.fs`): syntax A+D+E+F (Tim's
   choice): typed selector lists `Button.style [ Style.cls "primary"; States.hover ] [ Button.background c ]` (each
   element module has `style` next to `create`; `Style.any` for every type), nested `Style.on States.hover [ ... ]`
   blocks, inline `X.on States.hover [ ... ]` in views. A state is itself a selector part (no `Style.hover`
   shorthands, no `Style.state`); classes are plain strings (`X.classes [ "primary" ]`; no `Class.define`).
   Strings implicitly converted to selector parts are out (FS3391 under warnings-as-errors). Custom states are
   registered when made; `element.ActiveStates` (for tools; the inspector's states line) tests them all. Scopes:
   `App.styles`, any element's `styles` (it and its subtree); `Layer.appStyles false` opts a layer out (the
   inspector). Precedence: own value > inline blocks > styles (nearest scope first, later in a list wins) > type
   default > prop default; no specificity. A type selector matches that type exactly. Engine: per-element style
   values (`Get`: own, styles, defaults); property selectors `Style.prop p v` / `Style.where p f` and
  `States.ofProp` read the *unstyled* value and re-match when a tested prop changes (`Prop.TestedByStyles`); restyles queued (`IElementRoot.RequestRestyle`) and run at the start of
   `UpdateLayout` after components; a tree ignores requests until something uses styles. **A setting the view
   drops is now removed** (`ClearValue`), not stored at its default, so styles show through. Styles can't set
   children/classes/styles/name/key. `ValueSource.Style` + `PropertyValue.Selector` for the inspector.
2. **Themes** (decisions in DESIGN.md, "Decisions for M7 step 2"). **2a done, verified by Tim on Windows and in the browser
   2026-11-16, with the inspector changes since step 1** (`Theme.fs`, `Theme` in Style.fs): `Theme.dark [ Theme.accent c; ... ]` (settings, readable palette),
   `App.theme`/`ElementHost.theme` (hosts default to one shared `Theme.dark []`; a bare `ElementTree` has none) and
   an element `theme` setting (nearest wins); theme styles are matched first (own > inline > styles > app styles >
   theme > type defaults). State looks are general settings in the theme's styles: `Border.overlay` (hover/press
   tint), `opacity` (disabled; per-shape alpha via `DrawList.PushOpacity`), `scale` + `shadow` (press); Button's
   render is Border's plus the focus ring. Inherited `foreground`/`font`/`fontSize` (Layoutable settings; own >
   styles > type default > ancestor > theme base > prop default; `ValueSource.Inherited`). `Style.transition prop
   duration` (render-only props with an interpolator; tween starts when the new value is first read, frames only
   while moving). **2b done with theme values (Tim's choice after reading 2b's first code), verified by Tim on
   Windows and in the browser 2026-11-16:** `Theme.light`; **theme values** (`ThemeValue<'T>`, `ThemeValue.define "Name" default`; built-ins
   in `Palette`: surface, accent, text, hoverTint, shadow, cornerRadius, ...) that settings take in place of values
   (`Border.background Palette.surface`; element authors: `Setting.fromTheme prop themeValue`), resolved from the nearest
   theme when read (`Element.ThemeValueOf`; each element remembers its nearest theme until a global epoch is bumped by a move or theme change; stored as an internal `ThemeRef`); themes set them (`Theme.set themeValue v`,
   shorthands `Theme.accent c`); code reads `theme.Get Palette.accent`, render functions `ctx.Theme themeValue`. The
   built-in looks are one style list (using theme values) shared by every theme. The window's theme is the built-in app value
   `Theme.current` (`host.WindowTheme`: layers with app styles and the background follow it; `App.theme` and
   `ElementHost.theme` set its start; an element's `theme` applies only inside it, the root's too). Counter demo:
   `App.theme darkTheme`, `App.view (demo ())`; its `ThemeSwitch` sets `Theme.current`. Following the OS setting is later.
   **App values (done with 2b, verified 2026-11-16):** `AppValue.define "Name" default`; `ctx.useAppValue v`
   (a `State<'T>`, now backed by functions); one `AppValues` store per app (`App.start` makes it; `ElementHost.appValues`;
   every layer's tree gets it; a bare `ElementTree` has its own); `App.value v x`, `AppContext.Get/Set`,
   `Cmd.setAppValue` (Cmd effects receive the store). Components first run before joining the tree, so
   `Element.RealizingIn` (thread-static) tells them which tree is being built. Decisions in DESIGN.md.
   **Window title as an app value (done, verified by Tim 2026-11-16):** `WindowTitle.current` (AppValue.fs);
   `App.title` sets its start; `App.start` follows it (stats appended); `AppContext.Title` reads it; the popped-out
   inspector follows it. Public `AppValues.Subscribe(value, fun v -> ...)`. Counter demo titles the selected tab.

#### Later notes

- Game UI hosts (Godot first, Unreal via a NativeAOT C API; see DESIGN.md "Later: Scribble as game UI"). Keep: the
  draw list as plain data, no assumption that the core owns the window or loop, low per-frame allocations.

- M7b icons: modeled on Tim's `C:\Users\Timot\Source\dotnet\CustomAvaloniaControls\CustomAvaloniaControls\Icons`
  (MaterialIcon: immutable icon values, "star-filled-rotate90" strings, typed names, FromPath, brotli data decoded
  on first use). Plan: rasterize icon paths through the glyph rasterizer into the atlas; opt-in
  `Scribble.Icons.Material` package. Open: names/casing, pluggable sets, .svg import. Also custom cursors: a
  `Cursor.Image` (bitmap + hotspot, e.g. from an icon) shown by the OS as a hardware cursor, never drawn in-frame.
- Known issues: shrinking a window briefly flashes black at the edge with Vulkan on NVIDIA (D3D12 only slightly
  better; left as is). With G-SYNC, the cursor stutters over a Scribble window on Vulkan (not on D3D12, which
  presents through DirectComposition; "Cursor stutter with G-SYNC", 2026-11-17).
- Phones (2026-11-16): the samples fit 390-DIP-wide screens (`PhoneTests.fs` renders them at 390 × 844, 3×);
  touch drags scroll a ScrollViewer (`Element.pan`); WebGPU compatibility mode is requested when full WebGPU
  isn't offered (OpenGL ES phones), which is why `shapes.wgsl` uses `@interpolate(flat, either)`.
- Open questions: license (MIT or Apache 2.0) before the first public push; is "Scribble" free on NuGet;
  `RenderOffscreen` must become async for browser screenshot tests (closed 2026-11-18: `RenderOffscreenAsync`).
