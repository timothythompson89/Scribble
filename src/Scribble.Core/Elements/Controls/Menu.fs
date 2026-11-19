namespace Scribble.Core

open System
open Scribble.Accessibility
open Scribble.Rendering

module MenuItemProps =
    /// A picture before the text.
    let icon: Prop<Icon voption> = Prop.define "MenuItemIcon" ValueNone Affects.Measure
    /// Shown, dim, at the item's right ("Ctrl+C"): text only.
    let shortcut = Prop.define "MenuItemShortcut" "" Affects.Measure
    /// Given: the item can be checked, and shows a check mark while it is.
    let isChecked: Prop<bool voption> = Prop.define "MenuItemChecked" ValueNone Affects.Render |> AppState.mark
    let shortcutForeground = Prop.define "MenuItemShortcutForeground" Color.Transparent Affects.Render
    /// Behind the item the keyboard is on.
    let highlight = Prop.define "MenuItemHighlight" Color.Transparent Affects.Render
    /// Set by the Menu: some item has an icon or a check mark, so every item leaves room for one.
    let internal leading = Prop.local "MenuItemLeading" false
    /// Set by the Menu: it is a row (`Menu.orientation Horizontal`), so the item shows its text alone.
    let internal inRow = Prop.local "MenuItemInRow" false

/// One command in a Menu (DESIGN.md, "9f: the Menu"):
///
///     MenuItem.create [
///         MenuItem.text "Copy"
///         MenuItem.icon Material.contentCopy
///         MenuItem.shortcut "Ctrl+C"                  // shown at the right; it registers no key
///         MenuItem.isEnabled model.HasSelection
///         MenuItem.onClick (fun _ -> dispatch Copy)   // then the menu closes
///     ]
///
/// `MenuItem.isChecked value` makes it one that is on or off: a check mark shows while it is on, and the click
/// handler changes the app's value.
[<AbstractClass>]
type MenuItem =
    inherit Border
    static member text<'k when 'k :> MenuItem>(value: string) : Setting<'k> = Setting.create TextProps.text (if isNull value then "" else value)
    /// A picture before the text.
    static member icon<'k when 'k :> MenuItem>(value: Icon) : Setting<'k> = Setting.create MenuItemProps.icon (ValueSome value)
    /// Text shown, dim, at the item's right ("Ctrl+C"). It tells the user of a key the app handles elsewhere:
    /// the menu registers none.
    static member shortcut<'k when 'k :> MenuItem>(value: string) : Setting<'k> = Setting.create MenuItemProps.shortcut (if isNull value then "" else value)
    /// The item is one that is on or off, and this is which: a check mark shows while it is on.
    static member isChecked<'k when 'k :> MenuItem>(value: bool) : Setting<'k> = Setting.create MenuItemProps.isChecked (ValueSome value)
    /// The shortcut text's colour (the theme's dim text).
    static member shortcutForeground<'k when 'k :> MenuItem>(value: Color) : Setting<'k> = Setting.create MenuItemProps.shortcutForeground value
    static member shortcutForeground<'k when 'k :> MenuItem>(themeValue: ThemeValue<Color>) : Setting<'k> =
        Setting.fromTheme MenuItemProps.shortcutForeground themeValue
    /// The colour behind the item the keyboard is on (the theme's hover tint).
    static member highlight<'k when 'k :> MenuItem>(value: Color) : Setting<'k> = Setting.create MenuItemProps.highlight value
    static member highlight<'k when 'k :> MenuItem>(themeValue: ThemeValue<Color>) : Setting<'k> = Setting.fromTheme MenuItemProps.highlight themeValue

/// What a Menu and its items share.
module internal Menus =
    /// On an element around a menu that isn't a flyout's: called when an item is chosen (the menu should go).
    let onClose: Prop<(unit -> unit) voption> = Prop.define "MenuOnClose" ValueNone Affects.Nothing

    /// Closes the menu `e` is in: the flyout whose layer it is in is asked to close (its element's own, or the
    /// app's `isOpen`).
    let close (e: Element) =
        // Shown by an element in a layer of its own (a TextBox's row): whoever shows it hears.
        let mutable a = ValueSome e
        while a.IsSome do
            match a.Value.Get onClose with
            | ValueSome f ->
                try
                    f ()
                with ex ->
                    e.ReportError ex
            | ValueNone -> ()
            a <- a.Value.Parent
        match e.Root with
        | ValueSome(:? ElementTree as tree) ->
            match tree.Owner with
            | ValueSome anchor -> Flyouts.request anchor false
            | ValueNone -> ()
        | _ -> ()

