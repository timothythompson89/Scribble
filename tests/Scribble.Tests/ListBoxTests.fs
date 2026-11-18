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

let private listOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, ListBox.Type))

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

/// A ListBox of `n` people in a ScrollViewer, items 30 DIPs of content high; `extra` settings after.
let private list (n: int) (extra: Setting<ListBox> list) =
    ScrollViewer.create [
        ScrollViewer.content (
            ListBox.create [
                ListBox.source (people n)
                ListBox.keySelector (fun (p: Person) -> p.Id)
                ListBox.viewSelector (fun (p: Person) -> Border.create [ Border.height 30.0; Border.content (TextBlock.create [ TextBlock.text p.Name ]) ])
                yield! extra
            ])
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

        ScrollViewer.create [
            ScrollViewer.content (
                ListBox.create [
                    ListBox.source source.Value
                    ListBox.keySelector (fun (p: Person) -> p.Id)
                    ListBox.viewSelector (fun (p: Person) -> p.Name)
                    ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> reports.Add e.Keys)
                ])
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
        ScrollViewer.create [
            ScrollViewer.content (
                ListBox.create [
                    ListBox.source items
                    ListBox.keySelector (fun (p: Person) -> p.Id)
                    ListBox.selected [ 4 ]
                    ListBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) -> reports.Add e.Keys)
                ])
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
