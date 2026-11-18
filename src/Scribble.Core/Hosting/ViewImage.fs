namespace Scribble.Core

open System
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering

type internal ViewImageOptions =
    { Size: Size voption
      Scale: float
      Theme: Theme voption
      Background: Color voption
      ImageTimeout: TimeSpan
      WaitForImages: bool }

/// One setting for `ViewImage.render view [ ... ]`.
[<Struct>]
type ViewImageSetting = internal ViewImageSetting of (ViewImageOptions -> ViewImageOptions)

/// The settings for a view image (DESIGN.md, "Rendering to an image and saving it", decision 4; GLOSSARY: View
/// image): `ViewImage.render view [ ViewImage.size (400.0, 300.0); ViewImage.scale 2.0 ]`.
[<AbstractClass; Sealed>]
type ViewImage =
    /// The size to lay the view out at, in DIPs. Left out: the view's own size (as it measures with no limit).
    static member size(width: float, height: float) =
        if not (width > 0.0 && height > 0.0) || Double.IsInfinity width || Double.IsInfinity height then
            invalidArg (nameof width) "The size must be above 0 and finite."
        ViewImageSetting(fun o -> { o with Size = ValueSome(Size.Create(width, height)) })

    /// Pixels per DIP: 1 by default; 2 for a picture as sharp as a 2× screen shows it.
    static member scale(value: float) =
        if not (value > 0.0) || Double.IsInfinity value then invalidArg (nameof value) "The scale must be above 0."
        ViewImageSetting(fun o -> { o with Scale = value })

    /// The theme: the app's (`Theme.current`) by default, else `Theme.dark []`.
    static member theme(value: Theme) = ViewImageSetting(fun o -> { o with Theme = ValueSome value })

    /// What's behind the view: the theme's background by default, as in a window (decision 6). `Color.Transparent`
    /// keeps the picture transparent where the view draws nothing.
    static member background(color: Color) = ViewImageSetting(fun o -> { o with Background = ValueSome color })

    /// How long to wait for pictures still loading (`Image.fromUrl`, `Image.fromFile`): 10 s by default. Past it the
    /// picture is drawn without them (a debug warning says so).
    static member imageTimeout(value: TimeSpan) =
        if value < TimeSpan.Zero then invalidArg (nameof value) "The timeout can't be negative."
        ViewImageSetting(fun o -> { o with ImageTimeout = value })

    /// Whether to wait for pictures still loading (true by default); false draws what's ready at once.
    static member waitForImages(value: bool) = ViewImageSetting(fun o -> { o with WaitForImages = value })

