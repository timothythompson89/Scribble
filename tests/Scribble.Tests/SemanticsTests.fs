/// The semantics stage: each element type's semantic node, name from content, what's left out,
/// semantic actions, layers, and a check that every sample's controls have a name.
module Scribble.Tests.SemanticsTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
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

let private node (text: string) (host: ElementHost) =
    match host.SemanticTree().Find text with
    | ValueSome n -> n
    | ValueNone -> failwith ("No node \"" + text + "\" in:\n" + host.SemanticTree().ToString())

let private lines (s: string) = s.Replace("\r", "").TrimEnd('\n').Split('\n') |> List.ofArray

// --- Element types --------------------------------------------------------------------------------

[<Fact>]
let ``each built-in element type gives its node`` () =
    let _, host =
        showView (
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.text "Save" ]
                    Button.create [ Semantics.label "Close" ]
                    CheckBox.create [ CheckBox.text "Remember me"; CheckBox.isChecked true ]
                    CheckBox.create [ CheckBox.text "Some"; CheckBox.isIndeterminate true ]
                    Switch.create [ Switch.text "Dark" ]
                    ToggleButton.create [ ToggleButton.text "Bold"; ToggleButton.isChecked true ]
                    Slider.create [
                        Slider.value 30.0
                        Slider.maximum 50.0
                        Slider.step 5.0
                        Slider.orientation Orientation.Vertical
                        Semantics.label "Volume"
                    ]
                    TextBlock.create [ TextBlock.text "Hello" ]
                    TextBlock.create [ TextBlock.text "" ]
                    IconView.create []
                    IconView.create [ Semantics.label "Star" ]
                    Separator.create []
                    Border.create [ Semantics.role Role.Group; Semantics.label "Card"; Border.child (TextBlock.create [ TextBlock.text "Inside" ]) ]
                    Border.create [ Border.child (TextBlock.create [ TextBlock.text "Plain border" ]) ]
                ]
            ]
        )
    Assert.Equal<string list>(
        [ "Button \"Save\""
          "Button \"Close\""
          "CheckBox \"Remember me\" checked"
          "CheckBox \"Some\" mixed"
          "Switch \"Dark\" unchecked"
          "Button \"Bold\" checked"
          "Slider \"Volume\" 30 (0 to 50) vertical"
          "Text \"Hello\""
          "Image \"Star\""
          "Separator"
          "Group \"Card\""
          "  Text \"Inside\""
          "Text \"Plain border\"" ],
        lines (host.SemanticTree().ToString())
    )

[<Fact>]
let ``a control is named by the text inside it, which isn't a node of its own`` () =
    let _, host =
        showView (
            Button.create [
                Button.content (
                    StackPanel.create [
                        StackPanel.orientation Orientation.Horizontal
                        StackPanel.children [
                            IconView.create [ Semantics.label "Plus" ]
                            TextBlock.create [ TextBlock.text "Add" ]
                            TextBlock.create [ TextBlock.text "item" ]
                        ]
                    ]
                )
            ]
        )
    let tree = host.SemanticTree()
    Assert.Equal<string list>([ "Button \"Plus Add item\"" ], lines (tree.ToString()))
    Assert.Equal(NameSource.Content, tree.Children[0].NameSource)

[<Fact>]
let ``a label wins over the content`` () =
    let _, host = showView (Button.create [ Button.text "×"; Semantics.label "Close" ])
    let n = host.SemanticTree().Children[0]
    Assert.Equal("Close", n.Name)
    Assert.Equal(NameSource.Label, n.NameSource)
    Assert.Empty n.Children

[<Fact>]
let ``hidden elements and Semantics hidden leave their whole subtree out`` () =
    let _, host =
        showView (
            StackPanel.create [
                StackPanel.children [
                    Border.create [ Border.isVisible false; Border.child (Button.create [ Button.text "Gone" ]) ]
                    Border.create [ Semantics.hidden true; Border.child (Button.create [ Button.text "Decor" ]) ]
                    Button.create [ Button.text "Kept" ]
                ]
            ]
        )
    Assert.Equal<string list>([ "Button \"Kept\"" ], lines (host.SemanticTree().ToString()))

