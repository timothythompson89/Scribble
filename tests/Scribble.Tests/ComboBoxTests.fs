/// M8 step 4c: ComboBox on the fake platform (DESIGN.md, "4c: ComboBox"): opening and closing, picking with the
/// pointer and both keyboards (closed and open), controlled and not, focus coming back, keys leaving the source,
/// semantics, and the same elements realized for 100 and 10,000 items.
module Scribble.Tests.ComboBoxTests

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

let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 20 do
        n <- n + 1

let private show (view: View) =
    let w, _, app = start [ App.view view ]
    let host = app :?> ElementHost
    settle w
    w, host

let private boxOf (host: ElementHost) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, ComboBox.Type))
let private flyout (host: ElementHost) = host.Layers |> Seq.tryFind (fun l -> l.Name.StartsWith "Flyout")
let private isOpen (host: ElementHost) = (flyout host).IsSome
let private inFlyout (host: ElementHost) = all (flyout host).Value.Tree.Root.Value

/// The open list's realized containers, in item order.
let private containers (host: ElementHost) =
    inFlyout host |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, ListBoxItem.Type)) |> Seq.sortBy (fun e -> e.PositionInTree.Y) |> List.ofSeq

let private keyOf (e: Element) = unbox<int> (e.Get ListBoxItemProps.key)

/// The text the box shows (the selected item's, or the placeholder).
let private shown (host: ElementHost) =
    all (boxOf host) |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, TextBlock.Type)) |> Seq.map (fun e -> e.Get TextProps.text) |> String.concat " "

let private clickOn (w: FakeWindow) (e: Element) =
    let p = e.PositionInTree
    let at: PixelPoint = { X = p.X + 10.0; Y = p.Y + e.Bounds.Height / 2.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    settle w

let private keyWith (w: FakeWindow) (mods: Modifiers) k =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    settle w

let private key w k = keyWith w Modifiers.None k

/// The key of the list item that has focus in the open list.
let private focusedKey (host: ElementHost) =
    match host.Focused with
    | ValueSome e when obj.ReferenceEquals(e.Type, ListBoxItem.Type) -> keyOf e
    | _ -> -1

type private Changes() =
    member val Keys = List<int voption>()
    member val Items = List<string>()
    member c.Handler =
        ComboBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<Person, int>) ->
            c.Keys.Add e.Key
            c.Items.Add(match e.Item with ValueSome p -> p.Name | ValueNone -> "(none)"))

/// A ComboBox of `n` people (300 DIPs wide, at the top left), then a button to Tab to; `extra` settings after.
let private combo (n: int) (extra: Setting<ComboBox> list) =
    StackPanel.create [
        StackPanel.horizontalAlignment HorizontalAlignment.Left
        StackPanel.children [
            ComboBox.create [
                ComboBox.width 300.0
                ComboBox.source (people n)
                ComboBox.keySelector (fun (p: Person) -> p.Id)
                ComboBox.viewSelector (fun (p: Person) -> p.Name)
                ComboBox.placeholder "Pick someone"
                yield! extra
            ]
            Button.create [ Button.name "after"; Button.text "After" ]
        ]
    ]

[<Fact>]
let ``it shows the placeholder, opens on a click, and a click on an item picks it and closes`` () =
    let changes = Changes()
    let w, host = show (combo 5 [ changes.Handler ])
    Assert.Equal("Pick someone", shown host)
    Assert.False(isOpen host)
    clickOn w (boxOf host)
    Assert.True(isOpen host)
    Assert.Equal<int list>([ 0; 1; 2; 3; 4 ], containers host |> List.map keyOf)
    clickOn w (containers host |> List.find (fun e -> keyOf e = 3))
    Assert.False(isOpen host)
    Assert.Equal("Person 3", shown host)
    Assert.Equal<int voption list>([ ValueSome 3 ], List.ofSeq changes.Keys)
    Assert.Equal<string list>([ "Person 3" ], List.ofSeq changes.Items)
    // Opened again, the list shows the selection; picking the same item changes nothing.
    clickOn w (boxOf host)
    Assert.Equal<int list>([ 3 ], containers host |> List.filter (fun e -> e.Get TabHeaderProps.isSelected) |> List.map keyOf)
    clickOn w (containers host |> List.find (fun e -> keyOf e = 3))
    Assert.False(isOpen host)
    Assert.Equal(1, changes.Keys.Count)

