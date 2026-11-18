/// The Layers page: a toast layer and a modal dialog layer. The app adds the two layers (`toasts`, `dialog`); the
/// page asks them to show something.
module Scribble.Samples.Gallery.LayersPage

open System
open System.Threading.Tasks
open Scribble.Core.FlyoutProps
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Accessibility
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
                    Border.content (TextBlock.create [ TextBlock.text text; TextBlock.foreground Palette.onAccent ])
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
            Border.content (
                Border.create [
                    Border.width 340.0
                    Border.horizontalAlignment HorizontalAlignment.Center
                    Border.verticalAlignment VerticalAlignment.Center
                    Border.padding 20.0
                    Border.cornerRadius 10.0
                    Border.background Palette.surface
                    Border.shadow dialogShadow
                    Border.content (
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

/// One row of a menu: text on the left, transparent until hovered.
let private menuItem (text: string) (onClick: unit -> unit) =
    Button.create [
        Button.name text
        Button.background Color.Transparent
        Button.borderThickness 0.0
        Setting.create BorderProps.shadow ValueNone
        Button.padding (10.0, 6.0)
        Button.content (TextBlock.create [ TextBlock.text text; TextBlock.horizontalAlignment HorizontalAlignment.Left ])
        Button.onClick (fun _ -> onClick ())
    ]

/// The Actions menu: controlled, so choosing an item can close it (the page keeps whether it's open).
let private actionsMenu (isOpen: State<bool>) : Setting<Flyout> list =
    let choose (text: string) () =
        isOpen.Set false
        toastRequests.Trigger("Chose " + text)
    [
        Flyout.isOpen isOpen.Current
        Flyout.onOpenChanged isOpen.Set
        Flyout.content (
            StackPanel.create [
                StackPanel.width 180.0
                StackPanel.children [
                    menuItem "Copy" (choose "Copy")
                    menuItem "Duplicate" (choose "Duplicate")
                    Separator.create [ Separator.margin (0.0, 4.0) ]
                    menuItem "Delete" (choose "Delete")
                ]
            ]
        )
    ]

/// A line of text on one side.
let private sideFlyout (label: string) (placement: Placement) (alignment: FlyoutAlignment) : Setting<Flyout> list =
    [
        Flyout.placement placement
        Flyout.alignment alignment
        Flyout.content (TextBlock.create [ TextBlock.text ("Placed " + label.ToLowerInvariant()); TextBlock.margin (8.0, 4.0) ])
    ]

/// For the Placement section's panel (a style list made once: styles compare by reference).
let private placementStyles = [ Flyout.style [] [
    Flyout.cornerRadius 0.0
    Flyout.lightDismiss false
    Flyout.borderBrush Palette.accent
    Flyout.cornerRadius 12.0
    Flyout.padding 12.0
] ]

/// Made once and reused: pinned, with its own look (accent line, rounder corners, more padding).
let private pinnedSettings =
    StackPanel.create [
        StackPanel.width 220.0
        StackPanel.spacing 8.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text "Stays open until the button is clicked again."; TextBlock.textWrapping TextWrapping.Wrap ]
            Slider.create [ Slider.value 40.0; Semantics.label "Level" ]
            CheckBox.create [ CheckBox.text "An option" ]
        ]
    ]

/// The page: a component, so it can keep whether the Actions menu is open.
let view =
    Component.define "LayersPage" (fun ctx () ->
        let actionsOpen = ctx.useState false
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
            section "Flyout" "Content in its own layer next to a button. Escape or a click elsewhere closes it (that click does nothing else); focus moves in, and back when it closes. The menu is controlled: choosing an item closes it." (
                Button.create [
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Button.text "Actions"
                    Button.flyout (actionsMenu actionsOpen)
                ])
            section "Placement" "Each side, lined up at the start, centre or end; one that doesn't fit flips to the other side and stays in the window (make the window short, or scroll the button to the bottom edge). A style on the panel around them gives their flyouts square corners." (
                WrapPanel.create [
                    // Flyouts are styled as if inside their buttons: this reaches these four flyouts and no others.
                    WrapPanel.styles placementStyles
                    WrapPanel.children [
                        for label, placement, alignment in
                            [ "Below", Placement.Below, FlyoutAlignment.Start
                              "Above", Placement.Above, FlyoutAlignment.Center
                              "Right", Placement.Right, FlyoutAlignment.Start
                              "Left", Placement.Left, FlyoutAlignment.End ] do
                            Button.create [
                                Button.margin (0.0, 0.0, 8.0, 8.0)
                                Button.text label
                                Button.flyout (sideFlyout label placement alignment)
                            ]
                    ]
                ])
            section "Pinned" "Flyout.lightDismiss false: clicks elsewhere and Escape leave it open, and reach the app. This one also has its own look (Flyout.borderBrush, cornerRadius, padding)." (
                Button.create [
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Button.text "Pinned settings"
                    Button.flyout pinnedSettings
                ])
            section "Tooltips" "Rest the pointer on a button for half a second; moving to the next shows its tooltip at once. A click, the wheel or a key hides it. None on touch." (
                StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 8.0
                    StackPanel.children [
                        Button.create [ Button.text "Save"; Button.tooltip "Save the document (Ctrl+S)" ]
                        Button.create [ Button.text "Open"; Button.tooltip "Open a document (Ctrl+O)" ]
                        Button.create [
                            Button.text "Rich"
                            Button.tooltipView (
                                StackPanel.create [
                                    StackPanel.spacing 2.0
                                    StackPanel.children [
                                        TextBlock.create [ TextBlock.text "A tooltip view"; TextBlock.font (Fonts.SemiBold()) ]
                                        TextBlock.create [ TextBlock.text "Any view: text, icons, pictures."; TextBlock.foreground Palette.textDim ]
                                    ]
                                ]
                            )
                        ]
                    ]
                ])
        ])
