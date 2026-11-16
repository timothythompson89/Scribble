/// The List tab: counters with their own state (`useState`), keyed so state follows the item
/// when the list changes order, with a bar that animates to the count (`useAnimated`).
module Scribble.Samples.CounterDemo.ListTab

open System
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

let private barWidth = 120.0

/// One counter with its own state. The bar fills to the count (0 to 10) and animates there.
let counter =
    Component.define "Counter" (fun ctx (name: string) ->
        let count = ctx.useState 0
        let fill = ctx.useAnimated(float (min 10 (abs count.Current)) / 10.0, TimeSpan.FromMilliseconds 250.0)
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.spacing 8.0
            StackPanel.children [
                TextBlock.create [
                    TextBlock.text name
                    TextBlock.width 80.0
                    TextBlock.foreground Color.White
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
                Button.create [ Button.text "−"; Button.width 32.0; Button.onClick (fun _ -> count.Update(fun c -> c - 1)) ]
                TextBlock.create [
                    TextBlock.text (string count.Current)
                    TextBlock.width 32.0
                    TextBlock.textAlignment TextAlignment.Center
                    TextBlock.foreground Color.White
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
                Button.create [ Button.text "+"; Button.width 32.0; Button.onClick (fun _ -> count.Update(fun c -> c + 1)) ]
                Border.create [
                    Border.width barWidth
                    Border.height 8.0
                    Border.margin (8.0, 0.0, 0.0, 0.0)
                    Border.verticalAlignment VerticalAlignment.Center
                    Border.cornerRadius 4.0
                    Border.background (Color.Hex 0x2A2A3C)
                    Border.child (
                        Border.create [
                            Border.width (barWidth * fill)
                            Border.horizontalAlignment HorizontalAlignment.Left
                            Border.cornerRadius 4.0
                            Border.background (if count.Current < 0 then Color.Hex 0xF7768E else Color.Hex 0x7AA2F7)
                        ]
                    )
                ]
            ]
        ])

let private fruit = [ "Apples"; "Pears"; "Plums"; "Cherries"; "Figs"; "Kiwis"; "Limes"; "Mangos" ]

/// The list: add, remove and reverse. Counters are keyed by name, so each keeps its count when
/// the list changes around it.
let counters =
    Component.define "Counters" (fun ctx () ->
        let names = ctx.useState [ "Apples"; "Pears"; "Plums" ]
        let missing = fruit |> List.filter (fun f -> not (List.contains f names.Current))
        StackPanel.create [
            StackPanel.margin (24.0, 10.0, 24.0, 24.0)
            StackPanel.spacing 10.0
            StackPanel.children [
                StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 8.0
                    StackPanel.children [
                        Button.create [
                            Button.text "Add"
                            Button.isEnabled (not missing.IsEmpty)
                            Button.onClick (fun _ -> names.Update(fun ns -> ns @ List.truncate 1 missing))
                        ]
                        Button.create [
                            Button.text "Remove first"
                            Button.isEnabled (not names.Current.IsEmpty)
                            Button.onClick (fun _ -> names.Update(List.tail))
                        ]
                        Button.create [ Button.text "Reverse"; Button.onClick (fun _ -> names.Update List.rev) ]
                    ]
                ]
                StackPanel.create [ StackPanel.spacing 6.0; StackPanel.items names.Current id counter ]
            ]
        ])
