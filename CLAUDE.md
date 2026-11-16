# Scribble: notes for Claude

Scribble is a public, open UI framework for F#, written from the GPU up, with a FuncUI-style DSL. Targets:
Windows/macOS/Linux first (Windows is the dev platform), then the browser (WebAssembly, already working), later
phones, TVs and consoles. The full history of decisions is in `docs/DESIGN.md` (exported from the design doc that
was kept on claude.ai until 2026-11-15); this file is the working summary.

## How we work (important)

- **Discuss the plan before writing any code.** The owner (Tim) tends to second-guess and wants to see decisions,
  options and a recommendation first. For each milestone: list what it covers, the decisions it needs (with a
  recommendation for each), wait for agreement, record decisions in `docs/DESIGN.md`, then build.
- Claude writes the code; Tim tests in **Rider on Windows** (and in Chrome/Edge for the browser) and gives feedback.
- **No C# projects**, ever if at all possible. Everything is F#.
- Low level by intent: windowing, rendering, layout and text are our own code. Borrowed: WebGPU (wgpu-native) and
  HarfBuzz only.
- Everything must work under **NativeAOT** and trimming (and WebAssembly AOT).
- Keep `docs/DESIGN.md` and `README.md` current at the end of each milestone (status, decisions, what to check).
- When a milestone is done, report: what changed, results (tests, pixel checks, timings), what couldn't be
  verified, and exactly what Tim should try.
- **What Tim should try goes in `docs/CHECKLIST.md`** (Tim, 2026-11-16): overwrite it each time something is
  finished, with only the steps for checking that work (checkboxes, grouped by area, how to run it at the top),
  not items Tim has already verified (keep anything finished but not yet checked, updated to the current
  behaviour). When Tim asks for "the checklist", point to it and summarize; the README keeps its long-lived
  "What to check" sections.

## Build, run, test (Windows)

```powershell
dotnet build Scribble.slnx
dotnet test tests/Scribble.Tests                  # unit tests (layout, differ, text, App runtime on a fake platform)
dotnet test tests/Scribble.Tests.Rendering        # GPU screenshot tests (needs a GPU)
dotnet run --project samples/Scribble.Samples.Hello
dotnet run --project samples/Scribble.Samples.Counter
dotnet run --project samples/Scribble.Samples.Browser            # http://localhost:5280
dotnet run --project samples/Scribble.Samples.Counter.Browser    # http://localhost:5281
dotnet publish samples/Scribble.Samples.Hello -c Release         # NativeAOT exe (needs VS C++ tools)
dotnet publish samples/Scribble.Samples.Browser -c Release -p:BrowserAot=true   # wasm AOT (wasm-tools workload)
```

- Screenshot tests compare against PNGs in `tests/Scribble.Tests.Rendering/References` with a tolerance (channel
  difference 6, 0.5% of pixels). **The references were generated on a software Vulkan GPU (lavapipe) on Linux**;
  a real GPU may differ slightly at anti-aliased edges. If a test fails only by a little on Tim's machine, look at
  the diff before regenerating. `SCRIBBLE_UPDATE_SNAPSHOTS=1` rewrites references; `SCRIBBLE_REPO_ROOT` overrides
  where they're found. Some tests (dashboard, counter) compare two renderings with each other and must be 0 pixels.
- Tests use real xUnit (until 2026-11-15 they ran against a local stand-in; the first real run on Tim's machine
  passed everything, on a real GPU too).
- `SCRIBBLE_GPU_BACKEND=d3d12|vulkan` forces a wgpu backend.
- wgpu-native v29.0.1.1 is downloaded on first build into `native/` (git-ignored) by `build/WgpuNative.targets`,
  imported by `Scribble.Desktop`; the DLL flows to every app that references it.
- Performance numbers: always measure steady state (warm up, then best of N). .NET first runs unoptimized code;
  single cold runs were misleading once (M3 reported 3.5 ms; the real figure was under 1 ms).

## Projects

