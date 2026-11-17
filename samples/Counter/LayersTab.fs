/// The Layers tab: a toast layer, a modal dialog layer and 10,000 messages in an ItemsView. The app adds the two
/// layers (`toasts`, `dialog`); the page asks them to show something.
module Scribble.Samples.CounterDemo.LayersTab

open System
open System.Threading.Tasks
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples
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

/// A message in the long list.
type Message = { Id: int; Text: string }

/// Words for messages of varying length (made up, repeatable: message n is always the same).
let private words =
    [| "layout"; "pixels"; "the"; "scroller"; "keeps"; "only"; "what"; "shows"; "and"; "a"; "little"; "more";
       "measured"; "as"; "it"; "scrolls"; "estimates"; "for"; "everything"; "else"; "GPU"; "frames"; "on"; "request" |]

let private messageText (n: int) =
    // 3 to about 60 words: one to several lines.
    let count = 3 + (n * 7919 % 13) * (1 + n % 5)
    let picked = [ for k in 0 .. count - 1 -> words[(n * 31 + k * 17) % words.Length] ]
    "#" + n.ToString("N0", Globalization.CultureInfo.InvariantCulture) + " " + String.Join(" ", picked)

let private initialMessages = lazy (Array.init 10_000 (fun i -> { Id = i + 1; Text = messageText (i + 1) }))

/// 10,000 messages of varying length in an ItemsView (only those on screen exist), with buttons
/// that jump to one (the scroller), and one that adds a message and scrolls to it.
let private messages =
    Component.define "Messages" (fun ctx () ->
        let all = ctx.useState initialMessages.Value
        let scroller = ctx.useScroller ()
        let random = ctx.useMemo((), fun () -> Random 7)
        let jump (label: string) (id: unit -> int) =
            Button.create [ Button.text label; Button.onClick (fun _ -> scroller.ScrollTo(id (), ScrollAlign.Start)) ]
        StackPanel.create [
            StackPanel.spacing 8.0
            StackPanel.children [
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.lineSpacing 8.0
                    WrapPanel.children [
                        jump "Jump to #1" (fun () -> 1)
                        jump "#5,000" (fun () -> 5000)
                        jump "#9,000" (fun () -> 9000)
                        jump "Random" (fun () -> random.Next(1, all.Current.Length + 1))
                        Button.create [
                            Button.text "Add message"
                            Button.onClick (fun _ ->
                                let id = all.Current.Length + 1
                                all.Set(Array.append all.Current [| { Id = id; Text = messageText id + " (added)" } |])
                                // Applies at the next layout, once the new message is in the list.
                                scroller.ScrollTo(id, ScrollAlign.End))
                        ]
                    ]
                ]
                ScrollViewer.create [
                    ScrollViewer.height 300.0
                    ScrollViewer.content (
                        ItemsView.create [
                            ItemsView.name "messages"
                            ItemsView.source all.Current
                            ItemsView.key (fun (m: Message) -> m.Id)
                            ItemsView.view (fun (m: Message) ->
                                Border.create [
                                    Border.padding (10.0, 6.0)
                                    Border.background (if m.Id % 2 = 0 then Palette.surface else rowAlt)
                                    Border.child (TextBlock.create [ TextBlock.text m.Text; TextBlock.textWrapping Wrap ])
                                ])
                            ItemsView.scroller scroller
                        ]
                    )
                ]
            ]
        ])

/// The page: a long list in a ScrollViewer, and buttons that use the two layers.
let layersPage =
    StackPanel.create [
        StackPanel.margin (24.0, 16.0, 24.0, 0.0)
        StackPanel.spacing 12.0
        StackPanel.children [
            TextBlock.create [
                TextBlock.text "Layers above the app, and 10,000 messages in an ItemsView"
                TextBlock.foreground Palette.textDim
                TextBlock.textWrapping Wrap
            ]
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
            messages ()
        ]
    ]
