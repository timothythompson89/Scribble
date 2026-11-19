/// M8 step 4a: flyouts (their own layer next to an anchor; light dismiss, Escape, focus, placement) and tooltips.
module Scribble.Tests.FlyoutTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.AppTests

let private move (w: FakeWindow) x y = w.Send(PointerMoved(Mouse, 0, { X = x; Y = y }, Modifiers.None))
let private press (w: FakeWindow) x y = w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private release (w: FakeWindow) x y = w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
let private click w x y = press w x y; release w x y
let private key (w: FakeWindow) k = w.Send(KeyDown(k, Modifiers.None, false)); w.Send(KeyUp(k, Modifiers.None))

let rec private find (name: string) (e: Element) : Element option =
    if e.Name = name then Some e else e.Children |> Seq.tryPick (find name)

let private tryNamed (host: ElementHost) name =
    host.Layers |> Seq.tryPick (fun l -> l.Tree.Root |> ValueOption.toOption |> Option.bind (find name))

let private named host name = (tryNamed host name).Value

let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 20 do
        n <- n + 1

let private start (view: View) =
    let w, _, app = start [ App.view view ]
    let host = app :?> ElementHost
    settle w
    w, host

/// Window rect of an element.
let private rectOf (e: Element) =
    let p = e.PositionInTree
    Rect.Create(p.X, p.Y, e.Bounds.Width, e.Bounds.Height)

/// A 120×60 menu with two buttons.
let private menu (log: List<string>) =
    StackPanel.create [
        StackPanel.name "menu"
        StackPanel.width 120.0
        StackPanel.children [
            Button.create [ Button.name "first"; Button.height 30.0; Button.onClick (fun _ -> log.Add "first") ]
            Button.create [ Button.name "second"; Button.height 30.0; Button.onClick (fun _ -> log.Add "second") ]
        ]
    ]

/// The app: a background that logs clicks, with an anchor button (100×40) at (x, y) whose flyout shows the menu.
let private appShowing (content: View) (log: List<string>) (x: float) (y: float) (anchorSettings: Setting<Button> list) (flyoutSettings: Setting<Flyout> list) =
    Border.create [
        Border.name "background"
        Border.onClick (fun _ -> log.Add "app")
        Border.content (
            Canvas.create [
                Canvas.children [
                    Button.create (
                        [ Button.name "anchor"
                          Button.width 100.0
                          Button.height 40.0
                          Canvas.left x
                          Canvas.top y
                          Button.flyout ([ Flyout.content content ] @ flyoutSettings) ]
                        @ anchorSettings
                    )
                ]
            ]
        )
    ]

let private appWithBoth log x y anchorSettings flyoutSettings = appShowing (menu log) log x y anchorSettings flyoutSettings
let private appWith log x y (flyoutSettings: Setting<Flyout> list) = appWithBoth log x y [] flyoutSettings

let private flyoutLayers (host: ElementHost) = host.Layers |> Seq.filter (fun l -> l.Name.StartsWith "Flyout") |> Seq.length

[<Fact>]
let ``clicking an element with flyout content opens it below, in its own layer, and clicking again closes it`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [])
    Assert.Equal(0, flyoutLayers host)
    click w 50.0 30.0
    settle w
    Assert.Equal(1, flyoutLayers host)
    // Below the anchor (y 20–60), 4 DIPs apart, lined up with its left edge; the presenter has a 1 DIP line and 4 of
    // padding around the menu.
    let presenter = rectOf (named host "menu").Parent.Value
    Assert.Equal(20.0, presenter.X)
    Assert.Equal(64.0, presenter.Y)
    Assert.Equal(130.0, presenter.Width)
    click w 50.0 30.0
    settle w
    Assert.Equal(0, flyoutLayers host)
    Assert.Empty log

[<Fact>]
let ``a press outside closes the flyout and goes no further; a press inside doesn't close it`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [])
    click w 50.0 30.0
    settle w
    let first = rectOf (named host "first")
    click w (first.X + 5.0) (first.Y + 5.0)
    settle w
    Assert.Equal<string list>([ "first" ], List.ofSeq log)
    Assert.Equal(1, flyoutLayers host)
    click w 350.0 250.0
    settle w
    Assert.Equal(0, flyoutLayers host)
    Assert.Equal<string list>([ "first" ], List.ofSeq log)
    // With it closed, the app gets presses again.
    click w 350.0 250.0
    Assert.Equal<string list>([ "first"; "app" ], List.ofSeq log)

