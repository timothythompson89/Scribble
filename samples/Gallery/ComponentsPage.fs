/// The Components page: three components keeping their own state, each a different way: an Elmish counter
/// with a command (`useElmishWithCmd`, `Cmd.ofAsync`), keyed counters with an animated bar (`useState`,
/// `useAnimated`), and a slow load that restarts when its input changes (`useAsync`).
module Scribble.Samples.Gallery.ComponentsPage

open System
open System.Threading
open System.Threading.Tasks
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples.Gallery.Common

// --- Elmish: a model, messages and an update, inside a component -------------------------------

type Msg =
    | Increment
    | Decrement
    | Reset
    | IncrementLater

let update msg count =
    match msg with
    | Increment -> count + 1, Cmd.none
    | Decrement -> count - 1, Cmd.none
    | Reset -> 0, Cmd.none
    // The model doesn't change now; the command sends Increment after a second.
    | IncrementLater -> count, Cmd.ofAsync (fun () -> Async.Sleep 1000) () (fun () -> Increment) (fun _ -> Reset)

let elmishView count dispatch =
    StackPanel.create [
        StackPanel.spacing 12.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"Count: {count}"; TextBlock.fontSize 28.0 ]
            WrapPanel.create [
                WrapPanel.spacing 8.0
                WrapPanel.lineSpacing 8.0
                WrapPanel.children [
                    Button.create [ Button.text "−"; Button.onClick (fun _ -> dispatch Decrement) ]
                    Button.create [ Button.text "+"; Button.onClick (fun _ -> dispatch Increment) ]
                    Button.create [ Button.text "Reset"; Button.isEnabled (count <> 0); Button.onClick (fun _ -> dispatch Reset) ]
                    Button.create [ Button.text "+1 in a second"; Button.onClick (fun _ -> dispatch IncrementLater) ]
                ]
            ]
        ]
    ]

/// The counter as a component: its model lives in the component, not the app.
let elmishCounter =
    Component.define "ElmishCounter" (fun ctx () ->
        let count, dispatch = ctx.useElmishWithCmd((0, Cmd.none), update)
        elmishView count dispatch)

// --- useState and useAnimated: keyed counters ---------------------------------------------------

let private barWidth = 120.0

/// One counter with its own state. The bar fills to the count (0 to 10) and animates there.
let counter =
    Component.define "Counter" (fun ctx (name: string) ->
        let count = ctx.useState 0
        let filled = ctx.useAnimated(float (min 10 (abs count.Current)) / 10.0, TimeSpan.FromMilliseconds 250.0)
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.spacing 8.0
            StackPanel.children [
                TextBlock.create [
                    TextBlock.text name
                    TextBlock.width 80.0
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
                Button.create [ Button.text "−"; Button.width 32.0; Button.onClick (fun _ -> count.Update(fun c -> c - 1)) ]
                TextBlock.create [
                    TextBlock.text (string count.Current)
                    TextBlock.width 32.0
                    TextBlock.textAlignment TextAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
                Button.create [ Button.text "+"; Button.width 32.0; Button.onClick (fun _ -> count.Update(fun c -> c + 1)) ]
                Border.create [
                    Border.width barWidth
                    Border.height 8.0
                    Border.margin (8.0, 0.0, 0.0, 0.0)
                    Border.verticalAlignment VerticalAlignment.Center
                    Border.cornerRadius 4.0
                    Border.background track
                    Border.child (
                        Border.create [
                            Border.width (barWidth * filled)
                            Border.horizontalAlignment HorizontalAlignment.Left
                            Border.cornerRadius 4.0
                            Border.background (if count.Current < 0 then error else fill)
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
            StackPanel.spacing 10.0
            StackPanel.children [
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.lineSpacing 8.0
                    WrapPanel.children [
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

// --- useAsync: a slow load ------------------------------------------------------------------------

/// Pretends to fetch something slow: n squared, a second later. Honors cancellation.
let private slowSquare (n: int) (ct: CancellationToken) : Task<int> =
    task {
        do! Task.Delay(1000, ct)
        return n * n
    }

/// Change the number while it's loading: the old load is cancelled and a new one starts.
let slowLoad =
    Component.define "SlowLoad" (fun ctx () ->
        let n = ctx.useState 1
        let square = ctx.useAsync(n.Current, slowSquare)
        let result, color =
            match square with
            | AsyncValue.Loading -> "loading…", Palette.textDim
            | AsyncValue.Loaded v -> $"{n.Current}² = {v}", Palette.text
            | AsyncValue.Failed e -> e.Message, error
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.spacing 8.0
            StackPanel.children [
                Button.create [ Button.text "−"; Button.width 32.0; Button.onClick (fun _ -> n.Update(fun v -> v - 1)) ]
                TextBlock.create [
                    TextBlock.text (string n.Current)
                    TextBlock.width 32.0
                    TextBlock.textAlignment TextAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
                Button.create [ Button.text "+"; Button.width 32.0; Button.onClick (fun _ -> n.Update(fun v -> v + 1)) ]
                TextBlock.create [
                    TextBlock.text result
                    TextBlock.margin (8.0, 0.0, 0.0, 0.0)
                    TextBlock.foreground color
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            ]
        ])

// --- The page --------------------------------------------------------------------------------------

let view () =
    page "Components" "Views with their own state: an Elmish model, hooks, an animation and an async load. Each keeps its state while another page shows." [
        section "Elmish" "useElmishWithCmd: a model, messages and an update inside a component; \"+1 in a second\" sends a command." (elmishCounter ())
        section "useState and useAnimated" "Counters keyed by name, so each keeps its count when the list changes; the bar slides to the count." (counters ())
        section "useAsync" "A one-second load, cancelled and started again when the number changes." (slowLoad ())
    ]
