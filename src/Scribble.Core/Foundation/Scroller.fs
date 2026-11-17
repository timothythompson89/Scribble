namespace Scribble.Core

open System.Collections.Generic

/// Where something scrolled to lands in the viewport.
[<RequireQualifiedAccess>]
type ScrollAlign =
    /// No move when it's already fully visible; otherwise the least scroll that shows it.
    | Nearest
    /// At the top (or left).
    | Start
    /// In the middle.
    | Center
    /// At the bottom (or right).
    | End

/// A scroll position that can be restored exactly: the first visible item's key, and how far the
/// viewport's top is below that item's top (DIPs). `Scroller.FirstVisible` gives it;
/// `Scroller.ScrollTo(position)` goes back to it.
[<Struct>]
type ScrollPosition = { Key: obj; Offset: float }

/// What a scroller was asked to do; the element it's attached to applies it at its next layout.
[<RequireQualifiedAccess>]
type internal ScrollRequest =
    | ToKey of key: obj * align: ScrollAlign * animate: bool
    | ToIndex of index: int * align: ScrollAlign * animate: bool
    | ToPosition of position: ScrollPosition
    | ToOffset of y: float * animate: bool
    | By of dy: float * animate: bool

/// Scrolls a ScrollViewer or an ItemsView from code, as an action: `let scroller = ctx.useScroller ()`,
/// `ItemsView.scroller scroller` (or `ScrollViewer.scroller scroller`), then in a handler
/// `scroller.ScrollTo(key)`. Requests apply at the next layout (an item added in the same handler
/// can be scrolled to); several in one handler apply in order. Use it on the UI thread.
[<Sealed>]
type Scroller() =
    let requests = Queue<ScrollRequest>()
    let mutable wake: unit -> unit = ignore
    let mutable offset = 0.0
    let mutable horizontalOffset = 0.0
    let mutable firstVisible: ScrollPosition voption = ValueNone

    member private _.Add(request: ScrollRequest) =
        requests.Enqueue request
        wake ()

    /// How far the content is scrolled down (DIPs), as of the last layout.
    member _.Offset = offset
    /// How far the content is scrolled right (DIPs), as of the last layout.
    member _.HorizontalOffset = horizontalOffset
    /// The first item at least partly visible, and how far the viewport's top is below its top
    /// (an ItemsView's scroller only; as of the last layout). Keep it to come back later with
    /// `ScrollTo(position)`.
    member _.FirstVisible = firstVisible

    /// Scrolls to the item (an ItemsView's) or keyed element with `key`. A key it doesn't have does
    /// nothing. Animates briefly unless `animate` is false or it's far away.
    member this.ScrollTo(key: obj, ?align: ScrollAlign, ?animate: bool) =
        this.Add(ScrollRequest.ToKey(key, defaultArg align ScrollAlign.Nearest, defaultArg animate true))

    /// Back to a position from `FirstVisible`, exactly (no animation).
    member this.ScrollTo(position: ScrollPosition) = this.Add(ScrollRequest.ToPosition position)

    /// Scrolls to the item at `index` in an ItemsView's source.
    member this.ScrollToIndex(index: int, ?align: ScrollAlign, ?animate: bool) =
        this.Add(ScrollRequest.ToIndex(index, defaultArg align ScrollAlign.Nearest, defaultArg animate true))

    /// Scrolls so the content's top is `offset` DIPs above the viewport's (clamped to what there is).
    member this.ScrollToOffset(offset: float, ?animate: bool) = this.Add(ScrollRequest.ToOffset(offset, defaultArg animate true))

    /// Scrolls down by `delta` DIPs (up when negative).
    member this.ScrollBy(delta: float, ?animate: bool) = this.Add(ScrollRequest.By(delta, defaultArg animate true))

    member this.ScrollToTop(?animate: bool) = this.ScrollToOffset(0.0, defaultArg animate true)

    /// The element applying requests: `wakeUp` asks it for a layout.
    member internal _.Bind(wakeUp: unit -> unit) =
        wake <- wakeUp
        if requests.Count > 0 then wakeUp ()

    member internal _.HasRequests = requests.Count > 0
    member internal _.Take() : ScrollRequest list =
        let all = List.ofSeq requests
        requests.Clear()
        all

    /// Requests that couldn't apply yet (the element isn't laid out): kept for the next layout.
    member internal _.PutBack(rest: ScrollRequest list) =
        let later = List.ofSeq requests
        requests.Clear()
        for r in rest @ later do
            requests.Enqueue r

    member internal _.Report(x: float, y: float) =
        horizontalOffset <- x
        offset <- y

    member internal _.ReportFirstVisible(p: ScrollPosition voption) = firstVisible <- p
