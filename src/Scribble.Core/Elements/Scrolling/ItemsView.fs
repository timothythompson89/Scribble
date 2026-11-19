namespace Scribble.Core

open System
open System.Collections.Generic
open System.Diagnostics
open Scribble.Rendering

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
    /// The keys, their index, and the measured heights (stack) with their sums.
    member val Store: KeyStore = TypedKeyStore<obj, obj>() :> KeyStore with get, set
    /// The width the heights were measured at.
    member val MeasuredWidth = nan with get, set
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
    /// The element's scale (estimates are kept on whole physical pixels).
    member val Scale = 1.0 with get, set

/// The scroll anchor: the first visible item (by key), how far the viewport's top is into it, its
/// height then, and the keys after it.
type internal Anchor = { Key: obj; Into: float; Height: float; Next: obj[] }

module ItemsViewProps =
    let internal source = Prop.define "Source" ItemsSource.Empty Affects.Measure |> AppState.mark
    let internal keySelector = Prop.define "Key selector" (TypedKeyFunc<obj, obj>(id) :> KeyFunc) Affects.Nothing |> AppState.mark
    let internal viewSelector =
        // Without one, an item shows `string item` (strings, numbers, enums). Not unions or records: their
        // ToString prints with %A, which NativeAOT can't run; give them a view selector.
        Prop.define "View selector" (ItemsFunc<View voption>(fun item -> ValueSome(TextBlock.create [ TextBlock.text (string item) ]))) Affects.Measure
        |> AppState.mark
    /// An item's text, when its view isn't text: what typeahead searches and a screen reader calls it.
    let internal textSelector: Prop<ItemsFunc<string> voption> = Prop.define "Text selector" ValueNone Affects.Nothing |> AppState.mark
    let layout = Prop.define "Layout" ItemsLayout.Stack Affects.Measure
    let scroller = Prop.define "Scroller" (ValueNone: Scroller voption) Affects.Measure |> AppState.mark
    let internal state = Prop.local "ItemsState" (null: ItemsState)
    /// A list's item containers (M8 step 4b): wraps each item's view (key, item, view) in its container.
    let internal container: Prop<(obj -> obj -> View -> View) voption> = Prop.define "Item container" ValueNone Affects.Measure

