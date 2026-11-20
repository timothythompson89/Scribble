/// M8 step 4b: ListBox on the fake platform (DESIGN.md, "4b: ListBox and RadioButton"): selection by keys in each
/// mode, controlled and not, the keyboard (moving through items never realized), invoking, keys leaving the source,
/// and the same elements realized for 100 and 10,000 items.
module Scribble.Tests.ListBoxTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
open Scribble.Tests.AppTests

type Person = { Id: int; Name: string }

let private people n = Array.init n (fun i -> { Id = i; Name = "Person " + string i })

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let private show (view: View) =
    let w, _, app = start [ App.view view ]
    w.DeliverFrame() |> ignore
    w, (app :?> ElementHost)

/// The list inside the ListBox (a `ListBoxItems`): what holds the items.
let private listOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, ListBoxItems.Type))

/// The realized containers, in item order.
let private containers (host: ElementHost) =
    all host.Tree.Root.Value
    |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, ListBoxItem.Type))
    |> Seq.sortBy (fun e -> e.PositionInTree.Y)
    |> List.ofSeq

let private keyOf (e: Element) = unbox<int> (e.Get ListBoxItemProps.key)
let private container (host: ElementHost) (key: int) = containers host |> List.find (fun e -> keyOf e = key)
let private isSelected (e: Element) = e.Get TabHeaderProps.isSelected
let private selectedKeys (host: ElementHost) = containers host |> List.filter isSelected |> List.map keyOf

let private clickWith (w: FakeWindow) (mods: Modifiers) (e: Element) =
    let p = e.PositionInTree
    let at: PixelPoint = { X = p.X + 10.0; Y = p.Y + e.Bounds.Height / 2.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, mods))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, mods))
    w.DeliverFrame() |> ignore

let private click w e = clickWith w Modifiers.None e

let private keyWith (w: FakeWindow) (mods: Modifiers) k =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    w.DeliverFrame() |> ignore
    // A target realized by the key gets focus after the frame that realized it.
    w.DeliverFrame() |> ignore

let private key w k = keyWith w Modifiers.None k

/// A ListBox of `n` people, items 30 DIPs of content high; `extra` settings after.
let private list (n: int) (extra: Setting<ListBox> list) =
    ListBox.create [
        ListBox.source (people n)
        ListBox.keySelector (fun (p: Person) -> p.Id)
        ListBox.viewSelector (fun (p: Person) -> Border.create [ Border.height 30.0; Border.content (TextBlock.create [ TextBlock.text p.Name ]) ])
        yield! extra
    ]

type private Changes() =
    member val Keys = List<int list>()
    member val Items = List<string list>()
    member val Invoked = List<int>()
    member this.Settings: Setting<ListBox> list =
        [ ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) ->
              this.Keys.Add e.Keys
              this.Items.Add(e.Items |> List.map _.Name))
          ListBox.onItemInvoked (fun (e: ItemInvokedEventArgs<Person, int>) -> this.Invoked.Add e.Key) ]

[<Fact>]
let ``a click selects one item and keeps its own selection; Ctrl+click deselects`` () =
    let c = Changes()
    let w, host = show (list 20 c.Settings)
    click w (container host 2)
    Assert.Equal<int list>([ 2 ], selectedKeys host)
    Assert.Equal<string list>([ "Person 2" ], c.Items[0])
    w.Time.AdvanceMs 600.0
    click w (container host 4)
    Assert.Equal<int list>([ 4 ], selectedKeys host)
    // Clicking the selected item again keeps it.
    w.Time.AdvanceMs 600.0
    click w (container host 4)
    Assert.Equal<int list>([ 4 ], selectedKeys host)
    Assert.Equal(2, c.Keys.Count)
    clickWith w Modifiers.Control (container host 4)
    Assert.Empty(selectedKeys host)
    Assert.Equal<int list>([], c.Keys[2])

type private Msg = Picked of int list

