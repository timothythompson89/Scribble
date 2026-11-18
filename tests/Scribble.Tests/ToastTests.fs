/// M8 step 4d: toasts on the fake platform (DESIGN.md, "4d: Dialog and Toast"): a Toaster's layer, how long a toast
/// stays (fake time, nothing waiting on frames), three at a time, the pointer holding one, the close and action
/// buttons, the keyboard staying where it was, where they sit, and semantics.
module Scribble.Tests.ToastTests

open System
open System.Collections.Generic
open System.Threading.Tasks
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
open Scribble.Tests.AppTests

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

/// Runs what was posted to the UI thread, then the frames it asked for.
let private pump (w: FakeWindow) (host: ElementHost) =
    for _ in 1..3 do
        host.Dispatcher.Value.RunPending()
        let mutable n = 0
        while w.DeliverFrame() && n < 30 do
            n <- n + 1

let private app =
    StackPanel.create [
        StackPanel.children [ Button.create [ Button.name "one"; Button.text "One" ]; Button.create [ Button.name "two"; Button.text "Two" ] ]
    ]

let private show (extra: AppSetting<unit> list) =
    let w, _, a = start ([ App.view app ] @ extra)
    let host = a :?> ElementHost
    pump w host
    w, host

/// Every box in the toasts' layer (made with the first toast): the toasts, and the line that says how many wait.
let private boxes (host: ElementHost) =
    match host.Layers |> Seq.tryFind (fun l -> l.Name = "Toasts") |> Option.bind (fun l -> ValueOption.toOption l.Tree.Root) with
    | Some root -> all root |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, ToastPresenter.Type)) |> List.ofSeq
    | None -> []
let private isMore (e: Element) = List.contains "more" (e.Get LayoutProps.classes)
/// The toasts showing.
let private toasts (host: ElementHost) = boxes host |> List.filter (fun e -> not (isMore e))
/// The line above the stack that says how many wait.
let private more (host: ElementHost) = boxes host |> List.tryFind isMore
let private textOf (e: Element) =
    all e |> Seq.filter (fun x -> obj.ReferenceEquals(x.Type, TextBlock.Type)) |> Seq.map (fun x -> x.Get TextProps.text) |> String.concat " "
let private texts (host: ElementHost) = toasts host |> List.map textOf
let private buttons (e: Element) = all e |> Seq.filter (fun x -> obj.ReferenceEquals(x.Type, Button.Type)) |> List.ofSeq

let private advance (w: FakeWindow) (host: ElementHost) (ms: float) =
    w.Time.AdvanceMs ms
    pump w host

let private clickOn (w: FakeWindow) (host: ElementHost) (e: Element) =
    let p = e.PositionInTree
    let at: PixelPoint = { X = p.X + e.Bounds.Width / 2.0; Y = p.Y + e.Bounds.Height / 2.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    pump w host

[<Fact>]
let ``a toast shows in the Toasts layer, fully there after it slid in, and goes after four seconds`` () =
    let w, host = show []
    let toaster = host.Toaster
    Assert.Empty(toasts host)
    toaster.Show "Saved"
    pump w host
    Assert.Equal<string list>([ "Saved" ], texts host)
    let toast = (toasts host).Head
    Assert.Equal(1.0, toast.Get LayoutProps.opacity)
    advance w host 3900.0
    Assert.Equal<string list>([ "Saved" ], texts host)
    advance w host 400.0
    Assert.Empty(toasts host)
    Assert.Equal(0, toaster.Count)

[<Fact>]
let ``nothing asks for frames while a toast waits`` () =
    let w, host = show []
    let toaster = host.Toaster
    toaster.Show "Saved"
    pump w host
    w.Time.AdvanceMs 2000.0
    host.Dispatcher.Value.RunPending()
    Assert.False w.FrameRequested

[<Fact>]
let ``Show from another thread reaches the UI thread`` () =
    let w, host = show []
    let toaster = host.Toaster
    Task.Run(fun () -> toaster.Show "From a task").Wait()
    pump w host
    Assert.Equal<string list>([ "From a task" ], texts host)

[<Fact>]
let ``a Critical toast stays until its close button is clicked`` () =
    let w, host = show []
    let toaster = host.Toaster
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "Couldn't save" ]
    pump w host
    advance w host 60_000.0
    Assert.Equal<string list>([ "Couldn't save" ], texts host)
    Assert.Contains("critical", (toasts host).Head.Get LayoutProps.classes)
    clickOn w host (buttons (toasts host).Head |> List.last)
    advance w host 200.0
    Assert.Empty(toasts host)

