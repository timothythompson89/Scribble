namespace Scribble.Core

open System.Collections.Generic
open System.Diagnostics
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text

/// Timing of the last frame the host drew, for the title bar, the inspector and perf tests.
[<Struct>]
type FrameStats =
    { LayoutMilliseconds: float
      RenderMilliseconds: float
      Shapes: int }

/// What the ElementHost settings configure.
type internal ElementHostOptions =
    { Background: Color voption
      Dispatcher: Dispatcher voption
      BeforeFrame: unit -> unit
      Styles: Style list
      Theme: Theme voption
      AppValues: AppValues voption
      /// Time since some fixed start (the platform's `Elapsed`); a stopwatch when not set.
      Clock: (unit -> System.TimeSpan) voption }

/// One setting for `ElementHost.create`: `ElementHost.background (Color.Hex 0x181825)`.
[<Struct>]
type ElementHostSetting = internal ElementHostSetting of (ElementHostOptions -> ElementHostOptions)

/// Which layer one pointer belongs to.
type internal LayerPointer(kind: PointerKind) =
    member _.Kind = kind
    member val Position: Point voption = ValueNone with get, set
    member val Modifiers = Modifiers.None with get, set
    /// The layer the pointer is over (the one that last got its events).
    member val Over: Layer voption = ValueNone with get, set
    /// The layer that got the press: it keeps the pointer until every button is up.
    member val Capture: Layer voption = ValueNone with get, set
    member val Buttons = 0 with get, set