[<Fact>]
let ``a controlled list shows exactly the model's selection`` () =
    let reports = List<int list>()
    for follow in [ true; false ] do
        reports.Clear()
        let w, _, app =
            start [
                App.program [ 1 ]
                    (fun (Picked keys) model ->
                        reports.Add keys
                        if follow then keys else model)
                    (fun model dispatch ->
                        list 20 [
                            ListBox.selected model
                            ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> dispatch (Picked e.Keys))
                        ])
            ]
        w.DeliverFrame() |> ignore
        let host = app :?> ElementHost
        Assert.Equal<int list>([ 1 ], selectedKeys host)
        click w (container host 3)
        w.DeliverFrame() |> ignore
        Assert.Equal<int list list>([ [ 3 ] ], List.ofSeq reports)
        Assert.Equal<int list>((if follow then [ 3 ] else [ 1 ]), selectedKeys host)

[<Fact>]
let ``Multiple: Ctrl+click toggles, Shift+click selects a range, Ctrl+A everything`` () =
    let c = Changes()
    let w, host = show (list 20 (ListBox.selectionMode SelectionMode.Multiple :: c.Settings))
    click w (container host 1)
    w.Time.AdvanceMs 600.0
    clickWith w Modifiers.Control (container host 3)
    Assert.Equal<int list>([ 1; 3 ], selectedKeys host)
    w.Time.AdvanceMs 600.0
    clickWith w Modifiers.Shift (container host 5)
    // The range runs from the anchor (3, the last plain or Ctrl click) to 5.
    Assert.Equal<int list>([ 3; 4; 5 ], selectedKeys host)
    w.Time.AdvanceMs 600.0
    clickWith w Modifiers.Control (container host 4)
    Assert.Equal<int list>([ 3; 5 ], selectedKeys host)
    keyWith w Modifiers.Control (Key.Char 'a')
    Assert.Equal(20, c.Keys[c.Keys.Count - 1].Length)

[<Fact>]
let ``the keyboard moves through 10,000 items realizing only what it reaches`` () =
    let c = Changes()
    let w, host = show (list 10_000 c.Settings)
    let lb = listOf host
    // Tab lands on the first item (nothing selected).
    key w Key.Tab
    Assert.True((container host 0).IsFocused)
    key w Key.Down
    Assert.Equal<int list>([ 1 ], selectedKeys host)
    Assert.True((container host 1).IsFocused)
    key w Key.End
    Assert.Equal<int list>([ 9999 ], c.Keys[c.Keys.Count - 1])
    Assert.True((container host 9999).IsFocused)
    let struct (realized, total) = (ItemsView.realizedCount lb |> ValueOption.defaultValue (struct (0, 0)))
    Assert.Equal(10_000, total)
    Assert.InRange(realized, 1, 40)
    key w Key.Home
    Assert.True((container host 0).IsFocused)
    key w Key.PageDown
    Assert.True((selectedKeys host |> List.head) > 1)
    // Tab away and back: focus returns to the selected item.
    let selected = selectedKeys host |> List.head
    keyWith w Modifiers.Shift Key.Tab
    key w Key.Tab
    Assert.True((container host selected).IsFocused)

[<Fact>]
let ``Multiple: arrows move focus only, Space toggles, Shift+End selects a range through unrealized items`` () =
    let c = Changes()
    let w, host = show (list 10_000 (ListBox.selectionMode SelectionMode.Multiple :: c.Settings))
    key w Key.Tab
    key w Key.Down
    key w Key.Down
    Assert.Empty(selectedKeys host)
    Assert.True((container host 2).IsFocused)
    key w Key.Space
    Assert.Equal<int list>([ 2 ], selectedKeys host)
    keyWith w Modifiers.Shift Key.End
    let last = c.Keys[c.Keys.Count - 1]
    Assert.Equal(9998, last.Length)
    Assert.Equal(2, List.head last)
    Assert.Equal(9999, List.last last)
    let struct (realized, _) = (ItemsView.realizedCount (listOf host)).Value
    Assert.InRange(realized, 1, 40)

[<Fact>]
let ``a double-click or Enter invokes; None mode invokes on a click and never selects`` () =
    let c = Changes()
    let w, host = show (list 20 c.Settings)
    click w (container host 2)
    click w (container host 2)
    Assert.Equal<int list>([ 2 ], List.ofSeq c.Invoked)
    Assert.Equal<int list>([ 2 ], selectedKeys host)
    // Far apart: two clicks, no invoke.
    w.Time.AdvanceMs 600.0
    click w (container host 2)
    Assert.Equal(1, c.Invoked.Count)
    key w Key.Enter
    Assert.Equal<int list>([ 2; 2 ], List.ofSeq c.Invoked)
    let n = Changes()
    let w, host = show (list 20 (ListBox.selectionMode SelectionMode.None :: n.Settings))
    click w (container host 5)
    Assert.Equal<int list>([ 5 ], List.ofSeq n.Invoked)
    Assert.Empty(selectedKeys host)
    Assert.Empty(n.Keys)
    key w Key.Down
    key w Key.Space
    Assert.Equal<int list>([ 5; 6 ], List.ofSeq n.Invoked)

