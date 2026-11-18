/// The Scrolling page: 10,000 messages of varying length in an ItemsView inside a ScrollViewer (only the rows on
/// screen exist), with buttons that jump to one through a scroller, and one that adds a message and scrolls to it.
/// The page fills the window (the list takes the height left) instead of scrolling as a whole.
module Scribble.Samples.Gallery.ScrollingPage

open System
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples.Gallery.Common

/// A message in the long list: its number; its text is made when its row is shown (making all 10,000
/// texts up front took a noticeable moment in the interpreted browser build).
type Message = { Id: int; Added: bool }

/// Words for messages of varying length (made up, repeatable: message n is always the same).
let private words =
    [| "layout"; "pixels"; "the"; "scroller"; "keeps"; "only"; "what"; "shows"; "and"; "a"; "little"; "more";
       "measured"; "as"; "it"; "scrolls"; "estimates"; "for"; "everything"; "else"; "GPU"; "frames"; "on"; "request" |]

let private messageText (n: int) =
    // 3 to about 60 words: one to several lines.
    let count = 3 + (n * 7919 % 13) * (1 + n % 5)
    let picked = [ for k in 0 .. count - 1 -> words[(n * 31 + k * 17) % words.Length] ]
    "#" + n.ToString("N0", Globalization.CultureInfo.InvariantCulture) + " " + String.Join(" ", picked)

let private initialMessages = lazy (Array.init 10_000 (fun i -> { Id = i + 1; Added = false }))

/// 10,000 messages of varying length in an ItemsView (only those on screen exist), with buttons
/// that jump to one (the scroller), and one that adds a message and scrolls to it.
let private messages =
    Component.define "Messages" (fun ctx () ->
        let all = ctx.useState initialMessages.Value
        let scroller = ctx.useScroller ()
        let random = ctx.useMemo((), fun () -> Random 7)
        let jump (label: string) (id: unit -> int) =
            Button.create [ Button.text label; Button.onClick (fun _ -> scroller.ScrollTo(id (), ScrollAlign.Start)) ]
        DockPanel.create [
            // The buttons on top; the list takes the height left (no fixed height), so the page never scrolls
            // and the buttons stay under the pointer while the list moves.
            DockPanel.children [
                WrapPanel.create [
                    DockPanel.dock Dock.Top
                    WrapPanel.margin (0.0, 0.0, 0.0, 8.0)
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
                                all.Set(Array.append all.Current [| { Id = id; Added = true } |])
                                // Applies at the next layout, once the new message is in the list.
                                scroller.ScrollTo(id, ScrollAlign.End))
                        ]
                    ]
                ]
                ScrollViewer.create [
                    ScrollViewer.content (
                        ItemsView.create [
                            ItemsView.name "messages"
                            ItemsView.source all.Current
                            ItemsView.key (fun (m: Message) -> m.Id)
                            ItemsView.view (fun (m: Message) ->
                                Border.create [
                                    Border.padding (10.0, 6.0)
                                    Border.background (if m.Id % 2 = 0 then Palette.surface else rowAlt)
                                    Border.content (TextBlock.create [ TextBlock.text (messageText m.Id + (if m.Added then " (added)" else "")); TextBlock.textWrapping Wrap ])
                                ])
                            ItemsView.scroller scroller
                        ]
                    )
                ]
            ]
        ])

let view () =
    fillingPage "Scrolling" "10,000 messages in an ItemsView: only the rows on screen exist, measured as they show." (
        fillingSection "ItemsView in a ScrollViewer" "Wheel, drag the thumb or click the track; after a click in the list, arrows, PageUp/Down and Home/End. The jumps put a message at the top." (messages ()))
