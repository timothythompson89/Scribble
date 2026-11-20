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
                    Border.create [ Border.name "inline"; Border.classes [ card ]; Border.onState States.disabled [ Border.background red ]; Border.isEnabled false ]
                ]
            ]
        )
    Assert.Equal(green, background (find "plain" root)) // the later app style
    Assert.Equal(blue, background (find "card" root)) // the nearer scope
    Assert.Equal(red, background (find "inline" root)) // its own inline block

[<Fact>]
let ``a hover block applies while the pointer is over the element`` () =
    let t = tree [ Border.style [ Style.cls primary ] [ Border.background red; Style.onState States.hover [ Border.background green ] ] ]
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
    let t = tree [ Button.style [] [ Style.onState States.focused [ Button.background red; Style.onState States.hover [ Button.background green ] ] ] ]
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
    let root = show t (Border.create [ Border.background red; Border.onState States.hover [ Border.background green ] ])
    root.ChangePointersOver 1
    layout t
    Assert.Equal(red, background root) // the view's own background still wins over the block
    let root = show t (Border.create [ Border.onState States.hover [ Border.background green ] ])
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
    let inBlock = Assert.Throws<ArgumentException>(fun () -> Button.create [ Button.onState States.hover [ Button.onClick (fun _ -> ()) ] ] |> ignore)
    Assert.Contains("handlers come from views", inBlock.Message)

[<Fact>]
let ``styles still set how things look and behave`` () =
    Flyout.style [ Style.parent Button.Type [] ] [ Flyout.placement Placement.Right; Flyout.lightDismiss false; Flyout.padding 0.0 ] |> ignore
    Slider.style [] [ Slider.step 5.0; Slider.isEnabled false ] |> ignore
    CheckBox.style [ Style.cls primary ] [ Style.onState States.isChecked [ CheckBox.markColor red ] ] |> ignore

[<Fact>]
let ``Values says which style set a value`` () =
    let t = tree [ Button.style [ Style.cls primary ] [ Style.onState States.hover [ Button.background red ] ] ]
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
let ``a property block matches the value, and changing it re-matches`` () =
    let t = tree [ Border.style [] [ Style.onProp isChecked true [ Border.background red ] ] ]
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
            Border.style [] [ Style.onProp isChecked true [ Border.background red ] ]
        ]
    let root = show t (Border.create [])
    Assert.True(root.Get isChecked) // the style's value shows...
    Assert.Equal(Color.Transparent, background root) // ...but selectors only see the unstyled value

[<Fact>]
let ``where tests an ancestor's property with a function, and re-matches what's inside`` () =
    let t = tree [ TextBlock.style [ Style.inside Border.Type [ Style.where level (fun l -> l >= 2) ] ] [ TextBlock.foreground red ] ]
    let view l = Border.create [ levelOf l; Border.content (TextBlock.create [ TextBlock.name "label" ]) ]
    let root = show t (view 1)
    let label = find "label" root
    Assert.Equal(Color.Black, label.Get TextProps.foreground)
    show t (view 3) |> ignore
    Assert.Equal(red, label.Get TextProps.foreground)
    show t (view 1) |> ignore
    Assert.Equal(Color.Black, label.Get TextProps.foreground)

