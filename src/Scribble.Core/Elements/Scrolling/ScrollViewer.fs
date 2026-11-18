namespace Scribble.Core

open System
open System.Collections.Generic
open System.Diagnostics
open Scribble.Rendering

/// Whether a ScrollViewer scrolls in one direction, and shows a bar for it.
[<RequireQualifiedAccess>]
type ScrollBarMode =
    /// Scrolls when the content is bigger; a bar shows then.
    | Auto
    /// Scrolls (wheel, keys, touch, code) without a bar.
    | Hidden
    /// Doesn't scroll: the content gets the viewer's size that way (text wraps to its width).
    | Disabled

/// Where a ScrollViewer's bars go.
[<RequireQualifiedAccess>]
type ScrollBarSpace =
    /// A gutter on the scrolling side, kept whether or not the content overflows (so the content's
    /// width never changes when it starts to; it can't re-wrap and flip back).
    | Reserved
    /// Over the content (image viewers, maps, phone-style lists): the app leaves room if needed.
    | Overlay

/// A ScrollViewer's state, kept on the element.
[<Sealed; AllowNullLiteral>]
type internal ScrollState() =
    /// The offsets shown (content's top-left is this far above and left of the viewport's).
    member val X = 0.0 with get, set
    member val Y = 0.0 with get, set
    // An animation from (FromX, FromY) to (ToX, ToY); it starts at the next arrange when pending.
    member val Animating = false with get, set
    member val AnimPending = false with get, set
    member val FromX = 0.0 with get, set
    member val FromY = 0.0 with get, set
    member val ToX = 0.0 with get, set
    member val ToY = 0.0 with get, set
    member val AnimStart = TimeSpan.Zero with get, set
    member val AnimDuration = TimeSpan.Zero with get, set
    // Sizes from the last arrange: the content's, and the part shown (without the gutters).
    member val ExtentW = 0.0 with get, set
    member val ExtentH = 0.0 with get, set
    member val ViewW = 0.0 with get, set
    member val ViewH = 0.0 with get, set
    // A touch fling: velocity of the offsets (DIPs per second) and when it last moved.
    member val Flinging = false with get, set
    member val VelocityX = 0.0 with get, set
    member val VelocityY = 0.0 with get, set
    member val FlingLast = TimeSpan.MinValue with get, set
    /// Recent pan moves (Stopwatch time, dx, dy), for the fling's speed.
    member val PanSamples = Queue<struct (int64 * float * float)>() with get
    /// Dragging a thumb: 0 none, 1 vertical, 2 horizontal; where the pointer and offset started.
    member val Drag = 0 with get, set
    member val DragStart = 0.0 with get, set
    member val DragStartOffset = 0.0 with get, set
    /// The bar under the mouse: 0 none, 1 vertical, 2 horizontal.
    member val HoverBar = 0 with get, set
    /// Its own scroller (when no `scroller` setting gives one) and the one it applies requests from.
    member val Own = Scroller() with get
    member val Bound: Scroller voption = ValueNone with get, set
    /// What the elements inside were last told is visible (to tell them only when it changes).
    member val NotifiedX = nan with get, set
    member val NotifiedY = nan with get, set
    member val NotifiedW = nan with get, set
    member val NotifiedH = nan with get, set

    member s.MaxX = max 0.0 (s.ExtentW - s.ViewW)
    member s.MaxY = max 0.0 (s.ExtentH - s.ViewH)

module ScrollViewerProps =
    let content = Prop.content "Content"
    /// The scrollbar thumb's color (the theme sets it).
    let thumb = Prop.define "Thumb" (Color.White.WithAlpha 0.3f) Affects.Render
    /// The faint track under a thumb while the pointer is on it (the theme sets it).
    let track = Prop.define "Track" (Color.White.WithAlpha 0.06f) Affects.Render
    /// The gutter's width (DIPs; the theme sets it).
    let barSize = Prop.define "BarSize" 12.0 Affects.Measure
    let vertical = Prop.define "Vertical" ScrollBarMode.Auto Affects.Measure
    let horizontal = Prop.define "Horizontal" ScrollBarMode.Disabled Affects.Measure
    let scrollBarSpace = Prop.define "ScrollBarSpace" ScrollBarSpace.Reserved Affects.Measure
    let scroller = Prop.define "Scroller" (ValueNone: Scroller voption) Affects.Arrange
    let internal state = Prop.local "ScrollState" (null: ScrollState)

