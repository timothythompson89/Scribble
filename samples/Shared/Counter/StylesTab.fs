/// The Styles tab (M7): classes, state blocks, text styled inside a card, an inline block, chips
/// styled from a property value, and a button that swaps the page's style list.
module Scribble.Samples.CounterDemo.StylesTab

open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples
open Scribble.Samples.CounterDemo.Common

/// A property of our own for the chips below (M8's toggle controls will have one built in). Styles
/// select on its value with `Style.prop`, or through a state made from it.
let private selected = Prop.define "Selected" false Affects.Render
let private isSelected (v: bool) : Setting<Border> = Setting.create selected v
/// A state read from the property: usable like `States.hover`, and listed by the inspector.
let private selectedState = States.ofProp "selected" selected id

/// The page's styles: classes, a state block, and text inside cards. Colors are theme values, so
/// one list serves both themes.
let private normalStyles =
    [ Button.style [ Style.cls "primary" ] [ Button.background Palette.accent; Button.borderBrush accentBorder; Button.foreground Palette.onAccent ]
      Button.style [ Style.cls "danger" ] [ Button.background Palette.danger; Button.borderBrush dangerBorder; Button.foreground Palette.onAccent ]
      Border.style [ Style.cls "card" ] [
          Border.background Palette.surface
          Border.borderBrush line
          Border.borderThickness 1.0
          Border.cornerRadius 8.0
          Border.padding 12.0
          // Highlights while the pointer is anywhere over the card.
          Style.on States.hover [ Border.borderBrush highlight ]
      ]
      TextBlock.style [ Style.inside Border.Type [ Style.cls "card" ] ] [ TextBlock.foreground Palette.text ]
      TextBlock.style [ Style.inside Border.Type [ Style.cls "card"; States.hover ] ] [ TextBlock.foreground highlight ]
      // Chips look selected from their Selected value: through a state made from it (the chip), and
      // a property selector (its text).
      Border.style [ Style.cls "chip" ] [
          Border.borderBrush line
          Border.borderThickness 1.0
          Border.cornerRadius 14.0
          Border.padding (12.0, 5.0)
          Style.on States.hover [ Border.background subtle ]
      ]
      Border.style [ Style.cls "chip"; selectedState ] [
          Border.background Palette.accent
          Border.borderBrush highlight
          Style.on States.hover [ Border.background accentHover ]
      ]
      TextBlock.style [ Style.inside Border.Type [ Style.cls "chip" ] ] [ TextBlock.foreground Palette.textDim ]
      TextBlock.style [ Style.inside Border.Type [ Style.prop selected true ] ] [ TextBlock.foreground Palette.onAccent ] ]

/// The same, plus smaller buttons: switching the page between the two lists swaps a style list.
let private compactStyles = normalStyles @ [ Button.style [] [ Button.padding (8.0, 3.0) ] ]

let stylesPage =
    Component.define "StylesPage" (fun ctx () ->
        let compact = ctx.useState false
        let disabled = ctx.useState false
        let picked = ctx.useState (Set.ofList [ "Fast" ])
        let chipView (name: string) =
            Border.create [
                Border.classes [ "chip" ]
                isSelected (picked.Current.Contains name)
                Border.onClick (fun _ -> picked.Update(fun s -> if s.Contains name then s.Remove name else s.Add name))
                Border.child (TextBlock.create [ TextBlock.text name ])
            ]
        StackPanel.create [
            StackPanel.styles (if compact.Current then compactStyles else normalStyles)
            StackPanel.margin (24.0, 16.0, 24.0, 0.0)
            StackPanel.spacing 12.0
            StackPanel.children [
                TextBlock.create [ TextBlock.text "Classes, a hover block, text inside cards, inline blocks"; TextBlock.foreground Palette.textDim; TextBlock.textWrapping TextWrapping.Wrap ]
                // WrapPanels (Adaptive.fs): the rows wrap on a phone.
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.lineSpacing 8.0
                    WrapPanel.children [
                        Button.create [ Button.text "Plain" ]
                        Button.create [ Button.classes [ "primary" ]; Button.text "Primary" ]
                        Button.create [ Button.classes [ "danger" ]; Button.text "Danger"; Button.isEnabled (not disabled.Current) ]
                    ]
                ]
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.lineSpacing 8.0
                    WrapPanel.children [
                        Button.create [
                            Button.text (if compact.Current then "Normal buttons" else "Compact buttons")
                            Button.onClick (fun _ -> compact.Update not)
                        ]
                        Button.create [
                            Button.text (if disabled.Current then "Enable Danger" else "Disable Danger")
                            Button.onClick (fun _ -> disabled.Update not)
                        ]
                    ]
                ]
                Border.create [
                    Border.classes [ "card" ]
                    Border.child (
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                TextBlock.create [ TextBlock.text "A card"; TextBlock.font (Fonts.SemiBold()) ]
                                TextBlock.create [ TextBlock.text "Its text is styled because it's inside a card;" ; TextBlock.textWrapping TextWrapping.Wrap ]
                                TextBlock.create [ TextBlock.text "hover the card and the border and text light up." ; TextBlock.textWrapping TextWrapping.Wrap ]
                            ]
                        ]
                    )
                ]
                TextBlock.create [ TextBlock.text "Text outside the card keeps its own color."; TextBlock.foreground Palette.textDim; TextBlock.textWrapping TextWrapping.Wrap ]
                Border.create [
                    Border.minHeight 40.0
                    Border.cornerRadius 6.0
                    Border.on States.hover [ Border.background hover ]
                    Border.child (
                        TextBlock.create [
                            TextBlock.text "Hover me: Border.on States.hover [ ... ], a style right in the view"
                            TextBlock.foreground Palette.textDim
                            TextBlock.textWrapping TextWrapping.Wrap
                            TextBlock.margin (12.0, 8.0)
                            TextBlock.verticalAlignment VerticalAlignment.Center
                        ]
                    )
                ]
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.lineSpacing 8.0
                    WrapPanel.children [
                        chipView "Fast"
                        chipView "Quiet"
                        chipView "Cheap"
                        TextBlock.create [
                            TextBlock.text "Click to toggle: Style.prop selected true"
                            TextBlock.foreground Palette.textDim
                            TextBlock.verticalAlignment VerticalAlignment.Center
                        ]
                    ]
                ]
            ]
        ])
