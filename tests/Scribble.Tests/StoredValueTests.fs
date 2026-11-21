/// Stored values (DESIGN.md, "Stored values"): app values kept between runs. Their text forms, what is read when
/// nothing or nonsense is stored, when the store is written and flushed, and the desktop's JSON file (its JSON
/// part: isolated storage itself is the platform's).
module Scribble.Tests.StoredValueTests

open System
open System.Collections.Generic
open System.IO
open System.Text
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests

type Density =
    | Roomy = 0
    | Compact = 1

let private store (s: MemoryValueStore) = s :> IValueStore

/// App values of their own (a second run of the app) over the same store.
let private run (s: IValueStore) = AppValues(Store = s)

/// The Debug warnings heard while `act` runs that contain `about`.
let private warningsWhile (about: string) (act: unit -> unit) =
    let warnings = List<string>()
    let listener = Action<string>(fun m -> lock warnings (fun () -> warnings.Add m))
    DebugWarnings.Listener <- Delegate.Combine(DebugWarnings.Listener, listener) :?> Action<string>
    try
        act ()
    finally
        DebugWarnings.Listener <- Delegate.Remove(DebugWarnings.Listener, listener) :?> Action<string>
    lock warnings (fun () -> warnings |> Seq.filter (fun m -> m.Contains about) |> List.ofSeq)

// ---- Text forms ----

let private name' = StoredValue.string "Types.name" "nobody"
let private compact = StoredValue.bool "Types.compact" false
let private count = StoredValue.int "Types.count" 3
let private volume = StoredValue.float "Types.volume" 0.5
let private density = StoredValue.enum "Types.density" Density.Roomy

