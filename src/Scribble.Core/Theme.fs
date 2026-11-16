namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Rendering

/// The built-in looks: ordinary styles that read the theme's values by token, so every theme
/// shares one list (themes differ only in their values).
module internal ThemeLooks =
    /// How long hover and pressed tints fade.
    let private tintTime = TimeSpan.FromMilliseconds 100.0
    /// How long a press takes to sink in and spring back.
    let private pressTime = TimeSpan.FromMilliseconds 120.0
    /// The theme's shadow, flat (pressed or disabled).
    let private flat: Setting<Button> = Setting.fromThemeWith BorderProps.shadow Palette.shadow ThemeConvert.flatShadow

    let private styles =
        [ Button.style [] [
              Button.background Palette.control
              Button.borderBrush Palette.border
              Button.cornerRadius Palette.cornerRadius
              Button.foreground Palette.text
              Button.shadow Palette.shadow
              Style.transition BorderProps.overlay tintTime
              Style.transition LayoutProps.scale pressTime
              Style.transition BorderProps.shadow pressTime
              Style.on States.hover [ Button.overlay Palette.hoverTint ]
              // Sinks to 97% around its center and its shadow flattens.
              Style.on States.pressed [ Button.overlay Palette.pressTint; Button.scale 0.97; flat ]
              // Last, so it wins over hover and pressed.
              Style.on States.disabled [ Button.overlay Color.Transparent; Button.opacity 0.5; flat ]
          ]
          TextBlock.style [ States.disabled ] [ TextBlock.opacity 0.45 ]
          // A disabled Button fades as a whole: its text doesn't fade twice.
          TextBlock.style [ Style.inside Button.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          ScrollViewer.style [] [ ScrollViewer.thumb Palette.scrollThumb ] ]

    /// Inherited properties' base values, by token.
    let private bases: struct (Prop * ThemeToken)[] =
        [| struct (InheritedProps.foreground :> Prop, Palette.text :> ThemeToken)
           struct (InheritedProps.font :> Prop, Palette.font :> ThemeToken)
           struct (InheritedProps.fontSize :> Prop, Palette.fontSize :> ThemeToken)
           struct (FocusRing.colorProp :> Prop, Palette.focusRing :> ThemeToken) |]

    /// Made once, on first use, for every theme.
    let all = lazy (styles, bases)

    /// A theme from base values and its settings (later settings win).
    let make (preset: ThemeSetting list) (settings: ThemeSetting list) =
        let values = Dictionary<int, obj>()
        for s in preset @ settings do
            s.Apply values
        Theme(values, all)

[<AutoOpen>]
module ThemeExtensions =
    type Theme with
        /// The dark theme (the default): `Theme.dark []`, or with changes,
        /// `Theme.dark [ Theme.accent (Color.Hex 0x3D59A1); Theme.set cardLine (Color.Hex 0x34345A) ]`.
        /// Its values are the tokens' defaults.
        static member dark(settings: ThemeSetting list) : Theme = ThemeLooks.make [] settings

        /// The light theme: `Theme.light []`, or with changes, `Theme.light [ Theme.accent c ]`.
        static member light(settings: ThemeSetting list) : Theme =
            ThemeLooks.make
                [ Theme.background (Color.Hex 0xF3F3F7)
                  Theme.surface (Color.Hex 0xFFFFFF)
                  Theme.control (Color.Hex 0xFFFFFF)
                  Theme.border (Color.Hex 0xC9C9D6)
                  Theme.text (Color.Hex 0x1E1E2E)
                  Theme.textDim (Color.Hex 0x5E5E72)
                  Theme.danger (Color.Hex 0xB3364A)
                  Theme.focusRing (Color.Hex 0x2F5BD3)
                  Theme.hoverTint (Color.Black.WithAlpha 0.05f)
                  Theme.pressTint (Color.Black.WithAlpha 0.1f)
                  Theme.scrollThumb (Color.Black.WithAlpha 0.3f)
                  Theme.shadow { Blur = 3.0; OffsetX = 0.0; OffsetY = 1.0; Spread = 0.0; Color = Color.Rgba(0.0f, 0.0f, 0.0f, 0.15f) } ]
                settings

    /// The theme hosts use when given none (one instance).
    let internal defaultTheme = lazy (Theme.dark [])

    let private current = lazy (AppValue.define "Theme" defaultTheme.Value)

    type Theme with
        /// The window's theme, an app value: every layer that takes app styles (toasts, dialogs; not
        /// the inspector) and the window's background follow it. `App.theme t` sets its starting
        /// value (default `Theme.dark []`); switch from anywhere with `ctx.useAppValue Theme.current`
        /// (`.Set lightTheme`), `AppContext.Set(Theme.current, t)` or `Cmd.setAppValue Theme.current t`.
        /// A `theme` on an element applies inside it only.
        static member current: AppValue<Theme> = current.Value