[<Fact>]
let ``two slow clicks in an idle window don't invoke`` () =
    // No frame is drawn between the clicks (nothing changes while the pointer rests): the second click is timed
    // by the platform's clock at the click, not by the last frame, which was drawn just after the first.
    let c = Changes()
    let w, host = show (list 20 c.Settings)
    click w (container host 2)
    while w.DeliverFrame() do ()
    w.Time.AdvanceMs 2000.0
    click w (container host 2)
    Assert.Empty c.Invoked
    Assert.Equal<int list>([ 2 ], selectedKeys host)
    // Soon after: a double-click with the second.
    w.Time.AdvanceMs 100.0
    click w (container host 2)
    Assert.Equal<int list>([ 2 ], List.ofSeq c.Invoked)

[<Fact>]
let ``the selection follows keys through sorting and removal`` () =
    let reports = List<int list>()
    let source = ref (people 10)
    let view () =

        ListBox.create [
            ListBox.source source.Value
            ListBox.keySelector (fun (p: Person) -> p.Id)
            ListBox.viewSelector (fun (p: Person) -> p.Name)
            ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> reports.Add e.Keys)
        ]
    let w, host = show (view ())
    click w (container host 4)
    reports.Clear()
    // Reversed: item 4 is still the selected one, now at index 5.
    source.Value <- Array.rev source.Value
    host.Update(view ())
    w.DeliverFrame() |> ignore
    Assert.Equal<int list>([ 4 ], selectedKeys host)
    Assert.Empty reports
    // Removed: the list (keeping its own selection) drops it and says so.
    source.Value <- source.Value |> Array.filter (fun p -> p.Id <> 4)
    host.Update(view ())
    w.DeliverFrame() |> ignore
    w.DeliverFrame() |> ignore
    Assert.Empty(selectedKeys host)
    Assert.Equal<int list list>([ [] ], List.ofSeq reports)

[<Fact>]
let ``a controlled selection's missing key is left to the app`` () =
    let reports = List<int list>()
    let view (items: Person array) =
        ListBox.create [
            ListBox.source items
            ListBox.keySelector (fun (p: Person) -> p.Id)
            ListBox.selected [ 4 ]
            ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> reports.Add e.Keys)
        ]
    let w, host = show (view (people 10))
    Assert.Equal<int list>([ 4 ], selectedKeys host)
    host.Update(view (people 10 |> Array.filter (fun p -> p.Id <> 4)))
    w.DeliverFrame() |> ignore
    w.DeliverFrame() |> ignore
    Assert.Empty(selectedKeys host)
    Assert.Empty reports
    // Back again: selected again.
    host.Update(view (people 10))
    w.DeliverFrame() |> ignore
    Assert.Equal<int list>([ 4 ], selectedKeys host)

[<Fact>]
let ``the same elements are realized for 100 and 10,000 items`` () =
    let counts =
        [ for n in [ 100; 10_000 ] ->
              let _, host = show (list n [])
              let struct (r, _) = (ItemsView.realizedCount (listOf host)).Value
              r ]
    Assert.Equal(counts[0], counts[1])

[<Fact>]
let ``to a screen reader: a list (multi-select in Multiple mode) of items selected as shown, which select and invoke`` () =
    for mode in [ SelectionMode.Single; SelectionMode.Multiple ] do
        let c = Changes()
        let _, host = show (list 20 (ListBox.selected [ 2 ] :: ListBox.selectionMode mode :: c.Settings))
        let list = host.SemanticTree().Children[0].Children[0]
        Assert.Equal(Role.List, list.Role)
        Assert.Equal((mode = SelectionMode.Multiple), list.IsMultiSelectable)
        Assert.Equal("ListItem \"Person 2\" selected 3 of 20", list.Children[2].Describe())
        Assert.Equal("ListItem \"Person 1\" 2 of 20", list.Children[1].Describe())
        // The default action selects (a list keeping the app's selection only asks) and invokes.
        Assert.True(list.Children[4].Perform SemanticAction.Click)
        Assert.Equal<int list>([ 4 ], List.ofSeq c.Invoked)
        Assert.Equal<int list>((if mode = SelectionMode.Single then [ 4 ] else [ 2; 4 ]), c.Keys[0])

