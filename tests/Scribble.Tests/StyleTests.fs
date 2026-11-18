/// M7 step 1: styles (selectors, classes, states, scopes, precedence, re-matching).
module Scribble.Tests.StyleTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping

let private red = Color.Hex 0xFF0000
let private green = Color.Hex 0x00FF00
let private blue = Color.Hex 0x0000FF

let private primary = "primary"
let private card = "card"

let private tree (styles: Style list) =
    let t = ElementTree(textSystem ())
    t.Styles <- styles
    t

let private layout (t: ElementTree) = t.UpdateLayout(Size.Create(400.0, 300.0))

let rec private find (name: string) (e: Element) : Element =
    if e.Name = name then e
    else e.Children |> Seq.pick (fun c -> if c.Name = name then Some c else (try Some(find name c) with _ -> None))

let private show (t: ElementTree) (view: View) =
    t.Update view
    layout t
    t.Root.Value

let private background (e: Element) = e.Get BorderProps.background

[<Fact>]
let ``a type style applies to that type only, not to types built on it`` () =
    let t = tree [ Border.style [] [ Border.background red ] ]
    let root = show t (StackPanel.create [ StackPanel.children [ Border.create [ Border.name "b" ]; Button.create [ Button.name "x" ] ] ])
    Assert.Equal(red, background (find "b" root))
    Assert.Equal(Color.Hex 0x3A3A55, background (find "x" root)) // Button's own default

[<Fact>]
let ``classes select, and changing them re-matches`` () =
    let t = tree [ Border.style [ Style.cls primary ] [ Border.background red ] ]
    let root = show t (Border.create [ Border.name "b" ])
    Assert.Equal(Color.Transparent, background root)
    show t (Border.create [ Border.name "b"; Border.classes [ primary ] ]) |> ignore
    Assert.Equal(red, background root)
    show t (Border.create [ Border.name "b" ]) |> ignore
    Assert.Equal(Color.Transparent, background root)

[<Fact>]
let ``the view's own setting wins, and dropping it shows the style again`` () =
    let t = tree [ Border.style [] [ Border.background red ] ]
    let root = show t (Border.create [ Border.background green ])
    Assert.Equal(green, background root)
    show t (Border.create []) |> ignore
    Assert.Equal(red, background root)
    // Equal to the style's value, it's still the element's own (and stays when the style goes).
    show t (Border.create [ Border.background red ]) |> ignore
    t.Styles <- []
    layout t
    Assert.Equal(red, background root)

[<Fact>]
let ``later styles beat earlier ones, nearer scopes beat outer ones, inline blocks beat both`` () =
    let app = [ Border.style [] [ Border.background red ]; Border.style [] [ Border.background green ] ]
    let t = tree app
    let local = [ Border.style [ Style.cls card ] [ Border.background blue ] ]
    let root =
        show t (
            StackPanel.create [
                StackPanel.styles local
                StackPanel.children [
                    Border.create [ Border.name "plain" ]
                    Border.create [ Border.name "card"; Border.classes [ card ] ]
                    Border.create [ Border.name "inline"; Border.classes [ card ]; Border.on States.disabled [ Border.background red ]; Border.isEnabled false ]
                ]
            ]
        )
    Assert.Equal(green, background (find "plain" root)) // the later app style
    Assert.Equal(blue, background (find "card" root)) // the nearer scope
    Assert.Equal(red, background (find "inline" root)) // its own inline block

[<Fact>]
let ``a hover block applies while the pointer is over the element`` () =
    let t = tree [ Border.style [ Style.cls primary ] [ Border.background red; Style.on States.hover [ Border.background green ] ] ]
    let root = show t (Border.create [ Border.classes [ primary ] ])
    Assert.Equal(red, background root)
    root.ChangePointersOver 1
    layout t
    Assert.Equal(green, background root)
    root.ChangePointersOver -1
    layout t
    Assert.Equal(red, background root)

[<Fact>]
let ``blocks nest, so states combine`` () =
    let t = tree [ Button.style [] [ Style.on States.focused [ Button.background red; Style.on States.hover [ Button.background green ] ] ] ]
    let root = show t (Button.create [])
    root.ChangePointersOver 1
    layout t
    Assert.Equal(Color.Hex 0x3A3A55, background root) // hovered, not focused
    root.SetFocusState(true, true)
    layout t
    Assert.Equal(green, background root) // both
    root.ChangePointersOver -1
    layout t
    Assert.Equal(red, background root) // focused only

