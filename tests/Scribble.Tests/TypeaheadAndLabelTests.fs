/// M9 step 9e on the fake platform (DESIGN.md, "9e: what waited for M9"): typeahead in ListBox and ComboBox, an
/// item's text, a list taking letters without asking for a keyboard, `TextBox.submitOnEnter`, the Label element,
/// and what a TextBox tells a screen reader.
module Scribble.Tests.TypeaheadAndLabelTests

open System
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
open Scribble.Tests.AppTests

let private ms (n: float) = TimeSpan.FromMilliseconds n

// ---- The rule ----

[<Fact>]
let ``typed letters go to the next item that starts with them`` () =
    let items = [| "Alice"; "Bob"; "Carol"; "Charlie"; "Chloe"; "Dave"; "New Jersey"; "New York" |]
    let find (s: TypeaheadState) (at: float) (typed: string) (current: int) = Typeahead.find s (ms at) typed items.Length current (fun i -> items[i])
    let s = TypeaheadState()
    // One letter: the next item starting with it after the current one, round to the first.
    Assert.Equal(ValueSome 2, find s 0.0 "c" -1)
    Assert.Equal(ValueSome 3, find (TypeaheadState()) 0.0 "c" 2)
    Assert.Equal(ValueSome 2, find (TypeaheadState()) 0.0 "C" 4)
    // More letters soon after: they build up, and the current item stays while it still matches.
    Assert.Equal(ValueSome 3, find s 100.0 "h" 2)
    Assert.Equal(ValueSome 4, find s 200.0 "l" 3)
    Assert.Equal("chl", s.Text)
    // Nothing starts with it: nowhere to go, and the letters stay.
    Assert.Equal(ValueNone, find s 300.0 "z" 4)
    // After a pause: a new search.
    Assert.Equal(ValueSome 5, find s 1400.0 "d" 4)
    Assert.Equal("d", s.Text)
    // The same letter again steps through the items starting with it.
    let c = TypeaheadState()
    Assert.Equal(ValueSome 2, find c 0.0 "c" 0)
    Assert.Equal(ValueSome 3, find c 100.0 "c" 2)
    Assert.Equal(ValueSome 4, find c 200.0 "c" 3)
    Assert.Equal(ValueSome 2, find c 300.0 "c" 4)
    // A space among the letters.
    let n = TypeaheadState()
    Assert.Equal(ValueSome 6, find n 0.0 "n" 0)
    for i, letter in List.indexed [ "e"; "w"; " " ] do
        Assert.Equal(ValueSome 6, find n (100.0 * float (i + 1)) letter 6)
    Assert.Equal(ValueSome 7, find n 500.0 "y" 6)
    Assert.True(Typeahead.isActive n (ms 1400.0))
    Assert.False(Typeahead.isActive n (ms 1600.0))

// ---- Lists ----

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
    let platform, w, _, app = startAll [ App.view view ]
    let host = app :?> ElementHost
    settle w
    platform, w, host

let private ofType (host: ElementHost) (t: ElementType) = all host.Tree.Root.Value |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, t))

let private keyWith (w: FakeWindow) (mods: Modifiers) (k: Key) =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    settle w
    settle w

let private key (w: FakeWindow) (k: Key) = keyWith w Modifiers.None k

/// Types as a keyboard does, with no time between the keys.
let private typeText (w: FakeWindow) (text: string) =
    for c in text do
        let k = if c = ' ' then Key.Space else Key.Char(Char.ToLowerInvariant c)
        w.Send(KeyDown(k, Modifiers.None, false))
        w.Send(TextInput(string c))
        w.Send(KeyUp(k, Modifiers.None))
    settle w
    settle w

let private names = [| "Alice"; "Bob"; "Carol"; "Charlie"; "Chloe"; "Dave"; "New Jersey"; "New York" |]

let private listOf (items: string[]) (settings: Setting<ListBox> list) =
    ListBox.create [ yield ListBox.source items; yield ListBox.viewSelector (fun (s: string) -> s); yield! settings ]

let private selectedOf (host: ElementHost) = (ofType host ListBox.Type).Get ListBoxProps.selected |> List.map (fun k -> string k)

/// The text of the item that has the focus.
let private focusedItem (host: ElementHost) =
    match host.Focused with
    | ValueSome e when obj.ReferenceEquals(e.Type, ListBoxItem.Type) -> string (e.Get ListBoxItemProps.key)
    | _ -> ""