[<Fact>]
let ``a duration of its own`` () =
    let w, host = show []
    let toaster = host.Toaster
    toaster.Show [ Toast.text "Quick"; Toast.duration (TimeSpan.FromSeconds 1.0) ]
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "Timed error"; Toast.duration (TimeSpan.FromSeconds 2.0) ]
    pump w host
    advance w host 1200.0
    Assert.Equal<string list>([ "Timed error" ], texts host)
    advance w host 1200.0
    Assert.Empty(toasts host)

[<Fact>]
let ``at most three show, newest lowest; the rest wait their turn, and their time starts when they show`` () =
    let w, host = show []
    let toaster = host.Toaster
    for i in 1..5 do
        toaster.Show("Toast " + string i)
    pump w host
    Assert.Equal<string list>([ "Toast 1"; "Toast 2"; "Toast 3" ], texts host)
    Assert.Equal(5, toaster.Count)
    let ys = toasts host |> List.map (fun e -> e.PositionInTree.Y)
    Assert.Equal<float list>(List.sort ys, ys)
    // The first three go together; the two waiting take their places with a full four seconds.
    advance w host 4200.0
    Assert.Equal<string list>([ "Toast 4"; "Toast 5" ], texts host)
    advance w host 3000.0
    Assert.Equal<string list>([ "Toast 4"; "Toast 5" ], texts host)
    advance w host 1500.0
    Assert.Empty(toasts host)

[<Fact>]
let ``a toast waits while the pointer is over it, and its time starts again when the pointer leaves`` () =
    let w, host = show []
    let toaster = host.Toaster
    toaster.Show "Hold me"
    pump w host
    let toast = (toasts host).Head
    let p = toast.PositionInTree
    w.Send(PointerMoved(Mouse, 0, { X = p.X + 10.0; Y = p.Y + 10.0 }, Modifiers.None))
    advance w host 10_000.0
    Assert.Equal<string list>([ "Hold me" ], texts host)
    w.Send(PointerMoved(Mouse, 0, { X = 5.0; Y = 5.0 }, Modifiers.None))
    advance w host 3800.0
    Assert.Equal<string list>([ "Hold me" ], texts host)
    advance w host 500.0
    Assert.Empty(toasts host)

[<Fact>]
let ``an action runs its handler and closes the toast`` () =
    let log = List<string>()
    let w, host = show []
    let toaster = host.Toaster
    toaster.Show [ Toast.text "Message deleted"; Toast.action ("Undo", fun () -> log.Add "undo") ]
    pump w host
    let undo = buttons (toasts host).Head |> List.head
    Assert.Equal("Undo", textOf undo)
    clickOn w host undo
    Assert.Equal<string list>([ "undo" ], List.ofSeq log)
    advance w host 200.0
    Assert.Empty(toasts host)

[<Fact>]
let ``a press on a toast doesn't take the keyboard from the app`` () =
    let w, host = show []
    let toaster = host.Toaster
    let one = all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = "one")
    one.Focus()
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "Still here"; Toast.action ("Retry", ignore) ]
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "And me" ]
    pump w host
    clickOn w host (buttons (toasts host).Head |> List.last)
    advance w host 200.0
    Assert.Equal<string list>([ "And me" ], texts host)
    Assert.True one.IsFocused
    Assert.Equal("one", host.Focused.Value.Name)
    // Tab stays in the app: it never reaches a toast's buttons.
    w.Send(KeyDown(Key.Tab, Modifiers.None, false))
    pump w host
    Assert.Equal("two", host.Focused.Value.Name)
    w.Send(KeyDown(Key.Tab, Modifiers.None, false))
    pump w host
    Assert.Equal("one", host.Focused.Value.Name)

[<Fact>]
let ``in a narrow window toasts go across the bottom; in a wide one, at the bottom right, at most 360 wide`` () =
    let w, host = show []
    let toaster = host.Toaster
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "A fairly long message that would like more room than a narrow window has to give it" ]
    pump w host
    let toast () = (toasts host).Head
    // 400 DIPs wide: narrow.
    Assert.Equal((16.0, 368.0), ((toast ()).PositionInTree.X, (toast ()).Bounds.Width))
    Assert.Equal(300.0 - 16.0, (toast ()).PositionInTree.Y + (toast ()).Bounds.Height)
    w.Size <- { Width = 900; Height = 600 }
    w.Send(Resized w.Size)
    pump w host
    Assert.True((toast ()).Bounds.Width <= 360.0)
    Assert.Equal(900.0 - 16.0, (toast ()).PositionInTree.X + (toast ()).Bounds.Width)
    Assert.Equal(600.0 - 16.0, (toast ()).PositionInTree.Y + (toast ()).Bounds.Height)

