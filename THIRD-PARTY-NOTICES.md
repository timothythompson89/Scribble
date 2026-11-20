# Third-party notices

Scribble is under the MIT License (`LICENSE`). It bundles or downloads the work of others, listed here; each keeps
its own licence. "Where" says which Scribble package carries it, and so which apps ship it.

| What | Version | Where | Licence | Licence text |
| --- | --- | --- | --- | --- |
| [Inter](https://github.com/rsms/inter) (font), © The Inter Project Authors | Regular and SemiBold | `Scribble.Text` (embedded) | SIL Open Font License 1.1 | `src/Scribble.Text/Fonts/Inter-OFL.txt`; in the package under `licenses/` |
| [Twemoji](https://github.com/twitter/twemoji) colour font (the npm package `twemoji-colr-font`) | 15.0.3 | `Scribble.Text.Emoji` (embedded; opt-in) | Font: SIL OFL 1.1. Emoji art: CC-BY 4.0, © Twitter, Inc. and other contributors: **apps that use it must credit it** (`Emoji.Attribution` has the text) | `src/Scribble.Text.Emoji/Twemoji-LICENSE.txt`; in the package under `licenses/` |
| [Material Symbols](https://github.com/google/material-design-icons), © Google | Outlined, weight 400, outlined and filled | `Scribble.Icons.Material` (embedded path data; opt-in) | Apache License 2.0 | `src/Scribble.Icons.Material/MaterialSymbols-LICENSE.txt`; in the package under `licenses/` |
| [harfbuzzjs](https://github.com/harfbuzz/harfbuzzjs) (`harfbuzz.wasm`: HarfBuzz compiled to WebAssembly), © The harfbuzzjs project authors and the HarfBuzz authors | 1.6.4 | `Scribble.Text.HarfBuzz.Browser` (embedded) | MIT (harfbuzzjs) and HarfBuzz's "Old MIT" | `src/Scribble.Text.HarfBuzz.Browser/harfbuzzjs-LICENSE.txt`; in the package under `licenses/` |
| [HarfBuzz](https://github.com/harfbuzz/harfbuzz), native, as built by the SkiaSharp project | `HarfBuzzSharp.NativeAssets.Win32` 8.3.1.1 | A NuGet dependency of `Scribble.Text.HarfBuzz`: not inside our package | HarfBuzz: "Old MIT"; the package: MIT, © Microsoft | In that package (`LICENSE.txt`, `THIRD-PARTY-NOTICES.txt`) |
| [wgpu-native](https://github.com/gfx-rs/wgpu-native) (`wgpu_native.dll`), © The gfx-rs developers | v29.0.1.1 | `Scribble.Rendering.WebGPU` (`runtimes/win-x64/native`) | MIT or Apache License 2.0, at your choice; the library contains Rust crates under their own permissive licences | [LICENSE.MIT](https://github.com/gfx-rs/wgpu-native/blob/v29.0.1.1/LICENSE.MIT), [LICENSE.APACHE](https://github.com/gfx-rs/wgpu-native/blob/v29.0.1.1/LICENSE.APACHE) (its release zip holds no licence file) |
| [SDL](https://github.com/libsdl-org/SDL) (`SDL3.dll`), © Sam Lantinga | 3.4.20 | `Scribble.Platform.SDL3` (`runtimes/win-x64/native`) | zlib | In the package: `licenses/SDL3-LICENSE.txt` |
| [AccessKit](https://github.com/AccessKit/accesskit-c) (`accesskit.dll`), © The AccessKit Authors; parts from Chromium, © The Chromium Authors | accesskit-c 0.23.2 | `Scribble.Accessibility.AccessKit` (`runtimes/win-x64/native`) | MIT or Apache License 2.0, at your choice; Chromium's parts: BSD 3-clause | In the package: `licenses/AccessKit/` (`LICENSE-MIT`, `LICENSE-APACHE`, `LICENSE.chromium`) |

The three native libraries are not in this repository: the first build downloads each from its project's own
release page into `native/` (`build/*.targets`), and packing copies them from there.

## Only in the repository (not in any package)

| What | Where | Licence |
| --- | --- | --- |
| [PngSuite](http://www.schaik.com/pngsuite/), © Willem van Schaik | `tests/Scribble.Tests/PngSuite` (the PNG decoder's test images) | Free to use, copy, modify and distribute (`PngSuite.LICENSE` beside them) |

The Gallery's pictures (`samples/Gallery/Images`) are Scribble's own, drawn by `tools/MakeGalleryImages.fsx`.
