/// M8 step 3a's controls (ToggleButton, CheckBox, Switch, Slider, TabControl) on the fake
/// platform: controlled and own values, change events, keys, pointer drags, lazy tab pages.
module Scribble.Tests.ControlsTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.AppTests

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let private find name (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = name)
let private ofType (t: ElementType) (host: ElementHost) = all host.Tree.Root.Value |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, t)) |> List.ofSeq

let private show (settings: AppSetting<'Msg> list) =
    let w, device, app = start settings
    w.DeliverFrame() |> ignore
    w, device, (app :?> ElementHost)

let private at (x: float) (y: float) : PixelPoint = { X = x; Y = y }
let private press (w: FakeWindow) x y = w.Send(PointerPressed(Mouse, 0, at x y, MouseButton.Left, Modifiers.None))
let private move (w: FakeWindow) x y = w.Send(PointerMoved(Mouse, 0, at x y, Modifiers.None))
let private release (w: FakeWindow) x y = w.Send(PointerReleased(Mouse, 0, at x y, MouseButton.Left, Modifiers.None))
let private key (w: FakeWindow) k =
    w.Send(KeyDown(k, Modifiers.None, false))
    w.Send(KeyUp(k, Modifiers.None))

/// Presses and releases the left button in the middle of an element, then delivers a frame.
let private clickOn (w: FakeWindow) (e: Element) =
    let p = e.PositionInTree
    let x, y = p.X + e.Bounds.Width / 2.0, p.Y + e.Bounds.Height / 2.0
    press w x y
    release w x y
    w.DeliverFrame() |> ignore

let private isChecked (e: Element) = e.Get ToggleButtonProps.isChecked

// --- Toggles -------------------------------------------------------------------------------------

type private ToggleMsg =
    | SetOn of bool
    | Bump

/// A CheckBox ("box"), controlled when `controlled`, and a Button ("bump") that changes the model
/// (so the view is rebuilt) without touching it. `follow`: the model takes reported values.
let private toggleApp (reports: List<bool>) (controlled: bool) (follow: bool) =
    App.program (false, 0)
        (fun msg (on, n) ->
            match msg with
            | SetOn v -> ((if follow then v else on), n)
            | Bump -> (on, n + 1))
        (fun (on, n) dispatch ->
            StackPanel.create [
                StackPanel.children [
                    CheckBox.create [
                        yield CheckBox.name "box"
                        yield CheckBox.text "Box"
                        if controlled then yield CheckBox.isChecked on
                        yield CheckBox.onCheckedChanged (fun v -> reports.Add v; dispatch (SetOn v))
                    ]
                    Button.create [ Button.name "bump"; Button.text (string n); Button.onClick (fun _ -> dispatch Bump) ]
                ]
            ])

[<Fact>]
let ``a CheckBox without isChecked keeps its own value, through rebuilds`` () =
    let reports = List<bool>()
    let w, _, host = show [ toggleApp reports false false ]
    let box = find "box" host
    Assert.False(isChecked box)
    clickOn w box
    Assert.True(isChecked box)
    Assert.Equal<bool list>([ true ], List.ofSeq reports)
    // The model changes and the view is rebuilt: the own value stays.
    clickOn w (find "bump" host)
    Assert.Equal("1", (find "bump" host).Children[0].Get TextProps.text)
    Assert.True(isChecked box)
    clickOn w box
    Assert.False(isChecked box)
    Assert.Equal<bool list>([ true; false ], List.ofSeq reports)

[<Fact>]
let ``a controlled CheckBox shows only what the view gives`` () =
    let reports = List<bool>()
    // The model ignores the reports: the box stays off, but each click still reports `true`.
    let w, _, host = show [ toggleApp reports true false ]
    let box = find "box" host
    clickOn w box
    Assert.False(isChecked box)
    clickOn w box
    Assert.False(isChecked box)
    Assert.Equal<bool list>([ true; true ], List.ofSeq reports)

[<Fact>]
let ``a controlled CheckBox follows the model when it takes the value`` () =
    let reports = List<bool>()
    let w, _, host = show [ toggleApp reports true true ]
    let box = find "box" host
    clickOn w box
    Assert.True(isChecked box)
    clickOn w box
    Assert.False(isChecked box)
    Assert.Equal<bool list>([ true; false ], List.ofSeq reports)

[<Fact>]
let ``Space and Enter flip a focused toggle; a disabled one ignores clicks`` () =
    let reports = List<bool>()
    let view =
        StackPanel.create [
            StackPanel.children [
                Switch.create [ Switch.name "on"; Switch.text "On"; Switch.onCheckedChanged reports.Add ]
                Switch.create [ Switch.name "off"; Switch.text "Off"; Switch.isEnabled false; Switch.onCheckedChanged reports.Add ]
            ]
        ]
    let w, _, host = show [ App.view view ]
    key w Key.Tab
    Assert.True((find "on" host).IsFocused)
    key w Key.Space
    Assert.True(isChecked (find "on" host))
    key w Key.Enter
    Assert.False(isChecked (find "on" host))
    clickOn w (find "off" host)
    Assert.False(isChecked (find "off" host))
    Assert.Equal<bool list>([ true; false ], List.ofSeq reports)

[<Fact>]
let ``an indeterminate CheckBox reports true when clicked`` () =
    let reports = List<bool>()
    let view = CheckBox.create [ CheckBox.name "all"; CheckBox.isChecked false; CheckBox.isIndeterminate true; CheckBox.onCheckedChanged reports.Add ]
    let w, _, host = show [ App.view view ]
    clickOn w (find "all" host)
    Assert.Equal<bool list>([ true ], List.ofSeq reports)

[<Fact>]
let ``the theme colors a checked CheckBox, Switch and ToggleButton with the accent`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                CheckBox.create [ CheckBox.name "c" ]
                Switch.create [ Switch.name "s" ]
                ToggleButton.create [ ToggleButton.name "t"; ToggleButton.text "T" ]
            ]
        ]
    let w, _, host = show [ App.view view ]
    let accent = Palette.accent.Default
    for name in [ "c"; "s"; "t" ] do
        Assert.NotEqual(accent, (find name host).Get BorderProps.background)
        clickOn w (find name host)
        Assert.True(isChecked (find name host))
    // Past the color transition.
    for _ in 1..20 do
        System.Threading.Thread.Sleep 10
        w.DeliverFrame() |> ignore
    for name in [ "c"; "s"; "t" ] do
        Assert.Equal(accent, (find name host).Get BorderProps.background)
    Assert.Equal(Palette.onAccent.Default, (find "t" host).Get InheritedProps.foreground)

// --- Slider --------------------------------------------------------------------------------------

/// A horizontal Slider 218 DIPs wide: its thumb's center runs from 9 to 209, so 2 DIPs per unit.
let private sliderView (changed: List<float>) (committed: List<float>) (extra: Setting<Slider> list) =
    StackPanel.create [
        StackPanel.children [
            Slider.create (
                [ Slider.name "s"
                  Slider.width 218.0
                  Slider.horizontalAlignment HorizontalAlignment.Left
                  Slider.onValueChanged changed.Add
                  Slider.onValueCommitted committed.Add ]
                @ extra
            )
        ]
    ]

/// A point `units` along the slider's range (0 to 100), in window DIPs.
let private alongSlider (s: Element) (units: float) =
    let p = s.PositionInTree
    p.X + 9.0 + 2.0 * units, p.Y + s.Bounds.Height / 2.0

let private value (s: Element) = s.Get SliderProps.value

[<Fact>]
let ``a press on the track jumps there, a drag follows in steps, the release commits once`` () =
    let changed, committed = List<float>(), List<float>()
    let w, _, host = show [ App.view (sliderView changed committed []) ]
    let s = find "s" host
    let x, y = alongSlider s 40.2
    press w x y
    Assert.Equal(40.0, value s)
    let x2, _ = alongSlider s 40.4
    move w x2 y // still 40: nothing to report
    let x3, _ = alongSlider s 55.0
    move w x3 y
    let x4, _ = alongSlider s 150.0 // past the end: the maximum
    move w x4 y
    Assert.Equal(100.0, value s)
    Assert.Empty committed
    release w x4 y
    Assert.Equal<float list>([ 40.0; 55.0; 100.0 ], List.ofSeq changed)
    Assert.Equal<float list>([ 100.0 ], List.ofSeq committed)

[<Fact>]
let ``a press on the thumb drags it from where it was taken, without a jump`` () =
    let changed, committed = List<float>(), List<float>()
    let w, _, host = show [ App.view (sliderView changed committed [ Slider.value 50.0 ]) ]
    let s = find "s" host
    // 6 DIPs right of the thumb's center (3 units): no jump.
    let x, y = alongSlider s 53.0
    press w x y
    Assert.Empty changed
    let x2, _ = alongSlider s 63.0
    move w x2 y
    release w x2 y
    Assert.Equal<float list>([ 60.0 ], List.ofSeq changed)
    Assert.Equal<float list>([ 60.0 ], List.ofSeq committed)
    // Controlled and the app didn't follow: still 50.
    Assert.Equal(50.0, value s)

[<Fact>]
let ``keys step, page, go to the ends and clamp; each key step commits`` () =
    let changed, committed = List<float>(), List<float>()
    let w, _, host = show [ App.view (sliderView changed committed [ Slider.minimum 10.0; Slider.maximum 60.0; Slider.step 5.0 ]) ]
    let s = find "s" host
    key w Key.Tab
    Assert.True s.IsFocused
    Assert.Equal(0.0, value s) // the own value starts at the property's default, below the range
    key w Key.Right // snapped into the range first: 0 + 5 → 10
    key w Key.Right
    key w Key.Up
    key w Key.PageUp // 10% of 50 = 5 (as large as a step)
    key w Key.End
    key w Key.Right // at the end: no change, nothing reported
    key w Key.Home
    key w Key.Left
    key w Key.Down
    Assert.Equal<float list>([ 10.0; 15.0; 20.0; 25.0; 60.0; 10.0 ], List.ofSeq changed)
    Assert.Equal<float list>(List.ofSeq changed, List.ofSeq committed)

[<Fact>]
let ``a continuous slider steps 1% with the keys; a vertical one has its minimum at the bottom`` () =
    let changed, committed = List<float>(), List<float>()
    let view =
        StackPanel.create [
            StackPanel.orientation Orientation.Horizontal
            StackPanel.children [
                Slider.create [ Slider.name "c"; Slider.step 0.0; Slider.maximum 2.0; Slider.onValueChanged changed.Add ]
                Slider.create [ Slider.name "v"; Slider.orientation Orientation.Vertical; Slider.height 218.0; Slider.onValueChanged committed.Add ]
            ]
        ]
    let w, _, host = show [ App.view view ]
    key w Key.Tab
    key w Key.Right
    Assert.Equal(0.02, value (find "c" host), 9)
    let v = find "v" host
    let p = v.PositionInTree
    let x = p.X + v.Bounds.Width / 2.0
    // 9 DIPs above the bottom is the minimum; 9 below the top the maximum.
    press w x (p.Y + 218.0 - 9.0 - 2.0 * 30.0)
    release w x (p.Y + 9.0)
    Assert.Equal<float list>([ 30.0 ], List.ofSeq committed |> List.take 1)
    Assert.Equal(30.0, value v)

[<Fact>]
let ``a disabled slider ignores presses and keys`` () =
    let changed, committed = List<float>(), List<float>()
    let w, _, host = show [ App.view (sliderView changed committed [ Slider.isEnabled false ]) ]
    let s = find "s" host
    let x, y = alongSlider s 70.0
    press w x y
    release w x y
    key w Key.Tab
    key w Key.Right
    Assert.Empty changed
    Assert.Equal(0.0, value s)

// --- TabControl ----------------------------------------------------------------------------------

/// A page with a marker TextBlock named `page{i}`.
let private page i = TextBlock.create [ TextBlock.name $"page{i}"; TextBlock.text $"Page {i}" ]

let private tabsView (selected: List<int>) (count: int) (extra: Setting<TabControl> list) (tab: int -> Setting<TabItem> list) =
    TabControl.create (
        [ TabControl.name "tabs"
          TabControl.onSelectedChanged selected.Add
          TabControl.tabs [ for i in 0 .. count - 1 -> TabItem.create ([ TabItem.header $"Tab {i}"; TabItem.content (page i) ] @ tab i) ] ]
        @ extra
    )

let private pages (host: ElementHost) = ofType TabItem.Type host
let private headersOf (host: ElementHost) = ofType TabHeader.Type host
let private header i (host: ElementHost) = List.item i (headersOf host)
let private hasPage i (host: ElementHost) = all host.Tree.Root.Value |> Seq.exists (fun e -> e.Name = $"page{i}")

[<Fact>]
let ``pages are realized on the first visit and kept hidden after; keepAlive false lets one go`` () =
    let selected = List<int>()
    let view = tabsView selected 4 [] (fun i -> if i = 2 then [ TabItem.keepAlive false ] else [])
    let w, _, host = show [ App.view view ]
    Assert.Equal(4, (headersOf host).Length)
    Assert.True(hasPage 0 host)
    Assert.False(hasPage 1 host)
    let first = find "page0" host
    clickOn w (header 1 host)
    Assert.True(hasPage 1 host)
    // Page 0 is the same element, hidden.
    Assert.Same(first, find "page0" host)
    Assert.False((find "page0" host).Parent.Value.IsVisible)
    Assert.True((find "page1" host).Parent.Value.IsVisible)
    clickOn w (header 2 host)
    Assert.True(hasPage 2 host)
    clickOn w (header 3 host)
    // Page 2 went (keepAlive false); 0 and 1 stay; 3 is shown.
    Assert.False(hasPage 2 host)
    Assert.True(hasPage 0 host && hasPage 1 host && hasPage 3 host)
    Assert.Equal<int list>([ 1; 2; 3 ], List.ofSeq selected)
    Assert.Equal(3, (pages host).Length)

[<Fact>]
let ``arrow keys select the previous and next tab and move focus; Home and End go to the ends`` () =
    let selected = List<int>()
    let w, _, host = show [ App.view (tabsView selected 4 [] (fun _ -> [])) ]
    key w Key.Tab
    let headers () = headersOf host
    Assert.True((header 0 host).IsFocused)
    key w Key.Right
    w.DeliverFrame() |> ignore
    Assert.True((header 1 host).IsFocused)
    Assert.True(hasPage 1 host)
    key w Key.End
    w.DeliverFrame() |> ignore
    Assert.True((header 3 host).IsFocused)
    key w Key.Right // at the end: stays
    key w Key.Home
    w.DeliverFrame() |> ignore
    key w Key.Left // at the start: stays
    Assert.True((header 0 host).IsFocused)
    Assert.Equal<int list>([ 1; 3; 0 ], List.ofSeq selected)
    // Only the selected header is a Tab stop: Tab leaves the row (nothing else here, so it wraps to it).
    Assert.Equal<bool list>([ true; false; false; false ], [ for h in headers () -> h.Get LayoutProps.isTabStop ])

[<Fact>]
let ``a controlled TabControl reports the click and shows what the view gives`` () =
    let selected = List<int>()
    let w, _, host = show [ App.view (tabsView selected 3 [ TabControl.selected 0 ] (fun _ -> [])) ]
    clickOn w (header 2 host)
    Assert.Equal<int list>([ 2 ], List.ofSeq selected)
    Assert.False(hasPage 2 host)
    Assert.True((header 0 host).Get TabHeaderProps.isSelected)

[<Fact>]
let ``a disabled tab's header ignores clicks`` () =
    let selected = List<int>()
    let w, _, host = show [ App.view (tabsView selected 3 [] (fun i -> if i = 1 then [ TabItem.isEnabled false ] else [])) ]
    clickOn w (header 1 host)
    Assert.Empty selected
    Assert.False(hasPage 1 host)

[<Fact>]
let ``the selected header is brought into view when the headers don't fit`` () =
    let selected = List<int>()
    let w, _, host = show [ App.view (tabsView selected 20 [] (fun _ -> [])) ]
    let last () = (header 19 host)
    let visible (e: Element) = e.VisibleBounds.Width
    Assert.Equal(0.0, visible (last ()))
    key w Key.Tab
    key w Key.End
    // The scroll animates (about 200 ms): deliver frames until it settles.
    for _ in 1..40 do
        System.Threading.Thread.Sleep 10
        w.DeliverFrame() |> ignore
    Assert.Equal((last ()).Bounds.Width, visible (last ()), 6)

[<Fact>]
let ``tabs never visited cost nothing: the same elements with 2 or 20 of them`` () =
    // Heavy pages (100 TextBlocks each): only the shown one is realized.
    let heavy i = StackPanel.create [ StackPanel.children [ for k in 1..100 -> TextBlock.create [ TextBlock.text $"{i}.{k}" ] ] ]
    let pageElements count =
        let view = TabControl.create [ TabControl.tabs [ for i in 0 .. count - 1 -> TabItem.create [ TabItem.header $"{i}"; TabItem.content (heavy i) ] ] ]
        let _, _, host = show [ App.view view ]
        pages host |> List.sumBy (fun p -> Seq.length (all p))
    Assert.Equal(pageElements 2, pageElements 20)
    Assert.Equal(1 + 1 + 100, pageElements 20)
