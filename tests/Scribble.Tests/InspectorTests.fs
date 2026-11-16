/// M6: the inspector (panel, tree, pick mode, keys, properties, stats) on the fake platform.
module Scribble.Tests.InspectorTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
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
    let shown = texts panel
    Assert.Contains("PROPERTIES", shown) // the button is selected
    Assert.Contains("100px", shown) // its width, set by the view
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
    Assert.Contains(texts panel, fun t -> t.StartsWith "Click a row")

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

// --- Properties, hooks, formatting and frame stats (step 3) -----------------------------------------

let private pickAppButton (w: FakeWindow) =
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    move w 50.0 20.0
    settle w
    click w 50.0 20.0
    settle w

/// The value and source shown after a property's name in the panel.
let private shownFor (panel: Layer) (name: string) =
    let shown = texts panel
    let i = shown |> List.findIndexBack ((=) name)
    shown[i + 1], shown[i + 2]

[<Fact>]
let ``Element.Values says where each value comes from`` () =
    let t = ElementTree(Scribble.Tests.TestShaping.textSystem ())
    t.Update(Button.create [ Button.width 100.0 ])
    let e = t.Root.Value
    e.Set(LayoutProps.isEnabled, false)
    let source (p: Prop) = (e.Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, p))).Source
    Assert.Equal(ValueSource.View, source LayoutProps.width)
    Assert.Equal(ValueSource.Code, source LayoutProps.isEnabled)
    Assert.Equal(ValueSource.TypeDefault, source BorderProps.background)
    Assert.Equal(ValueSource.Default, source LayoutProps.margin)
    // Private data and the handlers left at their defaults aren't listed.
    Assert.DoesNotContain(e.Values, fun v -> obj.ReferenceEquals(v.Prop, LayoutProps.onPointerMove))
    // A setting the next view leaves out goes back to its default, and says so.
    t.Update(Button.create [])
    Assert.Equal(ValueSource.Default, source LayoutProps.width)

[<Fact>]
let ``Element.Hooks shows each hook's current value`` () =
    let mutable count: State<int> voption = ValueNone
    let probe =
        Component.define "Probe" (fun ctx () ->
            let c = ctx.useState 3
            count <- ValueSome c
            let _model, _ = ctx.useElmish(10, fun (msg: int) model -> model + msg)
            let _ = ctx.useMemo(2, fun d -> d * 2)
            ctx.useEffect((), ignore)
            Border.create [])
    let t = ElementTree(Scribble.Tests.TestShaping.textSystem ())
    t.Update(probe ())
    t.UpdateLayout(Size.Create(100.0, 100.0))
    t.RunAfterFrame()
    let hooks () = t.Root.Value.Hooks |> List.map (fun h -> h.Kind, h.Value |> ValueOption.map unbox<int>)
    Assert.Equal<(string * int voption) list>([ "useState", ValueSome 3; "useElmish", ValueSome 10; "useMemo", ValueSome 4; "useEffect", ValueNone ], hooks ())
    count.Value.Set 5
    t.UpdateLayout(Size.Create(100.0, 100.0))
    Assert.Equal(ValueSome 5, snd (hooks ()).Head)
    Assert.Empty(t.Root.Value.Children[0].Hooks) // the Border isn't a component

type private Model = { Title: string; Count: int }

