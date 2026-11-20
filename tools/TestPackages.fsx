// The consumer test (DESIGN.md, "Publishing", decision 9): packs every Scribble library, then builds apps that
// know Scribble only as NuGet packages, the way an app outside this repository does. It is the gate for a release
// and runs in CI.
//
//     dotnet fsi tools/TestPackages.fsx                      (a throwaway version, so nothing stale is reused)
//     dotnet fsi tools/TestPackages.fsx --version 0.1.0-alpha.1   (the release workflow: tests what it will push)
//     dotnet fsi tools/TestPackages.fsx --no-run             (builds and publishes, starts nothing: no GPU needed)
//
// What it checks:
//   1. `dotnet pack` of the solution gives the 17 packages, into artifacts/packages.
//   2. A desktop app referencing only Scribble.Desktop builds, has the four native libraries beside it, runs, draws
//      two frames and closes itself.
//   3. The same app published with NativeAOT has them too and runs the same.
//   4. A browser app referencing only Scribble.Browser builds and publishes with Scribble's default page.
//
// The apps are made in a temporary folder outside the repository (inside it they would pick up
// Directory.Build.props), with a package cache of their own for the Scribble packages, so a run never sees packages
// an earlier run left behind.

open System
open System.Diagnostics
open System.IO

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))
let args = fsi.CommandLineArgs |> Array.skip 1 |> List.ofArray
let noRun = args |> List.contains "--no-run"

let version =
    match args |> List.tryFindIndex ((=) "--version") with
    | Some i when i + 1 < args.Length -> args[i + 1]
    | _ -> "0.0.0-test." + DateTime.UtcNow.ToString "yyyyMMddHHmmss"

let packages = Path.Combine(root, "artifacts", "packages")
let work = Path.Combine(Path.GetTempPath(), "scribble-package-test")

let fail (message: string) =
    Console.Error.WriteLine("FAILED: " + message)
    exit 1

/// Runs a program to its end, echoing its output, and gives back the exit code and what it wrote.
let run (dir: string) (exe: string) (arguments: string) (timeout: TimeSpan) =
    Console.WriteLine($"> {exe} {arguments}")
    let info = ProcessStartInfo(exe, arguments, WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false)
    use p = new Process(StartInfo = info)
    let output = Text.StringBuilder()
    let keep (line: string) =
        if not (isNull line) then
            lock output (fun () -> output.AppendLine line |> ignore)
            Console.WriteLine("    " + line)
    p.OutputDataReceived.Add(fun e -> keep e.Data)
    p.ErrorDataReceived.Add(fun e -> keep e.Data)
    p.Start() |> ignore
    p.BeginOutputReadLine()
    p.BeginErrorReadLine()
    if not (p.WaitForExit timeout) then
        p.Kill true
        fail $"{exe} {arguments} was still running after {timeout.TotalSeconds:F0} s"
    p.WaitForExit()
    p.ExitCode, output.ToString()

let dotnet (dir: string) (arguments: string) =
    let code, _ = run dir "dotnet" arguments (TimeSpan.FromMinutes 20.0)
    if code <> 0 then
        fail $"dotnet {arguments} ended with {code}"

let expectFile (path: string) =
    if not (File.Exists path) then
        fail $"missing {path}"

// A NativeAOT publish finds the C++ linker through vswhere.exe, which Visual Studio's installer doesn't put on
// PATH; without it the publish ends in "'vswhere.exe' is not recognized" and a linker error.
let installer = Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86, "Microsoft Visual Studio", "Installer")
if File.Exists(Path.Combine(installer, "vswhere.exe")) then
    Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable "PATH" + ";" + installer)

// --- 1. Pack ----------------------------------------------------------------------------------------------

if Directory.Exists packages then
    Directory.Delete(packages, true)
// A full rebuild first: after a change of version alone, an incremental build left an assembly compiled against
// the old version of another (2026-11-19: "Could not load Scribble.Platform, Version=0.1.0.0" in the app below).
dotnet root $"build Scribble.slnx -c Release --no-incremental -p:Version={version}"
dotnet root $"pack Scribble.slnx -c Release --no-build -o \"{packages}\" -p:Version={version}"

let expected =
    [ "Accessibility.AccessKit"; "Browser"; "Core"; "Desktop"; "Icons.Material"; "Imaging"; "Inspector"; "Platform"
      "Platform.Browser"; "Platform.SDL3"; "Rendering"; "Rendering.WebGPU"; "Rendering.WebGPU.Browser"; "Text"
      "Text.Emoji"; "Text.HarfBuzz"; "Text.HarfBuzz.Browser" ]
for name in expected do
    expectFile (Path.Combine(packages, $"Scribble.{name}.{version}.nupkg"))
    expectFile (Path.Combine(packages, $"Scribble.{name}.{version}.snupkg"))
let found = Directory.GetFiles(packages, "*.nupkg").Length
if found <> expected.Length then
    fail $"{found} packages packed, {expected.Length} expected: a new library goes in this list (and in src/PACKAGE.md if apps reference it)"

// --- The apps ---------------------------------------------------------------------------------------------

if Directory.Exists work then
    Directory.Delete(work, true)
Directory.CreateDirectory work |> ignore

// Packages restored before (FSharp.Core, the AOT compiler, the WebAssembly pack) come from the machine's cache as
// a fallback; the Scribble packages are new each run and land in the test's own folder.
let machineCache =
    match Environment.GetEnvironmentVariable "NUGET_PACKAGES" with
    | null | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".nuget", "packages")
    | path -> path