[<Fact>]
let ``after a click, focus moved by the arrow keys shows the ring`` () =
    let w, host = show (list 20 [])
    click w (container host 2)
    Assert.True((container host 2).IsFocused)
    Assert.False((container host 2).IsFocusVisible)
    key w Key.Down
    Assert.True((container host 3).IsFocused)
    Assert.True((container host 3).IsFocusVisible)
    // A press puts it away again.
    click w (container host 5)
    Assert.False((container host 5).IsFocusVisible)

// ---- A ListBox scrolls on its own (DESIGN.md, "ListBox scrolls on its own") ----

let private ofType (host: ElementHost) (t: ElementType) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, t))
let private realizedIn (host: ElementHost) = let struct (r, _) = (ItemsView.realizedCount (listOf host)).Value in r

/// What a ListBox was before it scrolled on its own: its list, wrapped in a ScrollViewer by hand.
let private byHand (n: int) (extra: Setting<ItemsView> list) =
    ScrollViewer.create [
        ScrollViewer.content (
            View.Create(
                ListBoxItems.Type,
                ([ ListBox.source (people n)
                   ListBox.keySelector (fun (p: Person) -> p.Id)
                   ListBox.viewSelector (fun (p: Person) -> Border.create [ Border.height 30.0; Border.content (TextBlock.create [ TextBlock.text p.Name ]) ])
                   yield! extra ]: Setting<ItemsView> list)))
    ]

[<Fact>]
let ``it holds a ScrollViewer with its list inside, and scrolls in the height it is given`` () =
    let w, host = show (list 10_000 [])
    let lb = ofType host ListBox.Type
    let scroll = Seq.exactlyOne lb.Children
    Assert.Same(ScrollViewer.Type, scroll.Type)
    Assert.Same(ListBoxItems.Type, (Seq.exactlyOne scroll.Children).Type)
    Assert.Same(listOf host, (ListBox.listOf lb).Value)
    // As tall as the window, not as its items; only what shows is realized.
    Assert.Equal(host.Tree.Root.Value.Bounds.Height, lb.Bounds.Height)
    Assert.InRange(lb.Bounds.Height, 100.0, 5000.0)
    Assert.InRange(realizedIn host, 1, 40)
    // Scrolled from code, other items show.
    let first = containers host |> List.head |> keyOf
    ScrollViewer.jumpTo scroll 3000.0
    while w.DeliverFrame() do ()
    Assert.True((containers host |> List.head |> keyOf) > first)
    Assert.InRange(realizedIn host, 1, 40)

[<Fact>]
let ``to a screen reader it is what a list wrapped by hand was, and its label names the list`` () =
    for mode in [ SelectionMode.Single; SelectionMode.Multiple ] do
        let _, mine = show (list 40 [ ListBox.selected [ 2 ]; ListBox.selectionMode mode; Semantics.label "People" ])
        let _, hand =
            show (byHand 40 [ Setting.create ListBoxProps.selected [ box 2 ]; Setting.create ListBoxProps.selectionMode mode; Semantics.label "People" ])
        Assert.Equal(hand.SemanticTree().ToString(), mine.SemanticTree().ToString())
        let area = mine.SemanticTree().Children[0]
        Assert.Equal(Role.ScrollArea, area.Role)
        Assert.Equal(Role.List, area.Children[0].Role)
        Assert.Equal("People", area.Children[0].Name)

[<Fact>]
let ``a Label for a ListBox names its list`` () =
    let _, host =
        show (
            DockPanel.create [
                DockPanel.children [
                    Label.create [ Label.text "Team"; Label.target "team"; DockPanel.dock Dock.Top ]
                    list 5 [ ListBox.name "team" ]
                ]
            ])
    Assert.Contains("List \"Team\"", host.SemanticTree().ToString())

