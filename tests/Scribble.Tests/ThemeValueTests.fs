/// Theme values: settings that read a theme's value by name (`Border.background Palette.surface`).
module Scribble.Tests.TokenTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping

let private red = Color.Hex 0xFF0000
let private green = Color.Hex 0x00FF00
let private ms (v: float) = TimeSpan.FromMilliseconds v

/// An app's own theme value.
let private cardLine = ThemeValue.define "CardLine" (Color.Hex 0x123456)

let private layout (t: ElementTree) = t.UpdateLayout(Size.Create(400.0, 300.0))

let rec private find (name: string) (e: Element) : Element =
    if e.Name = name then e
    else e.Children |> Seq.pick (fun c -> if c.Name = name then Some c else (try Some(find name c) with _ -> None))

let private show (t: ElementTree) (view: View) =
    t.Update view
    layout t
    t.Root.Value

[<Fact>]
let ``a theme value reads the nearest theme, else its default`` () =
    let light = Theme.light [ Theme.set cardLine green ]
    let dark = Theme.dark [ Theme.set cardLine red ]
    let t = ElementTree(textSystem ())
    let view =
        StackPanel.create [
            StackPanel.children [
                Border.create [ Border.name "plain"; Border.background Palette.surface; Border.borderBrush cardLine ]
                Border.create [
                    Border.theme light
                    Border.child (Border.create [ Border.name "inner"; Border.background Palette.surface; Border.borderBrush cardLine ])
                ]
            ]
        ]
    // No theme anywhere: the theme values' defaults.
    let root = show t view
    Assert.Equal(Palette.surface.Default, (find "plain" root).Get BorderProps.background)
    Assert.Equal(Color.Hex 0x123456, (find "plain" root).Get BorderProps.borderBrush)
    // The tree's theme, and an element's own theme for what's inside it.
    t.Theme <- ValueSome dark
    layout t
    Assert.Equal(red, (find "plain" root).Get BorderProps.borderBrush)
    Assert.Equal(light.Get Palette.surface, (find "inner" root).Get BorderProps.background)
    Assert.Equal(green, (find "inner" root).Get BorderProps.borderBrush)

[<Fact>]
let ``styles written with theme values follow a theme switch`` () =
    let t = ElementTree(textSystem ())
    t.Styles <- [ Border.style [ Style.cls "card" ] [ Border.background Palette.surface ] ]
    let view (theme: Theme) = Border.create [ Border.theme theme; Border.child (Border.create [ Border.name "card"; Border.classes [ "card" ] ]) ]
    let root = show t (view (Theme.dark []))
    Assert.Equal((Theme.dark []).Get Palette.surface, (find "card" root).Get BorderProps.background)
    let light = Theme.light []
    show t (view light) |> ignore
    Assert.Equal(light.Get Palette.surface, (find "card" root).Get BorderProps.background)

[<Fact>]
let ``inherited text resolves where it's set, and font size theme values re-measure`` () =
    let big = Theme.dark [ Theme.fontSize 30.0 ]
    let t = ElementTree(textSystem ())
    let view (theme: Theme) =
        Border.create [
            Border.theme theme
            Border.foreground Palette.accent
            Border.fontSize Palette.fontSize
            Border.child (TextBlock.create [ TextBlock.name "t"; TextBlock.text "Hi"; TextBlock.verticalAlignment VerticalAlignment.Top ])
        ]
    let root = show t (view (Theme.dark []))
    Assert.Equal(Palette.accent.Default, (find "t" root).Get InheritedProps.foreground)
    let small = (find "t" root).Bounds.Height
    show t (view big) |> ignore
    Assert.Equal(30.0, (find "t" root).Get InheritedProps.fontSize)
    Assert.True((find "t" root).Bounds.Height > small * 1.5)