/// Shows one child, scrolled when it's bigger than the space the ScrollViewer gets: by the mouse
/// wheel (Shift for sideways), the bars (drag the thumb, click the track to page), the keyboard
/// (arrows, PageUp/PageDown, Home/End, once something inside has focus or after a click on it), a
/// finger (with a fling), and code (`ScrollViewer.scroller`). Give it a bounded height (a fixed
/// height, a Star share, or a place that stretches it, like a layer or a window): with unlimited
/// height it simply grows to fit. Vertical by default (`ScrollViewer.horizontal ScrollBarMode.Auto`
/// for sideways); the bar's gutter is kept by default (`scrollBarSpace`).
[<AbstractClass>]
type ScrollViewer =
    inherit Layoutable
    static member content<'k when 'k :> ScrollViewer>(value: View) : Setting<'k> = Setting.create ScrollViewerProps.content (ValueSome value)
    /// The scrollbar thumb's color.
    static member thumb<'k when 'k :> ScrollViewer>(value: Color) : Setting<'k> = Setting.create ScrollViewerProps.thumb value
    static member thumb<'k when 'k :> ScrollViewer>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme ScrollViewerProps.thumb themeValue
    /// The faint track shown under a thumb while the pointer is on the bar.
    static member track<'k when 'k :> ScrollViewer>(value: Color) : Setting<'k> = Setting.create ScrollViewerProps.track value
    static member track<'k when 'k :> ScrollViewer>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme ScrollViewerProps.track themeValue
    /// The bars' gutter width in DIPs (default 12; the thumb is 4 wide at rest, wider under the pointer).
    static member barSize<'k when 'k :> ScrollViewer>(value: float) : Setting<'k> = Setting.create ScrollViewerProps.barSize value
    static member barSize<'k when 'k :> ScrollViewer>(themeValue: ThemeValue<float>) : Setting<'k> = Setting.fromTheme ScrollViewerProps.barSize themeValue
    /// Up and down: `Auto` (default), `Hidden` (scrolls, no bar) or `Disabled`.
    static member vertical<'k when 'k :> ScrollViewer>(mode: ScrollBarMode) : Setting<'k> = Setting.create ScrollViewerProps.vertical mode
    /// Sideways: `Disabled` by default (the content gets the viewer's width), `Auto` or `Hidden`.
    static member horizontal<'k when 'k :> ScrollViewer>(mode: ScrollBarMode) : Setting<'k> = Setting.create ScrollViewerProps.horizontal mode
    /// `Reserved` (default): a gutter for each bar that can show; `Overlay`: bars over the content.
    static member scrollBarSpace<'k when 'k :> ScrollViewer>(space: ScrollBarSpace) : Setting<'k> = Setting.create ScrollViewerProps.scrollBarSpace space
    /// A scroller to scroll it from code (`ctx.useScroller ()`).
    static member scroller<'k when 'k :> ScrollViewer>(scroller: Scroller) : Setting<'k> = Setting.create ScrollViewerProps.scroller (ValueSome scroller)

