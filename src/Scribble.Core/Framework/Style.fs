namespace Scribble.Core

open System
open System.Collections.Generic

/// A property test in a selector (`Style.prop`, `Style.where`): reads the unstyled value.
[<NoEquality; NoComparison>]
type internal PropTest =
    { Prop: Prop
      Test: obj -> bool
      Text: string }

[<RequireQualifiedAccess>]
type internal Relation =
    | Inside
    | Parent

/// What a style matches: a type (exactly; none = any), classes, a name, states, and optionally an
/// ancestor (anywhere above, or the direct parent) that must match too.
[<NoEquality; NoComparison>]
type internal Selector =
    { Type: ElementType voption
      Classes: string list
      Name: string voption
      States: ElementState list
      Props: PropTest list
      Ancestor: struct (Relation * Selector) voption }

    static member Any = { Type = ValueNone; Classes = []; Name = ValueNone; States = []; Props = []; Ancestor = ValueNone }

/// One part of a style's selector: `Style.cls "primary"`, a state (`States.hover`),
/// `Style.inside Border.Type [ ... ]`.
and SelectorPart internal (apply: Selector -> Selector) =
    member internal this.Apply(s: Selector) =
        match box this with
        | :? ElementState as st -> { s with States = s.States @ [ st ] }
        | _ -> apply s

/// A state an element can be in: `States.hover`, `States.pressed`, ..., or an element type's own
/// (`States.define`, `States.ofProp`). The same value is a selector part
/// (`Button.style [ Style.cls "primary"; States.hover ] [ ... ]`) and a block
/// (`Style.on States.hover [ ... ]`, `Button.on States.hover [ ... ]`).
and [<Sealed>] ElementState internal (name: string, test: Element -> bool, prop: Prop voption, forcedValues: struct (obj * obj) voption) =
    inherit SelectorPart(id)
    // An element holds at most one inline block per state (`Button.on States.hover [ ... ]`).
    let blockProp =
        let p = Prop<StateBlock voption>("On " + name, ValueNone, Affects.Nothing, PropKind.Value)
        p.IsStateBlock <- true
        p

    member _.Name = name
    /// Forced on or off by a tool (`element.ForceState`), else whether it's on for real.
    member internal this.Test(e: Element) =
        match e.Forced |> List.tryFind (fun (struct (s, _)) -> obj.ReferenceEquals(s, this)) with
        | Some(struct (_, on)) -> on
        | None -> test e
    member internal _.BlockProp = blockProp
    /// The property it reads (`States.ofProp`), so an ancestor part testing it can be re-matched.
    member internal _.Prop = prop
    /// What render functions read from `Prop` while the state is forced on, and off (`true` and `false`
    /// for a bool state read as is), so a forced checked CheckBox draws its tick; ValueNone: none known
    /// (only styles follow).
    member internal _.ForcedValue(on: bool) =
        match forcedValues with
        | ValueSome(struct (whenOn, whenOff)) -> ValueSome(if on then whenOn else whenOff)
        | ValueNone -> ValueNone
    override _.ToString() = name

/// Settings that apply while an element is in a state: `Style.on States.hover [ ... ]` inside a
/// style, or `Button.on States.hover [ ... ]` in a view.
and [<Sealed>] internal StateBlock(state: ElementState, settings: struct (Prop * obj)[]) =
    member _.State = state
    member _.Settings = settings

    // Rebuilt views make new blocks; equal ones mustn't count as a change.
    override _.Equals(other: obj) =
        match other with
        | :? StateBlock as b ->
            obj.ReferenceEquals(state, b.State)
            && settings.Length = b.Settings.Length
            && Array.forall2 (fun (struct (p: Prop, v: obj)) (struct (q: Prop, w: obj)) -> obj.ReferenceEquals(p, q) && p.Same(v, w)) settings b.Settings
        | _ -> false

    override _.GetHashCode() = state.Name.GetHashCode() + settings.Length