| Project | What it is |
| --- | --- |
| `src/Scribble.Platform` | Window/event/DPI/frame-clock abstractions (`IPlatform`, `IWindow`, `PlatformEvent`) |
| `src/Scribble.Platform.Win32` | Hand-written Win32 (per-monitor DPI v2, draw-on-request loop) |
| `src/Scribble.Platform.Browser` | Browser platform: full-page canvas, JS interop in pure F# |
| `src/Scribble.Rendering` | Draw list, colors, geometry, glyph atlas, `shapes.wgsl` |
| `src/Scribble.Rendering.WebGPU` | webgpu.h bindings and renderer (wgpu-native) |
| `src/Scribble.Rendering.WebGPU.Browser` | `navigator.gpu` renderer |
| `src/Scribble.Text` | TrueType parser, rasterizer, text system, line breaking, wrapping/trimming; Inter built in |
| `src/Scribble.Text.HarfBuzz` / `.Browser` | Shaping: native HarfBuzz / harfbuzzjs wasm (identical output) |
| `src/Scribble.Text.Emoji` | Opt-in Twemoji COLRv0 font (CC-BY 4.0 art: apps must credit) |
| `src/Scribble.Core` | Views, differ, elements, layout, styles, themes, components, app values, Border/StackPanel/TextBlock/Button/ScrollViewer, App settings and runtime |
| `src/Scribble.Desktop` / `src/Scribble.Browser` | `App.run` / `App.runInBrowser`: one reference brings platform, GPU and shaping |
| `src/Scribble.Inspector` | Opt-in inspector (`Inspector.enable [ ... ]`): panel and highlight layers, tree, pick mode |
| `samples/Shared` | Dashboard, HelloApp, Counter (shared by desktop and browser hosts) |
| `tests/Scribble.Tests`, `tests/Scribble.Tests.Rendering` | Unit tests; GPU screenshot tests |

## Architecture in brief

- **Rendering:** everything is one instanced draw call per frame. Shapes are 96-byte instances in a storage buffer
  (SDF rounded rects, borders, Gaussian shadows, per-shape pixel clip); glyphs are shape kinds that sample an R8
  coverage atlas (2048²) or an RGBA color atlas (1024²). Edges snap to physical pixels by default (`Precise` opts out).
  Elements whose bounds + `Element.RenderOverflow` (64 DIPs) lie outside the clip aren't rendered (scrolled-out rows).
- **Text:** own parser (cmap 4/12, glyf incl. composites, COLR v0/CPAL), font-rs-style accumulation rasterizer, 4
  horizontal subpixel positions, no hinting; HarfBuzz shaping cached per string; fallback per grapheme cluster.
- **Frames on request only:** windows call `RequestFrame()`; nothing draws while nothing changes. `ElementHost`
  ignores its own tree's invalidations while a frame is being drawn (they're handled by that frame).
  **Power rule:** never add frames: none while idle, at most one per display refresh while something changes
  (Mailbox presentation was rejected for this). Desktop surfaces keep one frame in flight
  (`WebGpuDevice.frameLatency`, default 1) so drags lag the mouse least; that changes when frames show, not how many.
- **Elements (since M3b):** properties are typed keys (`Prop<'T>`, declaring `Affects.Measure/Arrange/Render`);
  an element type is defined settings-style (`Element.define "Name" [ Element.measure ...; Element.render ...;
  Element.defaults [...] ]`, or `Element.extend baseType`), stored as an `ElementType` record of stage functions
  plus per-type defaults (`Get`: own value, else type default, else prop default); settings are values created by
  static members on **marker types** (`Border`, `TextBlock`, ...) that inherit `Layoutable`/`Panel`, so wrong
  settings don't compile and shared ones appear everywhere (`TextBlock.margin`); `Type` and `create` live in a
  same-named module (since 2026-11-15). No nulls in the public API. Layout: WPF-style measure/arrange, layout rounding half-up,
  `Length` = Auto | Px | Percent | Star (strings "45px", "45%", "2*", "auto"); StackPanel shares leftover space
  among Star children only when its length is bounded.
- **Views and the differ (M4):** `X.create [ settings ]` returns a `View` (type + settings; child properties hold
  Views). `ElementTree.Update view` realizes the first time and patches after that: same type in the same place is
  patched setting by setting (missing settings reset to the type's default, else the prop's), children match by position or by `key`
  (`Panel.items list keyFn viewFn`), an identical View object is skipped. Element types must be static values
  (compared by reference).