[<Fact>]
let ``states, focus and actions come from the element`` () =
    let w, host =
        showView (
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.name "on"; Button.text "On"; Button.onClick ignore ]
                    Button.create [ Button.name "off"; Button.text "Off"; Button.isEnabled false; Button.onClick ignore ]
                ]
            ]
        )
    let on = node "\"On\"" host
    Assert.False on.IsDisabled
    Assert.True on.IsFocusable
    Assert.Equal<SemanticAction list>([ SemanticAction.Click; SemanticAction.Focus; SemanticAction.ScrollIntoView ], on.Actions)
    let off = node "\"Off\"" host
    Assert.True off.IsDisabled
    Assert.False off.IsFocusable
    Assert.Equal<SemanticAction list>([ SemanticAction.ScrollIntoView ], off.Actions)
    Assert.False(off.Perform SemanticAction.Click)
    Assert.True(on.Perform SemanticAction.Focus)
    settle w
    Assert.True((node "\"On\"" host).IsFocused)
    Assert.True(host.SemanticTree().ToString().Contains("Button \"On\" focused"))

[<Fact>]
let ``bounds are layout bounds in window coordinates`` () =
    let _, host =
        showView (
            StackPanel.create [
                StackPanel.margin 10.0
                StackPanel.children [ Border.create [ Border.height 30.0 ]; Button.create [ Button.text "B"; Button.height 20.0 ] ]
            ]
        )
    let b = node "\"B\"" host
    Assert.Equal(10.0, b.Bounds.X)
    Assert.Equal(40.0, b.Bounds.Y)
    Assert.Equal(20.0, b.Bounds.Height)

[<Fact>]
let ``computed semantics take the place of the element's own`` () =
    let _, host =
        showView (
            Border.create [
                Semantics.role Role.Group
                Semantics.label "Static"
                Semantics.computed (fun e -> [ Semantics.label ("Computed " + e.Type.Name); Semantics.isExpanded true ])
            ]
        )
    Assert.Equal<string list>([ "Group \"Computed Border\" expanded" ], lines (host.SemanticTree().ToString()))

[<Fact>]
let ``an app's own composite can be a switch`` () =
    let _, host =
        showView (
            Border.create [
                Semantics.role Role.Switch
                Semantics.label "Wi-Fi"
                Semantics.isChecked true
                Border.onClick ignore
                Border.focusable true
            ]
        )
    let n = node "Switch" host
    Assert.Equal("Switch \"Wi-Fi\" checked", n.Describe())
    Assert.Contains(SemanticAction.Click, n.Actions)

// --- Actions -------------------------------------------------------------------------------------

[<Fact>]
let ``click toggles a CheckBox by the click path, as an assistive click`` () =
    let sources = List<ClickSource>()
    let w, host = showView (CheckBox.create [ CheckBox.text "Box"; CheckBox.onClick (fun a -> sources.Add a.Source) ])
    Assert.True((node "CheckBox" host).Perform SemanticAction.Click)
    settle w
    Assert.Equal("CheckBox \"Box\" checked", (node "CheckBox" host).Describe())
    Assert.Equal<ClickSource list>([ ClickSource.Assistive ], List.ofSeq sources)

[<Fact>]
let ``increment and decrement step a Slider as its arrow keys do`` () =
    let reports = List<float>()
    let committed = List<float>()
    let w, host =
        showView (
            Slider.create [
                Slider.step 10.0
                Semantics.label "Level"
                Slider.onValueChanged reports.Add
                Slider.onValueCommitted committed.Add
            ]
        )
    let slider = node "Slider" host
    Assert.Equal<SemanticAction list>(
        [ SemanticAction.Increment; SemanticAction.Decrement; SemanticAction.Focus; SemanticAction.ScrollIntoView ],
        slider.Actions
    )
    Assert.True(slider.Perform SemanticAction.Increment)
    Assert.True(slider.Perform SemanticAction.Increment)
    settle w
    Assert.True((node "Slider" host).Perform SemanticAction.Decrement)
    settle w
    Assert.Equal<float list>([ 10.0; 20.0; 10.0 ], List.ofSeq reports)
    Assert.Equal<float list>([ 10.0; 20.0; 10.0 ], List.ofSeq committed)
    Assert.Equal("Slider \"Level\" 10 (0 to 100)", (node "Slider" host).Describe())

[<Fact>]
let ``a node whose element left its tree does nothing`` () =
    let w, host = showView (Button.create [ Button.text "Old"; Button.onClick ignore ])
    let old = node "Old" host
    host.Update(Border.create [])
    settle w
    Assert.False(old.Perform SemanticAction.Click)

[<Fact>]
let ``an action a node doesn't list is refused`` () =
    let _, host = showView (Button.create [ Button.text "B"; Button.onClick ignore ])
    Assert.False((node "\"B\"" host).Perform SemanticAction.Increment)

// --- Tabs ----------------------------------------------------------------------------------------

