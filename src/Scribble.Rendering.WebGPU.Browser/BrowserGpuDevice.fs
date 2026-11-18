namespace Scribble.Rendering.WebGPU.Browser

open System
open System.Runtime.InteropServices
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Platform.Browser.Js
open Scribble.Rendering

module internal GpuJs =
    [<Literal>]
    let ModuleName = "scribble-webgpu"

    let init = JsFunction(ModuleName, "init", T.task, [ T.string; T.string; T.double; T.double ])
    let uploadAtlas = JsFunction(ModuleName, "uploadAtlas", T.void', [ T.double; T.double; T.double; T.double; T.double; T.bytes ])

    /// Sends the changed region of a CPU atlas to its GPU texture (rows packed tightly).
    let upload (which: int) (atlas: GlyphAtlas) =
        let region = atlas.TakeDirtyRegion()
        if not region.IsEmpty then
            let x, y, w, h = int region.X, int region.Y, int region.Width, int region.Height
            let bpp = atlas.BytesPerPixel
            let packed = Array.zeroCreate<byte> (w * h * bpp)
            for row in 0 .. h - 1 do
                Array.blit atlas.Pixels (((y + row) * atlas.Size + x) * bpp) packed (row * w * bpp) (w * bpp)
            let args = uploadAtlas.Args()
            args[2].ToJS(float which)
            args[3].ToJS(float x)
            args[4].ToJS(float y)
            args[5].ToJS(float w)
            args[6].ToJS(float h)
            args[7].ToJS(Span<byte> packed)
            uploadAtlas.Invoke args
    let description = JsFunction(ModuleName, "description", T.string, [])
    let maxTextureSize = JsFunction(ModuleName, "maxTextureSize", T.double, [])
    let createImagePage = JsFunction(ModuleName, "createImagePage", T.void', [ T.double; T.double; T.double ])
    let destroyImagePage = JsFunction(ModuleName, "destroyImagePage", T.void', [ T.double ])
    let uploadImage = JsFunction(ModuleName, "uploadImage", T.void', [ T.double; T.double; T.double; T.double; T.double; T.bytes ])

    /// The image pages, in JavaScript (DESIGN.md, "M8 step 3b", (c)).
    let imagePages =
        { new IImagePages with
            member _.Create(page, w, h) =
                let args = createImagePage.Args()
                args[2].ToJS(float page)
                args[3].ToJS(float w)
                args[4].ToJS(float h)
                createImagePage.Invoke args

            member _.Destroy page =
                let args = destroyImagePage.Args()
                args[2].ToJS(float page)
                destroyImagePage.Invoke args

            member _.Upload(page, x, y, level) =
                let args = uploadImage.Args()
                args[2].ToJS(float page)
                args[3].ToJS(float x)
                args[4].ToJS(float y)
                args[5].ToJS(float level.Width)
                args[6].ToJS(float level.Height)
                args[7].ToJS(Span<byte> level.Pixels)
                uploadImage.Invoke args }
    let createSurface = JsFunction(ModuleName, "createSurface", T.void', [ T.string ])
    let destroySurface = JsFunction(ModuleName, "destroySurface", T.void', [ T.string ])
    let draw =
        JsFunction(ModuleName, "draw", T.void', [ T.string; T.double; T.double; T.double; T.double; T.bytes; T.double; T.bytes ])
    let dispose = JsFunction(ModuleName, "dispose", T.void', [])

    let withId (f: JsFunction) (id: string) =
        let args = f.Args()
        args[2].ToJS id
        f.Invoke args

/// A frame for a browser canvas: clear color plus a draw list, sent to JavaScript in one call.
[<Sealed>]
type internal BrowserFrame(elementId: string, size: PixelSize, scale: float, atlas: GlyphAtlas, colorAtlas: GlyphAtlas, images: ImageAtlas) =
    let mutable clearColor = Color.Transparent
    let drawList = DrawList(scale)
    let mutable finished = false

    interface IFrame with
        member _.Size = size
        member _.Scale = scale
        member _.Clear color = clearColor <- color
        member _.DrawList = drawList

        member _.Dispose() =
            if not finished then
                finished <- true
                GpuJs.upload 0 atlas
                GpuJs.upload 1 colorAtlas
                let runs = images.Prepare drawList
                let runWords = Array.zeroCreate<int> (runs.Count * 3)
                for i in 0 .. runs.Count - 1 do
                    runWords[i * 3] <- runs[i].First
                    runWords[i * 3 + 1] <- runs[i].Count
                    runWords[i * 3 + 2] <- runs[i].Page
                let args = GpuJs.draw.Args()
                args[2].ToJS elementId
                args[3].ToJS(float clearColor.R)
                args[4].ToJS(float clearColor.G)
                args[5].ToJS(float clearColor.B)
                args[6].ToJS(float clearColor.A)
                args[7].ToJS drawList.InstanceBytes
                args[8].ToJS(float drawList.Count)
                args[9].ToJS(MemoryMarshal.AsBytes(Span<int> runWords))
                GpuJs.draw.Invoke args

/// A canvas surface. The canvas's pixel size is owned by the platform (it resizes the canvas);
/// the browser reallocates the swap chain itself, so Resize only records the size.
[<Sealed>]
type internal BrowserSurface(elementId: string, initialSize: PixelSize, atlas: GlyphAtlas, colorAtlas: GlyphAtlas, images: ImageAtlas) =
    let mutable size = initialSize
    let mutable disposed = false
    do GpuJs.withId GpuJs.createSurface elementId

    interface IRenderSurface with
        member _.Size = size
        member _.Resize newSize = size <- newSize

        member _.TryBeginFrame(scale) =
            if disposed || size.IsEmpty then None
            else Some(new BrowserFrame(elementId, size, scale, atlas, colorAtlas, images) :> IFrame)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                GpuJs.withId GpuJs.destroySurface elementId

/// What the BrowserGpuDevice settings configure.
type internal BrowserGpuOptions = { Power: GpuPower }

/// One setting for `BrowserGpuDevice.CreateAsync [ ... ]`.
[<Struct>]
type BrowserGpuSetting = internal BrowserGpuSetting of (BrowserGpuOptions -> BrowserGpuOptions)

/// IRenderDevice on the browser's own WebGPU (navigator.gpu).
///
/// Unlike the native backend, creation is asynchronous: the browser only answers adapter and
/// device requests after control returns to its event loop.
[<Sealed>]
type BrowserGpuDevice private (description: string, atlas: GlyphAtlas, colorAtlas: GlyphAtlas, images: ImageAtlas) =
    let mutable disposed = false

    /// Which GPU to prefer on machines with more than one. Default: the browser chooses.
    static member powerPreference(value: GpuPower) = BrowserGpuSetting(fun o -> { o with Power = value })

    /// Loads the JavaScript side, then requests an adapter and a device:
    /// `BrowserGpuDevice.CreateAsync []`, or `CreateAsync [ BrowserGpuDevice.powerPreference GpuPower.LowPower ]`.
    static member CreateAsync(settings: BrowserGpuSetting list) : Task<BrowserGpuDevice> =
        let options = settings |> List.fold (fun o (BrowserGpuSetting apply) -> apply o) { Power = GpuPower.Default }
        task {
            do! importEmbeddedModule GpuJs.ModuleName typeof<BrowserGpuDevice>.Assembly "scribble-webgpu.js"
            let args = GpuJs.init.Args()
            args[2].ToJS(
                match options.Power with
                | GpuPower.HighPerformance -> "high-performance"
                | GpuPower.LowPower -> "low-power"
                | GpuPower.Default -> ""
            )
            args[3].ToJS Shaders.shapesWgsl.Value
            let atlas = GlyphAtlas()
            let colorAtlas = GlyphAtlas(1024, 4)
            args[4].ToJS(float atlas.Size)
            args[5].ToJS(float colorAtlas.Size)
            GpuJs.init.Invoke args
            do! returnedTask args
            let description = GpuJs.description.Args()
            GpuJs.description.Invoke description
            let limit = GpuJs.maxTextureSize.Args()
            GpuJs.maxTextureSize.Invoke limit
            let images = new ImageAtlas(GpuJs.imagePages, int (returnedDouble limit))
            return new BrowserGpuDevice(returnedString description, atlas, colorAtlas, images)
        }

    interface IRenderDevice with
        member _.Description = description
        member _.GlyphAtlas = atlas
        member _.ColorGlyphAtlas = colorAtlas

        member _.CreateSurface(window, size) =
            match window with
            | BrowserCanvas elementId -> new BrowserSurface(elementId, size, atlas, colorAtlas, images) :> IRenderSurface
            | other -> raise (PlatformNotSupportedException $"The browser WebGPU backend can't draw into {other}.")

        member _.RenderOffscreen(_, _, _) =
            // Reading pixels back is asynchronous in the browser (buffer.mapAsync returns a Promise),
            // so this needs an async variant of RenderOffscreen. Tracked in the design doc.
            raise (NotSupportedException "Offscreen rendering isn't available in the browser yet.")

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                (images :> IDisposable).Dispose()
                let args = GpuJs.dispose.Args()
                GpuJs.dispose.Invoke args
