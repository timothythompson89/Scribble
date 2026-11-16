/// The Hello sample, shared by the desktop and browser hosts: the dashboard (Dashboard.fs) with
/// animated progress bars and a toolbar. Space or Pause stops the animation; while paused, nothing
/// changes, so nothing is redrawn until something does (hover, a button).
module Scribble.Samples.HelloApp

open System
open Scribble.Core
open Scribble.Icons
open Scribble.Samples

type Model =
    { Seconds: float
      Animating: bool
      Hovered: int option
      /// The visible cards, by index into Dashboard.cards, in display order.
      Cards: int list
      /// How many times the cards were shuffled (seeds the next shuffle, so update stays pure).
      Shuffles: int }

type Msg =
    | Tick of seconds: float
    | TogglePause
    | Restart
    | AddCard
    | RemoveCard
    | Shuffle
    | Hover of card: int option

let allCards = [ 0 .. Dashboard.cards.Length - 1 ]

let init =
    { Seconds = 0.0
      Animating = true
      Hovered = None
      Cards = allCards
      Shuffles = 0 }

let update msg model =
    match msg with
    // Paused: the model doesn't change, so there's no rebuild and no next frame. That's how the
    // animation stops, and why it costs nothing while paused.
    | Tick seconds when model.Animating -> { model with Seconds = model.Seconds + seconds }
    | Tick _ -> model
    | TogglePause -> { model with Animating = not model.Animating }
    | Restart -> { model with Seconds = 0.0 }
    // Add the first card that isn't showing, at the end.
    | AddCard ->
        match allCards |> List.tryFind (fun i -> not (List.contains i model.Cards)) with
        | Some i -> { model with Cards = model.Cards @ [ i ] }
        | None -> model
    | RemoveCard when model.Cards.Length > 1 -> { model with Cards = List.take (model.Cards.Length - 1) model.Cards }
    | RemoveCard -> model
    | Shuffle ->
        let random = Random(model.Shuffles)
        { model with Cards = model.Cards |> List.sortBy (fun _ -> random.Next()); Shuffles = model.Shuffles + 1 }
    | Hover card -> { model with Hovered = card }

let private toolbar model dispatch =
    // A row of buttons that wraps onto more rows on a narrow screen.
    WrapPanel.create [
        WrapPanel.spacing 8.0
        WrapPanel.lineSpacing 8.0
        WrapPanel.children [
            // Icons: Material Symbols (Scribble.Icons.Material).
            Button.create [
                Button.icon (if model.Animating then Material.pause else Material.playArrow)
                Button.text (if model.Animating then "Pause" else "Resume")
                Button.onClick (fun _ -> dispatch TogglePause)
            ]
            Button.create [ Button.icon Material.restartAlt; Button.text "Restart"; Button.onClick (fun _ -> dispatch Restart) ]
            Button.create [ Button.icon Material.remove; Button.text "Remove card"; Button.onClick (fun _ -> dispatch RemoveCard) ]
            Button.create [ Button.icon Material.add; Button.text "Add card"; Button.onClick (fun _ -> dispatch AddCard) ]
            Button.create [ Button.icon Material.shuffle; Button.text "Shuffle"; Button.onClick (fun _ -> dispatch Shuffle) ]
        ]
    ]

let view model dispatch =
    Dashboard.view model.Seconds model.Hovered model.Cards (Some(toolbar model dispatch)) (fun card -> dispatch (Hover card))

let app: AppSetting<Msg> list =
    [ App.title "Scribble · Hello"
      App.size (960.0, 600.0)
      App.background Dashboard.background
      // Anything Inter lacks (emoji, mostly) comes from Twemoji.
      App.fonts [ Scribble.Text.Emoji.Twemoji() ]
      App.showStats true
      App.closeOnEscape true
      App.program init update view
      // Clamp so a long idle gap doesn't make the animation jump when it resumes.
      App.onFrame (fun time -> Some(Tick(min time.Delta.TotalSeconds 0.1)))
      App.onKey (fun key _ ->
          match key with
          | Key.Space -> Some TogglePause
          | _ -> None) ]
