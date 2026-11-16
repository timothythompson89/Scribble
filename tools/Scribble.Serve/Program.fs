/// Serves the browser samples to the local network over HTTPS:
///
///   dotnet run --project tools/Scribble.Serve                 publish both samples, then serve them
///   dotnet run --project tools/Scribble.Serve -- --aot        publish with WebAssembly AOT (slow publish, fast app)
///   dotnet run --project tools/Scribble.Serve -- --no-build   serve what was published last
///   dotnet run --project tools/Scribble.Serve -- --self-signed   no certificate authority to install (warnings instead)
///
/// Hello is on port 5280, Counter on 5281 (HTTPS); port 5279 (plain HTTP) offers the certificate to
/// install on other devices. Published files and certificates go to `artifacts/serve` (git-ignored).
module Scribble.Serve.Program

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.StaticFiles
open Microsoft.Extensions.Logging

type private Sample = { Name: string; Project: string; Port: int }

let private samples =
    [ { Name = "hello"; Project = "samples/Scribble.Samples.Browser"; Port = 5280 }
      { Name = "counter"; Project = "samples/Scribble.Samples.Counter.Browser"; Port = 5281 } ]

let private certificatePort = 5279

/// The repository root: the folder holding Scribble.slnx, above the working directory or this tool.
let private repoRoot () =
    let rec up (dir: DirectoryInfo) =
        if isNull dir then None
        elif File.Exists(Path.Combine(dir.FullName, "Scribble.slnx")) then Some dir.FullName
        else up dir.Parent
    up (DirectoryInfo Environment.CurrentDirectory)
    |> Option.orElse (up (DirectoryInfo AppContext.BaseDirectory))
    |> Option.defaultWith (fun () -> failwith "Run this from inside the Scribble repository.")

/// This machine's IPv4 addresses on networks that are up (no loopback, no link-local).
let private lanAddresses () =
    [ for nic in NetworkInterface.GetAllNetworkInterfaces() do
        if nic.OperationalStatus = OperationalStatus.Up && nic.NetworkInterfaceType <> NetworkInterfaceType.Loopback then
            for a in nic.GetIPProperties().UnicastAddresses do
                let ip = a.Address
                let bytes = ip.GetAddressBytes()
                if ip.AddressFamily = AddressFamily.InterNetwork && not (bytes[0] = 169uy && bytes[1] = 254uy) then
                    yield nic.Name, ip ]

/// Runs `dotnet publish` for one sample into `output`, streaming its output. False if it failed.
let private publish (root: string) (sample: Sample) (output: string) (aot: bool) =
    let args =
        [ "publish"; Path.Combine(root, sample.Project); "-c"; "Release"; "-o"; output; "--nologo"; "-v"; "minimal" ]
        @ (if aot then [ "-p:BrowserAot=true" ] else [])
    let info = ProcessStartInfo("dotnet", args)
    info.UseShellExecute <- false
    Console.WriteLine("Publishing " + sample.Name + (if aot then " (WebAssembly AOT: this takes a few minutes)" else "") + "...")
    use p = Process.Start info
    p.WaitForExit()
    p.ExitCode = 0

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

