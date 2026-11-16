/// The Load tab: a slow load that restarts, cancelling the old one, when its input changes
/// (`useAsync`).
module Scribble.Samples.CounterDemo.LoadTab

open System.Threading
open System.Threading.Tasks
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples.CounterDemo.Common

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
            StackPanel.margin (24.0, 10.0, 24.0, 0.0)
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
