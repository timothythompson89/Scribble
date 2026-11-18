namespace Scribble.Core

open System
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text

/// What rendering to an image and saving files use (DESIGN.md, "Rendering to an image and saving it"): the running
/// app's GPU device, text system, platform, dispatcher and app values, set by `App.start`, or a device and text system
/// alone from `DesktopApp.headless` in a tool.
[<NoComparison; NoEquality>]
type internal RenderEnvironment =
    { Device: IRenderDevice
      Text: TextSystem
      Platform: IPlatform voption
      /// The app's window (the save dialog's owner).
      Window: IWindow voption
      Dispatcher: Dispatcher voption
      AppValues: AppValues voption }

/// The process's rendering environments and the render counter. Fields as in `Ids` (CLAUDE.md).
///
/// An app's environment belongs to its UI thread (one per thread: a newer app on the same thread replaces it), so
/// apps on different threads (tests run in parallel) each find their own. Called from elsewhere: the headless one
/// if there is one, else the app started last (a background thread of a desktop app).
type internal RenderServices private () =
    [<DefaultValue>] static val mutable private apps: Collections.Generic.Dictionary<int, RenderEnvironment>
    [<DefaultValue>] static val mutable private lastApp: RenderEnvironment voption
    [<DefaultValue>] static val mutable private headless: RenderEnvironment voption
    [<DefaultValue>] static val mutable private renders: int64

    static member private Apps =
        if isNull RenderServices.apps then RenderServices.apps <- Collections.Generic.Dictionary()
        RenderServices.apps

    static member TryCurrent: RenderEnvironment voption =
        lock typeof<RenderServices> (fun () ->
            match RenderServices.Apps.TryGetValue Environment.CurrentManagedThreadId with
            | true, e -> ValueSome e
            | _ ->
                match RenderServices.headless with
                | ValueSome e -> ValueSome e
                | ValueNone -> RenderServices.lastApp)

    /// The environment, or a message saying how to get one.
    static member Current: RenderEnvironment =
        match RenderServices.TryCurrent with
        | ValueSome e -> e
        | ValueNone ->
            invalidOp
                "Nothing to render with: rendering to an image needs a running app (App.run), or DesktopApp.headless [] in a tool or script (use _ = DesktopApp.headless [])."

    /// Sets an environment: an app's (with a dispatcher) for this thread, else the headless one. Disposing the result
    /// takes it away again (if it's still the one set).
    static member Use(environment: RenderEnvironment) : IDisposable =
        let thread = Environment.CurrentManagedThreadId
        lock typeof<RenderServices> (fun () ->
            if environment.Dispatcher.IsSome then
                RenderServices.Apps[thread] <- environment
                RenderServices.lastApp <- ValueSome environment
            else RenderServices.headless <- ValueSome environment)
        let isThis (e: RenderEnvironment voption) =
            match e with
            | ValueSome e -> obj.ReferenceEquals(e.Device, environment.Device) && obj.ReferenceEquals(e.Text, environment.Text)
            | ValueNone -> false
        { new IDisposable with
            member _.Dispose() =
                lock typeof<RenderServices> (fun () ->
                    match RenderServices.Apps.TryGetValue thread with
                    | true, e when isThis (ValueSome e) -> RenderServices.Apps.Remove thread |> ignore
                    | _ -> ()
                    if isThis RenderServices.lastApp then RenderServices.lastApp <- ValueNone
                    if isThis RenderServices.headless then RenderServices.headless <- ValueNone) }

    /// A key no other render has (a rendered image is its own picture: decision 1 of round 2).
    static member NextKey() =
        lock typeof<RenderServices> (fun () ->
            RenderServices.renders <- RenderServices.renders + 1L
            "render:" + string RenderServices.renders)

/// Turning an offscreen render into an Image.
module internal RenderedImage =
    /// The render's premultiplied pixels made straight, as an Image at `scale` pixels per DIP.
    let ofBitmap (bitmap: Bitmap) (scale: float) : Image =
        let src = bitmap.Pixels
        let px = Array.zeroCreate<byte> src.Length
        for i in 0..4 .. src.Length - 4 do
            let a = int src[i + 3]
            if a = 255 then
                px[i] <- src[i]
                px[i + 1] <- src[i + 1]
                px[i + 2] <- src[i + 2]
                px[i + 3] <- 255uy
            elif a > 0 then
                // Rounded to nearest; a premultiplied channel above alpha (it shouldn't be) is clamped.
                px[i] <- byte (min 255 ((int src[i] * 255 + a / 2) / a))
                px[i + 1] <- byte (min 255 ((int src[i + 1] * 255 + a / 2) / a))
                px[i + 2] <- byte (min 255 ((int src[i + 2] * 255 + a / 2) / a))
                px[i + 3] <- byte a
        Image(RenderServices.NextKey(), ImageSource.Pixels(bitmap.Size.Width, bitmap.Size.Height, px), scale)

    /// Fails when a render of `width` × `height` pixels is more than the device takes.
    let checkSize (device: IRenderDevice) (width: int) (height: int) =
        let limit = device.MaxTextureSize
        if width > limit || height > limit then
            raise (
                ArgumentException(
                    $"An image of {width} × {height} pixels is more than the GPU takes ({limit} on a side): render it smaller, or at a lower scale."
                )
            )
        if width < 1 || height < 1 then
            raise (ArgumentException($"An image of {width} × {height} pixels is empty: give it a size."))