[<Fact>]
let ``only the toasts take the pointer: the app under the rest of their layer still gets clicks`` () =
    let log = List<string>()
    let view = Button.create [ Button.name "big"; Button.text "Big"; Button.onClick (fun _ -> log.Add "big") ]
    let w, _, a = start [ App.view view ]
    let host = a :?> ElementHost
    let toaster = host.Toaster
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "Here" ]
    pump w host
    w.Send(PointerPressed(Mouse, 0, { X = 50.0; Y = 50.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 50.0; Y = 50.0 }, MouseButton.Left, Modifiers.None))
    pump w host
    Assert.Equal<string list>([ "big" ], List.ofSeq log)

[<Fact>]
let ``toasts are above dialogs and flyouts, and a toast's node is an Alert named by its text`` () =
    let view =
        Modal.create [ Modal.isOpen true; Modal.title "Sure?"; Modal.content (Button.create [ Button.name "more"; Button.text "More"; Button.flyout (TextBlock.create [ TextBlock.text "Inside" ]) ]) ]
    let w, _, a = start [ App.view view ]
    let host = a :?> ElementHost
    let toaster = host.Toaster
    pump w host
    let more = host.Layers |> Seq.collect (fun l -> l.Tree.Root |> ValueOption.toArray |> Seq.collect all) |> Seq.find (fun e -> e.Name = "more")
    clickOn w host more
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "Offline" ]
    pump w host
    Assert.Equal<string list>([ "App"; "Modal"; "Flyout of more"; "Toasts" ], host.Layers |> Seq.map _.Name |> List.ofSeq)
    Assert.True(host.SemanticTree().ToString().Contains("Alert \"Offline\""), host.SemanticTree().ToString())
    // The toast can be closed over the dialog.
    clickOn w host (buttons (toasts host).Head |> List.last)
    advance w host 200.0
    Assert.Empty(toasts host)

[<Fact>]
let ``every host has its own toasts: one shown in a window shows there and nowhere else`` () =
    let w1, host1 = show []
    let w2, host2 = show []
    Assert.False(obj.ReferenceEquals(host1.Toaster, host2.Toaster))
    host1.Toaster.Show "In the first"
    pump w1 host1
    pump w2 host2
    Assert.Equal<string list>([ "In the first" ], texts host1)
    // The second never made a toasts layer: nothing asked it for one.
    Assert.DoesNotContain("Toasts", host2.Layers |> Seq.map _.Name)
    Assert.Equal(0, host2.Toaster.Count)

[<Fact>]
let ``ctx.useToaster gives the toasts of the window the component is in, the same one every run`` () =
    let seen = List<Toaster>()
    let view =
        Component.define "Sender" (fun ctx () ->
            let toaster = ctx.useToaster ()
            seen.Add toaster
            let count = ctx.useState 0
            Button.create [
                Button.name "send"
                Button.text ("Sent " + string count.Current)
                Button.onClick (fun _ ->
                    count.Set(count.Current + 1)
                    toaster.Show "From the component")
            ]) ()
    let w, _, a = start [ App.view view ]
    let host = a :?> ElementHost
    pump w host
    let send () = all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = "send")
    clickOn w host (send ())
    clickOn w host (send ())
    // Shown twice with the same words: one toast, counted.
    Assert.Equal<string list>([ "From the component  ×2" ], texts host)
    Assert.True(seen.Count >= 2)
    Assert.True(seen |> Seq.forall (fun t -> obj.ReferenceEquals(t, seen[0])))
    // It is the host's own toasts it reached.
    Assert.Equal(1, host.Toaster.Count)

[<Fact>]
let ``ctx.useToaster in a tree no host runs shows nothing`` () =
    let mutable toaster = Unchecked.defaultof<Toaster>
    let view =
        Component.define "Loose" (fun ctx () ->
            toaster <- ctx.useToaster ()
            Border.create []) ()
    let tree = ElementTree(Scribble.Tests.TestShaping.textSystem ())
    tree.Update view
    toaster.Show "Nobody sees this"
    toaster.Show "Nor this"
    Assert.Equal(0, toaster.Count)

