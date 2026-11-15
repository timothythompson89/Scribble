# Scribble

A UI framework for F#, written from the GPU up. See the design doc for goals, architecture and roadmap.

**Status: Milestone 4**: apps are F# view functions. `App.run` (desktop) or `App.runInBrowser` starts one; views
are built from settings lists (`StackPanel.create [ ... ]`), and on each update Scribble diffs the new view against
the last one and patches only what changed. Underneath: WPF-style layout (measure/arrange, lengths in px, % and
star), our own text stack (TrueType parser, rasterizer, HarfBuzz shaping, emoji fallback), and a GPU draw list
rendered in one draw call per frame, on Windows and in the browser. Scribble draws only when something asks for a
frame.

## Your first app

```fsharp
open Scribble.Rendering
open Scribble.Core
open Scribble.Desktop          // or Scribble.Browser, with App.runInBrowser

type Msg = Increment | Decrement

let update msg count =
    match msg with
    | Increment -> count + 1
    | Decrement -> count - 1

let view count dispatch =
    StackPanel.create [
        StackPanel.margin 24.0
        StackPanel.spacing 16.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"Count: {count}"; TextBlock.fontSize 28.0; TextBlock.foreground Color.White ]
            Button.create [ Button.text "+"; Button.onClick (fun () -> dispatch Increment) ]
        ]
    ]

[<EntryPoint>]
let main _ = App.run [ App.title "Counter"; App.program 0 update view ]
```

Reference `Scribble.Desktop` (or `Scribble.Browser`); it brings the platform, the GPU renderer and text shaping
along. `samples/Shared/Counter.fs` is this app in full; `dotnet run --project samples/Scribble.Samples.Counter`
runs it.

## Requirements

- Windows 10 1703 or later (development platform for now)
- .NET 10 SDK
- A GPU with Direct3D 12 or Vulkan drivers

## Run the sample

```powershell
dotnet run --project samples/Scribble.Samples.Hello
```

