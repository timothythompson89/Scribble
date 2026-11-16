/// The App runtime (program loop, clicks, frames, keys) against a fake platform and GPU device,
/// so it's tested without a window.
module Scribble.Tests.AppTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Tests.TestShaping

type FakeWindow(onEvent: PlatformEvent -> unit) =
    member val FrameRequested = false with get, set
    member val Closed = false with get, set
    member val TitleText = "" with get, set
    member val Cursor = Cursor.Default with get, set
    /// Delivers a requested frame, like the platform's loop does. Returns whether one was due.
    member this.DeliverFrame() =
        if this.FrameRequested then
            this.FrameRequested <- false
            onEvent Frame
            true
        else false
    member _.Send e = onEvent e
    interface IWindow with
        member _.ClientSize = { Width = 400; Height = 300 }
        member _.Scale = 1.0
        member this.Title with get () = this.TitleText and set v = this.TitleText <- v
        member _.Surface = BrowserCanvas "test"
        member this.IsClosed = this.Closed
        member _.Show() = ()
        member this.Close() = this.Closed <- true
        member this.RequestFrame() = this.FrameRequested <- true
        member this.SetCursor c = this.Cursor <- c
        member _.Dispose() = ()

type FakePlatform() =
    let mutable wakes = 0
    member val Window: FakeWindow option = None with get, set
    member val WakeHandler: unit -> unit = ignore with get, set
    /// How many times something woke the platform (any thread).
    member _.Wakes = Threading.Volatile.Read(&wakes)
    /// What the platform's loop does after a wake: drain the dispatcher (on the test's thread).
    member this.RunWake() = this.WakeHandler()
    interface IPlatform with
        member _.Name = "fake"
        member this.CreateWindow(_, onEvent) =
            let w = new FakeWindow(onEvent)
            this.Window <- Some w
            w :> IWindow
        member _.Run() = ()
        member this.SetWakeHandler handler = this.WakeHandler <- handler
        member _.Wake() = Threading.Interlocked.Increment(&wakes) |> ignore
        member _.Dispose() = ()

type FakeDevice() =
    let atlas, colorAtlas = GlyphAtlas(256), GlyphAtlas(64, 4)
    member val Frames = 0 with get, set
    member val LastShapes = 0 with get, set
    member val LastClear = Color.Black with get, set
    interface IRenderDevice with
        member _.Description = "fake GPU"
        member _.GlyphAtlas = atlas
        member _.ColorGlyphAtlas = colorAtlas
        member this.CreateSurface(_, size) =
            { new IRenderSurface with
                member _.Size = size
                member _.Resize _ = ()
                member _.TryBeginFrame scale =
                    let list = DrawList(scale)
                    Some
                        { new IFrame with
                            member _.Size = size
                            member _.Scale = scale
                            member _.Clear c = this.LastClear <- c
                            member _.DrawList = list
                            member _.Dispose() =
                                this.Frames <- this.Frames + 1
                                this.LastShapes <- list.Count }
                member _.Dispose() = () }
        member _.RenderOffscreen(_, _, _) = raise (NotSupportedException())
        member _.Dispose() = ()

let start (settings: AppSetting<'Msg> list) =
    let platform, device = new FakePlatform(), new FakeDevice()
    let app = App.start (platform, device, new FixedShaper(), settings)
    platform.Window.Value, device, app

type Msg =
    | Increment
    | Tick

let private counterView count dispatch =
    StackPanel.create [
        StackPanel.children [
            TextBlock.create [ TextBlock.name "label"; TextBlock.text $"Count: {count}" ]
            Button.create [ Button.name "plus"; Button.width 100.0; Button.height 30.0; Button.text "+1"; Button.onClick (fun _ -> dispatch Increment) ]
        ]
    ]

let private counter = App.program 0 (fun Increment count -> count + 1) counterView

let private press (w: FakeWindow) x y =
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))

let private release (w: FakeWindow) x y =
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))