[<Fact>]
let ``a property test is for an ancestor's part: in a style's own selector it throws, naming the block to use`` () =
    // One way to write each: a block for the element itself, a selector part for an ancestor, a state for a name.
    let own (make: unit -> Style) =
        let e = Assert.Throws<ArgumentException>(fun () -> make () |> ignore)
        Assert.Contains("Style.onProp", e.Message)
        Assert.Contains("Style.onWhere", e.Message)
        e.Message
    Assert.Contains("IsChecked", own (fun () -> Border.style [ Style.prop isChecked true ] [ Border.background red ]))
    Assert.Contains("Level", own (fun () -> Border.style [ Style.cls primary; Style.where level (fun l -> l > 0) ] [ Border.background red ]))
    own (fun () -> Style.any [ Style.prop isChecked true ] [ Layoutable.opacity 0.5 ]) |> ignore
    // In an ancestor's part, at any depth, both are fine.
    TextBlock.style [ Style.inside Border.Type [ Style.prop isChecked true ] ] [ TextBlock.foreground red ] |> ignore
    TextBlock.style [ Style.parent Border.Type [ Style.where level (fun l -> l > 0) ] ] [ TextBlock.foreground red ] |> ignore
    TextBlock.style [ Style.inside StackPanel.Type [ Style.inside Border.Type [ Style.prop level 2 ] ] ] [ TextBlock.foreground red ] |> ignore

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
    let t = tree [ Border.style [] [ Style.onState checkedState [ Border.background red ] ] ]
    let root = show t (Border.create [ checkedOn true ])
    Assert.Equal(red, background root)
    show t (Border.create [ checkedOn false ]) |> ignore
    Assert.Equal(Color.Transparent, background root)
    let t2 = tree []
    let r2 = show t2 (Border.create [ Border.onState checkedState [ Border.background green ] ])
    Assert.Equal(Color.Transparent, background r2)
    show t2 (Border.create [ checkedOn true; Border.onState checkedState [ Border.background green ] ]) |> ignore
    Assert.Equal(green, background r2)

[<Fact>]
let ``property selectors read as text for the inspector`` () =
    Assert.Equal("Border[IsChecked=true] TextBlock", (TextBlock.style [ Style.inside Border.Type [ Style.prop isChecked true ] ] [ TextBlock.foreground red ]).Description)
    Assert.Equal("Button.primary[Level=2]:hover > TextBlock", (TextBlock.style [ Style.parent Button.Type [ Style.cls primary; Style.prop level 2; States.hover ] ] [ TextBlock.foreground red ]).Description)
    Assert.Equal("Border[Level?] TextBlock", (TextBlock.style [ Style.inside Border.Type [ Style.where level (fun l -> l > 0) ] ] [ TextBlock.foreground red ]).Description)
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
    let broken = States.define "broken" [ States.dependsOn level; States.where (fun _ -> invalidOp "only for another type") ]
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

[<Fact>]
let ``onProp applies while the element's own value matches, and is matched again when it changes`` () =
    // DESIGN.md, "One way to say when": a style's selector says which elements; onState and onProp say when.
    let style =
        Slider.style [] [
            Slider.track red
            Style.onProp SliderProps.orientation Orientation.Vertical [
                Slider.track green
                Style.onState States.disabled [ Slider.track blue ]
            ]
            Style.onProp SliderProps.step 5.0 [ Slider.fill blue ]
        ]
    let t = tree [ style ]
    let view (settings: Setting<Slider> list) = Slider.create (Slider.name "s" :: settings)
    let track (root: Element) = (find "s" root).Get SliderProps.track
    // Lying down (the property's default): the plain setting.
    Assert.Equal(red, track (show t (view [])))
    // Upright: the block; with a state inside it, both must hold.
    let root = show t (view [ Slider.orientation Orientation.Vertical ])
    Assert.Equal(green, track root)
    Assert.Equal(blue, track (show t (view [ Slider.orientation Orientation.Vertical; Slider.isEnabled false ])))
    Assert.Equal(red, track (show t (view [ Slider.isEnabled false ])))
    // A style holds any number of them, each on its own property.
    let both = show t (view [ Slider.orientation Orientation.Vertical; Slider.step 5.0 ])
    Assert.Equal(green, track both)
    Assert.Equal(blue, (find "s" both).Get SliderProps.fill)
    // Tools read the rule as a selector with the test written out (simple values; a union's is left as "…").
    let value = (find "s" both).Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, SliderProps.fill))
    Assert.Equal("Slider[Step=5]", value.Selector)
    // Back to lying down: matched again.
    Assert.Equal(red, track (show t (view [])))

[<Fact>]
let ``onProp is a style: it can't set handlers or app state`` () =
    Assert.Throws<ArgumentException>(fun () ->
        Style.onProp SliderProps.orientation Orientation.Vertical [ Slider.value 3.0 ] |> ignore<Setting<Slider>>)
    |> ignore

// --- Style.onWhere (2026-11-19): onProp with a test where it has a value ---------------------------