[<Fact>]
let ``a TabControl is a tab list of tabs and the page shown`` () =
    let w, host =
        showView (
            TabControl.create [
                TabControl.tabs [
                    for name in [ "One"; "Two"; "Three" ] ->
                        TabItem.create [ TabItem.header name; TabItem.content (TextBlock.create [ TextBlock.text ("Page " + name) ]) ]
                ]
            ]
        )
    Assert.Equal<string list>(
        [ "TabList"
          "  Tab \"One\" selected 1 of 3"
          "  Tab \"Two\" 2 of 3"
          "  Tab \"Three\" 3 of 3"
          "TabPanel \"One\""
          "  Text \"Page One\"" ],
        lines (host.SemanticTree().ToString())
    )
    Assert.True((node "Tab \"Three\"" host).Perform SemanticAction.Click)
    settle w
    // The first page is kept, hidden: it isn't in the tree.
    Assert.Equal<string list>(
        [ "TabList"
          "  Tab \"One\" 1 of 3"
          "  Tab \"Two\" 2 of 3"
          "  Tab \"Three\" selected 3 of 3"
          "TabPanel \"Three\""
          "  Text \"Page Three\"" ],
        lines (host.SemanticTree().ToString())
    )

// --- Lists and scrolling -------------------------------------------------------------------------

let private longList () =
    ScrollViewer.create [
        ScrollViewer.height 100.0
        ScrollViewer.content (
            ItemsView.create [
                ItemsView.source [| 0 .. 9999 |]
                ItemsView.view (fun (i: int) -> TextBlock.create [ TextBlock.text ("Item " + string i); TextBlock.height 20.0 ])
            ]
        )
    ]

[<Fact>]
let ``an ItemsView is a list of its realized items, with their place among all of them`` () =
    let _, host = showView (longList ())
    let tree = host.SemanticTree()
    let area = tree.Children[0]
    Assert.Equal(Role.ScrollArea, area.Role)
    Assert.Equal<SemanticAction list>(
        [ SemanticAction.Focus; SemanticAction.ScrollForward; SemanticAction.ScrollBackward; SemanticAction.ScrollIntoView ],
        area.Actions
    )
    let list = area.Children[0]
    Assert.Equal(Role.List, list.Role)
    let first = list.Children[0]
    Assert.Equal("ListItem \"Item 0\" 1 of 10000", first.Describe())
    Assert.Empty first.Children
    let indexes = [ for c in list.Children -> c.Position.Value.Index ]
    Assert.Equal<int list>(List.sort indexes, indexes)
    Assert.True(list.Children.Count < 30)

[<Fact>]
let ``scroll forward pages on, and the list realizes the items it reaches`` () =
    let w, host = showView (longList ())
    let area () = host.SemanticTree().Children[0]
    Assert.Equal(0.0, (area ()).Scroll.Value.OffsetY)
    for _ in 1 .. 10 do
        Assert.True((area ()).Perform SemanticAction.ScrollForward)
        w.Time.AdvanceMs 300.0
        settle w
    let a = area ()
    Assert.True(a.Scroll.Value.OffsetY > 500.0)
    let firstShown = a.Children[0].Children |> Seq.map (fun c -> c.Position.Value.Index) |> Seq.min
    Assert.True(firstShown > 10, string firstShown)
    Assert.True((area ()).Perform SemanticAction.ScrollBackward)

[<Fact>]
let ``a ScrollViewer whose content fits has no node`` () =
    let _, host =
        showView (ScrollViewer.create [ ScrollViewer.height 100.0; ScrollViewer.content (Button.create [ Button.text "Small"; Button.height 20.0 ]) ])
    Assert.Equal<string list>([ "Button \"Small\"" ], lines (host.SemanticTree().ToString()))

// --- Layers --------------------------------------------------------------------------------------

[<Fact>]
let ``layers are top-level children in order, and a modal one hides those below`` () =
    let w, host = showView (Button.create [ Button.text "App" ])
    host.AddLayer [ Layer.order 100; Layer.view (Button.create [ Button.text "Popup" ]) ] |> ignore
    settle w
    Assert.Equal<string list>([ "Button \"App\""; "Button \"Popup\"" ], lines (host.SemanticTree().ToString()))
    host.AddLayer [ Layer.order 200; Layer.modal true; Layer.view (Button.create [ Button.text "Dialog" ]) ] |> ignore
    settle w
    Assert.Equal<string list>([ "Button \"Dialog\"" ], lines (host.SemanticTree().ToString()))

// --- Explaining ----------------------------------------------------------------------------------

