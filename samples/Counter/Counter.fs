/// The counter: the README's first example, the smallest Scribble app (Elmish: a model, messages, an update and a
/// view). Desktop and browser hosts are in Counter.Desktop and Counter.Browser (the same one-line Program.fs); the
/// Gallery shows everything else.
module Scribble.Samples.Counter

open Scribble.Core

type Msg =
    | Increment
    | Decrement

let update msg count =
    match msg with
    | Increment -> count + 1
    | Decrement -> count - 1

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
                ]
            ]
        ]
    ]

let app: AppSetting<Msg> list = [ App.title "Counter"; App.size (360.0, 200.0); App.program 0 update view ]
