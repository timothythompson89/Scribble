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

/// A ListBox of `n` people 300 DIPs high, items 30 DIPs of content high.
let private list (n: int) =
    ListBox.create [
        ListBox.height 300.0
        ListBox.verticalAlignment VerticalAlignment.Top
        ListBox.source (people n)
        ListBox.keySelector (fun (p: Person) -> p.Id)
        ListBox.viewSelector (fun (p: Person) -> Border.create [ Border.height 30.0; Border.content (TextBlock.create [ TextBlock.text p.Name ]) ])
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
    DebugWarnings.Listener <- Delegate.Combine(DebugWarnings.Listener, listener) :?> Action<string>
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
        DebugWarnings.Listener <- Delegate.Remove(DebugWarnings.Listener, listener) :?> Action<string>
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

// --- A2: a TextBox's text (DESIGN.md, "A2: a TextBox's text") -----------------------------------------

/// A TextBox at size 20 (the stand-in shaper makes every UTF-16 unit 10 DIPs wide), its text area 200 DIPs wide
/// from x = 13, above a button.
let private textPage (settings: Setting<TextBox> list) =
    StackPanel.create [
        StackPanel.children [
            TextBox.create ([ TextBox.name "box"; TextBox.fontSize 20.0; TextBox.width 226.0; TextBox.horizontalAlignment HorizontalAlignment.Left ] @ settings)
            Button.create [ Button.name "next"; Button.text "Next"; Button.horizontalAlignment HorizontalAlignment.Left ]
        ]
    ]

/// The page with the box focused by a click at its start, then `text` typed into it (it keeps its own text).
let private typed (settings: Setting<TextBox> list) (text: string) =
    let w, host = showView (textPage settings)
    w.Send(PointerPressed(Mouse, 0, { X = 15.0; Y = 15.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 15.0; Y = 15.0 }, MouseButton.Left, Modifiers.None))
    if text <> "" then w.Send(TextInput text)
    settle w
    w, host

let private key (w: FakeWindow) (mods: Modifiers) (k: Key) =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    settle w

let private boxNode (u: AkUpdate) = u.Nodes |> Array.find (fun n -> n.Selection.IsSome)
let private runsOf (u: AkUpdate) = u.Nodes |> Array.filter (fun n -> n.Role = AkRole.TextRun)
let private editOf (host: ElementHost) = TextBox.editOf (named "box" host)

[<Fact>]
let ``a text box's line is a text run: its text, each character's bytes and place, where words start, and the selection`` () =
    // "é" is two bytes in UTF-8 and one character; the emoji is four bytes, two UTF-16 units and one character.
    let w, host = typed [] "héllo wörld 😀!"
    let u = update host
    let box = boxNode u
    Assert.Equal(AkRole.TextInput, box.Role)
    Assert.Equal(ValueSome "héllo wörld 😀!", box.Value)
    Assert.True box.ClipsChildren
    let run = Assert.Single(runsOf u)
    Assert.Equal<uint64[]>([| run.Id |], box.Children)
    Assert.Equal(ValueSome "héllo wörld 😀!", run.Value)
    Assert.Equal<byte[]>([| 1uy; 2uy; 1uy; 1uy; 1uy; 1uy; 1uy; 2uy; 1uy; 1uy; 1uy; 1uy; 4uy; 1uy |], run.CharacterLengths)
    // Words as the caret's word moves see them: "héllo", "wörld", and the emoji with the "!" (one run of symbols).
    Assert.Equal<byte[]>([| 0uy; 6uy; 12uy |], run.WordStarts)
    // Each character 10 DIPs on from the last, the emoji 20 wide (two units).
    Assert.Equal<float32[]>([| for i in 0 .. 12 -> float32 (10 * i) |], run.CharacterPositions[0..12])
    Assert.Equal(140.0f, run.CharacterPositions[13])
    Assert.Equal(20.0f, run.CharacterWidths[12])
    Assert.Equal(10.0f, run.CharacterWidths[0])
    // The run's box in the window: from the text's left edge, as wide as the text.
    Assert.Equal((13.0, 13.0 + 150.0), (run.X0, run.X1))
    // The caret after the last character: the end of the run.
    Assert.Equal(ValueSome { AnchorRun = run.Id; AnchorCharacter = 14; FocusRun = run.Id; FocusCharacter = 14 }, box.Selection)
    // Shift+Left twice: the "!" and the emoji selected, by characters and not by UTF-16 units.
    key w Modifiers.Shift Key.Left
    key w Modifiers.Shift Key.Left
    Assert.Equal(ValueSome { AnchorRun = run.Id; AnchorCharacter = 14; FocusRun = run.Id; FocusCharacter = 12 }, (boxNode (update host)).Selection)
    // At twice the scale, twice the pixels.
    let big = (runsOf (updateAt 2.0 host))[0]
    Assert.Equal(20.0f, big.CharacterWidths[0])
    Assert.Equal((26.0, 26.0 + 300.0), (big.X0, big.X1))

[<Fact>]
let ``several lines are a run each; a line break is one more character at its line's end, where the caret then stands`` () =
    // 200 DIPs: twenty characters a line. Two paragraphs, the first wrapping once.
    let w, host = typed [ TextBox.multiline true ] "aaaa bbbb cccc dddd eeee\nlast"
    let u = update host
    let box = boxNode u
    Assert.Equal(AkRole.MultilineTextInput, box.Role)
    let runs = runsOf u
    Assert.Equal<string voption list>([ ValueSome "aaaa bbbb cccc dddd "; ValueSome "eeee\n"; ValueSome "last" ], [ for r in runs -> r.Value ])
    // The break counts as a character of one byte, with no width.
    Assert.Equal<byte[]>([| 1uy; 1uy; 1uy; 1uy; 1uy |], runs[1].CharacterLengths)
    Assert.Equal(0.0f, runs[1].CharacterWidths[4])
    // One under the other, a line apart.
    Assert.True(runs[1].Y0 > runs[0].Y0 && runs[2].Y0 > runs[1].Y0)
    Assert.Equal(runs[0].Y1, runs[1].Y0)
    // The caret is at the text's end: the last run's end.
    Assert.Equal((runs[2].Id, 4), (box.Selection.Value.FocusRun, box.Selection.Value.FocusCharacter))
    // Up, End: the end of "eeee", which is on its line break, not after it.
    key w Modifiers.None Key.Up
    key w Modifiers.None Key.End
    let selection = (boxNode (update host)).Selection.Value
    Assert.Equal((runs[1].Id, 4), (selection.FocusRun, selection.FocusCharacter))
    // Up, End: the end of the wrapped first line stays on that line.
    key w Modifiers.None Key.Up
    key w Modifiers.None Key.End
    let selection = (boxNode (update host)).Selection.Value
    Assert.Equal((runs[0].Id, 20), (selection.FocusRun, selection.FocusCharacter))

[<Fact>]
let ``a password is a bullet for each character and never its text`` () =
    let _, host = typed [ TextBox.isPassword true ] "pä😀s"
    let u = update host
    let box = boxNode u
    Assert.Equal(AkRole.PasswordInput, box.Role)
    Assert.Equal(ValueNone, box.Value)
    let run = Assert.Single(runsOf u)
    Assert.Equal(ValueSome "••••", run.Value)
    Assert.Equal(4, run.CharacterLengths.Length)
    Assert.Equal<byte[]>([| 0uy |], run.WordStarts)
    Assert.Equal(4, box.Selection.Value.FocusCharacter)
    for n in u.Nodes do
        Assert.DoesNotContain("pä", defaultValueArg n.Value "")

[<Fact>]
let ``a line of more than 255 characters is several runs on one line, and places map back`` () =
    let text = String.replicate 60 "word "
    let _, host = typed [] text
    let u = update host
    let runs = runsOf u
    Assert.Equal(2, runs.Length)
    Assert.Equal(255, runs[0].CharacterLengths.Length)
    Assert.Equal(45, runs[1].CharacterLengths.Length)
    Assert.Equal((0UL, runs[1].Id), (runs[0].PreviousOnLine, runs[0].NextOnLine))
    Assert.Equal((runs[0].Id, 0UL), (runs[1].PreviousOnLine, runs[1].NextOnLine))
    // The second starts where the first ends, with its own word starts counted from its own start.
    Assert.Equal(runs[0].X1, runs[1].X0)
    Assert.Equal(0.0f, runs[1].CharacterPositions[0])
    Assert.Equal<byte[]>([| for i in 0 .. 8 -> byte (5 * i) |], runs[1].WordStarts)
    // The caret at the end is in the second run; a place in it maps back to the line's 300th character.
    let selection = (boxNode u).Selection.Value
    Assert.Equal((runs[1].Id, 45), (selection.FocusRun, selection.FocusCharacter))
    Assert.Equal({ Line = 0; Character = 300 }, Translation.placeOf selection.FocusRun selection.FocusCharacter)
    Assert.Equal({ Line = 0; Character = 7 }, Translation.placeOf runs[0].Id 7)

[<Fact>]
let ``a very long text gives the thousand lines around the caret`` () =
    let text = String.Join("\n", Seq.init 3000 (fun i -> "line " + string i))
    let w, host = typed [ TextBox.multiline true; TextBox.maxLines 5 ] ""
    w.Send(TextInput text)
    settle w
    // The caret is at the end: the last thousand lines.
    let runs = runsOf (update host)
    Assert.Equal(TextBox.semanticLineCap, runs.Length)
    Assert.Equal(ValueSome "line 2999", runs[runs.Length - 1].Value)
    Assert.Equal(ValueSome "line 2000\n", runs[0].Value)
    // At the start: the first thousand, and the caret in the first.
    key w Modifiers.Control Key.Home
    let u = update host
    let runs = runsOf u
    Assert.Equal(ValueSome "line 0\n", runs[0].Value)
    Assert.Equal(TextBox.semanticLineCap, runs.Length)
    Assert.Equal((runs[0].Id, 0), ((boxNode u).Selection.Value.FocusRun, (boxNode u).Selection.Value.FocusCharacter))

[<Fact>]
let ``what an input method composes is in the lines, as it is drawn`` () =
    let w, host = typed [] "ab"
    w.Send(TextComposition("xyz", 3))
    settle w
    let u = update host
    let composed = runsOf u
    Assert.Equal(ValueSome "abxyz", composed[0].Value)
    Assert.Equal(5, (boxNode u).Selection.Value.FocusCharacter)
    // Not typed yet.
    Assert.Equal("ab", (editOf host).Text)

[<Fact>]
let ``a screen reader's text actions select, replace and set the text by the paths input takes`` () =
    let heard = ResizeArray<string>()
    let w, host = typed [ TextBox.maxLength 20; TextBox.onTextChanged heard.Add ] "hello brave world"
    heard.Clear()
    let box () = (host.SemanticTree().Find "TextBox").Value
    let run = (runsOf (update host))[0]
    // What AccessKit is told the box can do.
    let actions = (boxNode (update host)).Actions
    for a in [ AkAction.SetTextSelection; AkAction.ReplaceSelectedText; AkAction.SetValue ] do
        Assert.Contains(a, actions)
    // Select "brave", as AccessKit names places: a run and a character in it.
    let selection = SemanticArgument.TextSelection(Translation.placeOf run.Id 6, Translation.placeOf run.Id 11)
    Assert.True((box ()).Perform(SemanticAction.SetTextSelection, selection))
    settle w
    Assert.Equal("brave", (editOf host).SelectedText)
    Assert.Empty heard
    // Replace it, as pasted text: the app hears, and one undo takes it back.
    Assert.True((box ()).Perform(SemanticAction.ReplaceSelectedText, SemanticArgument.Text "calm"))
    settle w
    Assert.Equal("hello calm world", (editOf host).Text)
    Assert.Equal<string list>([ "hello calm world" ], List.ofSeq heard)
    key w Modifiers.Control (Key.Char 'z')
    Assert.Equal("hello brave world", (editOf host).Text)
    // Set the whole value: no more than maxLength gets in.
    Assert.True((box ()).Perform(SemanticAction.SetValue, SemanticArgument.Text "a text that is longer than twenty characters"))
    settle w
    Assert.Equal("a text that is longe", (editOf host).Text)
    // Without what it needs, an action does nothing.
    Assert.False((box ()).Perform SemanticAction.SetValue)
    Assert.False((box ()).Perform(SemanticAction.SetTextSelection, SemanticArgument.Text "no"))

[<Fact>]
let ``a read-only box can be selected in but not changed, and AccessKit is told so`` () =
    let w, host = showView (textPage [ TextBox.text "hello world"; TextBox.isReadOnly true ])
    let box () = (host.SemanticTree().Find "TextBox").Value
    let u = update host
    let actions = (boxNode u).Actions
    Assert.Contains(AkAction.SetTextSelection, actions)
    Assert.DoesNotContain(AkAction.ReplaceSelectedText, actions)
    Assert.DoesNotContain(AkAction.SetValue, actions)
    Assert.Equal(ValueNone, Translation.semanticAction (box ()) AkAction.SetValue)
    let run = (runsOf u)[0]
    Assert.True((box ()).Perform(SemanticAction.SetTextSelection, SemanticArgument.TextSelection(Translation.placeOf run.Id 0, Translation.placeOf run.Id 5)))
    settle w
    Assert.Equal("hello", (editOf host).SelectedText)
    Assert.False((box ()).Perform(SemanticAction.ReplaceSelectedText, SemanticArgument.Text "x"))
    Assert.Equal("hello world", (editOf host).Text)

[<Fact>]
let ``a blinking caret changes nothing AccessKit is given; a moved caret does`` () =
    let w, host = typed [] "hello"
    let first = update host
    // Half a second on, the caret has blinked off (a frame was drawn for it).
    w.Time.AdvanceMs 600.0
    settle w
    Assert.True((first = update host))
    key w Modifiers.None Key.Left
    Assert.False((first = update host))

[<Timing.TimingFact; Trait("Category", "Timing")>]
let ``a thousand lines of text for AccessKit are quick`` () =
    let text = String.Join("\n", Seq.init 3000 (fun i -> "line number " + string i + " of the text"))
    let w, host = typed [ TextBox.multiline true; TextBox.maxLines 5 ] ""
    w.Send(TextInput text)
    settle w
    for _ in 1 .. 10 do
        update host |> ignore
    let best =
        Seq.init 30 (fun _ ->
            let sw = Stopwatch.StartNew()
            update host |> ignore
            sw.Elapsed.TotalMilliseconds)
        |> Seq.min
    Console.WriteLine($"Semantic tree for AccessKit: a text box of 3,000 lines (1,000 given) {best:F3} ms ({(update host).Nodes.Length} nodes)")
    Assert.True(best < 20.0, $"the text took {best:F2} ms")
