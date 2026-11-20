# Scribble

A UI framework for F#, written from the GPU up, with a FuncUI-style DSL. The same app runs on the desktop
(Windows x64 today) and in the browser (WebAssembly and WebGPU).

**Pre-release.** The API still changes between versions; every break is listed in the repository's `CHANGELOG.md`.

## Which package

An app references one of two packages, which bring the rest:

| Package | For |
| --- | --- |
| `Scribble.Desktop` | A desktop app: SDL3 windows and input, the WebGPU renderer (wgpu-native), HarfBuzz shaping, the screen-reader bridge (AccessKit). The native libraries come with it. |
| `Scribble.Browser` | A browser app (`Microsoft.NET.Sdk.WebAssembly`): the canvas, the browser's WebGPU, HarfBuzz in WebAssembly, and a default page. |

Opt-in extras: `Scribble.Inspector` (a devtools-style inspector, F12), `Scribble.Icons.Material` (Material
Symbols), `Scribble.Text.Emoji` (the Twemoji colour font; apps that use it must credit it).

The other `Scribble.*` packages are the layers underneath and arrive as dependencies. The bare `Scribble` package
on NuGet is someone else's and has nothing to do with this project.

## A first app

```fsharp
open Scribble.Core

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
            TextBlock.create [ TextBlock.text $"Count: {count}"; TextBlock.fontSize 28.0 ]
            Button.create [ Button.text "+"; Button.onClick (fun _ -> dispatch Increment) ]
            Button.create [ Button.text "−"; Button.onClick (fun _ -> dispatch Decrement) ]
        ]
    ]

[<EntryPoint>]
let main _ = App.run [ App.title "Counter"; App.program 0 update view ]
```

## Requirements

- .NET 10
- Desktop: Windows 10 1703 or later, x64, with Direct3D 12 or Vulkan drivers
- Browser: one with WebGPU (current Chrome or Edge; Firefox 141 or later on Windows)

Everything works under NativeAOT, trimming and WebAssembly AOT.

## Licence

MIT. What Scribble bundles keeps its own licence: see `THIRD-PARTY-NOTICES.md` in this package.
