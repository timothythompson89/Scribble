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
/// and clicks: a press and release of the left button on the same element with an `onClick`
/// setting (or inside one). Full input routing arrives in Milestone 5.
[<Sealed>]
type ElementHost(window: IWindow, device: IRenderDevice, text: TextSystem) =
    let tree = ElementTree(text)
    let surface = device.CreateSurface(window.Surface, window.ClientSize)
    let mutable background = Color.Black
    let mutable stats = { LayoutMilliseconds = 0.0; RenderMilliseconds = 0.0; Shapes = 0 }
    let mutable beforeFrame: unit -> unit = ignore
    /// The clickable element the left button went down on.
    let mutable pressed: Element voption = ValueNone

    let rec clickable (e: Element voption) =
        match e with
        | ValueSome x when (x.Get LayoutProps.onClick).IsSome -> e
        | ValueSome x -> clickable x.Parent
        | ValueNone -> ValueNone

    let rec isInside (target: Element) (e: Element voption) =
        match e with
        | ValueSome x -> obj.ReferenceEquals(x, target) || isInside target x.Parent
        | ValueNone -> false

    let hit (p: PixelPoint) = tree.HitTest(float p.X / window.Scale, float p.Y / window.Scale)

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
        beforeFrame ()
        let size = window.ClientSize
        let scale = window.Scale
        tree.Scale <- scale
        let started = Stopwatch.GetTimestamp()
        tree.UpdateLayout { Width = float size.Width / scale; Height = float size.Height / scale }
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

    member this.HandleEvent(event: PlatformEvent) =
        match event with
        | Frame ->
            inFrame <- true
            try
                this.DrawFrame()
            finally
                inFrame <- false
        | Resized size -> surface.Resize size
        | ScaleChanged scale -> tree.Scale <- scale
        | PointerPressed(_, _, p, MouseButton.Left) -> pressed <- clickable (hit p)
        | PointerReleased(_, _, p, MouseButton.Left) ->
            match pressed with
            | ValueSome target ->
                pressed <- ValueNone
                if isInside target (hit p) then
                    match target.Get LayoutProps.onClick with
                    | ValueSome handler -> handler ()
                    | ValueNone -> ()
            | ValueNone -> ()
        | _ -> ()

    interface System.IDisposable with
        member _.Dispose() = surface.Dispose()
