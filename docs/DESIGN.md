# Scribble — design doc

Nov 14, 2026 · @Tim

## Vision and goals

Scribble is a public, open UI framework written in F# from the GPU up, with a declarative DSL in the spirit of Avalonia.FuncUI. It should run anywhere .NET NativeAOT runs: desktops first, then phones, TVs and consoles.

- **Audience:** F# developers building apps; the framework is public, so the API is a product.
- **Platforms:** Windows, macOS and Linux first; Android, iOS, TVs and consoles later. Windows is the development platform.
- **Scope:** UI only. Platform-specific features (notifications, tray icons, file pickers) wait until the core is solid.
- **Low level by intent:** windowing, rendering, layout and text are our own code. We borrow only a GPU abstraction (WebGPU) and a text shaper (HarfBuzz).
- **Look:** one custom Scribble look on every platform, not native imitation.

## Non-goals and deferred work

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

## Architecture and solution layout

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

## Platform layer

Win32 comes first, hand-written over P/Invoke, behind a small platform interface that later backends (Cocoa, Wayland/X11, Android, iOS, consoles) implement.

- **Interface:** `IPlatform` creates windows and runs the event loop; `IWindow` exposes size in physical pixels, scale factor, a native handle for the GPU surface, and an event stream.
- **Events:** one discriminated union (`PlatformEvent`) for resize, DPI change, pointer, keyboard, text input, focus and close. The core never sees Win32 messages.
- **DPI:** per-monitor DPI awareness v2 from day one. Layout works in device-independent pixels (DIPs); the renderer works in physical pixels. `WM_DPICHANGED` applies the suggested window rect and re-raises the scale.
- **Pointer model:** events carry a pointer kind (mouse, touch, pen) and id, so multi-touch fits later without new event types.
- **Focus model:** keyboard Tab order now; directional focus (up, down, left, right) designed in for gamepads and TV remotes.
- **Frame loop:** the platform drives frames (vsync through the swap chain); a frame clock gives `delta` and `total` time to the core.

## Rendering

Scribble draws through its own small render interface; the first backend is WebGPU via wgpu-native, and a raw Vulkan backend follows as the low-level path and the route to Switch.

- **Two levels.** A backend-neutral GPU interface (buffers, textures, pipelines, command encoding, surface present) and, above it, a 2D **draw list** that controls and the inspector write to: rects, rounded rects, borders, shadows, images, glyph runs, clips and transforms.
- **Shapes by signed-distance fields.** Rounded rects, borders and shadows are one instanced quad pipeline with SDF shaders, so they stay crisp at any DPI. Arbitrary paths come later.
- **WebGPU specifics.** Own `LibraryImport` bindings over the core `webgpu.h`, no implementation extensions, so wgpu-native can be swapped for Dawn. Shaders in WGSL.
- **Redraw strategy.** Redraw the whole window each frame at first; dirty-region tracking only if profiling demands it.
- **Headless mode.** The renderer can target an offscreen texture and read it back, which powers screenshot tests.
- **Later backends:** Vulkan, then Metal or D3D12 only if WebGPU falls short; console backends only with platform-holder access.

## Text

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

## Core: elements, layout, input and styles

The core is a retained tree of elements that the DSL's virtual tree is patched into; it owns layout, hit testing, routed events, focus and styling.

- **Elements:** mutable, long-lived nodes with typed properties. Each has a measure/arrange pass, a render pass into the draw list, and an optional semantic description for later accessibility.
- **Layout:** WPF/Avalonia style. `Measure(available)` returns desired size; `Arrange(rect)` places children. Units are DIPs. Panels: StackPanel, Grid, DockPanel later, Canvas later. Layout invalidation is incremental (dirty flags up the tree).
- **Input:** hit testing on arranged bounds and clips; routed events tunnel then bubble (preview and normal), with pointer capture.
- **Focus:** a focus manager handles Tab order now and directional navigation later.
- **Styles:** selectors and cascading styles, as in Avalonia or CSS: type, class, name and state selectors (`:hover`, `:pressed`, `:focus`, `:disabled`), descendant and child combinators. Precedence: inline attribute over style over theme default. Built in stages: inline and a theme record first, selectors once controls exist.

## Programming model

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

## Tooling and testing

An inspector arrives early because layout bugs are hard to see; hot reload comes later; tests cover the logic and the pixels.

- **Inspector:** a toggleable overlay (F12) that shows the element tree, layout bounds, margins, computed styles and which selector set each value. Later: frame-time graph and draw-call counts.
- **Hot reload (later, desktop only):** a dev host watches source files, recompiles UI code with FSharp.Compiler.Service, loads it into a collectible `AssemblyLoadContext`, re-runs the root component and lets the differ patch. View-only swap first; state serialization later. Never shipped in AOT builds.
- **Unit tests:** differ and patcher, layout (measure/arrange results for each panel), style selector matching and precedence, text line breaking.
- **Screenshot tests:** render scenes headless through WebGPU, compare to reference PNGs with a small tolerance. When the Vulkan backend exists, both backends render the same scenes.
- **IDE:** developed in Rider on Windows; nothing should depend on a particular IDE.

