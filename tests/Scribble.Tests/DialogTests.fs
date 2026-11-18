/// M8 step 4d: Dialog on the fake platform (DESIGN.md, "4d: Dialog and Toast"): its own modal layer over a scrim,
/// the view deciding whether it's open, Escape and the scrim asking, focus in and back, size, stacking, semantics.
module Scribble.Tests.DialogTests

open System.Collections.Generic
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

let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 30 do
        n <- n + 1

let private show (view: View) =
    let w, _, app = start [ App.view view ]
    let host = app :?> ElementHost
    settle w
    w, host

let private dialogLayer (host: ElementHost) = host.Layers |> Seq.tryFind (fun l -> l.Name = "Dialog")
let private isOpen (host: ElementHost) = (dialogLayer host).IsSome
let private named (host: ElementHost) (name: string) =
    host.Layers |> Seq.collect (fun l -> l.Tree.Root |> ValueOption.toArray |> Seq.collect all) |> Seq.find (fun e -> e.Name = name)
let private boxOf (host: ElementHost) = all (dialogLayer host).Value.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Dialog.Type))

let private clickAt (w: FakeWindow) (x: float) (y: float) =
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    settle w

let private clickOn (w: FakeWindow) (e: Element) =
    let p = e.PositionInTree
    clickAt w (p.X + e.Bounds.Width / 2.0) (p.Y + e.Bounds.Height / 2.0)

let private key (w: FakeWindow) k =
    w.Send(KeyDown(k, Modifiers.None, false))
    w.Send(KeyUp(k, Modifiers.None))
    settle w

/// What the app heard, in order.
type private Log() =
    member val Items = List<string>()
    member l.Add(s: string) = l.Items.Add s
    member l.All = List.ofSeq l.Items

/// A button "open" above a button "below", and a Dialog between them in the view with OK and Cancel.
let private app (log: Log) (isOpen: bool) (extra: Setting<Dialog> list) =
    StackPanel.create [
        // The gap is between the two buttons only: a Dialog standing between them gets none.
        StackPanel.spacing 10.0
        StackPanel.children [
            Button.create [ Button.name "open"; Button.text "Open"; Button.height 30.0; Button.onClick (fun _ -> log.Add "open") ]
            Dialog.create (
                [ Dialog.isOpen isOpen
                  Dialog.onOpenChanged (fun v -> log.Add(if v then "asked to open" else "asked to close"))
                  Dialog.title "Delete the file?"
                  Dialog.content (
                      StackPanel.create [
                          StackPanel.orientation Orientation.Horizontal
                          StackPanel.children [
                              Button.create [ Button.name "ok"; Button.text "OK"; Button.onClick (fun _ -> log.Add "ok") ]
                              Button.create [ Button.name "cancel"; Button.text "Cancel"; Button.onClick (fun _ -> log.Add "cancel") ]
                          ]
                      ]) ]
                @ extra)
            Button.create [ Button.name "below"; Button.text "Below"; Button.height 30.0; Button.onClick (fun _ -> log.Add "below") ]
        ]
    ]

[<Fact>]
let ``closed, it shows nothing and takes no space; open, its box is in the middle of a scrim in its own modal layer`` () =
    let log = Log()
    let w, host = show (app log false [])
    Assert.False(isOpen host)
    Assert.Equal(40.0, (named host "below").PositionInTree.Y)
    host.Update(app log true [])
    settle w
    let layer = (dialogLayer host).Value
    Assert.True layer.IsModal
    let scrim = layer.Tree.Root.Value
    Assert.True(obj.ReferenceEquals(scrim.Type, Scrim.Type))
    Assert.Equal((400.0, 300.0), (scrim.Bounds.Width, scrim.Bounds.Height))
    Assert.Equal(1.0, scrim.Get LayoutProps.opacity)
    let box = boxOf host
    let p = box.PositionInTree
    Assert.InRange(p.X * 2.0 + box.Bounds.Width, 399.0, 401.0)
    Assert.InRange(p.Y * 2.0 + box.Bounds.Height, 299.0, 301.0)
    // The app's own layout didn't move.
    Assert.Equal(40.0, (named host "below").PositionInTree.Y)
    host.Update(app log false [])
    settle w
    Assert.False(isOpen host)
    Assert.Empty log.All

[<Fact>]
let ``the box is never closer than 16 DIPs to the window's edges`` () =
    let wide =
        Dialog.create [ Dialog.isOpen true; Dialog.content (Border.create [ Border.width 2000.0; Border.height 2000.0 ]) ]
    let _, host = show wide
    let box = boxOf host
    let p = box.PositionInTree
    Assert.Equal((16.0, 16.0), (p.X, p.Y))
    Assert.Equal((368.0, 268.0), (box.Bounds.Width, box.Bounds.Height))

