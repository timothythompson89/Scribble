/// The counter: the whole app, desktop and browser. Program.fs in each host is one line.
module Scribble.Samples.Counter

open Scribble.Rendering
open Scribble.Core

type Msg =
    | Increment
    | Decrement
    | Reset

let update msg count =
    match msg with
    | Increment -> count + 1
    | Decrement -> count - 1
    | Reset -> 0

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
                    Button.create [ Button.text "−"; Button.onClick (fun () -> dispatch Decrement) ]
                    Button.create [ Button.text "+"; Button.onClick (fun () -> dispatch Increment) ]
                    if count <> 0 then Button.create [ Button.text "Reset"; Button.onClick (fun () -> dispatch Reset) ]
                ]
            ]
        ]
    ]

let app = [ App.title "Counter"; App.size (360.0, 180.0); App.program 0 update view ]
