namespace Scribble.Core

open System
open System.Collections.Generic
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Scribble.Imaging

/// Where an image is: loading (reading, downloading or decoding), ready, or failed (with why).
[<RequireQualifiedAccess>]
type internal ImageState =
    | Loading
    | Ready of DecodedImage
    | Failed of exn

/// One picture in the cache (DESIGN.md, "M8 step 3b", (d) and (e)): its state, its size as soon as it's known, how
/// many elements hold it, who to tell when loading ends, and its download. Its fields change under the cache's lock.
[<Sealed; AllowNullLiteral>]
type internal ImageEntry(key: string) =
    member _.Key = key
    member val State = ImageState.Loading with get, set
    /// Its size in pixels as the file gives it (before any reduction), once known.
    member val Info: ImageInfo voption = ValueNone with get, set
    member val Holders = 0 with get, set
    member val Waiters = ResizeArray<unit -> unit>() with get
    member val Reported = false with get, set
    /// Its place in the recently-used pool while nothing holds it.
    member val PoolNode: LinkedListNode<ImageEntry> = null with get, set
    /// A download: where from, its place in the queue while it waits, its cancellation while it runs, whether its
    /// bytes have all arrived (from then on it's decoded whatever happens), and whether it was dropped.
    member val Url: Uri = null with get, set
    member val Fetcher: Uri -> CancellationToken -> Task<Result<byte[], exn>> = Unchecked.defaultof<_> with get, set
    member val QueueNode: LinkedListNode<ImageEntry> = null with get, set
    member val Cancellation: CancellationTokenSource = null with get, set
    member val Downloaded = false with get, set
    member val Dropped = false with get, set
    /// The CPU bytes it counts in the pool: full size plus a third for mip levels.
    member this.Bytes =
        match this.State with
        | ImageState.Ready d -> int64 d.Width * int64 d.Height * 16L / 3L
        | _ -> 0L