[<Fact>]
let ``an app's ScrollViewer style reaches a ListBox's scrollbar`` () =
    let styles = [ ScrollViewer.style [] [ ScrollViewer.barSize 8.0 ] ]
    let w, _, app = start [ App.styles styles; App.view (list 100 []) ]
    w.DeliverFrame() |> ignore
    let host = app :?> ElementHost
    Assert.Equal(8.0, (ofType host ScrollViewer.Type).Get ScrollViewerProps.barSize)
    // And one for the scrollers inside ListBoxes alone.
    let styles = [ ScrollViewer.style [ Style.inside ListBox.Type [] ] [ ScrollViewer.barSize 6.0 ] ]
    let view = StackPanel.create [ StackPanel.children [ ScrollViewer.create [ ScrollViewer.height 50.0 ]; list 5 [] ] ]
    let w, _, app = start [ App.styles styles; App.view view ]
    w.DeliverFrame() |> ignore
    let host = app :?> ElementHost
    let scrollers = all host.Tree.Root.Value |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, ScrollViewer.Type)) |> List.ofSeq
    Assert.Equal<float list>([ 12.0; 6.0 ], scrollers |> List.map (fun e -> e.Get ScrollViewerProps.barSize))

[<Fact>]
let ``its scroller scrolls to an item never realized, and is the one found by the ListBox's name`` () =
    let scroller = Scroller()
    let w, host = show (list 10_000 [ ListBox.scroller scroller; ListBox.name "people" ])
    scroller.ScrollTo(9000)
    while w.DeliverFrame() do ()
    Assert.Contains(9000, containers host |> List.map keyOf)
    Assert.InRange(realizedIn host, 1, 40)
    let found, named = host.AppValues.NamedScrollers["people"].TryGetTarget()
    Assert.True(found && obj.ReferenceEquals(named, scroller))

[<Fact>]
let ``maxHeight limits it where the height is unlimited`` () =
    let _, host = show (StackPanel.create [ StackPanel.children [ list 10_000 [ ListBox.maxHeight 240.0 ] ] ])
    Assert.Equal(240.0, (ofType host ListBox.Type).Bounds.Height)
    Assert.InRange(realizedIn host, 1, 40)

[<Fact>]
let ``inside a ScrollViewer (a header above it, or code written before it scrolled) it still realizes only what shows`` () =
    let view =
        ScrollViewer.create [
            ScrollViewer.content (
                StackPanel.create [
                    StackPanel.children [ Border.create [ Border.height 80.0 ]; list 10_000 [] ]
                ])
        ]
    let w, host = show view
    while w.DeliverFrame() do ()
    Assert.InRange(realizedIn host, 1, 40)
    // The keys still move through it, and the outer ScrollViewer follows.
    key w Key.Tab
    key w Key.End
    while w.DeliverFrame() do ()
    Assert.True((container host 9999).IsFocused)
    Assert.InRange(realizedIn host, 1, 40)

/// What a debug build says of ListBoxes with unlimited height while `act` runs (and whether this listener was
/// still there at the end: a test that sets the listener outright may drop it).
let private warningsWhile (act: unit -> unit) =
    let warnings = List<string>()
    let listener = System.Action<string>(fun m -> lock warnings (fun () -> warnings.Add m))
    DebugWarnings.Listener <- System.Delegate.Combine(DebugWarnings.Listener, listener) :?> System.Action<string>
    let mutable heard = true
    try
        act ()
        heard <- not (isNull DebugWarnings.Listener) && (DebugWarnings.Listener.GetInvocationList() |> Array.contains (listener :> System.Delegate))
    finally
        DebugWarnings.Listener <- System.Delegate.Remove(DebugWarnings.Listener, listener) :?> System.Action<string>
    heard, lock warnings (fun () -> warnings |> Seq.filter (fun m -> m.Contains "ListBox" && m.Contains "unlimited height") |> List.ofSeq)

