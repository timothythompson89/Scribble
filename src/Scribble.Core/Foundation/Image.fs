namespace Scribble.Core

open System
open System.IO
open System.Net.Http
open System.Reflection
open System.Threading
open System.Threading.Tasks

/// How an image is filtered when drawn bigger than its pixels: `Smooth` (default) or `Pixelated` (nearest pixel
/// when enlarging, as CSS's `image-rendering: pixelated`; shrinking stays smooth).
type ImageScaling = Scribble.Rendering.ImageScaling

/// How an ImageView fits its image into its box (CSS's `object-fit`).
[<RequireQualifiedAccess>]
type ImageStretch =
    /// The image's own size, centred; cut off where it's bigger than the box.
    | None
    /// Fills the box, losing the image's shape.
    | Fill
    /// As big as fits in the box, keeping its shape (the default).
    | Uniform
    /// Covers the box, keeping its shape; what sticks out is cut off.
    | UniformToFill

/// Which way an ImageView may scale its image to fit.
[<RequireQualifiedAccess>]
type StretchDirection =
    /// Only smaller, never bigger than its own size (the default: a small logo stays small).
    | DownOnly
    /// Only bigger, never smaller.
    | UpOnly
    /// Either way.
    | Both

/// The HTTP side of `Image.fromUrl` (DESIGN.md, "M8 step 3b", (e), and "Image state per app, not per process"): one
/// `HttpClient` for each image cache, so for each app (in the browser .NET's runs on `fetch`), and the limits. Only
/// the page's address is the process's: a browser tab has one page whatever runs in it.
[<Sealed; AllowNullLiteral>]
type internal ImageHttp() =
    let sync = obj ()
    let mutable client: HttpClient = null
    let mutable handler: HttpMessageHandler = null
    let mutable timeout = TimeSpan.Zero

    /// Fields as in `Ids` (CLAUDE.md).
    [<DefaultValue>] static val mutable private baseUrl: Uri
    [<DefaultValue>] static val mutable private detached: ImageHttp

    /// At most this many downloads at once in one cache (the browser's own limit per server).
    static member MaxDownloads = 6
    /// A response bigger than this fails: decoding 8192 × 8192 needs no more.
    static member MaxBytes = 64L * 1024L * 1024L

    member _.Client =
        lock sync (fun () ->
            if isNull client then
                let c = if isNull handler then new HttpClient() else new HttpClient(handler, false)
                // Our own deadline (Timeout below) covers reading the body too.
                c.Timeout <- Timeout.InfiniteTimeSpan
                // Some servers refuse requests without one (Wikimedia: 403). Browsers send their own and forbid setting it.
                if not (OperatingSystem.IsBrowser()) then
                    c.DefaultRequestHeaders.UserAgent.Add(Headers.ProductInfoHeaderValue("Scribble", "1.0"))
                client <- c
            client)

    /// Tests answer requests themselves (a fake handler); null for the real network.
    member _.Handler
        with set (v: HttpMessageHandler) =
            lock sync (fun () ->
                handler <- v
                client <- null)

    /// How long a download may take, from its start to its last byte (30 s; real time: a network deadline, not
    /// something a frame waits on). Tests shorten it.
    member _.Timeout
        with get () = lock sync (fun () -> if timeout = TimeSpan.Zero then TimeSpan.FromSeconds 30.0 else timeout)
        and set (v: TimeSpan) = lock sync (fun () -> timeout <- v)

    /// The page's address in the browser (set by Scribble.Browser), for relative URLs; null on the desktop.
    static member BaseUrl
        with get () = lock typeof<ImageHttp> (fun () -> ImageHttp.baseUrl)
        and set (v: Uri) = lock typeof<ImageHttp> (fun () -> ImageHttp.baseUrl <- v)

    /// For a download with no app in reach (`Image.toPng` of a URL image, called from anywhere): the process's own,
    /// with nothing cached and no places to wait for.
    static member Detached =
        lock typeof<ImageHttp> (fun () ->
            if isNull ImageHttp.detached then ImageHttp.detached <- ImageHttp()
            ImageHttp.detached)

    /// The response's bytes, or why there are none.
    member this.Get(url: Uri, token: CancellationToken) : Task<Result<byte[], exn>> =
        task {
            try
                use request = new HttpRequestMessage(HttpMethod.Get, url)
                // ConfigureAwait(false): on the desktop the rest (and the decoding after it) stays off the UI thread
                // instead of coming back through its synchronization context.
                use! response = this.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false)
                if not response.IsSuccessStatusCode then
                    let code = int response.StatusCode
                    return
                        Error(HttpRequestException(string code + " " + response.ReasonPhrase, null, Nullable response.StatusCode) :> exn)
                else
                    let length = response.Content.Headers.ContentLength
                    if length.HasValue && length.Value > ImageHttp.MaxBytes then
                        return Error(InvalidDataException("The response is over 64 MB.") :> exn)
                    elif OperatingSystem.IsBrowser() then
                        // Reading the stream in chunks blocks inside .NET ("Cannot wait on monitors on this runtime" in
                        // single-threaded WebAssembly); fetch has the whole body anyway.
                        let! bytes = response.Content.ReadAsByteArrayAsync(token)
                        if bytes.LongLength > ImageHttp.MaxBytes then
                            return Error(InvalidDataException("The response is over 64 MB.") :> exn)
                        else return Ok bytes
                    else
                        use! stream = response.Content.ReadAsStreamAsync(token).ConfigureAwait(false)
                        use m = new MemoryStream()
                        let buffer = Array.zeroCreate<byte> 65536
                        let mutable reading = true
                        let mutable tooBig = false
                        while reading do
                            let! n = stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)
                            if n = 0 then reading <- false
                            else
                                m.Write(buffer, 0, n)
                                if m.Length > ImageHttp.MaxBytes then
                                    tooBig <- true
                                    reading <- false
                        if tooBig then return Error(InvalidDataException("The response is over 64 MB.") :> exn)
                        else return Ok(m.ToArray())
            with e ->
                return Error e
        }

