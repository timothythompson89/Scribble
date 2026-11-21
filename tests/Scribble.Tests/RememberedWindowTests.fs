/// A remembered window (DESIGN.md, "A remembered window"): what `App.rememberWindow` stores as the window moves,
/// resizes, is maximized, minimized and closed, what the next run opens with, and where a remembered window opens
/// on the displays there are now. The fake window says where it is; a real window's own events are the platform's.
module Scribble.Tests.RememberedWindowTests

open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests

let private app (store: MemoryValueStore) (extra: AppSetting<unit> list) =
    let w, _, app = start ([ App.valueStore (fun _ -> store :> IValueStore); App.rememberWindow true; App.view (Border.create []) ] @ extra)
    w.DeliverFrame() |> ignore
    w, (app :?> ElementHost).AppValues

let private resize (w: FakeWindow) (width: int) (height: int) =
    w.Size <- { Width = width; Height = height }
    w.Send(Resized w.Size)

let private move (w: FakeWindow) (x: int) (y: int) =
    w.Position <- struct (x, y)
    w.Send Moved

/// What the store holds of the window, as text; "-" for nothing (a value never set away from its default).
let private stored (s: MemoryValueStore) =
    [ for name in [ "X"; "Y"; "Width"; "Height"; "Maximized" ] ->
          match s.Values.TryGetValue("Scribble.Window." + name) with
          | true, v -> v
          | _ -> "-" ]

[<Fact>]
let ``the first run opens as App.size says, at the platform's place, and stores nothing until something changes`` () =
    let s = MemoryValueStore()
    let w, _ = app s [ App.size (800.0, 500.0) ]
    Assert.Equal<DipSize>(({ Width = 800.0; Height = 500.0 }: DipSize), w.Options.Size)
    Assert.Equal(ValueNone, w.Options.Position)
    Assert.False w.Options.Maximized
    Assert.Equal<string list>([ "-"; "-"; "-"; "-"; "-" ], stored s)

[<Fact>]
let ``a moved and resized window opens there next time, whatever App.size says`` () =
    let s = MemoryValueStore()
    let w, _ = app s [ App.size (800.0, 500.0) ]
    move w 320 240
    resize w 640 480
    Assert.Equal<string list>([ "320"; "240"; "640"; "480"; "-" ], stored s)
    let next, _ = app s [ App.size (800.0, 500.0) ]
    Assert.Equal<DipSize>(({ Width = 640.0; Height = 480.0 }: DipSize), next.Options.Size)
    Assert.Equal(ValueSome(struct (320, 240)), next.Options.Position)
    Assert.False next.Options.Maximized

[<Fact>]
let ``a maximized window keeps the size and place it returns to`` () =
    let s = MemoryValueStore()
    let w, _ = app s []
    move w 50 60
    resize w 700 400
    // Maximized: the platform says so, and the window is now as big as the screen.
    w.Maximized <- true
    w.Position <- struct (0, 0)
    w.Send Moved
    resize w 1920 1040
    Assert.Equal<string list>([ "50"; "60"; "700"; "400"; "true" ], stored s)
    let next, _ = app s []
    Assert.True next.Options.Maximized
    Assert.Equal<DipSize>(({ Width = 700.0; Height = 400.0 }: DipSize), next.Options.Size)
    Assert.Equal(ValueSome(struct (50, 60)), next.Options.Position)
    // Restored again, somewhere else: that is its normal place now.
    next.Maximized <- false
    resize next 710 410
    move next 55 65
    Assert.Equal<string list>([ "55"; "65"; "710"; "410"; "false" ], stored s)

[<Fact>]
let ``a minimized window keeps its normal place and is never reopened minimized`` () =
    let s = MemoryValueStore()
    let w, _ = app s []
    move w 200 150
    resize w 600 450
    // Minimized: no client area, and parked far off the screen.
    w.Minimized <- true
    w.Position <- struct (-32000, -32000)
    w.Send Moved
    resize w 0 0
    w.Send CloseRequested
    Assert.Equal<string list>([ "200"; "150"; "600"; "450"; "-" ], stored s)
    // A platform that says nothing of being minimized but parks the window: still not a place to return to.
    let s = MemoryValueStore()
    let w, _ = app s []
    move w 200 150
    move w -32000 -32000
    Assert.Equal("200", s.Values["Scribble.Window.X"])

[<Fact>]
let ``closing stores where the window is, and saves it`` () =
    let s = MemoryValueStore()
    let w, _ = app s []
    // Nothing moved or resized since it opened: the first run's place is still worth keeping.
    w.Send CloseRequested
    Assert.Equal<string list>([ "100"; "100"; "400"; "300"; "-" ], stored s)
    Assert.Equal(1, s.Flushes)

