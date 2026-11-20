# Contributing

Thank you for looking. Scribble is young and its design still moves every week, so for now:

- **Issues are welcome:** bugs, things that surprised you, questions, and what you would want from a UI framework
  for F#. A bug report is most useful with the smallest view that shows it, the platform (Windows version and GPU,
  or the browser and its version), and whether it also happens on the other GPU backend
  (`SCRIBBLE_GPU_BACKEND=vulkan` or `d3d12`).
- **Pull requests aren't taken yet.** Every decision is talked through and written down before the code
  (`docs/DESIGN.md`), and the public API is still being renamed and reshaped; a pull request would most likely
  collide with work in flight. Open an issue instead, and say so if you would like to build the thing yourself
  once pull requests open.

This will change as the API settles; this file will say when.

## Finding your way

- [`README.md`](README.md): what Scribble does, with examples for each part.
- [`docs/DESIGN.md`](DESIGN.md): Part 1 describes Scribble as it is; Part 2 is the history of every decision.
- [`GLOSSARY.md`](GLOSSARY.md): the words used in code, docs and discussion.
- [`CLAUDE.md`](../CLAUDE.md): how the work is done day to day (the code is written with Claude and tested by Tim),
  the build and test commands, and the rules and gotchas.

## Building and testing

```powershell
dotnet build Scribble.slnx
dotnet test tests/Scribble.Tests              # unit tests, no GPU
dotnet test tests/Scribble.Tests.Rendering    # screenshot tests: need a GPU, and compare exactly with pictures from one machine
dotnet fsi tools/TestPackages.fsx             # packs everything and builds apps from the packages
```

Windows 10 or later and the .NET 10 SDK. The first build downloads wgpu-native, SDL3 and AccessKit into `native/`.