- **Apps (M4):** `App.program init update view` (Elmish; an equal model causes no rebuild), `App.onKey`,
  `App.onFrame` (animation continues only while the model keeps changing), `App.fonts`, `App.showStats`,
  `App.closeOnEscape`. `App.start` runs on any `IPlatform` (tests use a fake). Pointer and keyboard input (M5): see README
  "Pointer input" and "Keyboard and focus"; `Button` is a Border with a default look that reacts to hover/pressed/disabled.
  `onClick` takes `ClickEventArgs` (`Modifiers`, `Source`); write `fun _ -> ...` when unused (no unit overload: F# can't resolve `fun _ ->` between them).
- **Syntax: FuncUI-style settings lists only** (decided 2026-11-15; the M4 builder trial was removed). Don't
  reintroduce computation-expression or modifier-chain syntax.
- **Settings lists for every user-facing API where possible** (Tim, 2026-11-15): not just views, but anything an
  app author configures (`App.run [ App.title ...; App.size ... ]`, `Element.define "X" [ Element.measure ... ]`
  already follow it). When designing a new public API, default to `Thing.create [ Thing.option v ]` / `[ X.a; X.b ]`
  over records with `{ Default with ... }`, optional arguments or builders, and say so if it doesn't fit. Internal
  code is free. Settings are static members on the type they configure (`WebGpuDevice.backend`,
  `ElementHost.background`), applied to an internal options record. Done 2026-11-15: `WebGpuDevice.Create [ ... ]`,
  `BrowserGpuDevice.CreateAsync [ ... ]` (shared `GpuPower`), `ElementHost.create window device text [ ... ]`,
  `TextSystem.create device shaper [ TextSystem.fallbacks ... ]` (atlas-only constructors are internal, for tests).
  Agreed exceptions: `App.program init update view` stays positional (types), platform contracts
  (`WindowOptions`, `IPlatform`) stay records/interfaces, component props stay `'P -> View`, hooks/`Cmd`/
  `ctx.Animate` stay calls. Details in `docs/DESIGN.md` ("Settings everywhere").

## Rules and gotchas

- `TreatWarningsAsErrors`; `WarningsNotAsErrors` IL2104;IL3053 (FSharp.Core's own trim warnings). Libraries set
  `IsScribbleLibrary=true` → `IsAotCompatible`/`IsTrimmable`.
- **No F# printf family** in shipped code (`printfn`, `sprintf`, `failwithf`, `%A`, `$"..."` with `%d`-style
  specifiers): crashes under NativeAOT. Use .NET interpolation (`$"{x:F1}"`) and `Console.WriteLine`. A literal `%`
  inside `$"..."` is read as a format specifier: concatenate it as a separate plain string.
- Native interop: `DllImport` with blittable signatures only (F# can't use LibraryImport/JSImport generators);
  callbacks via `Marshal.GetFunctionPointerForDelegate<'T>` (generic overload) with the delegate kept alive; struct
  layouts checked against the C headers. Browser interop: `JSFunctionBinding.BindJSFunction/InvokeJS`; void returns
  must be `JSMarshalerType.Discard`; JS modules are embedded in the DLLs and imported as `data:` URLs.
- In recursive type groups (`type ... and ...`), annotate generic members explicitly (`member _.Get<'T>(p: Prop<'T>) : 'T`)
  or F# infers them non-generically.
- Record field ambiguity: `Scribble.Core.Size`, `Scribble.Platform.DipSize` and `PixelSize` share `Width`/`Height`;
  annotate record literals (`let s: PixelSize = { ... }`). Consolidating these is an open item.
- A module and a type can't share a name across files in one namespace (FS0250); entry-point modules must not
  create a namespace equal to an existing module (e.g. `Scribble.Samples.Counter.Program` clashes with module
  `Scribble.Samples.Counter`).
- UI-thread-only access to elements (checked in debug builds). Fonts are shared process-wide, so caches inside
  `FontFace`/`Font` must stay thread-safe (rendering test classes run in parallel on different threads).
- Never put `create` on a marker type as a static member: statics are inherited, and `Button.create [ Button.width
  100.0 ]` became ambiguous with `Border.create` (FS0041). It lives in a same-named module in the same file.
- Core's `Cursor`, `Modifiers`, `MouseButton`, `PointerKind` are aliases of the Scribble.Platform types.
- Rider's F# project view hides linked files whose `Link` has a folder (`Link="Counter/Common.fs"`): link shared
  files flat (`Link="Common.fs"`), as the samples and tests do.
- Making an internal F# value public in Core once left the Inspector seeing it as internal (stale reference assembly
  after an incremental build: FS1094); `dotnet build --no-incremental` fixed it.
- Rider once silently overwrote `Directory.Build.props`; if a fix "doesn't take", check the file on disk. For the
  browser samples Rider's default browser must be "First listed" (Blazor launch quirk).

## Adding an element (the pattern)

```fsharp
module ProgressBarProps =
    let value = Prop.define "Value" 0.0 Affects.Render          // Prop.local for caches; Prop.child/children for children

[<AbstractClass>]
type ProgressBar =                                               // the settings (always the generic 'k form)
    inherit Layoutable                                           // or Panel / Border to inherit their settings
    static member value<'k when 'k :> ProgressBar>(v: float) : Setting<'k> = Setting.create ProgressBarProps.value v

module ProgressBar =                                             // the element: same name, same file
    let Type =                                                   // defined once: the differ compares by reference
        Element.define "ProgressBar" [                           // Element.extend Border.Type "Button" [ ... ] to start from another
            Element.measure (fun _ _ -> Size.Create(0.0, 8.0))   // left out: largest child / children fill / draws nothing
            Element.render (fun ctx list ->
                list.FillRect(Rect.Create(0.0, 0.0, ctx.Size.Width * ctx.Get ProgressBarProps.value, 8.0), Color.White))
            Element.defaults [ Layoutable.height 8.0 ]           // per-type defaults (plain values, not children)
        ]
    let create (settings: Setting<ProgressBar> list) = View.Create(Type, settings)
    let style parts (settings: Setting<ProgressBar> list) = Style.forType Type parts settings   // its styles (M7)
```

Settings are static members on the type (inherited, so `Button.background` works); `Type` and `create` live in a
same-named module (modules aren't inherited, so nothing collides with Border's). Helpers go in the module as
`private` (or `internal` when a derived element needs them, like `Border.draw` for Button).

## Status (2026-11-16)

Done and verified by Tim on Windows and in the browser: M0 (window, GPU), M0.5 (browser, draw on request), M1
(draw list), M2/M2b (text, emoji, fallback), M3 (elements, layout, wrapping), M3b (F#-style elements), M3c
(lengths), M4 (views, differ, App.run, Counter sample, builder trial), M5 (pointer, keyboard and focus, components and animation), plus the Hello toolbar (Pause/Restart/
Add/Remove/Shuffle cards, keyed grid). M5b (async), the tabbed Counter demo and "settings everywhere" are done and verified by Tim on Windows and in the browser (2026-11-15). M6 (layers, ScrollViewer, inspector with panel modes and properties) is done and verified (2026-11-16). M7 (step 1 styles; step 2a themes, with the inspector's forcing states, themed properties and selection corner marks; step 2b light theme, theme tokens and app values) is done and verified (2026-11-16). Last counts: 249 unit tests,
37 rendering tests (after the window title app value and the first-frame fix).

Remaining milestones: M7c custom title bars (Win32 first; the popped-out
inspector looks like the docked panel; see DESIGN.md), M7b icons, M8 core controls
(Grid, DockPanel, Canvas, dashed lines in the renderer, rest of ScrollViewer, CheckBox, Image, Slider, ListBox, ComboBox, Dialog, Toast, Popup/portal, TabControl), M9 TextBox, M10 hot reload; later:
animation, accessibility, bidi/complex scripts, IME, macOS/Linux backends, mobile, Vulkan backend.

### M5: agreed 2026-11-15 (full list in `docs/DESIGN.md`, "Decisions for M5")

Three steps, each tried by Tim before the next:
1. **Pointer** (done, verified by Tim on Windows and in the browser 2026-11-15; `src/Scribble.Core/Input.fs`): platform gaps (pointer left window, OS capture, modifiers, cursors); tunnel+bubble routing (preview
   settings only for pointer down/up, wheel, key down); enter/leave on the hovered chain; capture (drag off a button
   cancels the click); `isEnabled` (inherited); Button hover/pressed/disabled looks; `App.onPointerMove` removed.
2. **Keyboard** (done, verified by Tim on Windows and in the browser 2026-11-15): portable logical `Key` + modifiers (browser uses
   `e.key`, Win32 `MapVirtualKeyW`); routed `onKeyDown/Up` + Preview from the focused element (else the root);
   `focusable` (Button by default); focus manager in `InputRouter` (`MoveFocus Next/Previous`, tree order,
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

### M5b: async (decisions in `docs/DESIGN.md`, "Decisions for M5b")

`Dispatcher.fs`: one queue in Core (`Post`/`InvokeAsync`/`CheckAccess`/`RunPending`); platforms only `Wake`
(Win32 message-only window + `PostMessageW`, so it works during the move/resize modal loop; browser
`setTimeout(0)`). `dispatcher.Install()` (done by `App.run`/`runInBrowser`) sets a `SynchronizationContext` and
routes unobserved task exceptions to `OnError` (= `App.onError`). `State.Set/Update` and Elmish dispatch post
when off the UI thread (inline when the tree has no dispatcher, as in bare `ElementTree` tests). `Cmd.fs`: own
`Cmd` (`none/ofMsg/batch/map/ofSub/ofTask/ofAsync`); `App.programWithCmd`, `ctx.useElmishWithCmd`;
`ctx.useAsync(deps, fun d ct -> task|async)` → `AsyncValue.Loading|Loaded|Failed` (version counter drops late
results). Tests drain the fake platform's wakes by hand (`AsyncTests.fs`; use a real `Thread` for "other thread",
since `Task.Run(...).Wait()` can run inline).

### M6: agreed 2026-11-15 (full list in `docs/DESIGN.md`, "Decisions for M6")

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

### M7: agreed 2026-11-16 (full list in `docs/DESIGN.md`, "Decisions for M7")

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
   while moving). **2b done with theme tokens (Tim's choice after reading 2b's first code), verified by Tim on
   Windows and in the browser 2026-11-16:** `Theme.light`; **tokens** (`ThemeValue<'T>`, `ThemeValue.define "Name" default`; built-ins
   in `Palette`: surface, accent, text, hoverTint, shadow, cornerRadius, ...) that settings take in place of values
   (`Border.background Palette.surface`; element authors: `Setting.fromTheme prop token`), resolved from the nearest
   theme when read (`Element.TokenValue`; each element remembers its nearest theme until a global epoch is bumped by a move or theme change; stored as an internal `ThemeRef`); themes set them (`Theme.set token v`,
   shorthands `Theme.accent c`); code reads `theme.Get Palette.accent`, render functions `ctx.Theme token`. The
   built-in looks are one token style list shared by every theme. The window's theme is the built-in app value
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

### Later notes

- Game UI hosts (Godot first, Unreal via a NativeAOT C API; see DESIGN.md "Later: Scribble as game UI"). Keep: the
  draw list as plain data, no assumption that the core owns the window or loop, low per-frame allocations.

- M7b icons: modeled on Tim's `C:\Users\Timot\Source\dotnet\CustomAvaloniaControls\CustomAvaloniaControls\Icons`
  (MaterialIcon: immutable icon values, "star-filled-rotate90" strings, typed names, FromPath, brotli data decoded
  on first use). Plan: rasterize icon paths through the glyph rasterizer into the atlas; opt-in
  `Scribble.Icons.Material` package. Open: names/casing, pluggable sets, .svg import. Also custom cursors: a
  `Cursor.Image` (bitmap + hotspot, e.g. from an icon) shown by the OS as a hardware cursor, never drawn in-frame.
- Known issues: shrinking a window briefly flashes black at the edge with Vulkan on NVIDIA (D3D12 only slightly
  better; left as is). The Hello toolbar overlaps the subtitle below ~800 DIPs wide (needs Grid/DockPanel).
- Open questions: license (MIT or Apache 2.0) before the first public push; is "Scribble" free on NuGet;
  `RenderOffscreen` must become async for browser screenshot tests.