/// The built-in states, and `States.define` for an element type's own.
module States =
    /// The pointer is over the element or something inside it.
    let hover = ElementState("hover", (fun e -> e.IsPointerOver || e.HasForcedHoverInside), ValueNone, ValueNone)
    /// Pressed with the left button (and the pointer still over it), or Space held while focused.
    let pressed = ElementState("pressed", (fun e -> e.IsPressed), ValueNone, ValueNone)
    /// Has keyboard focus.
    let focused = ElementState("focus", (fun e -> e.IsFocused), ValueNone, ValueNone)
    /// Has focus and shows it (the keyboard is in use).
    let focusVisible = ElementState("focus-visible", (fun e -> e.IsFocusVisible), ValueNone, ValueNone)
    /// `isEnabled false` on the element or one of its ancestors.
    let disabled = ElementState("disabled", (fun e -> e.LooksDisabled), ValueNone, ValueNone)

    // Every custom state made, in order, for `element.ActiveStates` (states are usually made at
    // module level, possibly on several threads at once).
    let private custom = System.Collections.Generic.List<ElementState>()

    let private register (s: ElementState) =
        lock custom (fun () -> custom.Add s)
        s

    /// Every custom state made so far (`define`, `ofProp`), in order (a copy; for tools).
    let customStates () = lock custom (fun () -> custom.ToArray())

    /// Forgets a custom state (tests that make their own, so they don't show in other tests).
    let internal remove (s: ElementState) = lock custom (fun () -> custom.Remove s |> ignore)

    /// An element type's own state, tested by any function. Call `element.RefreshStates()` when it
    /// may have changed, so styles are matched again. For a state that only reads a property, use
    /// `ofProp` (it is matched again by itself).
    let define (name: string) (test: Element -> bool) = register (ElementState(name, test, ValueNone, ValueNone))

    /// A state read from a property (M8's CheckBox: `States.ofProp "checked" CheckBoxProps.isChecked id`):
    /// the element is in it while `test` passes for its own value (else its default; never a style's,
    /// so a style can't switch its own state). Matched again whenever that value changes.
    let ofProp (name: string) (prop: Prop<'T>) (test: 'T -> bool) =
        prop.TestedByStyles <- true
        // Forced, render functions read `true` (on) or `false` (off) for a bool property whose test
        // passes for `true` only.
        let forcedValues =
            match box test with
            | :? (bool -> bool) as t when t true && not (t false) -> ValueSome(struct (box true, box false))
            | _ -> ValueNone
        register (ElementState(name, (fun e -> test (unbox<'T> (e.UnstyledValue prop))), ValueSome(prop :> Prop), forcedValues))

    /// A ToggleButton (CheckBox, Switch) that is on (`isChecked`; F# reserves the word `checked`).
    let isChecked = ofProp "checked" ToggleButtonProps.isChecked id
    /// A CheckBox showing its "partly checked" dash.
    let isIndeterminate = ofProp "indeterminate" ToggleButtonProps.isIndeterminate id
    /// A TabControl's header for the selected tab, a ListBox's selected item.
    let isSelected = ofProp "selected" TabHeaderProps.isSelected id
    /// A ComboBox whose list is open.
    let isOpen = ofProp "open" ComboBoxOpenProps.isOpen id

    /// The built-in states, in the order tools show them.
    let internal builtIn = [| hover; pressed; focused; focusVisible; disabled |]

[<AutoOpen>]
module ElementStateExtensions =
    type Element with
        /// The states the element is in right now, built-in and custom (every `States.define` and
        /// `States.ofProp` made so far), for tools such as the inspector. Tests every state, so don't
        /// call it per frame. A custom state whose test throws for this element is left out.
        member this.ActiveStates: ElementState list =
            [ for s in States.builtIn do
                  if s.Test this then s
              for s in States.customStates () do
                  let on =
                      try
                          s.Test this
                      with _ ->
                          false
                  if on then s ]

        /// The states forced by `ForceState`, each with whether it's forced on (true) or off (false).
        member this.ForcedStates: (ElementState * bool) list = [ for struct (s, on) in this.Forced -> (s :?> ElementState), on ]

        /// Forces a state on or off (`on` false: as if the element weren't in it), for tools such as the
        /// inspector: styles match as if it were so and render functions see it (`ctx.IsPointerOver`,
        /// a bool property behind the state), but input doesn't (a forced `pressed` doesn't click, a
        /// forced `disabled` doesn't block, a forced-off one doesn't unblock). Like the real states, a
        /// forced `disabled` (on or off) reaches everything inside the element, except what has its own
        /// `isEnabled false` under a forced-off one, and a forced-on `hover` also hovers its ancestors;
        /// the rest apply to this element only. `UnforceState` stops forcing it.
        member this.ForceState(state: ElementState, on: bool) =
            let others = this.ForcedStates |> List.filter (fun (s, _) -> not (obj.ReferenceEquals(s, state)))
            this.SetForcedStates(others @ [ state, on ])

        /// Stops forcing a state: it follows the element again.
        member this.UnforceState(state: ElementState) =
            this.SetForcedStates(this.ForcedStates |> List.filter (fun (s, _) -> not (obj.ReferenceEquals(s, state))))

        member private this.SetForcedStates(states: (ElementState * bool) list) =
            let flag (s: ElementState) =
                if obj.ReferenceEquals(s, States.hover) then 1
                elif obj.ReferenceEquals(s, States.pressed) then 2
                elif obj.ReferenceEquals(s, States.focused) then 4
                elif obj.ReferenceEquals(s, States.focusVisible) then 8
                elif obj.ReferenceEquals(s, States.disabled) then 16
                else 0
            let values = [ for s, on in states do match s.Prop, s.ForcedValue on with ValueSome p, ValueSome v -> struct (p, v) | _ -> () ]
            let flags on = states |> List.sumBy (fun (s, o) -> if o = on then flag s else 0)
            this.SetForced([ for s, on in states -> struct (box s, on) ], flags true, flags false, values)

        /// Stops forcing every state.
        member this.ClearForcedStates() = if not this.Forced.IsEmpty then this.SetForced([], 0, 0, [])


module internal Selector =
    let build (t: ElementType voption) (parts: SelectorPart list) =
        parts |> List.fold (fun s (p: SelectorPart) -> p.Apply s) { Selector.Any with Type = t }

    let rec matches (s: Selector) (e: Element) =
        (match s.Type with
         | ValueSome t -> obj.ReferenceEquals(e.Type, t)
         | ValueNone -> true)
        && (match s.Name with
            | ValueSome n -> e.Name = n
            | ValueNone -> true)
        && (s.Classes.IsEmpty
            || (let classes = e.Get LayoutProps.classes
                s.Classes |> List.forall (fun c -> classes |> List.exists (fun x -> String.Equals(x, c, StringComparison.Ordinal)))))
        && s.States |> List.forall (fun st -> st.Test e)
        && s.Props |> List.forall (fun t -> t.Test(e.UnstyledValue t.Prop))
        && match s.Ancestor with
           | ValueNone -> true
           | ValueSome(struct (Relation.Parent, a)) ->
               match e.StyleParent with
               | ValueSome p -> matches a p
               | ValueNone -> false
           | ValueSome(struct (Relation.Inside, a)) ->
               let mutable p = e.StyleParent
               let mutable found = false
               while not found && p.IsSome do
                   found <- matches a p.Value
                   p <- p.Value.StyleParent
               found

    /// True when an ancestor part tests a state: a state change must then re-match the subtree.
    let rec ancestorStates (s: Selector) =
        match s.Ancestor with
        | ValueSome(struct (_, a)) -> not a.States.IsEmpty || ancestorStates a
        | ValueNone -> false

    /// Marks the properties an ancestor part tests: changing one must re-match the subtree.
    let rec markAncestorProps (s: Selector) =
        for t in s.Props do
            t.Prop.TestedByAncestorStyles <- true
        for st in s.States do
            match st.Prop with
            | ValueSome p -> p.TestedByAncestorStyles <- true
            | ValueNone -> ()
        match s.Ancestor with
        | ValueSome(struct (_, a)) -> markAncestorProps a
        | ValueNone -> ()

    /// A value in selector text. Only simple values are written out: `string`/`ToString()` on F#
    /// records and unions uses `%A`, which crashes under NativeAOT.
    let valueText (v: obj) =
        match v with
        | :? bool as b -> if b then "true" else "false"
        | :? string as t -> "\"" + t + "\""
        | :? Enum as x -> x.ToString()
        | :? IFormattable as f when v.GetType().IsPrimitive || (v :? decimal) ->
            f.ToString(null, Globalization.CultureInfo.InvariantCulture)
        | _ -> "…"

    /// CSS-like text for the inspector: `Border.card > Button.primary:hover`, `CheckBox[IsChecked=true]`.
    let rec text (s: Selector) =
        let own =
            (match s.Type with
             | ValueSome t -> t.Name
             | ValueNone -> if s.Classes.IsEmpty && s.Name.IsNone && s.States.IsEmpty && s.Props.IsEmpty then "*" else "")
            + String.Join("", s.Classes |> List.map (fun c -> "." + c))
            + (match s.Name with
               | ValueSome n -> "#" + n
               | ValueNone -> "")
            + String.Join("", s.Props |> List.map _.Text)
            + String.Join("", s.States |> List.map (fun st -> ":" + st.Name))
        match s.Ancestor with
        | ValueSome(struct (Relation.Parent, a)) -> text a + " > " + own
        | ValueSome(struct (Relation.Inside, a)) -> text a + " " + own
        | ValueNone -> own

/// One selector with the values it sets (a style is one or more: its base and its state blocks).
[<NoEquality; NoComparison>]
type internal Rule =
    { Selector: Selector
      Keys: Prop[]
      Values: obj[]
      Text: string
      AncestorStates: bool }

module internal Rule =
    /// Styles set how things look and behave, never app state (DESIGN.md, "M8 step 4", round 1f): a style has no model,
    /// no `dispatch` and no idea which element it's on. Nor what decides matching (classes, name, key, theme, styles).
    let check (p: Prop) =
        if p.IsHandler then
            invalidArg "settings" ("Styles can't set " + p.Name + ": handlers come from views, where the model is.")
        elif p.Kind <> PropKind.Value || p.IsAppState then
            invalidArg "settings" ("Styles can't set " + p.Name + ": content and app state come from views, where the model is.")
        elif p.NotStylable || p.IsStyles || p.IsLocal then
            invalidArg "settings" ("Styles can't set " + p.Name + " (classes, styles, name, key and theme decide what styles match; set them in views).")

    /// The rules for a selector and its settings: the plain settings, then each state block's
    /// (nested blocks add their state to the selector), in order, so later ones win.
    let rec flatten (selector: Selector) (prefix: string) (settings: struct (Prop * obj)[]) (into: List<Rule>) =
        let plain = settings |> Array.filter (fun struct (p, _) -> not p.IsStateBlock)
        for struct (p, _) in plain do
            check p
        if plain.Length > 0 then
            into.Add
                { Selector = selector
                  Keys = plain |> Array.map (fun struct (p, _) -> p)
                  Values = plain |> Array.map (fun struct (_, v) -> v)
                  Text = prefix + Selector.text selector
                  AncestorStates = Selector.ancestorStates selector }
        for struct (p, v) in settings do
            if p.IsStateBlock then
                match v with
                | :? (StateBlock voption) as b when b.IsSome ->
                    flatten { selector with States = selector.States @ [ b.Value.State ] } prefix b.Value.Settings into
                | _ -> ()

/// A style: a selector and the settings it gives every element it matches. Made with an element
/// module's `style` (`Button.style [ Style.cls "primary"; States.hover ] [ Button.background c ]`) or
/// `Style.any`; used in `App.styles [ ... ]` or an element's `styles` setting. Compared by
/// reference: define styles once, at module level.
[<Sealed>]
type Style internal (rules: Rule[], description: string) =
    member internal _.Rules = rules
    /// The selector written out, CSS-like (`Button.primary:hover`).
    member _.Description = description
    override _.ToString() = description

    static member internal Create(selector: Selector, settings: struct (Prop * obj)[]) =
        let rules = List<Rule>()
        Rule.flatten selector "" settings rules
        Style(rules.ToArray(), Selector.text selector)

    /// A style for elements of type `elementType` (exactly that type). Element modules wrap it as
    /// their `style`: `let style parts (settings: Setting<ProgressBar> list) = Style.forType Type parts settings`.
    static member forType<'k> (elementType: ElementType) (parts: SelectorPart list) (settings: Setting<'k> list) : Style =
        Style.Create(Selector.build (ValueSome elementType) parts, [| for s in settings -> struct (s.Prop, s.Value) |])

    /// Elements with this class (`X.classes [ "primary" ]`; all the classes given must be there;
    /// names are case-sensitive).
    static member cls(value: string) = SelectorPart(fun s -> { s with Classes = s.Classes @ [ value ] })
    /// The element named `value` (`Layoutable.name`).
    static member name(value: string) = SelectorPart(fun s -> { s with Name = ValueSome value })

    /// Elements whose own value of `prop` (else its default; never a style's) equals `value`:
    /// `CheckBox.style [ Style.prop CheckBoxProps.isChecked true ] [ ... ]`. Matched again when it changes.
    static member prop<'T when 'T: equality> (prop: Prop<'T>) (value: 'T) : SelectorPart =
        prop.TestedByStyles <- true
        let test =
            { Prop = prop
              Test = (fun v -> unbox<'T> v = value)
              Text = "[" + prop.Name + "=" + Selector.valueText (box value) + "]" }
        SelectorPart(fun s -> { s with Props = s.Props @ [ test ] })

    /// Elements whose own value of `prop` (else its default; never a style's) passes `test`:
    /// `TextBlock.style [ Style.where TextBlockProps.fontSize (fun s -> s >= 20.0) ] [ ... ]`.
    static member where<'T> (prop: Prop<'T>) (test: 'T -> bool) : SelectorPart =
        prop.TestedByStyles <- true
        let t = { Prop = prop; Test = (fun v -> test (unbox<'T> v)); Text = "[" + prop.Name + "?]" }
        SelectorPart(fun s -> { s with Props = s.Props @ [ t ] })

    /// Only inside an element of `elementType` matching `parts` (anywhere above):
    /// `TextBlock.style [ Style.inside Border.Type [ Style.cls "card" ] ] [ ... ]`.
    static member inside (elementType: ElementType) (parts: SelectorPart list) =
        let a = Selector.build (ValueSome elementType) parts
        Selector.markAncestorProps a
        SelectorPart(fun s -> { s with Ancestor = ValueSome(struct (Relation.Inside, a)) })

    /// Only when the direct parent is an element of `elementType` matching `parts`.
    static member parent (elementType: ElementType) (parts: SelectorPart list) =
        let a = Selector.build (ValueSome elementType) parts
        Selector.markAncestorProps a
        SelectorPart(fun s -> { s with Ancestor = ValueSome(struct (Relation.Parent, a)) })

    /// Settings that apply while the element is also in `state`, inside a style's settings:
    /// `Button.style [ Style.cls "primary" ] [ Button.background c; Style.on States.hover [ ... ] ]`.
    /// Blocks nest (hover inside focused: both). In a view, write `Button.on` (the same thing).
    static member on<'k> (state: ElementState) (settings: Setting<'k> list) : Setting<'k> =
        // A state block is a style, in a style or a view: checked when made, like one.
        for s in settings do
            if not s.Prop.IsStateBlock then Rule.check s.Prop
        Setting.create state.BlockProp (ValueSome(StateBlock(state, [| for s in settings -> struct (s.Prop, s.Value) |])))

    /// Fades `prop` from its old value to its new one over `duration` whenever it changes (not when
    /// the element first appears), inside a style's settings:
    /// `Style.transition BorderProps.overlay (TimeSpan.FromMilliseconds 100.0)`. Only properties
    /// that affect drawing alone (colors, numbers, shadows, corner radius, `opacity`, `scale`).
    static member transition<'k, 'T> (prop: Prop<'T>) (duration: TimeSpan) : Setting<'k> =
        if prop.Kind <> PropKind.Value || prop.Lerp.IsNone || (prop.Affects <> Affects.Render && prop.Affects <> Affects.Nothing) then
            invalidArg "prop" (prop.Name + " can't have a transition: only properties that affect drawing alone (colors, numbers, shadows, corner radius) can.")
        Setting.create prop.TransitionProp (ValueSome duration)

module StyleProps =
    /// Styles for the element and everything inside it.
    let styles =
        let p = Prop<Style list>("Styles", [], Affects.Nothing, PropKind.Value)
        p.IsStyles <- true
        p.NotStylable <- true
        p

/// One setting for a theme: `Theme.dark [ Theme.accent c; Theme.set cardLine c ]`.
[<Sealed>]
type ThemeSetting internal (apply: Dictionary<int, obj> -> unit) =
    member internal _.Apply(values: Dictionary<int, obj>) = apply values

/// A theme: values for theme values (`Palette.surface`, an app's own), the built-in looks as
/// styles below every other style, and base text values. Made with `Theme.dark [ ... ]` or
/// `Theme.light [ ... ]`; used with `App.theme`, or a `theme` setting on any element (on the app
/// view's root it applies to the whole window). Settings read its values by theme value
/// (`Border.background Palette.surface`); code reads one with `theme.Get Palette.surface`. Build a
/// theme once and keep it.
[<Sealed>]
type Theme internal (values: Dictionary<int, obj>, looks: Lazy<Style list * struct (Prop * ThemeKey)[]>) =
    /// A theme value's value in this theme (its default when the theme doesn't set it).
    member _.Get<'T>(themeValue: ThemeValue<'T>) : 'T =
        match values.TryGetValue themeValue.Id with
        | true, v -> unbox<'T> v
        | _ -> themeValue.Default

    /// The built-in looks (matched below every other style).
    member internal _.Styles: Style list = fst looks.Value

    interface IThemeSource with
        member _.TryBase(prop: Prop) =
            let bases = snd looks.Value
            let mutable i = 0
            while i < bases.Length && (let struct (p, _) = bases[i] in p.Id <> prop.Id) do
                i <- i + 1
            if i < bases.Length then
                let struct (_, themeValue) = bases[i]
                match values.TryGetValue themeValue.Id with
                | true, v -> ValueSome v
                | _ -> ValueSome themeValue.DefaultBoxed
            else ValueNone
        member _.Resolve(themeValue: ThemeKey) =
            match values.TryGetValue themeValue.Id with
            | true, v -> v
            | _ -> themeValue.DefaultBoxed

    /// Sets a theme value's value: a built-in one (`Theme.set Palette.accent c`) or the app's own.
    static member set<'T> (themeValue: ThemeValue<'T>) (value: 'T) = ThemeSetting(fun d -> d[themeValue.Id] <- box value)
    static member background(value: Scribble.Rendering.Color) = Theme.set Palette.background value
    static member surface(value: Scribble.Rendering.Color) = Theme.set Palette.surface value
    static member control(value: Scribble.Rendering.Color) = Theme.set Palette.control value
    static member border(value: Scribble.Rendering.Color) = Theme.set Palette.border value
    static member text(value: Scribble.Rendering.Color) = Theme.set Palette.text value
    static member textDim(value: Scribble.Rendering.Color) = Theme.set Palette.textDim value
    static member accent(value: Scribble.Rendering.Color) = Theme.set Palette.accent value
    static member danger(value: Scribble.Rendering.Color) = Theme.set Palette.danger value
    static member onAccent(value: Scribble.Rendering.Color) = Theme.set Palette.onAccent value
    static member focusRing(value: Scribble.Rendering.Color) = Theme.set Palette.focusRing value
    static member hoverTint(value: Scribble.Rendering.Color) = Theme.set Palette.hoverTint value
    static member pressTint(value: Scribble.Rendering.Color) = Theme.set Palette.pressTint value
    static member scrim(value: Scribble.Rendering.Color) = Theme.set Palette.scrim value
    static member scrollThumb(value: Scribble.Rendering.Color) = Theme.set Palette.scrollThumb value
    static member scrollTrack(value: Scribble.Rendering.Color) = Theme.set Palette.scrollTrack value
    static member shadow(value: BoxShadow) = Theme.set Palette.shadow value
    static member cornerRadius(value: float) = Theme.set Palette.cornerRadius value
    static member font(value: Scribble.Text.Font) = Theme.set Palette.font value
    static member fontSize(value: float) = Theme.set Palette.fontSize value

/// Matches styles to elements (ElementTree runs it at the start of layout).
module internal StyleEngine =
    /// The `styles` lists of the element's ancestors, outermost first.
    let scopesAbove (e: Element) =
        let mutable scopes = []
        let mutable p = e.StyleParent
        while p.IsSome do
            let s = p.Value.Get StyleProps.styles
            if not s.IsEmpty then scopes <- s :: scopes
            p <- p.Value.StyleParent
        scopes

    /// The styles of the theme that applies to `e`: the nearest `theme` setting (on it or above),
    /// else the tree's.
    let themeStyles (treeTheme: Theme voption) (e: Element) : Style list =
        let mutable found: Theme voption = ValueNone
        let mutable p = ValueSome e
        while found.IsNone && p.IsSome do
            match p.Value.UnstyledValue LayoutProps.theme with
            | :? (IThemeSource voption) as t when t.IsSome ->
                match t.Value with
                | :? Theme as th -> found <- ValueSome th
                | _ -> ()
            | _ -> ()
            p <- p.Value.StyleParent
        match found, treeTheme with
        | ValueSome t, _
        | ValueNone, ValueSome t -> t.Styles
        | ValueNone, ValueNone -> []

    /// Matches one element: its theme's styles, then the app's, then the ancestors' (outermost
    /// first), then its own `styles`, then its inline blocks; later wins. Returns true when a rule
    /// tested an ancestor's state.
    let apply (treeTheme: Theme voption) (appStyles: Style list) (scopes: Style list list) (e: Element) =
        let keys = List<Prop>()
        let values = List<obj>()
        let selectors = List<string>()
        let index = Dictionary<int, int>()
        let mutable ancestorStates = false
        let take (r: Rule) =
            if r.AncestorStates then ancestorStates <- true
            if Selector.matches r.Selector e then
                for i in 0 .. r.Keys.Length - 1 do
                    let p = r.Keys[i]
                    match index.TryGetValue p.Id with
                    | true, j ->
                        values[j] <- r.Values[i]
                        selectors[j] <- r.Text
                    | _ ->
                        index[p.Id] <- keys.Count
                        keys.Add p
                        values.Add r.Values[i]
                        selectors.Add r.Text
        let takeAll (styles: Style list) =
            for s in styles do
                for r in s.Rules do
                    take r
        let themed = themeStyles treeTheme e
        if not themed.IsEmpty then
            takeAll themed
            // Mark where these came from (`Values` reports them as ValueSource.Theme).
            for i in 0 .. selectors.Count - 1 do
                selectors[i] <- ThemeMark + selectors[i]
        takeAll appStyles
        for scope in scopes do
            takeAll scope
        let own = e.Get StyleProps.styles
        takeAll own
        // Inline blocks: rules for this element alone.
        for struct (p, v) in e.StyleSettings do
            if p.IsStateBlock then
                let rules = List<Rule>()
                Rule.flatten Selector.Any "inline " [| struct (p, v) |] rules
                for r in rules do
                    take r
        e.ApplyStyleValues(keys.ToArray(), values.ToArray(), selectors.ToArray())
        ancestorStates

    /// The tree's theme (as set on the element tree), if any.
    let treeTheme (e: Element) : Theme voption =
        match e.Root with
        | ValueSome r ->
            match r.Theme with
            | ValueSome(:? Theme as t) -> ValueSome t
            | _ -> ValueNone
        | ValueNone -> ValueNone

    /// Matches an element and everything inside it (their queued requests are done too).
    let rec applyDeep (treeTheme: Theme voption) (appStyles: Style list) (scopes: Style list list) (e: Element) =
        e.TakeRestyle() |> ignore
        let mutable found = apply treeTheme appStyles scopes e
        let own = e.Get StyleProps.styles
        let inner = if own.IsEmpty then scopes else scopes @ [ own ]
        for c in e.Children do
            if applyDeep treeTheme appStyles inner c then found <- true
        found

/// A property the element's theme can set on it, and the rules that set it (`Button:hover`).
type ThemedProperty = { Prop: Prop; Selectors: string list }

[<AutoOpen>]
module ThemedPropertyExtensions =
    type Element with
        /// The properties the element's theme (the nearest `theme`, else the tree's) can set on its
        /// type, in any state or class, with the rules that set them, for tools such as the
        /// inspector. Walks the theme's styles, so don't call it per frame.
        member this.ThemedProperties: ThemedProperty list =
            let order = List<Prop>()
            let selectors = Dictionary<int, List<string>>()
            for s in StyleEngine.themeStyles (StyleEngine.treeTheme this) this do
                for r in s.Rules do
                    let forType =
                        match r.Selector.Type with
                        | ValueSome t -> obj.ReferenceEquals(t, this.Type)
                        | ValueNone -> true
                    if forType then
                        for p in r.Keys do
                            match selectors.TryGetValue p.Id with
                            | true, l -> if not (l.Contains r.Text) then l.Add r.Text
                            | _ ->
                                order.Add p
                                selectors[p.Id] <- List<string>([ r.Text ])
            [ for p in order -> { Prop = p; Selectors = List.ofSeq selectors[p.Id] } ]