[<Fact>]
let ``each type comes back in the next run`` () =
    let s = MemoryValueStore()
    let first = run s
    // Nothing stored: the defaults, and reading stores nothing.
    Assert.Equal("nobody", first.Get name')
    Assert.Equal(0.5, first.Get volume)
    Assert.Empty s.Values
    first.Set(name', "Ada \"the first\"\nLovelace")
    first.Set(compact, true)
    first.Set(count, -42)
    first.Set(volume, 0.1 + 0.2)
    first.Set(density, Density.Compact)
    Assert.Equal("true", s.Values["Types.compact"])
    Assert.Equal("-42", s.Values["Types.count"])
    Assert.Equal("Compact", s.Values["Types.density"])
    let second = run s
    Assert.Equal("Ada \"the first\"\nLovelace", second.Get name')
    Assert.True(second.Get compact)
    Assert.Equal(-42, second.Get count)
    // A float comes back as the same float, not a rounded one.
    Assert.Equal(0.1 + 0.2, second.Get volume)
    Assert.Equal(Density.Compact, second.Get density)

[<Fact>]
let ``floats are written the same in every culture, and the odd ones come back`` () =
    let before = Globalization.CultureInfo.CurrentCulture
    try
        Globalization.CultureInfo.CurrentCulture <- Globalization.CultureInfo "de-DE"
        let s = MemoryValueStore()
        let first = run s
        first.Set(volume, 1234.5)
        Assert.Equal("1234.5", s.Values["Types.volume"])
        Assert.Equal(1234.5, (run s).Get volume)
        first.Set(volume, Double.PositiveInfinity)
        Assert.Equal(Double.PositiveInfinity, (run s).Get volume)
        first.Set(volume, nan)
        Assert.True(Double.IsNaN((run s).Get volume))
    finally
        Globalization.CultureInfo.CurrentCulture <- before

[<Fact>]
let ``a stored value says it is one, and something bigger is stored as its parts`` () =
    Assert.True(compact.IsStored)
    Assert.False((AppValue.define "Types.plain" 0).IsStored)
    // A window's size: two numbers (there is no stored value of a record: only the five functions exist).
    let width, height = StoredValue.int "Types.WindowSizeX" 960, StoredValue.int "Types.WindowSizeY" 600
    let s = MemoryValueStore()
    let first = run s
    first.Set(width, 1280)
    first.Set(height, 720)
    let second = run s
    Assert.Equal((1280, 720), (second.Get width, second.Get height))
    // An enum is kept by its case's name; a number that is no case of it gives the default.
    s.Values["Types.density"] <- "7"
    Assert.Equal(Density.Roomy, second.Get density)

// ---- What can't be read ----

let private broken = StoredValue.int "Broken.count" 7
let private brokenDensity = StoredValue.enum "Broken.density" Density.Roomy

[<Fact>]
let ``what can't be read gives the default and a Debug warning, and never throws`` () =
    let s = MemoryValueStore()
    s.Values["Broken.count"] <- "seven"
    s.Values["Broken.density"] <- "Dense"
    let values = run s
    let warnings =
        warningsWhile "Broken." (fun () ->
            Assert.Equal(7, values.Get broken)
            Assert.Equal(Density.Roomy, values.Get brokenDensity))
#if DEBUG
    Assert.Equal(2, warnings.Length)
    Assert.Contains(warnings, fun m -> m.Contains "Broken.count" && m.Contains "seven")
#else
    Assert.Empty warnings
#endif
    // Setting it stores it again.
    values.Set(broken, 8)
    Assert.Equal("8", s.Values["Broken.count"])

[<Fact>]
let ``a store that throws is a store with nothing in it`` () =
    let failing =
        { new IValueStore with
            member _.TryRead _ = failwith "locked"
            member _.Write(_, _, _) = failwith "full"
            member _.Flush() = failwith "gone" }
    let values = run failing
    let value = StoredValue.int "Failing.count" 5
    Assert.Equal(5, values.Get value)
    values.Set(value, 6)
    Assert.Equal(6, values.Get value)
    values.FlushStore()

[<Fact>]
let ``two stored values of one name are warned of`` () =
    let a = StoredValue.int "Twice.name" 1
    let b = StoredValue.int "Twice.name" 2
    let values = run (MemoryValueStore())
    let warnings =
        warningsWhile "Twice.name" (fun () ->
            values.Get a |> ignore
            values.Get b |> ignore)
#if DEBUG
    Assert.Single warnings |> ignore
#else
    Assert.Empty warnings
#endif

// ---- In an app: shared, written at once, flushed after half a second of quiet and at close ----

let private level = StoredValue.int "App.level" 1
let private levelText = ref ""

let private reader =
    Component.define "LevelReader" (fun ctx () ->
        let l = ctx.useStoredValue level
        levelText.Value <- "level " + string l.Current
        TextBlock.create [ TextBlock.text levelText.Value ])

let private setter (set: (int -> unit) ref) =
    Component.define "LevelSetter" (fun ctx () ->
        let l = ctx.useStoredValue level
        set.Value <- l.Set
        Border.create [])

let private app (s: MemoryValueStore) (set: (int -> unit) ref) : AppSetting<unit> list =
    [ App.valueStore (fun _ -> s :> IValueStore)
      App.view (StackPanel.create [ StackPanel.children [ reader (); setter set () ] ]) ]

[<Fact>]
let ``a stored value is read before the first view and shared by the components that use it`` () =
    let s = MemoryValueStore()
    s.Values["App.level"] <- "9"
    let set = ref ignore
    let w, _, _ = start (app s set)
    w.DeliverFrame() |> ignore
    Assert.Equal("level 9", levelText.Value)
    set.Value 10
    w.DeliverFrame() |> ignore
    Assert.Equal("level 10", levelText.Value)
    // Written at once (the browser's store keeps it there and then).
    Assert.Equal("10", s.Values["App.level"])

[<Fact>]
let ``the store is flushed once, half a second after the last change, and not for an equal value`` () =
    let s = MemoryValueStore()
    let set = ref ignore
    let w, _, _ = start (app s set)
    w.DeliverFrame() |> ignore
    // A drag: a change every 100 ms for a second. Nothing is flushed while it goes on.
    for i in 2..11 do
        set.Value i
        w.Time.AdvanceMs 100.0
    Assert.Equal(0, s.Flushes)
    Assert.Equal("11", s.Values["App.level"])
    // Half a second of quiet after the last change, at the latest a second after it.
    w.Time.AdvanceMs 1000.0
    Assert.Equal(1, s.Flushes)
    // Nothing more while nothing changes, and an equal value is no change.
    set.Value 11
    w.Time.AdvanceMs 5000.0
    Assert.Equal(1, s.Flushes)
    set.Value 12
    w.Time.AdvanceMs 400.0
    Assert.Equal(1, s.Flushes)
    w.Time.AdvanceMs 200.0
    Assert.Equal(2, s.Flushes)

[<Fact>]
let ``closing the window saves a change made in the last half second`` () =
    let s = MemoryValueStore()
    let set = ref ignore
    let w, _, _ = start (app s set)
    w.DeliverFrame() |> ignore
    set.Value 5
    Assert.Equal(0, s.Flushes)
    w.Send CloseRequested
    Assert.Equal(1, s.Flushes)
    // The timer that was waiting finds nothing left to do.
    w.Time.AdvanceMs 2000.0
    Assert.Equal(1, s.Flushes)

[<Fact>]
let ``the store is made from App.storeName, or from the assembly's name`` () =
    let names = List<string>()
    let make (name: string) =
        names.Add name
        MemoryValueStore() :> IValueStore
    start [ App.valueStore make; App.storeName "Notes"; App.view (Border.create []) ] |> ignore
    start [ App.valueStore make; App.view (Border.create []) ] |> ignore
    Assert.Equal("Notes", names[0])
    Assert.False(String.IsNullOrWhiteSpace names[1])
    // A store that can't be opened: the app runs, keeping its values for the run.
    let set = ref ignore
    let w, _, _ = start (App.valueStore (fun _ -> failwith "no storage") :: (app (MemoryValueStore()) set).Tail)
    w.DeliverFrame() |> ignore
    set.Value 3
    w.DeliverFrame() |> ignore
    Assert.Equal("level 3", levelText.Value)

[<Fact>]
let ``an app with no store keeps stored values for the run, and a Debug build says so`` () =
    let set = ref ignore
    let warnings =
        warningsWhile "no store for them" (fun () ->
            let w, _, _ = start ((app (MemoryValueStore()) set).Tail)
            w.DeliverFrame() |> ignore
            set.Value 4
            w.DeliverFrame() |> ignore
            Assert.Equal("level 4", levelText.Value))
#if DEBUG
    // Once a process: another test may have been the one to hear it.
    Assert.InRange(warnings.Length, 0, 1)
#else
    Assert.Empty warnings
#endif

[<Fact>]
let ``useStoredValue with a plain app value warns in Debug`` () =
    let plain = AppValue.define "Hook.plain" 0
    let view = Component.define "PlainUser" (fun ctx () -> TextBlock.create [ TextBlock.text (string (ctx.useStoredValue plain).Current) ])
    let warnings =
        warningsWhile "Hook.plain" (fun () ->
            let w, _, _ = start [ App.view (view ()) ]
            w.DeliverFrame() |> ignore)
#if DEBUG
    Assert.Single warnings |> ignore
#else
    Assert.Empty warnings
#endif

// ---- The desktop's JSON file ----

/// A file in memory: what `read` gives and `write` replaces. `Writes` counts the files written.
type private FakeFile(start: string) =
    member val Text: string = start with get, set
    member val Writes = 0 with get, set
    member this.Store() : IValueStore =
        let read () : Stream = if isNull this.Text then null else new MemoryStream(Encoding.UTF8.GetBytes this.Text)
        let write (fill: Stream -> unit) =
            use buffer = new MemoryStream()
            fill buffer
            this.Text <- Encoding.UTF8.GetString(buffer.ToArray())
            this.Writes <- this.Writes + 1
        JsonValueFile(read, write)

[<Fact>]
let ``the JSON file holds a member per value, each of its own JSON type`` () =
    let file = FakeFile null
    let s = file.Store()
    Assert.Equal(ValueNone, s.TryRead "Compact")
    s.Write("Compact", StoredKind.Bool, "true")
    s.Write("Volume", StoredKind.Number, "0.5")
    s.Write("Theme", StoredKind.Text, "Dark")
    s.Write("Name", StoredKind.Text, "Ada \"A\"\n\\Lovelace é")
    // Not a JSON number: kept as text.
    s.Write("Odd", StoredKind.Number, "NaN")
    Assert.Equal(0, file.Writes)
    s.Flush()
    Assert.Equal(1, file.Writes)
    use document = Json.JsonDocument.Parse file.Text
    let root = document.RootElement
    Assert.Equal(Json.JsonValueKind.True, root.GetProperty("Compact").ValueKind)
    Assert.Equal(0.5, root.GetProperty("Volume").GetDouble())
    Assert.Equal("Dark", root.GetProperty("Theme").GetString())
    Assert.Equal("NaN", root.GetProperty("Odd").GetString())
    // Read by a new store (the next run).
    let next = file.Store()
    Assert.Equal(ValueSome "true", next.TryRead "Compact")
    Assert.Equal(ValueSome "0.5", next.TryRead "Volume")
    Assert.Equal(ValueSome "Ada \"A\"\n\\Lovelace é", next.TryRead "Name")
    Assert.Equal(ValueSome "NaN", next.TryRead "Odd")
    // Nothing changed: no file is written, on a flush or for a value written again as it is.
    next.Flush()
    next.Write("Compact", StoredKind.Bool, "true")
    next.Flush()
    Assert.Equal(1, file.Writes)
    next.Write("Compact", StoredKind.Bool, "false")
    next.Flush()
    Assert.Equal(2, file.Writes)

[<Fact>]
let ``the JSON file keeps members it doesn't know, in their order`` () =
    let file = FakeFile """{ "Recent": ["a.txt", "b.txt"], "Volume": 0.25, "Window": { "X": 10 }, "Gone": null }"""
    let s = file.Store()
    Assert.Equal(ValueSome "0.25", s.TryRead "Volume")
    // A list, an object and null are not a value's text.
    Assert.Equal(ValueNone, s.TryRead "Recent")
    Assert.Equal(ValueNone, s.TryRead "Gone")
    s.Write("Volume", StoredKind.Number, "0.75")
    s.Write("New", StoredKind.Text, "x")
    s.Flush()
    use document = Json.JsonDocument.Parse file.Text
    let names = [ for m in document.RootElement.EnumerateObject() -> m.Name ]
    Assert.Equal<string list>([ "Recent"; "Volume"; "Window"; "Gone"; "New" ], names)
    Assert.Equal("b.txt", (document.RootElement.GetProperty "Recent").[1].GetString())
    Assert.Equal(10, document.RootElement.GetProperty("Window").GetProperty("X").GetInt32())
    Assert.Equal(0.75, document.RootElement.GetProperty("Volume").GetDouble())

[<Theory>]
[<InlineData("")>]
[<InlineData("{ \"Volume\": 0.5")>]
[<InlineData("[1, 2, 3]")>]
[<InlineData("not json at all")>]
let ``a damaged JSON file is a file with nothing in it, and the next save replaces it`` (text: string) =
    let file = FakeFile text
    let s = file.Store()
    Assert.Equal(ValueNone, s.TryRead "Volume")
    s.Write("Volume", StoredKind.Number, "1")
    s.Flush()
    use document = Json.JsonDocument.Parse file.Text
    Assert.Equal(1.0, document.RootElement.GetProperty("Volume").GetDouble())

[<Fact>]
let ``stored values go through the JSON file and back`` () =
    let file = FakeFile null
    let first = run (file.Store())
    first.Set(volume, 0.1 + 0.2)
    first.Set(density, Density.Compact)
    first.Set(compact, true)
    // No dispatcher (a bare tree): flushed at each change.
    Assert.Equal(3, file.Writes)
    let second = run (file.Store())
    Assert.Equal(0.1 + 0.2, second.Get volume)
    Assert.Equal(Density.Compact, second.Get density)
    Assert.True(second.Get compact)

// ---- Built in: the theme picked, and the window's size (DESIGN.md, "Common app values and stored values") ----

let private sea = Theme.dark [ Theme.accent (Scribble.Rendering.Color.Hex 0x00A0A0) ]
let private sand = Theme.light [ Theme.accent (Scribble.Rendering.Color.Hex 0xA08000) ]

let private themed (s: MemoryValueStore) (extra: AppSetting<unit> list) =
    let _, _, app = start ([ App.valueStore (fun _ -> s :> IValueStore); App.themes [ "Sea", sea; "Sand", sand ]; App.view (Border.create []) ] @ extra)
    (app :?> ElementHost).AppValues

[<Fact>]
let ``an app starts in its first theme, and in the one picked last time`` () =
    let s = MemoryValueStore()
    let values = themed s []
    Assert.Same(sea, values.Get Theme.current)
    // The name reads as the first theme's, and starting stored nothing.
    Assert.Equal("Sea", values.Get Theme.picked)
    Assert.Empty s.Values
    values.Set(Theme.picked, "Sand")
    Assert.Same(sand, values.Get Theme.current)
    Assert.Equal("Sand", s.Values["Scribble.Theme"])
    // The next run.
    let next = themed s []
    Assert.Same(sand, next.Get Theme.current)
    Assert.Equal("Sand", next.Get Theme.picked)
    // A name the app doesn't have changes nothing.
    next.Set(Theme.picked, "Fog")
    Assert.Same(sand, next.Get Theme.current)

[<Fact>]
let ``a stored theme name the app no longer has gives the first theme and stays stored`` () =
    let s = MemoryValueStore()
    s.Values["Scribble.Theme"] <- "Fog"
    let values = themed s []
    Assert.Same(sea, values.Get Theme.current)
    Assert.Equal("Sea", values.Get Theme.picked)
    Assert.Equal("Fog", s.Values["Scribble.Theme"])
    values.Set(Theme.picked, "Sand")
    Assert.Equal("Sand", s.Values["Scribble.Theme"])

[<Fact>]
let ``App.rememberTheme false switches the theme and keeps nothing`` () =
    let s = MemoryValueStore()
    s.Values["Scribble.Theme"] <- "Sand"
    let values = themed s [ App.rememberTheme false ]
    // What is stored is not read, and a pick is not written.
    Assert.Same(sea, values.Get Theme.current)
    values.Set(Theme.picked, "Sand")
    Assert.Same(sand, values.Get Theme.current)
    values.Set(Theme.picked, "Sea")
    Assert.Equal("Sand", s.Values["Scribble.Theme"])

[<Fact>]
let ``an app that names no themes has the built-in pair; App.theme alone has that theme only`` () =
    let s = MemoryValueStore()
    let _, _, app = start [ App.valueStore (fun _ -> s :> IValueStore); App.view (Border.create []) ]
    let values = (app :?> ElementHost).AppValues
    let dark = values.Get Theme.current
    Assert.Equal("Dark", values.Get Theme.picked)
    values.Set(Theme.picked, "Light")
    Assert.NotSame(dark, values.Get Theme.current)
    Assert.Equal("Light", s.Values["Scribble.Theme"])
    values.Set(Theme.picked, "Dark")
    Assert.Same(dark, values.Get Theme.current)
    // App.theme: one theme, whatever is stored or picked.
    let _, _, app = start [ App.valueStore (fun _ -> s :> IValueStore); App.theme sand; App.view (Border.create []) ]
    let values = (app :?> ElementHost).AppValues
    Assert.Same(sand, values.Get Theme.current)
    values.Set(Theme.picked, "Light")
    Assert.Same(sand, values.Get Theme.current)

[<Fact>]
let ``an app with no store and no stored values of its own hears no warning about a store`` () =
    let warnings = warningsWhile "no store for them" (fun () -> start [ App.view (Border.create []) ] |> ignore)
    Assert.Empty warnings

let private widths = List<float>()

let private sized =
    Component.define "Sized" (fun ctx () ->
        let size = ctx.useAppValue Window.size
        widths.Add size.Current.Width
        Border.create [])

[<Fact>]
let ``Window.size is the window's size in DIPs, and a component that uses it runs again on a resize`` () =
    widths.Clear()
    let w, _, app = start [ App.view (sized ()) ]
    let values = (app :?> ElementHost).AppValues
    w.DeliverFrame() |> ignore
    Assert.Equal(Size.Create(400.0, 300.0), values.Get Window.size)
    Assert.Equal<float list>([ 400.0 ], List.ofSeq widths)
    w.Size <- { Width = 640; Height = 480 }
    w.Send(Resized w.Size)
    w.DeliverFrame() |> ignore
    Assert.Equal(Size.Create(640.0, 480.0), values.Get Window.size)
    Assert.Equal<float list>([ 400.0; 640.0 ], List.ofSeq widths)
    // The same size again: nothing runs.
    w.Send(Resized w.Size)
    w.DeliverFrame() |> ignore
    Assert.Equal(2, widths.Count)

// ---- The Gallery keeps its theme ----

[<Fact>]
let ``the Gallery starts in the theme it was left in`` () =
    let themeOf (s: MemoryValueStore) =
        let _, _, app = start (Scribble.Samples.GalleryApp.app @ [ App.valueStore (fun name -> Assert.Equal("Gallery", name); s :> IValueStore) ])
        (app :?> ElementHost).AppValues.Get Theme.current
    Assert.Same(Scribble.Samples.GalleryApp.darkTheme, themeOf (MemoryValueStore()))
    let pink = MemoryValueStore()
    pink.Values["Scribble.Theme"] <- "Pink"
    Assert.Same(Scribble.Samples.GalleryApp.pinkTheme, themeOf pink)