[<Fact>]
let ``focus moves into the flyout when it opens, and Escape closes it and gives focus back`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [])
    key w Key.Tab
    settle w
    Assert.Equal("anchor", host.Focused.Value.Name)
    key w Key.Enter
    settle w
    Assert.Equal(1, flyoutLayers host)
    Assert.Equal("first", host.Focused.Value.Name)
    key w Key.Tab
    Assert.Equal("second", host.Focused.Value.Name)
    key w Key.Escape
    settle w
    Assert.Equal(0, flyoutLayers host)
    Assert.Equal("anchor", host.Focused.Value.Name)

[<Fact>]
let ``a flyout that doesn't fit below shows above, and stays inside the window`` () =
    let log = List<string>()
    // The anchor at the bottom right: x 340–440 is past the 400-wide window's edge too.
    let w, host = start (appWith log 340.0 250.0 [])
    click w 360.0 260.0
    settle w
    let presenter = rectOf (named host "menu").Parent.Value
    Assert.Equal(250.0 - 4.0 - presenter.Height, presenter.Y)
    Assert.Equal(400.0 - presenter.Width, presenter.X)

[<Fact>]
let ``placement, alignment and matching the anchor's width`` () =
    let log = List<string>()
    let w, host =
        start (appWith log 100.0 100.0 [ Flyout.placement Placement.Right; Flyout.alignment FlyoutAlignment.Center ])
    click w 150.0 120.0
    settle w
    let presenter = rectOf (named host "menu").Parent.Value
    Assert.Equal(204.0, presenter.X)
    Assert.Equal(System.Math.Round(120.0 - presenter.Height / 2.0), presenter.Y)
    let w2, host2 = start (appWithBoth log 100.0 100.0 [ Button.width 200.0 ] [ Flyout.matchAnchorWidth true ])
    click w2 150.0 120.0
    settle w2
    Assert.Equal(200.0, (rectOf (named host2 "menu").Parent.Value).Width)

[<Fact>]
let ``a pinned flyout stays open on presses outside, which reach the app`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [ Flyout.lightDismiss false ])
    click w 50.0 30.0
    settle w
    click w 350.0 250.0
    key w Key.Escape
    settle w
    Assert.Equal(1, flyoutLayers host)
    Assert.Equal<string list>([ "app" ], List.ofSeq log)
    click w 50.0 30.0
    settle w
    Assert.Equal(0, flyoutLayers host)

[<Fact>]
let ``a controlled flyout shows what the view says and only reports clicks and dismissals`` () =
    let log = List<string>()
    let view isOpen = appWith log 20.0 20.0 [ Flyout.isOpen isOpen; Flyout.onOpenChanged (fun v -> log.Add $"open {v}") ]
    let w, host = start (view true)
    Assert.Equal(1, flyoutLayers host)
    click w 350.0 250.0
    settle w
    Assert.Equal<string list>([ "open False" ], List.ofSeq log)
    Assert.Equal(1, flyoutLayers host)
    host.Update(view false)
    settle w
    Assert.Equal(0, flyoutLayers host)
    click w 50.0 30.0
    settle w
    Assert.Equal<string list>([ "open False"; "open True" ], List.ofSeq log)
    Assert.Equal(0, flyoutLayers host)

