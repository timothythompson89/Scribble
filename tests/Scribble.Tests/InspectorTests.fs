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

/// Every visible TextBlock's text in a layer, in view order.
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
                    Border.content (
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
let ``SEMANTICS shows the selection's node, or why it has none`` () =
    let log = List<string>()
    let w, _, panel = start log
    let pick x y =
        keyWith w (Key.Char 'c') ctrlShift
        settle w
        move w x y
        settle w
        click w x y
        settle w
        texts panel
    let shown = pick 50.0 20.0
    let at = shown |> List.findIndex ((=) "SEMANTICS")
    // The button has no text: a node without a name (what the samples' check catches).
    Assert.Equal<string list>([ "role"; "Button"; "name"; "none" ], shown |> List.skip (at + 1) |> List.take 4)
    Assert.Contains("Click, Focus, ScrollIntoView", shown)
    Assert.Contains("Semantics.role", shown)
    Assert.DoesNotContain("semantics.role", shown) // not under PROPERTIES
    let shown = pick 300.0 300.0
    let at = shown |> List.findIndex ((=) "SEMANTICS")
    Assert.Equal<string list>([ "not exposed"; "no role (what's inside it takes its place)" ], shown |> List.skip (at + 1) |> List.take 2)

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

/// Clicks the inspector element labelled `label` (its text), or a toolbar button by name ("popOut").
let private clickButton (w: FakeWindow) (layer: Layer) (label: string) =
    let rec findText (e: Element) =
        if e.IsVisible && ((e.Type.Name = "TextBlock" && e.Get TextProps.text = label) || e.Name = label) then Some e
        else e.Children |> Seq.tryPick findText
    let t = layer.Tree.Root.Value |> findText |> Option.get
    let p = t.PositionInTree
    click w (p.X + t.Bounds.Width / 2.0) (p.Y + t.Bounds.Height / 2.0)
    settle w

let private openPanel (w: FakeWindow) =
    keyWith w (Key.F 12) Modifiers.None
    settle w

/// Whether the layer shows an element with this name.
let private hasNamed (layer: Layer) (name: string) =
    let rec find (e: Element) = (e.IsVisible && e.Name = name) || e.Children |> Seq.exists find
    find layer.Tree.Root.Value

[<Fact>]
let ``a toolbar button's tooltip says what it does; the title stays`` () =
    let log = List<string>()
    let w, host, panel = start log
    openPanel w
    let tip () = host.Layers |> Seq.tryFind (fun l -> l.Name = "Tooltip") |> Option.map texts
    let rec named (e: Element) = if e.Name = "close" then Some e else e.Children |> Seq.tryPick named
    let close = (named panel.Tree.Root.Value).Value
    let p = close.PositionInTree
    move w (p.X + 14.0) (p.Y + 14.0)
    w.Time.AdvanceMs 500.0
    settle w
    Assert.Equal(Some [ "Close (F12)" ], tip ())
    Assert.Contains("Inspector", texts panel)
    // Above the inspector.
    Assert.Equal("Tooltip", (Seq.last host.Layers).Name)
    move w 100.0 300.0
    settle w
    Assert.Equal(None, tip ())
    // A click hides it (Dock on the left replaces itself with Dock on the right).
    let rec namedLeft (e: Element) = if e.Name = "dockLeft" then Some e else e.Children |> Seq.tryPick namedLeft
    let left = (namedLeft panel.Tree.Root.Value).Value
    let lp = left.PositionInTree
    move w (lp.X + 14.0) (lp.Y + 14.0)
    w.Time.AdvanceMs 500.0
    Assert.Equal(Some [ "Dock left" ], tip ())
    clickButton w panel "dockLeft"
    Assert.Equal(None, tip ())

[<Fact>]
let ``while picking, the title says so wherever the pointer is, until Escape`` () =
    let log = List<string>()
    let w, _, panel = start log
    openPanel w
    keyWith w (Key.Char 'c') ctrlShift
    move w 100.0 300.0 // out in the app
    settle w
    Assert.Contains("Picking… (Esc stops)", texts panel)
    keyWith w Key.Escape Modifiers.None
    settle w
    Assert.Contains("Inspector", texts panel)
    Assert.DoesNotContain("Picking… (Esc stops)", texts panel)

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
    clickButton w panel "fold" // unfolds
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
    clickButton w panel "dockRight"
    Assert.Equal(512.0, (panelBox panel).Bounds.X)
    Assert.False(hasNamed panel "popOut") // the fake platform has one window

/// A platform that can open several (fake) windows.
type private ManyWindows() =
    member val Time = FakeTime()
    member val Windows = List<FakeWindow>()
    member val Options = List<WindowOptions>()
    interface IPlatform with
        member _.Name = "many"
        member _.SupportsMultipleWindows = true
        member this.CreateWindow(options, onEvent) =
            let w = new FakeWindow(onEvent, this.Time)
            w.TitleText <- options.Title
            this.Options.Add options
            this.Windows.Add w
            w :> IWindow
        member _.SetWakeHandler _ = ()
        member _.Wake() = ()
        member this.Elapsed = this.Time.Now
        member this.After(delay, action) = this.Time.After(delay, action)
        member _.Run() = ()
        member _.SaveFile(_, _, _) = Threading.Tasks.Task.FromResult false
        member _.SetClipboardText _ = ()
        member _.GetClipboardText() = Threading.Tasks.Task.FromResult ""
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
    clickButton w panel "popOut"
    Assert.Equal(2, platform.Windows.Count)
    let popped = platform.Windows[1]
    Assert.Equal("Inspector – Test", popped.TitleText)
    // It follows the app's title (`WindowTitle.current`).
    host.AppValues.Set(WindowTitle.current, "Renamed")
    Assert.Equal("Inspector – Renamed", popped.TitleText)
    Assert.Equal("Renamed", w.TitleText)
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
    clickButton w panel "popOut"
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
    // Off the button (to the background, clear of the panel), and until its hover look has faded:
    // the tests then see the button at rest.
    move w 300.0 300.0
    let mutable n = 0
    while w.DeliverFrame() && n < 100 do
        n <- n + 1

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
    Assert.Equal(("#3A3A55", "type"), shownFor panel "background") // Button's look at rest: a type default
    Assert.Contains("Palette.control", texts panel) // the theme value it reads, under the value
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
let ``an element's states are listed, off ones too, and a long states section wraps`` () =
    // Custom states are kept for the whole process, so these are removed again at the end.
    let longStates = [ for i in 1 .. 6 -> States.define ("a-rather-long-custom-state-" + i.ToString()) [] ]
    let elsewhere = States.define "a-state-of-another-element" []
    try
        let log = List<string>()
        let w, host, panel = start log
        // The button has them (switched off); the other state is nothing of its.
        let button = (find "appButton" host.App.Tree.Root.Value).Value
        for s in longStates do
            button.Set(Button.state s false)
        pickAppButton w
        Assert.DoesNotContain("a-state-of-another-element", texts panel)
        // The states section: the row of toggles under the STATES heading.
        let rec statesRow (e: Element) =
            if obj.ReferenceEquals(e.Type, WrapPanel.Type) then Some e
            else e.Children |> Seq.tryPick statesRow
        let row = panel.Tree.Root.Value |> statesRow |> Option.get
        // Off, yet listed (so they can be forced).
        for i in 1 .. 6 do
            Assert.Contains("a-rather-long-custom-state-" + i.ToString(), texts panel)
        // Several lines, within the panel.
        Assert.True(row.Bounds.Height > 60.0)
        Assert.True(row.Bounds.Right <= (panelBox panel).Bounds.Width)
    finally
        States.remove elsewhere
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
    Assert.True(button.ForcedStates |> List.exists (fun (s, on) -> on && obj.ReferenceEquals(s, States.hover)))
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

[<Fact>]
let ``picking follows the app when it moves under a pointer that stands still`` () =
    let log = List<string>()
    let w, host, _ = start log
    let target () =
        let layer = host.Layers |> Seq.find (fun l -> l.Name = "Inspector highlight")
        let rec find (e: Element) =
            if obj.ReferenceEquals(e.Type, Highlight.Type) then Some e else e.Children |> Seq.tryPick find
        let h = layer.Tree.Root.Value |> find |> Option.get
        h.Get HighlightProps.target |> ValueOption.map _.Name
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    move w 50.0 20.0
    settle w
    Assert.Equal(ValueSome "appButton", target ())
    // The button moves away (as a scrolled or recycled row does); the pointer doesn't.
    let button = host.App.Tree.Root.Value |> find "appButton" |> Option.get
    button.Set(LayoutProps.margin, Thickness.Create(0.0, 200.0, 0.0, 0.0))
    settle w
    Assert.Equal(ValueSome "background", target ())
    // Over the panel, nothing is checked again: the last element stays.
    move w 700.0 300.0
    settle w
    button.Set(LayoutProps.margin, Thickness.Zero)
    settle w
    Assert.Equal(ValueSome "background", target ())

/// Browser order: the inspector's delayed tree refresh can run between frames, so pointer moves can
/// arrive while the new rows aren't laid out yet. Once the frame is drawn, the row the inspector shows
/// as hovered must be the one under the pointer.
[<Theory>]
[<InlineData("Components")>]
[<InlineData("Scrolling")>]
[<InlineData("Layout")>]
let ``after a pick expands the tree, the hovered row is the one under the pointer`` (tab: string) =
    let platform, device = new FakePlatform(), new FakeDevice()
    let app = App.start (platform, device, new Scribble.Tests.TestShaping.FixedShaper(), Scribble.Samples.GalleryApp.app @ [ Inspector.enable [] ])
    let w = platform.Window.Value
    let host = app :?> ElementHost
    let size: PixelSize = { Width = 1000; Height = 700 }
    w.Size <- size
    w.Send(Resized size)
    settle w
    let at x y : PixelPoint = { X = x; Y = y }
    let moveTo x y = w.Send(PointerMoved(Mouse, 0, at x y, Modifiers.None))
    let wake () =
        w.Time.AdvanceMs 150.0
        platform.RunWake()
    let rec text (e: Element) : string =
        if e.Type.Name = "TextBlock" then e.Get TextProps.text
        else e.Children |> Seq.map text |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""
    let rec findAll (f: Element -> bool) (e: Element) : Element seq =
        seq { if e.IsVisible then
                if f e then yield e
                for c in e.Children do yield! findAll f c }
    let header = host.App.Tree.Root.Value |> findAll (fun e -> e.Type.Name = "TabHeader" && text e = tab) |> Seq.head
    (header.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
    settle w
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    let highlightTarget () =
        let layer = host.Layers |> Seq.find (fun l -> l.Name = "Inspector highlight")
        layer.Tree.Root.Value |> findAll (fun e -> obj.ReferenceEquals(e.Type, Highlight.Type)) |> Seq.head
        |> fun h -> h.Get HighlightProps.target
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    wake ()
    settle w
    let target =
        host.App.Tree.Root.Value
        |> findAll (fun e -> e.Type.Name = "TextBlock" && e.VisibleBounds.Width > 0.0 && e.VisibleBounds.Y > 150.0 && e.VisibleBounds.X < 400.0)
        |> Seq.head
    let c = target.PositionInTree
    moveTo (c.X + 5.0) (c.Y + 5.0)
    settle w
    w.Send(PointerPressed(Mouse, 0, at (c.X + 5.0) (c.Y + 5.0), MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at (c.X + 5.0) (c.Y + 5.0), MouseButton.Left, Modifiers.None))
    // The pointer goes straight to the panel while the tree catches up (moves between frames).
    moveTo 700.0 200.0
    wake ()
    moveTo 700.0 205.0
    settle w
    wake ()
    moveTo 700.0 210.0
    settle w
    let problems = List<string>()
    let mutable y = 60.0
    while y < 690.0 do
        moveTo 700.0 y
        settle w
        // The row under the pointer (22 DIPs high), and what the inspector shows as hovered.
        let rec rowOf (e: Element) =
            if e.Bounds.Height = 22.0 && e.Type.Name = "Border" then ValueSome e
            else match e.Parent with ValueSome p -> rowOf p | ValueNone -> ValueNone
        match panel.Tree.HitTest(700.0, y) |> ValueOption.bind rowOf with
        // A layer's row isn't an element: hovering it hovers nothing, and the highlight shows the selection.
        | ValueSome row when (text row).StartsWith "Layer " -> ()
        | ValueSome row ->
            let shown = row.Children |> Seq.map text |> String.concat ""
            match highlightTarget () with
            | ValueSome t when not ((text row).StartsWith(Format.label t)) ->
                problems.Add($"y {y}: pointer on '{text row}', hovered '{Format.label t}'")
            | ValueNone -> problems.Add($"y {y}: pointer on '{text row}', nothing hovered")
            | _ -> ()
            ignore shown
        | ValueNone -> ()
        y <- y + 11.0
    Assert.True(problems.Count = 0, String.Join("\n", problems |> Seq.truncate 15))

/// Browsers send a pointer move between press and release even when the mouse stands still. The press
/// that picks ends pick mode; that move must not pick again (it left an element "hovered", shown on a
/// tree row far from the pointer).
[<Fact>]
let ``a move between the picking press and its release doesn't pick again`` () =
    let log = List<string>()
    let w, host, panel = start log
    let target () =
        let layer = host.Layers |> Seq.find (fun l -> l.Name = "Inspector highlight")
        let rec find (e: Element) =
            if obj.ReferenceEquals(e.Type, Highlight.Type) then Some e else e.Children |> Seq.tryPick find
        let h = layer.Tree.Root.Value |> find |> Option.get
        h.Get HighlightProps.target |> ValueOption.map _.Name, h.Get HighlightProps.outlineOnly
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    move w 50.0 20.0
    settle w
    press w 50.0 20.0
    move w 200.0 300.0 // the browser's move (here, far enough to land on the background)
    release w 200.0 300.0
    settle w
    // The picked button is selected and only outlined; nothing is hovered.
    Assert.Equal((ValueSome "appButton", true), target ())
    // And the pointer goes back to normal: the app hovers again, so does the panel.
    let appButton = host.App.Tree.Root.Value |> find "appButton" |> Option.get
    move w 50.0 20.0
    settle w
    Assert.True(appButton.IsPointerOver, "the app's button isn't hovered after the pick")
    move w 700.0 300.0
    settle w
    Assert.False(appButton.IsPointerOver)
    let over = panel.Tree.HitTest(700.0, 300.0)
    Assert.True(over.IsSome && over.Value.IsPointerOver, "the panel isn't hovered after the pick")

/// Under Rider's browser debugger the release of the picking click was lost (input that arrives
/// while the page is paused is dropped), which left the highlight holding the pointer: no hover
/// anywhere, and every later click picked again. The page now cancels a press whose release went
/// missing; after that, everything is back to normal.
[<Fact>]
let ``a picking press canceled instead of released leaves nothing stuck`` () =
    let log = List<string>()
    let w, host, panel = start log
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    move w 50.0 20.0
    settle w
    press w 50.0 20.0
    settle w
    // The release is lost; the page sees a move with no buttons down and cancels the press.
    w.Send(PointerCanceled(Mouse, 0))
    move w 52.0 22.0
    settle w
    let appButton = host.App.Tree.Root.Value |> find "appButton" |> Option.get
    Assert.True(appButton.IsPointerOver, "the app's button isn't hovered")
    move w 700.0 300.0
    settle w
    let over = panel.Tree.HitTest(700.0, 300.0)
    Assert.True(over.IsSome && over.Value.IsPointerOver, "the panel isn't hovered")
    // The next click is the app's, not another pick.
    click w 50.0 20.0
    settle w
    Assert.Equal<string list>([ "appButton" ], List.ofSeq log)

/// The Gallery with the inspector on the fake platform, on `tab`, at 1000 × `height`.
let private galleryOn (tab: string) (height: int) =
    let platform, device = new FakePlatform(), new FakeDevice()
    let app = App.start (platform, device, new Scribble.Tests.TestShaping.FixedShaper(), Scribble.Samples.GalleryApp.app @ [ Inspector.enable [] ])
    let w = platform.Window.Value
    let host = app :?> ElementHost
    let size: PixelSize = { Width = 1000; Height = height }
    w.Size <- size
    w.Send(Resized size)
    settle w
    let rec text (e: Element) : string =
        if e.Type.Name = "TextBlock" then e.Get TextProps.text
        else e.Children |> Seq.map text |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""
    let rec findAll (f: Element -> bool) (e: Element) : Element seq =
        seq { if e.IsVisible then
                if f e then yield e
                for c in e.Children do yield! findAll f c }
    let header = host.App.Tree.Root.Value |> findAll (fun e -> e.Type.Name = "TabHeader" && text e = tab) |> Seq.head
    (header.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
    settle w
    // The inspector refreshes its tree a little later, through the dispatcher.
    let wake () =
        w.Time.AdvanceMs 150.0
        platform.RunWake()
        settle w
    w, host, platform, wake

[<Fact>]
let ``the wheel scrolls the app while picking`` () =
    let w, host, _, _ = galleryOn "Scrolling" 700
    let messages = host.App.Tree.Root.Value |> find "messages" |> Option.get
    let before = messages.PositionInTree.Y
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    let p = messages.Parent.Value.PositionInTree
    let at: PixelPoint = { X = p.X + 40.0; Y = p.Y + 40.0 }
    w.Send(PointerMoved(Mouse, 0, at, Modifiers.None))
    settle w
    w.Send(PointerWheel(at, 0.0, -3.0, Modifiers.None))
    for _ in 1..30 do
        settle w
        host.App.Tree.Time <- host.App.Tree.Time + TimeSpan.FromMilliseconds 50.0
        w.Send(PointerMoved(Mouse, 0, at, Modifiers.None))
    settle w
    Assert.True(messages.PositionInTree.Y < before - 10.0, $"the list didn't scroll ({before} -> {messages.PositionInTree.Y})")
    // Still picking: a click picks, it doesn't reach the app.
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    Assert.Contains("Picking… (Esc stops)", texts panel)

[<Fact>]
let ``a pick scrolls the tree to the selected row`` () =
    let w, host, _, wake = galleryOn "Scrolling" 520
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    wake ()
    // A message in the list: deep in the tree, far below the panel's bottom once expanded.
    let messages = host.App.Tree.Root.Value |> find "messages" |> Option.get
    let p = messages.Children[0].PositionInTree
    let at: PixelPoint = { X = p.X + 20.0; Y = p.Y + 8.0 }
    w.Send(PointerMoved(Mouse, 0, at, Modifiers.None))
    settle w
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    settle w
    wake ()
    wake ()
    for _ in 1..20 do // past the scroll animation
        host.Layers |> Seq.iter (fun l -> l.Tree.Time <- l.Tree.Time + TimeSpan.FromMilliseconds 50.0)
        w.Send(PointerMoved(Mouse, 0, at, Modifiers.None))
        settle w
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    // The selected row: the realized row in the selection color.
    let selectedColor = (Color.Hex 0x3D59A1).WithAlpha 0.6f
    let rec selectedRow (e: Element) : Element option =
        if e.Type.Name = "Border" && e.Bounds.Height = 22.0 && e.Get BorderProps.background = selectedColor then Some e
        else e.Children |> Seq.tryPick selectedRow
    let row = panel.Tree.Root.Value |> selectedRow |> Option.get
    Assert.True(abs (row.VisibleBounds.Height - 22.0) < 0.5, $"the selected row shows {row.VisibleBounds.Height} of 22")

// --- The tree on an ItemsView (M8 step 3a+) ----------------------------------------------------

/// An app of `n` rows 20 DIPs tall in a StackPanel, with the inspector open and the first row
/// picked: its ancestors expand, so the tree lists every row. Returns the window, the host, the
/// inspector's tree (its ItemsView) and `wake`.
let private manyRows (n: int) =
    let platform, device = new FakePlatform(), new FakeDevice()
    let rows = [ for i in 0 .. n - 1 -> Border.create [ Border.name ("item" + string i); Border.height 20.0 ] ]
    let app =
        App.start (platform, device, new Scribble.Tests.TestShaping.FixedShaper(),
                   [ App.view (StackPanel.create [ StackPanel.children rows ]); Inspector.enable [] ])
    let w = platform.Window.Value
    let host = app :?> ElementHost
    let size: PixelSize = { Width = 900; Height = 500 }
    w.Size <- size
    w.Send(Resized size)
    settle w
    let wake () =
        w.Time.AdvanceMs 150.0
        platform.RunWake()
        settle w
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    move w 50.0 10.0
    settle w
    click w 50.0 10.0
    settle w
    wake ()
    wake ()
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    let rec itemsView (e: Element) : Element option =
        if e.Type.Name = "ItemsView" then Some e else e.Children |> Seq.tryPick itemsView
    w, host, (panel.Tree.Root.Value |> itemsView |> Option.get), wake

let private realizedRows (tree: Element) =
    match ItemsView.realizedCount tree with
    | ValueSome(struct (r, total)) -> r, total
    | ValueNone -> failwith "not an ItemsView"

[<Fact>]
let ``the tree realizes the same rows for 100 and 10,000 elements`` () =
    let _, _, small, _ = manyRows 100
    let _, _, large, _ = manyRows 10_000
    let smallRealized, smallTotal = realizedRows small
    let largeRealized, largeTotal = realizedRows large
    // Every element is listed (the layer, the StackPanel and its rows) ...
    Assert.True(smallTotal >= 100 && largeTotal >= 10_000, $"rows listed: {smallTotal}, {largeTotal}")
    // ... but only what fits in the panel has an element, the same for both.
    Assert.Equal(smallRealized, largeRealized)
    Assert.True(largeRealized < 60, $"{largeRealized} rows realized")

[<Fact>]
let ``a wheel scroll over the tree moves the hover to the row under the still pointer`` () =
    let w, host, tree, _ = manyRows 1_000
    let rowUnder (y: float) =
        tree.Children |> Seq.tryFind (fun r -> let p = r.PositionInTree in p.Y <= y && y < p.Y + r.Bounds.Height)
    // A point in the middle of the tree's viewport (its ScrollViewer).
    let rec scrollViewer (e: Element) = if e.Type.Name = "ScrollViewer" then e else scrollViewer e.Parent.Value
    let sv = scrollViewer tree
    let top = sv.PositionInTree
    let at: PixelPoint = { X = top.X + 60.0; Y = top.Y + sv.Bounds.Height / 2.0 }
    w.Send(PointerMoved(Mouse, 0, at, Modifiers.None))
    settle w
    let first = (rowUnder at.Y).Value
    Assert.True first.IsPointerOver
    for _ in 1..3 do
        w.Send(PointerWheel(at, 0.0, -3.0, Modifiers.None))
        for _ in 1..20 do // past the scroll animation; the pointer stays put
            host.Layers |> Seq.iter (fun l -> l.Tree.Time <- l.Tree.Time + TimeSpan.FromMilliseconds 50.0)
            settle w
    let hovered = tree.Children |> Seq.filter _.IsPointerOver |> List.ofSeq
    Assert.Equal(1, hovered.Length)
    Assert.True(obj.ReferenceEquals(hovered.Head, (rowUnder at.Y).Value), "the hovered row isn't the one under the pointer")
    // The tree did scroll.
    Assert.True(tree.PositionInTree.Y < top.Y - 50.0, $"the tree didn't scroll ({tree.PositionInTree.Y} vs {top.Y})")

// --- Sideways scrolling in the tree (M8 step 3a++) --------------------------------------------

/// An app of Borders nested `depth` deep (the innermost 100×20 at the top left), with the inspector
/// open and the innermost picked. Returns the host, the tree's ScrollViewer and its ItemsView.
let private nested (depth: int) =
    let platform, device = new FakePlatform(), new FakeDevice()
    let rec chain d =
        if d = 0 then Border.create [ Border.name "innermost"; Border.width 100.0; Border.height 20.0 ]
        else Border.create [ Border.horizontalAlignment HorizontalAlignment.Left; Border.verticalAlignment VerticalAlignment.Top; Border.content (chain (d - 1)) ]
    let app = App.start (platform, device, new Scribble.Tests.TestShaping.FixedShaper(), [ App.view (chain depth); Inspector.enable [] ])
    let w = platform.Window.Value
    let host = app :?> ElementHost
    let size: PixelSize = { Width = 900; Height = 500 }
    w.Size <- size
    w.Send(Resized size)
    settle w
    let wake () =
        w.Time.AdvanceMs 150.0
        platform.RunWake()
        settle w
    keyWith w (Key.Char 'c') ctrlShift
    settle w
    move w 50.0 10.0
    settle w
    click w 50.0 10.0
    settle w
    wake ()
    wake ()
    for _ in 1..20 do // past any scroll animation
        host.Layers |> Seq.iter (fun l -> l.Tree.Time <- l.Tree.Time + TimeSpan.FromMilliseconds 50.0)
        settle w
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    let rec first (f: Element -> bool) (e: Element) : Element option =
        if f e then Some e else e.Children |> Seq.tryPick (first f)
    let tree = panel.Tree.Root.Value |> first (fun e -> e.Type.Name = "ItemsView") |> Option.get
    let sv = tree.Parent.Value
    let sv = if sv.Type.Name = "ScrollViewer" then sv else (let rec up (e: Element) = if e.Type.Name = "ScrollViewer" then e else up e.Parent.Value in up tree)
    host, panel, sv, tree, first

[<Fact>]
let ``a deep tree scrolls sideways; a shallow one doesn't`` () =
    let _, _, shallowSv, shallowTree, _ = nested 2
    let struct (_, _, vw, _) = ScrollViewer.viewportOf shallowSv
    Assert.True(shallowTree.DesiredSize.Width <= vw, $"shallow: {shallowTree.DesiredSize.Width} wide in {vw}")
    let _, _, deepSv, deepTree, _ = nested 60
    let struct (_, _, vw, _) = ScrollViewer.viewportOf deepSv
    Assert.True(deepTree.DesiredSize.Width > vw + 300.0, $"deep: {deepTree.DesiredSize.Width} wide in {vw}")

[<Fact>]
let ``a deep pick brings the selected row's name into view sideways`` () =
    let _, panel, sv, _, first = nested 60
    let label = panel.Tree.Root.Value |> first (fun e -> e.Name = "inspectorSelectedLabel") |> Option.get
    let struct (x, _, vw, _) = ScrollViewer.viewportOf sv
    Assert.True(x > 300.0, $"the tree didn't scroll sideways (x {x})")
    // The label (chevron, the type name and a little indent) is inside the viewport, sideways.
    let left = label.PositionInTree.X - sv.PositionInTree.X
    Assert.True(left >= -0.5 && left + label.Bounds.Width <= vw + 0.5, $"label at {left}, {label.Bounds.Width} wide, viewport {vw}")

// --- Forcing states off (Tim, 2026-11-17) ----------------------------------------------------

[<Fact>]
let ``a state chip forces the opposite of what it shows, then lets go`` () =
    // A custom state that's always on, so its chip forces it off; removed again at the end.
    let always = States.define "always-on-for-this-test" []
    try
        let log = List<string>()
        let w, host, panel = start log
        let button = (find "appButton" host.App.Tree.Root.Value).Value
        button.Set(Button.state always true)
        pickAppButton w
        let forcedAs (s: ElementState) = button.ForcedStates |> List.tryPick (fun (f, on) -> if obj.ReferenceEquals(f, s) then Some on else None)
        // Off for real: forced on, then real again.
        clickButton w panel "disabled"
        Assert.Equal(Some true, forcedAs States.disabled)
        clickButton w panel "disabled"
        Assert.Equal(None, forcedAs States.disabled)
        // On for real: forced off (styles no longer see it), then real again.
        clickButton w panel "always-on-for-this-test"
        Assert.Equal(Some false, forcedAs always)
        Assert.DoesNotContain(always, button.ActiveStates)
        Assert.Contains("• forced", texts panel)
        clickButton w panel "always-on-for-this-test"
        Assert.Equal(None, forcedAs always)
        Assert.Contains(always, button.ActiveStates)
        Assert.Empty(log) // none of it reached the app
    finally
        States.remove always

[<Fact>]
let ``Element.Hooks names each hook as the component called it`` () =
    let probeValue = AppValue.define "ProbeValue" 7
    let probe =
        Component.define "Probe" (fun ctx () ->
            ctx.useScroller () |> ignore
            ctx.useCapture () |> ignore
            ctx.useClipboard () |> ignore
            ctx.useToaster () |> ignore
            ctx.useDialogs () |> ignore
            ctx.useConfirm [ Confirm.title "Sure?" ] |> ignore
            ctx.useAsk [ Ask.choice ("Yes", ignore) ] |> ignore
            ctx.useInform [ Inform.text "Done" ] |> ignore
            ctx.useDialog (fun (_: unit -> unit) -> [ Dialog.title "Mine" ]) |> ignore
            ctx.useAppValue probeValue |> ignore
            Border.create [])
    let t = ElementTree(Scribble.Tests.TestShaping.textSystem ())
    t.Update(probe ())
    let hooks = t.Root.Value.Hooks
    Assert.Equal<string list>(
        [ "useScroller"; "useCapture"; "useClipboard"; "useToaster"; "useDialogs"; "useConfirm"; "useAsk"; "useInform"; "useDialog"; "useAppValue ProbeValue" ],
        hooks |> List.map (fun h -> h.Kind))
    // The ones that only keep an object have no value to show.
    Assert.All(hooks |> List.take 8, fun h -> Assert.True(h.Value.IsNone))