## Engineering rules

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

## Milestones

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
- [ ] **M6 — Inspector.** Overlay with tree, bounds and properties.
- [ ] **M7 — Styles.** Theme record, then selectors and cascading styles.
- [ ] **M7b — Icons.** SVG path icons drawn through the glyph rasterizer and atlas; an opt-in Material Symbols package (typed names, "star-filled-rotate90" strings, your own path data), modeled on the CustomAvaloniaControls MaterialIcon design. After styles, so icons can inherit the text color. Open: element and name casing, pluggable icon sets, .svg file import. Also custom cursors (agreed 2026-11-15): a `Cursor.Image` case (bitmap + hotspot, e.g. made from an icon) that the OS shows as a hardware cursor (Win32 `CreateIconIndirect`, re-made per scale on `ScaleChanged`; browser `cursor: url(data:…) x y` / `image-set`). Not drawn in our frame: that lags a frame or two, forces a frame on every mouse move, stops at the window edge and ignores the user's cursor settings. Things that follow the pointer (drag ghosts) are drawn in-frame, with drag and drop.
- [ ] **M8 — Core controls.** Grid, DockPanel, ScrollViewer, CheckBox, Image, Slider, ListBox, ComboBox, TabControl (API, controlled or internal selection, lazy pages, keep-alive policy and arrow keys to be decided; after M7 so the selected tab has a proper look).
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
- **Tab order:** tree order, wrapping; built as `Move(direction)` with Next/Previous now, arrows later. tabIndex and focusing from code later.
- **Focus ring: elements draw it** (`ctx.IsFocusVisible` and a shared `FocusRing.draw` helper; Button follows its corners): 2 DIPs, accent color, 2 DIPs outside. Shown after Tab, hidden on the next pointer press and while the window is inactive.
- **Activation:** Enter clicks on key down; Space shows pressed while held and clicks on release; Escape while Space is held cancels; repeats never click. Built-in key behavior (Tab, Enter, Space) runs after the route, so handling the key in an element handler suppresses it.

M5 step 2 (keyboard and focus) delivered 2026-11-15: `Key` and the new `KeyDown`/`KeyUp` shapes in Scribble.Platform (aliased in Core). Win32 maps named virtual keys directly and everything else through `MapVirtualKeyW` (so letters follow the layout); the browser sends `e.key` through the text channel and modifiers in the record. In the core, `RoutedEventArgs` is the new base (`IsHandled`, `Handle`, `Modifiers`), `InputEventArgs` adds positions, `KeyEventArgs` adds `Key` and `IsRepeat`. `InputRouter` gained the focus manager (`MoveFocus Next/Previous` over focusable, visible, enabled elements in tree order, wrapping; press focuses or clears; invalid focus cleared in the after-layout refresh; ring state follows Tab, presses and window activation), key routing from the focused element (else the root), and built-ins after the route (Tab, Enter, Space press/release, Escape cancel). Elements keep `IsFocused`/`IsFocusVisible` and a key-pressed flag that feeds `IsPressed`. `FocusRing.draw` (Elements/FocusRing.fs) draws the ring; Button uses it and is focusable by default. `ElementHost.HandleEvent` returns whether a key was handled; `App.onKey` is `Key -> Modifiers -> 'Msg option` and runs only for unhandled, non-repeat keys (closeOnEscape too). 119 unit tests (8 new keyboard tests) and 30 rendering tests pass (new reference `button-focus@1x.png`, made on Tim's GPU). Verified by Tim on Windows and in the browser (2026-11-15).

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


Open questions:

- [ ] License (MIT or Apache 2.0) before the first public push.
- [ ] Is the name Scribble free on NuGet?
- [x] Exact DSL syntax: FuncUI-style settings lists (2026-11-15).
- [x] Test framework: xUnit (2026-11-15: the real runner passes every test).

Idea for M5 (agreed 2026-11-15, see the M5 decisions): a tactile pressed look for the default Button. It sinks (scales to about 97% around its center), its shadow flattens and it darkens slightly, then springs back over 100–150 ms. Needs the pressed state and a visual-only render transform (M5), and a small "animate this value" helper for the spring-back; the depth and timing become style settings in M7.

- [ ] Apps made only of components have no messages, so `App.view (root ())` needs `AppSetting<unit> list`. Perhaps `App.run` overloads or non-generic view-only settings.
- [ ] `RenderOffscreen` must become async for the browser (readback uses `mapAsync`). Decide before screenshot tests run in the browser.
- [x] Browser key codes: `e.key` mapped to the portable `Key` (M5 step 2, 2026-11-15).