/// Made once, as a style's test should be.
let private isHigh (v: float) = v >= 80.0
let private isHighTest: float -> bool = isHigh

[<Fact>]
let ``onWhere applies while the element's own value passes the test, and is matched again when it changes`` () =
    let style =
        Slider.style [] [
            Slider.fill red
            Style.onWhere SliderProps.value isHighTest [
                Slider.fill green
                Style.onState States.disabled [ Slider.fill blue ]
            ]
            // Beside an onProp block and another onWhere: a style holds any number.
            Style.onProp SliderProps.orientation Orientation.Vertical [ Slider.track blue ]
            Style.onWhere SliderProps.step (fun s -> s > 1.0) [ Slider.track green ]
        ]
    let t = tree [ style ]
    let view (settings: Setting<Slider> list) = Slider.create (Slider.name "s" :: settings)
    let fill (root: Element) = (find "s" root).Get SliderProps.fill
    Assert.Equal(red, fill (show t (view [ Slider.value 40.0 ])))
    Assert.Equal(green, fill (show t (view [ Slider.value 80.0 ])))
    // With a state inside it, both must hold.
    Assert.Equal(blue, fill (show t (view [ Slider.value 90.0; Slider.isEnabled false ])))
    Assert.Equal(red, fill (show t (view [ Slider.value 40.0; Slider.isEnabled false ])))
    // The other blocks, each on its own property.
    let both = show t (view [ Slider.value 90.0; Slider.step 5.0 ])
    Assert.Equal(green, fill both)
    Assert.Equal(green, (find "s" both).Get SliderProps.track)
    // Tools read the rule as a selector; a test can't be written out.
    let value = (find "s" both).Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, SliderProps.fill))
    Assert.Equal("Slider[Value?]", value.Selector)
    // Back under 80: matched again. No state was made for any of this.
    Assert.Equal(red, fill (show t (view [ Slider.value 79.0 ])))
    Assert.DoesNotContain((find "s" both).ApplicableStates, fun s -> s.Name = "high")

[<Fact>]
let ``onWhere reads the element's own value, never a style's`` () =
    // The first style gives every Slider a step of 5; the test is on the step, and no Slider has one of its own.
    let t =
        tree [ Slider.style [] [ Slider.step 5.0 ]
               Slider.style [] [ Style.onWhere SliderProps.step (fun s -> s = 5.0) [ Slider.fill red ] ] ]
    let root = show t (Slider.create [])
    Assert.Equal(5.0, root.Get SliderProps.step)
    Assert.NotEqual(red, root.Get SliderProps.fill)
    Assert.Equal(red, (show t (Slider.create [ Slider.step 5.0 ])).Get SliderProps.fill)

[<Fact>]
let ``onWhere is a style: it can't set handlers or app state`` () =
    Assert.Throws<ArgumentException>(fun () ->
        Style.onWhere SliderProps.value isHighTest [ Slider.value 3.0 ] |> ignore<Setting<Slider>>)
    |> ignore

[<Fact>]
let ``two onWhere blocks are the same only when their test is the same function`` () =
    let block (test: float -> bool) : Setting<Slider> = Style.onWhere SliderProps.value test [ Slider.fill red ]
    // A style list made once gives the same function each time: a rebuilt view's styles don't count as a change.
    Assert.Equal(box (block isHighTest).Value, box (block isHighTest).Value)
    // Two functions that happen to do the same aren't known to.
    Assert.NotEqual(box (block (fun v -> v >= 80.0)).Value, box (block (fun v -> v >= 80.0)).Value)
    // Nor is a test the same as a value on the same property.
    let byValue: Setting<Slider> = Style.onProp SliderProps.value 80.0 [ Slider.fill red ]
    Assert.NotEqual(box (block isHighTest).Value, box byValue.Value)
    Assert.NotEqual(box byValue.Value, box (block isHighTest).Value)

// --- States of your own (2026-11-19): States.define with settings, Border.state -------------------

let private limit = Prop.define "Limit" 3 Affects.Render
let private limitOf (v: int) : Setting<Border> = Setting.create limit v
/// Switched by the view.
let private chosen = States.define "chosen-in-style-tests" []
/// From two properties: the level has reached the limit.
let private full =
    States.define "full-in-style-tests" [
        States.dependsOn level
        States.dependsOn limit
        States.where (fun v -> v.Get level >= v.Get limit)
    ]

