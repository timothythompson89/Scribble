namespace Scribble.Core

open System
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering

module internal CaptureProps =
    /// On any element: the `Capture` it belongs to (`Layoutable.capture`). Boxed, so this can come before the type.
    let capture =
        let p = Prop<obj>("Capture", null, Affects.Nothing, PropKind.Value)
        p.Tracked <- true
        // App state: a style can't give every matched element one capture (round 1f).
        p.IsAppState <- true
        p

type internal CaptureOptions =
    { Scale: float voption
      Background: Color
      Margin: float }

/// One setting for `capture.ToImage [ ... ]`.
[<Struct>]
type CaptureSetting = internal CaptureSetting of (CaptureOptions -> CaptureOptions)

/// Asks for an image of an element as it is on screen (DESIGN.md, "Rendering to an image and saving it", decision 5;
/// GLOSSARY: Capture). Make one (`Capture()`, or `ctx.useCapture ()` in a component), give it to an element with
/// `Layoutable.capture`, then ask whenever you like:
/// ```
/// let chart = Capture()
/// Border.create [ Border.capture chart; Border.content (lineChart model) ]
/// // in a handler:
/// let! image = chart.ToImage [ Capture.scale 2.0 ]
/// let! png = Image.toPng image
/// ```
/// The picture is the element's own box and everything inside it, as last drawn: its hover, its scroll position,
/// its components' state. What's around it doesn't cut it (an element half scrolled out comes out whole), but only
/// what exists is drawn: an ItemsView inside shows the items it has realized. Transforms on the elements around it
/// are left out, as are the layers above it (flyouts, the inspector). Given to several elements, it captures the
/// last one that took it.
[<Sealed>]
type Capture() =
    let mutable target: WeakReference<Element> = null

    /// Pixels per DIP: the window's by default, or another (a sharper 2× picture from a 1× screen).
    static member scale(value: float) =
        if not (value > 0.0) || Double.IsInfinity value then invalidArg (nameof value) "The scale must be above 0."
        CaptureSetting(fun o -> { o with Scale = ValueSome value })

    /// What's behind the element: transparent by default (decision 6), so the picture is the element alone.
    static member background(color: Color) = CaptureSetting(fun o -> { o with Background = color })

    /// Room around the element, in DIPs, for what it draws outside its box (a shadow): 0 by default.
    static member margin(value: float) =
        if value < 0.0 || Double.IsNaN value || Double.IsInfinity value then
            invalidArg (nameof value) "The margin must be 0 or more."
        CaptureSetting(fun o -> { o with Margin = value })

    interface ITrackedValue with
        member _.Taken(element) = target <- WeakReference<Element>(element :?> Element)

    /// The element that has it now, in a tree, or why there's none.
    member private this.Element: Result<Element, string> =
        match target with
        | null -> Error "This Capture isn't on an element: give it to one with `capture` (Border.capture c) first."
        | t ->
            match t.TryGetTarget() with
            | true, e when e.Root.IsSome && obj.ReferenceEquals(e.GetBoxed CaptureProps.capture, this) -> Ok e
            | _ -> Error "The element this Capture was on has left the screen (or no longer has it)."

    /// An image of its element as it is now. Call on the UI thread, between frames (a handler, an effect): it draws
    /// what the last frame laid out and asks for no frame of its own. The task fails if the Capture isn't on an
    /// element that's showing, the element has no size yet, or the picture would be bigger than the GPU takes.
    member this.ToImage(settings: CaptureSetting list) : Task<Image> =
        try
            let element =
                match this.Element with
                | Ok e -> e
                | Error message -> invalidOp message
            let root = element.Root.Value
            root.VerifyAccess()
            let o =
                settings
                |> List.fold (fun o (CaptureSetting apply) -> apply o) { Scale = ValueNone; Background = Color.Transparent; Margin = 0.0 }
            let scale = o.Scale |> ValueOption.defaultValue root.Scale
            let b = element.Bounds
            if b.IsEmpty then invalidOp "The element has no size yet (it hasn't been laid out, or it's collapsed)."
            let width, height = b.Width + 2.0 * o.Margin, b.Height + 2.0 * o.Margin
            let device = RenderServices.Current.Device
            let px: PixelSize =
                { Width = int (Math.Ceiling(width * scale - 0.001))
                  Height = int (Math.Ceiling(height * scale - 0.001)) }
            RenderedImage.checkSize device px.Width px.Height
            let rendered =
                device.RenderOffscreenAsync(px, scale, fun frame ->
                    frame.Clear o.Background
                    let list = frame.DrawList
                    // The element draws at its place in its parent: move that place to the margin's corner.
                    list.PushTranslate(o.Margin - b.X, o.Margin - b.Y)
                    element.Render list
                    list.PopTransform())
            task {
                let! bitmap = rendered
                return RenderedImage.ofBitmap bitmap scale
            }
        with e ->
            Task.FromException<Image> e