[<Fact>]
let ``typing in a ListBox selects the item that starts with it, building up until a pause`` () =
    let _, w, host = show (listOf names [])
    key w Key.Tab
    typeText w "c"
    Assert.Equal<string list>([ "Carol" ], selectedOf host)
    Assert.Equal("Carol", focusedItem host)
    typeText w "h"
    Assert.Equal<string list>([ "Charlie" ], selectedOf host)
    typeText w "l"
    Assert.Equal<string list>([ "Chloe" ], selectedOf host)
    // After a pause the letters start again: "d", not "chld".
    w.Time.AdvanceMs 1500.0
    typeText w "d"
    Assert.Equal<string list>([ "Dave" ], selectedOf host)
    // The same letter again steps on, and round.
    w.Time.AdvanceMs 1500.0
    typeText w "n"
    Assert.Equal<string list>([ "New Jersey" ], selectedOf host)
    typeText w "n"
    Assert.Equal<string list>([ "New York" ], selectedOf host)
    typeText w "n"
    Assert.Equal<string list>([ "New Jersey" ], selectedOf host)
    // A space while letters build up is one of them.
    w.Time.AdvanceMs 1500.0
    typeText w "new y"
    Assert.Equal<string list>([ "New York" ], selectedOf host)
    Assert.Equal("New York", focusedItem host)
    // Nothing starts with it: the selection stays.
    w.Time.AdvanceMs 1500.0
    typeText w "zz"
    Assert.Equal<string list>([ "New York" ], selectedOf host)

[<Fact>]
let ``Space on its own still selects, and with several selections typing only moves the focus`` () =
    let _, w, host = show (listOf names [ ListBox.selectionMode SelectionMode.Multiple ])
    key w Key.Tab
    typeText w "b"
    Assert.Equal("Bob", focusedItem host)
    Assert.Empty(selectedOf host)
    // Long after the letter: Space is the key that selects.
    w.Time.AdvanceMs 1500.0
    typeText w " "
    Assert.Equal<string list>([ "Bob" ], selectedOf host)
    w.Time.AdvanceMs 1500.0
    typeText w "d"
    Assert.Equal("Dave", focusedItem host)
    Assert.Equal<string list>([ "Bob" ], selectedOf host)

type private Person = { Id: int; Name: string }

[<Fact>]
let ``items drawn by views are found by textSelector, which is also their name for a screen reader`` () =
    let people = names |> Array.mapi (fun i n -> { Id = i; Name = n })
    let view (withText: bool) =
        ListBox.create [
            yield ListBox.source people
            yield ListBox.keySelector (fun (p: Person) -> p.Id)
            // A view with no text in it.
            yield ListBox.viewSelector (fun (p: Person) -> Border.create [ Border.height 20.0 ])
            if withText then yield ListBox.textSelector (fun (p: Person) -> p.Name)
        ]
    let _, w, host = show (view true)
    key w Key.Tab
    typeText w "da"
    Assert.Equal<obj list>([ box 5 ], (ofType host ListBox.Type).Get ListBoxProps.selected)
    Assert.Contains("ListItem \"Dave\" selected", host.SemanticTree().ToString())
    Assert.Contains("ListItem \"Alice\"", host.SemanticTree().ToString())
    // Without it a record has no text: typing finds nothing.
    let _, w, host = show (view false)
    key w Key.Tab
    typeText w "da"
    Assert.Empty((ofType host ListBox.Type).Get ListBoxProps.selected)

[<Fact>]
let ``typing reaches item 9,000 of 10,000 without realizing those between`` () =
    let items = Array.init 10_000 (fun i -> "Item " + i.ToString "0000")
    let _, w, host = show (ListBox.create [ ListBox.height 240.0; ListBox.source items ])
    key w Key.Tab
    typeText w "item 9000"
    Assert.Equal<string list>([ "Item 9000" ], selectedOf host)
    Assert.Equal("Item 9000", focusedItem host)
    match ItemsView.realizedCount (ofType host ListBoxItems.Type) with
    | ValueSome(struct (realized, count)) ->
        Assert.Equal(10_000, count)
        Assert.InRange(realized, 1, 60)
    | ValueNone -> failwith "not an ItemsView"

