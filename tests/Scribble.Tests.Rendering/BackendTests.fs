/// Same pixels on every backend (project review 2026-11-17, decision 13): the same scenes rendered on
/// each backend this machine offers (D3D12 and Vulkan on Windows; Metal and Vulkan on a Mac with
/// MoltenVK) must match exactly, pixel for pixel.
module Scribble.Tests.Rendering.BackendTests

open System
open Xunit
open Xunit.Abstractions
open Scribble.Platform
open Scribble.Rendering
open Scribble.Rendering.WebGPU
open Scribble.Rendering.WebGPU.Native
open Scribble.Text
open Scribble.Text.HarfBuzz
open Scribble.Text.Emoji
open Scribble.Core
open Scribble.Tests.Rendering.ClearTests

/// A scene: its name, its size in DIPs, and how to draw it with a text system.
type private Scene = { Name: string; Width: float; Height: float; Draw: TextSystem -> IFrame -> unit }

let private scenes =
    [ { Name = "shapes"; Width = 400.0; Height = 300.0; Draw = fun _ f -> Scenes.shapes f }
      { Name = "text"; Width = 520.0; Height = 260.0; Draw = fun t f -> Scenes.text t f }
      { Name = "emoji"; Width = 560.0; Height = 250.0; Draw = fun t f -> Scenes.emoji t f }
      { Name = "dashes"; Width = 522.0; Height = 282.0
        Draw = fun _ f ->
            f.Clear(Color.Hex 0x181825)
            DashTests.draw f.DrawList }
      { Name = "dashboard"; Width = 760.0; Height = 760.0
        Draw =
          fun t f ->
              let tree = ElementTree(t, Scale = f.Scale)
              tree.Update(Scribble.Samples.Dashboard.view 0.0 None Scribble.Samples.HelloApp.allCards None ignore)
              tree.UpdateLayout(Size.Create(760.0, 760.0))
              f.Clear(Color.Hex 0x11111B)
              tree.Render f.DrawList } ]

/// Every scene at 1× and 2× on one backend, or None when this machine doesn't offer it. Devices are
/// made and disposed one at a time (GpuLifetime).
let private renderOn (backend: BackendType) =
    let device =
        lock GpuLifetime.Lock (fun () ->
            try
                Some(WebGpuDevice.Create [ WebGpuDevice.backend backend ])
            with _ ->
                None)
    match device with
    | None -> None
    | Some device ->
        try
            use shaper = new HarfBuzzShaper()
            let text = TextSystem.create device shaper [ TextSystem.fallbacks [ Emoji.Twemoji() ] ]
            let description = (device :> IRenderDevice).Description
            let images =
                [ for scene in scenes do
                      for scale in [ 1.0; 2.0 ] do
                          let size: PixelSize = { Width = int (scene.Width * scale); Height = int (scene.Height * scale) }
                          let image = (device :> IRenderDevice).RenderOffscreen(size, scale, fun f -> scene.Draw text f)
                          yield $"{scene.Name}@{scale}x", image ]
            Some(description, images)
        finally
            lock GpuLifetime.Lock (fun () -> (device :> IDisposable).Dispose())

/// The written exceptions (decision 13: only where a backend truly can't match). Picture name →
/// (most pixels, largest channel difference) allowed.
///
/// dashes@2x: dashes along rounded corners come from a division and a polynomial per pixel; WGSL
/// allows division 2.5 ULP of error and leaves fusing a*b+c to each compiler, so D3D12 and Vulkan
/// on one NVIDIA GPU land a pixel or two on either side of a rounding step (2 pixels of 588,816,
/// one step, 2026-11-17). Everything else matches exactly.
let private exceptions = dict [ "dashes@2x", (4, 1) ]

type BackendTests(output: ITestOutputHelper) =

    [<Fact>]
    member _.``every backend draws the same pixels``() =
        let candidates =
            if OperatingSystem.IsWindows() then [ BackendType.D3D12; BackendType.Vulkan ]
            elif OperatingSystem.IsMacOS() then [ BackendType.Metal; BackendType.Vulkan ]
            else [ BackendType.Vulkan ]
        let rendered = [ for b in candidates do match renderOn b with Some r -> yield b, r | None -> () ]
        for b, (description, _) in rendered do
            output.WriteLine($"{b}: {description}")
        if rendered.Length < 2 then
            // Nothing to compare with (xUnit 2 can't skip at run time): say so in the output.
            output.WriteLine($"Only {rendered.Length} backend(s) here; nothing to compare.")
        else
            let (first, (_, reference)) = rendered.Head
            let failures =
                [ for b, (_, images) in rendered.Tail do
                      for (name, expected), (_, actual) in List.zip reference images do
                          let c = Snapshot.compare expected actual
                          let allowed =
                              match exceptions.TryGetValue name with
                              | true, (pixels, step) -> c.DifferentPixels <= pixels && c.MaxDifference <= step
                              | _ -> c.DifferentPixels = 0
                          if c.DifferentPixels > 0 then
                              output.WriteLine($"{name}: {b} differs from {first} in {c.DifferentPixels} pixels (max {c.MaxDifference})")
                          if not allowed then
                              Png.save (IO.Path.Combine(Snapshot.outputDir (), $"{name}.{first}.png")) expected
                              Png.save (IO.Path.Combine(Snapshot.outputDir (), $"{name}.{b}.png")) actual
                              yield $"{name}: {b} differs from {first} in {c.DifferentPixels} pixels (max {c.MaxDifference})" ]
            Assert.True(failures.IsEmpty, String.Join("; ", failures))
