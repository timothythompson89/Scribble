/// Serves the browser samples to the local network over HTTPS:
///
///   dotnet run --project tools/Scribble.Serve                 publish the samples, then serve them
///   dotnet run --project tools/Scribble.Serve -- --aot        publish with WebAssembly AOT (slow publish, fast app)
///   dotnet run --project tools/Scribble.Serve -- --no-build   serve what was published last
///   dotnet run --project tools/Scribble.Serve -- --self-signed   no certificate authority to install (warnings instead)
///
/// The hub is on port 5279 (plain HTTP, so a new device can open it before trusting the certificate): it links the
/// samples, says whether the device trusts the certificate and offers it; the terminal shows its QR code. Hello is on
/// port 5280, the Gallery on 5281, Icons on 5282, Counter on 5283 (HTTPS). Published files and certificates go to `artifacts/serve`
/// (git-ignored).
module Scribble.Serve.Program

open System
open System.Diagnostics
open System.Globalization
open System.IO
open System.Net
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.StaticFiles
open Microsoft.Extensions.Logging

type private Sample = { Name: string; Project: string; Port: int; Title: string; Description: string }

let private samples =
    [ { Name = "hello"; Project = "samples/Hello.Browser"; Port = 5280
        Title = "Hello"; Description = "The dashboard: animated cards, a toolbar, text and emoji" }
      { Name = "gallery"; Project = "samples/Gallery.Browser"; Port = 5281
        Title = "Gallery"; Description = "Components, layout, controls, scrolling, layers, styles and light/dark themes" }
      { Name = "icons"; Project = "samples/Icons.Browser"; Port = 5282
        Title = "Icons"; Description = "All 4,304 Material Symbols" }
      { Name = "counter"; Project = "samples/Counter.Browser"; Port = 5283
        Title = "Counter"; Description = "The smallest app: an Elmish counter" } ]

let private hubPort = 5279

/// The repository root: the folder holding Scribble.slnx, above the working directory or this tool.
let private repoRoot () =
    let rec up (dir: DirectoryInfo) =
        if isNull dir then None
        elif File.Exists(Path.Combine(dir.FullName, "Scribble.slnx")) then Some dir.FullName
        else up dir.Parent
    up (DirectoryInfo Environment.CurrentDirectory)
    |> Option.orElse (up (DirectoryInfo AppContext.BaseDirectory))
    |> Option.defaultWith (fun () -> failwith "Run this from inside the Scribble repository.")

/// Runs `dotnet publish` for one sample into `output`, streaming its output. False if it failed.
let private publish (root: string) (sample: Sample) (output: string) (aot: bool) =
    let args =
        [ "publish"; Path.Combine(root, sample.Project); "-c"; "Release"; "-o"; output; "--nologo"; "-v"; "minimal" ]
        @ (if aot then [ "-p:BrowserAot=true" ] else [])
    let info = ProcessStartInfo("dotnet", args)
    info.UseShellExecute <- false
    // Start from an empty folder: publishes add fingerprinted files and never remove the old ones.
    if Directory.Exists output then Directory.Delete(output, true)
    Console.WriteLine("Publishing " + sample.Name + (if aot then " (WebAssembly AOT: this takes a few minutes)" else "") + "...")
    use p = Process.Start info
    p.WaitForExit()
    p.ExitCode = 0

/// How the last publish was made, kept next to it (`build.txt`: "aot" or "interpreted", then the time).
let private writeBuildInfo (artifacts: string) (aot: bool) =
    File.WriteAllText(Path.Combine(artifacts, "build.txt"), (if aot then "aot" else "interpreted") + "\n" + DateTime.UtcNow.ToString("o"))

let private readBuildInfo (artifacts: string) : Hub.BuildInfo voption =
    try
        match File.ReadAllLines(Path.Combine(artifacts, "build.txt")) with
        | [| kind; time |] ->
            ValueSome { Aot = (kind = "aot"); PublishedUtc = DateTime.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) }
        | _ -> ValueNone
    with _ -> ValueNone

