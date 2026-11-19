/// AccessKit step A1 on the fake platform (DESIGN.md, "AccessKit: the screen-reader bridge"): a semantic node's
/// id, the translation of a window's semantic tree into what AccessKit is given (roles, states, bounds, focus,
/// actions), when updates go out, and how an app's windows get their bridge. Nothing here calls AccessKit itself:
/// what Narrator and NVDA say is for the checklist.
module Scribble.Tests.AccessKitTests

open System
open System.Collections.Generic
open System.Diagnostics
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
open Scribble.Accessibility.AccessKit
open Scribble.Tests.AppTests

let private settle (w: FakeWindow) =
    let mutable n = 0
    while n < 100 && w.DeliverFrame() do
        n <- n + 1

let private show (settings: AppSetting<'Msg> list) =
    let w, _, app = start settings
    let host = app :?> ElementHost
    settle w
    w, host

let private showView (view: View) = show [ App.view view ]

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let private named name (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = name)

/// What AccessKit would be given for the window now, at `scale`.
let private updateAt (scale: float) (host: ElementHost) =
    let size = host.Window.ClientSize
    Translation.update host.Window.Title (float size.Width) (float size.Height) scale (host.SemanticTree()) host.Focused

let private update (host: ElementHost) = updateAt 1.0 host

let private labelled (label: string) (u: AkUpdate) = u.Nodes |> Array.find (fun n -> n.Label = label)

type private Person = { Id: int; Name: string }

let private people (n: int) = [ for i in 0 .. n - 1 -> { Id = i; Name = "Person " + string i } ]

/// A ListBox of `n` people in a ScrollViewer 300 DIPs high, items 30 DIPs of content high.
let private list (n: int) =
    ScrollViewer.create [
        ScrollViewer.height 300.0
        ScrollViewer.verticalAlignment VerticalAlignment.Top
        ScrollViewer.content (
            ListBox.create [
                ListBox.source (people n)
                ListBox.keySelector (fun (p: Person) -> p.Id)
                ListBox.viewSelector (fun (p: Person) -> Border.create [ Border.height 30.0; Border.content (TextBlock.create [ TextBlock.text p.Name ]) ])
            ])
    ]

let private controls =
    StackPanel.create [
        StackPanel.horizontalAlignment HorizontalAlignment.Left
        StackPanel.children [
            Button.create [ Button.name "save"; Button.text "Save"; Button.width 100.0; Button.height 30.0; Button.onClick ignore ]
            ToggleButton.create [ ToggleButton.text "Bold"; ToggleButton.isChecked true ]
            CheckBox.create [ CheckBox.text "Some"; CheckBox.isIndeterminate true ]
            Switch.create [ Switch.text "Dark" ]
            Slider.create [ Slider.value 30.0; Slider.maximum 50.0; Slider.step 5.0; Semantics.label "Volume" ]
            TextBox.create [ TextBox.text "Ada"; TextBox.placeholder "Name" ]
            TextBox.create [ TextBox.text "one\ntwo"; TextBox.multiline true; TextBox.placeholder "Notes" ]
            TextBox.create [ TextBox.text "secret"; TextBox.isPassword true; TextBox.placeholder "Password" ]
            TextBox.create [ TextBox.placeholder "Empty"; TextBox.isReadOnly true ]
            Button.create [ Button.text "Off"; Button.isEnabled false ]
            TextBlock.create [ TextBlock.text "A caption" ]
            Separator.create []
        ]
    ]

[<Fact>]
let ``the window is the root, named by its title, holding the layers' nodes; each node keeps its place and size in pixels`` () =
    let w, host = show [ App.title "My app"; App.view controls ]
    let u = updateAt 2.0 host
    Assert.Equal(Translation.rootId, u.Root)
    let root = u.Nodes[0]
    Assert.Equal(Translation.rootId, root.Id)
    Assert.Equal(AkRole.Window, root.Role)
    Assert.Equal("My app", root.Label)
    // The window's size as given (pixels already); a node's from DIPs, by the scale.
    Assert.Equal((0.0, 0.0, float w.Size.Width, float w.Size.Height), (root.X0, root.Y0, root.X1, root.Y1))
    let save = labelled "Save" u
    let e = named "save" host
    let at = e.PositionInTree
    Assert.Equal((at.X * 2.0, at.Y * 2.0, (at.X + 100.0) * 2.0, (at.Y + 30.0) * 2.0), (save.X0, save.Y0, save.X1, save.Y1))
    // Every node is some node's child, and the root's children are the top nodes in order.
    let tree = host.SemanticTree()
    Assert.Equal<uint64 list>([ for n in tree.Children -> n.Id ], List.ofArray root.Children)
    let children = HashSet<uint64>(u.Nodes |> Seq.collect (fun n -> n.Children))
    for n in u.Nodes do
        if n.Id <> Translation.rootId then Assert.Contains(n.Id, children)
    // Ids are one each.
    Assert.Equal(u.Nodes.Length, (u.Nodes |> Array.distinctBy (fun n -> n.Id)).Length)

[<Fact>]
let ``roles and states become AccessKit's`` () =
    let _, host = showView controls
    let u = update host
    let save = labelled "Save" u
    Assert.Equal(AkRole.Button, save.Role)
    Assert.Contains(AkAction.Click, save.Actions)
    Assert.Contains(AkAction.Focus, save.Actions)
    Assert.Equal(ValueNone, save.Toggled)
    // A toggle button is a button that is on or off; a check box can be mixed.
    Assert.Equal((AkRole.Button, ValueSome 1), (let n = labelled "Bold" u in n.Role, n.Toggled))
    Assert.Equal((AkRole.CheckBox, ValueSome 2), (let n = labelled "Some" u in n.Role, n.Toggled))
    Assert.Equal((AkRole.Switch, ValueSome 0), (let n = labelled "Dark" u in n.Role, n.Toggled))
    let volume = labelled "Volume" u
    Assert.Equal(AkRole.Slider, volume.Role)
    Assert.Equal(ValueSome { Value = 30.0; Minimum = 0.0; Maximum = 50.0; Step = 5.0 }, volume.Range)
    Assert.Contains(AkAction.Increment, volume.Actions)
    Assert.Contains(AkAction.Decrement, volume.Actions)
    // A text box is one of three by what it holds; a password's text is never told.
    Assert.Equal((AkRole.TextInput, ValueSome "Ada"), (let n = labelled "Name" u in n.Role, n.Value))
    Assert.Equal((AkRole.MultilineTextInput, ValueSome "one\ntwo"), (let n = labelled "Notes" u in n.Role, n.Value))
    Assert.Equal((AkRole.PasswordInput, ValueNone), (let n = labelled "Password" u in n.Role, n.Value))
    let empty = labelled "Empty" u
    Assert.Equal(ValueSome "", empty.Value)
    Assert.True empty.ReadOnly
    Assert.True((labelled "Off" u).Disabled)
    // Text is a label whose text is its value (where AccessKit reads it from).
    let caption = u.Nodes |> Array.find (fun n -> n.Value = ValueSome "A caption")
    Assert.Equal((AkRole.Label, ""), (caption.Role, caption.Label))
    Assert.Contains(u.Nodes, fun n -> n.Role = AkRole.Splitter)

[<Fact>]
let ``a list whose items can be selected is a list box of options, each with its place among all the items`` () =
    let _, host = showView (list 10_000)
    let u = update host
    let box = u.Nodes |> Array.find (fun n -> n.Role = AkRole.ListBox)
    let first = labelled "Person 0" u
    Assert.Equal(AkRole.ListBoxOption, first.Role)
    Assert.Equal(ValueSome false, first.Selected)
    Assert.Equal(ValueSome { Index = 1; Count = 10_000 }, first.Position)
    Assert.Contains(first.Id, box.Children)
    // The scroll area around it says how far it scrolls, and clips what is inside.
    let area = u.Nodes |> Array.find (fun n -> n.Role = AkRole.ScrollView)
    Assert.True area.ClipsChildren
    Assert.True(area.Scroll.Value.ExtentHeight > 100_000.0)
    Assert.Contains(AkAction.ScrollDown, area.Actions)
    Assert.Contains(AkAction.ScrollUp, area.Actions)

[<Fact>]
let ``only realized items are in the tree: the same number of nodes for 100 items and 10,000`` () =
    let _, small = showView (list 100)
    let _, large = showView (list 10_000)
    Assert.Equal((update small).Nodes.Length, (update large).Nodes.Length)
    Assert.InRange((update large).Nodes.Length, 5, 40)

[<Fact>]
let ``a node keeps its id from one tree to the next, and a recycled list item gets another`` () =
    let w, host = showView (list 10_000)
    let idOf (name: string) = (labelled name (update host)).Id
    let first = idOf "Person 0"
    let third = idOf "Person 2"
    settle w
    Assert.Equal(first, idOf "Person 0")
    // Scrolled far away: the elements that showed the first items show others now.
    let recycledBefore = HashSet<uint64>((update host).Nodes |> Seq.map (fun n -> n.Id))
    w.Send(PointerWheel({ X = 100.0; Y = 100.0 }, 0.0, -200.0, Modifiers.None))
    settle w
    let after = update host
    Assert.DoesNotContain(after.Nodes, fun n -> n.Label = "Person 0")
    let items = after.Nodes |> Array.filter (fun n -> n.Role = AkRole.ListBoxOption)
    Assert.NotEmpty items
    for item in items do
        Assert.DoesNotContain(item.Id, recycledBefore)
    // Back at the top: the first items are there again, as new things to a screen reader.
    w.Send(PointerWheel({ X = 100.0; Y = 100.0 }, 0.0, 100_000.0, Modifiers.None))
    settle w
    Assert.NotEqual(first, idOf "Person 0")
    Assert.NotEqual(third, idOf "Person 2")

[<Fact>]
let ``AccessKit's focus is the focused element's node, the nearest around it that has one, or the window`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                Button.create [ Button.name "save"; Button.text "Save" ]
                // A group with a focusable element of no role inside it.
                Border.create [
                    Semantics.role Role.Group
                    Semantics.label "Shipping"
                    Border.content (Border.create [ Border.name "bare"; Border.focusable true; Border.height 20.0 ])
                ]
                Border.create [ Border.name "alone"; Border.focusable true; Border.height 20.0 ]
            ]
        ]
    let w, host = showView view
    // Nothing focused: the window.
    Assert.Equal(Translation.rootId, (update host).Focus)
    (named "save" host).Focus()
    settle w
    Assert.Equal((labelled "Save" (update host)).Id, (update host).Focus)
    (named "bare" host).Focus()
    settle w
    Assert.Equal((labelled "Shipping" (update host)).Id, (update host).Focus)
    (named "alone" host).Focus()
    settle w
    Assert.Equal(Translation.rootId, (update host).Focus)

[<Fact>]
let ``an action AccessKit asks for is one of ours, among those the node supports`` () =
    let clicks = ResizeArray<string>()
    let view =
        StackPanel.create [
            StackPanel.children [
                Button.create [ Button.text "Save"; Button.onClick (fun _ -> clicks.Add "save") ]
                TextBlock.create [ TextBlock.text "A caption" ]
            ]
        ]
    let w, host = showView view
    let tree = host.SemanticTree()
    let save = (tree.Find "Button \"Save\"").Value
    Assert.Equal(ValueSome SemanticAction.Click, Translation.semanticAction save AkAction.Click)
    Assert.Equal(ValueSome SemanticAction.Focus, Translation.semanticAction save AkAction.Focus)
    // Not one a button has; and one that is none of ours at all (21: show the context menu).
    Assert.Equal(ValueNone, Translation.semanticAction save AkAction.Increment)
    Assert.Equal(ValueNone, Translation.semanticAction save 21)
    let caption = (tree.Find "Text \"A caption\"").Value
    Assert.Equal(ValueNone, Translation.semanticAction caption AkAction.Click)
    // Performed as the bridge does it.
    Assert.True(save.Perform (Translation.semanticAction save AkAction.Click).Value)
    settle w
    Assert.Equal<string list>([ "save" ], List.ofSeq clicks)

[<Fact>]
let ``updates are paced: the first at once, then at most one in 100 ms, a last one when changes stop, focus at once`` () =
    let ms (v: float) = TimeSpan.FromMilliseconds v
    let pacer = UpdatePacer(ms 100.0)
    // The first change after a quiet spell goes at once.
    Assert.Equal(PacerStep.Send, pacer.Changed(ms 1000.0, false))
    // The next one, 16 ms on, waits for the rest of the 100; those after it wait with it.
    Assert.Equal(PacerStep.WaitFor(ms 84.0), pacer.Changed(ms 1016.0, false))
    Assert.Equal(PacerStep.Waiting, pacer.Changed(ms 1032.0, false))
    Assert.Equal(PacerStep.Waiting, pacer.Changed(ms 1090.0, false))
    // The wait over: one update for all of them.
    Assert.True(pacer.Due(ms 1100.0))
    Assert.False(pacer.Due(ms 1100.0))
    // Still changing: again a wait, counted from the update just sent.
    Assert.Equal(PacerStep.WaitFor(ms 90.0), pacer.Changed(ms 1110.0, false))
    // A change of focus goes at once, and what was waiting goes with it.
    Assert.Equal(PacerStep.Send, pacer.Changed(ms 1120.0, true))
    Assert.False(pacer.Due(ms 1200.0))
    // After a quiet spell, at once again.
    Assert.Equal(PacerStep.Send, pacer.Changed(ms 5000.0, false))
    // An animation at 60 frames a second for a second: at most ten updates and ten timers.
    let animated = UpdatePacer(ms 100.0)
    let mutable sent = 0
    let mutable timers = List<float>()
    for frame in 0 .. 59 do
        let now = 16.0 * float frame
        for due in List.ofSeq timers do
            if due <= now then
                timers.Remove due |> ignore
                if animated.Due(ms due) then sent <- sent + 1
        match animated.Changed(ms now, false) with
        | PacerStep.Send -> sent <- sent + 1
        | PacerStep.WaitFor wait -> timers.Add(now + wait.TotalMilliseconds)
        | PacerStep.Waiting -> ()
    Assert.InRange(sent, 9, 10)
    Assert.InRange(timers.Count, 0, 1)

/// A bridge that only counts: what `ScreenReader.attach` is given in place of AccessKit's.
type private FakeBridges() =
    member val Made = List<ElementHost>()
    member val Disposed = 0 with get, set
    member this.Make: ElementHost -> IDisposable =
        fun host ->
            this.Made.Add host
            { new IDisposable with
                member _.Dispose() = this.Disposed <- this.Disposed + 1 }

[<Fact>]
let ``an app's window gets the bridge its values name, before it is shown; App.screenReader false leaves it out`` () =
    let bridges = FakeBridges()
    let shownAtAttach = ResizeArray<bool>()
    let desktop (settings: AppSetting<unit> list) =
        // What App.run on the desktop puts before the app's own settings.
        [ App.value ScreenReader.bridge (ValueSome bridges.Make)
          App.onStart (fun app ->
              shownAtAttach.Add (app.Host.Window :?> FakeWindow).Shown
              ScreenReader.attach app.Host) ]
        @ settings
    let _, host = show (desktop [ App.view (Button.create [ Button.text "Hi" ]) ])
    Assert.Equal(1, bridges.Made.Count)
    Assert.True(obj.ReferenceEquals(host, bridges.Made[0]))
    Assert.Equal<bool list>([ false ], List.ofSeq shownAtAttach)
    // It goes with its window.
    Assert.Equal(0, bridges.Disposed)
    host.Close()
    Assert.Equal(1, bridges.Disposed)
    // Asked not to: none.
    let _, _ = show (desktop [ App.screenReader false; App.view (Button.create [ Button.text "Hi" ]) ])
    Assert.Equal(1, bridges.Made.Count)
    // No bridge named (a platform without one): nothing happens.
    let _, plain = showView (Button.create [ Button.text "Hi" ])
    ScreenReader.attach plain
    Assert.Equal(1, bridges.Made.Count)
    // A tool's window beside the app's takes the app's.
    let _, app = show (desktop [ App.view (Button.create [ Button.text "Hi" ]) ])
    ScreenReader.attach (app, plain)
    Assert.Equal(3, bridges.Made.Count)
    Assert.True(obj.ReferenceEquals(plain, bridges.Made[2]))

[<Fact>]
let ``a bridge that can't start leaves the app running without one, with a debug warning`` () =
    let warnings = List<string>()
    let listener = Action<string>(fun m -> lock warnings (fun () -> warnings.Add m))
    DebugWarnings.Listener <- listener
    // (The listener is one for the whole process: another test running beside this one may take it.)
    let mutable heard = true
    try
        let failing: ElementHost -> IDisposable = fun _ -> raise (DllNotFoundException "accesskit")
        let w, host =
            show [ App.value ScreenReader.bridge (ValueSome failing)
                   App.onStart (fun app -> ScreenReader.attach app.Host)
                   App.view (Button.create [ Button.text "Hi" ]) ]
        settle w
        Assert.Contains("Button \"Hi\"", host.SemanticTree().ToString())
        heard <- obj.ReferenceEquals(DebugWarnings.Listener, listener)
    finally
        DebugWarnings.Listener <- null
#if DEBUG
    if heard then Assert.Contains(List.ofSeq warnings, fun m -> m.Contains "screen-reader bridge could not start")
#endif

[<Timing.TimingFact; Trait("Category", "Timing")>]
let ``building and translating the tree is quick, for the Gallery's page and for 10,000 items`` () =
    let measure (host: ElementHost) =
        for _ in 1 .. 20 do
            update host |> ignore
        Seq.init 50 (fun _ ->
            let sw = Stopwatch.StartNew()
            update host |> ignore
            sw.Elapsed.TotalMilliseconds)
        |> Seq.min
    let _, gallery = show [ App.size (960.0, 700.0); App.view (Scribble.Samples.Gallery.ControlsPage.view ()) ]
    let _, large = showView (list 10_000)
    let g, l = measure gallery, measure large
    Console.WriteLine($"Semantic tree for AccessKit: the Gallery's Controls page {g:F3} ms ({(update gallery).Nodes.Length} nodes); a 10,000-item list {l:F3} ms ({(update large).Nodes.Length} nodes)")
    // Generous: measured well under a millisecond in Release.
    Assert.True(g < 10.0, $"the Gallery's page took {g:F2} ms")
    Assert.True(l < 5.0, $"the list took {l:F2} ms")
