/// Headless GPU tests: render offscreen through the real WebGPU backend and read the pixels back.
/// These need a GPU (or a software Vulkan driver such as lavapipe) and the wgpu-native library.
module Scribble.Tests.Rendering.ClearTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Rendering.WebGPU

/// One device for the whole test run; creating devices is slow.
type GpuFixture() =
    let errors = Collections.Concurrent.ConcurrentQueue<GpuError>()
    let device = WebGpuDevice.Create [ WebGpuDevice.onError errors.Enqueue ]
    member _.Device = device :> IRenderDevice
    member _.Errors = errors
    interface IDisposable with
        member _.Dispose() = (device :> IDisposable).Dispose()

let expectPixel (bitmap: Bitmap) x y (r: byte, g: byte, b: byte, a: byte) =
    let (pr, pg, pb, pa) = bitmap.GetPixel(x, y)
    // Allow 1 step of rounding difference between drivers.
    let near (e: byte) (v: byte) = abs (int e - int v) <= 1
    Assert.True(near r pr && near g pg && near b pb && near a pa,
                $"pixel ({x},{y}) was ({pr},{pg},{pb},{pa}), expected ({r},{g},{b},{a})")

type ClearTests(gpu: GpuFixture) =
    interface IClassFixture<GpuFixture>

    [<Fact>]
    member _.``clear fills every pixel with the color``() =
        let size: PixelSize = { Width = 70; Height = 33 } // odd sizes exercise the 256-byte row padding
        let bitmap = gpu.Device.RenderOffscreen(size, 1.0, fun frame -> frame.Clear(Color.Hex 0x336699))
        Assert.Equal(size, bitmap.Size)
        Assert.Equal(70 * 33 * 4, bitmap.Pixels.Length)
        for (x, y) in [ 0, 0; 69, 0; 0, 32; 69, 32; 35, 16 ] do
            expectPixel bitmap x y (0x33uy, 0x66uy, 0x99uy, 255uy)
        Assert.Empty gpu.Errors

    [<Fact>]
    member _.``a frame with no clear is transparent``() =
        let bitmap = gpu.Device.RenderOffscreen({ Width = 8; Height = 8 }, 1.0, ignore)
        expectPixel bitmap 4 4 (0uy, 0uy, 0uy, 0uy)
        Assert.Empty gpu.Errors

    [<Fact>]
    member _.``the last clear wins``() =
        let bitmap =
            gpu.Device.RenderOffscreen(
                { Width = 4; Height = 4 },
                1.0,
                fun frame ->
                    frame.Clear Color.White
                    frame.Clear(Color.Rgb(1.0f, 0.0f, 0.0f))
            )
        expectPixel bitmap 0 0 (255uy, 0uy, 0uy, 255uy)

    [<Fact>]
    member _.``device describes its adapter``() =
        Assert.StartsWith("WebGPU/", gpu.Device.Description)