[<Fact>]
let ``Cmd.toast and Cmd.toastWith show in the program's window`` () =
    let update (text: string) (model: int) =
        model + 1, (if text = "error" then Cmd.toastWith [ Toast.severity Severity.Critical; Toast.text "Failed" ] else Cmd.toast text)
    let view (_: int) (dispatch: string -> unit) =
        StackPanel.create [
            StackPanel.children [
                Button.create [ Button.name "save"; Button.text "Save"; Button.onClick (fun _ -> dispatch "Saved") ]
                Button.create [ Button.name "fail"; Button.text "Fail"; Button.onClick (fun _ -> dispatch "error") ]
            ]
        ]
    let w, _, a = start [ App.programWithCmd (0, Cmd.none) update view ]
    let host = a :?> ElementHost
    pump w host
    clickOn w host (all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = "save"))
    clickOn w host (all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = "fail"))
    Assert.Equal<string list>([ "Saved"; "Failed" ], texts host)

[<Fact>]
let ``App.onError gets the app's context, so an error can show as a toast in its window`` () =
    let view = Button.create [ Button.name "break"; Button.text "Break"; Button.onClick (fun _ -> failwith "It broke") ]
    let w, _, a = start [ App.view view; App.onError (fun app e -> app.Toaster.Show [ Toast.severity Severity.Critical; Toast.text e.Message ]) ]
    let host = a :?> ElementHost
    pump w host
    clickOn w host (all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = "break"))
    Assert.Equal<string list>([ "It broke" ], texts host)

[<Fact>]
let ``a Warning toast has its own look and stays eight seconds; Toast.style reaches toasts by severity`` () =
    let styles = [ Toast.style [ Style.cls "warning" ] [ Border.padding 21.0 ] ]
    let w, _, a = start [ App.view app; App.styles styles ]
    let host = a :?> ElementHost
    pump w host
    host.Toaster.Show [ Toast.severity Severity.Warning; Toast.text "Low on space" ]
    host.Toaster.Show "Plain"
    pump w host
    let warning, plain = (toasts host)[0], (toasts host)[1]
    Assert.Contains("warning", warning.Get LayoutProps.classes)
    Assert.Equal((Theme.dark []).Get Palette.warning, warning.Get BorderProps.background)
    Assert.Equal(21.0, (warning.Get BorderProps.padding).Left)
    Assert.NotEqual(21.0, (plain.Get BorderProps.padding).Left)
    // The plain one goes after four seconds, the warning after eight.
    advance w host 4500.0
    Assert.Equal<string list>([ "Low on space" ], texts host)
    advance w host 4000.0
    Assert.Empty(toasts host)

[<Fact>]
let ``a toast's close icon is centred on its text's letters: the button's middle is the line's, and the icon sits lower by the text's optical nudge`` () =
    let w, host = show []
    host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text "Here" ]
    host.Toaster.Show [ Toast.text "With an action"; Toast.action ("Undo", ignore) ]
    pump w host
    let middle (e: Element) = e.PositionInTree.Y + e.Bounds.Height / 2.0
    for toast in toasts host do
        let text = all toast |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, TextBlock.Type))
        let close = buttons toast |> List.last
        // The same middle (were the button an even height against the line's odd one, they'd be half a DIP apart).
        Assert.Equal(middle text, middle close)
        // The icon's box starts below the padding by the nudge: on the letters' middle, not the line's.
        let icon = close.Get ButtonProps.iconRect
        let padding = (close.Get BorderProps.padding).Top
        Assert.True(icon.Y > padding, "the icon isn't nudged: " + string icon.Y)
        Assert.True(icon.Y - padding < 2.0)
    // With and without an action, toasts are the same height.
    let shown = toasts host
    Assert.Equal(shown[0].Bounds.Height, shown[1].Bounds.Height)

// --- Many toasts: repeats, what waits, order, a limit (Tim, 2026-11-18) ---------------------------

