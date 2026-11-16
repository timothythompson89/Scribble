/// The hub: the page on port 5279 (plain HTTP, so a device can open it before it trusts our
/// certificate) that links the samples, says whether this device trusts the certificate yet and
/// offers it if not; plus the addresses to reach it and its QR code for the terminal.
module Scribble.Serve.Hub

open System
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open System.Text
open QRCoder

/// An address this machine can be reached at. `Primary` is the best guess for other devices: an
/// adapter with a default gateway that isn't virtual (Hyper-V, WSL, VPN, ...).
type Address = { Name: string; Ip: IPAddress; Primary: bool }

let private isVirtual (nic: NetworkInterface) =
    let text = (nic.Name + " " + nic.Description).ToLowerInvariant()
    nic.NetworkInterfaceType = NetworkInterfaceType.Tunnel
    || [ "virtual"; "vethernet"; "hyper-v"; "wsl"; "vmware"; "virtualbox"; "vpn"; "tap-"; "wireguard"; "tailscale"; "zerotier" ]
       |> List.exists text.Contains

/// This machine's IPv4 addresses on networks that are up (no loopback, no link-local), the primary
/// one first, then other real adapters, then virtual ones.
let addresses () =
    [ for nic in NetworkInterface.GetAllNetworkInterfaces() do
        if nic.OperationalStatus = OperationalStatus.Up && nic.NetworkInterfaceType <> NetworkInterfaceType.Loopback then
            let props = nic.GetIPProperties()
            let hasGateway =
                props.GatewayAddresses
                |> Seq.exists (fun g -> g.Address.AddressFamily = AddressFamily.InterNetwork && not (g.Address.Equals IPAddress.Any))
            let virt = isVirtual nic
            for a in props.UnicastAddresses do
                let ip = a.Address
                let bytes = ip.GetAddressBytes()
                if ip.AddressFamily = AddressFamily.InterNetwork && not (bytes[0] = 169uy && bytes[1] = 254uy) then
                    yield struct ((if hasGateway && not virt then 0 elif not virt then 1 else 2), { Name = nic.Name; Ip = ip; Primary = false }) ]
    |> List.sortBy (fun struct (rank, _) -> rank)
    |> List.mapi (fun i struct (rank, a) -> { a with Primary = (i = 0 && rank = 0) })

/// `text` as a QR code in the terminal: two module rows per line with half blocks, light modules
/// drawn (so it reads right on a dark terminal, like Rider's), with the quiet zone around it.
let terminalQr (text: string) =
    use generator = new QRCodeGenerator()
    use data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L)
    let rows = data.ModuleMatrix
    let size = rows.Count
    let dark (x: int) (y: int) = y < size && rows[y][x]
    let sb = StringBuilder()
    for y in 0..2 .. size - 1 do
        sb.Append("  ") |> ignore
        for x in 0 .. size - 1 do
            let top, bottom = not (dark x y), (y + 1 < size && not (dark x (y + 1)))
            sb.Append(
                match top, bottom with
                | true, true -> '█'
                | true, false -> '▀'
                | false, true -> '▄'
                | false, false -> ' '
            )
            |> ignore
        sb.AppendLine() |> ignore
    sb.ToString()

/// A sample as the hub shows it.
type Card = { Title: string; Description: string; Port: int }

/// How the samples were built, shown on the hub so a device can tell it runs the latest publish.
type BuildInfo = { Aot: bool; PublishedUtc: DateTime }

let private encode (s: string) = WebUtility.HtmlEncode s