[<Fact>]
let ``values are written by our own code, and the app can format its own types`` () =
    let f = Format.value (fun _ -> ValueNone)
    Assert.Equal("#3D59A1", f (box (Color.Hex 0x3D59A1)))
    Assert.Equal("transparent", f (box Color.Transparent))
    Assert.Equal("12, 6", f (box (Thickness.Create(12.0, 6.0, 12.0, 6.0))))
    Assert.Equal("0", f (box Thickness.Zero))
    Assert.Equal("2*", f (box (Star 2.0)))
    Assert.Equal("left", f (box HorizontalAlignment.Left))
    Assert.Equal("horizontal", f (box Horizontal))
    Assert.Equal("1.5", f (box 1.5))
    Assert.Equal("any × 20", Format.sizeOf (Size.Create(Double.PositiveInfinity, 20.0)))
    Assert.Equal("\"hi\"", f (box "hi"))
    Assert.Equal("handler", f (box (ValueSome(fun (_: ClickEventArgs) -> ()))))
    Assert.Equal("none", f (box (ValueNone: (ClickEventArgs -> unit) voption)))
    Assert.Equal("handler", f (box (fun (x: int) -> x + 1)))
    Assert.Equal("[4: 1, 2, 3, …]", f (box [ 1; 2; 3; 4 ]))
    Assert.Equal("2 children", f (box [ Border.create []; Border.create [] ]))
    // A record (or union) of the app's: its type name, never ToString() (%A, not NativeAOT-safe).
    Assert.Equal("Model", f (box { Title = "a"; Count = 1 }))
    Assert.Equal("[2: \"primary\", \"card\"]", f (box [ "primary"; "card" ]))
    Assert.Equal("[1: Button.x:hover]", f (box [ Button.style [ Style.cls "x"; States.hover ] [] ]))
    let custom (v: obj) =
        match v with
        | :? Model as m -> ValueSome(m.Title + " (" + m.Count.ToString() + ")")
        | _ -> ValueNone
    Assert.Equal("a (1)", Format.value custom (box { Title = "a"; Count = 1 }))
    Assert.Equal("[1: a (1)]", Format.value custom (box [ { Title = "a"; Count = 1 } ]))

[<Fact>]
let ``the properties show the selection's values and sources, defaults only when asked`` () =
    let log = List<string>()
    let w, _, panel = start log
    pickAppButton w
    Assert.Equal(("100px", "view"), shownFor panel "width")
    Assert.Equal(("left", "view"), shownFor panel "horizontalAlignment")
    Assert.Equal(("handler", "view"), shownFor panel "onClick")
    Assert.Equal(("#3A3A55", "theme"), shownFor panel "background") // the theme's Button look
    Assert.Contains("from Button", texts panel)
    // What the theme can set shows even at its default (with no line under it until a theme style applies).
    Assert.Equal(("transparent", "default"), shownFor panel "overlay")
    Assert.DoesNotContain(texts panel, fun t -> t.StartsWith "theme sets it") // no line under it until a theme style applies
    Assert.DoesNotContain("margin", texts panel) // a property default: hidden at first
    clickButton w panel "Show defaults"
    Assert.Equal(("0", "default"), shownFor panel "margin")
    clickButton w panel "Show defaults"
    Assert.DoesNotContain("margin", texts panel)
    // The states line lists every built-in state as a toggle.
    for s in [ "hover"; "pressed"; "focus"; "focus-visible"; "disabled" ] do
        Assert.Contains(s, texts panel)
    Assert.Contains(texts panel, fun t -> t.Contains "shapes") // frame stats

[<Fact>]
let ``every state is listed, off ones too, and a long states section wraps`` () =
    // Custom states are global: every element's STATES section lists them, so these are removed
    // again at the end (or they'd crowd the other tests' panels).
    let longStates = [ for i in 1 .. 6 -> States.define ("a-rather-long-custom-state-" + i.ToString()) (fun _ -> false) ]
    try
        let log = List<string>()
        let w, _, panel = start log
        pickAppButton w
        // The states section: the row of toggles under the STATES heading.
        let rec statesRow (e: Element) =
            if e.Type.Name = "InspectorWrapRow" then Some e
            else e.Children |> Seq.tryPick statesRow
        let row = panel.Tree.Root.Value |> statesRow |> Option.get
        // Off, yet listed (so they can be forced).
        for i in 1 .. 6 do
            Assert.Contains("a-rather-long-custom-state-" + i.ToString(), texts panel)
        // Several lines, within the panel.
        Assert.True(row.Bounds.Height > 60.0)
        Assert.True(row.Bounds.Right <= (panelBox panel).Bounds.Width)
    finally
        for s in longStates do
            States.remove s