[<Fact>]
let ``the same toast shown again is one toast with a count, and its time starts over`` () =
    let w, host = show []
    let toaster = host.Toaster
    toaster.Show "Saved"
    pump w host
    advance w host 3000.0
    toaster.Show "Saved"
    toaster.Show "Saved"
    pump w host
    Assert.Equal<string list>([ "Saved  ×3" ], texts host)
    Assert.Equal(1, toaster.Count)
    // Four seconds from the last one, not from the first.
    advance w host 3000.0
    Assert.Equal<string list>([ "Saved  ×3" ], texts host)
    advance w host 1500.0
    Assert.Empty(toasts host)
    // Another severity, an action or a view of its own is another toast.
    toaster.Show "Saved"
    toaster.Show [ Toast.severity Severity.Warning; Toast.text "Saved" ]
    toaster.Show [ Toast.text "Saved"; Toast.action ("Undo", ignore) ]
    pump w host
    Assert.Equal(3, (toasts host).Length)

[<Fact>]
let ``an error that keeps happening is one toast, not a full stack`` () =
    let w, host = show []
    for _ in 1..40 do
        host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text "Couldn't reach the server" ]
    pump w host
    Assert.Equal<string list>([ "Couldn't reach the server  ×40" ], texts host)
    Assert.Equal(1, host.Toaster.Count)

let private real (host: ElementHost) = toasts host

[<Fact>]
let ``when toasts wait, a line under the stack says how many, and Close all clears everything`` () =
    let w, host = show []
    for i in 1..5 do
        host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text ("Error " + string i) ]
    pump w host
    Assert.Equal<string list>([ "Error 1"; "Error 2"; "Error 3" ], real host |> List.map textOf)
    let line = (more host).Value
    Assert.Equal("+2 more Close all", textOf line)
    // It stands under the toasts.
    Assert.True(line.PositionInTree.Y > (real host |> List.last).PositionInTree.Y)
    // Closing one lets the next in, and the line counts down.
    clickOn w host (buttons (real host).Head |> List.last)
    advance w host 200.0
    Assert.Equal<string list>([ "Error 2"; "Error 3"; "Error 4" ], real host |> List.map textOf)
    Assert.Equal("+1 more Close all", textOf (more host).Value)
    clickOn w host (buttons (more host).Value |> List.last)
    advance w host 200.0
    Assert.Empty(boxes host)
    Assert.Equal(0, host.Toaster.Count)

[<Fact>]
let ``a waiting Critical toast gets a place before waiting ones that aren't`` () =
    let w, host = show []
    let toaster = host.Toaster
    for i in 1..3 do
        toaster.Show [ Toast.text ("Plain " + string i); Toast.duration (TimeSpan.FromSeconds 1.0) ]
    toaster.Show "Waiting plain"
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "Waiting error" ]
    pump w host
    Assert.Equal<string list>([ "Plain 1"; "Plain 2"; "Plain 3" ], real host |> List.map textOf)
    advance w host 1300.0
    // The error went first though it was asked for last; the plain one follows.
    Assert.Equal<string list>([ "Waiting error"; "Waiting plain" ], real host |> List.map textOf)
    Assert.True((more host).IsNone)

[<Fact>]
let ``at most fifty wait: past that the oldest that aren't Critical go first`` () =
    let w, host = show []
    let toaster = host.Toaster
    for i in 1..3 do
        toaster.Show [ Toast.severity Severity.Critical; Toast.text ("Showing " + string i) ]
    toaster.Show [ Toast.severity Severity.Critical; Toast.text "An early error" ]
    for i in 1..60 do
        toaster.Show("Plain " + string i)
    pump w host
    // Three showing and fifty waiting.
    Assert.Equal(53, toaster.Count)
    Assert.Equal("+50 more Close all", textOf (more host).Value)
    // The early error is still among them: it shows as soon as there's a place.
    clickOn w host (buttons (real host).Head |> List.last)
    advance w host 200.0
    Assert.Contains("An early error", real host |> List.map textOf)