[<RequireQualifiedAccess>]
type internal ImageSource =
    | Resource of assembly: Assembly * name: string
    | Bytes of bytes: byte[]
    | Pixels of width: int * height: int * rgba: byte[]
    /// A full path.
    | File of path: string
    /// An absolute http(s) URL, or one relative to the page (resolved when it loads, in the browser).
    | Url of url: string * fetch: (ImageHttp -> Uri -> CancellationToken -> Task<Result<byte[], exn>>)

/// An image: an immutable value naming a picture and how to read it (GLOSSARY.md). Two images with the same source
/// key are the same picture, decoded once however many places show it, at any density; the bytes behind a key are
/// never compared (DESIGN.md, "M8 step 3b", (d) 2). Make one with `Image.fromResource`, `Image.fromBytes` or
/// `Image.fromPixels`:
/// ```
/// let logo = Image.fromResource (typeof<Marker>.Assembly, "logo.png") [ Image.density 2.0 ]
/// ImageView.create [ ImageView.image logo ]
/// ```
[<Sealed>]
type Image internal (key: string, source: ImageSource, density: float) =
    /// What identifies the picture: "resource:Assembly/name", "bytes:key", "pixels:key", "file:full path" or
    /// "url:address".
    member _.Key = key

    /// How many of its pixels make one DIP (1 unless `Image.density` says otherwise).
    member _.Density = density

    member internal _.Source = source

    member internal _.WithDensity(value: float) = Image(key, source, value)

    override _.Equals(other: obj) =
        match other with
        | :? Image as i -> i.Key = key && i.Density = density
        | _ -> false

    override _.GetHashCode() = HashCode.Combine(key, density)

    /// The key, with the density when it isn't 1: "resource:Gallery/avatar.png@2x".
    override _.ToString() =
        if density = 1.0 then key else key + "@" + density.ToString(Globalization.CultureInfo.InvariantCulture) + "x"