/// Connects element trees to a window: lays them out at the window's size and scale, draws them
/// on every Frame, and asks for a frame whenever something in them changes. `App.start` makes
/// one; a custom host (a game engine, a test) makes its own with `ElementHost.create`.
///
/// The app's tree is one layer (`App`, order 0); more layers can go above it (`AddLayer`). The
/// host decides which layer gets the pointer (the topmost with something under it, or a modal
/// one) and the keyboard (the layer last clicked, unless a modal layer is above it).
///
/// Forward the window's events to `HandleEvent`; the host handles Frame, Resized and ScaleChanged,
/// pointer input (routed events, pointer capture, clicks, element states, the cursor) and the
/// keyboard (focus, Tab order, routed key events, Enter/Space activation).
[<Sealed>]
type ElementHost internal (window: IWindow, device: IRenderDevice, text: TextSystem, options: ElementHostOptions) =
    let surface = device.CreateSurface(window.Surface, window.ClientSize)
    // ValueNone: the theme's background (see `ClearColor`).
    let mutable currentBackground = options.Background
    let sharedValues = match options.AppValues with ValueSome v -> v | ValueNone -> AppValues()
    do options.Theme |> ValueOption.iter (fun t -> sharedValues.Set(Theme.current, t))
    let mutable stats = { LayoutMilliseconds = 0.0; RenderMilliseconds = 0.0; Shapes = 0 }
    let runBeforeFrame = options.BeforeFrame
    let now =
        match options.Clock with
        | ValueSome f -> f
        | ValueNone ->
            let stopwatch = Stopwatch.StartNew()
            fun () -> stopwatch.Elapsed
    let started = now ()

    /// True while a frame is being prepared and drawn: changes made then (an update patched in
    /// before layout) are drawn by this frame, so they don't need to ask for another.
    let mutable inFrame = false

    /// Bottom to top.
    let layers = List<Layer>()
    let pointers = Dictionary<int, LayerPointer>()
    /// The layer last pressed in: it gets the keyboard (unless a modal layer is above it).
    let mutable pressedLayer: Layer voption = ValueNone
    /// The layer that had the keyboard at the last check (its focus looks active).
    let mutable keyboardOwner: Layer voption = ValueNone
    let mutable windowActive = true
    /// Keys looked at before any layer (tools such as the inspector): true stops the key there.
    let keyPreviews = List<Key -> Modifiers -> bool>()
    let frameDrawn = Event<FrameStats>()
    let layerChanged = Event<Layer>()
    let closed = Event<unit>()
    let mutable isClosed = false

    let dips (p: PixelPoint) = Point(p.X / window.Scale, p.Y / window.Scale)
    let same (a: Layer) (b: Layer) = obj.ReferenceEquals(a, b)
    let isIn (layer: Layer) = layers.Contains layer

    let add (layer: Layer) =
        let tree = layer.Tree
        tree.Scale <- window.Scale
        tree.Dispatcher <- options.Dispatcher
        tree.AppValues <- sharedValues
        if layer.UsesAppStyles then
            tree.Styles <- options.Styles
            tree.Theme <- ValueSome(sharedValues.Get Theme.current)
        tree.OnInvalidated <-
            fun () ->
                layerChanged.Trigger layer
                if not inFrame then window.RequestFrame()
        // After everything with the same or a lower order.
        layers.Insert(layers.FindLastIndex(fun l -> l.Order <= layer.Order) + 1, layer)
        layer.InitialView |> ValueOption.iter layer.Update
        window.RequestFrame()

    let app = Layer.Create(text, [ Layer.name "App"; Layer.order 0 ], window.SetCursor)

    // The window's theme follows the app value: layers that take app styles get the new one (their
    // trees re-style and ask for a frame; the background follows in that frame).
    let themeSubscription =
        sharedValues.Subscribe(
            Theme.current.Id,
            fun () ->
                let theme = ValueSome(sharedValues.Get Theme.current)
                for l in layers do
                    if l.UsesAppStyles then l.Tree.Theme <- theme
                window.RequestFrame()
        )

    /// A modal layer blocks what's below it while it shows something.
    let isBlocking (l: Layer) = l.IsModal && l.Tree.HasVisibleContent

    /// The layer that gets the pointer at `p`: the topmost modal layer, or the topmost one with an
    /// element there, whichever is higher; the app if none.
    let layerAt (p: Point) =
        let mutable i = layers.Count - 1
        let mutable found = ValueNone
        while found.IsNone && i >= 0 do
            let l = layers[i]
            if isBlocking l || l.Tree.HitTest(p.X, p.Y).IsSome then found <- ValueSome l
            i <- i - 1
        found |> ValueOption.defaultValue app

    /// The layer that gets the wheel at `p`: as `layerAt`, passing over layers that let it through.
    let wheelLayerAt (p: Point) =
        let mutable i = layers.Count - 1
        let mutable found = ValueNone
        while found.IsNone && i >= 0 do
            let l = layers[i]
            if isBlocking l || (not l.LetsWheelThrough && l.Tree.HitTest(p.X, p.Y).IsSome) then found <- ValueSome l
            i <- i - 1
        found |> ValueOption.defaultValue app

    let addLayer (layer: Layer) =
        // A layer holding a press sees nothing under it where another layer has the pointer.
        layer.Input.IsCovered <- fun p -> not (same (layerAt p) layer)
        add layer

    do addLayer app

    /// The layer keys go to: the one last pressed in (while it shows something; else the app),
    /// unless a modal layer is above it.
    let keyboardLayer () =
        let pressed =
            match pressedLayer with
            | ValueSome l when isIn l && l.Tree.HasVisibleContent -> l
            | _ -> app
        let topModal = layers.FindLastIndex(fun l -> isBlocking l)
        if topModal > layers.IndexOf pressed then layers[topModal] else pressed

    /// Only the keyboard's layer shows its focus as active (ring, held Space); the others look
    /// like an inactive window.
    let updateKeyboardOwner () =
        let current = keyboardLayer ()
        match keyboardOwner with
        | ValueSome l when same l current -> ()
        | previous ->
            previous |> ValueOption.iter (fun l -> if isIn l then l.Input.WindowFocusChanged false)
            keyboardOwner <- ValueSome current
            current.Input.WindowFocusChanged windowActive

    let pointerFor (kind: PointerKind) (id: int) =
        match pointers.TryGetValue id with
        | true, s -> s
        | _ ->
            let s = LayerPointer(kind)
            pointers[id] <- s
            s

    /// Moves a pointer that isn't pressed onto `layer`: the layer it leaves sees it go.
    let moveTo (s: LayerPointer) (id: int) (layer: Layer) =
        match s.Over with
        | ValueSome old when same old layer -> false
        | old ->
            old |> ValueOption.iter (fun o -> if isIn o then o.Input.PointerExited(s.Kind, id))
            s.Over <- ValueSome layer
            true

    /// The layer a pointer's events go to: the one holding its press, else the one under it.
    let targetFor (s: LayerPointer) (id: int) (p: Point) =
        match s.Capture with
        | ValueSome c when isIn c -> c
        | _ ->
            let l = layerAt p
            moveTo s id l |> ignore
            l

    /// After layout, or when the last button comes up: a pointer that stood still may now be
    /// over another layer (one appeared, went away or changed).
    let reassign (id: int) (s: LayerPointer) =
        match s.Position with
        | ValueSome p when s.Buttons = 0 ->
            let l = layerAt p
            if moveTo s id l then l.Input.PointerAt(s.Kind, id, p, s.Modifiers)
        | _ -> ()

    /// Cleared behind the layers every frame, instead of the theme's background (the default).
    static member background(value: Color) = ElementHostSetting(fun o -> { o with Background = ValueSome value })

    /// The UI thread's dispatcher: components use it for state set from other threads and to
    /// report errors from async work. Without one, state must be set on the UI thread.
    static member dispatcher(value: Dispatcher) = ElementHostSetting(fun o -> { o with Dispatcher = ValueSome value })

    /// Runs at the start of every frame, before layout: the place to advance animations (which
    /// change properties and so ask for the next frame themselves).
    static member beforeFrame(handler: unit -> unit) = ElementHostSetting(fun o -> { o with BeforeFrame = handler })

    /// Where animation time comes from: time since some fixed start, from a monotonic clock
    /// (App.start passes the platform's `Elapsed`). Without it, a stopwatch.
    static member clock(now: unit -> System.TimeSpan) = ElementHostSetting(fun o -> { o with Clock = ValueSome now })

    /// Styles for every layer (except those with `Layer.appStyles false`); `App.styles` sets them.
    static member styles(value: Style list) = ElementHostSetting(fun o -> { o with Styles = value })

    /// The starting window theme (`Theme.current` in its app values), for every layer except those
    /// with `Layer.appStyles false`; `App.theme` sets it. Default: `Theme.dark []`.
    static member theme(value: Theme) = ElementHostSetting(fun o -> { o with Theme = ValueSome value })

    /// The app values its layers share (`App.run` gives the app's, shared by its windows). Default: its own.
    static member appValues(value: AppValues) = ElementHostSetting(fun o -> { o with AppValues = ValueSome value })

    /// The app values every layer shares (`ctx.useAppValue`).
    member _.AppValues = sharedValues

    member _.Window = window

    /// The GPU device it draws with, and its text system (for tools that open a window of their own).
    member _.Device = device
    member _.Text = text

    /// Raises `Closed`, then closes the window (once). App.start calls it when the window is asked to
    /// close; call it instead of `Window.Close()` so everything attached hears about it.
    member _.Close() =
        if not isClosed then
            isClosed <- true
            // First, so windows owned by this one (an inspector pop-out) close while it still exists.
            closed.Trigger()
            window.Close()

    /// When the host closes through `Close()`, just before its window goes.
    member _.Closed: IEvent<unit> = closed.Publish

    /// The app's layer (order 0).
    member _.App = app
    /// The app's tree.
    member _.Tree = app.Tree

    member _.Root
        with get () = app.Tree.Root
        and set (v: Element voption) = app.Tree.Root <- v

    /// All layers, bottom to top (the app's included).
    member _.Layers: IReadOnlyList<Layer> = layers

    /// Adds a layer above the app (or wherever its order puts it) and returns it:
    /// `host.AddLayer [ Layer.order 200; Layer.modal true; Layer.view (dialog ()) ]`.
    member _.AddLayer(settings: LayerSetting list) : Layer =
        let layer = Layer.Create(text, settings, window.SetCursor)
        addLayer layer
        layer

    /// Takes a layer away (its elements leave the tree, so components stop). Not the app's.
    member _.RemoveLayer(layer: Layer) =
        if same layer app then invalidOp "The app's layer can't be removed."
        if layers.Remove layer then
            for s in pointers.Values do
                match s.Over with
                | ValueSome l when same l layer -> s.Over <- ValueNone
                | _ -> ()
                match s.Capture with
                | ValueSome l when same l layer ->
                    s.Capture <- ValueNone
                    s.Buttons <- 0
                | _ -> ()
            layer.Tree.Root <- ValueNone
            window.RequestFrame()

    /// Cleared behind the layers every frame instead of the theme's background (starts as
    /// `ElementHost.background`; ValueNone follows the theme; may change while running).
    member _.Background
        with get () = currentBackground
        and set v =
            currentBackground <- v
            window.RequestFrame()

    /// The window's theme (`Theme.current`, an app value; `App.theme`/`ElementHost.theme` start it).
    /// It applies to every layer that takes app styles (toasts, dialogs; not the inspector) and
    /// gives the window's background.
    member _.WindowTheme: Theme = sharedValues.Get Theme.current

    /// What the window is cleared to: `Background` if set, else the window theme's background
    /// (`Palette.background`).
    member this.ClearColor =
        match currentBackground with
        | ValueSome c -> c
        | ValueNone -> this.WindowTheme.Get Palette.background

    member _.LastFrame = stats

    /// Time on the host's clock (`ElementHost.clock`) since the host was made: what animations see.
    member _.Elapsed: System.TimeSpan = now () - started

    /// The UI thread's dispatcher (`ElementHost.dispatcher`; App.start sets one).
    member _.Dispatcher = app.Tree.Dispatcher

    /// Shows a view in the app's layer (builds the elements the first time, patches them after that).
    member _.Update(view: View) = app.Update view

    member private this.DrawFrame() =
        let time = now () - started
        for l in layers do
            l.Tree.Time <- time
        runBeforeFrame ()
        let size = window.ClientSize
        let scale = window.Scale
        let started = Stopwatch.GetTimestamp()
        let available: Size = { Width = float size.Width / scale; Height = float size.Height / scale }
        for l in layers.ToArray() do
            l.Tree.Scale <- scale
            l.Tree.UpdateLayout available
        // Layout may have moved things under a pointer that stood still (hover, cursor), or put a
        // layer under it, or taken one away.
        for KeyValue(id, s) in List.ofSeq pointers do
            reassign id s
        for l in layers do
            l.Input.Refresh()
        updateKeyboardOwner ()
        let laidOut = Stopwatch.GetTimestamp()
        // A layout that threw (reported already) left the tree half laid out: drop this frame, keep
        // the last one on screen.
        let layoutFailed = layers |> Seq.exists (fun l -> l.Tree.LayoutFailed)
        match (if layoutFailed then None else surface.TryBeginFrame scale) with
        | Some frame ->
            use frame = frame
            frame.Clear this.ClearColor
            for l in layers do
                l.Tree.Render frame.DrawList
            stats <-
                { LayoutMilliseconds = Stopwatch.GetElapsedTime(started, laidOut).TotalMilliseconds
                  RenderMilliseconds = Stopwatch.GetElapsedTime(laidOut).TotalMilliseconds
                  Shapes = frame.DrawList.Count }
        | None -> ()

    /// The layer that gets keys now.
    member _.KeyboardLayer = keyboardLayer ()

    /// Sees every key going down before any layer does, whatever has focus (repeats included);
    /// returning true stops the key there. For tools that need a shortcut that always works (the
    /// inspector's F12). Dispose the result to stop.
    member _.AddKeyPreview(handler: Key -> Modifiers -> bool) : System.IDisposable =
        keyPreviews.Add handler
        { new System.IDisposable with
            member _.Dispose() = keyPreviews.Remove handler |> ignore }

    /// After each frame is drawn (and its effects have run), with the frame's timing.
    member _.FrameDrawn: IEvent<FrameStats> = frameDrawn.Publish

    /// When something in a layer changed (layout, looks or state: anything that asks for a frame,
    /// including changes made while a frame is drawn). Fires often; keep handlers cheap.
    member _.LayerChanged: IEvent<Layer> = layerChanged.Publish

    /// The element with keyboard focus (in the layer that gets keys), if any.
    member _.Focused: Element voption = (keyboardLayer ()).Focused

    /// Moves keyboard focus as Tab (Next) or Shift+Tab (Previous) would, in the layer that gets keys.
    member _.MoveFocus(direction: FocusDirection) = (keyboardLayer ()).MoveFocus direction

    /// Handles a window event. Returns true for a key that an element (or built-in behavior such
    /// as Tab or a focused button's Enter) handled; the app's own key handling skips those.
    member this.HandleEvent(event: PlatformEvent) : bool =
        match event with
        | Frame ->
            inFrame <- true
            try
                this.DrawFrame()