let private contentTypes =
    let provider = FileExtensionContentTypeProvider()
    provider.Mappings[".wasm"] <- "application/wasm"
    provider.Mappings[".dat"] <- "application/octet-stream"
    provider.Mappings[".blat"] <- "application/octet-stream"
    provider.Mappings[".webcil"] <- "application/octet-stream"
    provider.Mappings[".js"] <- "text/javascript"
    provider.Mappings[".mjs"] <- "text/javascript"
    provider

/// Serves files under `root` ("/" is index.html). Sends the pre-compressed `.br` or `.gz` copy the
/// publish made when the browser accepts it. Never cached, so a reload gets a new publish.
let private serveFiles (root: string) (context: HttpContext) : Task =
    task {
        let path = context.Request.Path.Value |> Option.ofObj |> Option.defaultValue "/"
        let relative = if path = "/" || path = "" then "index.html" else Uri.UnescapeDataString(path.TrimStart '/')
        let full = Path.GetFullPath(Path.Combine(root, relative))
        let inside = full.StartsWith(Path.GetFullPath root + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        if not inside || not (File.Exists full) then
            context.Response.StatusCode <- 404
        else
            let response = context.Response
            let contentType =
                match contentTypes.TryGetContentType full with
                | true, t -> t
                | _ -> "application/octet-stream"
            response.ContentType <- contentType
            response.Headers.CacheControl <- "no-cache"
            response.Headers.Vary <- "Accept-Encoding"
            let accepts (encoding: string) =
                context.Request.Headers.AcceptEncoding.ToString().Contains(encoding, StringComparison.OrdinalIgnoreCase)
            let file =
                if accepts "br" && File.Exists(full + ".br") then
                    response.Headers.ContentEncoding <- "br"
                    full + ".br"
                elif accepts "gzip" && File.Exists(full + ".gz") then
                    response.Headers.ContentEncoding <- "gzip"
                    full + ".gz"
                else full
            do! response.SendFileAsync file
    }

let private quietLogging (builder: WebApplicationBuilder) =
    builder.Logging.ClearProviders() |> ignore
    builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning) |> ignore