[<Fact>]
let ``the same theme value set again is no change, and Values name it`` () =
    let t = ElementTree(textSystem ())
    let root = show t (Border.create [ Border.background Palette.surface; Border.shadow Palette.shadow ])
    let mutable invalidated = 0
    t.OnInvalidated <- fun () -> invalidated <- invalidated + 1
    show t (Border.create [ Border.background Palette.surface; Border.shadow Palette.shadow ]) |> ignore
    Assert.Equal(0, invalidated)
    let background = root.Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, BorderProps.background))
    Assert.Equal("Palette.surface", background.ThemeValue)
    Assert.Equal(box Palette.surface.Default, background.Value)
    let shadow = root.Values |> List.find (fun v -> obj.ReferenceEquals(v.Prop, BorderProps.shadow))
    Assert.Equal(box (ValueSome Palette.shadow.Default), shadow.Value)

[<Fact>]
let ``switching themes doesn't fade, though the property has a transition`` () =
    let t = ElementTree(textSystem ())
    t.Styles <- [ Border.style [] [ Style.transition BorderProps.background (ms 100.0) ] ]
    t.Time <- TimeSpan.Zero
    let view (theme: Theme) = Border.create [ Border.theme theme; Border.child (Border.create [ Border.name "b"; Border.background Palette.surface ]) ]
    let root = show t (view (Theme.dark []))
    (find "b" root).Get BorderProps.background |> ignore
    let light = Theme.light []
    show t (view light) |> ignore
    Assert.Equal(light.Get Palette.surface, (find "b" root).Get BorderProps.background)

[<Fact>]
let ``theme setting shorthands are theme values`` () =
    let theme = Theme.dark [ Theme.accent red; Theme.cornerRadius 9.0 ]
    Assert.Equal(red, theme.Get Palette.accent)
    Assert.Equal(9.0, theme.Get Palette.cornerRadius)
    Assert.Equal(Palette.text.Default, theme.Get Palette.text) // dark sets nothing else
    let t = ElementTree(textSystem ())
    t.Theme <- ValueSome theme
    let root = show t (Border.create [ Border.cornerRadius Palette.cornerRadius ])
    Assert.Equal(CornerRadius.Uniform 9.0, root.Get BorderProps.cornerRadius)

[<Fact>]
let ``the remembered nearest theme follows changes above it and new elements`` () =
    let light = Theme.light []
    let dark = Theme.dark [ Theme.surface red ]
    let t = ElementTree(textSystem ())
    let leaf name = Border.create [ Border.name name; Border.background Palette.surface ]
    let view (outer: Theme voption) (inner: Theme voption) (extra: bool) =
        Border.create [
            yield! outer |> ValueOption.map Border.theme |> ValueOption.toList
            Border.child (
                StackPanel.create [
                    StackPanel.children [
                        Border.create [ yield! inner |> ValueOption.map Border.theme |> ValueOption.toList; Border.child (leaf "a") ]
                        if extra then leaf "b"
                    ]
                ]
            )
        ]
    let surface name root = (find name root).Get BorderProps.background
    let root = show t (view ValueNone ValueNone false)
    Assert.Equal(Palette.surface.Default, surface "a" root) // remembered: no theme
    // A theme set above, then a nearer one, then the nearer one taken away again.
    show t (view (ValueSome dark) ValueNone false) |> ignore
    Assert.Equal(red, surface "a" root)
    show t (view (ValueSome dark) (ValueSome light) false) |> ignore
    Assert.Equal(light.Get Palette.surface, surface "a" root)
    show t (view (ValueSome dark) ValueNone false) |> ignore
    Assert.Equal(red, surface "a" root)
    // A new element finds the theme above it; the tree's theme counts where no element sets one.
    show t (view (ValueSome dark) ValueNone true) |> ignore
    Assert.Equal(red, surface "b" root)
    t.Theme <- ValueSome light
    show t (view ValueNone ValueNone true) |> ignore
    Assert.Equal(light.Get Palette.surface, surface "b" root)
