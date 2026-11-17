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
- [ ] **M6 — Inspector.** Layers above the app (pass-through, modal, per-layer focus), a basic ScrollViewer, and an opt-in `Scribble.Inspector`: tree of all layers, pick mode, highlight, properties with their source, frame stats (decisions agreed 2026-11-15, below).
- [ ] **M7 — Styles.** Theme record, then selectors and cascading styles.
- ~~**M7c — Custom title bars**~~ **Skipped (Tim, 2026-11-16)** after planning it: the plan (our own caption buttons reported to Windows as its own, so snap layouts work) and the alternatives (recoloring the system title bar with `DWMWA_CAPTION_COLOR`; extending the frame so Windows draws its own caption buttons over our content, untested over a wgpu swap chain; the Windows App SDK title bar API, ruled out as a borrowed runtime) were weighed and the feature dropped. The popped-out inspector keeps the system title bar. Original entry, for if it comes back: (added 2026-11-16, Tim; was to come right after M7 step 2b, before M7b). Windows whose title bar is drawn by the app, so the popped-out inspector looks exactly like the docked panel (its header as the title bar, × pops it back in, no maximize). Win32 first: a window option (`titleBar Custom`, default `System`) that removes the system caption but keeps DWM's frame, shadow and Windows 11 corners (`WM_NCCALCSIZE`, `DwmExtendFrameIntoClientArea`), with an inset while maximized; hit testing (`WM_NCHITTEST`) answered from the element tree through a setting that marks regions (`windowPart Caption/Minimize/Maximize/Close`), so dragging, double-click to maximize, Aero snap, Win+arrows, the system menu (Alt+Space, right-click on the caption) and Windows 11's snap-layout flyout on the maximize button keep working; resize edges; DPI changes across monitors. The browser is unaffected (no windows); macOS (content under the traffic lights) and Linux/Wayland (client-side decorations) come with those backends. Names, the exact settings and how the inspector's header doubles as a caption are decided when it's planned. Watch the known black-edge flash when resizing on NVIDIA.
- [ ] **M7b — Icons.** SVG path icons drawn through the glyph rasterizer and atlas; an opt-in Material Symbols package (typed names, "star-filled-rotate90" strings, your own path data), modeled on the CustomAvaloniaControls MaterialIcon design. After styles, so icons can inherit the text color. Open: element and name casing, pluggable icon sets, .svg file import. Also custom cursors (agreed 2026-11-15): a `Cursor.Image` case (bitmap + hotspot, e.g. made from an icon) that the OS shows as a hardware cursor (Win32 `CreateIconIndirect`, re-made per scale on `ScaleChanged`; browser `cursor: url(data:…) x y` / `image-set`). Not drawn in our frame: that lags a frame or two, forces a frame on every mouse move, stops at the window edge and ignores the user's cursor settings. Things that follow the pointer (drag ghosts) are drawn in-frame, with drag and drop.
- [ ] **M8 — Core controls.** Grid, DockPanel, Canvas (added 2026-11-16, Tim: children placed with `Canvas.left/top/right/bottom` settings that only the Canvas reads, like WPF's attached `Canvas.Left`; left and right together stretch; replaces the Hello dashboard's `at` margin+alignment helper; names and whether margin still applies decided when M8 is planned), ScrollViewer (the rest: horizontal, draggable thumb, keyboard, touch), CheckBox, Image, Slider, ListBox, ComboBox, Dialog, Toast, the "show this in a layer" element (Popup/portal; also ComboBox's drop-down), TabControl (API, controlled or internal selection, lazy pages, keep-alive policy and arrow keys to be decided; after M7 so the selected tab has a proper look).
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
- **New public read-only API in Core:** `element.Values` (property, value, source), `element.OfferedSize` (the space last offered to measure), `element.Hooks` (kind and current value: useState/useMemo/useAnimated their value, useElmish its model, useAsync Loading/Loaded/Failed, effects and subscriptions just their name). Public rather than internal so other tools and tests can use them.
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

- [ ] License (MIT or Apache 2.0) before the first public push.
- [ ] Is the name Scribble free on NuGet?
- [x] Exact DSL syntax: FuncUI-style settings lists (2026-11-15).
- [x] Test framework: xUnit (2026-11-15: the real runner passes every test).

Idea for M5 (agreed 2026-11-15, see the M5 decisions): a tactile pressed look for the default Button. It sinks (scales to about 97% around its center), its shadow flattens and it darkens slightly, then springs back over 100–150 ms. Needs the pressed state and a visual-only render transform (M5), and a small "animate this value" helper for the spring-back; the depth and timing become style settings in M7.

- [ ] Apps made only of components have no messages, so `App.view (root ())` needs `AppSetting<unit> list`. Perhaps `App.run` overloads or non-generic view-only settings.
- [ ] `RenderOffscreen` must become async for the browser (readback uses `mapAsync`). Decide before screenshot tests run in the browser.
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
   realized items), `ItemsView.layout` (`ItemsLayout.stack` default, `ItemsLayout.wrap w h`, `ItemsLayout.grid
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
17. **DockPanel:** WPF semantics (`dock` per child in order, `lastChildFill` default true).
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
- **DockPanel:** `DockPanel.dock Dock.Top` (an enum: Left, Top, Right, Bottom; default Left), `lastChildFill`
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

## M8 step 3a++: sideways scrolling for stacked ItemsViews; the inspector tree scrolls sideways (planned 2026-11-17)

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
not (the driver or Windows still applied VRR to the window). The 11-15 build (`ad9f9bb`, run from a worktree)
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
  `RenderOffscreen` must become async for browser screenshot tests.
