namespace Scribble.Core

open System
open System.Collections.Generic
open System.Diagnostics
open Scribble.Rendering

/// How an ItemsView lays out its items. Variable heights by default; the fixed kinds are laid out
/// by arithmetic (exact scrollbar from the start).
[<RequireQualifiedAccess>]
type ItemsLayout =
    internal
    | Stack
    | FixedStack of itemHeight: float
    | Wrap of cellWidth: float * cellHeight: float
    | Grid of columns: int

module ItemsLayout =
    /// One item per row, each as tall as it wants (measured as it's realized; unmeasured ones are
    /// estimated from the average, so the scrollbar may adjust a little while scrolling).
    let stack = ItemsLayout.Stack
    /// One item per row, all `itemHeight` DIPs tall: exact and fastest.
    let fixedStack (itemHeight: float) = ItemsLayout.FixedStack itemHeight
    /// Cells of `width` × `height` DIPs, left to right, as many per row as fit.
    let wrap (width: float) (height: float) = ItemsLayout.Wrap(width, height)
    /// `columns` cells per row sharing the width; every cell as tall as the first item.
    let grid (columns: int) = ItemsLayout.Grid(max 1 columns)

/// An ItemsView's items: any indexable collection, compared by reference (a new array is a change).
[<Sealed>]
type internal ItemsSource(original: obj, count: unit -> int, item: int -> obj) =
    member _.Original = original
    member _.Count = count ()
    member _.Item(i: int) = item i
    override _.Equals(other) =
        match other with
        | :? ItemsSource as o -> obj.ReferenceEquals(original, o.Original)
        | _ -> false
    override _.GetHashCode() = Runtime.CompilerServices.RuntimeHelpers.GetHashCode original
    static member Empty = ItemsSource(obj (), (fun () -> 0), (fun _ -> null))

/// A function from an item to something, stored untyped.
[<Sealed>]
type internal ItemsFunc<'R>(f: obj -> 'R) =
    member _.Invoke(item: obj) = f item

/// Sums over item indices (heights and counts of measured items) in O(log n).
[<Sealed>]
type internal SizeSums(n: int) =
    let sums: float[] = Array.zeroCreate (n + 1)
    let counts: int[] = Array.zeroCreate (n + 1)
    member _.Length = n
    member _.Add(i: int, height: float, count: int) =
        let mutable j = i + 1
        while j <= n do
            sums[j] <- sums[j] + height
            counts[j] <- counts[j] + count
            j <- j + (j &&& -j)
    /// Height and count of measured items in [0, i).
    member _.Prefix(i: int) =
        let mutable j = min i n
        let mutable s, c = 0.0, 0
        while j > 0 do
            s <- s + sums[j]
            c <- c + counts[j]
            j <- j - (j &&& -j)
        struct (s, c)

/// A realized item: its element, index, and the view version it was last patched to.
[<Sealed; AllowNullLiteral>]
type internal RealizedItem(element: Element) =
    member _.Element = element
    member val Index = 0 with get, set
    member val Version = 0 with get, set

/// An ItemsView's state, kept on the element.
[<Sealed; AllowNullLiteral>]
type internal ItemsState() =
    member val Source = ItemsSource.Empty with get, set
    member val Keys: obj[] = [||] with get, set
    member val KeyIndex: Dictionary<obj, int> = null with get, set
    /// Measured heights by key (stack), the width they were measured at, and their sums by index.
    member val Heights = Dictionary<obj, float>() with get
    member val MeasuredWidth = nan with get, set
    member val Sums = SizeSums 0 with get, set
    member val SumMeasured = 0.0 with get, set
    member val CountMeasured = 0 with get, set
    /// The estimate before anything is measured (kept from earlier measures across a width change).
    member val Fallback = 40.0 with get, set
    /// Grid: the cells' height (from the first item), and the width it was measured at.
    member val CellHeight = nan with get, set
    member val Realized = Dictionary<obj, RealizedItem>() with get
    member val Pool = Dictionary<ElementType, Stack<Element>>(HashIdentity.Reference) with get
    member val Version = 1 with get, set
    member val LastView: obj = null with get, set
    /// The visible part (own coordinates) when last looked: ValueNone means unlimited.
    member val Viewport: Rect voption = ValueNone with get, set
    member val HasViewport = false with get, set
    /// The realized range's extent (own coordinates), to tell when the viewport leaves it.
    member val RealizedTop = 0.0 with get, set
    member val RealizedBottom = 0.0 with get, set
    member val First = 0 with get, set
    member val Last = -1 with get, set
    member val Width = 0.0 with get, set
    member val Own = Scroller() with get
    member val Bound: Scroller voption = ValueNone with get, set

