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
    let shortcutForeground = Prop.define "MenuItemShortcutForeground" (Color.Hex 0x9A9AB0) Affects.Render
    /// Behind the item the keyboard is on.
    let highlight = Prop.define "MenuItemHighlight" (Color.White.WithAlpha 0.08f) Affects.Render
    /// Set by the Menu: some item has an icon or a check mark, so every item leaves room for one.
    let internal leading = Prop.local "MenuItemLeading" false

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
    /// Closes the menu `e` is in: the flyout whose layer it is in is asked to close (its element's own, or the
    /// app's `isOpen`).
    let close (e: Element) =
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

    let private measure (ctx: MeasureContext) (_: Size) =
        let ix, iy = Border.inset ctx
        let font, size = ctx.Get TextProps.font, ctx.Get TextProps.fontSize
        let text = ctx.Text.Measure(ctx.Get TextProps.text, font, size)
        let shortcut = ctx.Get MenuItemProps.shortcut
        let shortcutWidth = if shortcut = "" then 0.0 else shortcutGap + ctx.Text.Measure(shortcut, font, size).Width
        let leading = if ctx.Get MenuItemProps.leading then iconSize + iconGap else 0.0
        Size.Create(Math.Ceiling(ix + leading + text.Width + shortcutWidth), Math.Ceiling(max text.LineHeight iconSize + iy))

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
        if ctx.Get MenuItemProps.leading then
            let y = Math.Floor(p.Top + t + (inner - iconSize) / 2.0 + 0.5)
            // A checked item's mark stands where an icon would; an item that can't be checked shows its icon.
            match ctx.Get MenuItemProps.isChecked, ctx.Get MenuItemProps.icon with
            | ValueSome true, _ -> ctx.Text.DrawIcon(list, Icon.geometry check, check.ViewBox, 0.0, false, false, x, y, iconSize, foreground)
            | ValueSome false, _ -> ()
            | ValueNone, ValueSome icon -> ctx.Text.DrawIcon(list, Icon.geometry icon, icon.ViewBox, 0.0, false, false, x, y, iconSize, foreground)
            | ValueNone, ValueNone -> ()
            x <- x + iconSize + iconGap
        let top = Math.Floor(p.Top + t + (inner - lineHeight) / 2.0 + 0.5)
        ctx.Text.Draw(list, ctx.Get TextProps.text, font, fontSize, x, top, foreground)
        let shortcut = ctx.Get MenuItemProps.shortcut
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
                Border.cornerRadius 4.0
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
/// nothing chosen. No submenus yet. A Menu is a panel: its items are its `children` (`MenuItem.create [ ... ]`,
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
        for c in children do
            if obj.ReferenceEquals(c.Type, MenuItem.Type) && c.Get MenuItemProps.leading <> any then
                c.SetLocal(MenuItemProps.leading, any)
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
            match args.Key, at with
            | Key.Down, Some i when shown -> go (i + 1)
            | Key.Down, _ -> go 0
            | Key.Up, Some i when shown -> go (i - 1)
            | Key.Up, _ -> go (n - 1)
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
