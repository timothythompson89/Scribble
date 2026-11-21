/// Following the system's theme (DESIGN.md, "Following the system's theme"): `SystemTheme.prefersDark`, the name
/// `Theme.system`, `App.systemThemes`, and what a new user starts with. The fake platform says what the system is
/// set to and changes it as a system would; what a real system reports is the platform's.
module Scribble.Tests.SystemThemeTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests
open Scribble.Tests.TestShaping

let private night = Theme.dark [ Theme.accent (Scribble.Rendering.Color.Hex 0x101080) ]
let private day = Theme.light [ Theme.accent (Scribble.Rendering.Color.Hex 0x80A0FF) ]
let private rose = Theme.light [ Theme.accent (Scribble.Rendering.Color.Hex 0xFF2080) ]

let private named: AppSetting<unit> = App.themes [ "Rose", rose; "Night", night; "Day", day ]

/// An app on a system set to `system` (none: it doesn't say), over `store`.
let private startOver (system: bool voption) (store: MemoryValueStore) (settings: AppSetting<unit> list) =
    let platform, device = new FakePlatform(SystemDark = system), new FakeDevice()
    let app = App.start (platform, device, new FixedShaper(), [ App.valueStore (fun _ -> store :> IValueStore); App.view (Border.create []) ] @ settings)
    platform, (app :?> ElementHost).AppValues

let private startWith system settings = startOver system (MemoryValueStore()) settings

let private warningsWhile (about: string) (act: unit -> unit) =
    let warnings = List<string>()
    let listener = Action<string>(fun m -> lock warnings (fun () -> warnings.Add m))
    DebugWarnings.Listener <- Delegate.Combine(DebugWarnings.Listener, listener) :?> Action<string>
    try
        act ()
    finally
        DebugWarnings.Listener <- Delegate.Remove(DebugWarnings.Listener, listener) :?> Action<string>
    lock warnings (fun () -> warnings |> Seq.filter (fun m -> m.Contains about) |> List.ofSeq)

[<Fact>]
let ``SystemTheme.prefersDark is what the system says, follows it, and is dark where it doesn't say`` () =
    let platform, values = startWith (ValueSome false) []
    Assert.False(values.Get SystemTheme.prefersDark)
    let heard = List<bool>()
    use _ = values.Subscribe(SystemTheme.prefersDark, heard.Add)
    platform.SetSystemDark true
    Assert.True(values.Get SystemTheme.prefersDark)
    // The same again: nothing to hear.
    platform.SetSystemDark true
    platform.SetSystemDark false
    Assert.Equal<bool list>([ true; false ], List.ofSeq heard)
    let _, unknown = startWith ValueNone []
    Assert.True(unknown.Get SystemTheme.prefersDark)
    // A bare tree has no system to ask.
    Assert.True(AppValues().Get SystemTheme.prefersDark)

[<Fact>]
let ``an app that names which themes follow the system starts a new user on the system's, and follows it`` () =
    let s = MemoryValueStore()
    let platform, values = startOver (ValueSome false) s [ named; App.systemThemes "Night" "Day" ]
    Assert.Equal(Theme.system, values.Get Theme.picked)
    Assert.Same(day, values.Get Theme.current)
    // Starting stored nothing: the user hasn't picked.
    Assert.Empty s.Values
    platform.SetSystemDark true
    Assert.Same(night, values.Get Theme.current)
    platform.SetSystemDark false
    Assert.Same(day, values.Get Theme.current)
    Assert.Empty s.Values
    // On a dark system the same app starts dark.
    let _, dark = startOver (ValueSome true) (MemoryValueStore()) [ named; App.systemThemes "Night" "Day" ]
    Assert.Same(night, dark.Get Theme.current)

[<Fact>]
let ``a theme picked by name stays when the system changes, and picking System follows it again`` () =
    let s = MemoryValueStore()
    let platform, values = startOver (ValueSome false) s [ named; App.systemThemes "Night" "Day" ]
    values.Set(Theme.picked, "Rose")
    Assert.Same(rose, values.Get Theme.current)
    platform.SetSystemDark true
    Assert.Same(rose, values.Get Theme.current)
    Assert.Equal("Rose", s.Values["Scribble.Theme"])
    // The next run, on a dark system: still the one picked.
    let _, next = startOver (ValueSome true) s [ named; App.systemThemes "Night" "Day" ]
    Assert.Equal("Rose", next.Get Theme.picked)
    Assert.Same(rose, next.Get Theme.current)
    // Picking System is remembered like any name, and is the system's theme at once.
    next.Set(Theme.picked, Theme.system)
    Assert.Same(night, next.Get Theme.current)
    Assert.Equal("System", s.Values["Scribble.Theme"])
    let again, third = startOver (ValueSome false) s [ named; App.systemThemes "Night" "Day" ]
    Assert.Equal(Theme.system, third.Get Theme.picked)
    Assert.Same(day, third.Get Theme.current)
    again.SetSystemDark true
    Assert.Same(night, third.Get Theme.current)