[<Fact>]
let ``an uncontrolled flyout reports opening and closing`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [ Flyout.onOpenChanged (fun v -> log.Add $"open {v}") ])
    click w 50.0 30.0
    settle w
    click w 350.0 250.0
    settle w
    Assert.Equal<string list>([ "open True"; "open False" ], List.ofSeq log)

[<Fact>]
let ``the flyout follows its anchor and closes when the anchor goes away`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [])
    click w 50.0 30.0
    settle w
    host.Update(appWith log 60.0 20.0 [])
    settle w
    Assert.Equal(60.0, (rectOf (named host "menu").Parent.Value).X)
    host.Update(Border.create [ Border.name "background" ])
    settle w
    Assert.Equal(0, flyoutLayers host)
    Assert.True((tryNamed host "menu").IsNone)

[<Fact>]
let ``the flyout closes when its anchor scrolls out of view`` () =
    let log = List<string>()
    let view =
        ScrollViewer.create [
            ScrollViewer.name "scroller"
            ScrollViewer.height 100.0
            ScrollViewer.verticalAlignment VerticalAlignment.Top
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [
                        Button.create [ Button.name "anchor"; Button.height 40.0; Button.flyout (menu log) ]
                        Border.create [ Border.height 1000.0 ]
                    ]
                ]
            )
        ]
    let w, host = start view
    click w 50.0 20.0
    settle w
    Assert.Equal(1, flyoutLayers host)
    w.Send(PointerWheel({ X = 300.0; Y = 50.0 }, 0.0, -10.0, Modifiers.None))
    for _ in 1..60 do
        w.DeliverFrame() |> ignore
    settle w
    let struct (_, y, _, _) = ScrollViewer.viewportOf (named host "scroller")
    Assert.True(y > 40.0, $"not scrolled (y {y})")
    Assert.Equal(Rect.Create(0.0, 0.0, 0.0, 0.0), (named host "anchor").VisibleBounds)
    Assert.Equal(0, flyoutLayers host)

[<Fact>]
let ``a flyout inside a flyout opens above it; a press in the first closes only the second`` () =
    let log = List<string>()
    let inner = TextBlock.create [ TextBlock.name "inner"; TextBlock.text "Inner" ]
    let outer =
        StackPanel.create [
            StackPanel.name "outer"
            StackPanel.width 120.0
            StackPanel.children [
                Button.create [ Button.name "sub"; Button.height 30.0; Button.flyout [ Flyout.placement Placement.Right; Flyout.content inner ] ]
                Button.create [ Button.name "other"; Button.height 30.0; Button.onClick (fun _ -> log.Add "other") ]
            ]
        ]
    let w, host = start (appShowing outer log 20.0 20.0 [] [])
    click w 50.0 30.0
    settle w
    let sub = rectOf (named host "sub")
    click w (sub.X + 5.0) (sub.Y + 5.0)
    settle w
    Assert.Equal(2, flyoutLayers host)
    let other = rectOf (named host "other")
    click w (other.X + 5.0) (other.Y + 5.0)
    settle w
    Assert.Equal(1, flyoutLayers host)
    Assert.True((tryNamed host "outer").IsSome)
    Assert.Empty log

[<Fact>]
let ``an open flyout draws no frames while nothing changes`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [])
    click w 50.0 30.0
    settle w
    Assert.Equal(1, flyoutLayers host)
    Assert.False(w.FrameRequested)

[<Fact>]
let ``the flyout's content takes the theme's flyout look`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [])
    click w 50.0 30.0
    settle w
    let presenter = (named host "menu").Parent.Value
    Assert.Equal("Flyout", presenter.Type.Name)
    // The Flyout is its layer's root: nothing between them.
    Assert.True(presenter.Parent.IsNone)
    Assert.Equal(host.WindowTheme.Get Palette.surface, presenter.Get BorderProps.background)