[<EntryPoint>]
let main argv =
    let has (flag: string) = Array.contains flag argv
    let aot, noBuild, selfSigned = has "--aot", has "--no-build", has "--self-signed"
    let root = repoRoot ()
    let artifacts = Path.Combine(root, "artifacts", "serve")
    let certFolder = Path.Combine(artifacts, "certificates")

    let published =
        samples
        |> List.forall (fun s ->
            let output = Path.Combine(artifacts, s.Name)
            if noBuild then
                let ok = File.Exists(Path.Combine(output, "wwwroot", "index.html"))
                if not ok then Console.WriteLine("Nothing published for " + s.Name + " yet: run without --no-build first.")
                ok
            else publish root s output aot)
    if not published then
        1
    else
        if not noBuild then writeBuildInfo artifacts aot
        let build = readBuildInfo artifacts
        let host = Dns.GetHostName()
        let addresses = Hub.addresses ()
        let certificate = Certificates.server certFolder selfSigned host (addresses |> List.map _.Ip)
        let caPem = if selfSigned then "" else Certificates.authorityPem certFolder
        let cards = [ for s in samples -> ({ Title = s.Title; Description = s.Description; Port = s.Port }: Hub.Card) ]

        let urls (port: int) (scheme: string) =
            [ for a in addresses -> scheme + "://" + a.Ip.ToString() + ":" + string port + "/" ]
            @ [ scheme + "://" + host + ".local:" + string port + "/" ]

        let apps =
            [ for s in samples do
                let builder = WebApplication.CreateSlimBuilder()
                quietLogging builder
                builder.WebHost.ConfigureKestrel(fun options ->
                    options.ListenAnyIP(s.Port, fun listen -> listen.UseHttps certificate |> ignore))
                |> ignore
                let app = builder.Build()
                let files = Path.Combine(artifacts, s.Name, "wwwroot")
                app.Run(RequestDelegate(serveFiles files))
                yield app
              // The hub: plain HTTP, so a device that doesn't trust the certificate yet can open it.
              let builder = WebApplication.CreateSlimBuilder()
              quietLogging builder
              builder.WebHost.ConfigureKestrel(fun options -> options.ListenAnyIP hubPort) |> ignore
              let app = builder.Build()
              // Throwaway test pages for a device (a spike): files in `artifacts/serve/spike` are served under
              // /spike/, and what a page POSTs to /spike/report is added to `report.txt` there.
              let spike = Path.Combine(artifacts, "spike")
              app.Run(fun context ->
                  let path = context.Request.Path.Value |> Option.ofObj |> Option.defaultValue "/"
                  if path = "/spike/report" && context.Request.Method = "POST" then
                      task {
                          use reader = new StreamReader(context.Request.Body)
                          let! body = reader.ReadToEndAsync()
                          Directory.CreateDirectory spike |> ignore
                          File.AppendAllText(Path.Combine(spike, "report.txt"), body + "\n")
                          context.Response.StatusCode <- 204
                      }
                      :> Task
                  elif path = "/spike" || path.StartsWith("/spike/", StringComparison.Ordinal) then
                      context.Request.Path <- PathString(if path.Length <= 7 then "/" else path.Substring 6)
                      serveFiles spike context
                  elif context.Request.Path.Value = "/scribble-dev-ca.crt" && not selfSigned then
                      context.Response.ContentType <- "application/x-x509-ca-cert"
                      context.Response.Headers.ContentDisposition <- "attachment; filename=scribble-dev-ca.crt"
                      context.Response.WriteAsync caPem
                  else
                      // Links use the address the device used to reach the hub.
                      let reached = context.Request.Host.Host
                      context.Response.ContentType <- "text/html; charset=utf-8"
                      context.Response.Headers.CacheControl <- "no-cache"
                      context.Response.WriteAsync(Hub.page reached cards build selfSigned))
              yield app ]

        for app in apps do
            app.StartAsync().Wait()

        Console.OutputEncoding <- Text.Encoding.UTF8
        let hubUrls = urls hubPort "http"
        Console.WriteLine()
        Console.WriteLine("Serving the browser samples on this network (Ctrl+C stops).")
        Console.WriteLine()
        Console.WriteLine("On another device, open the hub (scan with the camera, or type the address):")
        Console.WriteLine()
        Console.Write(Hub.terminalQr hubUrls.Head)
        Console.WriteLine()
        Console.WriteLine("  " + hubUrls.Head + (if addresses.IsEmpty || not addresses.Head.Primary then "" else "   (" + addresses.Head.Name + ")"))
        if hubUrls.Length > 1 then
            Console.WriteLine("  other addresses for it:")
            for (u, a) in List.zip hubUrls.Tail ((addresses |> List.map (fun a -> a.Name)) @ [ "host name" ] |> List.tail) do
                Console.WriteLine("    " + u + "   (" + a + ")")
        Console.WriteLine()
        Console.WriteLine("Samples on this machine: " + String.Join(", ", [ for s in samples -> "https://localhost:" + string s.Port + "/ (" + s.Title + ")" ]))
        match build with
        | ValueSome b -> Console.WriteLine("Build: " + (if b.Aot then "WebAssembly AOT" else "interpreted (pass --aot for speed)"))
        | ValueNone -> ()
        if selfSigned then
            Console.WriteLine("Self-signed certificate: each device warns once; choose to continue anyway.")
        else
            Console.WriteLine("The hub offers the certificate to devices that don't trust it yet (this machine: "
                              + Path.Combine(certFolder, "scribble-dev-ca.crt") + ").")
        Console.WriteLine()
        Console.WriteLine("Nothing loads on another device? Windows Firewall may be blocking it: allow \"Scribble.Serve\"")
        Console.WriteLine("(or dotnet) on private networks, and make sure this network is set to Private.")

        let stop = new System.Threading.ManualResetEventSlim(false)
        Console.CancelKeyPress.Add(fun e ->
            e.Cancel <- true
            stop.Set())
        stop.Wait()
        for app in apps do
            app.StopAsync().Wait()
        0
