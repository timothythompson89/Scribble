namespace Scribble.Core

open System
open Scribble.Rendering
open Scribble.Text

/// The built-in looks, as ordinary styles made from a theme's palette.
module internal ThemeLooks =
    /// How long hover and pressed tints fade.
    let private tintTime = TimeSpan.FromMilliseconds 100.0
    /// How long a press takes to sink in and spring back.
    let private pressTime = TimeSpan.FromMilliseconds 120.0

    /// Button's resting shadow, and flat (pressed or disabled).
    let raised = { Blur = 3.0; OffsetX = 0.0; OffsetY = 1.0; Spread = 0.0; Color = Color.Rgba(0.0f, 0.0f, 0.0f, 0.35f) }
    let flat = { raised with Blur = raised.Blur * 0.4; OffsetY = 0.0 }

    let build (t: Theme) : struct (Style list * struct (Prop * obj)[]) =
        let styles =
            [ Button.style [] [
                  Button.background t.Control
                  Button.borderBrush t.Border
                  Button.cornerRadius t.CornerRadius
                  Button.foreground t.Text
                  Style.transition BorderProps.overlay tintTime
                  Style.transition LayoutProps.scale pressTime
                  Style.transition BorderProps.shadow pressTime
                  Style.on States.hover [ Button.overlay (Color.White.WithAlpha 0.08f) ]
                  // Sinks to 97% around its center and its shadow flattens.
                  Style.on States.pressed [ Button.overlay (Color.Black.WithAlpha 0.2f); Button.scale 0.97; Button.shadow flat ]
                  // Last, so it wins over hover and pressed.
                  Style.on States.disabled [ Button.overlay Color.Transparent; Button.opacity 0.5; Button.shadow flat ]
              ]
              TextBlock.style [ States.disabled ] [ TextBlock.opacity 0.45 ]
              // A disabled Button fades as a whole: its text doesn't fade twice.
              TextBlock.style [ Style.inside Button.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
              ScrollViewer.style [] [ ScrollViewer.thumb (Color.White.WithAlpha 0.3f) ] ]
        let bases =
            [| struct (InheritedProps.foreground :> Prop, box t.Text)
               struct (InheritedProps.font :> Prop, box t.Font)
               struct (InheritedProps.fontSize :> Prop, box t.FontSize)
               struct (FocusRing.colorProp :> Prop, box t.FocusRing) |]
        struct (styles, bases)

[<AutoOpen>]
module ThemeExtensions =
    type Theme with
        /// The dark theme (the default): `Theme.dark []`, or with changes,
        /// `Theme.dark [ Theme.accent (Color.Hex 0x3D59A1); Theme.cornerRadius 8.0 ]`.
        static member dark(settings: ThemeSetting list) : Theme =
            let palette: ThemePalette =
                { Background = Color.Hex 0x181825
                  Surface = Color.Hex 0x24243A
                  Control = Color.Hex 0x3A3A55
                  Border = Color.Hex 0x4A4A68
                  Text = Color.Hex 0xE6E6F0
                  TextDim = Color.Hex 0x9A9AB0
                  Accent = Color.Hex 0x3D59A1
                  Danger = Color.Hex 0x8C2F39
                  FocusRing = FocusRing.color
                  CornerRadius = 6.0
                  Font = Fonts.Regular()
                  FontSize = 14.0 }
            Theme(settings |> List.fold (fun p s -> s.Apply p) palette, ThemeLooks.build)

    /// The theme hosts use when given none (one instance, so its styles are made once).
    let internal defaultTheme = lazy (Theme.dark [])
