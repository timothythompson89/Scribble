/// M7 step 2a: themes, inherited text properties, overlay/opacity/scale and transitions.
module Scribble.Tests.ThemeTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping

let private red = Color.Hex 0xFF0000
let private green = Color.Hex 0x00FF00
let private ms (v: float) = TimeSpan.FromMilliseconds v

let private themed (theme: Theme) (styles: Style list) =
    let t = ElementTree(textSystem ())
    t.Theme <- ValueSome theme
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

let private dark = Theme.dark []

[<Fact>]
let ``the theme's looks sit below app styles and own values`` () =
    let t = themed (Theme.dark [ Theme.control green ]) [ Button.style [ Style.cls "red" ] [ Button.background red ] ]
    let root =
        show t (
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.name "plain" ]
                    Button.create [ Button.name "styled"; Button.classes [ "red" ] ]
                    Button.create [ Button.name "own"; Button.classes [ "red" ]; Button.background Color.White ]
                ]
            ]
        )
    Assert.Equal(green, (find "plain" root).Get BorderProps.background)
    Assert.Equal(red, (find "styled" root).Get BorderProps.background)
    Assert.Equal(Color.White, (find "own" root).Get BorderProps.background)

[<Fact>]
let ``the theme gives the state looks: hover, pressed, disabled`` () =
    let t = themed dark []
    let root =
        show t (
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.name "a" ]
                    Button.create [ Button.name "off"; Button.isEnabled false; Button.text "x" ]
                ]
            ]
        )
    let a = find "a" root
    Assert.Equal(Color.Transparent, a.Get BorderProps.overlay)
    // Each change fades from the moment it's first drawn (read), as a frame would.
    a.ChangePointersOver 1
    layout t
    a.Get BorderProps.overlay |> ignore
    t.Time <- ms 1000.0 // past the fade
    Assert.Equal(Color.White.WithAlpha 0.08f, a.Get BorderProps.overlay)
    a.ChangePointersPressing 1
    layout t
    a.Get BorderProps.overlay |> ignore
    a.Get LayoutProps.scale |> ignore
    t.Time <- ms 2000.0
    Assert.Equal(0.97, a.Get LayoutProps.scale, 6)
    Assert.Equal(Color.Black.WithAlpha 0.2f, a.Get BorderProps.overlay)
    let off = find "off" root
    Assert.Equal(0.5, off.Get LayoutProps.opacity)
    // The text inside a disabled Button doesn't fade a second time.
    Assert.Equal(1.0, off.Children[0].Get LayoutProps.opacity)

[<Fact>]
let ``disabled text fades on its own`` () =
    let t = themed dark []
    let root = show t (TextBlock.create [ TextBlock.text "x"; TextBlock.isEnabled false ])
    Assert.Equal(0.45, root.Get LayoutProps.opacity)

[<Fact>]
let ``without a theme there are no state looks, only the types' base looks`` () =
    let t = ElementTree(textSystem ())
    let root = show t (Button.create [ Button.name "a"; Button.isEnabled false ])
    Assert.Equal(Color.Hex 0x3A3A55, root.Get BorderProps.background)
    Assert.Equal(1.0, root.Get LayoutProps.opacity)

[<Fact>]
let ``an element's theme applies to what's inside it`` () =
    let t = themed dark []
    let inner = Theme.dark [ Theme.control red; Theme.text green ]
    let root =
        show t (
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.name "outside" ]
                    Border.create [
                        Border.theme inner
                        Border.content (
                            StackPanel.create [
                                StackPanel.children [ Button.create [ Button.name "inside" ]; TextBlock.create [ TextBlock.name "text" ] ]
                            ]
                        )
                    ]
                ]
            ]
        )
    Assert.Equal(dark.Get Palette.control, (find "outside" root).Get BorderProps.background)
    Assert.Equal(red, (find "inside" root).Get BorderProps.background)
    Assert.Equal(green, (find "text" root).Get TextProps.foreground)

[<Fact>]
let ``text color, font size and font are inherited: own value, then ancestors, then the theme`` () =
    let t = themed (Theme.dark [ Theme.text green; Theme.fontSize 16.0 ]) []
    let view (color: Color) =
        StackPanel.create [
            StackPanel.children [
                TextBlock.create [ TextBlock.name "plain" ]
                Border.create [
                    Border.foreground color
                    Border.fontSize 20.0
                    Border.content (
                        StackPanel.create [
                            StackPanel.children [
                                TextBlock.create [ TextBlock.name "inner" ]
                                TextBlock.create [ TextBlock.name "own"; TextBlock.foreground Color.White ]
                            ]
                        ]
                    )
                ]
            ]
        ]
    let root = show t (view red)
    Assert.Equal(green, (find "plain" root).Get TextProps.foreground)
    Assert.Equal(16.0, (find "plain" root).Get TextProps.fontSize)
    Assert.Equal(red, (find "inner" root).Get TextProps.foreground)
    Assert.Equal(20.0, (find "inner" root).Get TextProps.fontSize)
    Assert.Equal(Color.White, (find "own" root).Get TextProps.foreground)
    // Changing the ancestor's value reaches the text inside.
    let root = show t (view Color.Black)
    Assert.Equal(Color.Black, (find "inner" root).Get TextProps.foreground)
    // A Button's text takes the Button's color.
    let root = show t (Button.create [ Button.foreground red; Button.text "x" ])
    Assert.Equal(red, root.Children[0].Get TextProps.foreground)