[<Fact>]
let ``one flyout's own settings win over the theme's look`` () =
    let log = List<string>()
    let w, host = start (appWith log 20.0 20.0 [ Flyout.padding 0.0; Flyout.background (Color.Hex 0x102030) ])
    click w 50.0 30.0
    settle w
    let flyout = (named host "menu").Parent.Value
    Assert.Equal(Thickness.Uniform 0.0, flyout.Get BorderProps.padding)
    Assert.Equal(Color.Hex 0x102030, flyout.Get BorderProps.background)
    // The 1-DIP line stays: 20 + 4 below the anchor's bottom (60), then the line.
    Assert.Equal(65.0, (named host "menu").PositionInTree.Y)

[<Fact>]
let ``changing the placement of an open flyout places it again`` () =
    let log = List<string>()
    let w, host = start (appWith log 100.0 100.0 [])
    click w 150.0 120.0
    settle w
    Assert.Equal(144.0, (rectOf (named host "menu").Parent.Value).Y)
    host.Update(appWith log 100.0 100.0 [ Flyout.placement Placement.Right ])
    settle w
    let r = rectOf (named host "menu").Parent.Value
    Assert.Equal((204.0, 100.0), (r.X, r.Y))
    Assert.Equal(1, flyoutLayers host)

[<Fact>]
let ``one Flyout settings list on two buttons: each opens its own`` () =
    let log = List<string>()
    let shared: Setting<Flyout> list = [ Flyout.content (TextBlock.create [ TextBlock.name "shared"; TextBlock.text "Shared" ]) ]
    let view =
        Canvas.create [
            Canvas.children [
                Button.create [ Button.name "one"; Button.width 100.0; Button.height 40.0; Canvas.left 20.0; Canvas.top 20.0; Button.flyout shared ]
                Button.create [ Button.name "two"; Button.width 100.0; Button.height 40.0; Canvas.left 200.0; Canvas.top 20.0; Button.flyout shared ]
            ]
        ]
    let w, host = start view
    click w 250.0 30.0
    settle w
    Assert.Equal(1, flyoutLayers host)
    Assert.Equal(200.0, (rectOf (named host "shared").Parent.Value).X)
    // A press on the first button closes the second's flyout (light dismiss) and goes no further.
    click w 50.0 30.0
    settle w
    Assert.Equal(0, flyoutLayers host)
    click w 50.0 30.0
    settle w
    Assert.Equal(20.0, (rectOf (named host "shared").Parent.Value).X)
    ignore log

// --- Tooltips --------------------------------------------------------------------------------------

let private tipApp () =
    Canvas.create [
        Canvas.children [
            Button.create [ Button.name "a"; Button.width 100.0; Button.height 40.0; Canvas.left 20.0; Canvas.top 20.0; Button.tooltip "Save" ]
            Button.create [ Button.name "b"; Button.width 100.0; Button.height 40.0; Canvas.left 140.0; Canvas.top 20.0; Button.tooltip "Open" ]
            Border.create [ Border.name "plain"; Border.width 100.0; Border.height 40.0; Canvas.left 260.0; Canvas.top 20.0 ]
        ]
    ]

let private startTips () = start (tipApp ())

let private tipLayers (host: ElementHost) = host.Layers |> Seq.filter (fun l -> l.Name = "Tooltip") |> Seq.length

let private tipText (host: ElementHost) =
    host.Layers
    |> Seq.tryFind (fun l -> l.Name = "Tooltip")
    |> Option.bind (fun l ->
        let rec text (e: Element) : string option =
            if e.Type.Name = "TextBlock" then Some(e.Get TextProps.text) else e.Children |> Seq.tryPick text
        l.Tree.Root |> ValueOption.toOption |> Option.bind text)

[<Fact>]
let ``a tooltip shows after the pointer rests half a second, below the pointer`` () =
    let w, host = startTips ()
    move w 50.0 30.0
    w.Time.AdvanceMs 400.0
    Assert.Equal(0, tipLayers host)
    move w 52.0 30.0
    w.Time.AdvanceMs 400.0
    Assert.Equal(0, tipLayers host)
    w.Time.AdvanceMs 100.0
    settle w
    Assert.Equal(Some "Save", tipText host)
    let presenter = (host.Layers |> Seq.find (fun l -> l.Name = "Tooltip")).Tree.Root.Value
    Assert.Equal("Tooltip", presenter.Type.Name)
    Assert.Equal(52.0, presenter.PositionInTree.X)
    Assert.Equal(46.0, presenter.PositionInTree.Y)
    // Never in the way: the pointer goes through it.
    Assert.True((host.Layers |> Seq.find (fun l -> l.Name = "Tooltip")).Tree.HitTest(60.0, 50.0).IsNone)