/// The hub page for a device that reached us at `host` (its links use the same address).
let page (host: string) (cards: Card list) (build: BuildInfo voption) (selfSigned: bool) =
    let link port = "https://" + host + ":" + string port + "/"
    let cardsHtml =
        String.Join(
            "\n",
            [ for c in cards ->
                $"""<a class="card" href="{encode (link c.Port)}"><div><b>{encode c.Title}</b><span>{encode c.Description}</span></div><span class="open">Open</span></a>""" ]
        )
    let buildHtml =
        match build with
        | ValueSome b ->
            let kind = if b.Aot then "WebAssembly AOT" else "interpreted (no AOT: slower)"
            let iso = b.PublishedUtc.ToString("o")
            $"""<p class="build">Build: {kind}, published <time datetime="{iso}">{iso}</time>.</p>"""
        | ValueNone -> ""
    let probePort = cards |> List.tryHead |> Option.map _.Port |> Option.defaultValue 5280
    let certSection =
        if selfSigned then
            """<section class="status"><p>Self-signed certificate: each sample warns once on each device; choose to continue anyway.</p></section>"""
        else
            """<section class="status" id="status"><p id="status-text">Checking whether this device trusts the certificate…</p>
<details id="cert"><summary>Install the certificate</summary>
<p>The samples need HTTPS (WebGPU only runs on secure pages). Install this certificate once on each device to open them without warnings. It only identifies the development machine serving this page.</p>
<p><a class="button" href="/scribble-dev-ca.crt">Download the certificate</a></p>
<ul>
<li><b>Android:</b> Settings, <i>Security</i>, <i>Encryption &amp; credentials</i>, <i>Install a certificate</i>, <i>CA certificate</i>, pick the downloaded file. (Menu names vary by phone; search settings for "CA certificate".)</li>
<li><b>iPhone and iPad (Safari):</b> download, then Settings, <i>Profile Downloaded</i>, <i>Install</i>. Then Settings, <i>General</i>, <i>About</i>, <i>Certificate Trust Settings</i>, and turn on full trust for it.</li>
<li><b>Windows:</b> open the file, <i>Install Certificate</i>, <i>Current User</i>, <i>Place all certificates in the following store</i>: <i>Trusted Root Certification Authorities</i>.</li>
<li><b>macOS:</b> open the file in Keychain Access, then set it to <i>Always Trust</i>.</li>
</ul>
<p>Then reload this page. To remove it later, delete "Scribble development CA" from the same place.</p>
</details></section>"""
    let probeScript =
        """<script>
const text = document.getElementById("status-text"), cert = document.getElementById("cert"), status = document.getElementById("status");
// A request to a sample only succeeds once this device trusts our certificate (no-cors: we only need the TLS handshake).
fetch("https://" + location.hostname + ":%PROBE%/", { mode: "no-cors", cache: "no-store" })
  .then(() => { status.className = "status ok"; text.textContent = "✓ This device trusts the certificate."; cert.open = false; })
  .catch(() => { status.className = "status warn"; text.textContent = "This device doesn't trust the certificate yet: install it first, then reload."; cert.open = true; });
</script>"""
    let script = if selfSigned then "" else probeScript.Replace("%PROBE%", string probePort)
    $"""<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Scribble samples</title>
<style>
:root {{ color-scheme: dark; --bg: #181825; --card: #24243a; --hover: #2e2e4a; --text: #e6e6f0; --dim: #a6a6c0; --accent: #7aa2f7; --ok: #9ece6a; --warn: #e0af68; }}
body {{ font: 16px/1.5 system-ui, sans-serif; background: var(--bg); color: var(--text); max-width: 40em; margin: 0 auto; padding: 1.5em 16px; }}
h1 {{ font-size: 1.5em; margin: 0 0 .2em; }} p {{ margin: .5em 0; }} a {{ color: var(--accent); }}
.cards {{ display: grid; gap: 10px; margin: 1.2em 0; }}
.card {{ display: flex; align-items: center; gap: 12px; padding: 14px 16px; background: var(--card); border-radius: 10px; color: var(--text); text-decoration: none; }}
.card:hover, .card:focus-visible {{ background: var(--hover); }}
.card div {{ flex: 1; display: flex; flex-direction: column; }} .card span {{ color: var(--dim); font-size: .9em; }}
.card .open {{ color: var(--accent); font-weight: 600; font-size: 1em; }}
.status {{ padding: 12px 16px; border-radius: 10px; background: var(--card); }}
.status.ok #status-text {{ color: var(--ok); }} .status.warn #status-text {{ color: var(--warn); font-weight: 600; }}
details summary {{ cursor: pointer; color: var(--accent); margin: .4em 0; }} li {{ margin: .4em 0; }}
a.button {{ display: inline-block; padding: .55em 1em; background: var(--accent); color: #11111b; border-radius: 6px; text-decoration: none; font-weight: 600; }}
.build {{ color: var(--dim); font-size: .9em; }}
</style></head>
<body><h1>Scribble samples</h1><p class="build">Served from this machine over HTTPS. WebGPU needed (current Chrome, Edge or Safari).</p>
{certSection}
<div class="cards">{cardsHtml}</div>
{buildHtml}
<script>for (const t of document.querySelectorAll("time")) t.textContent = new Date(t.dateTime).toLocaleString();</script>
{script}
</body></html>"""