/// Shows many items, realizing only the ones the ScrollViewers around it show (plus a little more),
/// so a list of 10,000 costs about what a list of 40 does. Items are data: `source` (an array, a
/// list, or any `IReadOnlyList`), `keySelector` (identifies an item across updates), `viewSelector` (called only
/// for realized items), and `layout`. Put it inside a ScrollViewer (anywhere inside: headers above
/// it scroll with it); outside any, it realizes everything. Items scrolled out are recycled, and a
/// component inside one loses its state (keep what must survive in the model, by key); the item
/// with focus stays realized. Scroll from code with `scroller`.
///
///     ItemsView.create [
///         ItemsView.source model.Messages
///         ItemsView.keySelector (fun (m: Message) -> m.Id)
///         ItemsView.viewSelector (fun (m: Message) -> TextBlock.create [ TextBlock.text m.Text ])
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
            | :? ('T array) as a -> ItemsSource(a, (fun () -> a.Length), (fun i -> box a[i]), (fun () -> box a))
            | :? ('T list) as l ->
                let array = lazy (List.toArray l)
                ItemsSource(l, (fun () -> array.Value.Length), (fun i -> box array.Value[i]), (fun () -> box array.Value))
            | :? IReadOnlyList<'T> as l -> ItemsSource(l, (fun () -> l.Count), (fun i -> box l[i]), (fun () -> box l))
            | other ->
                let array = lazy (Seq.toArray other)
                ItemsSource(other, (fun () -> array.Value.Length), (fun i -> box array.Value[i]), (fun () -> box array.Value))
        Setting.create ItemsViewProps.source source
    /// How to get the key that identifies an item across updates (default: the item itself).
    static member keySelector<'k, 'T, 'K when 'k :> ItemsView and 'K: equality>(f: 'T -> 'K) : Setting<'k> =
        Setting.create ItemsViewProps.keySelector (TypedKeyFunc<'T, 'K>(f) :> KeyFunc)
    /// The view for one item; called only for items being realized (or when the items change).
    static member viewSelector<'k, 'T when 'k :> ItemsView>(f: 'T -> View) : Setting<'k> =
        Setting.create ItemsViewProps.viewSelector (ItemsFunc<View voption>(fun item -> ValueSome(f (unbox<'T> item))))
    /// The text for one item, shown as a TextBlock (and its name for screen readers):
    /// `viewSelector (fun (p: Person) -> p.Name)`. Without a view selector, an item shows `string item`
    /// (fine for strings, numbers and enums; a union's or record's ToString needs %A, which NativeAOT can't run).
    static member viewSelector<'k, 'T when 'k :> ItemsView>(f: 'T -> string) : Setting<'k> =
        Setting.create ItemsViewProps.viewSelector (ItemsFunc<View voption>((fun item -> ValueSome(ItemsView.textView (f (unbox<'T> item)))), Text = ValueSome(fun item -> f (unbox<'T> item))))
    static member internal textView(text: string) = TextBlock.create [ TextBlock.text text ]
    /// An item's text when a view draws it: `textSelector (fun (p: Person) -> p.Name)`. A list jumps to the item
    /// whose text starts with what is typed (ListBox, ComboBox), and a screen reader calls the item by it. Without
    /// it: the text form of `viewSelector`, else the item itself if it is a string, a number or an enum.
    static member textSelector<'k, 'T when 'k :> ItemsView>(f: 'T -> string) : Setting<'k> =
        Setting.create ItemsViewProps.textSelector (ValueSome(ItemsFunc<string>(fun item -> f (unbox<'T> item))))
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
            let a = cur.Parent.Value
            let b = cur.Bounds
            if isScrollViewer a then
                // A ScrollViewer's content sits at minus its offset: read the offset as it is now,
                // not where the content was last arranged (a scroll or an anchor correction may
                // have moved it since, within this layout pass).
                let struct (x, y, _, _) = ScrollViewer.viewportOf a
                ox <- ox - x
                oy <- oy - y
            else
                ox <- ox + b.X
                oy <- oy + b.Y
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

    /// The estimate for unmeasured items: the measured ones' average, on whole physical pixels (so
    /// estimated positions are too, and corrections never move what shows by a fraction).
    let private average (s: ItemsState) =
        if s.CountMeasured > 0 then
            let scale = if s.Scale > 0.0 then s.Scale else 1.0
            Math.Round(s.SumMeasured / float s.CountMeasured * scale, MidpointRounding.AwayFromZero) / scale
        else s.Fallback

    /// The top of item `i` in a variable stack (measured heights exactly, others by the average).
    let private stackTop (s: ItemsState) (i: int) =
        let struct (sum, count) = s.Store.Prefix i
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
        match s.Store.Height i with
        | ValueSome h -> h
        | ValueNone -> average s

    let private record (s: ItemsState) (i: int) (h: float) =
        match s.Store.Record(i, h) with
        | ValueSome old -> s.SumMeasured <- s.SumMeasured + h - old
        | ValueNone ->
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

    /// A new source: its keys go into the store in place (O(items) to read them, but nothing is
    /// rebuilt or boxed for what kept its key and position; DESIGN.md, "Recycling measured; styles
    /// on realization; new sources", decision 3).
    let private sourceChanged (e: Element) (s: ItemsState) (source: ItemsSource) =
        s.Source <- source
        s.Store <- s.Store.Update(source, e.Get ItemsViewProps.keySelector)
        s.Version <- s.Version + 1

    let private widthChanged (s: ItemsState) (width: float) =
        s.Fallback <- average s
        s.Store.ClearHeights()
        s.SumMeasured <- 0.0
        s.CountMeasured <- 0
        s.CellHeight <- nan
        s.MeasuredWidth <- width

    let private indexOfKey (s: ItemsState) (key: obj) = s.Store.IndexOf key

    // --- Realizing ---------------------------------------------------------------------------

    // Profiling (tools/Scribble.Bench): a timestamp when it's on, and the time since one.
    let inline private stamp () = if ItemsProfile.On then Stopwatch.GetTimestamp() else 0L
    let inline private since (t0: int64) = Stopwatch.GetTimestamp() - t0

    let private release (s: ItemsState) (item: RealizedItem) =
        let t0 = stamp ()
        let el = item.Element
        el.SetParent ValueNone
        el.SetRoot ValueNone
        // Component state is dropped with the element (it would show in another item otherwise).
        if ItemsProfile.On then
            ItemsProfile.Released <- ItemsProfile.Released + 1
        if not (hasComponent el) && not ItemsProfile.NoPool then
            let stack =
                match s.Pool.TryGetValue el.Type with
                | true, st -> st
                | _ ->
                    let st = Stack<Element>()
                    s.Pool[el.Type] <- st
                    st
            if stack.Count < 64 then stack.Push el
        if ItemsProfile.On then ItemsProfile.Release <- ItemsProfile.Release + since t0

    /// The realized item for index `i`, realized (from the pool when it can) or patched as needed.
    let private realize (e: Element) (s: ItemsState) (viewFn: ItemsFunc<View voption>) (i: int) : RealizedItem voption =
        let key = s.Store.KeyAt(i)
        let attach (el: Element) =
            let t0 = stamp ()
            el.SetParent(ValueSome e)
            el.SetRoot e.Root
            el.InvalidateAll()
            if ItemsProfile.On then ItemsProfile.Attach <- ItemsProfile.Attach + since t0
        // The app's `view` throwing is reported (App.onError) and gives no view: a new item is left
        // out, a realized one keeps its element. Layout goes on for the rest.
        let container = e.Get ItemsViewProps.container
        let viewOf (item: obj) =
            let t0 = stamp ()
            let v =
                try
                    match viewFn.Invoke item, container with
                    | ValueSome v, ValueSome wrap -> ValueSome(wrap key item v)
                    | v, _ -> v
                with ex ->
                    e.ReportError ex
                    ValueNone
            if ItemsProfile.On then ItemsProfile.View <- ItemsProfile.View + since t0
            v
        let patch (el: Element) (v: View) =
            let t0 = stamp ()
            el.Patch v
            if ItemsProfile.On then ItemsProfile.Patch <- ItemsProfile.Patch + since t0
        // Items are realized during layout, after this layout's restyle pass: match their styles
        // now, before they're measured (joining the tree asked for a full restyle; a patch may have
        // asked for more), so they measure and draw styled in this frame.
        let styleNow (el: Element) =
            match e.Root with
            | ValueSome(:? ElementTree as tree) ->
                let t0 = stamp ()
                tree.RunStylesWithin el
                if ItemsProfile.On then ItemsProfile.Restyle <- ItemsProfile.Restyle + since t0
            | _ -> ()
        match s.Realized.TryGetValue key with
        | true, item ->
            item.Index <- i
            if item.Version <> s.Version then
                match viewOf (s.Source.Item i) with
                | ValueSome v when obj.ReferenceEquals(v.Type, item.Element.Type) ->
                    if ItemsProfile.On then ItemsProfile.Repatched <- ItemsProfile.Repatched + 1
                    patch item.Element v
                    styleNow item.Element
                | _ -> ()
                item.Version <- s.Version
            ValueSome item
        | _ ->
            match viewOf (s.Source.Item i) with
            | ValueNone -> ValueNone
            | ValueSome v ->
                let el =
                    match s.Pool.TryGetValue v.Type with
                    | true, stack when stack.Count > 0 ->
                        let el = stack.Pop()
                        // Another item now: another thing to a screen reader.
                        el.SemanticGeneration <- el.SemanticGeneration + 1
                        if ItemsProfile.On then ItemsProfile.Reused <- ItemsProfile.Reused + 1
                        attach el
                        patch el v
                        el
                    | _ ->
                        let t0 = stamp ()
                        let el = Element.RealizingIn(e.Root, fun () -> Element.Realize v)
                        if ItemsProfile.On then
                            ItemsProfile.Create <- ItemsProfile.Create + since t0
                            ItemsProfile.Created <- ItemsProfile.Created + 1
                        attach el
                        el
                styleNow el
                let item = RealizedItem(el)
                item.Index <- i
                item.Version <- s.Version
                s.Realized[key] <- item
                ValueSome item

    /// Measures a realized item for its slot; returns its height.
    let private measureItem (layout: ItemsLayout) (s: ItemsState) (width: float) (item: RealizedItem) =
        let t0 = stamp ()
        let el = item.Element
        let h =
            match cells layout s width with
            | ValueSome(struct (_, w, h)) ->
                el.Measure(Size.Create(w, (match layout with ItemsLayout.Grid _ when Double.IsNaN s.CellHeight -> Double.PositiveInfinity | _ -> h)))
                if Double.IsInfinity width then s.Store.RecordWidth(item.Index, el.DesiredSize.Width)
                el.DesiredSize.Height
            | ValueNone ->
                el.Measure(Size.Create(width, Double.PositiveInfinity))
                let h = el.DesiredSize.Height
                if Double.IsInfinity width then s.Store.RecordWidth(item.Index, el.DesiredSize.Width)
                record s item.Index h
                h
        if ItemsProfile.On then ItemsProfile.Measure <- ItemsProfile.Measure + since t0
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
        let t0 = stamp ()
        let n = s.Store.Count
        let viewFn = e.Get ItemsViewProps.viewSelector
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
                     keep.Add(s.Store.KeyAt(0)) |> ignore
                 | ValueNone -> s.CellHeight <- s.Fallback
             | _ -> ())
            let mutable i = indexAt layout s width n top
            first <- i
            while i < n && (slotOf layout s width i).Y < bottom do
                match realize e s viewFn i with
                | ValueSome item ->
                    measureItem layout s width item |> ignore
                    keep.Add(s.Store.KeyAt(i)) |> ignore
                | ValueNone -> ()
                i <- i + 1
            last <- i - 1
            // Many items in one range means many of them are (nearly) zero high: hidden items belong out
            // of the source (decided in M8 step 1b). Debug builds only.
            if last - first + 1 > 1000 && not (Double.IsNegativeInfinity top) then
                Debug.WriteLine("ItemsView: " + string (last - first + 1) + " items realized at once (rows " + string first + " to " + string last + "). Items with isVisible false or no height are still realized; filter hidden items out of the source instead.")
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
        if ItemsProfile.On then ItemsProfile.Range <- ItemsProfile.Range + since t0

    /// Scrolls to item `i`: realizes it (at its estimated place) and asks the scrollers around to show it.
    /// A rect at `y`, `height` tall, where the viewport already is sideways (0 wide): a vertical
    /// scroll leaves a sideways scrolled stack where it is (and never sees an unlimited width).
    let private rowsAt (s: ItemsState) (y: float) (height: float) =
        let x = match s.Viewport with ValueSome v -> max 0.0 v.X | ValueNone -> 0.0
        Rect.Create(x, y, 0.0, height)

    let private jumpTo (e: Element) (s: ItemsState) (layout: ItemsLayout) (width: float) (i: int) (align: ScrollAlign) (animate: bool) (extra: float) =
        let viewFn = e.Get ItemsViewProps.viewSelector
        match realize e s viewFn i with
        | ValueSome item -> measureItem layout s width item |> ignore
        | ValueNone -> ()
        let slot = slotOf layout s width i
        let rect =
            if extra <> 0.0 then
                let height = match s.Viewport with ValueSome v -> v.Height | ValueNone -> slot.Height
                Rect.Create(slot.X, slot.Y + extra, slot.Width, height)
            else slot
        let rect = match layout with ItemsLayout.Stack | ItemsLayout.FixedStack _ -> rowsAt s rect.Y rect.Height | _ -> rect
        e.BringRectIntoView(rect, align, animate)
        struct (i, slot)

    /// The scroll anchor at viewport `v`: the item at its top, how far into it the top is, its
    /// height, and the keys after it (one of them takes over if the item is removed).
    let private anchorAt (s: ItemsState) (v: Rect) =
        let n = s.Store.Count
        if n = 0 || s.Last < s.First then ValueNone
        else
            let i = Math.Clamp(stackIndexAt s n v.Y, 0, n - 1)
            ValueSome
                { Key = s.Store.KeyAt(i)
                  Into = v.Y - stackTop s i
                  Height = heightOf s i
                  Next = Array.init (max 0 (min (n - 1) (i + 16) - i)) (fun k -> s.Store.KeyAt(i + 1 + k)) }

    /// The anchor's index now (by key), or the first of the keys after it that's still there.
    let private findAnchor (s: ItemsState) (a: Anchor) =
        match indexOfKey s a.Key with
        | ValueSome i -> ValueSome(struct (i, a))
        | ValueNone ->
            a.Next
            |> Array.tryPick (fun k -> match indexOfKey s k with ValueSome i -> Some i | ValueNone -> None)
            |> function
                | Some i -> ValueSome(struct (i, { a with Height = 0.0 }))
                | None -> ValueNone

    /// Applies the scroller's requests: the item jumped to and its slot (to-key, to-index), and
    /// whether any request scrolled.
    let private applyRequests (e: Element) (s: ItemsState) (layout: ItemsLayout) (width: float) : struct (struct (int * Rect) voption * bool) =
        let scroller = scrollerOf e s
        let mutable jumped = ValueNone
        let scrolled = scroller.HasRequests
        if scroller.HasRequests then
            let n = s.Store.Count
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
                    e.BringRectIntoView(rowsAt s (max 0.0 y) height, ScrollAlign.Start, animate)
                | ScrollRequest.By(dy, animate) ->
                    match s.Viewport with
                    | ValueSome v -> e.BringRectIntoView(rowsAt s (max 0.0 (v.Y + dy)) v.Height, ScrollAlign.Start, animate)
                    | ValueNone -> ()
        struct (jumped, scrolled)

    let private measure (ctx: MeasureContext) (available: Size) =
        let e = ctx.Element
        let s = stateOf e
        let layout = ctx.Get ItemsViewProps.layout
        let source = ctx.Get ItemsViewProps.source
        let view = box (ctx.Get ItemsViewProps.viewSelector)
        if not (obj.ReferenceEquals(view, s.LastView)) then
            s.LastView <- view
            s.Version <- s.Version + 1
        s.Scale <- e.Scale
        // What shows now; before the first arrange, the top of the nearest ScrollViewer's viewport.
        let viewport = viewportOf e
        // Unlimited width (a ScrollViewer scrolling sideways): a stack measures its items at their
        // natural width (M8 step 3a++); wrap and grid flow to the viewport's width instead.
        let width =
            match layout, viewport with
            | (ItemsLayout.Wrap _ | ItemsLayout.Grid _), ValueSome v when Double.IsInfinity available.Width -> v.Width
            | _ -> available.Width
        // The scroll anchor, taken before the items or the width change: the first visible item
        // keeps its place on screen while sizes or items above it change.
        let before = match viewport with ValueSome v when layout = ItemsLayout.Stack -> anchorAt s v | _ -> ValueNone
        if not (source.Equals s.Source) then sourceChanged e s source
        let rewrapped = width <> s.MeasuredWidth
        if rewrapped then widthChanged s width
        s.Width <- width
        let n = s.Store.Count
        s.Viewport <- viewport
        let struct (jumped, scrolled) = applyRequests e s layout width
        // A scroll request moved the view: the viewport is where it is now, and the anchor is what
        // shows there.
        let viewport, anchor =
            if scrolled then
                let v = viewportOf e
                v, (match v with ValueSome v when layout = ItemsLayout.Stack && jumped.IsNone -> anchorAt s v | _ -> ValueNone)
            else viewport, before
        s.Viewport <- viewport
        /// Moves the ScrollViewer so the viewport's top is at `y` (never above the content).
        let moveTo (v: Rect) (y: float) =
            let delta = max 0.0 y - v.Y
            if abs delta > 1e-9 then
                match nearestScrollViewer e with
                | ValueSome sv ->
                    ScrollViewer.shift sv delta
                    let moved = v.Offset(0.0, delta)
                    s.Viewport <- ValueSome moved
                    moved
                | ValueNone -> v
            else v
        match viewport with
        | ValueNone -> realizeRange e s layout width Double.NegativeInfinity Double.PositiveInfinity
        | ValueSome v ->
            let extra = overscan v
            match jumped with
            | ValueSome(struct (i, slot)) ->
                // Around the item scrolled to (never what's between), then it's the anchor.
                realizeRange e s layout width (slot.Y - v.Height - extra) (slot.Bottom + v.Height + extra)
                if layout = ItemsLayout.Stack && i < n then moveTo v (v.Y + stackTop s i - slot.Y) |> ignore
            | ValueNone ->
                match anchor |> ValueOption.bind (fun a -> findAnchor s a) with
                | ValueSome(struct (i, a)) ->
                    // Measure the anchor first: its real height decides where the view goes (after a
                    // width change every height is new, and one measured row can move the average).
                    (match realize e s (e.Get ItemsViewProps.viewSelector) i with
                     | ValueSome item -> measureItem layout s width item |> ignore
                     | ValueNone -> ())
                    // Where the anchor should be: the same distance into it (after a width change,
                    // the same fraction of it), from its top as now estimated, then as measured.
                    let target () =
                        let into =
                            if rewrapped && a.Height > 0.0 then a.Into / a.Height * heightOf s i
                            else a.Into
                        stackTop s i + into
                    let v = moveTo v (target ())
                    realizeRange e s layout width (v.Y - extra) (v.Bottom + extra)
                    moveTo v (target ()) |> ignore
                | ValueNone -> realizeRange e s layout width (v.Y - extra) (v.Bottom + extra)
        // Sideways: the widest item measured so far that's still in the source (the extent's width
        // doesn't shrink while scrolling; DESIGN.md, M8 step 3a++, decision 5).
        let w = if Double.IsInfinity width then s.Store.MaxWidth() else width
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
        let n = s.Store.Count
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
            (scrollerOf e s).ReportFirstVisible(ValueSome { Key = s.Store.KeyAt(i); Offset = v.Y - slot.Y })
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

    /// Where a realized item's element is among all the items (semantics: "3 of 10,000").
    let private itemPosition (e: Element) (child: Element) =
        let s = stateOf e
        let mutable found = ValueNone
        for item in s.Realized.Values do
            if obj.ReferenceEquals(item.Element, child) then
                found <- ValueSome({ Index = item.Index + 1; Count = s.Source.Count }: Scribble.Accessibility.SemanticPosition)
        found

    let Type =
        Element.define "ItemsView" [
            Element.defaults [
                Scribble.Accessibility.Semantics.role Scribble.Accessibility.Role.List
                Scribble.Accessibility.Semantics.itemPosition itemPosition
            ]
            Element.measure measure
            Element.arrange (fun ctx size ->
                arrange ctx size
                viewportChanged ctx.Element)
            Element.scrolling
                { MakeVisible = fun _ _ _ _ -> Point.Zero
                  ViewportChanged = viewportChanged
                  ScrollToKey = scrollToKey
                  HasViewport = false }
        ]

    let create (settings: Setting<ItemsView> list) = View.Create(Type, settings)
    /// A style for ItemsViews (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<ItemsView> list) : Style = Style.forType Type parts settings

    // --- For lists built on an ItemsView (ListBox): the items by index and key, the realized ones --------

    /// How many items there are (as of the last layout).
    let internal count (e: Element) = (stateOf e).Store.Count
    /// Item `i`'s key and the item.
    let internal keyAt (e: Element) (i: int) = (stateOf e).Store.KeyAt i
    let internal itemAt (e: Element) (i: int) = (stateOf e).Source.Item i
    /// An item's text (DESIGN.md, "9e", decision 66): `textSelector`'s, else the text a text `viewSelector` shows,
    /// else the item itself when it is plain (a string, a number, an enum; a record's or union's ToString needs
    /// %A, which NativeAOT can't run). Read from `e`'s settings, which a ComboBox has too.
    let internal itemText (e: Element) (item: obj) : string =
        try
            match e.Get ItemsViewProps.textSelector with
            | ValueSome f -> f.Invoke item
            | ValueNone ->
                match (e.Get ItemsViewProps.viewSelector).Text with
                | ValueSome f -> f item
                | ValueNone ->
                    match item with
                    | null -> ""
                    | :? string as s -> s
                    | :? IConvertible as c -> c.ToString Globalization.CultureInfo.CurrentCulture
                    | _ -> ""
        with ex ->
            e.ReportError ex
            ""
    /// Where the item with `key` is (ValueNone: not in the source).
    let internal indexOf (e: Element) (key: obj) = (stateOf e).Store.IndexOf key
    /// The realized items: key and element.
    let internal realized (e: Element) = [ for KeyValue(k, item) in (stateOf e).Realized -> struct (k, item.Element) ]
    /// The realized element for `key`, if any.
    let internal elementFor (e: Element) (key: obj) =
        match (stateOf e).Realized.TryGetValue key with
        | true, item -> ValueSome item.Element
        | _ -> ValueNone
    /// Scrolls item `i` into view (nearest edge, no animation), realizing it at the next layout.
    let internal reveal (e: Element) (i: int) =
        let s = stateOf e
        (scrollerOf e s).ScrollToIndex(i, ScrollAlign.Nearest, false)
        e.InvalidateMeasure()
    /// How many items fit in the viewport (PageUp/PageDown), from the average height; 1 without a viewport.
    let internal perPage (e: Element) =
        let s = stateOf e
        match viewportOf e with
        | ValueSome v -> max 1 (int (Math.Floor(v.Height / max 1.0 (average s))))
        | ValueNone -> 10
    /// Re-wraps the realized items (their containers ask for the selection again).
    let internal refresh (e: Element) =
        let s = stateOf e
        s.Version <- s.Version + 1
        e.InvalidateMeasure()

    /// How many items an ItemsView has realized, and how many it has (for tools: the inspector shows
    /// "realized 40 of 4,304"). ValueNone for other elements.
    let realizedCount (e: Element) : struct (int * int) voption =
        // An ItemsView or a type extending it (ListBox): anything that laid out items.
        if not (isNull (e.Get ItemsViewProps.state)) then
            let s = stateOf e
            ValueSome(struct (s.Realized.Count, s.Store.Count))
        else ValueNone
