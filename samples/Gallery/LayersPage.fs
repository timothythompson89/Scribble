/// The Layers page: a toast layer and a modal dialog layer. The app adds the two layers (`toasts`, `dialog`); the
/// page asks them to show something.
module Scribble.Samples.Gallery.LayersPage

open System
open System.Threading.Tasks
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples
open Scribble.Samples.Gallery.Common

// The page asks the layers to show something through these events. (Until M8 brings an element
// that shows view content in a layer, a layer's view is fixed, so it listens instead.)
let private toastRequests = Event<string>()

/// Shows `text` in a toast (the app's errors use it: GalleryApp's `App.onError`).
let toast (text: string) = toastRequests.Trigger text
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
                    Border.background Palette.accent
                    Border.shadow floatShadow
                    Border.child (TextBlock.create [ TextBlock.text text; TextBlock.foreground Palette.onAccent ])
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
            Border.background scrim
            Border.child (
                Border.create [
                    Border.width 340.0
                    Border.horizontalAlignment HorizontalAlignment.Center
                    Border.verticalAlignment VerticalAlignment.Center
                    Border.padding 20.0
                    Border.cornerRadius 10.0
                    Border.background Palette.surface
                    Border.shadow dialogShadow
                    Border.child (
                        StackPanel.create [
                            StackPanel.spacing 14.0
                            StackPanel.children [
                                TextBlock.create [ TextBlock.text "A modal layer"; TextBlock.fontSize 18.0 ]
                                TextBlock.create [
                                    TextBlock.text "Until this closes, the app below can't be clicked, scrolled or reached with Tab: Tab stays in here."
                                    TextBlock.textWrapping TextWrapping.Wrap
                                    TextBlock.foreground Palette.textDim
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

let view () =
    page "Layers" "Element trees above the app, each with its own focus: toasts the pointer falls through, and a modal dialog that blocks everything below." [
        section "A pass-through layer" "Toasts stack at the bottom right for 2.5 seconds; the app keeps working everywhere but on a toast." (
            Button.create [
                Button.horizontalAlignment HorizontalAlignment.Left
                Button.text "Show a toast"
                Button.onClick (fun _ -> toastRequests.Trigger("Toast at " + DateTime.Now.ToString "HH:mm:ss"))
            ])
        section "A modal layer" "The window dims; nothing below reacts to the pointer, the wheel or the keyboard, and Tab stays in the dialog." (
            Button.create [
                Button.horizontalAlignment HorizontalAlignment.Left
                Button.text "Open a modal layer"
                Button.onClick (fun _ -> dialogRequests.Trigger())
            ])
    ]
