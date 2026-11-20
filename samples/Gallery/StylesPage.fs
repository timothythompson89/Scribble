/// The Styles page (M7): classes, state blocks, text styled inside a card, an inline block, chips styled from a
/// state of our own, a slider styled from a state read from its value, and a button that swaps the page's style list.
module Scribble.Samples.Gallery.StylesPage

open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Accessibility
open Scribble.Samples
open Scribble.Samples.Gallery.Common

/// A state of our own for the chips below: the view switches it (`Border.state chosen true`), styles use it
/// like `States.hover`, and the inspector lists it on the chips.
let private chosen = States.define "chosen" []
/// A state read from a property an element already has: a Slider is "high" from 80 up. It is matched again by
/// itself whenever the value changes.
let private high = States.ofProp "high" SliderProps.value (fun v -> v >= 80.0)

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
          Style.onState States.hover [ Border.borderBrush highlight ]
      ]
      TextBlock.style [ Style.inside Border.Type [ Style.cls "card" ] ] [ TextBlock.foreground Palette.text ]
      TextBlock.style [ Style.inside Border.Type [ Style.cls "card"; States.hover ] ] [ TextBlock.foreground highlight ]
      // Chips look chosen while they're in our own state: the chip, and the text inside it.
      Border.style [ Style.cls "chip" ] [
          Border.borderBrush line
          Border.borderThickness 1.0
          Border.cornerRadius 14.0
          Border.padding (12.0, 5.0)
          Style.onState States.hover [ Border.background subtle ]
          Style.onState chosen [
              Border.background Palette.accent
              Border.borderBrush highlight
              Style.onState States.hover [ Border.background accentHover ]
          ]
      ]
      TextBlock.style [ Style.inside Border.Type [ Style.cls "chip" ] ] [ TextBlock.foreground Palette.textDim ]
      TextBlock.style [ Style.inside Border.Type [ Style.cls "chip"; chosen ] ] [ TextBlock.foreground Palette.onAccent ]
      // The slider's filled part warns while its value is high.
      Slider.style [] [ Style.onState high [ Slider.fill Palette.danger ] ] ]

/// The same, plus smaller buttons: switching the page between the two lists swaps a style list.
let private compactStyles = normalStyles @ [ Button.style [] [ Button.padding (8.0, 3.0) ] ]

/// The page without its title (the rendering tests show it alone).
let demo =
    Component.define "Styles" (fun ctx () ->
        let compact = ctx.useState false
        let disabled = ctx.useState false
        let picked = ctx.useState (Set.ofList [ "Fast" ])
        let level = ctx.useState 40.0
        let chipView (name: string) =
            Border.create [
                Border.classes [ "chip" ]
                Border.state chosen (picked.Current.Contains name)
                Border.onClick (fun _ -> picked.Update(fun s -> if s.Contains name then s.Remove name else s.Add name))
                Border.content (TextBlock.create [ TextBlock.text name ])
            ]
        StackPanel.create [
            StackPanel.styles (if compact.Current then compactStyles else normalStyles)
            StackPanel.spacing 12.0
            StackPanel.children [
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
                    Border.content (
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
                    Border.onState States.hover [ Border.background hover ]
                    Border.content (
                        TextBlock.create [
                            TextBlock.text "Hover me: Border.onState States.hover [ ... ], right in the view"
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
                            TextBlock.text "Click to toggle: Border.state chosen"
                            TextBlock.foreground Palette.textDim
                            TextBlock.verticalAlignment VerticalAlignment.Center
                        ]
                    ]
                ]
                Slider.create [
                    Semantics.label "Level"
                    Slider.width 240.0
                    Slider.horizontalAlignment HorizontalAlignment.Left
                    Slider.value level.Current
                    Slider.onValueChanged level.Set
                ]
                TextBlock.create [
                    TextBlock.text "Drag past 80 and it turns red: a state read from its value, States.ofProp"
                    TextBlock.foreground Palette.textDim
                    TextBlock.textWrapping TextWrapping.Wrap
                ]
            ]
        ])

let view () =
    page "Styles" "Looks from style lists: classes, states (built in, one of our own, one read from a value), text inside a card and inline blocks." [ demo () ]