[<Fact>]
let ``a state the view switches: styles follow it, on the element and inside it`` () =
    let t =
        tree [ Border.style [ Style.cls card ] [ Style.onState chosen [ Border.background red ] ]
               Border.style [ Style.inside Border.Type [ Style.cls card; chosen ] ] [ Border.background green ] ]
    let view (on: bool voption) =
        Border.create [
            Border.classes [ card ]
            match on with
            | ValueSome v -> Border.state chosen v
            | ValueNone -> ()
            Border.content (Border.create [ Border.name "in" ])
        ]
    let root = show t (view (ValueSome false))
    let inner = find "in" root
    Assert.Equal(Color.Transparent, background root)
    Assert.Equal(Color.Transparent, background inner)
    show t (view (ValueSome true)) |> ignore
    Assert.Equal(red, background root)
    Assert.Equal(green, background inner)
    Assert.Contains(chosen, root.ActiveStates)
    // Dropped from the view: out of the state.
    show t (view ValueNone) |> ignore
    Assert.Equal(Color.Transparent, background root)
    Assert.Equal(Color.Transparent, background inner)

[<Fact>]
let ``only a state made without settings can be switched, and never by a style`` () =
    for s in [ States.hover; checkedState; full ] do
        let error = Assert.Throws<ArgumentException>(fun () -> Border.state s true |> ignore<Setting<Border>>)
        Assert.Contains(s.Name, error.Message)
    Assert.Throws<ArgumentException>(fun () -> Border.style [] [ Border.state chosen true ] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Border.style [] [ Style.onState States.hover [ Border.state chosen true ] ] |> ignore) |> ignore

[<Fact>]
let ``a state from several properties is matched again when any of them changes`` () =
    let t = tree [ Border.style [] [ Style.onState full [ Border.background red ] ] ]
    let root = show t (Border.create [ levelOf 1 ])
    Assert.Equal(Color.Transparent, background root)
    show t (Border.create [ levelOf 3 ]) |> ignore // the limit's default is 3
    Assert.Equal(red, background root)
    show t (Border.create [ levelOf 3; limitOf 10 ]) |> ignore
    Assert.Equal(Color.Transparent, background root)
    show t (Border.create [ levelOf 12; limitOf 10 ]) |> ignore
    Assert.Equal(red, background root)
    Assert.Contains(full, root.ActiveStates)

[<Fact>]
let ``a state's test reads the element's own values, not a style's`` () =
    // The style gives every Border a level past the limit: that mustn't put them in the state.
    let t = tree [ Border.style [] [ levelOf 50; Style.onState full [ Border.background red ] ] ]
    let root = show t (Border.create [])
    Assert.Equal(50, root.Get level)
    Assert.Equal(Color.Transparent, background root)
    Assert.DoesNotContain(full, root.ActiveStates)

[<Fact>]
let ``a state needs both what it depends on and a test, or neither`` () =
    let noProps = Assert.Throws<ArgumentException>(fun () -> States.define "no-props" [ States.where (fun _ -> true) ] |> ignore)
    Assert.Contains("States.dependsOn", noProps.Message)
    let noTest = Assert.Throws<ArgumentException>(fun () -> States.define "no-test" [ States.dependsOn level ] |> ignore)
    Assert.Contains("States.where", noTest.Message)

[<Fact>]
let ``a test that reads a property the state doesn't depend on warns in a Debug build`` () =
    let unlisted = Prop.define "UnlistedForStateTest" 0 Affects.Render
    let careless = States.define "careless" [ States.dependsOn level; States.where (fun v -> v.Get level > v.Get unlisted) ]
    let warnings = System.Collections.Generic.List<string>()
    let listener = Action<string>(fun m -> lock warnings (fun () -> warnings.Add m))
    // Added beside any other test's listener, and taken away again, so neither takes the other's.
    DebugWarnings.Listener <- Delegate.Combine(DebugWarnings.Listener, listener) :?> Action<string>
    // (A test that sets the listener outright may still drop this one: then there is nothing to check.)
    let mutable heard = true
    try
        let t = tree []
        let root = show t (Border.create [ levelOf 1 ])
        Assert.Contains(careless, root.ActiveStates)
        heard <- not (isNull DebugWarnings.Listener) && (DebugWarnings.Listener.GetInvocationList() |> Array.contains (listener :> Delegate))
    finally
        DebugWarnings.Listener <- Delegate.Remove(DebugWarnings.Listener, listener) :?> Action<string>
        States.remove careless
