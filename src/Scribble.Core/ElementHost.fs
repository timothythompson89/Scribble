namespace Scribble.Core

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

/// Connects an element tree to a window: lays the tree out at the window's size and scale, draws it
/// on every Frame, and asks for a frame whenever something in the tree changes.
///
/// Forward the window's events to `HandleEvent`; the host handles Frame, Resized and ScaleChanged,
/// pointer input (routed events, pointer capture, clicks, element states, the cursor) and the
/// keyboard (focus, Tab order, routed key events, Enter/Space activation).
[<Sealed>]
type ElementHost(window: IWindow, device: IRenderDevice, text: TextSystem) =
    let tree = ElementTree(text)
    let surface = device.CreateSurface(window.Surface, window.ClientSize)
    let mutable background = Color.Black
    let mutable stats = { LayoutMilliseconds = 0.0; RenderMilliseconds = 0.0; Shapes = 0 }
    let mutable beforeFrame: unit -> unit = ignore
    let input = InputRouter(tree, window.SetCursor)
    let clock = Stopwatch.StartNew()

    let dips (p: PixelPoint) = Point(p.X / window.Scale, p.Y / window.Scale)

    /// True while a frame is being prepared and drawn: changes made then (an update patched in
    /// before layout) are drawn by this frame, so they don't need to ask for another.
    let mutable inFrame = false

    do
        tree.Scale <- window.Scale
        tree.OnInvalidated <- fun () -> if not inFrame then window.RequestFrame()

    member _.Window = window
    member _.Tree = tree

    member _.Root
        with get () = tree.Root
        and set (v: Element voption) = tree.Root <- v

    /// Cleared behind the tree every frame.
    member _.Background
        with get () = background
        and set v =
            background <- v
            window.RequestFrame()

    /// Runs at the start of every frame, before layout: the place to advance animations (which
    /// change properties and so ask for the next frame themselves).
    member _.BeforeFrame
        with get () = beforeFrame
        and set v = beforeFrame <- v

    member _.LastFrame = stats

    /// Shows a view (builds the elements the first time, patches them after that).
    member _.Update(view: View) = tree.Update view

    member private _.DrawFrame() =
        tree.Time <- clock.Elapsed
        beforeFrame ()
        let size = window.ClientSize
        let scale = window.Scale
        tree.Scale <- scale
        let started = Stopwatch.GetTimestamp()
        tree.UpdateLayout { Width = float size.Width / scale; Height = float size.Height / scale }
        // Layout may have moved things under a pointer that stood still (hover, cursor).
        input.Refresh()
        let laidOut = Stopwatch.GetTimestamp()
        match surface.TryBeginFrame scale with
        | Some frame ->
            use frame = frame
            frame.Clear background
            tree.Render frame.DrawList
            stats <-
                { LayoutMilliseconds = Stopwatch.GetElapsedTime(started, laidOut).TotalMilliseconds
                  RenderMilliseconds = Stopwatch.GetElapsedTime(laidOut).TotalMilliseconds
                  Shapes = frame.DrawList.Count }
        | None -> ()

    /// The element with keyboard focus, if any.
    member _.Focused: Element voption = input.Focused

    /// Moves keyboard focus as Tab (Next) or Shift+Tab (Previous) would.
    member _.MoveFocus(direction: FocusDirection) = input.MoveFocus direction

    /// Handles a window event. Returns true for a key that an element (or built-in behavior such
    /// as Tab or a focused button's Enter) handled; the app's own key handling skips those.
    member this.HandleEvent(event: PlatformEvent) : bool =
        match event with
        | Frame ->
            inFrame <- true
            try
                this.DrawFrame()
            finally
                inFrame <- false
            // Effects and "keep animating" requests: outside the frame, so what they change asks
            // for the next one.
            tree.RunAfterFrame()
            false
        | Resized size -> surface.Resize size; false
        | ScaleChanged scale -> tree.Scale <- scale; false
        | PointerMoved(kind, id, p, mods) -> input.PointerMoved(kind, id, dips p, mods); false
        | PointerPressed(kind, id, p, button, mods) -> input.PointerPressed(kind, id, dips p, button, mods); false
        | PointerReleased(kind, id, p, button, mods) -> input.PointerReleased(kind, id, dips p, button, mods); false
        | PointerExited(kind, id) -> input.PointerExited(kind, id); false
        | PointerCanceled(kind, id) -> input.PointerCanceled(kind, id); false
        | PointerWheel(p, dx, dy, mods) -> input.Wheel(dips p, dx, dy, mods); false
        | KeyDown(key, mods, isRepeat) -> input.KeyDown(key, mods, isRepeat)
        | KeyUp(key, mods) -> input.KeyUp(key, mods)
        | FocusChanged active -> input.WindowFocusChanged active; false
        | _ -> false

    interface System.IDisposable with
        member _.Dispose() = surface.Dispose()
