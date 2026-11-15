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

The counter (`dotnet run --project samples/Scribble.Samples.Counter`): the buttons change the count (a press and a
release on the same button counts as a click), Reset appears when the count isn't zero, and nothing redraws while
you don't click. `-- --builders` runs the same app written in the experimental builder syntax (below); it should
look identical. The browser version is `samples/Scribble.Samples.Counter.Browser` (port 5281; add `?arg=--builders`
to the address for the builder version).

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
`App.showStats`, `App.closeOnEscape`, `App.onKey`, `App.onFrame` (animation: frames continue only while the model
keeps changing) and `App.onPointerMove` (hover, until input arrives properly in Milestone 5). `onClick` works on
any element; `Button` is a Border with a default look.

### Experimental: builder syntax

The same views can be written with constructors, chained modifiers and blocks (`Scribble.Core.Builders`, names
provisional). `samples/Shared/CounterBuilders.fs` is the counter this way; it draws pixel-identically:

```fsharp
open type Scribble.Core.Builders.Ui

let view count dispatch : View =
    VStack(spacing = 16.0).margin(24.0) {
        Text($"Count: {count}").fontSize(28.0).foreground(Color.White)
        HStack(spacing = 8.0) {
            Button("−", fun () -> dispatch Decrement)
            Button("+", fun () -> dispatch Increment)
            if count <> 0 then Button("Reset", fun () -> dispatch Reset)
        }
    }
```

A panel's own modifiers go before its block: F# can't chain a method call after a closing brace.

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
type ProgressBar =                                                // 3. a marker type with setting functions
    inherit Layoutable
    static member val Type =                                      // 2. the element type: measure/arrange/render
        { ElementType.Default with
            Name = "ProgressBar"
            Measure = fun _ _ -> Size.Create(0.0, 8.0)
            Render = fun ctx list -> list.FillRect(Rect.Create(0.0, 0.0, ctx.Size.Width * ctx.Get ProgressBarProps.value, 8.0), Color.White) }
    static member create(settings: Setting<ProgressBar> list) = View.Create(ProgressBar.Type, settings)
    static member value<'k when 'k :> ProgressBar>(v: float) : Setting<'k> = Setting.create ProgressBarProps.value v
```

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
| `samples/Shared/Counter.fs`, `CounterBuilders.fs` | The counter, in settings lists and in the builder syntax |
| `samples/Scribble.Samples.Hello` | Desktop host |
| `samples/Scribble.Samples.Browser` | Browser host (WebAssembly) |
| `samples/Scribble.Samples.Counter`, `.Counter.Browser` | The counter, desktop and browser (one-line Program.fs each) |
| `tests/*` | Unit and rendering tests |

## Interop rules

F# can't use the `LibraryImport` source generator (it's C#-only), so native calls use `DllImport` with
**blittable signatures only**: pointers instead of `ref`/`out`/`string`, structs made of primitive fields,
callbacks via `Marshal.GetFunctionPointerForDelegate` with the delegate kept alive. Calls like that need
no runtime marshalling, so they work under NativeAOT.
