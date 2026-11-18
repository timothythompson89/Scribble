/// M8 step 4b: RadioGroup and RadioButton on the fake platform: choosing by click and keys, controlled
/// and not, one Tab stop, disabled options skipped, nested radios, semantics.
module Scribble.Tests.RadioButtonTests

open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Accessibility
open Scribble.Tests.AppTests

type Fruit =
    | Apple
    | Pear
    | Plum

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let private radios (host: ElementHost) =
    all host.Tree.Root.Value |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, RadioButton.Type)) |> List.ofSeq

let private radio host (v: Fruit) = radios host |> List.find (fun r -> obj.Equals(r.Get RadioButtonProps.value, box v))
let private checkedOnes host = radios host |> List.filter (fun r -> r.Get ToggleButtonProps.isChecked) |> List.map (fun r -> unbox<Fruit> (r.Get RadioButtonProps.value))

let private click (w: FakeWindow) (e: Element) =
    let p = e.PositionInTree
    let at: PixelPoint = { X = p.X + 9.0; Y = p.Y + e.Bounds.Height / 2.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.DeliverFrame() |> ignore

let private key (w: FakeWindow) mods k =
    w.Send(KeyDown(k, mods, false))
    w.Send(KeyUp(k, mods))
    w.DeliverFrame() |> ignore

/// Apple, Pear (disabled when `pearOff`), and Plum inside a Border (radios anywhere below count).
let private group (pearOff: bool) (extra: Setting<RadioGroup> list) =
    RadioGroup.create [
        yield! extra
        RadioGroup.children [
            RadioButton.create [ RadioButton.value Apple; RadioButton.text "Apple" ]
            RadioButton.create [ RadioButton.value Pear; RadioButton.text "Pear"; RadioButton.isEnabled (not pearOff) ]
            Border.create [ Border.content (RadioButton.create [ RadioButton.value Plum; RadioButton.text "Plum" ]) ]
        ]
    ]

let private show (view: View) =
    let w, _, app = start [ App.view view ]
    w.DeliverFrame() |> ignore
    w, (app :?> ElementHost)

[<Fact>]
let ``a click chooses one option; clicking it again keeps it`` () =
    let reports = List<Fruit>()
    let w, host = show (group false [ RadioGroup.onSelectedChanged (fun (f: Fruit) -> reports.Add f) ])
    Assert.Empty(checkedOnes host)
    click w (radio host Pear)
    Assert.Equal<Fruit list>([ Pear ], checkedOnes host)
    click w (radio host Plum)
    click w (radio host Plum)
    Assert.Equal<Fruit list>([ Plum ], checkedOnes host)
    Assert.Equal<Fruit list>([ Pear; Plum ], List.ofSeq reports)

[<Fact>]
let ``a controlled group shows the model's choice`` () =
    for follow in [ true; false ] do
        let w, _, app =
            start [
                App.program Apple (fun (f: Fruit) model -> if follow then f else model) (fun model dispatch ->
                    group false [ RadioGroup.selected model; RadioGroup.onSelectedChanged dispatch ])
            ]
        w.DeliverFrame() |> ignore
        let host = app :?> ElementHost
        Assert.Equal<Fruit list>([ Apple ], checkedOnes host)
        click w (radio host Plum)
        w.DeliverFrame() |> ignore
        Assert.Equal<Fruit list>((if follow then [ Plum ] else [ Apple ]), checkedOnes host)

[<Fact>]
let ``one Tab stop; the arrow keys choose, wrap and skip a disabled option`` () =
    let w, host = show (group true [ RadioGroup.selected Apple ])
    key w Modifiers.None Key.Tab
    Assert.True((radio host Apple).IsFocused)
    key w Modifiers.None Key.Down
    // Pear is disabled: straight to Plum (but controlled at Apple: the view decides, so Apple stays checked).
    Assert.True((radio host Plum).IsFocused)
    let w, host = show (StackPanel.create [ StackPanel.children [ group true []; Button.create [ Button.text "After" ] ] ])
    key w Modifiers.None Key.Tab
    Assert.True((radio host Apple).IsFocused)
    key w Modifiers.None Key.Right
    Assert.Equal<Fruit list>([ Plum ], checkedOnes host)
    key w Modifiers.None Key.Down
    Assert.Equal<Fruit list>([ Apple ], checkedOnes host)
    key w Modifiers.None Key.Up
    Assert.Equal<Fruit list>([ Plum ], checkedOnes host)
    // Tab leaves the group in one step (one stop), and comes back to the chosen option.
    key w Modifiers.None Key.Tab
    Assert.False((radio host Plum).IsFocused || (radio host Apple).IsFocused)
    key w Modifiers.Shift Key.Tab
    Assert.True((radio host Plum).IsFocused)

[<Fact>]
let ``Space chooses the focused option`` () =
    let w, host = show (group false [])
    key w Modifiers.None Key.Tab
    key w Modifiers.None Key.Space
    Assert.Equal<Fruit list>([ Apple ], checkedOnes host)

[<Fact>]
let ``to a screen reader: a radio group of radio buttons, the chosen one checked`` () =
    let _, host = show (group false [ RadioGroup.selected Pear ])
    let g = host.SemanticTree().Children[0]
    Assert.Equal(Role.RadioGroup, g.Role)
    Assert.Equal("RadioButton \"Pear\" checked", g.Children[1].Describe())
    Assert.Equal("RadioButton \"Apple\" unchecked", g.Children[0].Describe())