/// Decoded pictures, process-wide and thread-safe (decision 9). Elements hold what they show (`Element.HoldImage`);
/// at zero holders a picture goes to a recently-used pool of `poolLimit` bytes, oldest out first, and leaving the
/// pool releases its decoded image (GPU devices then give back its atlas space). A failed picture leaves at once, so
/// it's tried again only when shown again after that; so does a download nothing holds any more, cancelled.
[<Sealed>]
type internal ImageCache(poolLimit: int64) =
    let entries = Dictionary<string, ImageEntry>(StringComparer.Ordinal)
    let pool = LinkedList<ImageEntry>()
    let mutable pooledBytes = 0L
    let decodes = Dictionary<string, int>(StringComparer.Ordinal)
    /// Downloads waiting for one of the `MaxDownloads` places, in the order asked.
    let queue = LinkedList<ImageEntry>()
    let mutable downloading = 0

    let sync = obj ()

    /// Decode inline on this thread (unit tests: deterministic with FakeTime, and per thread so tests running in
    /// parallel don't see each other's choice).
    [<ThreadStatic; DefaultValue>] static val mutable private inlineHere: bool

    static member internal DecodeInlineOnThisThread
        with get () = ImageCache.inlineHere
        and set (v: bool) = ImageCache.inlineHere <- v

    /// Runs decoding work: Task.Run (a later task on the same thread in single-threaded WebAssembly), or inline.
    member private _.RunDecode(work: unit -> unit) =
        if ImageCache.inlineHere then work () else Task.Run(work) |> ignore

    [<DefaultValue>] static val mutable private shared: ImageCache

    /// The process's cache (32 MB pool: decision 10). Fields as in `Ids` (CLAUDE.md).
    static member Shared =
        lock typeof<ImageCache> (fun () ->
            if isNull (box ImageCache.shared) then ImageCache.shared <- ImageCache(32L * 1024L * 1024L)
            ImageCache.shared)

    member _.PoolLimit = poolLimit
    member _.PooledBytes = lock sync (fun () -> pooledBytes)
    member _.Count = lock sync (fun () -> entries.Count)
    member _.Contains(key: string) = lock sync (fun () -> entries.ContainsKey key)
    /// Downloads running and waiting (tests).
    member _.Downloads = lock sync (fun () -> struct (downloading, queue.Count))

    /// How many times `key` was decoded (or made from pixels), for tests.
    member _.Decodes(key: string) =
        lock sync (fun () ->
            match decodes.TryGetValue key with
            | true, n -> n
            | _ -> 0)

    member private this.Evict() =
        while pooledBytes > poolLimit && pool.Count > 0 do
            let oldest = pool.First.Value
            pool.RemoveFirst()
            oldest.PoolNode <- null
            pooledBytes <- pooledBytes - oldest.Bytes
            entries.Remove oldest.Key |> ignore
            match oldest.State with
            | ImageState.Ready d -> d.Release()
            | _ -> ()

    /// After a holder let go or loading ended: an unheld entry is pooled (ready), dropped (failed), or, while its
    /// download hasn't finished, dropped with its download (returned, to be cancelled outside the lock). Under the
    /// lock.
    member private this.Settle(entry: ImageEntry) : CancellationTokenSource =
        if entry.Holders = 0 then
            match entry.State with
            | ImageState.Ready _ when isNull entry.PoolNode ->
                entry.PoolNode <- pool.AddLast entry
                pooledBytes <- pooledBytes + entry.Bytes
                this.Evict()
                null
            | ImageState.Failed _ ->
                entries.Remove entry.Key |> ignore
                null
            | ImageState.Loading when not (isNull entry.Url) && not entry.Downloaded && not entry.Dropped ->
                entry.Dropped <- true
                entry.Waiters.Clear()
                entries.Remove entry.Key |> ignore
                if not (isNull entry.QueueNode) then
                    queue.Remove entry.QueueNode
                    entry.QueueNode <- null
                entry.Cancellation
            | _ -> null
        else null

    member private this.Finish(entry: ImageEntry, state: ImageState) =
        let waiters =
            lock sync (fun () ->
                entry.State <- state
                let w = entry.Waiters.ToArray()
                entry.Waiters.Clear()
                this.Settle entry |> ignore
                w)
#if DEBUG
        match state with
        | ImageState.Ready d when d.Reduced ->
            DebugWarnings.Once(
                "image-reduced:" + entry.Key,
                $"The image '{entry.Key}' is {d.OriginalWidth} × {d.OriginalHeight} pixels, over {DecodedImage.MaxSize} on a side: it was halved to {d.Width} × {d.Height} to fit the GPU (it shows at the same size, a little softer). A smaller picture would save memory and time.")
        | _ -> ()
