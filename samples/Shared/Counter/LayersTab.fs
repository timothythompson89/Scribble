/// The Layers tab: a toast layer, a modal dialog layer and a ScrollViewer. The app adds the two
/// layers (`toasts`, `dialog`); the page asks them to show something.
module Scribble.Samples.CounterDemo.LayersTab

open System
open System.Threading.Tasks
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples.CounterDemo.Common

// The page asks the layers to show something through these events. (Until M8 brings an element
// that shows view content in a layer, a layer's view is fixed, so it listens instead.)
let private toastRequests = Event<string>()
let private dialogRequests = Event<unit>()
let private toastIds = ref 0

/// A layer at the bottom right: each toast stays 2.5 seconds. The pointer falls through
/// everywhere else, so the app stays usable.
let toasts =
    Component.define "Toasts" (fun ctx () ->
        let shown = ctx.useState<(int * string) list> []
        ctx.useSubscription((), fun () ->
            toastRequests.Publish.Subscribe(fun text ->
                toastIds.Value <- toastIds.Value + 1
                let id = toastIds.Value
                shown.Update(fun ts -> ts @ [ id, text ])
                // From a timer thread: Update queues itself onto the UI thread.
                Task.Delay(2500).ContinueWith(fun (_: Task) -> shown.Update(List.filter (fun (i, _) -> i <> id))) |> ignore))
        StackPanel.create [
            StackPanel.horizontalAlignment HorizontalAlignment.Right
            StackPanel.verticalAlignment VerticalAlignment.Bottom
            StackPanel.margin 16.0
            StackPanel.spacing 8.0
            StackPanel.items shown.Current fst (fun (_, text) ->
                Border.create [
                    Border.padding (14.0, 10.0)
                    Border.cornerRadius 8.0
                    Border.background (Color.Hex 0x3D59A1)
                    Border.shadow { Blur = 12.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.5f }
                    Border.child (TextBlock.create [ TextBlock.text text; TextBlock.foreground Color.White ])
                ])
        ])

/// A modal layer: while the dialog shows, the app below gets no pointer or keyboard input. Hidden,
/// it shows nothing and so blocks nothing.
let dialog =
    Component.define "Dialog" (fun ctx () ->
        let isOpen = ctx.useState false
        ctx.useSubscription((), fun () -> dialogRequests.Publish.Subscribe(fun () -> isOpen.Set true))
        Border.create [
            Border.isVisible isOpen.Current
            Border.background (Color.Black.WithAlpha 0.55f)
            Border.child (
                Border.create [
                    Border.width 340.0
                    Border.horizontalAlignment HorizontalAlignment.Center
                    Border.verticalAlignment VerticalAlignment.Center
                    Border.padding 20.0
                    Border.cornerRadius 10.0
                    Border.background (Color.Hex 0x24243A)
                    Border.shadow { Blur = 24.0; OffsetX = 0.0; OffsetY = 8.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.6f }
                    Border.child (
                        StackPanel.create [
                            StackPanel.spacing 14.0
                            StackPanel.children [
                                TextBlock.create [ TextBlock.text "A modal layer"; TextBlock.fontSize 18.0; TextBlock.foreground Color.White ]
                                TextBlock.create [
                                    TextBlock.text "Until this closes, the app below can't be clicked, scrolled or reached with Tab: Tab stays in here."
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.foreground dim
                                ]
                                StackPanel.create [
                                    StackPanel.orientation Horizontal
                                    StackPanel.spacing 8.0
                                    StackPanel.horizontalAlignment HorizontalAlignment.Right
                                    StackPanel.children [
                                        Button.create [ Button.text "Toast"; Button.onClick (fun _ -> toastRequests.Trigger "From the dialog") ]
                                        Button.create [ Button.text "Close"; Button.onClick (fun _ -> isOpen.Set false) ]
                                    ]
                                ]
                            ]
                        ]
                    )
                ]
            )
        ])

/// The page: a long list in a ScrollViewer, and buttons that use the two layers.
let layersPage =
    StackPanel.create [
        StackPanel.margin (24.0, 16.0, 24.0, 0.0)
        StackPanel.spacing 12.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text "Layers above the app, and a ScrollViewer"; TextBlock.foreground dim ]
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 8.0
                StackPanel.children [
                    Button.create [
                        Button.text "Show a toast"
                        Button.onClick (fun _ -> toastRequests.Trigger("Toast at " + DateTime.Now.ToString "HH:mm:ss"))
                    ]
                    Button.create [ Button.text "Open a modal layer"; Button.onClick (fun _ -> dialogRequests.Trigger()) ]
                ]
            ]
            ScrollViewer.create [
                ScrollViewer.height 300.0
                ScrollViewer.content (
                    StackPanel.create [
                        StackPanel.children [
                            for i in 1..40 ->
                                Border.create [
                                    Border.height 30.0
                                    Border.padding (10.0, 0.0)
                                    Border.background (if i % 2 = 0 then Color.Hex 0x24243A else Color.Hex 0x2A2A42)
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text $"Row {i} of 40"
                                            TextBlock.foreground Color.White
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                    )
                                ]
                        ]
                    ]
                )
            ]
        ]
    ]
