namespace Scribble.Core

open System
open Scribble.Rendering
open Scribble.Text

/// A tree of elements with what it needs to lay itself out and draw: the text system, the pixel
/// scale, and a callback for "something changed, please give me a frame". Works without a window,
/// which is how layout tests run.
[<Sealed>]
type ElementTree(text: TextSystem) =
    let ownerThread = Environment.CurrentManagedThreadId
    let mutable root: Element voption = ValueNone
    let mutable scale = 1.0
    let mutable onInvalidated: unit -> unit = ignore

    member _.Text = text

    /// Called when the tree needs a new frame. ElementHost sets this to the window's RequestFrame.
    member _.OnInvalidated
        with get () = onInvalidated
        and set v = onInvalidated <- v

    member this.Root
        with get () = root
        and set (value: Element voption) =
            match root with
            | ValueSome old -> old.SetRoot ValueNone
            | ValueNone -> ()
            root <- value
            match value with
            | ValueSome e ->
                if e.Parent.IsSome then invalidOp "The root element can't have a parent."
                e.SetRoot(ValueSome(this :> IElementRoot))
                e.InvalidateAll()
            | ValueNone -> ()
            onInvalidated ()

    /// Shows a view: the first time it builds the elements, after that it patches them, so only
    /// what changed between the previous view and this one is redone.
    member this.Update(view: View) =
        match root with
        | ValueSome r when obj.ReferenceEquals(r.Type, view.Type) -> r.Patch view
        | _ -> this.Root <- ValueSome(Element.Realize view)

    /// Physical pixels per DIP. Changing it re-lays out everything (rounding and text sizes change).
    member _.Scale
        with get () = scale
        and set v =
            if v <> scale then
                scale <- v
                root |> ValueOption.iter _.InvalidateAll()
                onInvalidated ()

    /// Lays the tree out in `size` DIPs. Only invalid parts do any work.
    member _.UpdateLayout(size: Size) =
        match root with
        | ValueSome r ->
            r.Measure size
            r.Arrange(Rect.Create(0.0, 0.0, size.Width, size.Height))
        | ValueNone -> ()

    member _.Render(list: DrawList) = root |> ValueOption.iter (fun r -> r.Render list)

    /// The deepest visible element under a point (DIPs, tree coordinates).
    /// Purely geometric for now; input routing arrives in Milestone 5.
    member _.HitTest(x: float, y: float) : Element voption =
        let rec hit (e: Element) (ox: float) (oy: float) =
            if not e.IsVisible then ValueNone
            else
                let b = e.Bounds
                let left, top = ox + b.X, oy + b.Y
                if x >= left && x < left + b.Width && y >= top && y < top + b.Height then
                    // Later children are drawn on top, so test them first.
                    let children = e.Children
                    let mutable found = ValueNone
                    let mutable i = children.Count - 1
                    while found.IsNone && i >= 0 do
                        found <- hit children[i] left top
                        i <- i - 1
                    if found.IsSome then found else ValueSome e
                else ValueNone
        match root with
        | ValueSome r -> hit r 0.0 0.0
        | ValueNone -> ValueNone

    interface IElementRoot with
        member _.Text = text
        member _.Scale = scale
        member _.Invalidated() = onInvalidated ()
        member _.VerifyAccess() =
#if DEBUG
            if Environment.CurrentManagedThreadId <> ownerThread then
                invalidOp "UI elements can only be used on the UI thread (the thread that created the tree)."
#else
            ignore ownerThread
#endif
