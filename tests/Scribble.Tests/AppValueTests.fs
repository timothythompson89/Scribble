/// App values: state declared once (`AppValue.define`) and shared by any component that uses it.
module Scribble.Tests.AppValueTests

open System
open System.Collections.Generic
open System.Threading
open Xunit
open Scribble.Core
open Scribble.Tests.TestShaping
open Scribble.Tests.AppTests

let private lightMode = AppValue.define "LightMode" false
let private userName = AppValue.define "UserName" "nobody"

let private frame (t: ElementTree) =
    t.UpdateLayout(Size.Create(400.0, 300.0))
    t.RunAfterFrame()

let rec private texts (e: Element) : string list =
    [ if obj.ReferenceEquals(e.Type, TextBlock.Type) then e.Get TextProps.text
      for c in e.Children do
          yield! texts c ]

/// Shows the value; counts its runs under `name`.
let private reader (runs: List<string>) =
    Component.define "Reader" (fun ctx (name: string) ->
        runs.Add name
        let light = ctx.useAppValue lightMode
        TextBlock.create [ TextBlock.text (name + (if light.Current then " light" else " dark")) ])

[<Fact>]
let ``components share an app value, and only those that read it re-run`` () =
    let runs = List<string>()
    let reader = reader runs
    let mutable switch: State<bool> voption = ValueNone
    let switcher =
        Component.define "Switch" (fun ctx () ->
            runs.Add "switch"
            switch <- ValueSome(ctx.useAppValue lightMode)
            TextBlock.create [ TextBlock.text "switch" ])
    let other = Component.define "Other" (fun _ () -> runs.Add "other"; TextBlock.create [ TextBlock.text "other" ])
    let t = ElementTree(textSystem ())
    t.Update(StackPanel.create [ StackPanel.children [ reader "a"; StackPanel.create [ StackPanel.children [ reader "b" ] ]; switcher (); other () ] ])
    frame t
    Assert.Equal<string list>([ "a dark"; "b dark"; "switch"; "other" ], texts t.Root.Value)
    runs.Clear()
    // Set from one component: every reader re-runs, the one that doesn't read it doesn't.
    switch.Value.Set true
    frame t
    Assert.Equal<string list>([ "a light"; "b light"; "switch"; "other" ], texts t.Root.Value)
    Assert.Equal<string list>([ "a"; "b"; "switch" ], List.ofSeq runs |> List.sort)
    Assert.True(t.AppValues.Get lightMode)
    // An equal value changes nothing.
    runs.Clear()
    t.AppValues.Set(lightMode, true)
    frame t
    Assert.Empty runs

[<Fact>]
let ``each bare tree has its own values, and a removed component stops listening`` () =
    let runs = List<string>()
    let reader = reader runs
    let a, b = ElementTree(textSystem ()), ElementTree(textSystem ())
    a.Update(reader "a")
    b.Update(reader "b")
    frame a
    frame b
    a.AppValues.Set(lightMode, true)
    frame a
    frame b
    Assert.Equal<string list>([ "a light" ], texts a.Root.Value)
    Assert.Equal<string list>([ "b dark" ], texts b.Root.Value)
    // Gone from the tree: a later change doesn't reach it (nor fail).
    a.Update(TextBlock.create [ TextBlock.text "none" ])
    frame a
    runs.Clear()
    a.AppValues.Set(lightMode, false)
    frame a
    Assert.Empty runs

[<Fact>]
let ``a component that switches to another value listens to that one`` () =
    let first = AppValue.define "First" "first"
    let pickFirst =
        Component.define "PickFirst" (fun ctx (which: bool) ->
            let name = ctx.useAppValue (if which then userName else first)
            TextBlock.create [ TextBlock.text name.Current ])
    let t = ElementTree(textSystem ())
    t.Update(pickFirst false)
    frame t
    Assert.Equal<string list>([ "first" ], texts t.Root.Value)
    t.Update(pickFirst true)
    frame t
    Assert.Equal<string list>([ "nobody" ], texts t.Root.Value)
    t.AppValues.Set(userName, "Tim")
    frame t
    Assert.Equal<string list>([ "Tim" ], texts t.Root.Value)
    // The value it used before no longer re-runs it.
    let mutable invalidated = 0
    t.OnInvalidated <- fun () -> invalidated <- invalidated + 1
    t.AppValues.Set(first, "changed")
    Assert.Equal(0, invalidated)