module Image =
    /// How many of the image's pixels make one DIP: 2 for a picture made for a 2× screen (it then shows at half its
    /// pixel size, sharp on such a screen).
    let density (value: float) (image: Image) =
        if not (value > 0.0) || Double.IsInfinity value then invalidArg (nameof value) "Density must be above 0."
        image.WithDensity value

    let private apply (settings: (Image -> Image) list) (image: Image) =
        settings |> List.fold (fun i f -> f i) image

    /// An image embedded in an assembly (`<EmbeddedResource Include="..." LogicalName="name" />`). Its size is read
    /// from the file's header at once; it decodes off the UI thread when something first shows it.
    let fromResource (assembly: Assembly, name: string) (settings: (Image -> Image) list) : Image =
        if isNull assembly then nullArg (nameof assembly)
        if String.IsNullOrEmpty name then invalidArg (nameof name) "A resource name is needed."
        Image("resource:" + assembly.GetName().Name + "/" + name, ImageSource.Resource(assembly, name), 1.0)
        |> apply settings

    /// An image file's bytes (PNG), named by `key`: the same key is the same picture, and the bytes aren't compared,
    /// so making this in a view on every update is cheap.
    let fromBytes (key: string, bytes: byte[]) (settings: (Image -> Image) list) : Image =
        if String.IsNullOrEmpty key then invalidArg (nameof key) "A key is needed."
        if isNull bytes then nullArg (nameof bytes)
        Image("bytes:" + key, ImageSource.Bytes bytes, 1.0) |> apply settings

    /// An image from straight (not premultiplied) RGBA pixels, `width × height × 4` bytes, row by row from the top,
    /// named by `key` as for `fromBytes`. Ready as soon as something shows it (no decoding).
    let fromPixels (key: string, width: int, height: int, rgba: byte[]) (settings: (Image -> Image) list) : Image =
        if String.IsNullOrEmpty key then invalidArg (nameof key) "A key is needed."
        if isNull rgba then nullArg (nameof rgba)
        if width < 1 || height < 1 || int64 width * int64 height * 4L <> int64 rgba.Length then
            invalidArg (nameof rgba) "Pixels must be width × height × 4 bytes (RGBA), at least 1 × 1."
        Image("pixels:" + key, ImageSource.Pixels(width, height, rgba), 1.0) |> apply settings

    /// An image file (PNG). A relative path is relative to the app's folder (where its exe is), not the current
    /// directory. Read and decoded off the UI thread when something first shows it; its size isn't known until then,
    /// so an ImageView measures 0 × 0 until it loads (give it `width`/`height` to keep the layout still). A changed
    /// file isn't noticed while the picture is cached. Not in the browser (no file system): use `fromResource` or
    /// `fromUrl` there.
    let fromFile (path: string) (settings: (Image -> Image) list) : Image =
        if String.IsNullOrEmpty path then invalidArg (nameof path) "A path is needed."
        let full = IO.Path.GetFullPath(if IO.Path.IsPathRooted path then path else IO.Path.Combine(AppContext.BaseDirectory, path))
        Image("file:" + full, ImageSource.File full, 1.0) |> apply settings

    /// An image downloaded from `url` (http or https; a `file:` URL is read as `fromFile`). In the browser a relative
    /// URL ("images/logo.png") is resolved against the page; on the desktop it must be absolute. Downloaded when
    /// something first shows it (at most 6 at once; one nothing shows any more is cancelled) and decoded off the UI
    /// thread; its size isn't known until then. The same URL is the same picture: a change on the server isn't seen
    /// while it's cached (use a new URL, "?v=2"). No headers or sign-in: download such a picture yourself and use
    /// `fromBytes`. In the browser, a picture on another site must be sent with Access-Control-Allow-Origin.
    // Only an app that calls fromUrl links HttpClient (2.4 MB in a NativeAOT exe): the cache gets the download
    // function from here, not by naming HttpClient itself.
    let private download (http: ImageHttp) (url: Uri) (token: CancellationToken) = http.Get(url, token)

    let fromUrl (url: string) (settings: (Image -> Image) list) : Image =
        if String.IsNullOrEmpty url then invalidArg (nameof url) "A URL is needed."
        // "/images/a.png" is a path on the page's site, not a file (Uri reads it as file:/// on Unix).
        let absolute =
            if url.StartsWith "/" then ValueNone
            else
                match Uri.TryCreate(url, UriKind.Absolute) with
                | true, u -> ValueSome u
                | _ -> ValueNone
        match absolute with
        | ValueSome u when u.IsFile -> fromFile u.LocalPath settings
        | ValueSome u -> Image("url:" + u.AbsoluteUri, ImageSource.Url(u.AbsoluteUri, download), 1.0) |> apply settings
        | ValueNone -> Image("url:" + url, ImageSource.Url(url, download), 1.0) |> apply settings

    /// The image as a PNG file's bytes (DESIGN.md, "Rendering to an image and saving it"): a rendered image or one
    /// from pixels is encoded (transparency kept); one from a PNG file, resource, bytes or URL gives that file's bytes
    /// as they are (the decoder reads only PNG, so they're already one), read or downloaded off the UI thread. Fails
    /// as the image would fail to load (a missing file, a download error, not a PNG).
    let toPng (image: Image) : Task<byte[]> =
        let checkedPng (bytes: byte[]) =
            if not (Scribble.Imaging.Png.isPng bytes) then
                raise (Scribble.Imaging.ImageFormatException("'" + image.Key + "' isn't a PNG file."))
            bytes
        match image.Source with
        | ImageSource.Pixels(w, h, rgba) ->
            try Task.FromResult(Scribble.Imaging.Png.encode (w, h, rgba)) with e -> Task.FromException<byte[]> e
        | ImageSource.Bytes bytes -> (try Task.FromResult(checkedPng (Array.copy bytes)) with e -> Task.FromException<byte[]> e)
        | ImageSource.Resource(assembly, name) ->
            Task.Run(fun () ->
                use stream = assembly.GetManifestResourceStream name
                if isNull stream then raise (FileNotFoundException("No resource named '" + name + "' in " + assembly.GetName().Name + "."))
                use m = new MemoryStream()
                stream.CopyTo m
                checkedPng (m.ToArray()))
        | ImageSource.File path ->
            if OperatingSystem.IsBrowser() then
                Task.FromException<byte[]>(PlatformNotSupportedException("There is no file system in the browser."))
            else Task.Run(fun () -> checkedPng (File.ReadAllBytes path))
        | ImageSource.Url(url, fetch) ->
            task {
                let resolved =
                    match Uri.TryCreate(url, UriKind.Absolute) with
                    | true, u when not (url.StartsWith "/") -> u
                    | _ ->
                        match ImageHttp.BaseUrl with
                        | null -> raise (ArgumentException("'" + url + "' is a relative URL, with no page to resolve it against."))
                        | b -> Uri(b, url)
                let http = ImageHttp.Detached
                use cts = new CancellationTokenSource(http.Timeout)
                let! result = fetch http resolved cts.Token
                return
                    match result with
                    | Ok bytes -> checkedPng bytes
                    | Error e -> raise e
            }