module ScrollViewer =
    /// DIPs per wheel notch and arrow key (about three lines of text).
    // Internal: a multi-line TextBox scrolls and draws its bar the same way (DESIGN.md, "9c", decision 36).
    let internal step = 48.0
    let internal thinBar = 4.0
    let internal inset = 2.0
    let private minThumb = 20.0
    let private wheelTime = TimeSpan.FromMilliseconds 100.0
    let private scrollTime = TimeSpan.FromMilliseconds 200.0

    let internal stateOf (e: Element) =
        match e.Get ScrollViewerProps.state with
        | null ->
            let s = ScrollState()
            e.SetLocal(ScrollViewerProps.state, s)
            s
        | s -> s

    /// The gutters (right, bottom) the bars keep, from the settings.
    let private gutterOf (vertical: ScrollBarMode) (horizontal: ScrollBarMode) (space: ScrollBarSpace) (size: float) =
        let reserved = space = ScrollBarSpace.Reserved
        struct ((if reserved && vertical = ScrollBarMode.Auto then size else 0.0),
                (if reserved && horizontal = ScrollBarMode.Auto then size else 0.0))

    /// Starts moving to (x, y): animated over `duration`, or at once.
    let private moveTo (e: Element) (s: ScrollState) (x: float) (y: float) (duration: TimeSpan voption) =
        s.Flinging <- false
        match duration with
        | ValueSome d ->
            s.FromX <- s.X
            s.FromY <- s.Y
            s.ToX <- x
            s.ToY <- y
            s.AnimDuration <- d
            s.Animating <- true
            s.AnimPending <- true
        | ValueNone ->
            s.Animating <- false
            s.X <- x
            s.Y <- y
        e.InvalidateArrange()

    /// Where it's heading: the animation's end, else where it is.
    let private targetX (s: ScrollState) = if s.Animating then s.ToX else s.X
    let private targetY (s: ScrollState) = if s.Animating then s.ToY else s.Y

    /// Scrolls by (dx, dy) toward where it's heading, animated briefly; false when it's at the end already.
    let private scrollBy (e: Element) (s: ScrollState) (dx: float) (dy: float) (duration: TimeSpan voption) =
        let x = Math.Clamp(targetX s + dx, 0.0, s.MaxX)
        let y = Math.Clamp(targetY s + dy, 0.0, s.MaxY)
        if x <> targetX s || y <> targetY s then
            moveTo e s x y duration
            true
        else false

    /// A wheel delta that is whole notches (a mouse wheel) animates; finer ones (a touchpad) apply directly.
    let private isNotch (d: float) = abs d >= 0.99 && abs (d - Math.Round d) < 0.01

    /// A thumb's place along its track: (track start, track length, thumb start, thumb length), or
    /// none when the content fits.
    let internal thumbSpan (trackLength: float) (extent: float) (viewport: float) (offset: float) =
        if extent > viewport && viewport > 0.0 && trackLength > 0.0 then
            let length = min trackLength (max minThumb (trackLength * viewport / extent))
            let travel = if extent - viewport > 0.0 then offset / (extent - viewport) else 0.0
            ValueSome(struct (length, (trackLength - length) * Math.Clamp(travel, 0.0, 1.0)))
        else ValueNone

    let private bars (e: Element) (s: ScrollState) =
        let v, h = e.Get ScrollViewerProps.vertical, e.Get ScrollViewerProps.horizontal
        let showV = v = ScrollBarMode.Auto && s.ExtentH > s.ViewH
        let showH = h = ScrollBarMode.Auto && s.ExtentW > s.ViewW
        struct (showV, showH)

    /// Which bar's strip a local point is in: 0 none, 1 vertical, 2 horizontal.
    let private barAt (e: Element) (s: ScrollState) (p: Point) =
        let size = e.Get ScrollViewerProps.barSize
        let b = e.Bounds
        let struct (showV, showH) = bars e s
        if showV && p.X >= b.Width - size && p.Y < s.ViewH then 1
        elif showH && p.Y >= b.Height - size && p.X < s.ViewW then 2
        else 0

    /// The thumb's travel per DIP of pointer movement (offset per DIP), for dragging.
    let private dragRatio (s: ScrollState) (axis: int) =
        let trackLength, extent, viewport = (if axis = 1 then s.ViewH else s.ViewW) - 2.0 * inset, (if axis = 1 then s.ExtentH else s.ExtentW), (if axis = 1 then s.ViewH else s.ViewW)
        match thumbSpan trackLength extent viewport 0.0 with
        | ValueSome(struct (length, _)) when trackLength - length > 0.0 -> (extent - viewport) / (trackLength - length)
        | _ -> 0.0

    /// Looks inside for an ItemsView with the item, or an element keyed `key`, and scrolls to it.
    let rec private showKey (e: Element) (key: obj) (align: ScrollAlign) (animate: bool) =
        let mutable found = false
        let children = e.Children
        let mutable i = 0
        while not found && i < children.Count do
            let c = children[i]
            match c.CurrentView with
            | ValueSome v when v.Key.IsSome && obj.Equals(v.Key.Value, key) ->
                c.BringRectIntoView(Rect.Create(0.0, 0.0, c.Bounds.Width, c.Bounds.Height), align, animate)
                found <- true
            | _ -> ()
            if not found then
                match c.Type.Scrolling with
                | ValueSome hooks -> found <- hooks.ScrollToKey c key align animate
                | ValueNone -> ()
            if not found then found <- showKey c key align animate
            i <- i + 1
        found

    /// The new offset along one axis that shows [start, start + length) of the content, as `align` says.
    let internal alignedOffset (current: float) (viewport: float) (start: float) (length: float) (align: ScrollAlign) =
        match align with
        | ScrollAlign.Start -> start
        | ScrollAlign.End -> start + length - viewport
        | ScrollAlign.Center -> start + length / 2.0 - viewport / 2.0
        | ScrollAlign.Nearest ->
            if start >= current && start + length <= current + viewport then current
            elif start < current || length > viewport then start
            else start + length - viewport

    /// Scrolls so `rect` (the viewer's own coordinates) shows; returns how far the content moved.
    let private makeVisible (e: Element) (rect: Rect) (align: ScrollAlign) (animate: bool) : Point =
        let s = stateOf e
        let v, h = e.Get ScrollViewerProps.vertical, e.Get ScrollViewerProps.horizontal
        // Rect in content coordinates: undo the content's position as arranged (what the rect was
        // offset by on its way here), then add its margin back, which is part of the extent (without
        // it a page with a top margin stopped that much short of an item at its end). Not -X, -Y:
        // the position was rounded to pixels when arranged, and the offset may have moved since.
        let cx, cy =
            if e.Children.Count > 0 then
                let c = e.Children[0]
                let m = c.Get LayoutProps.margin
                rect.X - c.Bounds.X + m.Left, rect.Y - c.Bounds.Y + m.Top
            else rect.X + s.X, rect.Y + s.Y
        let x = if h = ScrollBarMode.Disabled then s.X else max 0.0 (alignedOffset (targetX s) s.ViewW cx rect.Width align)
        let y = if v = ScrollBarMode.Disabled then s.Y else max 0.0 (alignedOffset (targetY s) s.ViewH cy rect.Height align)
        // How far the content moves (now, or by the animation's end): taken before moving, since a
        // jump changes the offset at once.
        let moved = Point(x - s.X, y - s.Y)
        if x <> targetX s || y <> targetY s then
            // Far away: jump (animating past thousands of items would realize them all).
            let far = abs (y - s.Y) > 3.0 * s.ViewH || abs (x - s.X) > 3.0 * s.ViewW
            moveTo e s x y (if animate && not far then ValueSome scrollTime else ValueNone)
        moved

    /// Applies what its scroller was asked (offsets here; keys through what's inside).
    let private applyRequests (e: Element) (s: ScrollState) =
        let scroller =
            match e.Get ScrollViewerProps.scroller with
            | ValueSome sc -> sc
            | ValueNone -> s.Own
        if not (match s.Bound with ValueSome b -> obj.ReferenceEquals(b, scroller) | ValueNone -> false) then
            s.Bound <- ValueSome scroller
            scroller.Bind(fun () -> e.InvalidateArrange())
        if scroller.HasRequests then
            for r in scroller.Take() do
                let duration animate = if animate then ValueSome scrollTime else ValueNone
                match r with
                | ScrollRequest.ToOffset(y, animate) -> moveTo e s (targetX s) (max 0.0 y) (duration animate)
                | ScrollRequest.By(dy, animate) -> moveTo e s (targetX s) (max 0.0 (targetY s + dy)) (duration animate)
                | ScrollRequest.ToKey(key, align, animate) -> showKey e key align animate |> ignore
                | ScrollRequest.ToIndex _
                | ScrollRequest.ToPosition _ -> Debug.WriteLine "ScrollViewer: items by index or position need an ItemsView's scroller."
        scroller

    let Type =
        Element.define "ScrollViewer" [
            Element.defaults [ Scribble.Accessibility.Semantics.role Scribble.Accessibility.Role.ScrollArea ]
            Element.measure (fun ctx available ->
                let v, h = ctx.Get ScrollViewerProps.vertical, ctx.Get ScrollViewerProps.horizontal
                let struct (gutterV, gutterH) = gutterOf v h (ctx.Get ScrollViewerProps.scrollBarSpace) (ctx.Get ScrollViewerProps.barSize)
                let s = stateOf ctx.Element
                // What will show, for what's inside to plan with (an ItemsView realizing items).
                if not (Double.IsInfinity available.Width) then s.ViewW <- max 0.0 (available.Width - gutterV)
                if not (Double.IsInfinity available.Height) then s.ViewH <- max 0.0 (available.Height - gutterH)
                let childAvailable =
                    Size.Create(
                        (if h = ScrollBarMode.Disabled then max 0.0 (available.Width - gutterV) else Double.PositiveInfinity),
                        (if v = ScrollBarMode.Disabled then max 0.0 (available.Height - gutterH) else Double.PositiveInfinity))
                let mutable w, ht = 0.0, 0.0
                for child in ctx.Children do
                    // As big as it likes in a scrolling direction: the part that doesn't fit is scrolled to.
                    let d = ctx.Measure(child, childAvailable)
                    w <- max w d.Width
                    ht <- max ht d.Height
                Size.Create(min (w + gutterV) available.Width, min (ht + gutterH) available.Height))
            Element.arrange (fun ctx size ->
                let e = ctx.Element
                let s = stateOf e
                let v, h = ctx.Get ScrollViewerProps.vertical, ctx.Get ScrollViewerProps.horizontal
                let struct (gutterV, gutterH) = gutterOf v h (ctx.Get ScrollViewerProps.scrollBarSpace) (ctx.Get ScrollViewerProps.barSize)
                let vw, vh = max 0.0 (size.Width - gutterV), max 0.0 (size.Height - gutterH)
                let mutable extentW, extentH = 0.0, 0.0
                for child in ctx.Children do
                    let d = ctx.DesiredSize child
                    extentW <- max extentW d.Width
                    extentH <- max extentH d.Height
                s.ViewW <- vw
                s.ViewH <- vh
                s.ExtentW <- (if h = ScrollBarMode.Disabled then vw else extentW)
                s.ExtentH <- (if v = ScrollBarMode.Disabled then vh else extentH)
                let scroller = applyRequests e s
                let now = ctx.Time
                let again () =
                    match e.Root with
                    | ValueSome r -> r.AfterFrame(fun () -> e.InvalidateArrange())
                    | ValueNone -> ()
                if s.Animating then
                    // Started a frame ago, so the first frame already moves.
                    if s.AnimPending then
                        s.AnimStart <- now - TimeSpan.FromMilliseconds 16.0
                        s.AnimPending <- false
                    let t = Math.Clamp((now - s.AnimStart).TotalMilliseconds / max 1.0 s.AnimDuration.TotalMilliseconds, 0.0, 1.0)
                    let k = Easing.easeOut t
                    s.X <- s.FromX + (s.ToX - s.FromX) * k
                    s.Y <- s.FromY + (s.ToY - s.FromY) * k
                    if t < 1.0 then again () else s.Animating <- false
                elif s.Flinging then
                    let dt = if s.FlingLast = TimeSpan.MinValue then 1.0 / 60.0 else min 0.1 (now - s.FlingLast).TotalSeconds
                    s.FlingLast <- now
                    s.X <- s.X + s.VelocityX * dt
                    s.Y <- s.Y + s.VelocityY * dt
                    // Slows to a stop over about a second.
                    let decay = Math.Exp(-3.0 * dt)
                    s.VelocityX <- s.VelocityX * decay
                    s.VelocityY <- s.VelocityY * decay
                    let atEnd = (s.Y <= 0.0 || s.Y >= s.MaxY) && (s.X <= 0.0 || s.X >= s.MaxX)
                    if atEnd || abs s.VelocityX + abs s.VelocityY < 20.0 then s.Flinging <- false else again ()
                // A resize or smaller content can leave it past the end.
                s.X <- Math.Clamp(s.X, 0.0, s.MaxX)
                s.Y <- Math.Clamp(s.Y, 0.0, s.MaxY)
                for child in ctx.Children do
                    ctx.Arrange(child, Rect.Create(-s.X, -s.Y, max s.ExtentW vw, max s.ExtentH vh))
                scroller.Report(s.X, s.Y)
                match e.Root with
                | ValueSome r when e.Name <> "" -> r.AppValues.RegisterScroller(e.Name, scroller)
                | _ -> ()
                if s.X <> s.NotifiedX || s.Y <> s.NotifiedY || vw <> s.NotifiedW || vh <> s.NotifiedH then
                    s.NotifiedX <- s.X
                    s.NotifiedY <- s.Y
                    s.NotifiedW <- vw
                    s.NotifiedH <- vh
                    e.ViewportChangedBelow())
            Element.clipChildren (fun ctx ->
                let s = stateOf ctx.Element
                Rect.Create(0.0, 0.0, s.ViewW, s.ViewH))
            Element.renderAfter (fun ctx list ->
                let e = ctx.Element
                let s = stateOf e
                let size = ctx.Size
                let barSize = ctx.Get ScrollViewerProps.barSize
                let struct (showV, showH) = bars e s
                let hover = if ctx.IsPointerOver then s.HoverBar else 0
                let thumb = ctx.Get ScrollViewerProps.thumb
                let draw (axis: int) =
                    let wide = hover = axis || s.Drag = axis
                    let thickness = ctx.Animate((if axis = 1 then "v" else "h"), (if wide then barSize - 2.0 else thinBar), TimeSpan.FromMilliseconds 100.0)
                    let trackLength = (if axis = 1 then s.ViewH else s.ViewW) - 2.0 * inset
                    let extent, viewport, offset = (if axis = 1 then s.ExtentH, s.ViewH, s.Y else s.ExtentW, s.ViewW, s.X)
                    match thumbSpan trackLength extent viewport offset with
                    | ValueSome(struct (length, start)) ->
                        if wide then
                            let track =
                                if axis = 1 then Rect.Create(size.Width - barSize, 0.0, barSize, s.ViewH)
                                else Rect.Create(0.0, size.Height - barSize, s.ViewW, barSize)
                            list.FillRect(track, ctx.Get ScrollViewerProps.track)
                        let r =
                            if axis = 1 then Rect.Create(size.Width - inset - thickness, inset + start, thickness, length)
                            else Rect.Create(inset + start, size.Height - inset - thickness, length, thickness)
                        list.FillRoundedRect(r, CornerRadius.Uniform(thickness / 2.0), thumb)
                    | ValueNone -> ()
                if showV then draw 1
                if showH then draw 2)
            Element.wheel (fun ctx e ->
                let el = ctx.Element
                let s = stateOf el
                let v, h = ctx.Get ScrollViewerProps.vertical, ctx.Get ScrollViewerProps.horizontal
                // Positive DeltaY is away from the user (towards the top); Shift turns it sideways.
                let sideways = e.DeltaX <> 0.0 || e.Modifiers.HasFlag Modifiers.Shift
                let dx, dy, delta =
                    if e.DeltaX <> 0.0 then e.DeltaX * step, 0.0, e.DeltaX
                    elif sideways then -e.DeltaY * step, 0.0, e.DeltaY
                    else 0.0, -e.DeltaY * step, e.DeltaY
                let dx = if h = ScrollBarMode.Disabled then 0.0 else dx
                let dy = if v = ScrollBarMode.Disabled then 0.0 else dy
                // At the end already: leave the wheel to an outer scroller.
                if (dx <> 0.0 || dy <> 0.0) && scrollBy el s dx dy (if isNotch delta then ValueSome wheelTime else ValueNone) then
                    e.Handle())
            Element.keyDown (fun ctx e ->
                let el = ctx.Element
                let s = stateOf el
                let v, h = ctx.Get ScrollViewerProps.vertical, ctx.Get ScrollViewerProps.horizontal
                let canV, canH = v <> ScrollBarMode.Disabled, h <> ScrollBarMode.Disabled
                let page = max step (s.ViewH - step)
                let plain = e.Modifiers &&& (Modifiers.Alt ||| Modifiers.Meta) = Modifiers.None
                let moved =
                    plain
                    && match e.Key with
                       | Key.Up when canV -> scrollBy el s 0.0 -step (ValueSome wheelTime)
                       | Key.Down when canV -> scrollBy el s 0.0 step (ValueSome wheelTime)
                       | Key.PageUp when canV -> scrollBy el s 0.0 -page (ValueSome wheelTime)
                       | Key.PageDown when canV -> scrollBy el s 0.0 page (ValueSome wheelTime)
                       | Key.Home when canV -> scrollBy el s 0.0 -(targetY s) (ValueSome scrollTime)
                       | Key.End when canV -> scrollBy el s 0.0 (s.MaxY - targetY s) (ValueSome scrollTime)
                       | Key.Left when canH -> scrollBy el s -step 0.0 (ValueSome wheelTime)
                       | Key.Right when canH -> scrollBy el s step 0.0 (ValueSome wheelTime)
                       | _ -> false
                if moved then e.Handle())
            Element.pointerDown (fun ctx e ->
                let el = ctx.Element
                let s = stateOf el
                // A touch stops a fling (and still presses what's under it).
                if s.Flinging then s.Flinging <- false
                let p = e.LocalPosition
                match barAt el s p with
                | 0 -> ()
                | axis ->
                    let along, offset = (if axis = 1 then p.Y, s.Y else p.X, s.X)
                    let trackLength = (if axis = 1 then s.ViewH else s.ViewW) - 2.0 * inset
                    let extent, viewport = (if axis = 1 then s.ExtentH, s.ViewH else s.ExtentW, s.ViewW)
                    match thumbSpan trackLength extent viewport offset with
                    | ValueSome(struct (length, start)) ->
                        let thumbStart = inset + start
                        if along >= thumbStart && along < thumbStart + length then
                            s.Drag <- axis
                            s.DragStart <- along
                            s.DragStartOffset <- offset
                            s.Animating <- false
                            s.Flinging <- false
                            el.InvalidateVisual()
                        else
                            // The track pages toward the click.
                            let page = max step (viewport - step)
                            let delta = if along < thumbStart then -page else page
                            (if axis = 1 then scrollBy el s 0.0 delta (ValueSome wheelTime) else scrollBy el s delta 0.0 (ValueSome wheelTime)) |> ignore
                        e.Handle()
                    | ValueNone -> ())
            Element.pointerMove (fun ctx e ->
                let el = ctx.Element
                let s = stateOf el
                let p = e.LocalPosition
                if s.Drag <> 0 then
                    let along = if s.Drag = 1 then p.Y else p.X
                    let next = s.DragStartOffset + (along - s.DragStart) * dragRatio s s.Drag
                    if s.Drag = 1 then moveTo el s s.X (Math.Clamp(next, 0.0, s.MaxY)) ValueNone
                    else moveTo el s (Math.Clamp(next, 0.0, s.MaxX)) s.Y ValueNone
                    e.Handle()
                elif e.PointerKind = PointerKind.Mouse then
                    let bar = barAt el s p
                    if bar <> s.HoverBar then
                        s.HoverBar <- bar
                        el.InvalidateVisual())
            Element.pointerUp (fun ctx _ ->
                let s = stateOf ctx.Element
                if s.Drag <> 0 then
                    s.Drag <- 0
                    ctx.Element.InvalidateVisual())
            Element.pan (fun ctx dx dy ->
                let el = ctx.Element
                let s = stateOf el
                let v, h = ctx.Get ScrollViewerProps.vertical, ctx.Get ScrollViewerProps.horizontal
                let canV = v <> ScrollBarMode.Disabled && s.ExtentH > s.ViewH
                let canH = h <> ScrollBarMode.Disabled && s.ExtentW > s.ViewW
                // Takes a drag along a direction it can scroll, even at an end (as phones do); a
                // drag the other way, or content that fits, stays a press (or goes to an outer one).
                let takes = (canV && abs dy >= abs dx) || (canH && abs dx > abs dy)
                if takes then
                    let samples = s.PanSamples
                    samples.Enqueue(struct (Stopwatch.GetTimestamp(), dx, dy))
                    while samples.Count > 8 do
                        samples.Dequeue() |> ignore
                    // The content follows the finger: a drag down shows what's above.
                    let x = if canH then Math.Clamp(s.X - dx, 0.0, s.MaxX) else s.X
                    let y = if canV then Math.Clamp(s.Y - dy, 0.0, s.MaxY) else s.Y
                    if x <> s.X || y <> s.Y then moveTo el s x y ValueNone
                takes)
            Element.panEnded (fun ctx canceled ->
                let el = ctx.Element
                let s = stateOf el
                let samples = s.PanSamples.ToArray()
                s.PanSamples.Clear()
                if not canceled && samples.Length >= 2 then
                    // The finger's speed over its last 100 ms of moves.
                    let now = Stopwatch.GetTimestamp()
                    let recent = samples |> Array.filter (fun struct (t, _, _) -> float (now - t) / float Stopwatch.Frequency < 0.1)
                    if recent.Length >= 2 then
                        let struct (first, _, _) = recent[0]
                        let seconds = max 0.016 (float (now - first) / float Stopwatch.Frequency)
                        let sx = recent |> Array.sumBy (fun struct (_, dx, _) -> dx)
                        let sy = recent |> Array.sumBy (fun struct (_, _, dy) -> dy)
                        let vx, vy = -sx / seconds, -sy / seconds
                        if abs vx + abs vy > 200.0 then
                            s.Animating <- false
                            s.Flinging <- true
                            s.FlingLast <- TimeSpan.MinValue
                            s.VelocityX <- (if ctx.Get ScrollViewerProps.horizontal = ScrollBarMode.Disabled then 0.0 else vx)
                            s.VelocityY <- (if ctx.Get ScrollViewerProps.vertical = ScrollBarMode.Disabled then 0.0 else vy)
                            el.InvalidateArrange())
            Element.scrolling
                { MakeVisible = makeVisible
                  ViewportChanged = ignore
                  ScrollToKey = fun _ _ _ _ -> false
                  HasViewport = true }
            // A press inside focuses it (for the keys), but Tab doesn't stop on it.
            Element.defaults [ Layoutable.clipToBounds true; Layoutable.focusable true; Layoutable.isTabStop false ]
        ]

    let create (settings: Setting<ScrollViewer> list) = View.Create(Type, settings)
    /// A style for ScrollViewers: `ScrollViewer.style [ Style.cls "c"; States.hover ] [ ScrollViewer.margin 4.0 ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<ScrollViewer> list) : Style = Style.forType Type parts settings

    /// The visible part of a ScrollViewer (its viewport) and its content's offset, for what's
    /// inside it: (offset x, offset y, viewport width, viewport height). Also its state.
    let internal viewportOf (e: Element) =
        let s = stateOf e
        struct (s.X, s.Y, s.ViewW, s.ViewH)

    /// Moves a ScrollViewer's content by `dy` without animating (an ItemsView keeping its first
    /// visible item in place after sizes were corrected above it).
    let internal shift (e: Element) (dy: float) =
        let s = stateOf e
        if dy <> 0.0 then
            s.Y <- s.Y + dy
            if s.Animating then
                s.FromY <- s.FromY + dy
                s.ToY <- s.ToY + dy
            s.NotifiedY <- s.Y
            e.InvalidateArrange()

    /// Jumps to vertical offset `y` (tests and tools).
    let internal jumpTo (e: Element) (y: float) =
        let s = stateOf e
        moveTo e s s.X y ValueNone

    /// What it shows of what it scrolls over, when it can scroll (semantics); ValueNone when its
    /// content fits.
    let internal scrollInfo (e: Element) : Scribble.Accessibility.SemanticScroll voption =
        let s = stateOf e
        let canV = e.Get ScrollViewerProps.vertical <> ScrollBarMode.Disabled && s.MaxY > 0.5
        let canH = e.Get ScrollViewerProps.horizontal <> ScrollBarMode.Disabled && s.MaxX > 0.5
        if canV || canH then
            ValueSome
                { OffsetX = s.X; OffsetY = s.Y
                  ExtentWidth = s.ExtentW; ExtentHeight = s.ExtentH
                  ViewportWidth = s.ViewW; ViewportHeight = s.ViewH }
        else ValueNone

    /// Scrolls a page on (vertically when it can, else sideways), as PageDown does; false at the end.
    let internal page (e: Element) (forward: bool) =
        let s = stateOf e
        let sign = if forward then 1.0 else -1.0
        if e.Get ScrollViewerProps.vertical <> ScrollBarMode.Disabled && s.MaxY > 0.5 then
            scrollBy e s 0.0 (sign * max step (s.ViewH - step)) (ValueSome wheelTime)
        elif e.Get ScrollViewerProps.horizontal <> ScrollBarMode.Disabled && s.MaxX > 0.5 then
            scrollBy e s (sign * max step (s.ViewW - step)) 0.0 (ValueSome wheelTime)
        else false