[<Fact>]
let ``a toast comes up from below its place and goes on upwards as it leaves`` () =
    let w, host = show []
    host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text "Here" ]
    // Asked for, not yet shown: clear, below its place.
    host.Dispatcher.Value.RunPending()
    pump w host
    let toast () = (toasts host).Head
    Assert.Equal(0.0, (toast ()).Get ToastProps.offset)
    Assert.Equal(1.0, (toast ()).Get LayoutProps.opacity)
    // Its close button, without the frames after (it's gone 150 ms later).
    let close = buttons (toast ()) |> List.last
    let at: PixelPoint = { X = close.PositionInTree.X + 5.0; Y = close.PositionInTree.Y + 5.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    // Going: above its place this time (its fade has only begun).
    Assert.True((toast ()).Get ToastProps.offset < 0.0)

// --- Placement (Tim, 2026-11-18): Toasts.placement, an app value -----------------------------------

/// A host 900 × 600 (wide), with the app's settings.
let private wide (extra: AppSetting<unit> list) =
    let w, _, a = start ([ App.view app ] @ extra)
    let host = a :?> ElementHost
    w.Size <- { Width = 900; Height = 600 }
    w.Send(Resized w.Size)
    pump w host
    w, host

let private rect (e: Element) = e.PositionInTree.X, e.PositionInTree.Y, e.Bounds.Width, e.Bounds.Height

[<Fact>]
let ``toasts stack at the bottom right by default, and App.toasts puts them elsewhere`` () =
    let at (settings: ToastsSetting list) =
        let w, host = wide [ if not settings.IsEmpty then App.toasts settings ]
        host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text "Here" ]
        pump w host
        let x, y, width, height = rect (toasts host).Head
        x, y, x + width, y + height
    let _, _, right, bottom = at []
    Assert.Equal((884.0, 584.0), (right, bottom))
    let left, _, _, bottom = at [ Toasts.horizontalAlignment HorizontalAlignment.Left ]
    Assert.Equal((16.0, 584.0), (left, bottom))
    let left, top, right, _ = at [ Toasts.horizontalAlignment HorizontalAlignment.Center; Toasts.verticalAlignment VerticalAlignment.Top ]
    Assert.Equal(16.0, top)
    Assert.InRange(left + right, 899.0, 901.0)
    let left, top, right, _ = at [ Toasts.horizontalAlignment HorizontalAlignment.Stretch; Toasts.verticalAlignment VerticalAlignment.Top ]
    Assert.Equal((16.0, 16.0, 884.0), (left, top, right))
    // Vertical Center isn't a place for toasts: the bottom.
    let _, _, _, bottom = at [ Toasts.verticalAlignment VerticalAlignment.Center ]
    Assert.Equal(584.0, bottom)

[<Fact>]
let ``at the top it all mirrors: the newest toast is topmost, under the waiting line, and they come down from above`` () =
    let w, host = wide [ App.toasts [ Toasts.verticalAlignment VerticalAlignment.Top ] ]
    for i in 1..5 do
        host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text ("Error " + string i) ]
    pump w host
    let byY = boxes host |> List.sortBy (fun e -> e.PositionInTree.Y) |> List.map textOf
    Assert.Equal<string list>([ "+2 more Close all"; "Error 3"; "Error 2"; "Error 1" ], byY)
    Assert.Equal(16.0, (more host).Value.PositionInTree.Y)
    // Going: on downwards, away from the edge it came from.
    let oldest = toasts host |> List.find (fun e -> textOf e = "Error 1")
    let close = buttons oldest |> List.last
    let at: PixelPoint = { X = close.PositionInTree.X + 5.0; Y = close.PositionInTree.Y + 5.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    Assert.True(oldest.Get ToastProps.offset > 0.0)

[<Fact>]
let ``the placement is an app value: setting it while toasts show moves them`` () =
    let w, host = wide []
    host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text "Here" ]
    pump w host
    let toast = (toasts host).Head
    Assert.Equal(584.0, toast.PositionInTree.Y + toast.Bounds.Height)
    host.AppValues.Set(Toasts.placement, { Horizontal = HorizontalAlignment.Left; Vertical = VerticalAlignment.Top })
    pump w host
    // The same toast, moved.
    Assert.True(obj.ReferenceEquals(toast, (toasts host).Head))
    Assert.Equal((16.0, 16.0), (toast.PositionInTree.X, toast.PositionInTree.Y))
    // A component sets it the same way.
    Assert.Equal(VerticalAlignment.Top, (host.AppValues.Get Toasts.placement).Vertical)

[<Fact>]
let ``a narrow window shows toasts across its width whatever the horizontal alignment, at the edge the vertical one says`` () =
    let w, _, a = start [ App.view app; App.toasts [ Toasts.horizontalAlignment HorizontalAlignment.Left; Toasts.verticalAlignment VerticalAlignment.Top ] ]
    let host = a :?> ElementHost
    pump w host
    host.Toaster.Show [ Toast.severity Severity.Critical; Toast.text "Here" ]
    pump w host
    // 400 DIPs wide.
    let x, y, width, _ = rect (toasts host).Head
    Assert.Equal((16.0, 16.0, 368.0), (x, y, width))