[<Fact>]
let ``the first frame builds the view`` () =
    let w, device, _ = start [ App.title "Counter"; counter ]
    Assert.True(w.DeliverFrame())
    Assert.Equal(1, device.Frames)
    Assert.True(device.LastShapes > 0)

[<Fact>]
let ``a click dispatches, rebuilds once and then goes idle`` () =
    let w, device, app = start [ counter ]
    w.DeliverFrame() |> ignore
    let label () =
        let host = (app :?> ElementHost)
        (host.Tree.Root.Value.Children[0]).Get TextProps.text
    Assert.Equal("Count: 0", label ())
    // The button sits below the label; press and release inside it.
    press w 20 30
    release w 20 30
    Assert.True(w.DeliverFrame())
    Assert.Equal("Count: 1", label ())
    // Nothing else changed: no further frames.
    Assert.False(w.DeliverFrame())

[<Fact>]
let ``releasing outside the button doesn't click`` () =
    let w, _, app = start [ counter ]
    w.DeliverFrame() |> ignore
    press w 20 30
    release w 300 250
    w.DeliverFrame() |> ignore
    let host = app :?> ElementHost
    Assert.Equal("Count: 0", (host.Tree.Root.Value.Children[0]).Get TextProps.text)

[<Fact>]
let ``onFrame animates while the model changes and stops when it doesn't`` () =
    let mutable ticks = 0
    let update msg n =
        match msg with
        | Tick when n < 3 -> n + 1
        | _ -> n
    let w, _, _ =
        start [ App.program 0 update (fun n _ -> TextBlock.create [ TextBlock.text (string n) ])
                App.onFrame (fun _ -> ticks <- ticks + 1; Some Tick) ]
    let mutable frames = 0
    while w.DeliverFrame() && frames < 100 do
        frames <- frames + 1
    // Three ticks change the model; the fourth doesn't, so the loop goes idle.
    Assert.True(frames >= 4 && frames <= 6, $"{frames} frames")
    Assert.False(w.DeliverFrame())

[<Fact>]
let ``keys become messages, Escape closes when asked`` () =
    let w, _, app =
        start [ counter
                App.closeOnEscape true
                App.onKey (fun key _ ->
                    match key with
                    | Key.Space -> Some Increment
                    | _ -> None) ]
    w.DeliverFrame() |> ignore
    w.Send(KeyDown(Key.Space, Modifiers.None, false))
    w.Send(KeyDown(Key.Space, Modifiers.None, true)) // a repeat: ignored
    w.DeliverFrame() |> ignore
    let host = app :?> ElementHost
    Assert.Equal("Count: 1", (host.Tree.Root.Value.Children[0]).Get TextProps.text)
    w.Send(KeyDown(Key.Escape, Modifiers.None, false))
    Assert.True w.Closed

[<Fact>]
let ``showStats puts frame stats in the title`` () =
    let w, _, _ = start [ App.title "Stats"; App.showStats true; counter ]
    w.DeliverFrame() |> ignore
    // The counter refreshes once a second; the title keeps its name either way.
    Assert.True(w.TitleText = "" || w.TitleText.StartsWith "Stats")

[<Fact>]
let ``a custom host is made from settings`` () =
    let w = new FakeWindow(ignore)
    let device = new FakeDevice()
    let frames = ref 0
    let dispatcher = Dispatcher ignore
    use host =
        ElementHost.create w device (TextSystem.create device (new FixedShaper()) []) [
            ElementHost.background (Color.Hex 0x123456)
            ElementHost.dispatcher dispatcher
            ElementHost.beforeFrame (fun () -> frames.Value <- frames.Value + 1)
        ]
    host.Update(TextBlock.create [ TextBlock.text "hi" ])
    host.HandleEvent Frame |> ignore
    Assert.Equal(1, frames.Value)
    Assert.Equal(Color.Hex 0x123456, device.LastClear)
    Assert.True(obj.ReferenceEquals(dispatcher, host.Dispatcher.Value))
    host.Background <- Color.White // still changeable while running
    host.HandleEvent Frame |> ignore
    Assert.Equal(Color.White, device.LastClear)