module MenuItem =
    /// The icon's or check mark's size, and the space after it and before a shortcut.
    let private iconSize = 16.0
    let private iconGap = 10.0
    let private shortcutGap = 28.0

    /// A check mark, in a 24 × 24 box.
    let private check = Icon.fromPath "M9 16.17 4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z" [ Icon.name "check" ]

    /// What stands before the text: room for an icon or a check mark (0: none), and the gap after it. In a row
    /// (DESIGN.md, "9g", decision 94) an item shows its text alone: a check mark only if it can be checked, its
    /// icon only if it has no text.
    let private leadingOf (e: Element) : struct (float * float) =
        if e.Get MenuItemProps.inRow then
            let noText = e.Get TextProps.text = ""
            if (e.Get MenuItemProps.isChecked).IsSome || (noText && (e.Get MenuItemProps.icon).IsSome) then
                struct (iconSize, (if noText then 0.0 else iconGap))
            else struct (0.0, 0.0)
        elif e.Get MenuItemProps.leading then struct (iconSize, iconGap)
        else struct (0.0, 0.0)

    let private measure (ctx: MeasureContext) (_: Size) =
        let ix, iy = Border.inset ctx
        let font, size = ctx.Get TextProps.font, ctx.Get TextProps.fontSize
        let text = ctx.Text.Measure(ctx.Get TextProps.text, font, size)
        let shortcut = if ctx.Get MenuItemProps.inRow then "" else ctx.Get MenuItemProps.shortcut
        let shortcutWidth = if shortcut = "" then 0.0 else shortcutGap + ctx.Text.Measure(shortcut, font, size).Width
        let struct (mark, gap) = leadingOf ctx.Element
        Size.Create(Math.Ceiling(ix + mark + gap + text.Width + shortcutWidth), Math.Ceiling(max text.LineHeight iconSize + iy))

    let private render (ctx: RenderContext) (list: DrawList) =
        Border.draw ctx list
        let size = ctx.Size
        let radius = ctx.Get BorderProps.cornerRadius
        // The item the keyboard is on (the pointer's is the theme's hover tint, as on a list's item).
        if ctx.IsFocusVisible then list.FillRoundedRect(Rect.Create(0.0, 0.0, size.Width, size.Height), radius, ctx.Get MenuItemProps.highlight)
        let p, t = ctx.Get BorderProps.padding, ctx.Get BorderProps.borderThickness
        let font, fontSize = ctx.Get TextProps.font, ctx.Get TextProps.fontSize
        let foreground = ctx.Get TextProps.foreground
        let lineHeight = ctx.Text.LineHeight(font, fontSize)
        let left = p.Left + t
        let inner = size.Height - p.Vertical - 2.0 * t
        let mutable x = left
        let struct (mark, gap) = leadingOf ctx.Element
        if mark > 0.0 then
            let y = Math.Floor(p.Top + t + (inner - iconSize) / 2.0 + 0.5)
            // A checked item's mark stands where an icon would; an item that can't be checked shows its icon.
            match ctx.Get MenuItemProps.isChecked, ctx.Get MenuItemProps.icon with
            | ValueSome true, _ -> ctx.Text.DrawIcon(list, Icon.geometry check, check.ViewBox, 0.0, false, false, x, y, iconSize, foreground)
            | ValueSome false, _ -> ()
            | ValueNone, ValueSome icon -> ctx.Text.DrawIcon(list, Icon.geometry icon, icon.ViewBox, 0.0, false, false, x, y, iconSize, foreground)
            | ValueNone, ValueNone -> ()
            x <- x + mark + gap
        let top = Math.Floor(p.Top + t + (inner - lineHeight) / 2.0 + 0.5)
        ctx.Text.Draw(list, ctx.Get TextProps.text, font, fontSize, x, top, foreground)
        let shortcut = if ctx.Get MenuItemProps.inRow then "" else ctx.Get MenuItemProps.shortcut
        if shortcut <> "" then
            let width = ctx.Text.Measure(shortcut, font, fontSize).Width
            ctx.Text.Draw(list, shortcut, font, fontSize, Math.Floor(size.Width - p.Right - t - width), top, ctx.Get MenuItemProps.shortcutForeground)

    let Type =
        Element.extend Border.Type "MenuItem" [
            Element.measure measure
            Element.render render
            // After the app's `onClick`: choosing an item closes the menu.
            Element.click (fun ctx _ -> Menus.close ctx.Element)
            Element.defaults [
                // Tab stops, so the focus goes to the first when the menu opens (Tab itself closes the menu).
                Layoutable.focusable true
                Border.padding (10.0, 6.0)
                // Half the theme's corners, as other small parts (a CheckBox's box).
                Setting.fromThemeWith BorderProps.cornerRadius Palette.cornerRadius ThemeConvert.halfCorners
                Layoutable.foreground Palette.text
                MenuItem.shortcutForeground Palette.textDim
                MenuItem.highlight Palette.hoverTint
                Semantics.role Role.MenuItem
                Semantics.labelFrom TextProps.text
                Setting.create SemanticsProps.isChecked (
                    Read([| MenuItemProps.isChecked |], fun e ->
                        match e.Get MenuItemProps.isChecked with
                        | ValueSome true -> ValueSome CheckState.On
                        | ValueSome false -> ValueSome CheckState.Off
                        | ValueNone -> ValueNone))
            ]
        ]

    let create (settings: Setting<MenuItem> list) : View = View.Create(Type, settings)
    /// A style for MenuItems: `MenuItem.style [ States.hover ] [ MenuItem.background c ]`.
    let style (parts: SelectorPart list) (settings: Setting<MenuItem> list) : Style = Style.forType Type parts settings