[<Fact>]
let ``an element without a node says why`` () =
    let _, host =
        showView (
            StackPanel.create [
                StackPanel.name "panel"
                StackPanel.children [
                    Button.create [ Button.name "button"; Button.text "B" ]
                    IconView.create [ IconView.name "icon" ]
                    Border.create [ Border.name "hidden"; Semantics.hidden true ]
                    ScrollViewer.create [ ScrollViewer.name "fits"; ScrollViewer.content (Border.create [ Border.height 1.0 ]) ]
                ]
            ]
        )
    let reason name =
        match (named name host).SemanticNode() with
        | Ok n -> "node: " + n.Describe()
        | Error why -> why
    Assert.Equal("node: Button \"B\"", reason "button")
    Assert.Equal("no role (what's inside it takes its place)", reason "panel")
    Assert.Equal("decorative (no name)", reason "icon")
    Assert.Equal("left out (Semantics.hidden)", reason "hidden")
    Assert.Equal("a ScrollViewer whose content fits", reason "fits")
    let text = all (named "button" host) |> Seq.find (fun e -> e.Type.Name = "TextBlock")
    Assert.Equal(Error "part of its control's name", text.SemanticNode())

[<Fact>]
let ``a label without a role gets a debug warning`` () =
    let said = List<string>()
    DebugWarnings.Listener <- System.Action<string>(said.Add)
    try
        let _, host = showView (Border.create [ Semantics.label "Lonely card" ])
        host.SemanticTree() |> ignore
    finally
        DebugWarnings.Listener <- null
#if DEBUG
    Assert.Contains(said, fun m -> m.Contains "Lonely card")
#endif

// --- The samples ---------------------------------------------------------------------------------

/// Nodes a user can focus or click without a name (an icon-only button without a label).
/// Containers (scroll areas, lists, tab lists) may go without one.
let private unnamed (tree: SemanticTree) =
    let container (r: Role) =
        match r with
        | Role.ScrollArea | Role.List | Role.TabList | Role.TabPanel | Role.Group -> true
        | _ -> false
    let nodes = List.ofSeq tree.Nodes
    Assert.True(nodes |> List.exists (fun n -> n.Name <> "" && not n.Actions.IsEmpty), "The tree has no named controls:\n" + tree.ToString())
    nodes
    |> List.filter (fun n ->
        n.Name = "" && not (container n.Role)
        && (List.contains SemanticAction.Click n.Actions || List.contains SemanticAction.Focus n.Actions))
    |> List.map (fun n -> string n.Role + " at " + string n.Bounds.X + "," + string n.Bounds.Y)

let private sized (w: FakeWindow) =
    let size: PixelSize = { Width = 1200; Height = 900 }
    w.Size <- size
    w.Send(Resized size)
    settle w

[<Fact>]
let ``every Counter tab names whatever can be focused or clicked`` () =
    let w, host = show (Scribble.Samples.Counter.app @ [ Scribble.Inspector.Inspector.enable [] ])
    sized w
    let tabs = host.SemanticTree().Nodes |> Seq.filter (fun n -> n.Role = Role.Tab) |> Seq.map _.Name |> List.ofSeq
    Assert.True(tabs.Length >= 6, String.concat ", " tabs)
    let problems = List<string>()
    for tab in tabs do
        Assert.True((node ("Tab \"" + tab + "\"") host).Perform SemanticAction.Click)
        settle w
        let tree = host.SemanticTree()
        Assert.Equal(1, tree.Nodes |> Seq.filter (fun n -> n.Role = Role.TabPanel && n.Name = tab) |> Seq.length)
        for p in unnamed tree do
            problems.Add(tab + ": " + p)
    Assert.Empty problems

[<Fact>]
let ``the inspector is left out of the semantic tree`` () =
    let w, host = show (Scribble.Samples.Counter.app @ [ Scribble.Inspector.Inspector.enable [] ])
    sized w
    let names () = host.SemanticTree().Nodes |> Seq.map (fun n -> string n.Role + " " + n.Name) |> List.ofSeq
    let before = names ()
    w.Send(KeyDown((Key.F 12), Modifiers.None, false))
    w.Send(KeyUp((Key.F 12), Modifiers.None))
    settle w
    Assert.True(host.Layers |> Seq.exists (fun l -> l.Name = "Inspector" && l.Tree.HasVisibleContent))
    Assert.Equal<string list>(before, names ())

[<Fact>]
let ``Hello names whatever can be focused or clicked`` () =
    let w, host = show Scribble.Samples.HelloApp.app
    sized w
    Assert.Empty(unnamed (host.SemanticTree()))

[<Fact>]
let ``the Icons gallery names whatever can be focused or clicked`` () =
    let w, host = show Scribble.Samples.IconGallery.app
    sized w
    Assert.Empty(unnamed (host.SemanticTree()))