[<Fact>]
let ``Cmd.setAppValue sets it from an Elmish component, and Hooks show it by name`` () =
    let runs = List<string>()
    let reader = reader runs
    let elmish =
        Component.define "Elmish" (fun ctx () ->
            let _, dispatch = ctx.useElmishWithCmd(((), Cmd.none), fun (light: bool) () -> (), Cmd.setAppValue lightMode light)
            Button.create [ Button.text "go"; Button.onClick (fun _ -> dispatch true) ])
    let t = ElementTree(textSystem ())
    t.Update(StackPanel.create [ StackPanel.children [ elmish (); reader "r" ] ])
    frame t
    let button = t.Root.Value.Children[0].Children[0]
    (button.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
    frame t
    Assert.Equal<string list>([ "go"; "r light" ], texts t.Root.Value)
    let hooks = t.Root.Value.Children[1].Hooks
    Assert.Equal("useAppValue LightMode", hooks[0].Kind)
    Assert.Equal(ValueSome(box true), hooks[0].Value)

[<Fact>]
let ``in an app: a starting value, shared by every layer, set through AppContext and from other threads`` () =
    let runs = List<string>()
    let reader = reader runs
    let mutable context: AppContext voption = ValueNone
    let platform, device = new FakePlatform(), new FakeDevice()
    use app =
        App.start (
            platform,
            device,
            new FixedShaper(),
            [ App.view (reader "app")
              App.value lightMode true
              App.layer [ Layer.name "Toasts"; Layer.view (reader "toast") ]
              App.onStart (fun c -> context <- ValueSome c) ]
        )
    let w = platform.Window.Value
    let host = app :?> ElementHost
    let shown () = [ for l in host.Layers do yield! texts l.Tree.Root.Value ]
    w.DeliverFrame() |> ignore
    Assert.Equal<string list>([ "app light"; "toast light" ], shown ())
    Assert.True(context.Value.Get lightMode)
    context.Value.Set(lightMode, false)
    w.DeliverFrame() |> ignore
    Assert.Equal<string list>([ "app dark"; "toast dark" ], shown ())
    // From another thread: queued to the UI thread (the platform is woken to run it).
    let other = Thread(fun () -> host.AppValues.Set(lightMode, true))
    other.Start()
    other.Join()
    Assert.False(host.AppValues.Get lightMode)
    platform.RunWake()
    w.DeliverFrame() |> ignore
    Assert.Equal<string list>([ "app light"; "toast light" ], shown ())

[<Fact>]
let ``the Gallery's switch sets Theme.current, and the window and its layers follow`` () =
    let w, device, app = start Scribble.Samples.GalleryApp.app
    let host = app :?> ElementHost
    let light, dark = Scribble.Samples.Gallery.Common.lightTheme, Scribble.Samples.Gallery.Common.darkTheme
    w.DeliverFrame() |> ignore
    Assert.Same(dark, host.WindowTheme)
    let rec all (e: Element) : Element seq = seq { yield e; for c in e.Children do yield! all c }
    let switch () = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Switch.Type))
    Assert.False(switch().Get ToggleButtonProps.isChecked)
    // Click it in the middle.
    let s = switch ()
    let p = s.PositionInTree
    let at: Scribble.Platform.PixelPoint = { X = p.X + s.Bounds.Width / 2.0; Y = p.Y + s.Bounds.Height / 2.0 }
    w.Send(Scribble.Platform.PointerPressed(Scribble.Platform.Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(Scribble.Platform.PointerReleased(Scribble.Platform.Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.DeliverFrame() |> ignore
    Assert.Same(light, host.WindowTheme)
    Assert.Equal(light.Get Palette.background, device.LastClear)
    // Controlled by the app value: on now.
    Assert.True(switch().Get ToggleButtonProps.isChecked)
    for l in host.Layers do
        Assert.Same(light, l.Tree.Theme.Value)

[<Fact>]
let ``the window title is an app value: App.title starts it, sets change the window's title`` () =
    let mutable context: AppContext voption = ValueNone
    let seen = List<string>()
    let platform, device = new FakePlatform(), new FakeDevice()
    use app =
        App.start (platform, device, new FixedShaper(), [ App.title "Start"; App.view (Border.create []); App.onStart (fun c -> context <- ValueSome c) ])
    let w = platform.Window.Value
    let host = app :?> ElementHost
    Assert.Equal("Start", w.TitleText)
    Assert.Equal("Start", context.Value.Title)
    use _ = host.AppValues.Subscribe(WindowTitle.current, fun t -> seen.Add t)
    // Through AppContext: at once, and AppContext.Title follows.
    context.Value.Set(WindowTitle.current, "Second")
    Assert.Equal("Second", w.TitleText)
    Assert.Equal("Second", context.Value.Title)
    // An equal value changes nothing.
    context.Value.Set(WindowTitle.current, "Second")
    // From another thread: queued to the UI thread.
    let other = Thread(fun () -> host.AppValues.Set(WindowTitle.current, "Third"))
    other.Start()
    other.Join()
    Assert.Equal("Second", w.TitleText)
    platform.RunWake()
    Assert.Equal("Third", w.TitleText)
    Assert.Equal<string list>([ "Second"; "Third" ], List.ofSeq seen)

[<Fact>]
let ``without App.title the window is called Scribble; with showStats the stats follow the current title`` () =
    let w, _, app = start [ App.view (Border.create []) ]
    Assert.Equal("Scribble", w.TitleText)
    let w2, _, app2 = start [ App.title "Stats"; App.showStats true; App.view (Border.create []) ]
    let host = app2 :?> ElementHost
    host.AppValues.Set(WindowTitle.current, "Renamed")
    w2.DeliverFrame() |> ignore
    // The stats are added once a second, after whatever the title is now.
    Assert.StartsWith("Renamed", w2.TitleText)
    app.Dispose()
    app2.Dispose()

[<Fact>]
let ``the Gallery's title names the selected page`` () =
    let w, _, app = start Scribble.Samples.GalleryApp.app
    let host = app :?> ElementHost
    w.DeliverFrame() |> ignore
    Assert.Equal("Gallery – Components", w.TitleText)
    let rec all (e: Element) : Element seq = seq { yield e; for c in e.Children do yield! all c }
    let tab =
        all host.Tree.Root.Value
        |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && texts e = [ "Styles" ])
    (tab.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
    Assert.Equal("Gallery – Styles", w.TitleText)
    app.Dispose()
