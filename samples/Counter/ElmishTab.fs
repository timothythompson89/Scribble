/// The Elmish tab: an Elmish counter inside a component, with a command that adds 1 a second
/// later (`useElmishWithCmd`, `Cmd.ofAsync`).
module Scribble.Samples.CounterDemo.ElmishTab

open Scribble.Core

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
            TextBlock.create [ TextBlock.text $"Count: {count}"; TextBlock.fontSize 28.0 ]
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