[<Fact>]
let ``a drag is saved once, half a second after it ends`` () =
    let s = MemoryValueStore()
    let w, _ = app s []
    for i in 1..20 do
        move w (100 + i * 10) 100
        w.Time.AdvanceMs 50.0
    Assert.Equal(0, s.Flushes)
    Assert.Equal("300", s.Values["Scribble.Window.X"])
    w.Time.AdvanceMs 1000.0
    Assert.Equal(1, s.Flushes)

[<Fact>]
let ``without App.rememberWindow nothing is stored and nothing stored is used`` () =
    let s = MemoryValueStore()
    s.Values["Scribble.Window.X"] <- "10"
    s.Values["Scribble.Window.Y"] <- "20"
    s.Values["Scribble.Window.Width"] <- "500"
    s.Values["Scribble.Window.Height"] <- "400"
    s.Values["Scribble.Window.Maximized"] <- "true"
    let w, _, _ = start [ App.valueStore (fun _ -> s :> IValueStore); App.size (800.0, 500.0); App.view (Border.create []) ]
    w.DeliverFrame() |> ignore
    Assert.Equal<DipSize>(({ Width = 800.0; Height = 500.0 }: DipSize), w.Options.Size)
    Assert.Equal(ValueNone, w.Options.Position)
    Assert.False w.Options.Maximized
    move w 300 300
    resize w 640 480
    w.Send CloseRequested
    Assert.Equal<string list>([ "10"; "20"; "500"; "400"; "true" ], stored s)

[<Fact>]
let ``stored nonsense gives the app's own size and the platform's place`` () =
    let s = MemoryValueStore()
    s.Values["Scribble.Window.X"] <- "left"
    s.Values["Scribble.Window.Y"] <- "20"
    s.Values["Scribble.Window.Width"] <- "3"
    s.Values["Scribble.Window.Height"] <- "400"
    let w, _ = app s [ App.size (800.0, 500.0) ]
    Assert.Equal<DipSize>(({ Width = 800.0; Height = 500.0 }: DipSize), w.Options.Size)
    Assert.Equal(ValueNone, w.Options.Position)

// ---- Where a remembered window opens on the displays there are now ----

let private primary: ScreenRect = { X = 0; Y = 0; Width = 1920; Height = 1040 }
let private right: ScreenRect = { X = 1920; Y = 0; Width = 2560; Height = 1400 }
let private above: ScreenRect = { X = 0; Y = -1080; Width = 1920; Height = 1040 }

[<Fact>]
let ``a window still on a display opens where it was`` () =
    let fit = WindowPlacement.fit [ primary; right ]
    Assert.Equal((ValueSome(struct (100, 100)), struct (800, 600)), fit (struct (100, 100)) (struct (800, 600)))
    // On the second display, and one that hangs over the edge with its title bar still reachable.
    Assert.Equal((ValueSome(struct (2500, 300)), struct (800, 600)), fit (struct (2500, 300)) (struct (800, 600)))
    Assert.Equal((ValueSome(struct (-700, 500)), struct (800, 600)), fit (struct (-700, 500)) (struct (800, 600)))
    // A display above the primary one has negative coordinates.
    Assert.Equal((ValueSome(struct (50, -900)), struct (800, 600)), WindowPlacement.fit [ primary; above ] (struct (50, -900)) (struct (800, 600)))

[<Fact>]
let ``a window whose display is gone opens at the platform's place, no larger than the primary display`` () =
    // It was on the right-hand display, which is unplugged.
    Assert.Equal((ValueNone, struct (800, 600)), WindowPlacement.fit [ primary ] (struct (2500, 300)) (struct (800, 600)))
    Assert.Equal((ValueNone, struct (1920, 976)), WindowPlacement.fit [ primary ] (struct (2500, 300)) (struct (2400, 1300)))
    // Too little of the top edge left to grab, above the top, or below the bottom.
    Assert.Equal((ValueNone, struct (800, 600)), WindowPlacement.fit [ primary ] (struct (-760, 500)) (struct (800, 600)))
    Assert.Equal((ValueNone, struct (800, 600)), WindowPlacement.fit [ primary ] (struct (100, -50)) (struct (800, 600)))
    Assert.Equal((ValueNone, struct (800, 600)), WindowPlacement.fit [ primary ] (struct (100, 1000)) (struct (800, 600)))

[<Fact>]
let ``a window larger than its display is shrunk to it, and no displays known changes nothing`` () =
    Assert.Equal((ValueSome(struct (0, 0)), struct (1920, 976)), WindowPlacement.fit [ primary ] (struct (0, 0)) (struct (3000, 2000)))
    Assert.Equal((ValueSome(struct (5000, 5000)), struct (3000, 2000)), WindowPlacement.fit [] (struct (5000, 5000)) (struct (3000, 2000)))
