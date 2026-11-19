# Scribble: notes for Claude

Scribble is a public, open UI framework for F#, written from the GPU up, with a FuncUI-style DSL; Windows and the
browser today, macOS/Linux next, later phones, TVs and consoles. **`docs/DESIGN.md` Part 1** describes it as it is
(projects, architecture, status, next steps, engineering rules) with an index into Part 2, the history of every
decision. Read Part 1 before planning work; this file is how we work and what bites.

## How we work (important)

- **Discuss the plan before writing any code.** Tim tends to second-guess and wants to see decisions, options and
  a recommendation first. For each milestone: list what it covers, the decisions it needs (a recommendation for
  each), wait for agreement, record decisions in `docs/DESIGN.md`, then build.
- Claude writes the code; Tim tests in **Rider on Windows** (and Chrome/Edge for the browser) and gives feedback.
- **F# only**: no C# projects.
- Low level by intent: rendering, layout, text, styles and controls are our own code. Borrowed: WebGPU (wgpu-native),
  HarfBuzz, SDL3 (desktop windows and input) and, later, AccessKit (screen readers). The browser platform is ours.
  Image decoding is ours (`Scribble.Imaging`).
- **Frame work proportional to what's visible, not to what exists**, with tests (same elements realized for 100
  and 10,000 items; scroll to item 9,000 without realizing those between).
- **Same pixels on every platform** (project review, decision 13): exact screenshot comparison, backends agree.
- Everything works under **NativeAOT**, trimming and WebAssembly AOT.
- **Use the words in `GLOSSARY.md`** (view, element, element type, settings type, property, setting, value, theme
  value, app value, visual state, layer, host, ...) in code, docs and discussion; add a term when one settles.
  Decisions go in `docs/DESIGN.md` (no ADR files).
- When something is done: keep DESIGN.md Part 1 and README current, add an "as built" note to its decisions,
  overwrite **`docs/CHECKLIST.md`** (git-ignored) with what Tim should try on the desktop (checkboxes, grouped by
  area, how to run at the top; keep items finished but not yet checked, updated), and report what changed, results
  (tests, pixel checks, timings), what couldn't be verified, and what to try. "The checklist" means that file.
- **A checklist holds only what tests can't verify** (a real window, keyboard, pointer or device, and how things
  feel): what could be tested gets a test instead. Browser checks (on the PC) are appended to
  **`docs/CHECKLIST-BROWSER.md`** and phone checks to **`docs/CHECKLIST-PHONE.md`** (both git-ignored, never
  overwritten), each for a pass of its own once desktop development is stable.

## Build, run, test (Windows)

```powershell
dotnet build Scribble.slnx
dotnet test tests/Scribble.Tests                  # unit tests on a fake platform (1–2 s)
dotnet test tests/Scribble.Tests.Rendering        # GPU screenshot tests
$env:SCRIBBLE_TIMING = "1"; dotnet test tests/Scribble.Tests -c Release --filter Category=Timing   # before each milestone report
dotnet run --project samples/Gallery.Desktop      # also Hello.Desktop, Icons.Desktop, Counter.Desktop
dotnet run --project samples/Gallery.Browser      # http://localhost:5281 (Hello 5280, Icons 5282, Counter 5283)
dotnet publish samples/Hello.Desktop -c Release   # NativeAOT exe (needs VS C++ tools; vswhere on PATH)
dotnet publish samples/Hello.Browser -c Release -p:BrowserAot=true   # wasm AOT (wasm-tools workload)
dotnet run --project tools/Scribble.Serve         # browser samples on the local network over HTTPS; hub + QR on :5279
dotnet run -c Release --project tools/Scribble.Bench   # ItemsView costs
dotnet fsi tools/MakePngReferences.fsx            # PngSuite references through WIC (only if PngSuite changes)
dotnet fsi tools/MakeGalleryImages.fsx         # the Gallery's pictures (only if a picture changes)
```

- **A test run counts only if nothing aborted and the totals are right** (2026-11-18: 1,253 unit tests plus 7
  timing tests skipped; 100 rendering tests). A hang killed by `--blame-hang-timeout` still prints "Passed!" with
  only the finished tests: check for "Test Run Aborted" and the total. Delete hang dumps in `TestResults/` once read.
- **Screenshot references** (`tests/Scribble.Tests.Rendering/References`) come from Tim's machine on D3D12 and
  are compared exactly. A mismatch is a bug or a stale picture: look at the diff before regenerating
  (`SCRIBBLE_UPDATE_SNAPSHOTS=1`; actual output in `bin/snapshots`). `BackendTests` requires D3D12 and Vulkan to
  agree (one written exception: `dashes@2x`). Shader maths must match on every backend: no built-in whose precision
  WGSL leaves open (`atan2` is a polynomial), colours rounded to 8 bits in the shader (`exact8`).
