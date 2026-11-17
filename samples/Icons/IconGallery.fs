/// Every Material Symbol (`Scribble.Icons.Material`) in a scrolling grid: an ItemsView, so only the
/// tiles on screen exist (about 70 of 4,304). An Outlined/Filled switch, a search field that
/// scrolls to the first match, and a click on a tile shows the F# to write for that icon.
module Scribble.Samples.IconGallery

open System
open Scribble.Text
open Scribble.Core
open Scribble.Icons

/// The member `Material` has for a Google name, as `tools/GenerateMaterial.fsx` makes it:
/// arrow_upward -> arrowUpward; 10k -> _10k; F# keywords in double backticks.
let memberName (name: string) =
    let keywords = set [ "class"; "done"; "event"; "function"; "public"; "select"; "try"; "type"; "open"; "default"; "base" ]
    let parts = name.Split '_'
    let tail = parts[1..] |> Array.map (fun p -> if p = "" then "" else string (Char.ToUpperInvariant p[0]) + p.Substring 1)
    let camel = parts[0] + String.Join("", tail)
    let camel = if Char.IsDigit camel[0] then "_" + camel else camel
    if keywords.Contains camel then "``" + camel + "``" else camel

/// One tile: the icon above its name; a click picks it. Over it, the pointer becomes that icon (a
/// cursor made from it, pointing at its center), to see how it works as a cursor.
let private tile (name: string) (filled: bool) (isMatch: bool) (pick: string -> unit) =
    let icon = Material.icon name
    let icon = if filled then icon.filled else icon
    Border.create [
        Border.cursor (Cursor.fromIcon icon [])
        Border.width 112.0
        Border.height 76.0
        Border.cornerRadius 6.0
        Border.padding (6.0, 10.0)
        Border.borderThickness (if isMatch then 1.5 else 0.0)
        Border.borderBrush Palette.accent
        Border.on States.hover [ Border.background Palette.surface ]
        Border.onClick (fun _ -> pick name)
        Border.child (
            StackPanel.create [
                StackPanel.spacing 8.0
                StackPanel.children [
                    IconView.create [
                        IconView.icon icon
                        IconView.size 24.0
                        IconView.horizontalAlignment HorizontalAlignment.Center
                    ]
                    TextBlock.create [
                        TextBlock.text name
                        TextBlock.fontSize 11.0
                        TextBlock.foreground Palette.textDim
                        TextBlock.textTrimming CharacterEllipsis
                        TextBlock.horizontalAlignment HorizontalAlignment.Center
                    ]
                ]
            ]
        )
    ]

let private switchStyles = [ Button.style [ Style.cls "selected" ] [ Button.background Palette.accent; Button.foreground Palette.onAccent ] ]

let private switchButton (text: string) (selected: bool) (onClick: unit -> unit) =
    Button.create [
        Button.text text
        Button.padding (12.0, 6.0)
        Button.classes (if selected then [ "selected" ] else [])
        Button.onClick (fun _ -> onClick ())
    ]

/// Every icon's name, in Google's order (the ItemsView's items; made once).
let private names = lazy (Array.ofSeq (Material.names ()))

/// The first icon whose name starts with `query`, else the first that contains it.
let firstMatch (query: string) =
    if query = "" then None
    else
        let all = names.Value
        match all |> Array.tryFind (fun n -> n.StartsWith(query, StringComparison.Ordinal)) with
        | Some n -> Some n
        | None -> all |> Array.tryFind (fun n -> n.Contains(query, StringComparison.Ordinal))

/// A field that takes typed keys (a stand-in until M9's TextBox): click it (or Tab to it) and type.
let private searchField (query: string) (found: bool) (onKey: KeyEventArgs -> unit) =
    Border.create [
        Border.name "search"
        Border.focusable true
        Border.width 220.0
        Border.padding (10.0, 6.0)
        Border.cornerRadius 6.0
        Border.background Palette.surface
        Border.borderThickness 1.0
        Border.borderBrush (if query <> "" && not found then Palette.danger else Palette.border)
        Border.on States.focused [ Border.borderBrush Palette.accent ]
        Border.onKeyDown onKey
        Border.child (
            TextBlock.create [
                TextBlock.text (if query = "" then "Search icons…" else query)
                TextBlock.foreground (if query = "" then Palette.textDim else Palette.text)
            ]
        )
    ]

let gallery =
    Component.define "IconGallery" (fun ctx () ->
        let filled = ctx.useState false
        let picked = ctx.useState ""
        let query = ctx.useState ""
        let scroller = ctx.useScroller ()
        let found = firstMatch query.Current
        let onKey (e: KeyEventArgs) =
            let next =
                match e.Key with
                | Key.Char c when c = '_' || Char.IsLetterOrDigit c -> Some(query.Current + string c)
                | Key.Backspace when query.Current <> "" -> Some(query.Current.Substring(0, query.Current.Length - 1))
                | Key.Escape when query.Current <> "" -> Some ""
                | _ -> None
            match next with
            | Some q ->
                e.Handle()
                query.Set q
                // An action: scroll to the first match (it applies at the next layout).
                match firstMatch q with
                | Some name -> scroller.ScrollTo(name, ScrollAlign.Start)
                | None -> ()
            | None -> ()
        // Only the tiles on screen are made: the ItemsView calls `view` for those.
        let grid =
            ItemsView.create [
                ItemsView.name "icons"
                ItemsView.source names.Value
                ItemsView.view (fun (name: string) -> tile name filled.Current (Some name = found) picked.Set)
                ItemsView.layout (ItemsLayout.wrap 116.0 80.0)
                ItemsView.scroller scroller
            ]
        let code =
            if picked.Current = "" then "Click an icon to see its F# name."
            else "Material." + memberName picked.Current + (if filled.Current then ".filled" else "")
        StackPanel.create [
            StackPanel.margin 16.0
            StackPanel.spacing 12.0
            StackPanel.styles switchStyles
            StackPanel.children [
                SplitRow.create [
                    SplitRow.gap 16.0
                    SplitRow.children [
                        TextBlock.create [
                            TextBlock.text ("Material Symbols · " + string (Seq.length (Material.names ())) + " icons")
                            TextBlock.fontSize 20.0
                            TextBlock.verticalAlignment VerticalAlignment.Center
                        ]
                        StackPanel.create [
                            StackPanel.orientation Horizontal
                            StackPanel.spacing 8.0
                            StackPanel.children [
                                searchField query.Current found.IsSome onKey
                                switchButton "Outlined" (not filled.Current) (fun () -> filled.Set false)
                                switchButton "Filled" filled.Current (fun () -> filled.Set true)
                            ]
                        ]
                    ]
                ]
                TextBlock.create [
                    TextBlock.name "code"
                    TextBlock.text code
                    TextBlock.foreground (if picked.Current = "" then Palette.textDim else Palette.text)
                ]
                ScrollViewer.create [
                    ScrollViewer.name "icon scroller"
                    ScrollViewer.height "1*"
                    ScrollViewer.content grid
                ]
            ]
        ])

let app: AppSetting<unit> list =
    [ App.title "Scribble – Material Symbols"
      App.size (920.0, 720.0)
      App.view (gallery ()) ]