[<Fact>]
let ``inside and parent match on ancestors`` () =
    let t =
        tree [
            TextBlock.style [ Style.inside Border.Type [ Style.cls card ] ] [ TextBlock.foreground red ]
            TextBlock.style [ Style.parent StackPanel.Type [] ] [ TextBlock.fontSize 20.0 ]
        ]
    let root =
        show t (
            StackPanel.create [
                StackPanel.children [
                    TextBlock.create [ TextBlock.name "top" ]
                    Border.create [
                        Border.classes [ card ]
                        Border.content (Border.create [ Border.content (TextBlock.create [ TextBlock.name "deep" ]) ])
                    ]
                ]
            ]
        )
    let top, deep = find "top" root, find "deep" root
    Assert.Equal(Color.Black, top.Get TextProps.foreground)
    Assert.Equal(red, deep.Get TextProps.foreground)
    Assert.Equal(20.0, top.Get TextProps.fontSize)
    Assert.Equal(14.0, deep.Get TextProps.fontSize)

[<Fact>]
let ``a state on an ancestor re-matches what's inside`` () =
    let t = tree [ TextBlock.style [ Style.inside Button.Type [ States.hover ] ] [ TextBlock.foreground red ] ]
    let root = show t (Button.create [ Button.content (TextBlock.create [ TextBlock.name "label" ]) ])
    let label = find "label" root
    // Inherited from the Button's own text color.
    Assert.Equal(Color.Hex 0xE6E6F0, label.Get TextProps.foreground)
    root.ChangePointersOver 1
    layout t
    Assert.Equal(red, label.Get TextProps.foreground)
    root.ChangePointersOver -1
    layout t
    Assert.Equal(Color.Hex 0xE6E6F0, label.Get TextProps.foreground)

[<Fact>]
let ``disabled follows isEnabled on an ancestor`` () =
    let t = tree [ Button.style [ States.disabled ] [ Button.background red ] ]
    let view enabled = StackPanel.create [ StackPanel.isEnabled enabled; StackPanel.children [ Button.create [ Button.name "x" ] ] ]
    let root = show t (view true)
    Assert.Equal(Color.Hex 0x3A3A55, background (find "x" root))
    show t (view false) |> ignore
    Assert.Equal(red, background (find "x" root))

[<Fact>]
let ``a style that changes size re-lays out`` () =
    let big = "big"
    let t = tree [ Border.style [ Style.cls big ] [ Border.padding 20.0 ] ]
    let root = show t (Border.create [ Border.horizontalAlignment HorizontalAlignment.Left; Border.content (Border.create [ Border.width 10.0; Border.height 10.0 ]) ])
    Assert.Equal(10.0, root.Bounds.Width)
    show t (Border.create [ Border.horizontalAlignment HorizontalAlignment.Left; Border.classes [ big ]; Border.content (Border.create [ Border.width 10.0; Border.height 10.0 ]) ]) |> ignore
    Assert.Equal(50.0, root.Bounds.Width)

[<Fact>]
let ``a styles setting covers its element and what's inside, not its siblings`` () =
    let local = [ Style.any [] [ Layoutable.margin 3.0 ] ]
    let t = tree []
    let root =
        show t (
            StackPanel.create [
                StackPanel.children [
                    Border.create [ Border.name "scoped"; Border.styles local; Border.content (TextBlock.create [ TextBlock.name "inner" ]) ]
                    Border.create [ Border.name "sibling" ]
                ]
            ]
        )
    Assert.Equal(Thickness.Uniform 3.0, (find "scoped" root).Get LayoutProps.margin)
    Assert.Equal(Thickness.Uniform 3.0, (find "inner" root).Get LayoutProps.margin)
    Assert.Equal(Thickness.Zero, (find "sibling" root).Get LayoutProps.margin)
    Assert.Equal(Thickness.Zero, root.Get LayoutProps.margin)

[<Fact>]
let ``inline blocks in a view work without any styles elsewhere`` () =
    let t = tree []
    let root = show t (Border.create [ Border.background red; Border.on States.hover [ Border.background green ] ])
    root.ChangePointersOver 1
    layout t
    Assert.Equal(red, background root) // the view's own background still wins over the block
    let root = show t (Border.create [ Border.on States.hover [ Border.background green ] ])
    Assert.Equal(green, background root)
    root.ChangePointersOver -1
    layout t
    Assert.Equal(Color.Transparent, background root)

[<Fact>]
let ``styles can't set children, classes or names`` () =
    Assert.Throws<ArgumentException>(fun () -> Border.style [] [ Border.content (Border.create []) ] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Border.style [] [ Border.classes [ card ] ] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Border.style [] [ Border.name "x" ] |> ignore) |> ignore