[<Fact>]
let ``clicking the box again, or pressing elsewhere, closes the list with nothing changed`` () =
    let changes = Changes()
    let w, host = show (combo 5 [ changes.Handler ])
    clickOn w (boxOf host)
    clickOn w (boxOf host)
    Assert.False(isOpen host)
    clickOn w (boxOf host)
    Assert.True(isOpen host)
    w.Send(PointerPressed(Mouse, 0, { X = 700.0; Y = 500.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 700.0; Y = 500.0 }, MouseButton.Left, Modifiers.None))
    settle w
    Assert.False(isOpen host)
    Assert.Empty changes.Keys

[<Fact>]
let ``opening puts focus on the selected item, scrolled into view`` () =
    let w, host = show (combo 1000 [ ComboBox.selected (ValueSome 500) ])
    (boxOf host).Focus()
    key w Key.Enter
    Assert.True(isOpen host)
    Assert.Equal(500, focusedKey host)
    // Only what shows is realized, around the selection.
    let keys = containers host |> List.map keyOf
    Assert.Contains(500, keys)
    Assert.DoesNotContain(0, keys)

[<Fact>]
let ``in the open list the arrows only move focus; Enter picks, closes and gives focus back to the box`` () =
    let changes = Changes()
    let w, host = show (combo 20 [ changes.Handler ])
    let box = boxOf host
    box.Focus()
    key w Key.Space
    Assert.True(isOpen host)
    Assert.Equal(0, focusedKey host)
    key w Key.Down
    key w Key.Down
    Assert.Equal(2, focusedKey host)
    Assert.Empty changes.Keys
    Assert.Equal("Pick someone", shown host)
    key w Key.Enter
    Assert.False(isOpen host)
    Assert.Equal<int voption list>([ ValueSome 2 ], List.ofSeq changes.Keys)
    Assert.Equal("Person 2", shown host)
    Assert.True box.IsFocused
    Assert.True box.IsFocusVisible

[<Fact>]
let ``Escape and Tab close the list with nothing changed, and focus comes back`` () =
    let changes = Changes()
    let w, host = show (combo 20 [ changes.Handler ])
    let box = boxOf host
    box.Focus()
    for closing in [ Key.Escape; Key.Tab ] do
        key w Key.Enter
        Assert.True(isOpen host)
        key w Key.Down
        key w closing
        Assert.False(isOpen host)
        Assert.True box.IsFocused
    Assert.Empty changes.Keys
    Assert.Equal("Pick someone", shown host)

[<Fact>]
let ``closed, the arrows and Home and End pick the next item without opening`` () =
    let changes = Changes()
    let w, host = show (combo 5 [ changes.Handler ])
    (boxOf host).Focus()
    key w Key.Down
    key w Key.Down
    key w Key.Up
    key w Key.End
    key w Key.Down // already the last: nothing
    key w Key.Home
    key w Key.Up // already the first: nothing
    Assert.False(isOpen host)
    Assert.Equal<int voption list>([ ValueSome 0; ValueSome 1; ValueSome 0; ValueSome 4; ValueSome 0 ], List.ofSeq changes.Keys)
    Assert.Equal("Person 0", shown host)

[<Fact>]
let ``Alt+Down and F4 open the list`` () =
    let w, host = show (combo 5 [])
    (boxOf host).Focus()
    keyWith w Modifiers.Alt Key.Down
    Assert.True(isOpen host)
    key w Key.Escape
    Assert.False(isOpen host)
    key w (Key.F 4)
    Assert.True(isOpen host)

[<Fact>]
let ``controlled, it shows exactly the key given and only tells the handler`` () =
    let changes = Changes()
    let w, host = show (combo 5 [ ComboBox.selected (ValueSome 1); changes.Handler ])
    Assert.Equal("Person 1", shown host)
    (boxOf host).Focus()
    key w Key.Down
    Assert.Equal<int voption list>([ ValueSome 2 ], List.ofSeq changes.Keys)
    Assert.Equal("Person 1", shown host)
    clickOn w (boxOf host)
    clickOn w (containers host |> List.find (fun e -> keyOf e = 4))
    Assert.False(isOpen host)
    Assert.Equal("Person 1", shown host)
    Assert.Equal<int voption list>([ ValueSome 2; ValueSome 4 ], List.ofSeq changes.Keys)

