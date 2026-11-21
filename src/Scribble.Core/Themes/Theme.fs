namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Rendering

/// The built-in looks in each visual state, and their transitions: ordinary styles that read the theme's
/// values by theme value, so every theme shares one list (themes differ only in their values). How an element
/// looks at rest is its type's defaults, by theme value too (DESIGN.md, "Where defaults live").
module internal ThemeLooks =
    /// How long hover and pressed tints fade.
    let private tintTime = TimeSpan.FromMilliseconds 100.0
    /// How long a toast takes to fade in or out (it slides for as long: `ToastPresenter`).
    let private moveTime = TimeSpan.FromMilliseconds 150.0
    /// How long a modal takes to fade in.
    let private fadeTime = TimeSpan.FromMilliseconds 120.0
    /// How long a press takes to sink in and spring back.
    let private pressTime = TimeSpan.FromMilliseconds 120.0
    /// The theme's shadow, flat (pressed or disabled).
    let private flat<'k when 'k :> Button> : Setting<'k> = Setting.fromThemeWith BorderProps.shadow Palette.shadow ThemeConvert.flatShadow

    /// Button's state looks (ToggleButton shares them): hover, pressed, disabled.
    let private buttonLook<'k when 'k :> Button> (whileOn: Setting<'k> list) : Setting<'k> list =
        [ Style.transition BorderProps.overlay tintTime
          Style.transition LayoutProps.scale pressTime
          Style.transition BorderProps.shadow pressTime
          yield! whileOn
          Style.onState States.hover [ Button.overlay Palette.hoverTint ]
          // Sinks to 97% around its center and its shadow flattens.
          Style.onState States.pressed [ Button.overlay Palette.pressTint; Button.scale 0.97; flat ]
          // Last, so it wins over hover and pressed.
          Style.onState States.disabled [ Button.overlay Color.Transparent; Button.opacity 0.5; flat ] ]

    /// CheckBox's and Switch's state looks: the accent while on, with the mark (tick, knob) in the colour that
    /// reads on it; hover and press tint the box or track.
    let private labeledLook<'k when 'k :> ToggleButton> : Setting<'k> list =
        [ Style.transition BorderProps.overlay tintTime
          Style.transition BorderProps.background tintTime
          Style.transition BorderProps.borderBrush tintTime
          Style.onState States.isChecked [ ToggleButton.background Palette.accent; ToggleButton.borderBrush Palette.accent; ToggleButton.markColor Palette.onAccent ]
          Style.onState States.isIndeterminate [ ToggleButton.background Palette.accent; ToggleButton.borderBrush Palette.accent; ToggleButton.markColor Palette.onAccent ]
          Style.onState States.hover [ ToggleButton.overlay Palette.hoverTint ]
          Style.onState States.pressed [ ToggleButton.overlay Palette.pressTint ]
          Style.onState States.disabled [ ToggleButton.overlay Color.Transparent; ToggleButton.opacity 0.5 ] ]

    let private styles =
        [ Button.style [] (buttonLook [])
          ToggleButton.style [] (
              buttonLook [
                  Style.onState States.isChecked [
                      ToggleButton.background Palette.accent
                      ToggleButton.borderBrush Palette.accent
                      ToggleButton.foreground Palette.onAccent
                  ]
              ]
          )
          CheckBox.style [] labeledLook
          Switch.style [] labeledLook
          // Chosen, it fills with the accent, as a checked CheckBox does, and its dot reads on it (2026-11-19: an accent
          // dot on the control colour was hard to see on the dark theme).
          RadioButton.style [] [
              Style.transition BorderProps.overlay tintTime
              Style.transition BorderProps.background tintTime
              Style.transition BorderProps.borderBrush tintTime
              Style.onState States.isChecked [ RadioButton.background Palette.accent; RadioButton.borderBrush Palette.accent ]
              Style.onState States.hover [ RadioButton.overlay Palette.hoverTint ]
              Style.onState States.pressed [ RadioButton.overlay Palette.pressTint ]
              Style.onState States.disabled [ RadioButton.overlay Color.Transparent; RadioButton.opacity 0.5 ]
          ]
          Slider.style [] [
              Style.transition SliderProps.overlay tintTime
              // A white thumb doesn't lighten: its edge takes the accent instead.
              Style.onState States.hover [ Slider.thumbBorder Palette.accent ]
              Style.onState States.pressed [ Slider.overlay Palette.pressTint ]
              Style.onState States.disabled [ Slider.overlay Color.Transparent; Slider.opacity 0.5 ]
          ]
          // Text tabs: dim (their type default) until selected (the TabControl draws the accent underline) or hovered.
          TabHeader.style [] [
              Style.transition BorderProps.overlay tintTime
              Style.transition InheritedProps.foreground tintTime
              Style.onState States.hover [ TabHeader.overlay Palette.hoverTint; TabHeader.foreground Palette.text ]
              Style.onState States.isSelected [ TabHeader.foreground Palette.text ]
              Style.onState States.pressed [ TabHeader.overlay Palette.pressTint ]
              Style.onState States.disabled [ TabHeader.overlay Color.Transparent; TabHeader.opacity 0.5 ]
          ]
          // List items (M8 step 4b, decision 15): hover and press tint; selected, an accent tint and
          // the accent bar at the left edge.
          ListBoxItem.style [] [
              Style.transition BorderProps.overlay tintTime
              Style.transition BorderProps.background tintTime
              Style.onState States.isSelected [ Setting.fromThemeWith BorderProps.background Palette.accent ThemeConvert.selectionTint ]
              Style.onState States.hover [ ListBoxItem.overlay Palette.hoverTint ]
              Style.onState States.pressed [ ListBoxItem.overlay Palette.pressTint ]
              Style.onState States.disabled [ ListBoxItem.overlay Color.Transparent; ListBoxItem.opacity 0.5 ]
          ]
          // Menu items (M9 step 9f): as list items under the pointer; the one the keyboard is on has the same tint.
          MenuItem.style [] [
              Style.transition BorderProps.overlay tintTime
              Style.onState States.hover [ MenuItem.overlay Palette.hoverTint ]
              Style.onState States.pressed [ MenuItem.overlay Palette.pressTint ]
              Style.onState States.disabled [ MenuItem.overlay Color.Transparent; MenuItem.opacity 0.5 ]
          ]
          // A field (M8 step 4c, decision 10): its line takes the accent while it has focus
          // or its list is open.
          ComboBox.style [] [
              Style.transition BorderProps.overlay tintTime
              Style.transition BorderProps.borderBrush tintTime
              Style.onState States.hover [ ComboBox.overlay Palette.hoverTint ]
              Style.onState States.pressed [ ComboBox.overlay Palette.pressTint ]
              Style.onState States.focused [ ComboBox.borderBrush Palette.accent ]
              Style.onState States.isOpen [ ComboBox.borderBrush Palette.accent ]
              Style.onState States.disabled [ ComboBox.overlay Color.Transparent; ComboBox.opacity 0.5 ]
          ]
          // A field to type in (M9 step 9a, decision 17): a ComboBox's state looks without the hover and press tints.
          TextBox.style [] [
              Style.transition BorderProps.borderBrush tintTime
              Style.onState States.focused [ TextBox.borderBrush Palette.accent ]
              Style.onState States.disabled [ TextBox.opacity 0.5 ]
          ]
          TextBlock.style [ Style.inside ComboBox.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [] [ Style.onState States.disabled [ TextBlock.opacity 0.45 ] ]
          // A disabled Button fades as a whole: its text doesn't fade twice.
          TextBlock.style [ Style.inside Button.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside ToggleButton.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside CheckBox.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside Switch.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside RadioButton.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside ListBoxItem.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          TextBlock.style [ Style.inside TabHeader.Type [ States.disabled ] ] [ TextBlock.opacity 1.0 ]
          // A modal (M8 step 4d): the scrim fades in with the box on it.
          Scrim.style [] [ Style.transition LayoutProps.opacity fadeTime ]
          // Toasts (M8 step 4d) fade in and out; a warning in the warning colour, a critical one in the danger colour.
          ToastPresenter.style [] [ Style.transition LayoutProps.opacity moveTime ]
          ToastPresenter.style [ Style.cls "warning" ] [
              ToastPresenter.background Palette.warning
              ToastPresenter.borderBrush Palette.warning
              ToastPresenter.foreground Palette.onAccent
          ]
          ToastPresenter.style [ Style.cls "critical" ] [
              ToastPresenter.background Palette.danger
              ToastPresenter.borderBrush Palette.danger
              ToastPresenter.foreground Palette.onAccent
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
                  Theme.textDanger (Color.Hex 0xC0392B)
                  Theme.focusRing (Color.Hex 0x2F5BD3)
                  Theme.hoverTint (Color.Black.WithAlpha 0.05f)
                  Theme.pressTint (Color.Black.WithAlpha 0.1f)
                  Theme.scrim (Color.Black.WithAlpha 0.3f)
                  Theme.scrollThumb (Color.Black.WithAlpha 0.3f)
                  Theme.scrollTrack (Color.Black.WithAlpha 0.05f)
                  Theme.shadow { Blur = 3.0; OffsetX = 0.0; OffsetY = 1.0; Spread = 0.0; Color = Color.Rgba(0.0f, 0.0f, 0.0f, 0.15f) } ]
                settings

    /// The theme hosts use when given none (one instance).
    let internal defaultTheme = lazy (Theme.dark [])

    let private current = lazy (AppValue.define "Theme" defaultTheme.Value)

    /// The built-in light theme, for an app that names no themes (one instance, made when first asked for).
    let internal defaultLight = lazy (Theme.light [])

    let private picked = lazy (StoredValue.string "Scribble.Theme" "Dark")

    type Theme with
        /// The window's theme, an app value: every layer that takes app styles (toasts, dialogs; not
        /// the inspector) and the window's background follow it. `App.theme t` sets its starting
        /// value (default `Theme.dark []`); switch from anywhere with `ctx.useAppValue Theme.current`
        /// (`.Set lightTheme`), `AppContext.Set(Theme.current, t)` or `Cmd.setAppValue Theme.current t`.
        /// A `theme` on an element applies inside it only.
        static member current: AppValue<Theme> = current.Value

        /// The name of the app's theme, a stored value: set it and the window's theme follows (`Theme.current`),
        /// and the app starts in that theme next time. The names are `App.themes`'s (`"Dark"` and `"Light"`, the
        /// built-in pair, for an app that gives none). `let picked = ctx.useStoredValue Theme.picked`, then
        /// `picked.Current` and `picked.Set "Light"`. A name the app doesn't have changes nothing.
        /// `App.rememberTheme false`: it still switches the theme, and nothing is kept.
        static member picked: AppValue<string> = picked.Value

        /// The name that means "follow the system": `picked.Set Theme.system`, and the window has the app's dark
        /// or light theme as the system is set (`App.systemThemes` says which they are; the built-in pair knows),
        /// changing with it. It does nothing in an app with themes of its own that hasn't said.
        static member system: string = "System"
