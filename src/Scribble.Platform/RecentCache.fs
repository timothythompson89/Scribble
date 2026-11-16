namespace Scribble.Platform

open System.Collections.Generic

/// Keeps what was made for the most recently used `capacity` keys and releases the rest, oldest
/// first, so things made on demand (image cursors: OS handles on Windows, data URLs in the
/// browser) can't pile up. The entry just used is never the one released, so whatever was asked
/// for last (the cursor showing) stays valid. For platforms; one thread.
[<Sealed>]
type RecentCache<'K, 'V when 'K: equality>(capacity: int, release: 'V -> unit) =
    let capacity = max 1 capacity
    let entries = Dictionary<'K, LinkedListNode<KeyValuePair<'K, 'V>>>()
    /// Most recently used first.
    let order = LinkedList<KeyValuePair<'K, 'V>>()

    member _.Count = entries.Count

    /// The value for `key`, made by `make` if it isn't kept; it becomes the most recent, and the
    /// least recent beyond `capacity` is released.
    member _.GetOrAdd(key: 'K, make: unit -> 'V) : 'V =
        match entries.TryGetValue key with
        | true, node ->
            order.Remove node
            order.AddFirst node
            node.Value.Value
        | _ ->
            let value = make ()
            entries[key] <- order.AddFirst(KeyValuePair(key, value))
            while entries.Count > capacity do
                let last = order.Last
                order.RemoveLast()
                entries.Remove last.Value.Key |> ignore
                release last.Value.Value
            value

    /// Releases everything (the window is closing).
    member _.Clear() =
        for kv in order do
            release kv.Value
        order.Clear()
        entries.Clear()
