/// The Layers page: toasts (the Gallery's `toaster`, which also shows the app's errors), dialogs, flyouts and
/// tooltips. Each shows in a layer of its own above the app; the page only says what and when.
module Scribble.Samples.Gallery.LayersPage

open System
open Scribble.Core.FlyoutProps
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Accessibility
open Scribble.Samples
open Scribble.Samples.Gallery.Common

/// The dialogs' buttons, at the right.
let private buttonRow (buttons: View list) =
    StackPanel.create [
        StackPanel.orientation Horizontal
        StackPanel.spacing 8.0
        StackPanel.horizontalAlignment HorizontalAlignment.Right
        StackPanel.children buttons
    ]

/// Some text over a row of buttons.
let private dialogBody (text: string) (buttons: View list) =
    StackPanel.create [
        StackPanel.spacing 16.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text text; TextBlock.textWrapping TextWrapping.Wrap; TextBlock.foreground Palette.textDim ]
            buttonRow buttons
        ]
    ]

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
        toaster.Show("Chose " + text)
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
        let dialogOpen = ctx.useState false
        let questionOpen = ctx.useState false
        let answer = ctx.useState "Not asked yet"
        page "Layers" "Content above the app, each in a layer of its own: toasts that never take the keyboard, dialogs that take everything until they close, flyouts and tooltips." [
            section "Toasts" "At the bottom right (across the bottom of a narrow window), newest lowest, three at a time. One stays 4 seconds and waits while the pointer is on it; an error stays until closed. The app keeps working everywhere but on a toast." (
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.lineSpacing 8.0
                    WrapPanel.children [
                        Button.create [ Button.text "Show a toast"; Button.onClick (fun _ -> toaster.Show("Toast at " + DateTime.Now.ToString "HH:mm:ss")) ]
                        Button.create [
                            Button.text "Show an error"
                            Button.onClick (fun _ -> toaster.Show [ Toast.kind ToastKind.Error; Toast.text "Couldn't reach the server" ])
                        ]
                        Button.create [
                            Button.text "With an action"
                            Button.onClick (fun _ -> toaster.Show [ Toast.text "Message deleted"; Toast.action ("Undo", fun () -> toaster.Show "Message restored") ])
                        ]
                    ]
                ])
            section "Dialog" "The window dims; nothing below reacts to the pointer, the wheel or the keyboard, and Tab stays in the dialog. Escape closes the first; the second must be answered." (
                StackPanel.create [
                    StackPanel.spacing 8.0
                    StackPanel.children [
                        WrapPanel.create [
                            WrapPanel.spacing 8.0
                            WrapPanel.lineSpacing 8.0
                            WrapPanel.children [
                                Button.create [ Button.text "Open a dialog"; Button.onClick (fun _ -> dialogOpen.Set true) ]
                                Button.create [ Button.text "Ask a question"; Button.onClick (fun _ -> questionOpen.Set true) ]
                            ]
                        ]
                        TextBlock.create [ TextBlock.text answer.Current; TextBlock.foreground Palette.textDim ]
                        // Dialogs stand anywhere in the view and take no space there.
                        Dialog.create [
                            Dialog.isOpen dialogOpen.Current
                            Dialog.onOpenChanged dialogOpen.Set
                            Dialog.lightDismiss true
                            Dialog.title "A dialog"
                            Dialog.content (
                                dialogBody "Until this closes, the app below can't be clicked, scrolled or reached with Tab. Escape or a click outside closes it; a toast still shows above it." [
                                    Button.create [ Button.text "Toast"; Button.onClick (fun _ -> toaster.Show "From the dialog") ]
                                    Button.create [ Button.text "Close"; Button.onClick (fun _ -> dialogOpen.Set false) ]
                                ])
                        ]
                        Dialog.create [
                            Dialog.isOpen questionOpen.Current
                            Dialog.closeOnEscape false
                            Dialog.title "Delete the draft?"
                            Dialog.content (
                                dialogBody "This one must be answered: Escape and clicks outside do nothing." [
                                    Button.create [
                                        Button.text "Keep"
                                        Button.onClick (fun _ ->
                                            answer.Set "Kept the draft"
                                            questionOpen.Set false)
                                    ]
                                    Button.create [
                                        Button.text "Delete"
                                        Button.onClick (fun _ ->
                                            answer.Set "Deleted the draft"
                                            questionOpen.Set false)
                                    ]
                                ])
                        ]
                    ]
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
