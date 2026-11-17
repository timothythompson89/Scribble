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
  HarfBuzz only. OS APIs (windows, cursors, IME, accessibility) are the platform, not borrowing; image decoding is
  decided in M8 (Tim, 2026-11-16).
- **Frame work proportional to what's visible, not to what exists** is a rule, with tests (virtualization, M8:
  same elements realized for 100 and 10,000 items, scroll-step layout and draw list within 1.5×, scroll to item
  9,000 without realizing those between).
- Everything must work under **NativeAOT** and trimming (and WebAssembly AOT).
- Keep `docs/DESIGN.md` and `README.md` current at the end of each milestone (status, decisions, what to check).
- **Use the words in `GLOSSARY.md`** (view, element, element type, settings type, property, setting, value,
  theme value, app value, visual state, layer, host, ...) in code comments, docs and discussion, and add a term
  there when a new one settles. Decisions stay in `docs/DESIGN.md` (no ADR files; Tim, 2026-11-16).
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
$env:SCRIBBLE_TIMING = "1"; dotnet test tests/Scribble.Tests -c Release --filter Category=Timing   # wall-clock tests, before each milestone report
dotnet test tests/Scribble.Tests.Rendering        # GPU screenshot tests (needs a GPU)
dotnet run --project samples/Hello.Desktop
dotnet run --project samples/Counter.Desktop
dotnet run --project samples/Icons.Desktop                # every Material Symbol
dotnet run --project samples/Hello.Browser                # http://localhost:5280 (Counter 5281, Icons 5282)
dotnet publish samples/Hello.Desktop -c Release           # NativeAOT exe (needs VS C++ tools; vswhere on PATH)
dotnet publish samples/Hello.Browser -c Release -p:BrowserAot=true   # wasm AOT (wasm-tools workload)
dotnet run --project tools/Scribble.Serve      # the browser samples to the local network over HTTPS; hub + QR on :5279
```

- **A test run only counts if nothing aborted and the total is right** (62 rendering tests, 2026-11-17): a hang
  killed by `--blame-hang-timeout` still prints "Passed!" with only the finished tests in the minimal summary.
  Check for "Test Run Aborted" and the total, not for "Passed!". `TestResults/` (hang dumps, gigabytes) is
  git-ignored; delete dumps once read.
- **GPU devices in tests are created and disposed one at a time** (`GpuLifetime.Lock` in `ClearTests.fs`): doing it on
  several threads at once deadlocked in NVIDIA's driver under the Windows loader lock. Instances enable only the
  backends that can be used (`InstanceExtras`: primary = Vulkan, Metal, D3D12; or the one forced), never OpenGL
  unless nothing else gives an adapter.
- Screenshot tests compare against PNGs in `tests/Scribble.Tests.Rendering/References` **exactly** (same pixels
  on every platform is a rule, decision 13). The references come from Tim's machine on the default backend (D3D12,
  RTX 4070 Ti, 2026-11-17). `BackendTests` renders the same scenes on D3D12 and Vulkan and requires the same
  pixels; its one written exception is `dashes@2x` (2 pixels, one step). A mismatch is a bug or a stale picture:
  look at the diff before regenerating. `SCRIBBLE_UPDATE_SNAPSHOTS=1` rewrites references; `SCRIBBLE_REPO_ROOT`
  overrides where they're found; the fixture honours `SCRIBBLE_GPU_BACKEND`. Shader maths must give the same
  result on every backend: no built-in whose precision WGSL leaves open (`atan2` is a polynomial), colours rounded
  to 8 bits in the shader (`exact8`).
- Tests use real xUnit (until 2026-11-15 they ran against a local stand-in; the first real run on Tim's machine
  passed everything, on a real GPU too).
- `SCRIBBLE_GPU_BACKEND=d3d12|vulkan` forces a wgpu backend. Default on Windows: D3D12 (since 2026-11-17; Vulkan
  on NVIDIA rebuilt swap chains 2–3× slower, which made live resizes lag, and stutters with G-SYNC), then
  Vulkan/Metal, then OpenGL. D3D12 presents through a DirectComposition visual (`dx12PresentationSystem`).
- wgpu-native v29.0.1.1 is downloaded on first build into `native/` (git-ignored) by `build/WgpuNative.targets`,
  imported by `Scribble.Desktop`; the DLL flows to every app that references it.
- Performance numbers: measure **Release** builds (WebAssembly AOT for the browser: `tools/Scribble.Serve -- --aot`),
  and judge "fast enough" on Tim's phone with an AOT publish, the weakest real target (Tim, 2026-11-16). Debug and
  the interpreted browser build only need to stay comfortable to develop with; Release is about 1.5–2× faster than
  Debug on the desktop, AOT many times faster than interpreted. What matters on every build is how much work a frame
  does: keep it proportional to what's visible, not to what exists.
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
| `src/Scribble.Core` | Views, differ, elements, layout, styles, themes, components, app values, the element types, App settings and runtime. Folders follow compile order, each one unbroken run (Rider shows a split folder twice): `Foundation`, `Framework`, `Elements/Basic`, `Panels`, `Scrolling`, `Controls`, `Themes`, `Hosting` |
| `src/Scribble.Desktop` / `src/Scribble.Browser` | `App.run` (the same call, in `namespace Scribble.Core`): one reference brings platform, GPU and shaping; Browser also brings the default page (`build/`) |
| `src/Scribble.Inspector` | Opt-in inspector (`Inspector.enable [ ... ]`): panel and highlight layers, tree, pick mode. Files in compile order: `InspectorTypes`, `Format`, `InspectorState`, `Highlight`, `Elements` (Chevron, Grip), `Views`, `Inspector` (the public API) |
| `src/Scribble.Icons.Material` | Opt-in Material Symbols (`Material.home`, `Material.filled`, `Material.icon "star-filled"`); `Material.fs` is generated by `tools/GenerateMaterial.fsx` |
| `samples/Hello`, `Counter`, `Icons` | Each sample's app as a library; `X.Desktop` / `X.Browser` hosts with the same Program.fs; `Samples.Shared` (Adaptive.fs: `SplitRow`); host defaults in `samples/Directory.Build.props` |
| `tests/Scribble.Tests`, `tests/Scribble.Tests.Rendering` | Unit tests; GPU screenshot tests |
| `tools/Scribble.Serve` | Dev tool: publishes the browser samples and serves them to the local network over HTTPS (own CA); a hub page on :5279 (QR code in the terminal; QRCoder, this tool only) |
| `tools/Scribble.Bench` | Dev tool: measures ItemsView (scroll steps, recycling, new sources) with real HarfBuzz; `dotnet run -c Release --project tools/Scribble.Bench`; reads Core's internal `ItemsProfile` counters (off unless it turns them on) |

## Architecture in brief

- **Rendering:** everything is one instanced draw call per frame. Shapes are 96-byte instances in a storage buffer
  (SDF rounded rects, borders, Gaussian shadows, per-shape pixel clip); glyphs are shape kinds that sample an R8
  coverage atlas (2048²) or an RGBA color atlas (1024²). Edges snap to physical pixels by default (`Precise` opts out).
  Elements whose bounds + `Element.RenderOverflow` (64 DIPs) lie outside the clip aren't rendered (scrolled-out rows).
- **Text:** own parser (cmap 4/12, glyf incl. composites, COLR v0/CPAL), font-rs-style accumulation rasterizer, 4
  horizontal subpixel positions, no hinting; HarfBuzz shaping cached per string; fallback per grapheme cluster.
- **Frames on request only:** windows call `RequestFrame()`; nothing draws while nothing changes. `ElementHost`
  ignores its own tree's invalidations while a frame is being drawn (they're handled by that frame).
  Surfaces rebuild their swap chain when a frame begins, not on `Resize`; during a live resize (Windows' size-move
  loop) `WM_SIZE` draws at most once per display refresh, the size-move timer draws the rest (2026-11-17).
  **Power rule:** never add frames: none while idle, at most one per display refresh while something changes
  (Mailbox presentation was rejected for this). Desktop surfaces keep one frame in flight
  (`WebGpuDevice.frameLatency`, default 1) so drags lag the mouse least; that changes when frames show, not how many.
- **Elements (since M3b):** properties are typed keys (`Prop<'T>`, declaring `Affects.Measure/Arrange/Render`);
  an element type is defined settings-style (`Element.define "Name" [ Element.measure ...; Element.render ...;
  Element.defaults [...] ]`, or `Element.extend baseType`), stored as an `ElementType` record of stage functions
  plus per-type defaults (`Get`: own value, else type default, else prop default); settings are values created by
  static members on **settings types** (`Border`, `TextBlock`, ...) that inherit `Layoutable`/`Panel`, so wrong
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
  "Pointer input" and "Keyboard and focus"; `Button` is a Border with a default look that reacts to hover/pressed/disabled; it stretches like other elements (CheckBox and Switch align left; 2026-11-17).
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
  `ctx.Animate` stay calls, `DrawList` methods keep optional arguments (`?mode`, `?offsetX`: called per shape
  per frame, a settings list would allocate each time; 2026-11-16). Details in `docs/DESIGN.md` ("Settings
  everywhere", "Evaluation 2026-11-16").

## Rules and gotchas

- **Time comes from the platform** (`IPlatform.Elapsed`, `IPlatform.After`, `Dispatcher.PostAfter`,
  `ElementHost.Elapsed`), never a `Stopwatch` or `Task.Delay` for anything that waits or animates: the tests'
  `FakeTime` moves only per delivered frame (16 ms) or by `AdvanceMs`, so no unit test sleeps (2026-11-17).
- **Wall-clock assertions** go in tests marked `[<Timing.TimingFact; Trait("Category", "Timing")>]` (skipped unless
  `SCRIBBLE_TIMING=1`); split a test so its count-based checks stay in the normal run (decision 12).
- **Debug-only warnings for app authors** go through `DebugWarnings.Once(key, message)` (once per process; tests
  listen with `DebugWarnings.Listener`).
- **App code is guarded** (decision 22): a new place that runs app or element-author code (a handler, a stage, a
  view function) catches and calls `ReportError` (element or `IElementRoot`), leaving out only what failed.
  Without `App.onError` the dispatcher rethrows, which ends the app.
- `TreatWarningsAsErrors`; `WarningsNotAsErrors` IL2104;IL3053 (FSharp.Core's own trim warnings). Libraries set
  `IsScribbleLibrary=true` → `IsAotCompatible`/`IsTrimmable`.
- **No F# printf family** in shipped code (`printfn`, `sprintf`, `failwithf`, `%A`, `$"..."` with `%d`-style
  specifiers): crashes under NativeAOT. Use .NET interpolation (`$"{x:F1}"`) and `Console.WriteLine`. A literal `%`
  inside `$"..."` is read as a format specifier: concatenate it as a separate plain string.
- **Never `fixed` on a string** for native calls: pin with `GCHandle.Alloc(s, GCHandleType.Pinned)` and pass
  `AddrOfPinnedObject()` as `nativeint`. A background GC misread a `fixed` string pin (a pinned `char&`) as an object
  start while HarfBuzz ran and killed the process with "Internal CLR error (0x80131506)" (2026-11-16, .NET 10.0.15;
  found with a crash dump: the GC read text as a type pointer). `fixed` on arrays has caused nothing so far.
- Native interop: `DllImport` with blittable signatures only (F# can't use LibraryImport/JSImport generators);
  callbacks via `Marshal.GetFunctionPointerForDelegate<'T>` (generic overload) with the delegate kept alive; struct
  layouts checked against the C headers. Browser interop: `JSFunctionBinding.BindJSFunction/InvokeJS`; void returns
  must be `JSMarshalerType.Discard`; JS modules are embedded in the DLLs and imported as `data:` URLs.
- **No `static let` for state that types share across threads** (counters, caches): F# runs it with the whole
  file's initialization and checks that on use, which threw "static data accessed recursively" when two threads
  first used the types at once (2026-11-16). Use a holder type with `[<DefaultValue>] static val mutable` fields
  (`Ids`, `FontIds`; F# wouldn't take their address for `Interlocked` there, so they increment under a lock).
- In recursive type groups (`type ... and ...`), annotate generic members explicitly (`member _.Get<'T>(p: Prop<'T>) : 'T`)
  or F# infers them non-generically.
- Record field ambiguity: `Scribble.Core.Size`, `Scribble.Platform.DipSize` and `PixelSize` share `Width`/`Height`;
  annotate record literals (`let s: PixelSize = { ... }`). Consolidating these is an open item.
- A module and a type can't share a name across files in one namespace (FS0250); entry-point modules must not
  create a namespace equal to an existing module (e.g. `Scribble.Samples.Counter.Program` clashes with module
  `Scribble.Samples.Counter`; the hosts use plain `module Program`).
- UI-thread-only access to elements (checked in debug builds). Fonts are shared process-wide, so caches inside
  `FontFace`/`Font` must stay thread-safe (rendering test classes run in parallel on different threads).
- Never put `create` on a settings type as a static member: statics are inherited, and `Button.create [ Button.width
  100.0 ]` became ambiguous with `Border.create` (FS0041). It lives in a same-named module in the same file.
- Core's `Cursor`, `Modifiers`, `MouseButton`, `PointerKind` are aliases of the Scribble.Platform types.
- Rider's F# project view hides linked files whose `Link` has a folder (`Link="Counter/Common.fs"`): link shared
  files flat (`Link="Common.fs"`). The samples and tests avoid links now: sample apps are libraries.
- Making an internal F# value public in Core once left the Inspector seeing it as internal (stale reference assembly
  after an incremental build: FS1094); `dotnet build --no-incremental` fixed it.
- Rider once silently overwrote `Directory.Build.props`; if a fix "doesn't take", check the file on disk. For the
  browser samples Rider's default browser must be "First listed" (Blazor launch quirk).
- Browser AOT builds (`-p:BrowserAot=true`) use their own `obj/aot` and `bin/aot`: sharing `obj`, an interpreted publish
  after an AOT one shipped the IL-stripped assemblies and the runtime died at start ("interp.c: should not be reached").
- Rider run configurations are shared files in `.run/` (one per sample host, plus Scribble.Serve and Scribble.Serve (AOT); committed).
  Renaming or adding a host project means updating or adding its `.run/*.run.xml` in the same change.

## Adding an element (the pattern)

A new element type goes in the `Elements/` folder it belongs to (Basic, Panels, Scrolling, Controls), after the
types it uses, in `Scribble.Core.fsproj`'s compile order.

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

- **Done and verified by Tim** (Windows and browser): M0–M7 and M7b (window and GPU, browser, draw list, text,
  elements and layout, views and differ, input, components, async, layers, inspector, styles, themes, app
  values, icons, the Material package, image cursors). M7c (custom title bars) was skipped. M8 step 1 (scrolling and
  ItemsView) is done and verified by Tim (Windows, 2026-11-16). M8 step 2 (Grid, DockPanel, Canvas, WrapPanel in Core,
  Separator, zIndex, attached settings, dashed/dotted borders, the inspector's Grid overlay, the Counter's Layout
  tab) is built, not yet checked by Tim; it changed decision 16 (Tim agreed) (a child spanning a star track doesn't size Auto
  tracks; see DESIGN.md "Step 2 as built"). M8 step 1b (non-uniform virtualization tests; the scroll anchor by key) is built, not yet checked by Tim (zero-height
  runs: documented, debug warning; option A). M8 step 3a (ToggleButton, CheckBox, Switch, Slider, TabControl; the Counter on a TabControl with a Controls tab) is built, not yet checked by Tim (DESIGN.md "Step 3a as built"). M8 step 3a+ (the inspector tree on ItemsView) is built, not yet checked by Tim (DESIGN.md "M8 step 3a+"); so is 3a++ (stacked ItemsViews scroll sideways; the inspector tree too). Project review 2026-11-17 step 1 (the platform clock and timers; errors to `App.onError`, the app keeps running) and the Core folders are built, not yet checked by Tim (DESIGN.md "Project review 2026-11-17"). Counts: 409 unit tests (1–2 s; 5 timing tests skipped unless `SCRIBBLE_TIMING=1`), 62 rendering tests. Project review steps 2 (exact pictures, the backend test), 3 (Core fixes: warnings for a property set twice and for an overfull glyph atlas, forced states dropped on leaving the tree, `Timing` tests) and 4 (the inspector in seven files) are built, not yet checked by Tim. Per-milestone summaries: `docs/DESIGN.md`, "Milestone summaries"; decisions there too.
- **Next: M8 core controls**: steps 1 (ScrollViewer completed, ItemsView, scrollers) and 2 (panels, dashed
  lines) are done, 3a (toggles, Slider, TabControl) is built; then 3b Image (decoding gets its own round), then step 4: RadioButton, ListBox,
  ComboBox, Dialog, Toast, tooltips, the "show this in a layer" element. Then M9 TextBox, M10 hot reload; later:
  animation, accessibility, bidi/complex scripts, IME, macOS/Linux, mobile, Vulkan backend, game UI hosts (keep the
  draw list plain data, no assumption that the core owns the window or loop, low per-frame allocations).
- **Known issues:** shrinking a window briefly flashes black at the edge with Vulkan on NVIDIA (left as is).
  With G-SYNC (windowed VRR), the cursor stutters over a Scribble window on **Vulkan**: the monitor refreshes only
  when we present, and we draw on request. D3D12 presents through a DirectComposition visual, which fixes it
  (2026-11-17; DESIGN.md "Cursor stutter with G-SYNC"); don't switch D3D12 back to an HWND swap chain, and don't
  "fix" it by drawing while the pointer moves (rejected). To check on the first Mac build.
  The rendering tests' occasional hangs ("when chained after other dotnet commands") were a driver deadlock, fixed
  2026-11-16 (DESIGN.md, "Rendering test hangs").
- **Decided 2026-11-17 (project review, DESIGN.md "Project review 2026-11-17"):** MIT license; packages `Scribble.*`;
  same pixels on every platform is a rule. **Open:** `RenderOffscreen` must become async for browser screenshot tests.

## Agent skills

### Issue tracker

Local markdown under `.scratch/<feature>/` (no remote yet). See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels, as a `Status:` line in each issue file. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` at the root; decisions in `docs/DESIGN.md` (no ADR files). See `docs/agents/domain.md`.