module ItemsViewProps =
    let internal source = Prop.define "Source" ItemsSource.Empty Affects.Measure
    let internal key = Prop.define "Key function" (ItemsFunc<obj>(fun item -> item)) Affects.Nothing
    let internal view = Prop.define "View function" (ItemsFunc<View voption>(fun _ -> ValueNone)) Affects.Measure
    let layout = Prop.define "Layout" ItemsLayout.Stack Affects.Measure
    let scroller = Prop.define "Scroller" (ValueNone: Scroller voption) Affects.Measure
    let internal state = Prop.local "ItemsState" (null: ItemsState)

/// Shows many items, realizing only the ones the ScrollViewers around it show (plus a little more),
/// so a list of 10,000 costs about what a list of 40 does. Items are data: `source` (an array, a
/// list, or any `IReadOnlyList`), `key` (identifies an item across updates), `view` (called only
/// for realized items), and `layout`. Put it inside a ScrollViewer (anywhere inside: headers above
/// it scroll with it); outside any, it realizes everything. Items scrolled out are recycled, and a
/// component inside one loses its state (keep what must survive in the model, by key); the item
/// with focus stays realized. Scroll from code with `scroller`.
///
///     ItemsView.create [
///         ItemsView.source model.Messages
///         ItemsView.key (fun (m: Message) -> m.Id)
///         ItemsView.view (fun (m: Message) -> TextBlock.create [ TextBlock.text m.Text ])
///     ]
[<AbstractClass>]
type ItemsView =
    inherit Layoutable
    /// The items: an array or any `IReadOnlyList` (`ResizeArray`) is used as it is; an F# list or
    /// other sequence is copied into an array once, when it changes. Compared by reference: give a
    /// new collection (an updated model's) to change the items.
    static member source<'k, 'T when 'k :> ItemsView>(items: seq<'T>) : Setting<'k> =
        let source =
            match items with
            | :? ('T array) as a -> ItemsSource(a, (fun () -> a.Length), (fun i -> box a[i]))
            | :? ('T list) as l ->
                let array = lazy (List.toArray l)
                ItemsSource(l, (fun () -> array.Value.Length), (fun i -> box array.Value[i]))
            | :? IReadOnlyList<'T> as l -> ItemsSource(l, (fun () -> l.Count), (fun i -> box l[i]))
            | other ->
                let array = lazy (Seq.toArray other)
                ItemsSource(other, (fun () -> array.Value.Length), (fun i -> box array.Value[i]))
        Setting.create ItemsViewProps.source source
    /// What identifies an item across updates (default: the item itself).
    static member key<'k, 'T, 'K when 'k :> ItemsView>(f: 'T -> 'K) : Setting<'k> =
        Setting.create ItemsViewProps.key (ItemsFunc<obj>(fun item -> box (f (unbox<'T> item))))
    /// The view for one item; called only for items being realized (or when the items change).
    static member view<'k, 'T when 'k :> ItemsView>(f: 'T -> View) : Setting<'k> =
        Setting.create ItemsViewProps.view (ItemsFunc<View voption>(fun item -> ValueSome(f (unbox<'T> item))))
    /// `ItemsLayout.stack` (default), `fixedStack height`, `wrap width height` or `grid columns`.
    static member layout<'k when 'k :> ItemsView>(layout: ItemsLayout) : Setting<'k> = Setting.create ItemsViewProps.layout layout
    /// A scroller to scroll to items from code (`ctx.useScroller ()`, then `scroller.ScrollTo key`).
    static member scroller<'k when 'k :> ItemsView>(scroller: Scroller) : Setting<'k> = Setting.create ItemsViewProps.scroller (ValueSome scroller)

module ItemsView =
    let private stateOf (e: Element) =
        match e.Get ItemsViewProps.state with
        | null ->
            let s = ItemsState()
            e.SetLocal(ItemsViewProps.state, s)
            s
        | s -> s

    let private isScrollViewer (e: Element) = obj.ReferenceEquals(e.Type, ScrollViewer.Type)

    /// The part of `e` the ScrollViewers around it show (its own coordinates); ValueNone: unlimited.
    let private viewportOf (e: Element) : Rect voption =
        let mutable result = ValueNone
        let mutable ox, oy = 0.0, 0.0
        let mutable cur = e
        while cur.Parent.IsSome do
            let b = cur.Bounds
            ox <- ox + b.X
            oy <- oy + b.Y
            let a = cur.Parent.Value
            if isScrollViewer a then
                let struct (_, _, vw, vh) = ScrollViewer.viewportOf a
                let visible = Rect.Create(-ox, -oy, vw, vh)
                result <-
                    match result with
                    | ValueSome (r: Rect) -> ValueSome(r.Intersect visible)
                    | ValueNone -> ValueSome visible
            cur <- a
        result

    let rec private nearestScrollViewer (e: Element) =
        match e.Parent with
        | ValueSome p when isScrollViewer p -> ValueSome p
        | ValueSome p -> nearestScrollViewer p
        | ValueNone -> ValueNone

    let rec private containsFocus (e: Element) =
        e.IsFocused || (let mutable found = false in (for c in e.Children do if not found && containsFocus c then found <- true); found)

    let rec private hasComponent (e: Element) =
        e.ComponentState.IsSome || (let mutable found = false in (for c in e.Children do if not found && hasComponent c then found <- true); found)

    // --- Positions ---------------------------------------------------------------------------

    let private average (s: ItemsState) = if s.CountMeasured > 0 then s.SumMeasured / float s.CountMeasured else s.Fallback

    /// The top of item `i` in a variable stack (measured heights exactly, others by the average).
    let private stackTop (s: ItemsState) (i: int) =
        let struct (sum, count) = s.Sums.Prefix i
        sum + float (i - count) * average s

    /// The item at height `y` in a variable stack (the last one starting at or above it).
    let private stackIndexAt (s: ItemsState) (n: int) (y: float) =
        if n = 0 then 0
        else
            let mutable lo, hi = 0, n - 1
            while lo < hi do
                let mid = (lo + hi + 1) / 2
                if stackTop s mid <= y then lo <- mid else hi <- mid - 1
            lo

    let private heightOf (s: ItemsState) (i: int) =
        match s.Heights.TryGetValue s.Keys[i] with
        | true, h -> h
        | _ -> average s

    let private record (s: ItemsState) (i: int) (h: float) =
        let key = s.Keys[i]
        match s.Heights.TryGetValue key with
        | true, old ->
            if old <> h then
                s.Heights[key] <- h
                s.Sums.Add(i, h - old, 0)
                s.SumMeasured <- s.SumMeasured + h - old
        | _ ->
            s.Heights[key] <- h
            s.Sums.Add(i, h, 1)
            s.SumMeasured <- s.SumMeasured + h
            s.CountMeasured <- s.CountMeasured + 1

    /// Cells per row and cell size for the fixed layouts; ValueNone for a variable stack.
    let private cells (layout: ItemsLayout) (s: ItemsState) (width: float) =
        match layout with
        | ItemsLayout.Stack -> ValueNone
        | ItemsLayout.FixedStack h -> ValueSome(struct (1, width, h))
        | ItemsLayout.Wrap(w, h) ->
            let columns = if Double.IsInfinity width || w <= 0.0 then 1 else max 1 (int (Math.Floor(width / w)))
            ValueSome(struct (columns, w, h))
        | ItemsLayout.Grid columns ->
            let w = if Double.IsInfinity width then 100.0 else width / float columns
            ValueSome(struct (columns, w, (if Double.IsNaN s.CellHeight then s.Fallback else s.CellHeight)))

    /// Where item `i` goes (own coordinates).
    let private slotOf (layout: ItemsLayout) (s: ItemsState) (width: float) (i: int) =
        match cells layout s width with
        | ValueSome(struct (columns, w, h)) -> Rect.Create(float (i % columns) * w, float (i / columns) * h, w, h)
        | ValueNone -> Rect.Create(0.0, stackTop s i, width, heightOf s i)

    let private totalHeight (layout: ItemsLayout) (s: ItemsState) (width: float) (n: int) =
        match cells layout s width with
        | ValueSome(struct (columns, _, h)) -> float ((n + columns - 1) / columns) * h
        | ValueNone -> stackTop s n

    /// The first item whose row reaches below `y`.
    let private indexAt (layout: ItemsLayout) (s: ItemsState) (width: float) (n: int) (y: float) =
        match cells layout s width with
        | ValueSome(struct (columns, _, h)) -> Math.Clamp(int (Math.Floor(y / h)) * columns, 0, max 0 (n - 1))
        | ValueNone -> stackIndexAt s n y

    // --- Source changes ------------------------------------------------------------------------

    let private sourceChanged (e: Element) (s: ItemsState) (source: ItemsSource) =
        s.Source <- source
        let keyFn = e.Get ItemsViewProps.key
        let n = source.Count
        s.Keys <- Array.init n (fun i -> keyFn.Invoke(source.Item i))
        s.KeyIndex <- null
        s.Version <- s.Version + 1
        let sums = SizeSums n
        for i in 0 .. n - 1 do
            match s.Heights.TryGetValue s.Keys[i] with
            | true, h -> sums.Add(i, h, 1)
            | _ -> ()
        s.Sums <- sums

    let private widthChanged (s: ItemsState) (width: float) =
        s.Fallback <- average s
        s.Heights.Clear()
        s.SumMeasured <- 0.0
        s.CountMeasured <- 0
        s.Sums <- SizeSums s.Keys.Length
        s.CellHeight <- nan
        s.MeasuredWidth <- width

    let private indexOfKey (s: ItemsState) (key: obj) =
        if isNull s.KeyIndex then
            let d = Dictionary<obj, int>()
            for i in 0 .. s.Keys.Length - 1 do
                d.TryAdd(s.Keys[i], i) |> ignore
            s.KeyIndex <- d
        match s.KeyIndex.TryGetValue key with
        | true, i -> ValueSome i
        | _ -> ValueNone

    // --- Realizing ---------------------------------------------------------------------------

    let private release (s: ItemsState) (item: RealizedItem) =
        let el = item.Element
        el.SetParent ValueNone
        el.SetRoot ValueNone
        // Component state is dropped with the element (it would show in another item otherwise).
        if not (hasComponent el) then
            let stack =
                match s.Pool.TryGetValue el.Type with
                | true, st -> st
                | _ ->
                    let st = Stack<Element>()
                    s.Pool[el.Type] <- st
                    st
            if stack.Count < 64 then stack.Push el

    /// The realized item for index `i`, realized (from the pool when it can) or patched as needed.
    let private realize (e: Element) (s: ItemsState) (viewFn: ItemsFunc<View voption>) (i: int) : RealizedItem voption =
        let key = s.Keys[i]
        let attach (el: Element) =
            el.SetParent(ValueSome e)
            el.SetRoot e.Root
            el.InvalidateAll()
        match s.Realized.TryGetValue key with
        | true, item ->
            item.Index <- i
            if item.Version <> s.Version then
                match viewFn.Invoke(s.Source.Item i) with
                | ValueSome v when obj.ReferenceEquals(v.Type, item.Element.Type) -> item.Element.Patch v
                | _ -> ()
                item.Version <- s.Version
            ValueSome item
        | _ ->
            match viewFn.Invoke(s.Source.Item i) with
            | ValueNone -> ValueNone
            | ValueSome v ->
                let el =
                    match s.Pool.TryGetValue v.Type with
                    | true, stack when stack.Count > 0 ->
                        let el = stack.Pop()
                        attach el
                        el.Patch v
                        el
                    | _ ->
                        let el = Element.RealizingIn(e.Root, fun () -> Element.Realize v)
                        attach el
                        el
                let item = RealizedItem(el)
                item.Index <- i
                item.Version <- s.Version
                s.Realized[key] <- item
                ValueSome item

    /// Measures a realized item for its slot; returns its height.
    let private measureItem (layout: ItemsLayout) (s: ItemsState) (width: float) (item: RealizedItem) =
        let el = item.Element
        match cells layout s width with
        | ValueSome(struct (_, w, h)) ->
            el.Measure(Size.Create(w, (match layout with ItemsLayout.Grid _ when Double.IsNaN s.CellHeight -> Double.PositiveInfinity | _ -> h)))
            el.DesiredSize.Height
        | ValueNone ->
            el.Measure(Size.Create(width, Double.PositiveInfinity))
            let h = el.DesiredSize.Height
            record s item.Index h
            h

    let private scrollerOf (e: Element) (s: ItemsState) =
        let scroller =
            match e.Get ItemsViewProps.scroller with
            | ValueSome sc -> sc
            | ValueNone -> s.Own
        if not (match s.Bound with ValueSome b -> obj.ReferenceEquals(b, scroller) | ValueNone -> false) then
            s.Bound <- ValueSome scroller
            scroller.Bind(fun () -> e.InvalidateMeasure())
        scroller

    let private overscan (viewport: Rect) = max 100.0 (viewport.Height / 2.0)

    /// Realizes and measures the items between `top` and `bottom` (own coordinates); lets go of the rest.
    let private realizeRange (e: Element) (s: ItemsState) (layout: ItemsLayout) (width: float) (top: float) (bottom: float) =
        let n = s.Keys.Length
        let viewFn = e.Get ItemsViewProps.view
        let keep = HashSet<obj>()
        let mutable first = n
        let mutable last = -1
        if n > 0 then
            // The grid's row height comes from its first item.
            (match layout with
             | ItemsLayout.Grid _ when Double.IsNaN s.CellHeight ->
                 match realize e s viewFn 0 with
                 | ValueSome item ->
                     s.CellHeight <- measureItem layout s width item
                     keep.Add s.Keys[0] |> ignore
                 | ValueNone -> s.CellHeight <- s.Fallback
             | _ -> ())
            let mutable i = indexAt layout s width n top
            first <- i
            while i < n && (slotOf layout s width i).Y < bottom do
                match realize e s viewFn i with
                | ValueSome item ->
                    measureItem layout s width item |> ignore
                    keep.Add s.Keys[i] |> ignore
                | ValueNone -> ()
                i <- i + 1
            last <- i - 1
        // Let go of what's out of range, but never of the item holding focus.
        let gone = List<obj>()
        for KeyValue(key, item) in s.Realized do
            if not (keep.Contains key) then
                let stillThere = (indexOfKey s key).IsSome
                if stillThere && containsFocus item.Element then
                    match indexOfKey s key with
                    | ValueSome i ->
                        item.Index <- i
                        realize e s viewFn i |> ignore
                    | ValueNone -> ()
                else gone.Add key
        for key in gone do
            release s s.Realized[key]
            s.Realized.Remove key |> ignore
        s.First <- first
        s.Last <- last
        s.RealizedTop <- if last >= first then (slotOf layout s width first).Y else 0.0
        s.RealizedBottom <- if last >= first then (let r = slotOf layout s width last in r.Bottom) else 0.0
        e.SetManagedChildren(s.Realized.Values |> Seq.sortBy _.Index |> Seq.map _.Element |> Seq.toArray)

    /// Scrolls to item `i`: realizes it (at its estimated place) and asks the scrollers around to show it.
    let private jumpTo (e: Element) (s: ItemsState) (layout: ItemsLayout) (width: float) (i: int) (align: ScrollAlign) (animate: bool) (extra: float) =
        let viewFn = e.Get ItemsViewProps.view
        match realize e s viewFn i with
        | ValueSome item -> measureItem layout s width item |> ignore
        | ValueNone -> ()
        let slot = slotOf layout s width i
        let rect =
            if extra <> 0.0 then
                let height = match s.Viewport with ValueSome v -> v.Height | ValueNone -> slot.Height
                Rect.Create(slot.X, slot.Y + extra, slot.Width, height)
            else slot
        e.BringRectIntoView(rect, align, animate)
        struct (i, slot)

    let private applyRequests (e: Element) (s: ItemsState) (layout: ItemsLayout) (width: float) : struct (int * Rect) voption =
        let scroller = scrollerOf e s
        let mutable jumped = ValueNone
        if scroller.HasRequests then
            let n = s.Keys.Length
            for r in scroller.Take() do
                let byIndex (i: int) align animate extra =
                    if n > 0 then jumped <- ValueSome(jumpTo e s layout width (Math.Clamp(i, 0, n - 1)) align animate extra)
                match r with
                | ScrollRequest.ToKey(key, align, animate) ->
                    match indexOfKey s key with
                    | ValueSome i -> byIndex i align animate 0.0
                    | ValueNone -> Debug.WriteLine("ItemsView: no item with key " + string key)
                | ScrollRequest.ToIndex(i, align, animate) -> byIndex i align animate 0.0
                | ScrollRequest.ToPosition p ->
                    match indexOfKey s p.Key with
                    | ValueSome i -> byIndex i ScrollAlign.Start false p.Offset
                    | ValueNone -> ()
                | ScrollRequest.ToOffset(y, animate) ->
                    let height = match s.Viewport with ValueSome v -> v.Height | ValueNone -> 0.0
                    e.BringRectIntoView(Rect.Create(0.0, max 0.0 y, 1.0, height), ScrollAlign.Start, animate)
                | ScrollRequest.By(dy, animate) ->
                    match s.Viewport with
                    | ValueSome v -> e.BringRectIntoView(Rect.Create(0.0, max 0.0 (v.Y + dy), 1.0, v.Height), ScrollAlign.Start, animate)
                    | ValueNone -> ()
        jumped

    let private measure (ctx: MeasureContext) (available: Size) =
        let e = ctx.Element
        let s = stateOf e
        let layout = ctx.Get ItemsViewProps.layout
        let source = ctx.Get ItemsViewProps.source
        if not (source.Equals s.Source) then sourceChanged e s source
        let view = box (ctx.Get ItemsViewProps.view)
        if not (obj.ReferenceEquals(view, s.LastView)) then
            s.LastView <- view
            s.Version <- s.Version + 1
        let width = available.Width
        if width <> s.MeasuredWidth then widthChanged s width
        s.Width <- width
        let n = s.Keys.Length
        // What shows now; before the first arrange, the top of the nearest ScrollViewer's viewport.
        let viewport = viewportOf e
        s.Viewport <- viewport
        // Keep the first visible item where it is on screen while sizes above it get corrected.
        let anchor =
            match viewport, layout with
            | ValueSome v, ItemsLayout.Stack when s.Last >= s.First && n > 0 ->
                let i = Math.Clamp(stackIndexAt s n v.Y, 0, n - 1)
                ValueSome(struct (i, stackTop s i))
            | _ -> ValueNone
        let jumped = applyRequests e s layout width
        /// Item `i` moved from `before` (sizes above it were corrected): move the scroller with it,
        /// so it stays where it is on screen.
        let keep (v: Rect) (i: int) (before: float) =
            match layout with
            | ItemsLayout.Stack when i < n ->
                let delta = stackTop s i - before
                if abs delta > 0.01 then
                    match nearestScrollViewer e with
                    | ValueSome sv ->
                        ScrollViewer.shift sv delta
                        s.Viewport <- ValueSome(v.Offset(0.0, delta))
                    | ValueNone -> ()
            | _ -> ()
        match viewport with
        | ValueNone -> realizeRange e s layout width Double.NegativeInfinity Double.PositiveInfinity
        | ValueSome v ->
            let extra = overscan v
            match jumped with
            | ValueSome(struct (i, slot)) ->
                // Around the item scrolled to (never what's between), then it's the anchor.
                realizeRange e s layout width (slot.Y - v.Height - extra) (slot.Bottom + v.Height + extra)
                keep v i slot.Y
            | ValueNone ->
                realizeRange e s layout width (v.Y - extra) (v.Bottom + extra)
                match anchor with
                | ValueSome(struct (i, before)) -> keep v i before
                | ValueNone -> ()
        let w =
            if Double.IsInfinity width then
                let mutable m = 0.0
                for item in s.Realized.Values do
                    m <- max m item.Element.DesiredSize.Width
                m
            else width
        Size.Create(w, totalHeight layout s width n)

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let e = ctx.Element
        let s = stateOf e
        let layout = ctx.Get ItemsViewProps.layout
        let width = if Double.IsInfinity s.Width then size.Width else s.Width
        for item in s.Realized.Values do
            let slot = slotOf layout s width item.Index
            let slot = match layout with ItemsLayout.Stack -> Rect.Create(0.0, slot.Y, size.Width, slot.Height) | _ -> slot
            ctx.Arrange(item.Element, slot)
        let scroller = scrollerOf e s
        match e.Root with
        | ValueSome r when e.Name <> "" -> r.AppValues.RegisterScroller(e.Name, scroller)
        | _ -> ()

    /// After arrange or a scroll: measure again when what shows is outside what's realized (or far
    /// inside it, to let go of items); report the first visible item.
    let private viewportChanged (e: Element) =
        let s = stateOf e
        let n = s.Keys.Length
        let viewport = viewportOf e
        let layout = e.Get ItemsViewProps.layout
        match viewport with
        | ValueSome v when n > 0 ->
            let extra = overscan v
            let coversTop = s.First = 0 || s.RealizedTop <= v.Y
            let coversBottom = s.Last >= n - 1 || s.RealizedBottom >= v.Bottom
            let tooMuch = s.RealizedTop < v.Y - 3.0 * extra || s.RealizedBottom > v.Bottom + 3.0 * extra
            if not (coversTop && coversBottom) || tooMuch || s.Last < s.First then e.InvalidateMeasure()
            let width = if Double.IsInfinity s.Width then e.Bounds.Width else s.Width
            let i = indexAt layout s width n v.Y
            let slot = slotOf layout s width i
            let i, slot = if slot.Bottom <= v.Y && i + 1 < n then i + 1, slotOf layout s width (i + 1) else i, slot
            (scrollerOf e s).ReportFirstVisible(ValueSome { Key = s.Keys[i]; Offset = v.Y - slot.Y })
            match nearestScrollViewer e with
            | ValueSome sv ->
                let struct (x, y, _, _) = ScrollViewer.viewportOf sv
                (scrollerOf e s).Report(x, y)
            | ValueNone -> ()
        | ValueNone when s.HasViewport -> e.InvalidateMeasure()
        | _ -> ()
        s.HasViewport <- viewport.IsSome

    let private scrollToKey (e: Element) (key: obj) (align: ScrollAlign) (animate: bool) =
        let s = stateOf e
        match indexOfKey s key with
        | ValueSome _ ->
            (scrollerOf e s).ScrollTo(key, align, animate)
            true
        | ValueNone -> false

    let Type =
        Element.define "ItemsView" [
            Element.measure measure
            Element.arrange (fun ctx size ->
                arrange ctx size
                viewportChanged ctx.Element)
            Element.scrolling
                { MakeVisible = fun _ _ _ _ -> Point.Zero
                  ViewportChanged = viewportChanged
                  ScrollToKey = scrollToKey }
        ]

    let create (settings: Setting<ItemsView> list) = View.Create(Type, settings)
    /// A style for ItemsViews (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<ItemsView> list) : Style = Style.forType Type parts settings

    /// How many items an ItemsView has realized, and how many it has (for tools: the inspector shows
    /// "realized 40 of 4,304"). ValueNone for other elements.
    let realizedCount (e: Element) : struct (int * int) voption =
        if obj.ReferenceEquals(e.Type, Type) then
            let s = stateOf e
            ValueSome(struct (s.Realized.Count, s.Keys.Length))
        else ValueNone