Directory.CreateDirectory machineCache |> ignore

File.WriteAllText(
    Path.Combine(work, "nuget.config"),
    $"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="{Path.Combine(work, "packages")}" />
  </config>
  <fallbackPackageFolders>
    <clear />
    <add key="machine" value="{machineCache}" />
  </fallbackPackageFolders>
  <packageSources>
    <clear />
    <add key="scribble-local" value="{packages}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
""")

// Nothing above this folder may shape the build.
File.WriteAllText(Path.Combine(work, "Directory.Build.props"), "<Project />\n")
File.WriteAllText(Path.Combine(work, "Directory.Build.targets"), "<Project />\n")
File.WriteAllText(Path.Combine(work, "global.json"), File.ReadAllText(Path.Combine(root, "global.json")))

/// The README's first app.
let counter =
    """
open System
open Scribble.Core

type Msg = Increment | Decrement

let update msg count =
    match msg with
    | Increment -> count + 1
    | Decrement -> count - 1

let view count dispatch =
    StackPanel.create [
        StackPanel.margin 24.0
        StackPanel.spacing 16.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"Count: {count}"; TextBlock.fontSize 28.0 ]
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 8.0
                StackPanel.children [
                    Button.create [ Button.text "−"; Button.onClick (fun _ -> dispatch Decrement) ]
                    Button.create [ Button.text "+"; Button.onClick (fun _ -> dispatch Increment) ]
                ]
            ]
        ]
    ]
"""

let project (sdk: string) (package: string) (extra: string) =
    $"""<Project Sdk="{sdk}">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>{extra}
  </PropertyGroup>
  <ItemGroup><Compile Include="Program.fs" /></ItemGroup>
  <ItemGroup><PackageReference Include="{package}" Version="{version}" /></ItemGroup>
</Project>
"""

// --- 2 and 3. Desktop -------------------------------------------------------------------------------------

let desktop = Path.Combine(work, "DesktopApp")
Directory.CreateDirectory desktop |> ignore
File.WriteAllText(Path.Combine(desktop, "DesktopApp.fsproj"), project "Microsoft.NET.Sdk" "Scribble.Desktop" "")
// The counter, closing itself a moment after its second frame and saying how many it drew.
File.WriteAllText(
    Path.Combine(desktop, "Program.fs"),
    "module Program\n"
    + counter
    + """
let mutable frames = 0

[<EntryPoint>]
let main _ =
    let code =
        App.run [
            App.title "Counter"
            App.program 0 update view
            App.onStart (fun app ->
                app.Platform.After(TimeSpan.FromSeconds 1.0, fun () -> app.Host.Close()))
            App.onFrame (fun _ ->
                frames <- frames + 1
                None)
        ]
    Console.WriteLine("Scribble package test: frames=" + string frames)
    code
""")

let natives = [ "wgpu_native.dll"; "SDL3.dll"; "accesskit.dll"; "libHarfBuzzSharp.dll" ]

let checkDesktop (what: string) (dir: string) (nativeDir: string) =
    for dll in natives do
        expectFile (Path.Combine(nativeDir, dll))
    if not noRun then
        let code, output = run dir (Path.Combine(dir, "DesktopApp.exe")) "" (TimeSpan.FromSeconds 60.0)
        if code <> 0 then
            fail $"the {what} desktop app ended with {code}"
        let line = output.Split('\n') |> Array.tryFind _.Contains("Scribble package test: frames=")
        match line with
        | Some l when (int (l.Trim().Substring(l.IndexOf '=' + 1))) >= 1 -> ()
        | _ -> fail $"the {what} desktop app drew no frame"

dotnet desktop "build -c Release"
let built = Path.Combine(desktop, "bin", "Release", "net10.0")
// Without a runtime identifier the SDK leaves native libraries in runtimes/<rid>/native and the host finds them.
checkDesktop "built" built (Path.Combine(built, "runtimes", "win-x64", "native"))

dotnet desktop "publish -c Release -r win-x64 -p:PublishAot=true -o publish"
let published = Path.Combine(desktop, "publish")
checkDesktop "NativeAOT" published published

// --- 4. Browser -------------------------------------------------------------------------------------------

let browser = Path.Combine(work, "BrowserApp")
Directory.CreateDirectory browser |> ignore
File.WriteAllText(
    Path.Combine(browser, "BrowserApp.fsproj"),
    project "Microsoft.NET.Sdk.WebAssembly" "Scribble.Browser" "\n    <InvariantGlobalization>true</InvariantGlobalization>")
File.WriteAllText(
    Path.Combine(browser, "Program.fs"),
    "module Program\n"
    + counter
    + """
[<EntryPoint>]
let main _ = App.run [ App.title "Counter"; App.program 0 update view ]
""")

dotnet browser "publish -c Release -o publish"
let wwwroot = Path.Combine(browser, "publish", "wwwroot")
// Scribble's own page, which the package's build/Scribble.Browser.targets adds when the app has none.
expectFile (Path.Combine(wwwroot, "index.html"))
expectFile (Path.Combine(wwwroot, "main.js"))
if Directory.GetFiles(Path.Combine(wwwroot, "_framework"), "Scribble.Core*").Length = 0 then
    fail "the browser app's _framework has no Scribble.Core"

let ran = if noRun then "built and published (not started: --no-run)" else "built, published and run"
Console.WriteLine($"Packages {version}: {expected.Length} packed; desktop and NativeAOT apps {ran}; browser app published.")