/// The message a style made with `make` refuses with.
let private refusal (make: unit -> Style) = (Assert.Throws<ArgumentException>(fun () -> make () |> ignore)).Message

[<Fact>]
let ``styles never set app state: handlers, controlled values, content, flyouts and tooltips`` () =
    // Handlers, found by their type: the built-in ones and an element author's own.
    let onPoke: Prop<(int -> unit) voption> = Prop.define "OnPoke" ValueNone Affects.Nothing
    for make in
        [ (fun () -> Button.style [] [ Button.onClick (fun _ -> ()) ])
          (fun () -> Slider.style [] [ Slider.onValueChanged ignore ])
          (fun () -> Border.style [] [ Setting.create onPoke (ValueSome ignore) ]) ] do
        Assert.Contains("handlers come from views", refusal make)
    // Controlled values, content, a flyout or a tooltip.
    for make in
        [ (fun () -> CheckBox.style [] [ CheckBox.isChecked true ])
          (fun () -> Slider.style [] [ Slider.value 5.0 ])
          (fun () -> TabControl.style [] [ TabControl.selected 1 ])
          (fun () -> Border.style [] [ Border.content (Border.create []) ])
          (fun () -> Button.style [] [ Button.flyout (Border.create []) ])
          (fun () -> Flyout.style [] [ Flyout.isOpen true ])
          (fun () -> Flyout.style [] [ Flyout.onOpenChanged ignore ])
          (fun () -> Button.style [] [ Button.tooltip "Hi" ]) ] do
        Assert.Contains("come from views", refusal make)
    Assert.Contains("content and app state", refusal (fun () -> CheckBox.style [] [ CheckBox.isChecked true ]))
    // A state block is a style, in a view too: checked when it's made.
    let inBlock = Assert.Throws<ArgumentException>(fun () -> Button.create [ Button.on States.hover [ Button.onClick (fun _ -> ()) ] ] |> ignore)
    Assert.Contains("handlers come from views", inBlock.Message)

[<Fact>]
let ``styles still set how things look and behave`` () =
    Flyout.style [ Style.parent Button.Type [] ] [ Flyout.placement Placement.Right; Flyout.lightDismiss false; Flyout.padding 0.0 ] |> ignore
    Slider.style [] [ Slider.step 5.0; Slider.isEnabled false ] |> ignore
    CheckBox.style [ Style.cls primary ] [ Style.on States.isChecked [ CheckBox.markColor red ] ] |> ignore

[<Fact>]
let ``Values says which style set a value`` () =
    let t = tree [ Button.style [ Style.cls primary ] [ Style.on States.hover [ Button.background red ] ] ]
    let root = show t (Button.create [ Button.classes [ primary ] ])
    root.ChangePointersOver 1
    layout t
    let v = root.Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, BorderProps.background))
    Assert.Equal(ValueSource.Style, v.Source)
    Assert.Equal("Button.primary:hover", v.Selector)
    let t2 = tree [ TextBlock.style [ Style.parent Border.Type [ Style.cls card ] ] [ TextBlock.fontSize 9.0 ] ]
    let r2 = show t2 (Border.create [ Border.classes [ card ]; Border.content (TextBlock.create []) ])
    let size = r2.Children[0].Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, TextProps.fontSize))
    Assert.Equal("Border.card > TextBlock", size.Selector)

[<Fact>]
let ``without styles, hovering queues nothing`` () =
    let t = tree []
    let root = show t (Border.create [])
    let mutable invalidated = 0
    t.OnInvalidated <- fun () -> invalidated <- invalidated + 1
    root.ChangePointersOver 1
    Assert.Equal(1, invalidated) // the visual invalidation only, no restyle request on top

// --- Property selectors -------------------------------------------------------------------------

let private isChecked = Prop.define "IsChecked" false Affects.Render
let private level = Prop.define "Level" 0 Affects.Render
let private checkedState = States.ofProp "checked" isChecked id
let private checkedOn (v: bool) : Setting<Border> = Setting.create isChecked v
let private levelOf (v: int) : Setting<Border> = Setting.create level v

[<Fact>]
let ``a property selector matches the value, and changing it re-matches`` () =
    let t = tree [ Border.style [ Style.prop isChecked true ] [ Border.background red ] ]
    let root = show t (Border.create [])
    Assert.Equal(Color.Transparent, background root)
    show t (Border.create [ checkedOn true ]) |> ignore
    Assert.Equal(red, background root)
    show t (Border.create []) |> ignore // dropped: back to the default, false
    Assert.Equal(Color.Transparent, background root)
    root.Set(isChecked, true) // from code too
    layout t
    Assert.Equal(red, background root)
    root.Reset isChecked
    layout t
    Assert.Equal(Color.Transparent, background root)

