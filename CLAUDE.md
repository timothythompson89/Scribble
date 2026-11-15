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
| `src/Scribble.Core` | Views, differ, elements, layout, Border/StackPanel/TextBlock/Button, App settings and runtime |
| `src/Scribble.Desktop` / `src/Scribble.Browser` | `App.run` / `App.runInBrowser`: one reference brings platform, GPU and shaping |
| `samples/Shared` | Dashboard, HelloApp, Counter (shared by desktop and browser hosts) |
| `tests/Scribble.Tests`, `tests/Scribble.Tests.Rendering` | Unit tests; GPU screenshot tests |

## Architecture in brief

- **Rendering:** everything is one instanced draw call per frame. Shapes are 96-byte instances in a storage buffer
  (SDF rounded rects, borders, Gaussian shadows, per-shape pixel clip); glyphs are shape kinds that sample an R8
  coverage atlas (2048²) or an RGBA color atlas (1024²). Edges snap to physical pixels by default (`Precise` opts out).
- **Text:** own parser (cmap 4/12, glyf incl. composites, COLR v0/CPAL), font-rs-style accumulation rasterizer, 4
  horizontal subpixel positions, no hinting; HarfBuzz shaping cached per string; fallback per grapheme cluster.
- **Frames on request only:** windows call `RequestFrame()`; nothing draws while nothing changes. `ElementHost`
  ignores its own tree's invalidations while a frame is being drawn (they're handled by that frame).
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
```

Settings are static members on the type (inherited, so `Button.background` works); `Type` and `create` live in a
same-named module (modules aren't inherited, so nothing collides with Border's). Helpers go in the module as
`private` (or `internal` when a derived element needs them, like `Border.draw` for Button).

## Status (2026-11-15)

Done and verified by Tim on Windows and in the browser: M0 (window, GPU), M0.5 (browser, draw on request), M1
(draw list), M2/M2b (text, emoji, fallback), M3 (elements, layout, wrapping), M3b (F#-style elements), M3c
(lengths), M4 (views, differ, App.run, Counter sample, builder trial), M5 (pointer, keyboard and focus, components and animation), plus the Hello toolbar (Pause/Restart/
Add/Remove/Shuffle cards, keyed grid). Last counts: 135 unit tests, 31 rendering tests (after click args).

Remaining milestones: **M5b async in components** (next), M6 inspector, M7 styles, M7b icons, M8 core controls
(Grid, DockPanel, ScrollViewer, CheckBox, Image, Slider, ListBox, ComboBox), M9 TextBox, M10 hot reload; later:
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

Views don't read element states directly (that's M7 styles). Async/dispatcher is **M5b**.

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
