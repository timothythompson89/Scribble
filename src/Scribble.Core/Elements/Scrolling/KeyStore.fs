namespace Scribble.Core

open System
open System.Collections.Generic
open System.Diagnostics
open Scribble.Rendering

/// An ItemsView's items: any indexable collection, compared by reference (a new array is a change).
[<Sealed>]
type internal ItemsSource(original: obj, count: unit -> int, item: int -> obj, indexable: unit -> obj) =
    member _.Original = original
    member _.Count = count ()
    member _.Item(i: int) = item i
    /// The items as an array or `IReadOnlyList` of their own type (for reading keys without boxing).
    member _.Indexable = indexable ()
    override _.Equals(other) =
        match other with
        | :? ItemsSource as o -> obj.ReferenceEquals(original, o.Original)
        | _ -> false
    override _.GetHashCode() = Runtime.CompilerServices.RuntimeHelpers.GetHashCode original
    static member Empty = ItemsSource(obj (), (fun () -> 0), (fun _ -> null), (fun () -> box Array.empty<obj>))

/// A function from an item to something, stored untyped.
[<Sealed>]
type internal ItemsFunc<'R>(f: obj -> 'R) =
    member _.Invoke(item: obj) = f item

/// Sums over item indices (heights and counts of measured items) in O(log n). Kept for its whole
/// capacity, so items appended within it keep what's there (a Fenwick tree's entries past the
/// length stay correct for when it grows).
[<Sealed>]
type internal SizeSums() =
    let mutable sums: float[] = Array.zeroCreate 1
    let mutable counts: int[] = Array.zeroCreate 1
    let mutable n = 0
    member _.Length = n
    member _.Capacity = sums.Length - 1
    member _.Add(i: int, height: float, count: int) =
        let cap = sums.Length - 1
        let mutable j = i + 1
        while j <= cap do
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
    /// A longer length within the capacity (appended items, all unmeasured so far): true if it fit.
    member _.Grow(length: int) =
        if length <= sums.Length - 1 then
            n <- length
            true
        else false
    /// Empty, `length` long (room for at least that many; doubles when it has to grow).
    member _.Reset(length: int) =
        if length > sums.Length - 1 then
            let cap = max length (2 * (sums.Length - 1))
            sums <- Array.zeroCreate (cap + 1)
            counts <- Array.zeroCreate (cap + 1)
        else
            Array.Clear sums
            Array.Clear counts
        n <- length

/// An items view's keys, read from the items with the `key` function and kept in their own type
/// (no boxing), with the index from key to position and the measured heights by key (and their
/// sums by position). A new source updates them in place: what kept its key and position isn't
/// touched, and appended items leave everything before them as it was.
[<AbstractClass>]
type internal KeyStore() =
    abstract Count: int
    /// Item `i`'s key as an object (boxed once, while it stays at `i`).
    abstract KeyAt: i: int -> obj
    abstract IndexOf: key: obj -> int voption
    abstract Height: i: int -> float voption
    /// Records item `i`'s measured height; returns the one it had (ValueNone: newly measured).
    abstract Record: i: int * height: float -> float voption
    /// Records item `i`'s natural width (measured at unlimited width, for a sideways scrolling stack).
    abstract RecordWidth: i: int * width: float -> unit
    /// The widest recorded width among the items in the source now (0 if none): the extent's width.
    abstract MaxWidth: unit -> float
    /// Forgets every measured height (the width changed).
    abstract ClearHeights: unit -> unit
    /// Height and count of measured items in [0, i).
    abstract Prefix: i: int -> struct (float * int)
    /// Takes a new source's keys; returns the store to use from now on (this one, or a new one if
    /// the key function's types changed).
    abstract Update: source: ItemsSource * keyFn: KeyFunc -> KeyStore

/// The `key` setting's function, typed; it makes the store that holds its keys.
and [<AbstractClass>] internal KeyFunc() =
    abstract CreateStore: unit -> KeyStore

and [<Sealed>] internal TypedKeyFunc<'T, 'K when 'K: equality>(f: 'T -> 'K) =
    inherit KeyFunc()
    member _.F = f
    override _.CreateStore() = TypedKeyStore<'T, 'K>() :> KeyStore

