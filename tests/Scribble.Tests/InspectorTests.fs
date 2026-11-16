/// M6 step 2: the inspector (panel, tree, pick mode, keys) on the fake platform.
module Scribble.Tests.InspectorTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Inspector
open Scribble.Tests.AppTests

let private ctrlShift = Modifiers.Control ||| Modifiers.Shift
let private keyWith (w: FakeWindow) k mods = w.Send(KeyDown(k, mods, false)); w.Send(KeyUp(k, mods))
let private move (w: FakeWindow) x y = w.Send(PointerMoved(Mouse, 0, { X = x; Y = y }, Modifiers.None))
let private click (w: FakeWindow) x y =
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))

/// Delivers frames until nothing asks for another.
let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 10 do
        n <- n + 1

let rec private find (name: string) (e: Element) : Element option =
    if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)

/// Every visible TextBlock's text in a layer, in tree order.
let private texts (layer: Layer) =
    let rec collect (e: Element) =
        [ if e.IsVisible then
              if e.Type.Name = "TextBlock" then e.Get TextProps.text
              for c in e.Children do
                  yield! collect c ]
    match layer.Tree.Root with
    | ValueSome r -> collect r
    | ValueNone -> []

/// A 900×500 app with a 100×40 button at the top left, clear of the panel (x 512–892).
let private start (log: List<string>) =
    let w, _, app =
        start [
            App.view (
                Border.create [
                    Border.name "background"
                    Border.onClick (fun _ -> log.Add "app")
                    Border.child (
                        Button.create [
                            Button.name "appButton"
                            Button.width 100.0
                            Button.height 40.0
                            Button.horizontalAlignment HorizontalAlignment.Left
                            Button.verticalAlignment VerticalAlignment.Top
                            Button.onClick (fun _ -> log.Add "appButton")
                        ]
                    )
                ]
            )
            App.layer [ Layer.name "Toasts" ]
            Inspector.enable []
        ]
    let host = app :?> ElementHost
    w.Size <- { Width = 900; Height = 500 }
    w.Send(Resized w.Size)
    settle w
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    w, host, panel

[<Fact>]
let ``F12 shows and hides the panel, and the app's layout never changes`` () =
    let log = List<string>()
    let w, host, panel = start log
    let appButton = host.App.Tree.Root.Value |> find "appButton" |> Option.get
    let before = appButton.Bounds
    Assert.False panel.Tree.HasVisibleContent
    keyWith w (Key.F 12) Modifiers.None
    settle w
    Assert.True panel.Tree.HasVisibleContent
    Assert.Equal(before, appButton.Bounds)
    let shown = texts panel
    Assert.Contains("Inspector", shown)
    Assert.Contains("Layer App", shown)
    Assert.Contains("Layer Toasts", shown)
    Assert.DoesNotContain("Layer Inspector", shown) // not its own layers
    Assert.Contains("#appButton", shown) // the root starts expanded
    keyWith w (Key.F 12) Modifiers.None
    settle w
    Assert.False panel.Tree.HasVisibleContent

[<Fact>]
let ``its keys are claimed from the platform and reach no layer`` () =
    let log = List<string>()
    let w, host, _ = start log
    Assert.Contains((Key.F 12, Modifiers.None), w.Claimed)
    Assert.Contains((Key.Char 'c', ctrlShift), w.Claimed)
    let mutable seen = 0
    host.App.Update(Border.create [ Border.onKeyDown (fun _ -> seen <- seen + 1) ])
    settle w
    keyWith w (Key.F 12) Modifiers.None
    Assert.Equal(0, seen)

[<Fact>]
let ``pick mode selects what's clicked, and the click doesn't reach the app`` () =
    let log = List<string>()
    let w, host, panel = start log
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    Assert.True panel.Tree.HasVisibleContent // pick mode opens the panel
    move w 50.0 20.0
    settle w
    click w 50.0 20.0
    settle w
    Assert.Empty log
    let footer = texts panel |> List.last
    Assert.StartsWith("Button", footer)
    // Pick mode is over: the next click is the app's.
    click w 50.0 20.0
    Assert.Equal<string list>([ "appButton" ], List.ofSeq log)
    ignore host

[<Fact>]
let ``Escape leaves pick mode without selecting`` () =
    let log = List<string>()
    let w, _, panel = start log
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    keyWith w Key.Escape Modifiers.None
    settle w
    click w 50.0 20.0
    Assert.Equal<string list>([ "appButton" ], List.ofSeq log)
    Assert.StartsWith("Click a row", texts panel |> List.last)

[<Fact>]
let ``the panel takes the pointer only where it is`` () =
    let log = List<string>()
    let w, _, _ = start log
    keyWith w (Key.F 12) Modifiers.None
    settle w
    click w 700.0 450.0 // on the panel
    click w 300.0 450.0 // left of it: the app
    Assert.Equal<string list>([ "app" ], List.ofSeq log)

// --- Panel modes (step 2b) ---------------------------------------------------------------------------

let private press (w: FakeWindow) x y = w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private release (w: FakeWindow) x y = w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))

/// A drag the way it happens on screen: frames are drawn between the moves (so the panel is
/// rebuilt while the button is down), and the pointer runs ahead of the panel, off what it pressed.
let private dragFrom (w: FakeWindow) (x, y) (toX, toY) =
    move w x y
    press w x y
    settle w
    for i in 1..4 do
        let t = float i / 4.0
        move w (x + (toX - x) * t) (y + (toY - y) * t)
        settle w
    release w toX toY
    settle w