The first build downloads wgpu-native (pinned to `v29.0.1.1`) into `native/` (git-ignored) and copies
`wgpu_native.dll` next to the executable. If the download fails (offline, proxy), download
`wgpu-windows-x86_64-msvc-release.zip` from the
[wgpu-native v29.0.1.1 release](https://github.com/gfx-rs/wgpu-native/releases/tag/v29.0.1.1)
and extract it to `native/wgpu-native-v29.0.1.1/win-x64/`, so that `lib/wgpu_native.dll` sits inside it.

Set `SCRIBBLE_GPU_BACKEND` to `d3d12` or `vulkan` to force a backend (default: wgpu chooses).

### What to check

1. A dark dashboard of cards with emoji avatars, titles, subtitles and live percentages; the progress bars animate.
   Text should look crisp at every scaling, and the percentages stay right-aligned as they change. The dashboard is
   a view function of the animation time and the hovered card (`samples/Shared/Dashboard.fs`), rebuilt and diffed
   every frame; it should look exactly as before. The title bar shows frames per second, layout time and shapes.
2. Resize the window: the cards reflow into more or fewer columns and stay sharp.
3. Move the window between monitors with different scaling (or change Windows scaling): borders stay crisp,
   1 DIP lines stay one clean line, and the console prints `Scale changed to …`.
4. Hover a card: its border turns its accent color and its shadow grows.
5. Space pauses the animation: frames stop and CPU/GPU use drops to about zero. Moving the mouse onto another card
   still updates the hover (one frame per change); moving within a card draws nothing.
6. Escape or the close button exits cleanly, with no errors in the console.
7. The toolbar: Pause/Resume (the label follows Space too), Restart, Remove card / Add card (1 to 8 cards; the rest
   stay put) and Shuffle (cards are keyed, so each keeps its element when it moves).
8. Keyboard: Tab moves a blue ring through the toolbar buttons (Shift+Tab backwards, wrapping); Enter clicks the
   focused one, Space presses it while held and clicks on release (Escape first cancels). Clicking a button focuses
   it without a ring, so Space then presses *that* button; click the background to give Space back to the app.

The counter (`dotnet run --project samples/Scribble.Samples.Counter`) is the components demo:

1. The top counter is the Elmish counter inside a component (`useElmish`): the buttons change the count (a press
   and a release on the same button counts as a click), Reset is disabled (faded, no hand cursor) at zero.
2. Below it, a list of counters, each with its own count (`useState`); the bar slides to the new count (0 to 10,
   red when negative) over a quarter second (`useAnimated`). Add, Remove first and Reverse change the list: counts
   stay with their names (keyed). Clicking one counter re-runs only that counter.
3. Buttons sink slightly (97%) while pressed and spring back; their faint shadow flattens. Pressing with Space
   does the same. Text stays sharp while sinking.
4. Nothing redraws while you don't click; an animation draws until it arrives, then stops.
5. Tab, Enter and Space work as in the Hello sample.
The browser version is `samples/Scribble.Samples.Counter.Browser` (port 5281).

## Run the browser sample

Needs a browser with WebGPU (current Chrome or Edge; Firefox 141+ on Windows).

```powershell
dotnet run --project samples/Scribble.Samples.Browser
```

This builds the app to WebAssembly, serves it on http://localhost:5280 and opens the browser.
Output from `printfn` appears in the browser's developer console (F12). It should look the same as the
desktop sample.

If `dotnet run` can't serve it, publish and use any static file server, for example:

```powershell
dotnet publish samples/Scribble.Samples.Browser -c Release
dotnet tool install -g dotnet-serve
dotnet serve -d samples/Scribble.Samples.Browser/bin/Release/net10.0/publish/wwwroot -o
```

For a faster release build, compile to WebAssembly ahead of time (needs `dotnet workload install wasm-tools`
once; publishing takes minutes, and the download is larger):

```powershell
dotnet publish samples/Scribble.Samples.Browser -c Release -p:BrowserAot=true
dotnet serve -d samples/Scribble.Samples.Browser/bin/Release/net10.0/publish/wwwroot -o
```

The browser console's first line says `Scribble browser build: WebAssembly AOT` (or `interpreted`).

How the browser backend talks to JavaScript: F# can't run .NET's `[JSImport]` generator, so
`Scribble.Platform.Browser/Js.fs` calls the API that generator emits (`JSFunctionBinding`) directly.
Each library's JavaScript is embedded in its DLL and loaded as a `data:` URL, so apps ship no extra files.

## Tests

```powershell
dotnet test
```

- `Scribble.Tests`: unit tests (no GPU).
- `Scribble.Tests.Rendering`: renders offscreen through WebGPU and checks the pixels. Needs a GPU.
  Screenshot tests compare with PNGs in `tests/Scribble.Tests.Rendering/References`, with a small tolerance for
  differences between GPUs. On a mismatch the actual image is written to `tests/Scribble.Tests.Rendering/bin/snapshots`.
  To accept new output as the reference, run `$env:SCRIBBLE_UPDATE_SNAPSHOTS = "1"; dotnet test`.

## Text

```fsharp
let text = TextSystem(device.GlyphAtlas, new HarfBuzzShaper())      // browser: BrowserHarfBuzzShaper.CreateAsync()
drawList.DrawText(text, "Hello", Fonts.SemiBold(), 15.0, x, y, color) // (x, y) = top-left of the line box, in DIPs
let m = text.Measure("Hello", Fonts.Regular(), 15.0)                  // Width, Height, Baseline, LineHeight
```

Fallback and emoji: pass fallback fonts to the text system. Characters the requested font lacks are drawn with
the first fallback that has them; emoji prefer a color font. Text is split into grapheme clusters first, so
sequences like 👨‍👩‍👧, 👍🏽, 🇺🇸 and 1️⃣ stay intact.

```fsharp
let text = TextSystem(device, shaper, [ Emoji.Twemoji() ])   // Emoji module: Scribble.Text.Emoji package
```

**Attribution:** the emoji art is Twemoji by Twitter, Inc. and other contributors, under CC-BY 4.0. Apps that use
`Scribble.Text.Emoji` should credit it (e.g. in an About box); `Emoji.Attribution` has the text.

On Windows, HarfBuzz comes from the `HarfBuzzSharp.NativeAssets.Win32` NuGet package (SkiaSharp's build;
just the native DLL). In the browser it's the official harfbuzzjs WebAssembly module, embedded in
`Scribble.Text.HarfBuzz.Browser`. Both give identical results.

Licenses: Inter is under the SIL Open Font License 1.1 (`src/Scribble.Text/Fonts/Inter-OFL.txt`);
harfbuzzjs/HarfBuzz under the MIT-style "Old MIT" license (`src/Scribble.Text.HarfBuzz.Browser/harfbuzzjs-LICENSE.txt`).

## Views, updates and elements

A view is a description: `Border.create [ ... ]` returns a `View` (cheap to make), and the app's view function
builds a new one from the model on every update. Scribble turns the first one into elements and, after that,
compares each new view with the previous one: an element of the same type in the same place is patched setting by
setting, anything else is rebuilt, and an unchanged View object is skipped. Only what changed is laid out and drawn
again. Children match by position; in lists that insert, remove or reorder, give them a key so each item keeps its
element: `TextBlock.key todo.Id`, or let the panel take keys from the collection:

```fsharp
StackPanel.create [ StackPanel.items todos _.Id (fun todo -> TextBlock.create [ TextBlock.text todo.Title ]) ]
```

The app holds the model (`App.program init update view`, the Elmish pattern). Messages update it straight away;
the view is rebuilt on the next frame, once, however many messages arrived, and not at all if the model didn't
change. Other app settings: `App.title`, `App.size`, `App.background`, `App.fonts` (fallbacks such as Twemoji),
`App.showStats`, `App.closeOnEscape`, `App.onKey` and `App.onFrame` (animation: frames continue only while the
model keeps changing). `Button` is a Border with a default look that lightens under the pointer, darkens while
pressed and fades when disabled; it takes keyboard focus, and Enter or Space clicks it.

### Pointer input

Every element takes pointer settings:

```fsharp
Border.create [
    Border.onPointerEnter (fun _ -> dispatch (Hover(Some card)))  // enter/leave: not routed
    Border.onPointerLeave (fun _ -> dispatch (Hover None))
    Border.onPointerDown (fun e -> if e.Modifiers.HasFlag Modifiers.Shift then e.Handle())
    Border.onClick (fun () -> dispatch Open)                      // left press + release over it
    Border.cursor Cursor.Hand
    Border.isEnabled model.CanOpen                                // false: no input here or inside
]
```

- **Routing:** down, up, move and wheel go to the element under the pointer and then to its ancestors (bubbling).
  `onPreviewPointerDown`, `onPreviewPointerUp` and `onPreviewWheel` run first, from the root down (tunneling).
  `e.Handle()` stops the event; a press handled in Preview is taken by that element (no click below it).
- **Capture:** the element that gets a press keeps getting the moves and the release until the last button is up,
  even outside the window. Dragging off a button and releasing doesn't click; dragging back on and releasing does.
- **States:** element types read `ctx.IsPointerOver`, `ctx.IsPressed` and `ctx.IsEnabled` in Render. Views don't
  read states (that's styles, Milestone 7); use enter/leave.
- Positions are DIPs: `e.Position` (window) and `e.LocalPosition` (the element whose handler runs).

### Keyboard and focus

```fsharp
Border.create [
    Border.focusable true                                         // Tab stops here; a press focuses it
    Border.onKeyDown (fun e ->                                    // from the focused element up to the root
        if e.Key = Key.Char 's' && e.Modifiers.HasFlag Modifiers.Control then
            dispatch Save
            e.Handle())
]

App.onKey (fun key mods -> match key with Key.Space -> Some TogglePause | _ -> None)
```

- **Keys** are logical and follow the keyboard layout: `Key.Char 's'` (letters always lowercase), `Key.Enter`,
  `Key.Left`, `Key.F 5`, `Key.Other "..."`; modifiers come separately. The browser uses `e.key`; Win32 maps virtual
  keys through the active layout.
- **Routing:** `onPreviewKeyDown`/`onPreviewKeyUp` run from the root down to the focused element (or the root
  when nothing has focus), then `onKeyDown`/`onKeyUp` from there back up. `e.Handle()` stops the event and also
  the built-in behavior below.
- **Focus:** `focusable` (Buttons are by default). Tab and Shift+Tab move through focusable elements in tree order
  and wrap; a press focuses the nearest focusable element under the pointer, or clears focus. Disabling, hiding or
  removing the focused element clears focus.
- **Focus ring:** element types draw it when `ctx.IsFocusVisible` (`FocusRing.draw ctx list radius`): after Tab,
  not after a press, and not while the window is inactive. `ctx.IsFocused` is true either way.
- **Activation:** Enter clicks the focused element (if it has `onClick`); Space shows it pressed and clicks on
  release; Escape while Space is held cancels; key repeats never click.
- **App.onKey** runs last and only for keys nothing handled, so a focused button's Space doesn't also reach the
  app's Space shortcut. Repeats are left out. `App.closeOnEscape` likewise.

### Components

A component is a piece of view with its own state. Define it once, at module level; it's a function from its
props to a View, so it goes anywhere a view does:

```fsharp
let counter = Component.define "Counter" (fun ctx (name: string) ->
    let count = ctx.useState 0                                    // survives re-runs
    let fill = ctx.useAnimated(float count.Current / 10.0, TimeSpan.FromMilliseconds 250.0)
    StackPanel.create [
        StackPanel.orientation Horizontal
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"{name}: {count.Current}" ]
            Button.create [ Button.text "+"; Button.onClick (fun () -> count.Update(fun c -> c + 1)) ]
        ]
    ])

StackPanel.items names id counter                                // keyed: state follows the name
```

- **What re-runs:** a component runs again on the next frame when its state changes, and only its part of the tree
  is patched. When its parent re-runs, it runs again only if its props changed (F# equality; props that hold
  functions always count as changed). No props: `fun ctx () -> ...`, used as `counter ()`.
- **Hooks** (call them unconditionally, in the same order every time; a change of order fails with the
  component's name):
  - `ctx.useState initial`: `.Current`, `.Set v`, `.Update f` (an equal value changes nothing; setting state
    while the component runs fails).
  - `ctx.useMemo(deps, fun d -> ...)`: computed again only when `deps` change.
  - `ctx.useEffect(deps, fun d -> ...)`: after the frame that shows the component, and again when `deps` change
    (`()` for once; a tuple for several values).
  - `ctx.useSubscription(deps, fun d -> disposable)`: disposed when `deps` change and when the component goes.
  - `ctx.useElmish(init, update)`: `let model, dispatch = ...` (no commands until M5b).
  - `ctx.useAnimated(target, duration, ?easing)`: a `float` or `Color` that moves to each new target; the
    component re-runs every frame while it moves.
- **For element authors:** `ctx.Animate("key", target, duration, ?easing)` in a render function animates
  without re-running any view, and `Element.transform (fun ctx -> Transform.ScaleAround(...))` changes how an
  element and its children are drawn without touching layout or input. Button uses both to sink while pressed.
  Easings: `Easing.linear`, `easeIn`, `easeOut` (the default), `easeInOut`, or any `float -> float`.
- An app made only of components has no messages: annotate its settings (`let app: AppSetting<unit> list = ...`).

### Elements and layout

Widths and heights (and their min/max) take a number of DIPs, a `Length`, or a string:

| Setting | Means |
| --- | --- |
| `Border.width 36.0`, `Border.width (Px 36.0)`, `"36px"`, `"36"` | Fixed, in DIPs |
| `Border.width (Percent 50.0)`, `"50%"` | Half of the space the parent offers (margin included); automatic in unlimited space |
| `Border.height (Star 1.0)`, `"*"`, `"2*"` | A weighted share of leftover space in a StackPanel; elsewhere it stretches |
| `Auto`, `"auto"` (the default) | Size to content, or stretch per alignment |

Star is opt-in: a StackPanel's children are as long as their content unless they ask for a share, and the panel
only shares when its own length is known. A header, a content area that fills, and a footer:

```fsharp
StackPanel.create [
    StackPanel.children [
        header
        Border.create [ Border.height "*"; Border.child content ]
        footer
    ]
]
```

Settings are checked at compile time: `TextBlock.margin` works because every element has a margin, while
`TextBlock.create [ Border.background … ]` is a type error. Every element has margin, width/height, min/max sizes,
alignment, visibility and clipping. Layout is measure-and-arrange, rounded to whole physical pixels, and changing a
setting only redoes what it affects.

A custom element is three parts (see `samples/Shared/Dashboard.fs` for four of them):

```fsharp
module ProgressBarProps =                                         // 1. properties, and what they affect
    let value = Prop.define "Value" 0.0 Affects.Render

[<AbstractClass>]
type ProgressBar =                                                // 2. its settings
    inherit Layoutable
    static member value<'k when 'k :> ProgressBar>(v: float) : Setting<'k> = Setting.create ProgressBarProps.value v

module ProgressBar =                                              // 3. the element: definition and create
    let Type =
        Element.define "ProgressBar" [
            Element.measure (fun _ _ -> Size.Create(0.0, 8.0))
            Element.render (fun ctx list ->
                list.FillRect(Rect.Create(0.0, 0.0, ctx.Size.Width * ctx.Get ProgressBarProps.value, 8.0), Color.White))
        ]
    let create (settings: Setting<ProgressBar> list) = View.Create(Type, settings)
```

A definition lists only what differs from a plain container (`Element.measure`, `Element.arrange`,
`Element.render`). `Element.extend Border.Type "Button" [ ... ]` starts from another element, and
`Element.defaults [ Border.background ...; Layoutable.cursor Cursor.Hand ]` sets values its elements have unless
a view says otherwise (that's how Button gets its look). `Type` and `create` go in a module rather than on the
settings type because static members are inherited: on the type, Button's would collide with Border's.

Element types are defined once, as static values: the differ recognizes a type by reference. `ElementTree` works
without a window (`tree.Update view`), which is how the layout and differ tests run; `App.start` runs an app on any
`IPlatform`, which is how the app tests run against a fake platform. Typical numbers (Release, after warm-up): 10,000 elements lay out in about 1 ms, and changing one of them takes a
few microseconds; building, diffing and laying out a 1,000-element view with one change takes about 0.5 ms.

## Project layout

| Project | Purpose |
| --- | --- |
| `src/Scribble.Core` | Views, the differ, elements and layout; Border, StackPanel, TextBlock, Button; App settings and runtime |
| `src/Scribble.Desktop` | `App.run`: the desktop platform, WebGPU renderer and HarfBuzz shaping in one reference |
| `src/Scribble.Browser` | `App.runInBrowser`: the browser equivalents |
| `src/Scribble.Platform` | Window, event, DPI and frame-clock abstractions |
| `src/Scribble.Platform.Win32` | Win32 backend, hand-written P/Invoke |
| `src/Scribble.Rendering` | Backend-neutral rendering interfaces |
| `src/Scribble.Rendering.WebGPU` | WebGPU backend over `webgpu.h` (wgpu-native) |
| `src/Scribble.Text` | Font parsing, glyph rasterizer, glyph cache, text drawing, measuring, wrapping and trimming; Inter |
| `src/Scribble.Text.Emoji` | Opt-in color emoji: the Twemoji font (COLR v0, ~1.4 MB) |
| `src/Scribble.Text.HarfBuzz` | Shaping with native HarfBuzz (desktop) |
| `src/Scribble.Text.HarfBuzz.Browser` | Shaping with HarfBuzz compiled to WebAssembly (browser) |
| `src/Scribble.Platform.Browser` | Browser backend: canvas, DOM events, `requestAnimationFrame` |
| `src/Scribble.Rendering.WebGPU.Browser` | Browser WebGPU (`navigator.gpu`) backend |
| `samples/Shared/Dashboard.fs` | The sample dashboard as a view function (also used by the rendering tests) |
| `samples/Shared/HelloApp.fs` | The Hello sample's model, update and app settings, shared by both hosts |
| `samples/Shared/Counter.fs` | The counter |
| `samples/Scribble.Samples.Hello` | Desktop host |
| `samples/Scribble.Samples.Browser` | Browser host (WebAssembly) |
| `samples/Scribble.Samples.Counter`, `.Counter.Browser` | The counter, desktop and browser (one-line Program.fs each) |
| `tests/*` | Unit and rendering tests |

## Interop rules

F# can't use the `LibraryImport` source generator (it's C#-only), so native calls use `DllImport` with
**blittable signatures only**: pointers instead of `ref`/`out`/`string`, structs made of primitive fields,
callbacks via `Marshal.GetFunctionPointerForDelegate` with the delegate kept alive. Calls like that need
no runtime marshalling, so they work under NativeAOT.