/// What a Menu keeps: the letters typed into it.
[<Sealed; AllowNullLiteral>]
type internal MenuState() =
    member val Typeahead = TypeaheadState() with get

/// A list of commands, shown as an element's flyout or as its context menu (DESIGN.md, "9f: the Menu"):
///
///     let menu =
///         Menu.create [
///             Menu.children [
///                 MenuItem.create [ MenuItem.text "Copy"; MenuItem.shortcut "Ctrl+C"; MenuItem.onClick (fun _ -> copy ()) ]
///                 MenuItem.create [ MenuItem.text "Word wrap"; MenuItem.isChecked model.Wrap; MenuItem.onClick (fun _ -> toggleWrap ()) ]
///                 Menu.separator
///                 MenuItem.create [ MenuItem.text "Delete"; MenuItem.isEnabled model.CanDelete; MenuItem.onClick (fun _ -> delete ()) ]
///             ]
///         ]
///
///     Button.create [ Button.text "Actions"; Button.flyout menu ]       // opens on a click, as any flyout
///     TextBlock.create [ TextBlock.text "Right-click me"; TextBlock.contextMenu menu ]
///
/// A context menu opens at the pointer on a right-click, at the element on the Menu key or Shift+F10, and on a long
/// press with a finger. In a menu ↑/↓ move through the items (round the ends; disabled ones are skipped), Home and
/// End go to the first and last, Enter or Space chooses, and a typed letter goes to the item that starts with it.
/// Choosing an item runs its `onClick` and closes the menu; Escape, Tab or a press elsewhere closes it with
/// nothing chosen. No submenus yet. `Menu.orientation Horizontal` makes it a row (with `Menu.minWidth 0.0`, or it
/// keeps a menu's least width): each item shows its text alone (its icon if it has no text, a check mark if it can
/// be checked, no shortcut), the separators stand upright, and ←/→ move through it. A TextBox shows its context
/// menu so for a finger (DESIGN.md, "9g"). A Menu is a panel: its items are its `children` (`MenuItem.create [ ... ]`,
/// `Menu.separator`, or any view), and `Menu.items source key view` makes them from a list, as for any panel.
[<AbstractClass>]
type Menu =
    inherit StackPanel
    /// A line between groups of items.
    static member separator: View = Separator.create [ Separator.margin (0.0, 4.0) ]