[<Fact>]
let ``moving to another element with a tooltip shows its tooltip at once; after a while it waits again`` () =
    let w, host = startTips ()
    move w 50.0 30.0
    w.Time.AdvanceMs 500.0
    settle w
    move w 150.0 30.0
    settle w
    Assert.Equal(Some "Open", tipText host)
    // No frames in between: hover tints animate, and frames move the fake clock.
    move w 300.0 30.0
    Assert.Equal(0, tipLayers host)
    w.Time.AdvanceMs 200.0
    move w 50.0 30.0
    Assert.Equal(Some "Save", tipText host)
    move w 300.0 30.0
    w.Time.AdvanceMs 400.0
    move w 150.0 30.0
    settle w
    Assert.Equal(0, tipLayers host)

[<Fact>]
let ``a press, the wheel or a key hides the tooltip, and it stays hidden until the pointer leaves`` () =
    let w, host = startTips ()
    move w 50.0 30.0
    w.Time.AdvanceMs 500.0
    settle w
    Assert.Equal(1, tipLayers host)
    press w 50.0 30.0
    release w 50.0 30.0
    settle w
    Assert.Equal(0, tipLayers host)
    move w 55.0 30.0
    w.Time.AdvanceMs 600.0
    Assert.Equal(0, tipLayers host)
    move w 300.0 30.0
    move w 50.0 30.0
    w.Time.AdvanceMs 500.0
    Assert.Equal(1, tipLayers host)
    key w Key.Down
    Assert.Equal(0, tipLayers host)
    move w 300.0 30.0
    move w 50.0 30.0
    w.Time.AdvanceMs 500.0
    w.Send(PointerWheel({ X = 50.0; Y = 30.0 }, 0.0, -1.0, Modifiers.None))
    Assert.Equal(0, tipLayers host)

[<Fact>]
let ``no tooltips on touch, and none after the pointer leaves the window`` () =
    let w, host = startTips ()
    w.Send(PointerMoved(Touch, 1, { X = 50.0; Y = 30.0 }, Modifiers.None))
    w.Time.AdvanceMs 600.0
    Assert.Equal(0, tipLayers host)
    move w 50.0 30.0
    w.Send(PointerExited(Mouse, 0))
    w.Time.AdvanceMs 600.0
    Assert.Equal(0, tipLayers host)

[<Fact>]
let ``a tooltip view shows any view`` () =
    let view =
        Border.create [
            Border.name "target"
            Border.tooltipView (TextBlock.create [ TextBlock.text "Custom" ])
        ]
    let w, host = start view
    move w 50.0 30.0
    w.Time.AdvanceMs 500.0
    settle w
    Assert.Equal(Some "Custom", tipText host)

// --- A flyout is styled as if inside its element (round 1c) ----------------------------------------------------------

/// Two buttons at (20, 20) and (200, 20), each with a flyout showing a TextBlock named after it.
let private twoAnchors (containerSettings: Setting<Canvas> list) (one: Setting<Button> list) (two: Setting<Button> list) =
    let anchor name x (settings: Setting<Button> list) =
        Button.create (
            [ Button.name name; Button.width 100.0; Button.height 40.0; Canvas.left x; Canvas.top 20.0
              Button.flyout (TextBlock.create [ TextBlock.name (name + "-text"); TextBlock.text name ]) ]
            @ settings)
    Canvas.create (containerSettings @ [ Canvas.children [ anchor "one" 20.0 one; anchor "two" 200.0 two ] ])

