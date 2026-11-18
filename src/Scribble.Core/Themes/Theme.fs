namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Rendering

/// The built-in looks: ordinary styles that read the theme's values by theme value, so every theme
/// shares one list (themes differ only in their values).
module internal ThemeLooks =
    /// How long hover and pressed tints fade.
    let private tintTime = TimeSpan.FromMilliseconds 100.0
    /// How long a press takes to sink in and spring back.
    let private pressTime = TimeSpan.FromMilliseconds 120.0
    /// The theme's shadow, flat (pressed or disabled).
    let private flat<'k when 'k :> Button> : Setting<'k> = Setting.fromThemeWith BorderProps.shadow Palette.shadow ThemeConvert.flatShadow

    /// Button's look (ToggleButton shares it): its colors, then hover, pressed, disabled.
    let private buttonLook<'k when 'k :> Button> (whileOn: Setting<'k> list) : Setting<'k> list =
        [ Button.background Palette.control
          Button.borderBrush Palette.border
          Button.cornerRadius Palette.cornerRadius
          Button.foreground Palette.text
          Button.shadow Palette.shadow
          Style.transition BorderProps.overlay tintTime
          Style.transition LayoutProps.scale pressTime
          Style.transition BorderProps.shadow pressTime
          yield! whileOn
          Style.on States.hover [ Button.overlay Palette.hoverTint ]
          // Sinks to 97% around its center and its shadow flattens.
          Style.on States.pressed [ Button.overlay Palette.pressTint; Button.scale 0.97; flat ]
          // Last, so it wins over hover and pressed.
          Style.on States.disabled [ Button.overlay Color.Transparent; Button.opacity 0.5; flat ] ]

    /// CheckBox's and Switch's look: the box or track in the control colors, the accent while on,
    /// the mark (tick, knob) as `markOff` while off; hover and press tint the box or track.
    let private labeledLook<'k when 'k :> ToggleButton> (markOff: ThemeValue<Color>) (extra: Setting<'k> list) : Setting<'k> list =
        [ ToggleButton.background Palette.control
          ToggleButton.borderBrush Palette.border
          ToggleButton.foreground Palette.text
          ToggleButton.markColor markOff
          Style.transition BorderProps.overlay tintTime
          Style.transition BorderProps.background tintTime
          Style.transition BorderProps.borderBrush tintTime
          yield! extra
          Style.on States.isChecked [ ToggleButton.background Palette.accent; ToggleButton.borderBrush Palette.accent; ToggleButton.markColor Palette.onAccent ]
          Style.on States.isIndeterminate [ ToggleButton.background Palette.accent; ToggleButton.borderBrush Palette.accent; ToggleButton.markColor Palette.onAccent ]
          Style.on States.hover [ ToggleButton.overlay Palette.hoverTint ]
          Style.on States.pressed [ ToggleButton.overlay Palette.pressTint ]
          Style.on States.disabled [ ToggleButton.overlay Color.Transparent; ToggleButton.opacity 0.5 ] ]

    let private styles =
        [ Button.style [] (buttonLook [])
          ToggleButton.style [] (
              buttonLook [
                  Style.on States.isChecked [
                      ToggleButton.background Palette.accent
                      ToggleButton.borderBrush Palette.accent
                      ToggleButton.foreground Palette.onAccent
                  ]
              ]
          )
          CheckBox.style [] (labeledLook Palette.onAccent [ Setting.fromThemeWith BorderProps.cornerRadius Palette.cornerRadius ThemeConvert.halfCorners ])
          Switch.style [] (labeledLook Palette.textDim [])
          // A circle in the control colours; chosen, an accent ring and dot (M8 step 4b, decision 18).
          RadioButton.style [] [
              RadioButton.background Palette.control
              RadioButton.borderBrush Palette.border
              RadioButton.foreground Palette.text
              RadioButton.markColor Palette.accent
              Style.transition BorderProps.overlay tintTime
              Style.transition BorderProps.borderBrush tintTime
              Style.on States.isChecked [ RadioButton.borderBrush Palette.accent ]
              Style.on States.hover [ RadioButton.overlay Palette.hoverTint ]
              Style.on States.pressed [ RadioButton.overlay Palette.pressTint ]
              Style.on States.disabled [ RadioButton.overlay Color.Transparent; RadioButton.opacity 0.5 ]
          ]
          Slider.style [] [
              Slider.track Palette.control
              Slider.fill Palette.accent
              Slider.thumb Palette.onAccent
              Slider.thumbBorder Palette.border
              Slider.thumbShadow Palette.shadow
              Style.transition SliderProps.overlay tintTime
              // A white thumb doesn't lighten: its edge takes the accent instead.
              Style.on States.hover [ Slider.thumbBorder Palette.accent ]
              Style.on States.pressed [ Slider.overlay Palette.pressTint ]
              Style.on States.disabled [ Slider.overlay Color.Transparent; Slider.opacity 0.5 ]
          ]
          // Text tabs: dim until selected (the TabControl draws the accent underline) or hovered.
          TabHeader.style [] [
              TabHeader.foreground Palette.textDim
              Style.transition BorderProps.overlay tintTime
              Style.transition InheritedProps.foreground tintTime
              Style.on States.hover [ TabHeader.overlay Palette.hoverTint; TabHeader.foreground Palette.text ]
              Style.on States.isSelected [ TabHeader.foreground Palette.text ]
              Style.on States.pressed [ TabHeader.overlay Palette.pressTint ]
              Style.on States.disabled [ TabHeader.overlay Color.Transparent; TabHeader.opacity 0.5 ]
          ]
          TabControl.style [] [ TabControl.line Palette.border; TabControl.underline Palette.accent ]
          // List items (M8 step 4b, decision 15): square, transparent; hover and press tint; selected, an accent tint and
          // the accent bar at the left edge.
          ListBoxItem.style [] [
              ListBoxItem.background Color.Transparent
              ListBoxItem.indicator Palette.accent
              Style.transition BorderProps.overlay tintTime
              Style.transition BorderProps.background tintTime
              Style.on States.isSelected [ Setting.fromThemeWith BorderProps.background Palette.accent ThemeConvert.selectionTint ]
              Style.on States.hover [ ListBoxItem.overlay Palette.hoverTint ]
              Style.on States.pressed [ ListBoxItem.overlay Palette.pressTint ]
              Style.on States.disabled [ ListBoxItem.overlay Color.Transparent; ListBoxItem.opacity 0.5 ]
          ]
          TextBlock.style [ States.disabled ] [ TextBlock.opacity 0.45 ]
          // A disabled Button fades as a whole: its text doesn't fade twice.
          TextBlock.style [ Style.inside Button.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside ToggleButton.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside CheckBox.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside Switch.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside RadioButton.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside ListBoxItem.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside TabHeader.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          ScrollViewer.style [] [ ScrollViewer.thumb Palette.scrollThumb; ScrollViewer.track Palette.scrollTrack ]
          Separator.style [] [ Separator.color Palette.border ] 
          // Flyouts and tooltips float: the surface colour, a line, the theme's corners, a wide shadow.
          Flyout.style [] [
              Flyout.background Palette.surface
              Flyout.borderBrush Palette.border
              Flyout.borderThickness 1.0
              Flyout.cornerRadius Palette.cornerRadius
              Flyout.padding 4.0
              Setting.fromThemeWith BorderProps.shadow Palette.shadow ThemeConvert.floatShadow
          ]
          TooltipPresenter.style [] [
              TooltipPresenter.background Palette.surface
              TooltipPresenter.borderBrush Palette.border
              TooltipPresenter.borderThickness 1.0
              TooltipPresenter.cornerRadius Palette.cornerRadius
              TooltipPresenter.padding (8.0, 4.0)
              Setting.fromThemeWith BorderProps.shadow Palette.shadow ThemeConvert.floatShadow
          ] ]

    /// Inherited properties' base values, by theme value.
    let private bases: struct (Prop * ThemeKey)[] =
        [| struct (InheritedProps.foreground :> Prop, Palette.text :> ThemeKey)
           struct (InheritedProps.font :> Prop, Palette.font :> ThemeKey)
           struct (InheritedProps.fontSize :> Prop, Palette.fontSize :> ThemeKey)
           struct (FocusRing.colorProp :> Prop, Palette.focusRing :> ThemeKey) |]

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
        /// Its values are the theme values' defaults.
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
                  Theme.scrollTrack (Color.Black.WithAlpha 0.05f)
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
