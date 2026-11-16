/// The counter sample, now a components demo (desktop and browser; Program.fs in each host is one
/// line):
///
/// - an Elmish counter inside a component, with a command that adds 1 a second later
///   (`useElmishWithCmd`, `Cmd.ofAsync`),
/// - a list of counters, each with its own state (`useState`), keyed so state follows the item
///   when the list changes order, with a bar that animates to the count (`useAnimated`),
/// - a slow load that restarts (cancelling the old one) when its input changes (`useAsync`),
/// - Buttons that sink when pressed,
/// - the three on tabs that keep each page's state while another is shown.
module Scribble.Samples.Counter

open System
open System.Threading
open System.Threading.Tasks
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

// --- The Elmish counter ---------------------------------------------------------------------------

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

let view count dispatch =
    StackPanel.create [
        StackPanel.margin 24.0
        StackPanel.spacing 16.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text $"Count: {count}"; TextBlock.fontSize 28.0; TextBlock.foreground Color.White ]
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 8.0
                StackPanel.children [
                    Button.create [ Button.text "−"; Button.onClick (fun _ -> dispatch Decrement) ]
                    Button.create [ Button.text "+"; Button.onClick (fun _ -> dispatch Increment) ]
                    Button.create [ Button.text "Reset"; Button.isEnabled (count <> 0); Button.onClick (fun _ -> dispatch Reset) ]
                    Button.create [ Button.text "+1 in a second"; Button.onClick (fun _ -> dispatch IncrementLater) ]
                ]
            ]
        ]
    ]

/// The same counter as a component: its model lives in the component, not the app.
let elmishCounter =
    Component.define "ElmishCounter" (fun ctx () ->
        let count, dispatch = ctx.useElmishWithCmd((0, Cmd.none), update)
        view count dispatch)

// --- A list of counters ---------------------------------------------------------------------------

let private dim = Color.Hex 0x9A9AB0
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

// --- A slow load ----------------------------------------------------------------------------------

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
            | AsyncValue.Loading -> "loading…", dim
            | AsyncValue.Loaded v -> $"{n.Current}² = {v}", Color.White
            | AsyncValue.Failed e -> e.Message, Color.Hex 0xF7768E
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.spacing 8.0
            StackPanel.margin (24.0, 10.0, 24.0, 0.0)
            StackPanel.children [
                Button.create [ Button.text "−"; Button.width 32.0; Button.onClick (fun _ -> n.Update(fun v -> v - 1)) ]
                TextBlock.create [
                    TextBlock.text (string n.Current)
                    TextBlock.width 32.0
                    TextBlock.textAlignment TextAlignment.Center
                    TextBlock.foreground Color.White
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

// --- The app --------------------------------------------------------------------------------------

let private heading (text: string) =
    TextBlock.create [ TextBlock.text text; TextBlock.foreground dim; TextBlock.margin (24.0, 16.0, 24.0, 0.0) ]

/// The demo's pages: tab title, then the page.
let private pages =
    [ "Elmish", [ heading "useElmishWithCmd, with a command that adds 1 a second later"; elmishCounter () ]
      "Load", [ heading "useAsync: a one-second load, cancelled when the number changes"; slowLoad () ]
      "List", [ heading "useState and useAnimated, keyed by name"; counters () ] ]

/// Tabs made from what's already there: every page stays in the tree and only the selected one
/// is visible (`isVisible false` is neither measured, drawn, hit nor focused), so each page keeps
/// its state, including a load still running, while another is shown.
let demo =
    Component.define "Demo" (fun ctx () ->
        let selected = ctx.useState 0
        StackPanel.create [
            StackPanel.children [
                yield StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 4.0
                    StackPanel.margin (24.0, 16.0, 24.0, 0.0)
                    StackPanel.children [
                        for i, (title, _) in List.indexed pages ->
                            Button.create [
                                yield Button.text title
                                yield Button.width 80.0
                                // Until styles (M7), the selected tab is just a different background.
                                if i = selected.Current then yield Button.background (Color.Hex 0x3D59A1)
                                yield Button.onClick (fun _ -> selected.Set i)
                            ]
                    ]
                ]
                // Pages by position: the same three in the same places, so switching only flips
                // isVisible and never rebuilds a page.
                for i, (_, content) in List.indexed pages ->
                    let shown = i = selected.Current
                    StackPanel.create [ StackPanel.isVisible shown; StackPanel.children content ]
            ]
        ])

/// Components hold all the state, so the app has no messages of its own (hence `unit`).
let app: AppSetting<unit> list = [ App.title "Counter"; App.size (520.0, 520.0); App.view (demo ()) ]