/// Opens the flyout of the button at x and gives the Flyout element around `textName`.
let private openAt (w: FakeWindow) (host: ElementHost) x textName =
    click w x 30.0
    settle w
    (named host textName).Parent.Value

let private closeAll (w: FakeWindow) =
    key w Key.Escape
    settle w

[<Fact>]
let ``a style in the element's styles reaches its flyout and no other`` () =
    let w, host = start (twoAnchors [] [ Button.styles [ Flyout.style [] [ Flyout.padding 0.0 ] ] ] [])
    Assert.Equal(Thickness.Uniform 0.0, (openAt w host 50.0 "one-text").Get BorderProps.padding)
    closeAll w
    Assert.Equal(Thickness.Uniform 4.0, (openAt w host 250.0 "two-text").Get BorderProps.padding)

[<Fact>]
let ``selectors pick flyouts by their element and what it's inside`` () =
    let styles =
        [ Flyout.style [ Style.parent Button.Type [ Style.cls "toolbar" ] ] [ Flyout.padding 0.0 ]
          Flyout.style [ Style.inside Canvas.Type [ Style.name "tools" ] ] [ Flyout.cornerRadius 9.0 ] ]
    let w, host = start (twoAnchors [ Canvas.name "tools"; Canvas.styles styles ] [ Button.classes [ "toolbar" ] ] [])
    let one = openAt w host 50.0 "one-text"
    Assert.Equal(Thickness.Uniform 0.0, one.Get BorderProps.padding)
    Assert.Equal(CornerRadius.Uniform 9.0, one.Get BorderProps.cornerRadius)
    closeAll w
    let two = openAt w host 250.0 "two-text"
    Assert.Equal(Thickness.Uniform 4.0, two.Get BorderProps.padding)
    Assert.Equal(CornerRadius.Uniform 9.0, two.Get BorderProps.cornerRadius)

[<Fact>]
let ``a theme around the element is its flyout's, but the element's own text colour is not`` () =
    let light = Theme.light []
    let red = Color.Hex 0xff0000
    let view = Border.create [ Border.theme light; Border.content (twoAnchors [] [ Setting.create InheritedProps.foreground red ] []) ]
    let w, host = start view
    let flyout = openAt w host 50.0 "one-text"
    Assert.Equal(light.Get Palette.surface, flyout.Get BorderProps.background)
    Assert.NotEqual(host.WindowTheme.Get Palette.surface, light.Get Palette.surface)
    let text = named host "one-text"
    Assert.Equal(light.Get Palette.text, text.Get InheritedProps.foreground)

[<Fact>]
let ``a class given to the element while its flyout is open restyles the flyout`` () =
    let styles = [ Flyout.style [ Style.parent Button.Type [ Style.cls "toolbar" ] ] [ Flyout.padding 0.0 ] ]
    let w, host = start (twoAnchors [ Canvas.styles styles ] [] [])
    let flyout = openAt w host 50.0 "one-text"
    Assert.Equal(Thickness.Uniform 4.0, flyout.Get BorderProps.padding)
    host.Update(twoAnchors [ Canvas.styles styles ] [ Button.classes [ "toolbar" ] ] [])
    settle w
    Assert.Equal(Thickness.Uniform 0.0, flyout.Get BorderProps.padding)

[<Fact>]
let ``a tooltip is styled as if inside its element too`` () =
    let view =
        Canvas.create [
            Canvas.styles [ Tooltip.style [] [ Border.padding 0.0 ] ]
            Canvas.children [
                Button.create [ Button.name "b"; Button.width 100.0; Button.height 40.0; Canvas.left 20.0; Canvas.top 20.0; Button.tooltip "Tip" ]
            ]
        ]
    let w, host = start view
    move w 50.0 30.0
    w.Time.AdvanceMs 500.0
    settle w
    let tip = host.Layers |> Seq.find (fun l -> l.Name = "Tooltip")
    Assert.Equal(Thickness.Uniform 0.0, tip.Tree.Root.Value.Get BorderProps.padding)