/// The panel's Border (under the component's host element).
let private panelBox (panel: Layer) = panel.Tree.Root.Value.Children[0]

/// Clicks the inspector button labelled `label`.
let private clickButton (w: FakeWindow) (layer: Layer) (label: string) =
    let rec findText (e: Element) =
        if e.IsVisible && e.Type.Name = "TextBlock" && e.Get TextProps.text = label then Some e
        else e.Children |> Seq.tryPick findText
    let t = layer.Tree.Root.Value |> findText |> Option.get
    let p = t.PositionInTree
    click w (p.X + t.Bounds.Width / 2.0) (p.Y + t.Bounds.Height / 2.0)
    settle w

let private openPanel (w: FakeWindow) =
    keyWith w (Key.F 12) Modifiers.None
    settle w

[<Fact>]
let ``dragging the title undocks the panel, and the app gets back what it covered`` () =
    let log = List<string>()
    let w, _, panel = start log
    openPanel w
    Assert.Equal(512.0, (panelBox panel).Bounds.X)
    dragFrom w (540.0, 25.0) (440.0, 125.0)
    let b = (panelBox panel).Bounds
    Assert.Equal((412.0, 108.0, 380.0, 300.0), (b.X, b.Y, b.Width, b.Height))
    click w 850.0 450.0
    Assert.Equal<string list>([ "app" ], List.ofSeq log)

[<Fact>]
let ``a floating panel stays inside the window`` () =
    let log = List<string>()
    let w, _, panel = start log
    openPanel w
    dragFrom w (540.0, 25.0) (-400.0, 900.0)
    let b = (panelBox panel).Bounds
    Assert.Equal((0.0, 200.0), (b.X, b.Y))

[<Fact>]
let ``a double click on the title collapses and expands the panel`` () =
    let log = List<string>()
    let w, _, panel = start log
    openPanel w
    click w 540.0 25.0
    click w 540.0 25.0
    settle w
    Assert.True((panelBox panel).Bounds.Height < 60.0)
    Assert.DoesNotContain("Layer App", texts panel)
    clickButton w panel "+"
    Assert.True((panelBox panel).Bounds.Height > 400.0)

[<Fact>]
let ``dragging a docked panel's inner edge changes its width`` () =
    let log = List<string>()
    let w, _, panel = start log
    openPanel w
    dragFrom w (515.0, 200.0) (575.0, 200.0)
    let b = (panelBox panel).Bounds
    Assert.Equal((572.0, 320.0), (b.X, b.Width))

[<Fact>]
let ``the corner grip resizes a floating panel, and Right docks it again`` () =
    let log = List<string>()
    let w, _, panel = start log
    openPanel w
    dragFrom w (540.0, 25.0) (440.0, 125.0) // floating at 412, 108, 380 × 300
    dragFrom w (784.0, 400.0) (824.0, 420.0)
    let b = (panelBox panel).Bounds
    Assert.Equal((420.0, 320.0), (b.Width, b.Height))
    clickButton w panel "Right"
    Assert.Equal(512.0, (panelBox panel).Bounds.X)
    Assert.DoesNotContain("Pop out", texts panel) // the fake platform has one window

/// A platform that can open several (fake) windows.
type private ManyWindows() =
    member val Windows = List<FakeWindow>()
    member val Options = List<WindowOptions>()
    interface IPlatform with
        member _.Name = "many"
        member _.SupportsMultipleWindows = true
        member this.CreateWindow(options, onEvent) =
            let w = new FakeWindow(onEvent)
            w.TitleText <- options.Title
            this.Options.Add options
            this.Windows.Add w
            w :> IWindow
        member _.SetWakeHandler _ = ()
        member _.Wake() = ()
        member _.Run() = ()
        member _.Dispose() = ()

[<Fact>]
let ``Pop out moves the panel into a window of its own, which closes with the app`` () =
    let platform = new ManyWindows()
    let app =
        App.start (platform, new FakeDevice(), new Scribble.Tests.TestShaping.FixedShaper(), [ App.title "Test"; App.view (Border.create []); Inspector.enable [] ])
    let host = app :?> ElementHost
    let w = platform.Windows[0]
    settle w
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    openPanel w
    clickButton w panel "Pop out"
    Assert.Equal(2, platform.Windows.Count)
    let popped = platform.Windows[1]
    Assert.Equal("Inspector – Test", popped.TitleText)
    // Owned by the app's window, so it stays above it.
    Assert.True(platform.Options[1].Owner |> ValueOption.exists (fun o -> obj.ReferenceEquals(o, w)))
    Assert.True(platform.Options[0].Owner.IsNone)
    Assert.True(popped.DeliverFrame())
    Assert.False panel.Tree.HasVisibleContent // not in the app's window any more
    // Closing it puts the panel back.
    popped.Send CloseRequested
    settle w
    Assert.True popped.Closed
    Assert.True panel.Tree.HasVisibleContent
    // Popped out again, it closes with the app.
    clickButton w panel "Pop out"
    let again = platform.Windows[2]
    w.Send CloseRequested
    Assert.True again.Closed
