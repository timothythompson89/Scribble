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

let private show (toaster: Toaster) (extra: AppSetting<unit> list) =
    let w, _, a = start ([ App.view app; App.toaster toaster ] @ extra)
    let host = a :?> ElementHost
    pump w host
    w, host

let private layer (host: ElementHost) = host.Layers |> Seq.find (fun l -> l.Name = "Toasts")
let private toasts (host: ElementHost) =
    match (layer host).Tree.Root with
    | ValueSome root -> all root |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, ToastPresenter.Type)) |> List.ofSeq
    | ValueNone -> []
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
    let toaster = Toaster()
    let w, host = show toaster []
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
    let toaster = Toaster()
    let w, host = show toaster []
    toaster.Show "Saved"
    pump w host
    w.Time.AdvanceMs 2000.0
    host.Dispatcher.Value.RunPending()
    Assert.False w.FrameRequested

[<Fact>]
let ``toasts asked for before the app starts show once it has`` () =
    let toaster = Toaster()
    toaster.Show "Early"
    let _, host = show toaster []
    Assert.Equal<string list>([ "Early" ], texts host)

[<Fact>]
let ``Show from another thread reaches the UI thread`` () =
    let toaster = Toaster()
    let w, host = show toaster []
    Task.Run(fun () -> toaster.Show "From a task").Wait()
    pump w host
    Assert.Equal<string list>([ "From a task" ], texts host)

[<Fact>]
let ``an Error stays until its close button is clicked`` () =
    let toaster = Toaster()
    let w, host = show toaster []
    toaster.Show [ Toast.kind ToastKind.Error; Toast.text "Couldn't save" ]
    pump w host
    advance w host 60_000.0
    Assert.Equal<string list>([ "Couldn't save" ], texts host)
    Assert.Contains("error", (toasts host).Head.Get LayoutProps.classes)
    clickOn w host (buttons (toasts host).Head |> List.last)
    advance w host 200.0
    Assert.Empty(toasts host)

[<Fact>]
let ``a duration of its own`` () =
    let toaster = Toaster()
    let w, host = show toaster []
    toaster.Show [ Toast.text "Quick"; Toast.duration (TimeSpan.FromSeconds 1.0) ]
    toaster.Show [ Toast.kind ToastKind.Error; Toast.text "Timed error"; Toast.duration (TimeSpan.FromSeconds 2.0) ]
    pump w host
    advance w host 1200.0
    Assert.Equal<string list>([ "Timed error" ], texts host)
    advance w host 1200.0
    Assert.Empty(toasts host)

[<Fact>]
let ``at most three show, newest lowest; the rest wait their turn, and their time starts when they show`` () =
    let toaster = Toaster()
    let w, host = show toaster []
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
    let toaster = Toaster()
    let w, host = show toaster []
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
    let toaster = Toaster()
    let log = List<string>()
    let w, host = show toaster []
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
    let toaster = Toaster()
    let w, host = show toaster []
    let one = all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = "one")
    one.Focus()
    toaster.Show [ Toast.kind ToastKind.Error; Toast.text "Still here"; Toast.action ("Retry", ignore) ]
    toaster.Show [ Toast.kind ToastKind.Error; Toast.text "And me" ]
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
    let toaster = Toaster()
    let w, host = show toaster []
    toaster.Show [ Toast.kind ToastKind.Error; Toast.text "A fairly long message that would like more room than a narrow window has to give it" ]
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
    let toaster = Toaster()
    let log = List<string>()
    let view = Button.create [ Button.name "big"; Button.text "Big"; Button.onClick (fun _ -> log.Add "big") ]
    let w, _, a = start [ App.view view; App.toaster toaster ]
    let host = a :?> ElementHost
    toaster.Show [ Toast.kind ToastKind.Error; Toast.text "Here" ]
    pump w host
    w.Send(PointerPressed(Mouse, 0, { X = 50.0; Y = 50.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 50.0; Y = 50.0 }, MouseButton.Left, Modifiers.None))
    pump w host
    Assert.Equal<string list>([ "big" ], List.ofSeq log)

[<Fact>]
let ``toasts are above dialogs and flyouts, and a toast's node is an Alert named by its text`` () =
    let toaster = Toaster()
    let view =
        Dialog.create [ Dialog.isOpen true; Dialog.title "Sure?"; Dialog.content (Button.create [ Button.name "more"; Button.text "More"; Button.flyout (TextBlock.create [ TextBlock.text "Inside" ]) ]) ]
    let w, _, a = start [ App.view view; App.toaster toaster ]
    let host = a :?> ElementHost
    pump w host
    let more = host.Layers |> Seq.collect (fun l -> l.Tree.Root |> ValueOption.toArray |> Seq.collect all) |> Seq.find (fun e -> e.Name = "more")
    clickOn w host more
    toaster.Show [ Toast.kind ToastKind.Error; Toast.text "Offline" ]
    pump w host
    Assert.Equal<string list>([ "App"; "Dialog"; "Flyout of more"; "Toasts" ], host.Layers |> Seq.map _.Name |> List.ofSeq)
    Assert.True(host.SemanticTree().ToString().Contains("Alert \"Offline\""), host.SemanticTree().ToString())
    // The toast can be closed over the dialog.
    clickOn w host (buttons (toasts host).Head |> List.last)
    advance w host 200.0
    Assert.Empty(toasts host)