#endif
        for w in waiters do
            w ()

    member private this.Fail(entry: ImageEntry, e: exn) =
        this.Finish(entry, ImageState.Failed(ImageLoadException(entry.Key, e.Message, e)))

    member private this.CountDecode(key: string) =
        lock sync (fun () ->
            decodes[key] <-
                (match decodes.TryGetValue key with
                 | true, n -> n + 1
                 | _ -> 1))

    /// Reads the size from the header, then decodes (on this thread).
    member private this.DecodeNow(entry: ImageEntry, bytes: byte[]) =
        let state =
            try
                let info = ImageDecoder.readInfo bytes
                lock sync (fun () -> entry.Info <- ValueSome info)
                this.CountDecode entry.Key
                ImageState.Ready(ImageDecoder.decode bytes)
            with e ->
                ImageState.Failed(ImageLoadException(entry.Key, e.Message, e))
        this.Finish(entry, state)

    /// The size from the header at once (so the first layout is right), then decoding off the UI thread.
    member private this.FromMemory(entry: ImageEntry, bytes: byte[]) =
        match (try Ok(ImageDecoder.readInfo bytes) with e -> Error e) with
        | Ok info ->
            lock sync (fun () -> entry.Info <- ValueSome info)
            this.RunDecode(fun () -> this.DecodeNow(entry, bytes))
        | Error e -> this.Fail(entry, e)

    // --- Downloads (DESIGN.md, "M8 step 3b", (e) 4, 5) ----------------------------------------------------

    /// Starts waiting downloads while there are places.
    member private this.Pump() =
        let start = ResizeArray()
        lock sync (fun () ->
            while downloading < ImageHttp.MaxDownloads && queue.Count > 0 do
                let e = queue.First.Value
                queue.RemoveFirst()
                e.QueueNode <- null
                e.Cancellation <- new CancellationTokenSource()
                downloading <- downloading + 1
                start.Add e)
        for e in start do
            this.Fetch e |> ignore

    /// Downloads one entry (it holds a place), then decodes it, fails it, or forgets it if it was dropped meanwhile.
    member private this.Fetch(entry: ImageEntry) : Task =
        task {
            let cts = entry.Cancellation
            let timeout = ImageHttp.Timeout
            cts.CancelAfter timeout
            let! result = (entry.Fetcher entry.Url cts.Token).ConfigureAwait(false)
            let dropped =
                lock sync (fun () ->
                    downloading <- downloading - 1
                    entry.Cancellation <- null
                    if not entry.Dropped then
                        match result with
                        | Ok _ -> entry.Downloaded <- true
                        | Error _ -> ()
                    entry.Dropped)
            cts.Dispose()
            this.Pump()
            if not dropped then
                match result with
                | Ok bytes -> this.DecodeNow(entry, bytes)
                | Error(:? OperationCanceledException) ->
                    let seconds = timeout.TotalSeconds
                    this.Fail(entry, TimeoutException($"The download took longer than {seconds:F0} s."))
                | Error(:? HttpRequestException as e) when not e.StatusCode.HasValue ->
                    let hint =
                        if OperatingSystem.IsBrowser() then
                            " (if this URL is on another site, it must send Access-Control-Allow-Origin)"
                        else ""
                    this.Fail(entry, HttpRequestException(e.Message + hint, e))
                | Error e -> this.Fail(entry, e)
        }

    /// Queues a download for `entry`.
    member private this.Download(entry: ImageEntry, url: Uri, fetch) =
        lock sync (fun () ->
            entry.Url <- url
            entry.Fetcher <- fetch
            entry.QueueNode <- queue.AddLast entry)
        this.Pump()

    member private this.Start(entry: ImageEntry, image: Image) =
        let fail (e: exn) = this.Fail(entry, e)
        match image.Source with
        | ImageSource.Pixels(w, h, rgba) ->
            let px = Array.zeroCreate<byte> rgba.Length
            for i in 0..4 .. rgba.Length - 4 do
                let a = int rgba[i + 3]
                px[i] <- byte ((int rgba[i] * a + 127) / 255)
                px[i + 1] <- byte ((int rgba[i + 1] * a + 127) / 255)
                px[i + 2] <- byte ((int rgba[i + 2] * a + 127) / 255)
                px[i + 3] <- byte a
            this.CountDecode entry.Key
            lock sync (fun () -> entry.Info <- ValueSome { Width = w; Height = h })
            this.Finish(entry, ImageState.Ready(DecodedImage(w, h, px)))
        | ImageSource.Bytes bytes -> this.FromMemory(entry, bytes)
        | ImageSource.Resource(assembly, name) ->
            let bytes =
                try
                    use stream = assembly.GetManifestResourceStream name
                    if isNull stream then
                        let names = String.Join(", ", assembly.GetManifestResourceNames())
                        let owner = assembly.GetName().Name
                        Error(
                            FileNotFoundException(
                                $"No resource named '{name}' in {owner} (it has: {names}). Embed it with <EmbeddedResource Include=\"...\" LogicalName=\"{name}\" />."
                            )
                            :> exn
                        )
                    else
                        use m = new MemoryStream()
                        stream.CopyTo m
                        Ok(m.ToArray())
                with e ->
                    Error e
            match bytes with
            | Ok b -> this.FromMemory(entry, b)
            | Error e -> fail e
        | ImageSource.File path ->
            if OperatingSystem.IsBrowser() then
                fail (PlatformNotSupportedException("There is no file system in the browser: use Image.fromResource or Image.fromUrl."))
            else
                this.RunDecode(fun () ->
                    match (try Ok(File.ReadAllBytes path) with e -> Error e) with
                    | Ok bytes -> this.DecodeNow(entry, bytes)
                    | Error e -> fail e)
        | ImageSource.Url(url, fetch) ->
            let resolved =
                match Uri.TryCreate(url, UriKind.Absolute) with
                | true, u when not (url.StartsWith "/") -> Ok u
                | _ ->
                    match ImageHttp.BaseUrl with
                    | null ->
                        Error(
                            ArgumentException(
                                "'" + url + "' is a relative URL: outside the browser there's no page to resolve it against, so give an absolute http or https URL."
                            )
                            :> exn
                        )
                    | b -> Ok(Uri(b, url))
            match resolved with
            | Ok u when u.Scheme = Uri.UriSchemeHttp || u.Scheme = Uri.UriSchemeHttps -> this.Download(entry, u, fetch)
            | Ok u -> fail (ArgumentException("Only http and https URLs can be downloaded, not '" + u.Scheme + ":'."))
            | Error e -> fail e

    /// Holds `image` (one more holder), starting it if it's new: memory sources learn their size here, then decode
    /// through `RunDecode`; pixels are ready at once; files and URLs load off the UI thread.
    member this.Acquire(image: Image) : ImageEntry =
        let entry, isNew =
            lock sync (fun () ->
                match entries.TryGetValue image.Key with
                | true, e ->
                    e.Holders <- e.Holders + 1
                    if not (isNull e.PoolNode) then
                        pool.Remove e.PoolNode
                        e.PoolNode <- null
                        pooledBytes <- pooledBytes - e.Bytes
                    e, false
                | _ ->
                    let e = ImageEntry(image.Key, Holders = 1)
                    entries[image.Key] <- e
                    e, true)
        if isNew then this.Start(entry, image)
        entry

    /// One holder fewer. A download nothing holds any more is cancelled (outside the lock: cancelling may run its
    /// callbacks on this thread).
    member this.Release(entry: ImageEntry) =
        let cancel =
            lock sync (fun () ->
                entry.Holders <- entry.Holders - 1
                this.Settle entry)
        if not (isNull cancel) then
            try
                cancel.Cancel()
            with :? ObjectDisposedException ->
                ()

    /// Calls `waiter` when loading ends, or returns false when it already has.
    member _.AddWaiter(entry: ImageEntry, waiter: unit -> unit) =
        lock sync (fun () ->
            match entry.State with
            | ImageState.Loading ->
                entry.Waiters.Add waiter
                true
            | _ -> false)

    /// True for the one caller that reports this entry's failure (decision 7: once per image).
    member _.ClaimReport(entry: ImageEntry) =
        lock sync (fun () ->
            if entry.Reported then false
            else
                entry.Reported <- true
                true)

    member _.StateOf(entry: ImageEntry) = lock sync (fun () -> entry.State)
    member _.InfoOf(entry: ImageEntry) = lock sync (fun () -> entry.Info)

/// An image couldn't be loaded: which one, and why (the decoder's message, a missing resource or file, an HTTP
/// status, a timeout).
and ImageLoadException(key: string, message: string, inner: exn) =
    inherit Exception("Image '" + key + "' couldn't be loaded: " + message, inner)
    /// The image's key (`Image.Key`).
    member _.Key = key

/// An element's hold on one picture (`Element.HoldImage`): asked for since the last render, and whether it waits
/// for loading to end. UI thread only.
[<Sealed; AllowNullLiteral>]
type internal ImageHold(entry: ImageEntry) =
    member _.Entry = entry
    member val Touched = true with get, set
    member val Waiting = false with get, set