[<Fact>]
let ``an inherited font size changes the text's measured size`` () =
    let t = themed dark []
    let view (size: float) =
        Border.create [
            Border.fontSize size
            Border.content (TextBlock.create [ TextBlock.name "t"; TextBlock.text "Hello"; TextBlock.verticalAlignment VerticalAlignment.Top ])
        ]
    let root = show t (view 14.0)
    let small = (find "t" root).Bounds.Height
    let root = show t (view 28.0)
    Assert.True((find "t" root).Bounds.Height > small * 1.5)

[<Fact>]
let ``a transition fades between values, starting from where it was, and not on first show`` () =
    let fade = [ Border.style [] [ Style.transition BorderProps.background (ms 100.0) ] ]
    let t = themed dark fade
    let mutable invalidated = 0
    t.OnInvalidated <- fun () -> invalidated <- invalidated + 1
    t.Time <- TimeSpan.Zero
    let root = show t (Border.create [ Border.background Color.Black ])
    // First show: the value as is.
    Assert.Equal(Color.Black, root.Get BorderProps.background)
    show t (Border.create [ Border.background Color.White ]) |> ignore
    Assert.Equal(Color.Black, root.Get BorderProps.background) // just started
    t.Time <- ms 50.0
    let mid = root.Get BorderProps.background
    Assert.True(mid.R > 0.0f && mid.R < 1.0f)
    // While moving, it asks for the next frame (after this one).
    invalidated <- 0
    t.RunAfterFrame()
    Assert.True(invalidated > 0)
    t.Time <- ms 100.0
    Assert.Equal(Color.White, root.Get BorderProps.background)
    // Once there, it stops asking.
    t.RunAfterFrame()
    invalidated <- 0
    root.Get BorderProps.background |> ignore
    t.RunAfterFrame()
    Assert.Equal(0, invalidated)

[<Fact>]
let ``only properties that affect drawing alone can have transitions`` () =
    Assert.Throws<ArgumentException>(fun () -> Style.transition<Border, Length> LayoutProps.width (ms 100.0) |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> Style.transition<TextBlock, string> TextProps.text (ms 100.0) |> ignore) |> ignore

[<Fact>]
let ``opacity and scale apply while rendering`` () =
    let t = themed dark []
    let root =
        show t (
            Border.create [
                Border.width 100.0; Border.height 50.0; Border.horizontalAlignment HorizontalAlignment.Left
                Border.verticalAlignment VerticalAlignment.Top
                Border.background Color.White; Border.opacity 0.5; Border.scale 0.5
            ]
        )
    let list = DrawList(1.0)
    root.Render list
    let data = list.InstanceData
    Assert.Equal(0.5f, data[11]) // alpha
    Assert.Equal(25.0f, data[0]) // x: 100 wide at half size, around its center
    Assert.Equal(50.0f, data[2]) // width
    Assert.Equal(1.0, list.Opacity) // popped again

[<Fact>]
let ``themed properties list what the theme can set on the type, in any state`` () =
    let t = themed dark []
    let root = show t (Button.create [])
    let themedProps = root.ThemedProperties
    let rules (p: Prop) = themedProps |> List.pick (fun x -> if obj.ReferenceEquals(x.Prop, p) then Some x.Selectors else None)
    Assert.Equal<string list>([ "Button:hover"; "Button:pressed"; "Button:disabled" ], rules BorderProps.overlay)
    Assert.Equal<string list>([ "Button:pressed" ], rules LayoutProps.scale)
    Assert.Equal<string list>([ "Button" ], rules BorderProps.background)
    // No theme: nothing.
    let bare = ElementTree(textSystem ())
    Assert.Empty((show bare (Button.create [])).ThemedProperties)

[<Fact>]
let ``Values say whether a style value came from the theme or from the app`` () =
    let t = themed dark [ Button.style [ Style.cls "red" ] [ Button.background red ] ]
    let root =
        show t (StackPanel.create [ StackPanel.children [ Button.create [ Button.name "plain" ]; Button.create [ Button.name "red"; Button.classes [ "red" ] ] ] ])
    let background (e: Element) = e.Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, BorderProps.background))
    let plain, styled = background (find "plain" root), background (find "red" root)
    Assert.Equal(ValueSource.Theme, plain.Source)
    Assert.Equal("Button", plain.Selector)
    Assert.Equal(ValueSource.Style, styled.Source)
    Assert.Equal("Button.red", styled.Selector)