- `SCRIBBLE_LOG_TEXT=1` writes what SDL says of text input (typed text, an input method's composing steps, the
  caret's place) to the console: for finding out what an input method sends on a machine. It prints what is typed.
- `SCRIBBLE_GPU_BACKEND=d3d12|vulkan` forces a backend (apps and the test fixture). Default on Windows: D3D12,
  presenting through a DirectComposition visual. GPU devices in tests are created and disposed one at a time
  (`GpuLifetime.Lock`): in parallel they deadlocked NVIDIA's driver.
- wgpu-native v29.0.1.1 is downloaded on first build into `native/` (git-ignored) by `build/WgpuNative.targets`.
  SDL 3.4.20 likewise, by `build/Sdl3.targets` (the desktop platform, `Scribble.Platform.SDL3`; our own bindings).
- **Performance:** measure Release at steady state (warm up, then best of N; a cold run once read 3.5 ms for
  under 1). Judge "fast enough" on Tim's phone with an AOT publish (`tools/Scribble.Serve -- --aot`).

## Rules and gotchas

- **Power rule:** never add frames: none while idle, at most one per display refresh while something changes.
  Windows draw only on `RequestFrame()`; don't "fix" anything by drawing while the pointer moves.
- **Time comes from the platform** (`IPlatform.Elapsed`, `IPlatform.After`, `Dispatcher.PostAfter`,
  `ElementHost.Elapsed`) for anything that waits or animates; the tests' `FakeTime` moves per delivered frame
  (16 ms) or by `AdvanceMs`, so unit tests never sleep.
- **App code is guarded:** a new place that runs app or element-author code (a handler, a stage, a view function)
  catches and calls `ReportError` (element or `IElementRoot`), leaving out only what failed.
- **Wall-clock assertions** go in tests marked `[<Timing.TimingFact; Trait("Category", "Timing")>]`; their
  count-based checks go in a normal test beside them.
- **Debug-only warnings for app authors** go through `DebugWarnings.Once(key, message)` (tests listen with
  `DebugWarnings.Listener`).
- **Settings lists for every user-facing API** (`Thing.create [ Thing.option v ]`), as static members on the type
  they configure, applied to an internal options record. Agreed exceptions: `App.program init update view`,
  platform contracts (`WindowOptions`, `IPlatform`), component props (`'P -> View`), hooks, `Cmd`, `ctx.Animate`,
  `DrawList` methods (optional arguments: a list would allocate per shape). FuncUI-style lists only: no
  computation-expression or modifier-chain syntax.
- **AOT-safe F#:** .NET interpolation (`$"{x:F1}"`) and `Console.WriteLine` in shipped code; the printf family
  (`printfn`, `sprintf`, `failwithf`, `%A`, `%d` in `$"..."`) crashes under NativeAOT. A literal `%` inside `$"..."`
  is read as a specifier: concatenate it. `TreatWarningsAsErrors` (IL2104/IL3053 from FSharp.Core excepted).
- **Native interop:** `DllImport` with blittable signatures; callbacks via `Marshal.GetFunctionPointerForDelegate<'T>`
  with the delegate kept alive; struct layouts checked against the C headers. Pin strings with
  `GCHandle.Alloc(s, GCHandleType.Pinned)`: `fixed` on a string once let a background GC kill the process
  (0x80131506). Browser: `JSFunctionBinding`, void returns `JSMarshalerType.Discard`, JS modules embedded and
  imported as `data:` URLs.
- **State shared across threads** lives in holder types with `[<DefaultValue>] static val mutable` fields, changed
  under a lock (`Ids`, `ElementCounts`, `DebugWarnings`): a `static let` threw "static data accessed recursively"
  when two threads first used a file at once. Elements are UI-thread only; font caches are shared and thread-safe.
- F#: in recursive type groups annotate generic members (`member _.Get<'T>(p: Prop<'T>) : 'T`); annotate record
  literals where `Size`, `DipSize`, `PixelSize` share fields; a module and a type can't share a name across files
  of one namespace (FS0250); host entry points are plain `module Program`. After making something public in Core,
  a stale reference assembly may hide it (FS1094): `dotnet build --no-incremental`.
- Core's `Cursor`, `Modifiers`, `MouseButton`, `PointerKind` alias the Scribble.Platform types. `onClick` takes
  `ClickEventArgs`: write `fun _ -> ...` when unused.
- Rider: it once silently overwrote `Directory.Build.props` (check the file on disk if a fix doesn't take); linked
  files with a folder in `Link` are hidden (link flat); browser samples need the default browser "First listed".
  Run configurations live in `.run/`: adding or renaming a host project updates its `.run/*.run.xml` too.
- Browser AOT builds use their own `obj/aot` and `bin/aot` (sharing `obj` shipped IL-stripped assemblies).
- **Known issues:** shrinking a window flashes black at the edge with Vulkan on NVIDIA (left as is). With G-SYNC
  the cursor stutters over a window on Vulkan; D3D12's DirectComposition presentation fixes it, so keep it (check
  on the first Mac build). Browser screenshot tests can now be written (`RenderOffscreenAsync`).

## Adding an element (the pattern)

A new element type goes in the `src/Scribble.Core/Elements/` folder it belongs to (Basic, Panels, Scrolling,
Controls), after the types it uses in the compile order; Core's folders are each one unbroken run of files.

```fsharp
module ProgressBarProps =
    let value = Prop.define "Value" 0.0 Affects.Render          // Prop.local for caches; Prop.content/children for views inside

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
    let style parts (settings: Setting<ProgressBar> list) = Style.forType Type parts settings
```

- Settings are static members on the type (inherited, so `Button.background` works); `Type` and `create` live in
  the same-named module, never as statics (inherited statics made `Button.create` ambiguous with `Border.create`).
  Helpers go in the module as `private`, or `internal` when a derived element needs them (`Border.draw`).
- **Naming** (decision 8): a control's value setting is named for what it is (`isChecked`, `value`, `selected`),
  its change event `on<Name>Changed`, the end of a drag `on<Name>Committed`. A control is controlled when the view
  gives its value, else it keeps its own (`Control.fs`). Mark the value `|> AppState.mark` (styles never set app state;
  handlers are found by their type).
- Every element stretches by default; a control whose click area should stay small sets its own alignment
  (CheckBox and Switch: `Left`).
- Unit tests for behaviour; a screenshot test when it draws something new.

## Agent skills

### Issue tracker

Local markdown under `.scratch/<feature>/` (no remote yet). See `docs/agents/issue-tracker.md`.

### Triage labels

The five default labels, as a `Status:` line in each issue file. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` at the root; decisions in `docs/DESIGN.md` (no ADR files). See `docs/agents/domain.md`.