and [<Sealed>] internal TypedKeyStore<'T, 'K when 'K: equality>() =
    inherit KeyStore()
    let comparer = EqualityComparer<'K>.Default
    let mutable keys: 'K[] = Array.zeroCreate 0
    let mutable boxes: obj[] = Array.zeroCreate 0
    let mutable n = 0
    /// Built on the first lookup, then kept up to date.
    let mutable index: Dictionary<'K, int> = null
    let heights = Dictionary<'K, float>()
    let sums = SizeSums()
    /// Natural widths by key (kept for keys that leave, like heights, in case they come back).
    let widths = Dictionary<'K, float>()
    /// The widest of `widths` among the keys in the source; recomputed when `maxDirty`.
    let mutable maxWidth = 0.0
    let mutable maxDirty = false
    /// Keys that left a position in an update (removed or moved), reused between updates.
    let left = List<'K>()

    let buildIndex () =
        index <- Dictionary<'K, int>(n)
        for i in 0 .. n - 1 do
            index.TryAdd(keys[i], i) |> ignore

    /// Sums from scratch: the measured heights of the items there now.
    let rebuildSums () =
        sums.Reset n
        if heights.Count > 0 then
            for i in 0 .. n - 1 do
                match heights.TryGetValue keys[i] with
                | true, h -> sums.Add(i, h, 1)
                | _ -> ()

    /// Positions before this kept their keys (during `Take`).
    let mutable firstChanged = 0

    /// Item `i`'s key `k` in a new source that replaced `old` items.
    member private _.Put(i: int, k: 'K, old: int) =
        // EqualityComparer<'K>.Default called directly: the JIT turns it into a plain compare.
        if not (i < old && EqualityComparer<'K>.Default.Equals(keys[i], k)) then
            if i < old then
                left.Add keys[i]
                if i < firstChanged then firstChanged <- i
            keys[i] <- k
            boxes[i] <- null
            if not (isNull index) then index[k] <- i

    member private this.Take(source: ItemsSource, f: 'T -> 'K) =
        let count = source.Count
        if count > keys.Length then
            let cap = max count (2 * keys.Length)
            Array.Resize(&keys, cap)
            Array.Resize(&boxes, cap)
        let old = n
        firstChanged <- min old count
        left.Clear()
        // One loop per kind of collection (no call through a closure per item).
        match source.Indexable with
        | :? ('T[]) as a ->
            for i in 0 .. count - 1 do
                this.Put(i, f a[i], old)
        | :? IReadOnlyList<'T> as l ->
            for i in 0 .. count - 1 do
                this.Put(i, f l[i], old)
        | _ ->
            for i in 0 .. count - 1 do
                this.Put(i, f (unbox<'T> (source.Item i)), old)
        for i in count .. old - 1 do
            left.Add keys[i]
            keys[i] <- Unchecked.defaultof<'K>
            boxes[i] <- null
        // Keys left or arrived (the widest may have gone; one measured before may be back): find the
        // widest again when it's next asked for.
        if widths.Count > 0 && (left.Count > 0 || count > old) then maxDirty <- true
        n <- count
        // A key that left its position is gone from the index unless it's at another one now.
        if not (isNull index) then
            for k in left do
                match index.TryGetValue k with
                | true, i when i >= count || not (comparer.Equals(keys[i], k)) -> index.Remove k |> ignore
                | _ -> ()
        // Only appended (everything before kept its key and place): the sums stay; the new items
        // count only if they were measured before (under the same key).
        let appended = count >= old && firstChanged >= old
        if appended && sums.Grow count then
            if heights.Count > 0 then
                for i in old .. count - 1 do
                    match heights.TryGetValue keys[i] with
                    | true, h -> sums.Add(i, h, 1)
                    | _ -> ()
        else rebuildSums ()

    override _.Count = n
    override _.KeyAt(i) =
        match boxes[i] with
        | null ->
            let b = box keys[i]
            boxes[i] <- b
            b
        | b -> b
    override _.IndexOf(key: obj) =
        match key with
        | :? 'K as k ->
            if isNull index then buildIndex ()
            match index.TryGetValue k with
            | true, i -> ValueSome i
            | _ -> ValueNone
        | _ -> ValueNone
    override _.Height(i) =
        match heights.TryGetValue keys[i] with
        | true, h -> ValueSome h
        | _ -> ValueNone
    override _.Record(i, h) =
        let k = keys[i]
        match heights.TryGetValue k with
        | true, old ->
            if old <> h then
                heights[k] <- h
                sums.Add(i, h - old, 0)
            ValueSome old
        | _ ->
            heights[k] <- h
            sums.Add(i, h, 1)
            ValueNone
    override _.ClearHeights() =
        heights.Clear()
        widths.Clear()
        maxWidth <- 0.0
        maxDirty <- false
        sums.Reset n
    override _.RecordWidth(i, w) =
        let k = keys[i]
        let old = match widths.TryGetValue k with | true, o -> o | _ -> -1.0
        widths[k] <- w
        if not maxDirty then
            if w >= maxWidth then maxWidth <- w
            elif old >= maxWidth then maxDirty <- true // the widest got narrower
    override this.MaxWidth() =
        if maxDirty then
            // Over what was measured (bounded by what has been realized), not over the source.
            if isNull index then buildIndex ()
            let mutable m = 0.0
            for KeyValue(k, w) in widths do
                if w > m && index.ContainsKey k then m <- w
            maxWidth <- m
            maxDirty <- false
        maxWidth
    override _.Prefix(i) = sums.Prefix i
    override this.Update(source, keyFn) =
        match keyFn with
        | :? TypedKeyFunc<'T, 'K> as typed ->
            this.Take(source, typed.F)
            this :> KeyStore
        | _ -> keyFn.CreateStore().Update(source, keyFn)

