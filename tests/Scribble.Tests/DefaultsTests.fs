/// Where defaults live (DESIGN.md, "Where defaults live", decision 11): an element type's look at rest is its
/// type defaults, by theme value; the theme's styles add only state looks.
module Scribble.Tests.DefaultsTests

open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping

/// Every built-in element type.
let private types: ElementType list =
    [ Border.Type; TextBlock.Type; Label.Type; IconView.Type; ImageView.Type; Separator.Type; Flyout.Type; TooltipPresenter.Type
      StackPanel.Type; Grid.Type; DockPanel.Type; Canvas.Type; WrapPanel.Type
      ScrollViewer.Type; ItemsView.Type
      Button.Type; ToggleButton.Type; CheckBox.Type; Switch.Type; RadioButton.Type; RadioGroup.Type; Slider.Type
      TabControl.Type; TabHeader.Type; TabItem.Type; ListBox.Type; ListBoxItems.Type; ListBoxItem.Type; ComboBox.Type; TextBox.Type
      Menu.Type; MenuItem.Type; SelectionHandle.Type; Modal.Type; Scrim.Type; ToastPresenter.Type ]

/// One element of the type with no settings, laid out in a tree with or without a theme.
let private realize (theme: Theme voption) (elementType: ElementType) : Element =
    let t = ElementTree(textSystem ())
    t.Theme <- theme
    t.Update(View.Create(elementType, ([]: Setting<Layoutable> list)))
    t.UpdateLayout(Size.Create(400.0, 300.0))
    t.Root.Value

/// Text settings come from above (a theme's base values; black without one), not from the type.
let private fromAbove (p: Prop) =
    obj.ReferenceEquals(p, InheritedProps.foreground) || obj.ReferenceEquals(p, InheritedProps.font) || obj.ReferenceEquals(p, InheritedProps.fontSize)

[<Fact>]
let ``every built-in element type looks the same at rest without a theme and under the dark theme`` () =
    let dark = Theme.dark []
    for elementType in types do
        let bare, themed = realize ValueNone elementType, realize (ValueSome dark) elementType
        for v in themed.Values do
            // Transitions say how a change fades, not how it looks; a type that sets its own text colour is compared.
            let isTransition = v.Prop.Name.StartsWith "Transition"
            let typeSets = bare.Values |> List.exists (fun b -> obj.ReferenceEquals(b.Prop, v.Prop) && b.Source = ValueSource.TypeDefault)
            // What a control set on itself in code (a ComboBox's flyout) is not a look.
            let ownCode = v.Source = ValueSource.Code || v.Source = ValueSource.View
            if not isTransition && not ownCode && (typeSets || not (fromAbove v.Prop)) then
                Assert.True(
                    v.Prop.Same(bare.GetBoxed v.Prop, themed.GetBoxed v.Prop),
                    elementType.Name + "." + v.Prop.Name + " differs between no theme and the dark theme: its look at rest belongs in its type defaults")
        // And nothing at rest comes from the theme's styles.
        for v in themed.Values do
            if v.Source = ValueSource.Theme && not (v.Prop.Name.StartsWith "Transition") then
                Assert.Fail(elementType.Name + "." + v.Prop.Name + " is set at rest by the theme's style '" + v.Selector + "': make it a type default")

/// A value that is a colour a theme would want to change.
let private fixedColour (value: obj) =
    match value with
    | :? Color as c -> c.A > 0.0f
    | :? (BoxShadow voption) as s -> s.IsSome
    | _ -> false

[<Fact>]
let ``no built-in type default or property default is a fixed colour`` () =
    // An element of a type with no defaults reads property defaults.
    let probe = realize ValueNone (Element.define "DefaultsProbe" [])
    for elementType in types do
        let e = realize ValueNone elementType
        for v in e.Values do
            if v.Source = ValueSource.TypeDefault && fixedColour v.Value then
                Assert.True(
                    v.ThemeValue.Length > 0,
                    elementType.Name + "'s type default for " + v.Prop.Name + " is a fixed colour: give it a theme value (Palette)")
            // Property defaults are neutral: black text (unit tests rely on it) is the one exception.
            if not (obj.ReferenceEquals(v.Prop, InheritedProps.foreground)) then
                Assert.False(
                    fixedColour (probe.GetBoxed v.Prop),
                    v.Prop.Name + "'s property default is a fixed colour: make it transparent and give " + elementType.Name + " a type default by theme value")

[<Fact>]
let ``a type default given twice in one list takes the later one`` () =
    let shared: Setting<Border> list = [ Border.background (Color.Hex 0x111111); Border.padding 4.0 ]
    let probe = Element.extend Border.Type "TwiceProbe" [ Element.defaults (shared @ [ Border.background (Color.Hex 0x222222) ]) ]
    let e = realize ValueNone probe
    Assert.Equal(Color.Hex 0x222222, e.Get BorderProps.background)
    Assert.Equal(Thickness.Uniform 4.0, e.Get BorderProps.padding)
    // Without a theme a Switch's knob is its own colour, not the mark colour of the list it shares.
    Assert.Equal(Palette.textDim.Default, (realize ValueNone Switch.Type).Get ToggleButtonProps.markColor)

[<Fact>]
let ``tools show a toast, a tooltip and a modal by the names an app wrote`` () =
    // DESIGN.md, "Names an app sees": the boxes the framework makes aren't called presenters where an app can see.
    Assert.Equal("Toast", ToastPresenter.Type.Name)
    Assert.Equal("Tooltip", TooltipPresenter.Type.Name)
    Assert.Equal("Modal", ModalAnchor.Type.Name)
    // The names styles are written with, and what the inspector says of them.
    Assert.Equal("Toast.warning", (Toast.style [ Style.cls "warning" ] [ Border.padding 2.0 ]).Description)
    Assert.Equal("Tooltip", (Tooltip.style [] [ Border.padding 2.0 ]).Description)