[<Fact>]
let ``a state toggle forces the state's look until it's cleared or the inspector closes`` () =
    let log = List<string>()
    let w, host, panel = start log
    pickAppButton w
    let button = (find "appButton" host.Tree.Root.Value).Value
    Assert.Equal(Color.Transparent, button.Get BorderProps.overlay)
    clickButton w panel "hover"
    Assert.True(button.ForcedStates |> List.exists (fun s -> obj.ReferenceEquals(s, States.hover)))
    Assert.False button.IsPointerOver // looks only: input still sees the real state
    Assert.DoesNotContain("hover (forced)", texts panel) // shown by color, not words
    Assert.Contains("• forced", texts panel) // marked in the tree
    let overlay = button.Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, BorderProps.overlay))
    Assert.Equal(ValueSource.Theme, overlay.Source) // the hover look applies (fading in)
    Assert.Equal("Button:hover", overlay.Selector)
    clickButton w panel "hover" // toggles it off again
    Assert.Empty(button.ForcedStates)
    clickButton w panel "pressed"
    Assert.True(button.ActiveStates |> List.exists (fun s -> obj.ReferenceEquals(s, States.pressed)))
    clickButton w panel "Clear forced"
    Assert.Empty(button.ForcedStates)
    Assert.DoesNotContain("Clear forced", texts panel)
    // Closing the inspector clears what's forced.
    clickButton w panel "disabled"
    Assert.Equal(0.5, button.Get LayoutProps.opacity)
    openPanel w // F12 closes it
    Assert.Empty(button.ForcedStates)
    Assert.Equal(1.0, button.Get LayoutProps.opacity)

[<Fact>]
let ``Deselect, or clicking the selected row again, clears the selection`` () =
    let log = List<string>()
    let w, _, panel = start log
    let hint = "Click a row, or Pick"
    let showsHint () = texts panel |> List.exists (fun t -> t.StartsWith hint)
    pickAppButton w
    Assert.False(showsHint ())
    clickButton w panel "Deselect"
    Assert.True(showsHint ())
    Assert.DoesNotContain("Deselect", texts panel)
    clickButton w panel "#appButton" // its tree row: selects
    Assert.False(showsHint ())
    clickButton w panel "#appButton" // again: deselects
    Assert.True(showsHint ())
    Assert.Empty(log) // none of it reached the app

[<Fact>]
let ``with the inspector open and an element selected, an idle app draws nothing`` () =
    let log = List<string>()
    let w, _, _ = start log
    pickAppButton w
    settle w
    Assert.False(w.DeliverFrame())
    Assert.False(w.FrameRequested)

[<Fact>]
let ``dragging the divider gives the properties more room`` () =
    let log = List<string>()
    let w, _, panel = start log
    openPanel w
    let rec findDivider (e: Element) =
        if e.Get LayoutProps.cursor = ValueSome Cursor.ResizeUpDown then Some e
        else e.Children |> Seq.tryPick findDivider
    let divider = panel.Tree.Root.Value |> findDivider |> Option.get
    let properties () =
        let siblings = divider.Parent.Value.Children
        let i = Seq.findIndex (fun c -> obj.ReferenceEquals(c, divider)) siblings
        siblings[i + 1].Bounds.Height
    Assert.Equal(300.0, properties ())
    let p = divider.PositionInTree
    // Down makes them shorter.
    dragFrom w (p.X + 100.0, p.Y + 3.0) (p.X + 100.0, p.Y + 53.0)
    Assert.Equal(250.0, properties ())
    // Never so tall that the tree disappears: the window is 500 high.
    dragFrom w (p.X + 100.0, p.Y + 53.0) (p.X + 100.0, 0.0)
    Assert.Equal(484.0 - 170.0, properties ())

[<Fact>]
let ``the selection is only outlined, so its own looks stay visible; something pointed at is filled`` () =
    let log = List<string>()
    let w, host, _ = start log
    let highlight () =
        let layer = host.Layers |> Seq.find (fun l -> l.Name = "Inspector highlight")
        let rec find (e: Element) =
            if obj.ReferenceEquals(e.Type, Highlight.Type) then Some e else e.Children |> Seq.tryPick find
        layer.Tree.Root.Value |> find |> Option.get
    // Picking: whatever is under the pointer gets the full highlight.
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    move w 50.0 20.0
    settle w
    Assert.False((highlight ()).Get HighlightProps.outlineOnly)
    // Picked: the selection gets just an outline.
    click w 50.0 20.0
    settle w
    Assert.True((highlight ()).Get HighlightProps.outlineOnly)