module ViewImage =
    let private defaults =
        { Size = ValueNone
          Scale = 1.0
          Theme = ValueNone
          Background = ValueNone
          ImageTimeout = TimeSpan.FromSeconds 10.0
          WaitForImages = true }

    let rec private loading (e: Element) (into: ResizeArray<ImageEntry>) =
        into.AddRange e.ImagesLoading
        for c in e.Children do
            loading c into

    /// One render, on the thread that made the tree: lays out (again after effects), finds the pictures still
    /// loading, and either waits for them (`wait` gets them and the time left, and says whether they all ended) or
    /// draws.
    let private renderWith
        (env: RenderEnvironment)
        (o: ViewImageOptions)
        (view: View)
        (wait: ResizeArray<ImageEntry> -> TimeSpan -> Task<bool>)
        : Task<Image> =
        let tree = ElementTree(env.Text)
        env.Dispatcher |> ValueOption.iter (fun d -> tree.Dispatcher <- ValueSome d)
        env.AppValues |> ValueOption.iter (fun v -> tree.AppValues <- v)
        let theme =
            match o.Theme, env.AppValues with
            | ValueSome t, _ -> t
            | ValueNone, ValueSome v -> v.Get Theme.current
            | ValueNone, ValueNone -> AppValues().Get Theme.current
        tree.Theme <- ValueSome theme
        tree.Scale <- o.Scale
        let available =
            match o.Size with
            | ValueSome s -> s
            | ValueNone ->
                // Measured with no limit, arranged at what it asked for.
                tree.Place <- ValueSome(fun root _ -> Rect.Create(0.0, 0.0, root.DesiredSize.Width, root.DesiredSize.Height))
                Size.Create(Double.PositiveInfinity, Double.PositiveInfinity)
        tree.Update view
        let layout () =
            tree.UpdateLayout available
            tree.RunAfterFrame()
            tree.UpdateLayout available
            // A render into nothing, so pictures only drawing asks for are held (and start loading) too.
            tree.Render(DrawList(o.Scale))
            let pending = ResizeArray()
            tree.Root |> ValueOption.iter (fun r -> loading r pending)
            pending
        let started = DateTime.UtcNow
        let finish () =
            let size =
                match o.Size, tree.Root with
                | ValueSome s, _ -> s
                | ValueNone, ValueSome r -> Size.Create(r.Bounds.Width, r.Bounds.Height)
                | ValueNone, ValueNone -> Size.Create(0.0, 0.0)
            let px: PixelSize =
                { Width = int (Math.Ceiling(size.Width * o.Scale - 0.001))
                  Height = int (Math.Ceiling(size.Height * o.Scale - 0.001)) }
            RenderedImage.checkSize env.Device px.Width px.Height
            let background = o.Background |> ValueOption.defaultWith (fun () -> theme.Get Palette.background)
            env.Device.RenderOffscreenAsync(px, o.Scale, fun frame ->
                frame.Clear background
                tree.Render frame.DrawList)
        task {
            try
                let mutable pending = layout ()
                let mutable waiting = o.WaitForImages && pending.Count > 0
                while waiting do
                    let left = o.ImageTimeout - (DateTime.UtcNow - started)
                    let! ended = wait pending left
                    if ended then
                        // What finished loading is measured and drawn again.
                        tree.Root |> ValueOption.iter (fun r -> r.InvalidateAll())
                        pending <- layout ()
                        waiting <- pending.Count > 0
                    else
#if DEBUG
                        let keys = String.Join(", ", pending |> Seq.map _.Key |> Seq.distinct)
                        let seconds = o.ImageTimeout.TotalSeconds
                        DebugWarnings.Once(
                            "viewimage-timeout:" + keys,
                            $"ViewImage.render drew without pictures still loading after {seconds:F0} s ({keys}). ViewImage.imageTimeout waits longer."
                        )
#endif
                        waiting <- false
                let! bitmap = finish ()
                return RenderedImage.ofBitmap bitmap o.Scale
            finally
                // Lets go of its pictures and ends its components.
                tree.Root <- ValueNone
        }

    /// Resolves when every entry has loaded (true) or `left` has passed (false).
    let private waitAll (after: TimeSpan -> (unit -> unit) -> unit) (entries: ResizeArray<ImageEntry>) (left: TimeSpan) : Task<bool> =
        if left <= TimeSpan.Zero then Task.FromResult false
        else
            let finished = TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            let remaining = ref entries.Count
            let one () =
                if Threading.Interlocked.Decrement(&remaining.contents) = 0 then finished.TrySetResult true |> ignore
            for e in entries do
                if not (ImageCache.Shared.AddWaiter(e, one)) then one ()
            after left (fun () -> finished.TrySetResult false |> ignore)
            finished.Task

    /// An image of `view` laid out and drawn on its own, with no window (GLOSSARY: View image): components start
    /// fresh, nothing is hovered or focused, everything is scrolled to its start, and pictures still loading are
    /// waited for (`ViewImage.imageTimeout`). In an app it uses the app's GPU device, fonts and theme, and runs on the
    /// UI thread (called from another thread, it goes there). In a tool or script, start with
    /// `use _ = DesktopApp.headless []`; there it waits on the calling thread, and the task is already finished when
    /// it returns. Fails when the picture would be bigger than the GPU takes (8192 pixels a side on the desktop).
    let render (view: View) (settings: ViewImageSetting list) : Task<Image> =
        try
            let o = settings |> List.fold (fun o (ViewImageSetting apply) -> apply o) defaults
            let env = RenderServices.Current
            match env.Dispatcher with
            | ValueSome d ->
                let after (delay: TimeSpan) action = d.PostAfter(delay, action)
                if d.CheckAccess() then renderWith env o view (waitAll after)
                else d.InvokeAsync(fun () -> renderWith env o view (waitAll after)).Unwrap()
            | ValueNone ->
                // A tool: no UI thread to come back to, so it waits here, blocking, and stays on this thread.
                let realTime (delay: TimeSpan) (action: unit -> unit) =
                    Task.Delay(delay).ContinueWith(fun (_: Task) -> action ()) |> ignore
                let blockingWait (entries: ResizeArray<ImageEntry>) (left: TimeSpan) =
                    let t = waitAll realTime entries left
                    t.Wait()
                    t
                renderWith env o view blockingWait
        with e ->
            Task.FromException<Image> e

/// One setting for `Headless.start` (`DesktopApp.headless [ ... ]`).
[<Struct>]
type HeadlessSetting = internal HeadlessSetting of (Scribble.Text.Font list -> Scribble.Text.Font list)

/// The settings for rendering without an app.
[<AbstractClass; Sealed>]
type Headless =
    /// Fonts to fall back on for characters the main font lacks (emoji: `Emoji.Twemoji()`), as `App.fonts`.
    static member fonts(fallbacks: Scribble.Text.Font list) = HeadlessSetting(fun _ -> fallbacks)

/// Rendering without an app (a tool, a script, a test): `ViewImage.render` uses the device and shaper given here
/// until the result is disposed. On the desktop, `DesktopApp.headless []` makes both.
module Headless =
    let start (device: IRenderDevice) (shaper: Scribble.Text.IShaper) (settings: HeadlessSetting list) : IDisposable =
        if isNull (box device) then nullArg (nameof device)
        if isNull (box shaper) then nullArg (nameof shaper)
        let fonts = settings |> List.fold (fun f (HeadlessSetting apply) -> apply f) []
        let text = Scribble.Text.TextSystem.create device shaper [ Scribble.Text.TextSystem.fallbacks fonts ]
        RenderServices.Use
            { Device = device
              Text = text
              Platform = ValueNone
              Window = ValueNone
              Dispatcher = ValueNone
              AppValues = ValueNone }