[<Fact>]
let ``a Debug build warns of a big ListBox with unlimited height, and of no other`` () =
    let inStack (n: int) (extra: Setting<ListBox> list) = StackPanel.create [ StackPanel.children [ list n extra ] ]
    // Few items, a height of its own, a window's height, a ScrollViewer around it: nothing to say.
    let heard, quiet =
        warningsWhile (fun () ->
            show (inStack 200 [ ListBox.name "quiet" ]) |> ignore
            show (inStack 5000 [ ListBox.name "quiet"; ListBox.maxHeight 200.0 ]) |> ignore
            show (list 5000 [ ListBox.name "quiet" ]) |> ignore
            show (ScrollViewer.create [ ScrollViewer.content (inStack 5000 [ ListBox.name "quiet" ]) ]) |> ignore)
    if heard then Assert.Empty(quiet |> List.filter (fun m -> m.Contains "\"quiet\""))
    let heard, loud = warningsWhile (fun () -> show (inStack 201 [ ListBox.name "loud" ]) |> ignore)
#if DEBUG
    if heard then
        let message = Assert.Single(loud |> List.filter (fun m -> m.Contains "\"loud\""))
        Assert.Contains("201 items", message)
        Assert.Contains("ListBox.maxHeight", message)
#else
    ignore (heard, loud)
#endif

[<Fact>]
let ``it has a frame by default (a line of 1 and a padding of 4 round the scrolling part), which settings take away`` () =
    let _, host = show (StackPanel.create [ StackPanel.children [ list 100 [ ListBox.height 200.0 ] ] ])
    let lb = ofType host ListBox.Type
    let scroll = Seq.exactlyOne lb.Children
    Assert.Equal(200.0, lb.Bounds.Height)
    // The line, then the gap a flyout keeps round a ComboBox's open list.
    Assert.Equal(1.0, lb.Get BorderProps.borderThickness)
    Assert.Equal((5.0, 5.0), (scroll.Bounds.X, scroll.Bounds.Y))
    Assert.Equal((lb.Bounds.Width - 10.0, 190.0), (scroll.Bounds.Width, scroll.Bounds.Height))
    Assert.InRange(realizedIn host, 1, 40)
    // No frame, edge to edge.
    let bare = list 100 [ ListBox.background Scribble.Rendering.Color.Transparent; ListBox.borderThickness 0.0; ListBox.padding 0.0 ]
    let _, host = show bare
    let lb = ofType host ListBox.Type
    let scroll = Seq.exactlyOne lb.Children
    Assert.Equal((0.0, 0.0), (scroll.Bounds.X, scroll.Bounds.Y))
    Assert.Equal((lb.Bounds.Width, lb.Bounds.Height), (scroll.Bounds.Width, scroll.Bounds.Height))
    Assert.Equal(0.0f, (lb.Get BorderProps.background).A)

[<Fact>]
let ``its frame is a ComboBox's and a TextBox's, in both themes`` () =
    for theme in [ Theme.dark []; Theme.light [] ] do
        let view =
            StackPanel.create [
                StackPanel.children [ list 5 []; ComboBox.create [ ComboBox.source [ "a"; "b" ] ]; TextBox.create [] ]
            ]
        let w, _, app = start [ App.theme theme; App.view view ]
        w.DeliverFrame() |> ignore
        let host = app :?> ElementHost
        let lb, combo, text = ofType host ListBox.Type, ofType host ComboBox.Type, ofType host TextBox.Type
        for other in [ combo; text ] do
            Assert.Equal(other.Get BorderProps.background, lb.Get BorderProps.background)
            Assert.Equal(other.Get BorderProps.borderBrush, lb.Get BorderProps.borderBrush)
            Assert.Equal(other.Get BorderProps.borderThickness, lb.Get BorderProps.borderThickness)
            Assert.Equal(other.Get BorderProps.cornerRadius, lb.Get BorderProps.cornerRadius)
        Assert.True((lb.Get BorderProps.background).A > 0.0f)

// ---- Arrow keys in wrap and grid layouts (DESIGN.md, "A ListBox's arrow keys in wrap and grid layouts") ----

/// The key of the item with the focus.
let private focusedKey (host: ElementHost) = containers host |> List.find (fun e -> e.IsFocused) |> keyOf

/// A list with no frame, `width` wide inside its scrollbar's gutter of 12.
let private bare (width: float) (extra: Setting<ListBox> list) : Setting<ListBox> list =
    [ ListBox.background Scribble.Rendering.Color.Transparent
      ListBox.borderThickness 0.0
      ListBox.padding 0.0
      ListBox.width (width + 12.0)
      ListBox.horizontalAlignment HorizontalAlignment.Left
      yield! extra ]