module Menu =
    let private state: Prop<MenuState> = Prop.local "MenuState" null

    let private stateOf (e: Element) =
        match e.Get state with
        | null ->
            let s = MenuState()
            e.SetLocal(state, s)
            s
        | s -> s

    /// The items a key can go to: those that can be chosen now.
    let private items (menu: Element) =
        [ for c in menu.Children do
              if obj.ReferenceEquals(c.Type, MenuItem.Type) && c.IsEnabled && c.IsVisible then c ]

    let private measure (ctx: MeasureContext) (available: Size) =
        // Room for an icon or a check mark in every item when any has one, so their texts line up.
        let children = ctx.Element.Children
        let mutable any = false
        for c in children do
            if obj.ReferenceEquals(c.Type, MenuItem.Type) && ((c.Get MenuItemProps.icon).IsSome || (c.Get MenuItemProps.isChecked).IsSome) then any <- true
        // A row: its items show their text alone, and the lines between them stand upright.
        let row = ctx.Get StackProps.orientation = Horizontal
        for c in children do
            if obj.ReferenceEquals(c.Type, MenuItem.Type) && (c.Get MenuItemProps.leading <> any || c.Get MenuItemProps.inRow <> row) then
                c.SetLocal(MenuItemProps.leading, any)
                c.SetLocal(MenuItemProps.inRow, row)
                c.InvalidateMeasure()
            elif obj.ReferenceEquals(c.Type, Separator.Type) && c.Get SeparatorProps.upright <> row then
                c.SetLocal(SeparatorProps.upright, row)
                c.InvalidateMeasure()
        StackPanel.Type.Measure ctx available

    let private keyDown (ctx: InputContext) (args: KeyEventArgs) =
        let menu = ctx.Element
        let held = args.Modifiers &&& (Modifiers.Control ||| Modifiers.Alt ||| Modifiers.Meta)
        if held = Modifiers.None then
            let all = items menu
            let n = all.Length
            let at = all |> List.tryFindIndex (fun e -> e.IsFocused)
            // Opened by the mouse, the first item has the focus but shows none: the first key starts from an end.
            let shown = match at with Some i -> all[i].IsFocusVisible | None -> false
            let go (i: int) =
                args.Handle()
                if n > 0 then all[((i % n) + n) % n].Focus()
            // A row's items are side by side: ←/→ in place of ↑/↓.
            let next, previous = if menu.Get StackProps.orientation = Horizontal then Key.Right, Key.Left else Key.Down, Key.Up
            match args.Key, at with
            | k, Some i when k = next && shown -> go (i + 1)
            | k, _ when k = next -> go 0
            | k, Some i when k = previous && shown -> go (i - 1)
            | k, _ when k = previous -> go (n - 1)
            | Key.Home, _ -> go 0
            | Key.End, _ -> go (n - 1)
            // Tab leaves the menu with nothing chosen.
            | Key.Tab, _ ->
                args.Handle()
                Menus.close menu
            | _ -> ()

    /// A typed letter goes to the next item that starts with it (letters build up as in a list).
    let private textInput (ctx: InputContext) (args: TextInputEventArgs) =
        let menu = ctx.Element
        let all = items menu
        if not all.IsEmpty && not (String.IsNullOrWhiteSpace args.Text) then
            args.Handle()
            let at = all |> List.tryFindIndex (fun e -> e.IsFocused && e.IsFocusVisible) |> Option.defaultValue -1
            match Typeahead.find (stateOf menu).Typeahead ctx.Time args.Text all.Length at (fun i -> all[i].Get TextProps.text) with
            | ValueSome i -> all[i].Focus()
            | ValueNone -> ()

    let Type =
        Element.extend StackPanel.Type "Menu" [
            Element.measure measure
            Element.keyDown keyDown
            Element.textInput textInput
            Element.defaults [ Layoutable.minWidth 160.0; Semantics.role Role.Menu ]
        ]

    let create (settings: Setting<Menu> list) : View = View.Create(Type, settings)
    /// A style for Menus (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Menu> list) : Style = Style.forType Type parts settings