[<Fact>]
let ``a selected key that leaves the source: its own is dropped and reported, a controlled one shows the placeholder`` () =
    let changes = Changes()
    let view (n: int) (extra: Setting<ComboBox> list) =
        ComboBox.create (
            [ ComboBox.source (people n)
              ComboBox.keySelector (fun (p: Person) -> p.Id)
              ComboBox.viewSelector (fun (p: Person) -> p.Name)
              ComboBox.placeholder "Pick someone"
              changes.Handler ]
            @ extra)
    // Its own selection.
    let w, host = show (view 5 [])
    (boxOf host).Focus()
    key w Key.End
    Assert.Equal("Person 4", shown host)
    host.Update(view 3 [])
    settle w
    Assert.Equal("Pick someone", shown host)
    Assert.Equal<int voption list>([ ValueSome 4; ValueNone ], List.ofSeq changes.Keys)
    // Controlled: nothing is raised, and the key shows again when it comes back.
    changes.Keys.Clear()
    let controlled = [ ComboBox.selected (ValueSome 4) ]
    let w, host = show (view 5 controlled)
    Assert.Equal("Person 4", shown host)
    host.Update(view 3 controlled)
    settle w
    Assert.Equal("Pick someone", shown host)
    Assert.Empty changes.Keys
    host.Update(view 5 controlled)
    settle w
    Assert.Equal("Person 4", shown host)

[<Fact>]
let ``the list is as wide as the box, at most maxListHeight tall, and realizes the same elements for 100 and 10,000 items`` () =
    let count n =
        let w, host = show (combo n [ ComboBox.maxListHeight 200.0 ])
        clickOn w (boxOf host)
        let root = (flyout host).Value.Tree.Root.Value
        Assert.Equal(300.0, root.Bounds.Width)
        Assert.True(root.Bounds.Height <= 200.0 + 20.0, "the flyout is " + string root.Bounds.Height + " tall")
        inFlyout host |> Seq.length
    let small = count 100
    Assert.Equal(small, count 10_000)
    Assert.True(small < 80, string small + " elements for a 200 DIP list")

[<Fact>]
let ``a view per item shows in the list and in the box, and clicks on the box's copy open the list`` () =
    let clicks = List<int>()
    let view =
        ComboBox.create [
            ComboBox.width 300.0
            ComboBox.source (people 3)
            ComboBox.keySelector (fun (p: Person) -> p.Id)
            ComboBox.viewSelector (fun (p: Person) -> Button.create [ Button.text p.Name; Button.onClick (fun _ -> clicks.Add p.Id) ])
            ComboBox.selected (ValueSome 1)
        ]
    let w, host = show view
    Assert.Equal("Person 1", shown host)
    clickOn w (boxOf host)
    Assert.True(isOpen host)
    Assert.Empty clicks

[<Fact>]
let ``its node is a ComboBox named by the selected item, collapsed or expanded, and Expand and Collapse work`` () =
    let lines (s: string) = s.Replace("\r", "").TrimEnd('\n').Split('\n') |> List.ofArray
    let w, host = show (ComboBox.create [ ComboBox.source [ "Red"; "Green" ]; ComboBox.selected (ValueSome "Green") ])
    Assert.Equal<string list>([ "ComboBox \"Green\" collapsed" ], lines (host.SemanticTree().ToString()))
    let node = host.SemanticTree().Children[0]
    Assert.Contains(SemanticAction.Expand, node.Actions)
    Assert.True(node.Perform SemanticAction.Expand)
    settle w
    Assert.True(isOpen host)
    let text = host.SemanticTree().ToString()
    Assert.Contains("ComboBox \"Green\" expanded", text)
    Assert.Contains("ListItem \"Red\"", text)
    Assert.True((host.SemanticTree().Children[0]).Perform SemanticAction.Collapse)
    settle w
    Assert.False(isOpen host)

[<Fact>]
let ``a disabled ComboBox doesn't open or change`` () =
    let changes = Changes()
    let w, host = show (combo 5 [ ComboBox.isEnabled false; changes.Handler ])
    clickOn w (boxOf host)
    Assert.False(isOpen host)
    Assert.Empty changes.Keys