[<Fact>]
let ``an app that hasn't said which themes follow the system starts on its first, and System picks nothing`` () =
    let platform, values = startWith (ValueSome false) [ named ]
    Assert.Equal("Rose", values.Get Theme.picked)
    Assert.Same(rose, values.Get Theme.current)
    values.Set(Theme.picked, Theme.system)
    Assert.Same(rose, values.Get Theme.current)
    platform.SetSystemDark true
    Assert.Same(rose, values.Get Theme.current)
    // A stored "System" from a version that had it set up: the first theme, and the stored name stays.
    let s = MemoryValueStore()
    s.Values["Scribble.Theme"] <- "System"
    let _, later = startOver (ValueSome true) s [ named ]
    Assert.Equal("Rose", later.Get Theme.picked)
    Assert.Same(rose, later.Get Theme.current)
    Assert.Equal("System", s.Values["Scribble.Theme"])

[<Fact>]
let ``the built-in pair still starts dark on a light system, and follows the system once System is picked`` () =
    let platform, values = startWith (ValueSome false) []
    let dark = values.Get Theme.current
    Assert.Equal("Dark", values.Get Theme.picked)
    values.Set(Theme.picked, Theme.system)
    let light = values.Get Theme.current
    Assert.NotSame(dark, light)
    platform.SetSystemDark true
    Assert.Same(dark, values.Get Theme.current)
    platform.SetSystemDark false
    Assert.Same(light, values.Get Theme.current)

[<Fact>]
let ``App.rememberTheme false still follows the system and keeps nothing`` () =
    let s = MemoryValueStore()
    s.Values["Scribble.Theme"] <- "Rose"
    let platform, values = startOver (ValueSome true) s [ named; App.systemThemes "Night" "Day"; App.rememberTheme false ]
    Assert.Equal(Theme.system, values.Get Theme.picked)
    Assert.Same(night, values.Get Theme.current)
    platform.SetSystemDark false
    Assert.Same(day, values.Get Theme.current)
    values.Set(Theme.picked, "Night")
    Assert.Equal("Rose", s.Values["Scribble.Theme"])

[<Fact>]
let ``a component that uses SystemTheme.prefersDark runs again when the system changes`` () =
    let seen = List<bool>()
    let view =
        Component.define "SystemWatcher" (fun ctx () ->
            seen.Add (ctx.useAppValue SystemTheme.prefersDark).Current
            Border.create [])
    let platform, device = new FakePlatform(SystemDark = ValueSome false), new FakeDevice()
    use _ = App.start (platform, device, new FixedShaper(), [ App.view (view ()) ])
    let w = platform.Window.Value
    w.DeliverFrame() |> ignore
    platform.SetSystemDark true
    w.DeliverFrame() |> ignore
    Assert.Equal<bool list>([ false; true ], List.ofSeq seen)

[<Fact>]
let ``names that can't work are warned of in Debug`` () =
    let reserved = warningsWhile "follow the system" (fun () -> startWith ValueNone [ App.themes [ "System", night; "Day", day ] ] |> ignore)
    let missing = warningsWhile "App.systemThemes names" (fun () -> startWith ValueNone [ named; App.systemThemes "Night" "Noon" ] |> ignore)
#if DEBUG
    Assert.InRange(reserved.Length, 0, 1)
    Assert.InRange(missing.Length, 0, 1)
#else
    Assert.Empty reserved
    Assert.Empty missing
#endif
    // A name that isn't there, on a system set to what it stands for: System picks nothing, and a new user starts
    // on the first theme.
    let _, values = startWith (ValueSome false) [ named; App.systemThemes "Night" "Noon" ]
    Assert.Equal("Rose", values.Get Theme.picked)
    Assert.Same(rose, values.Get Theme.current)