[<Fact>]
let ``in a grid, Up and Down move by a row and Left and Right by an item, on to the next row`` () =
    // Ten items in four columns: 0 1 2 3 / 4 5 6 7 / 8 9.
    let w, host = show (list 10 (bare 400.0 [ ListBox.layout (ItemsLayout.grid 4) ]))
    key w Key.Tab
    Assert.Equal(0, focusedKey host)
    key w Key.Right
    key w Key.Right
    Assert.Equal<int list>([ 2 ], selectedKeys host)
    key w Key.Down
    Assert.Equal(6, focusedKey host)
    // Nothing is below 6 in the short last row: its last item.
    key w Key.Down
    Assert.Equal(9, focusedKey host)
    // In the last row Down does nothing; Up goes straight up from here (no remembered column).
    key w Key.Down
    Assert.Equal(9, focusedKey host)
    key w Key.Up
    Assert.Equal(5, focusedKey host)
    key w Key.Up
    key w Key.Up
    Assert.Equal(1, focusedKey host)
    // Left and Right carry on over a row's end, and stop at the first and the last item.
    key w Key.Down
    key w Key.Left
    Assert.Equal(4, focusedKey host)
    key w Key.Left
    Assert.Equal(3, focusedKey host)
    key w Key.Right
    Assert.Equal(4, focusedKey host)
    key w Key.Home
    key w Key.Left
    Assert.Equal(0, focusedKey host)
    key w Key.End
    key w Key.Right
    Assert.Equal(9, focusedKey host)
    Assert.Equal<int list>([ 9 ], selectedKeys host)

[<Fact>]
let ``in a wrap layout the page keys move by the rows that show, keeping the column, through 10,000 items`` () =
    // Three cells of 100 fit the width; five rows of 40 fit the height.
    let settings = bare 300.0 [ ListBox.height 200.0; ListBox.verticalAlignment VerticalAlignment.Top; ListBox.layout (ItemsLayout.wrap 100.0 40.0) ]
    let w, host = show (list 10_000 settings)
    Assert.Equal(3, ItemsView.columns (listOf host))
    key w Key.Tab
    key w Key.Right
    key w Key.Down
    Assert.Equal(4, focusedKey host)
    key w Key.PageDown
    Assert.Equal(19, focusedKey host)
    key w Key.PageDown
    Assert.Equal(34, focusedKey host)
    key w Key.PageUp
    Assert.Equal(19, focusedKey host)
    // Past the top: the first row, same column.
    key w Key.PageUp
    key w Key.PageUp
    Assert.Equal(1, focusedKey host)
    // The last row holds 9,999 alone. From 9,998 (the row above, third column) there is nothing below.
    key w Key.End
    Assert.Equal(9999, focusedKey host)
    key w Key.Left
    Assert.Equal(9998, focusedKey host)
    key w Key.PageDown
    Assert.Equal(9999, focusedKey host)
    key w Key.Up
    Assert.Equal(9996, focusedKey host)
    Assert.InRange(realizedIn host, 1, 60)

[<Fact>]
let ``in a grid, Ctrl moves the focus only and Shift extends the selection in source order`` () =
    let c = Changes()
    let w, host = show (list 12 (bare 400.0 (ListBox.layout (ItemsLayout.grid 4) :: ListBox.selectionMode SelectionMode.Multiple :: c.Settings)))
    key w Key.Tab
    key w Key.Right
    Assert.Equal(1, focusedKey host)
    Assert.Empty(selectedKeys host)
    key w Key.Space
    keyWith w Modifiers.Shift Key.Down
    Assert.Equal<int list>([ 1; 2; 3; 4; 5 ], selectedKeys host)
    keyWith w Modifiers.Shift Key.Right
    Assert.Equal<int list>([ 1; 2; 3; 4; 5; 6 ], selectedKeys host)
    // Single: Ctrl+Right moves the focus and leaves the selection.
    let w, host = show (list 12 (bare 400.0 [ ListBox.layout (ItemsLayout.grid 4) ]))
    key w Key.Tab
    key w Key.Down
    Assert.Equal<int list>([ 4 ], selectedKeys host)
    keyWith w Modifiers.Control Key.Right
    Assert.Equal(5, focusedKey host)
    Assert.Equal<int list>([ 4 ], selectedKeys host)