/// The page that offers the certificate authority to other devices (plain HTTP: they don't trust
/// our HTTPS yet), with how to install it.
let private certificatePage =
    """<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Scribble: certificate</title>
<style>body{font:16px system-ui,sans-serif;max-width:40em;margin:2em auto;padding:0 1em;line-height:1.5}a.button{display:inline-block;padding:.6em 1em;background:#3d59a1;color:#fff;border-radius:6px;text-decoration:none}li{margin:.4em 0}</style></head>
<body><h1>Scribble development certificate</h1>
<p>The samples need HTTPS (WebGPU only runs on secure pages). Install this certificate once on each device to open them without warnings. It only identifies the development machine serving this page.</p>
<p><a class="button" href="/scribble-dev-ca.crt">Download the certificate</a></p>
<ul>
<li><b>Windows:</b> open the file, <i>Install Certificate</i>, <i>Current User</i>, <i>Place all certificates in the following store</i>: <i>Trusted Root Certification Authorities</i>.</li>
<li><b>Android:</b> Settings, <i>Security</i>, <i>Encryption &amp; credentials</i>, <i>Install a certificate</i>, <i>CA certificate</i>, pick the downloaded file. (Menu names vary by phone; search settings for "CA certificate".)</li>
<li><b>iPhone and iPad (Safari):</b> download, then Settings, <i>Profile Downloaded</i>, <i>Install</i>. Then Settings, <i>General</i>, <i>About</i>, <i>Certificate Trust Settings</i>, and turn on full trust for it.</li>
<li><b>macOS:</b> open the file in Keychain Access, then set it to <i>Always Trust</i>.</li>
</ul>
<p>Then open the samples (addresses below, on the same network). To remove it later, delete "Scribble development CA" from the same place.</p>
<ul>%LINKS%</ul></body></html>"""

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
        let host = Dns.GetHostName()
        let addresses = lanAddresses ()
        let certificate = Certificates.server certFolder selfSigned host (addresses |> List.map snd)
        let caPem = if selfSigned then "" else Certificates.authorityPem certFolder

        let urls (port: int) (scheme: string) =
            [ for (_, ip) in addresses -> scheme + "://" + ip.ToString() + ":" + string port + "/" ]
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
              if not selfSigned then
                  let builder = WebApplication.CreateSlimBuilder()
                  quietLogging builder
                  builder.WebHost.ConfigureKestrel(fun options -> options.ListenAnyIP certificatePort) |> ignore
                  let app = builder.Build()
                  let links =
                      String.Join(
                          "",
                          [ for s in samples do
                              for u in urls s.Port "https" -> "<li>" + s.Name + ": <a href=\"" + u + "\">" + u + "</a></li>" ]
                      )
                  let page = certificatePage.Replace("%LINKS%", links)
                  app.Run(fun context ->
                      if context.Request.Path.Value = "/scribble-dev-ca.crt" then
                          context.Response.ContentType <- "application/x-x509-ca-cert"
                          context.Response.Headers.ContentDisposition <- "attachment; filename=scribble-dev-ca.crt"
                          context.Response.WriteAsync caPem
                      else
                          context.Response.ContentType <- "text/html; charset=utf-8"
                          context.Response.WriteAsync page)
                  yield app ]

        for app in apps do
            app.StartAsync().Wait()

        Console.WriteLine()
        Console.WriteLine("Serving the browser samples on this network (Ctrl+C stops):")
        for s in samples do
            Console.WriteLine()
            Console.WriteLine("  " + s.Name + ":")
            for u in urls s.Port "https" do
                Console.WriteLine("    " + u)
        Console.WriteLine()
        if selfSigned then
            Console.WriteLine("Self-signed certificate: each device warns once; choose to continue anyway.")
        else
            Console.WriteLine("First time on a device: install the certificate from")
            for u in urls certificatePort "http" do
                Console.WriteLine("    " + u)
            Console.WriteLine("  (this machine: " + Path.Combine(certFolder, "scribble-dev-ca.crt") + ")")
        Console.WriteLine()
        Console.WriteLine("Nothing loads on another device? Windows Firewall may be blocking it: allow \"Scribble.Serve\"")
        Console.WriteLine("(or dotnet) on private networks, and make sure this network is set to Private.")
        for (name, ip) in addresses do
            Console.WriteLine("  network: " + name + " " + ip.ToString())

        let stop = new System.Threading.ManualResetEventSlim(false)
        Console.CancelKeyPress.Add(fun e ->
            e.Cancel <- true
            stop.Set())
        stop.Wait()
        for app in apps do
            app.StopAsync().Wait()
        0