#if DEBUG
                // What a frame asks for is ignored (it's handled by that frame), so styles queued
                // during it wait until something else asks for a frame. Nothing should do that
                // (DESIGN.md, "Recycling measured; ...", decision 8); say who did.
                for l in layers do
                    match l.Tree.FirstQueuedRestyle with
                    | ValueSome e ->
                        Debug.WriteLine(
                            "Scribble: " + e.Type.Name + (if e.Name <> "" then " #" + e.Name else "") + " in layer '" + l.Name
                            + "' still has styles to match after its frame; they apply only when something else asks for a frame.")
                    | ValueNone -> ()
#endif
            finally
                inFrame <- false
            // Effects and "keep animating" requests: outside the frame, so what they change asks
            // for the next one.
            for l in layers.ToArray() do
                l.Tree.RunAfterFrame()
            options.Dispatcher |> ValueOption.iter (fun d -> d.FrameEnded())
            frameDrawn.Trigger stats
            false
        | Resized size -> surface.Resize size; false
        | ScaleChanged scale ->
            for l in layers do
                l.Tree.Scale <- scale
            false
        | PointerMoved(kind, id, p, mods) ->
            let p = dips p
            let s = pointerFor kind id
            s.Position <- ValueSome p
            s.Modifiers <- mods
            (targetFor s id p).Input.PointerMoved(kind, id, p, mods)
            false
        | PointerPressed(kind, id, p, button, mods) ->
            let p = dips p
            let s = pointerFor kind id
            s.Position <- ValueSome p
            s.Modifiers <- mods
            let layer = targetFor s id p
            if s.Buttons = 0 then
                s.Capture <- ValueSome layer
                pressedLayer <- ValueSome layer
                updateKeyboardOwner ()
            s.Buttons <- s.Buttons + 1
            layer.Input.PointerPressed(kind, id, p, button, mods)
            false
        | PointerReleased(kind, id, p, button, mods) ->
            let p = dips p
            let s = pointerFor kind id
            s.Position <- ValueSome p
            s.Modifiers <- mods
            let layer = targetFor s id p
            s.Buttons <- max 0 (s.Buttons - 1)
            if s.Buttons = 0 then s.Capture <- ValueNone
            layer.Input.PointerReleased(kind, id, p, button, mods)
            // Released over another layer: that one has the pointer now.
            reassign id s
            false
        | PointerExited(kind, id) ->
            let s = pointerFor kind id
            match s.Capture, s.Over with
            | ValueSome l, _
            | ValueNone, ValueSome l -> if isIn l then l.Input.PointerExited(kind, id)
            | _ -> ()
            if s.Buttons = 0 then
                s.Position <- ValueNone
                s.Over <- ValueNone
                if kind <> PointerKind.Mouse then pointers.Remove id |> ignore
            false
        | PointerCanceled(kind, id) ->
            let s = pointerFor kind id
            match s.Capture, s.Over with
            | ValueSome l, _
            | ValueNone, ValueSome l -> if isIn l then l.Input.PointerCanceled(kind, id)
            | _ -> ()
            s.Buttons <- 0
            s.Capture <- ValueNone
            false
        | PointerWheel(p, dx, dy, mods) ->
            let p = dips p
            (wheelLayerAt p).Input.Wheel(p, dx, dy, mods)
            false
        | KeyDown(key, mods, isRepeat) ->
            let mutable taken = false
            for preview in keyPreviews.ToArray() do
                if not taken then taken <- preview key mods
            taken || (keyboardLayer ()).Input.KeyDown(key, mods, isRepeat)
        | KeyUp(key, mods) -> (keyboardLayer ()).Input.KeyUp(key, mods)
        | FocusChanged active ->
            windowActive <- active
            (keyboardLayer ()).Input.WindowFocusChanged active
            false
        | _ -> false

    interface System.IDisposable with
        member _.Dispose() =
            themeSubscription.Dispose()
            surface.Dispose()

module ElementHost =
    /// A host for `window`, drawing with `device` and `text`:
    /// `ElementHost.create window device text [ ElementHost.background (Color.Hex 0x181825) ]`.
    let create (window: IWindow) (device: IRenderDevice) (text: TextSystem) (settings: ElementHostSetting list) =
        let defaults = { Background = ValueNone; Dispatcher = ValueNone; BeforeFrame = ignore; Styles = []; Theme = ValueNone; AppValues = ValueNone; Clock = ValueNone }
        let options = settings |> List.fold (fun o (ElementHostSetting apply) -> apply o) defaults
        new ElementHost(window, device, text, options)