// A label inside a button, inside a panel: for the forced states that spread.
let private nested () =
    let t =
        themed dark [
            TextBlock.style [ States.disabled ] [ TextBlock.foreground red ]
            Border.style [ States.hover ] [ Border.background green ] // exactly Border: not the Button
        ]
    let root =
        show t (
            Border.create [
                Border.name "panel"
                Border.content (
                    StackPanel.create [
                        StackPanel.children [
                            Button.create [ Button.name "button"; Button.content (TextBlock.create [ TextBlock.name "label"; TextBlock.text "Hi" ]) ]
                            TextBlock.create [ TextBlock.name "outside"; TextBlock.text "Out" ]
                        ]
                    ]
                )
            ]
        )
    t, root

[<Fact>]
let ``a forced disabled reaches everything inside, as isEnabled false does`` () =
    let t, root = nested ()
    let button, label, outside = find "button" root, find "label" root, find "outside" root
    Assert.NotEqual(red, label.Get InheritedProps.foreground)
    button.ForceState(States.disabled, true)
    layout t
    Assert.Equal(red, label.Get InheritedProps.foreground)
    Assert.Contains(States.disabled, label.ActiveStates)
    Assert.NotEqual(red, outside.Get InheritedProps.foreground) // only inside
    Assert.True(button.IsEnabled && label.IsEnabled) // input still sees it enabled
    button.UnforceState States.disabled
    layout t
    Assert.NotEqual(red, label.Get InheritedProps.foreground)
    Assert.DoesNotContain(States.disabled, label.ActiveStates)

[<Fact>]
let ``a forced hover reaches the ancestors, as the pointer over a child does`` () =
    let t, root = nested ()
    let panel, button, label, outside = find "panel" root, find "button" root, find "label" root, find "outside" root
    label.ForceState(States.hover, true)
    layout t
    Assert.Contains(States.hover, button.ActiveStates)
    Assert.Equal(green, panel.Get BorderProps.background)
    Assert.DoesNotContain(States.hover, outside.ActiveStates) // a sibling isn't hovered
    Assert.False(button.IsPointerOver) // input doesn't see it
    label.UnforceState States.hover
    layout t
    Assert.DoesNotContain(States.hover, button.ActiveStates)
    Assert.NotEqual(green, panel.Get BorderProps.background)

[<Fact>]
let ``an element leaving its tree drops its forced states`` () =
    let t = themed dark []
    let view withButton =
        StackPanel.create [
            StackPanel.children [
                if withButton then Button.create [ Button.name "forced"; Button.text "x" ]
                TextBlock.create [ TextBlock.text "stays" ]
            ]
        ]
    let button = find "forced" (show t (view true))
    button.ForceState(States.hover, true)
    button.ForceState(States.disabled, false)
    button.ForceState(States.pressed, true)
    show t (view false) |> ignore
    // The counts that gate the forced-state walks are process-wide (other tests may change them
    // meanwhile), so check what the element holds: nothing forced, nothing counted.
    Assert.Empty(button.ForcedStates)
    Assert.False(button.LooksForced 1 || button.LooksForcedOff 16 || button.LooksForced 2)

[<Fact>]
let ``the light theme gives dark text, light controls and dark tints`` () =
    let light = Theme.light []
    let t = themed light []
    let root =
        show t (
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.name "b"; Button.content (TextBlock.create [ TextBlock.name "label"; TextBlock.text "Hi" ]) ]
                    ScrollViewer.create [ ScrollViewer.name "s" ]
                ]
            ]
        )
    let b = find "b" root
    Assert.Equal(light.Get Palette.control, b.Get BorderProps.background)
    Assert.Equal(light.Get Palette.shadow, (b.Get BorderProps.shadow).Value)
    Assert.Equal(light.Get Palette.text, (find "label" root).Get InheritedProps.foreground)
    Assert.Equal(light.Get Palette.scrollThumb, (find "s" root).Get ScrollViewerProps.thumb)
    b.ForceState(States.hover, true)
    layout t
    Assert.Equal(light.Get Palette.hoverTint, b.Get BorderProps.overlay)
    Assert.Equal(Color.Black.WithAlpha 0.05f, light.Get Palette.hoverTint)

[<Fact>]
let ``the new palette colors are settings`` () =
    let shadow = { Blur = 9.0; OffsetX = 0.0; OffsetY = 2.0; Spread = 0.0; Color = red }
    let t =
        Theme.dark [ Theme.onAccent green; Theme.hoverTint red; Theme.pressTint green; Theme.scrollThumb red; Theme.shadow shadow ]
    Assert.Equal(green, t.Get Palette.onAccent)
    Assert.Equal(red, t.Get Palette.hoverTint)
    Assert.Equal(green, t.Get Palette.pressTint)
    Assert.Equal(red, t.Get Palette.scrollThumb)
    Assert.Equal(shadow, t.Get Palette.shadow)
    Assert.Equal(Color.White, (Theme.dark []).Get Palette.onAccent)
    Assert.Equal(Color.White, (Theme.light []).Get Palette.onAccent)