[<Fact>]
let ``while it's open the app below gets no clicks, and clicks inside reach the dialog's buttons`` () =
    let log = Log()
    let w, host = show (app log true [])
    clickOn w (named host "below")
    clickOn w (named host "open")
    Assert.Empty log.All
    clickOn w (named host "ok")
    Assert.Equal<string list>([ "ok" ], log.All)

[<Fact>]
let ``opening moves focus to the first Tab stop inside, Tab stays inside, and closing gives focus back`` () =
    let log = Log()
    let w, host = show (app log false [])
    let opener = named host "open"
    opener.Focus()
    host.Update(app log true [])
    settle w
    Assert.Equal("ok", host.Focused.Value.Name)
    key w Key.Tab
    Assert.Equal("cancel", host.Focused.Value.Name)
    key w Key.Tab
    Assert.Equal("ok", host.Focused.Value.Name)
    host.Update(app log false [])
    settle w
    Assert.True opener.IsFocused
    Assert.Equal("open", host.Focused.Value.Name)

[<Fact>]
let ``Escape asks to close and the view decides; closeOnEscape false doesn't ask`` () =
    let log = Log()
    let w, host = show (app log true [])
    key w Key.Escape
    Assert.Equal<string list>([ "asked to close" ], log.All)
    // Controlled: still open until the view says otherwise.
    Assert.True(isOpen host)
    log.Items.Clear()
    let w, host = show (app log true [ Dialog.closeOnEscape false ])
    key w Key.Escape
    Assert.Empty log.All
    Assert.True(isOpen host)

[<Fact>]
let ``a press on the scrim asks to close only with lightDismiss; a press on the box never does`` () =
    let log = Log()
    let w, host = show (app log true [])
    clickAt w 5.0 5.0
    Assert.Empty log.All
    let w, host = show (app log true [ Dialog.lightDismiss true ])
    let box = boxOf host
    clickAt w (box.PositionInTree.X + 4.0) (box.PositionInTree.Y + 4.0)
    Assert.Empty log.All
    clickAt w 5.0 5.0
    Assert.Equal<string list>([ "asked to close" ], log.All)
    Assert.True(isOpen host)

[<Fact>]
let ``a flyout opened inside a dialog shows above it, and Escape closes the flyout first`` () =
    let log = Log()
    let view =
        Dialog.create [
            Dialog.isOpen true
            Dialog.onOpenChanged (fun _ -> log.Add "asked to close")
            Dialog.content (Button.create [ Button.name "more"; Button.text "More"; Button.flyout (Button.create [ Button.name "inner"; Button.text "Inner" ]) ])
        ]
    let w, host = show view
    clickOn w (named host "more")
    let names = host.Layers |> Seq.map _.Name |> List.ofSeq
    Assert.Equal<string list>([ "App"; "Dialog"; "Flyout of more" ], names)
    key w Key.Escape
    Assert.Equal<string list>([ "App"; "Dialog" ], host.Layers |> Seq.map _.Name |> List.ofSeq)
    Assert.Empty log.All
    key w Key.Escape
    Assert.Equal<string list>([ "asked to close" ], log.All)

[<Fact>]
let ``its content follows the view while it's open, and it closes when it leaves the view`` () =
    let view (text: string) = Dialog.create [ Dialog.isOpen true; Dialog.content (TextBlock.create [ TextBlock.name "text"; TextBlock.text text ]) ]
    let w, host = show (view "One")
    Assert.Equal("One", (named host "text").Get TextProps.text)
    host.Update(view "Two")
    settle w
    Assert.Equal("Two", (named host "text").Get TextProps.text)
    host.Update(Border.create [])
    settle w
    Assert.False(isOpen host)

[<Fact>]
let ``its node is a Dialog named by its title, and the app below is left out while it shows`` () =
    let lines (s: string) = s.Replace("\r", "").TrimEnd('\n').Split('\n') |> List.ofArray |> List.map (fun l -> l.Trim())
    let log = Log()
    let w, host = show (app log false [])
    Assert.Equal<string list>([ "Button \"Open\""; "Button \"Below\"" ], lines (host.SemanticTree().ToString()))
    host.Update(app log true [])
    settle w
    Assert.Equal<string list>(
        [ "Dialog \"Delete the file?\""; "Text \"Delete the file?\""; "Button \"OK\" focused"; "Button \"Cancel\"" ],
        lines (host.SemanticTree().ToString()))