[<Fact>]
let ``a list takes letters without asking for a keyboard; a TextBox asks for one`` () =
    let view =
        StackPanel.create [
            StackPanel.children [ TextBox.create [ TextBox.name "box" ]; listOf names [] ]
        ]
    let _, w, _ = show view
    Assert.Equal(ValueNone, w.TextInput)
    key w Key.Tab
    Assert.Equal(KeyboardKind.Text, w.TextInput.Value.Keyboard)
    key w Key.Tab
    // The list: typed text is wanted (a desktop sends it), but no on-screen keyboard and nothing composed.
    Assert.Equal(KeyboardKind.None, w.TextInput.Value.Keyboard)

// ---- ComboBox ----

let private comboOf (host: ElementHost) = ofType host ComboBox.Type
let private pickedOf (host: ElementHost) = match (comboOf host).Get ComboBoxProps.selected with ValueSome k -> string k | ValueNone -> ""

[<Fact>]
let ``typing in a closed ComboBox picks the matching item; open, it moves through the list`` () =
    let picked = ResizeArray<string>()
    let view =
        StackPanel.create [
            StackPanel.children [
                ComboBox.create [
                    ComboBox.source names
                    ComboBox.viewSelector (fun (s: string) -> s)
                    ComboBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<string, string>) -> picked.Add(e.Key |> ValueOption.defaultValue ""))
                ]
            ]
        ]
    let _, w, host = show view
    key w Key.Tab
    Assert.True((comboOf host).IsFocused)
    typeText w "ch"
    Assert.Equal("Charlie", pickedOf host)
    w.Time.AdvanceMs 1500.0
    typeText w "new y"
    Assert.Equal("New York", pickedOf host)
    // It stayed closed: the space was a letter, not the key that opens it.
    Assert.False((comboOf host).Get ComboBoxOpenProps.isOpen)
    Assert.Equal<string list>([ "Carol"; "Charlie"; "New Jersey"; "New York" ], List.ofSeq picked)
    // Open (F4): typing moves the focus in the list, and Enter picks what it is on.
    w.Time.AdvanceMs 1500.0
    key w (Key.F 4)
    Assert.True((comboOf host).Get ComboBoxOpenProps.isOpen)
    typeText w "bo"
    Assert.Equal("New York", pickedOf host)
    key w Key.Enter
    Assert.Equal("Bob", pickedOf host)
    Assert.False((comboOf host).Get ComboBoxOpenProps.isOpen)

// ---- submitOnEnter ----

let private boxOf (host: ElementHost) = ofType host TextBox.Type
let private textOf (host: ElementHost) = (TextBox.editOf (boxOf host)).Text

[<Fact>]
let ``with submitOnEnter, Enter submits a multi-line box and Shift+Enter makes the new line`` () =
    let submitted = ResizeArray<string>()
    let page (settings: Setting<TextBox> list) =
        StackPanel.create [
            StackPanel.children [ TextBox.create ([ TextBox.multiline true; TextBox.onSubmitted submitted.Add ] @ settings) ]
        ]
    let _, w, host = show (page [ TextBox.submitOnEnter true ])
    key w Key.Tab
    typeText w "one"
    keyWith w Modifiers.Shift Key.Enter
    typeText w "two"
    Assert.Equal("one\ntwo", textOf host)
    Assert.Empty submitted
    key w Key.Enter
    Assert.Equal<string list>([ "one\ntwo" ], List.ofSeq submitted)
    // The box keeps its text: clearing it is the app's (decision 61).
    Assert.Equal("one\ntwo", textOf host)
    keyWith w Modifiers.Control Key.Enter
    Assert.Equal(2, submitted.Count)
    // Without it: Enter is a new line and only Ctrl+Enter submits.
    submitted.Clear()
    let _, w, host = show (page [])
    key w Key.Tab
    typeText w "a"
    key w Key.Enter
    Assert.Equal("a\n", textOf host)
    Assert.Empty submitted
    // One line: the setting changes nothing.
    let _, w, host = show (StackPanel.create [ StackPanel.children [ TextBox.create [ TextBox.submitOnEnter true; TextBox.onSubmitted submitted.Add ] ] ])
    key w Key.Tab
    typeText w "x"
    keyWith w Modifiers.Shift Key.Enter
    Assert.Equal("x", textOf host)