#if DEBUG
    if heard then Assert.Contains(List.ofSeq warnings, fun m -> m.Contains "careless" && m.Contains "UnlistedForStateTest")
#endif

/// Read from a property a control already has, as the Gallery's Styles page does.
let private high = States.ofProp "high-in-style-tests" SliderProps.value (fun v -> v >= 80.0)

[<Fact>]
let ``ApplicableStates: the built-in states, and the custom ones the view switches here or a style reaching it uses`` () =
    let unused = States.define "unused-in-style-tests" []
    try
        let t =
            tree [ Border.style [ Style.cls card ] [ Style.onState chosen [ Border.background red ] ]
                   Slider.style [ Style.cls card ] [ Style.onState high [ Slider.fill red ] ] ]
        t.Theme <- ValueSome(Theme.dark [])
        let root =
            show t (
                StackPanel.create [
                    StackPanel.children [
                        Border.create [ Border.name "plain" ]
                        Border.create [ Border.name "styled"; Border.classes [ card ] ]
                        Border.create [ Border.name "switched"; Border.state chosen false ]
                        Border.create [ Border.name "level"; levelOf 1 ]
                        CheckBox.create [ CheckBox.name "box" ]
                        Slider.create [ Slider.name "styled slider"; Slider.classes [ card ]; Slider.value 40.0 ]
                        Slider.create [ Slider.name "other slider"; Slider.value 90.0 ]
                    ]
                ])
        let has (name: string) (s: ElementState) = (find name root).ApplicableStates |> List.exists (fun x -> obj.ReferenceEquals(x, s))
        // The built-in ones: everywhere.
        for name in [ "plain"; "styled"; "box" ] do
            Assert.True(has name States.hover)
            Assert.True(has name States.disabled)
        // Nothing of a plain Border's.
        for s in [ chosen; full; States.isChecked; unused ] do
            Assert.False(has "plain" s)
        // A style reaching it gives the state a look; the view switches it here.
        Assert.True(has "styled" chosen)
        Assert.True(has "switched" chosen)
        // Having a property the state reads ties nothing: no style here uses "full".
        Assert.False(has "level" full)
        Assert.False(has "level" chosen)
        // A state read from a Slider's value is the styled slider's alone: the other is even in it (90), to no effect.
        Assert.True(has "styled slider" high)
        Assert.False(has "other slider" high)
        Assert.Contains(high, (find "other slider" root).ActiveStates)
        // The theme styles a checked CheckBox, so an unchecked one has the state; a Border doesn't.
        Assert.True(has "box" States.isChecked)
        Assert.False(has "box" chosen)
        // Forced, a state is the element's until it's let go.
        let plain = find "plain" root
        plain.ForceState(unused, true)
        Assert.True(has "plain" unused)
        plain.UnforceState unused
        Assert.False(has "plain" unused)
    finally
        States.remove unused

[<Fact>]
let ``a state read from a control's own value follows it`` () =
    let t = tree [ Slider.style [] [ Style.onState high [ Slider.fill red ] ] ]
    let root = show t (Slider.create [ Slider.value 40.0 ])
    Assert.NotEqual(red, root.Get SliderProps.fill)
    Assert.DoesNotContain(high, root.ActiveStates)
    show t (Slider.create [ Slider.value 85.0 ]) |> ignore
    Assert.Equal(red, root.Get SliderProps.fill)
    Assert.Contains(high, root.ActiveStates)
    show t (Slider.create [ Slider.value 79.0 ]) |> ignore
    Assert.NotEqual(red, root.Get SliderProps.fill)