[<Fact>]
let ``a style setting the tested property doesn't change what matches`` () =
    let t =
        tree [
            Border.style [] [ checkedOn true ]
            Border.style [ Style.prop isChecked true ] [ Border.background red ]
        ]
    let root = show t (Border.create [])
    Assert.True(root.Get isChecked) // the style's value shows...
    Assert.Equal(Color.Transparent, background root) // ...but selectors only see the unstyled value

[<Fact>]
let ``where tests with a function`` () =
    let t = tree [ Border.style [ Style.where level (fun l -> l >= 2) ] [ Border.background red ] ]
    let root = show t (Border.create [ levelOf 1 ])
    Assert.Equal(Color.Transparent, background root)
    show t (Border.create [ levelOf 3 ]) |> ignore
    Assert.Equal(red, background root)

[<Fact>]
let ``a property on an ancestor re-matches what's inside`` () =
    let t = tree [ TextBlock.style [ Style.inside Border.Type [ Style.prop isChecked true ] ] [ TextBlock.foreground red ] ]
    let view c = Border.create [ checkedOn c; Border.content (StackPanel.create [ StackPanel.children [ TextBlock.create [ TextBlock.name "label" ] ] ]) ]
    let root = show t (view false)
    let label = find "label" root
    Assert.Equal(Color.Black, label.Get TextProps.foreground)
    show t (view true) |> ignore
    Assert.Equal(red, label.Get TextProps.foreground)
    show t (view false) |> ignore
    Assert.Equal(Color.Black, label.Get TextProps.foreground)

[<Fact>]
let ``a state from a property works in styles and inline blocks`` () =
    let t = tree [ Border.style [] [ Style.on checkedState [ Border.background red ] ] ]
    let root = show t (Border.create [ checkedOn true ])
    Assert.Equal(red, background root)
    show t (Border.create [ checkedOn false ]) |> ignore
    Assert.Equal(Color.Transparent, background root)
    let t2 = tree []
    let r2 = show t2 (Border.create [ Border.on checkedState [ Border.background green ] ])
    Assert.Equal(Color.Transparent, background r2)
    show t2 (Border.create [ checkedOn true; Border.on checkedState [ Border.background green ] ]) |> ignore
    Assert.Equal(green, background r2)

[<Fact>]
let ``property selectors read as text for the inspector`` () =
    Assert.Equal("Border[IsChecked=true]", (Border.style [ Style.prop isChecked true ] [ Border.background red ]).Description)
    Assert.Equal("Button.primary[Level=2]:hover", (Button.style [ Style.cls primary; Style.prop level 2; States.hover ] [ Button.background red ]).Description)
    Assert.Equal("Border[Level?]", (Border.style [ Style.where level (fun l -> l > 0) ] [ Border.background red ]).Description)
    Assert.Equal("Border:checked", (Border.style [ checkedState ] [ Border.background red ]).Description)

[<Fact>]
let ``classes are names: a name built at runtime matches, and case counts`` () =
    let t = tree [ Border.style [ Style.cls "primary" ] [ Border.background red ] ]
    let root = show t (Border.create [ Border.classes [ String('p', 1) + "rimary" ] ])
    Assert.Equal(red, background root)
    show t (Border.create [ Border.classes [ "Primary" ] ]) |> ignore
    Assert.Equal(Color.Transparent, background root)

[<Fact>]
let ``a state is a selector part, built-in or custom`` () =
    let t = tree [ Border.style [ checkedState; States.hover ] [ Border.background red ] ]
    let root = show t (Border.create [ checkedOn true ])
    Assert.Equal(Color.Transparent, background root)
    root.ChangePointersOver 1
    layout t
    Assert.Equal(red, background root)
    show t (Border.create []) |> ignore
    Assert.Equal(Color.Transparent, background root)

[<Fact>]
let ``ActiveStates lists built-in and custom states that are on, skipping tests that throw`` () =
    let broken = States.define "broken" (fun _ -> invalidOp "only for another type")
    let t = tree []
    let root = show t (Border.create [ checkedOn true ])
    root.ChangePointersOver 1
    let has (s: ElementState) = root.ActiveStates |> List.exists (fun x -> obj.ReferenceEquals(x, s))
    Assert.True(has States.hover)
    Assert.True(has checkedState)
    Assert.False(has States.disabled)
    Assert.False(has broken)
    show t (Border.create []) |> ignore
    Assert.False(has checkedState)