// ---- Labels ----

let private named (host: ElementHost) (name: string) = all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = name)

[<Fact>]
let ``a Label names the control it is for, and a click on it focuses that control`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                Label.create [ Label.name "l1"; Label.text "Email"; Label.target "email" ]
                TextBox.create [ TextBox.name "email"; TextBox.placeholder "you@example.com" ]
                Label.create [ Label.text "Phone"; Label.target "phone" ]
                // Its own label wins over the Label's.
                TextBox.create [ TextBox.name "phone"; Semantics.label "Telephone" ]
                Label.create [ Label.name "l3"; Label.text "Nothing"; Label.target "missing" ]
                Label.create [ Label.text "Remember"; Label.target "remember" ]
                Border.create [ Border.content (Switch.create [ Switch.name "remember" ]) ]
            ]
        ]
    let _, w, host = show view
    let tree = host.SemanticTree().ToString()
    // The label's text, over the placeholder.
    Assert.Contains("TextBox \"Email\"", tree)
    Assert.Contains("TextBox \"Telephone\"", tree)
    Assert.Contains("Switch \"Remember\"", tree)
    // The label is still text a reader can reach.
    Assert.Contains("Text \"Email\"", tree)
    let click (e: Element) =
        let p = e.PositionInTree
        w.Time.AdvanceMs 1000.0
        w.Send(PointerPressed(Mouse, 0, { X = p.X + 3.0; Y = p.Y + 3.0 }, MouseButton.Left, Modifiers.None))
        w.Send(PointerReleased(Mouse, 0, { X = p.X + 3.0; Y = p.Y + 3.0 }, MouseButton.Left, Modifiers.None))
        settle w
    click (named host "l1")
    Assert.True((named host "email").IsFocused)
    // A label for nothing focuses nothing (the press, as on any text, takes the focus away).
    click (named host "l3")
    Assert.Equal(ValueNone, host.Focused)

[<Fact>]
let ``a Label finds the nearest control with its target's name`` () =
    let row (n: int) =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.children [
                Label.create [ Label.name ("label" + string n); Label.text ("Amount " + string n); Label.target "amount" ]
                Border.create [ Border.content (TextBox.create [ TextBox.name "amount"; TextBox.text (string n) ]) ]
            ]
        ]
    let _, _, host = show (StackPanel.create [ StackPanel.children [ row 1; row 2; row 3 ] ])
    let boxes = all host.Tree.Root.Value |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, TextBox.Type)) |> List.ofSeq
    for n in 1 .. 3 do
        let target = Label.findTarget (named host ("label" + string n))
        Assert.True(obj.ReferenceEquals(target.Value, boxes[n - 1]))
    let tree = host.SemanticTree().ToString()
    for n in 1 .. 3 do
        Assert.Contains("TextBox \"Amount " + string n + "\" value \"" + string n + "\"", tree)

[<Fact>]
let ``a TextBox tells a screen reader its text, that it is read-only, and nothing of a password`` () =
    let view =
        StackPanel.create [
            StackPanel.children [
                TextBox.create [ TextBox.placeholder "Name"; TextBox.text "Ada" ]
                TextBox.create [ TextBox.placeholder "Fixed"; TextBox.text "can't change"; TextBox.isReadOnly true ]
                TextBox.create [ TextBox.placeholder "Secret"; TextBox.text "hunter2"; TextBox.isPassword true ]
                TextBox.create [ TextBox.placeholder "Notes"; TextBox.multiline true; TextBox.text "one\ntwo" ]
            ]
        ]
    let _, _, host = show view
    let tree = host.SemanticTree()
    let text = tree.ToString()
    Assert.Contains("TextBox \"Name\" value \"Ada\"", text)
    Assert.Contains("TextBox \"Fixed\" value \"can't change\" read-only", text)
    Assert.Contains("TextBox \"Secret\" password", text)
    Assert.DoesNotContain("hunter2", text)
    Assert.Contains("TextBox \"Notes\" value \"one\\ntwo\"", text)
    let secret = (tree.Find "TextBox \"Secret\"").Value
    Assert.Equal(ValueNone, secret.Value)
    Assert.True secret.IsProtected
    let name = (tree.Find "TextBox \"Name\"").Value
    Assert.Equal(ValueSome "Ada", name.Value)
    Assert.False name.IsReadOnly