[<Fact>]
let ``in a wrapFill layout the keys follow its rows`` () =
    // 300 wide: three cells of at least 90, widened to 100.
    let w, host = show (list 10 (bare 300.0 [ ListBox.layout (ItemsLayout.wrapFill 90.0 40.0) ]))
    Assert.Equal(3, ItemsView.columns (listOf host))
    key w Key.Tab
    key w Key.Right
    key w Key.Down
    Assert.Equal(4, focusedKey host)
    key w Key.Right
    key w Key.Right
    Assert.Equal(6, focusedKey host)
    key w Key.Down
    Assert.Equal(9, focusedKey host)
    key w Key.Up
    Assert.Equal(6, focusedKey host)

[<Fact>]
let ``in a stack, Left and Right are not the list's`` () =
    let w, host = show (list 20 [])
    key w Key.Tab
    key w Key.Down
    key w Key.Right
    key w Key.Left
    Assert.Equal(1, focusedKey host)
    Assert.Equal<int list>([ 1 ], selectedKeys host)
    Assert.Equal(1, ItemsView.columns (listOf host))

// ---- A control inside an item that leaves input to the item (README, "ListBox") ----

/// A Multiple list the view controls, each item a CheckBox ticked while its item is selected; the box takes no
/// pointer and no focus when `ignores`.
let private checkList (ignores: bool) (toggles: int ref) =
    Component.define "CheckList" (fun ctx () ->
        let selected = ctx.useState ([]: int list)
        ListBox.create [
            ListBox.source (people 5)
            ListBox.keySelector (fun (p: Person) -> p.Id)
            ListBox.selectionMode SelectionMode.Multiple
            ListBox.selected selected.Current
            ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> selected.Set e.Keys)
            ListBox.viewSelector (fun (p: Person) ->
                CheckBox.create [
                    CheckBox.text p.Name
                    CheckBox.isChecked (List.contains p.Id selected.Current)
                    CheckBox.onCheckedChanged (fun _ -> toggles.Value <- toggles.Value + 1)
                    if ignores then
                        CheckBox.isHitTestVisible false
                        CheckBox.focusable false
                ])
        ])

let private boxIn (e: Element) = all e |> Seq.find (fun c -> obj.ReferenceEquals(c.Type, CheckBox.Type))
let private checkedKeys (host: ElementHost) = containers host |> List.filter (fun c -> (boxIn c).Get ToggleButtonProps.isChecked) |> List.map keyOf

[<Fact>]
let ``a CheckBox in an item that takes no pointer and no focus leaves both to the item, and shows its selection`` () =
    let toggles = ref 0
    let w, host = show (checkList true toggles ())
    // A click on the box is a click on the item: the item is selected, and the view ticks its box.
    click w (boxIn (container host 2))
    Assert.Equal<int list>([ 2 ], selectedKeys host)
    Assert.Equal<int list>([ 2 ], checkedKeys host)
    clickWith w Modifiers.Control (boxIn (container host 4))
    Assert.Equal<int list>([ 2; 4 ], checkedKeys host)
    clickWith w Modifiers.Control (boxIn (container host 2))
    Assert.Equal<int list>([ 4 ], selectedKeys host)
    Assert.Equal<int list>([ 4 ], checkedKeys host)
    // The box itself was never asked.
    Assert.Equal(0, toggles.Value)
    // Tab stops on the list's item and then leaves the list: never on a box. Space selects from the keyboard.
    let w, host = show (checkList true toggles ())
    key w Key.Tab
    Assert.True(obj.ReferenceEquals((all host.Tree.Root.Value |> Seq.find (fun e -> e.IsFocused)).Type, ListBoxItem.Type))
    key w Key.Space
    Assert.Equal<int list>([ 0 ], checkedKeys host)
    key w Key.Tab
    Assert.DoesNotContain(all host.Tree.Root.Value, fun e -> e.IsFocused && obj.ReferenceEquals(e.Type, CheckBox.Type))

[<Fact>]
let ``a plain CheckBox in an item takes the click itself`` () =
    let toggles = ref 0
    let w, host = show (checkList false toggles ())
    click w (boxIn (container host 2))
    Assert.Equal(1, toggles.Value)
    Assert.Empty(selectedKeys host)
