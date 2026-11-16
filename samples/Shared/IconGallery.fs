/// Every Material Symbol (`Scribble.Icons.Material`) in a scrolling grid: an Outlined/Filled
/// switch, and a click on a tile shows the F# to write for that icon.
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
let private tile (name: string) (filled: bool) (pick: string -> unit) =
    let icon = Material.icon name
    let icon = if filled then icon.filled else icon
    Border.create [
        Border.cursor (Cursor.fromIcon icon [])
        Border.width 112.0
        Border.height 76.0
        Border.cornerRadius 6.0
        Border.padding (6.0, 10.0)
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

let gallery =
    Component.define "IconGallery" (fun ctx () ->
        let filled = ctx.useState false
        let picked = ctx.useState ""
        // Made again only when Outlined/Filled changes: picking an icon re-runs this component,
        // and the same View object lets the differ skip all 4,304 tiles.
        let grid =
            ctx.useMemo(filled.Current, fun isFilled ->
                WrapPanel.create [
                    WrapPanel.name "icons"
                    WrapPanel.spacing 4.0
                    WrapPanel.lineSpacing 4.0
                    WrapPanel.children [ for name in Material.names () -> tile name isFilled picked.Set ]
                ])
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
