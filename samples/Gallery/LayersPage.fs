/// The Layers page: toasts and dialogs (the window's own, through hooks), a Modal written in the view, flyouts and
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

/// The modal's buttons, at the right.
let private buttonRow (buttons: View list) =
    StackPanel.create [
        StackPanel.orientation Horizontal
        StackPanel.spacing 8.0
        StackPanel.horizontalAlignment HorizontalAlignment.Right
        StackPanel.children buttons
    ]

/// Some text over a row of buttons.
let private modalBody (text: string) (buttons: View list) =
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
let private actionsMenu (toaster: Toaster) (isOpen: State<bool>) : Setting<Flyout> list =
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

/// The places the page offers for the toasts.
let private placements =
    [| for vName, v in [ "Bottom", VerticalAlignment.Bottom; "Top", VerticalAlignment.Top ] do
           for hName, h in [ "right", HorizontalAlignment.Right; "centre", HorizontalAlignment.Center; "left", HorizontalAlignment.Left; "across", HorizontalAlignment.Stretch ] ->
               vName + " " + hName, { Horizontal = h; Vertical = v } |]

/// The page: a component, so it can keep whether the Actions menu is open.
let view =
    Component.define "LayersPage" (fun ctx () ->
        // This window's toasts.
        let toaster = ctx.useToaster ()
        let placement = ctx.useAppValue Toasts.placement
        let actionsOpen = ctx.useState false
        let modalOpen = ctx.useState false
        let answer = ctx.useState "Nothing asked yet"
        // Dialogs: each is declared here with what its answers do, and opened from a button below.
        let confirmSend =
            ctx.useConfirm [
                Confirm.title "Send the report?"
                Confirm.text "It goes to everyone on the list."
                Confirm.accept "Send"
                Confirm.onAccept (fun () -> answer.Set "Sent the report")
                Confirm.onCancel (fun () -> answer.Set "Didn't send it")
            ]
        let confirmLeave =
            ctx.useConfirm [
                Confirm.title "Leave without saving?"
                Confirm.text "Your changes will be lost."
                Confirm.accept "Leave"
                Confirm.cancel "Stay"
                Confirm.severity Severity.Warning
                Confirm.onAccept (fun () -> answer.Set "Left without saving")
                Confirm.onCancel (fun () -> answer.Set "Stayed")
            ]
        // With an argument: what's asked depends on what the button passes.
        let confirmDelete =
            ctx.useConfirm (fun (what: string) -> [
                Confirm.title ("Delete " + what + "?")
                Confirm.text "It can't be brought back. Focus starts on Keep."
                Confirm.accept "Delete"
                Confirm.cancel "Keep"
                Confirm.severity Severity.Critical
                Confirm.onAccept (fun () -> answer.Set("Deleted " + what))
                Confirm.onCancel (fun () -> answer.Set("Kept " + what))
            ])
        let askUnsaved =
            ctx.useAsk [
                Ask.title "Unsaved changes"
                Ask.text "Save them before closing?"
                Ask.choice ("Save", fun () -> answer.Set "Saved the changes")
                Ask.choice ("Discard", fun () -> answer.Set "Discarded the changes")
                Ask.choice ("Cancel", fun () -> answer.Set "Went back")
                Ask.onDismiss (fun () -> answer.Set "Escape: no choice")
            ]
        // A dialog with content of its own: it follows this component's state while it's open.
        let taps = ctx.useState 0
        let counting =
            ctx.useDialog (fun close -> [
                Dialog.title "A dialog of your own"
                Dialog.content (
                    modalBody ("Tapped " + string taps.Current + " times. The count is the page's state: the dialog follows it while it's open.") [
                        Button.create [ Button.text "Tap"; Button.onClick (fun _ -> taps.Set(taps.Current + 1)) ]
                        Button.create [ Button.text "Done"; Dialog.initialFocus true; Button.onClick (fun _ -> close ()) ]
                    ])
                Dialog.onClosed (fun () -> answer.Set("Closed the custom dialog at " + string taps.Current))
            ])
        let sessionExpired =
            ctx.useInform [
                Inform.title "Your session has expired"
                Inform.text "Sign in again to carry on."
                Inform.onClosed (fun () -> answer.Set "Was informed")
            ]
        // A sequence of questions, each depending on the last answer: the window's Dialogs, whose calls give the
        // answer back as a task. (The hooks above declare one dialog each; this is for asking one after another.)
        let dialogs = ctx.useDialogs ()
        let closeDraft () =
            task {
                let! choice =
                    dialogs.Ask [
                        Ask.title "Close the draft?"
                        Ask.text "It has unsaved changes."
                        Ask.option ("save", "Save")
                        Ask.option ("discard", "Discard")
                        Ask.option ("stay", "Cancel")
                    ]
                match choice with
                | ValueSome "save" -> answer.Set "Saved, then closed"
                | ValueSome "discard" ->
                    // Only asked when the first answer was Discard.
                    let! sure =
                        dialogs.Confirm [
                            Confirm.title "Discard the changes?"
                            Confirm.text "They can't be brought back."
                            Confirm.accept "Discard"
                            Confirm.cancel "Keep"
                            Confirm.severity Severity.Critical
                        ]
                    answer.Set(if sure then "Discarded, then closed" else "Kept the draft open")
                | _ -> answer.Set "Stayed in the draft"
            }
            |> ignore
        page "Layers" "Content above the app, each in a layer of its own: toasts that never take the keyboard, modals and dialogs that take everything until they close, flyouts and tooltips." [
            section "Toasts" "At the bottom right (across the bottom of a narrow window), newest lowest, three at a time. One stays 4 seconds (a warning 8) and waits while the pointer is on it; a critical one stays until closed. The same toast again is counted, not stacked; what waits shows as \"+N more\" with Close all. The box chooses where they stack. The app keeps working everywhere but on a toast." (
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.lineSpacing 8.0
                    WrapPanel.children [
                        // Where the toasts stack: an app value, so the page can change it while toasts show.
                        ComboBox.create [
                            Semantics.label "Toast placement"
                            ComboBox.width 170.0
                            ComboBox.source placements
                            ComboBox.keySelector (fun (name: string, _: ToastPlacement) -> name)
                            ComboBox.viewSelector (fun (name: string, _: ToastPlacement) -> name)
                            ComboBox.selected (placements |> Array.tryFind (fun (_, p) -> p = placement.Current) |> Option.map fst |> ValueOption.ofOption)
                            ComboBox.onSelectedChanged (fun (e: SelectionChangedEventArgs<string * ToastPlacement, string>) ->
                                e.Item |> ValueOption.iter (fun (_, p) -> placement.Set p))
                        ]
                        Button.create [ Button.text "Show a toast"; Button.onClick (fun _ -> toaster.Show("Toast at " + DateTime.Now.ToString "HH:mm:ss")) ]
                        Button.create [
                            Button.text "Show a warning"
                            Button.onClick (fun _ -> toaster.Show [ Toast.severity Severity.Warning; Toast.text "The disk is almost full" ])
                        ]
                        Button.create [
                            Button.text "Show an error"
                            Button.onClick (fun _ -> toaster.Show [ Toast.severity Severity.Critical; Toast.text "Couldn't reach the server" ])
                        ]
                        Button.create [
                            Button.text "Many at once"
                            Button.onClick (fun _ ->
                                // The same error five times is one toast, counted; the rest queue behind "+N more".
                                for _ in 1..5 do
                                    toaster.Show [ Toast.severity Severity.Critical; Toast.text "Couldn't reach the server" ]
                                for name in [ "report.pdf"; "notes.txt"; "photo.png"; "data.csv"; "draft.md" ] do
                                    toaster.Show("Saved " + name))
                        ]
                        Button.create [
                            Button.text "With an action"
                            Button.onClick (fun _ -> toaster.Show [ Toast.text "Message deleted"; Toast.action ("Undo", fun () -> toaster.Show "Message restored") ])
                        ]
                    ]
                ])
            section "Modal and dialogs" "The window dims; nothing below reacts to the pointer, the wheel or the keyboard, and Tab stays inside. The first is a Modal, written in the view with content of its own; the others are dialogs, declared with hooks (ctx.useConfirm, useAsk, useInform, and useDialog for content of your own), each saying what its answers do. \"In sequence\" asks one after another through ctx.useDialogs, whose calls give back the answer." (
                StackPanel.create [
                    StackPanel.spacing 8.0
                    StackPanel.children [
                        WrapPanel.create [
                            WrapPanel.spacing 8.0
                            WrapPanel.lineSpacing 8.0
                            WrapPanel.children [
                                Button.create [ Button.text "Open a modal"; Button.onClick (fun _ -> modalOpen.Set true) ]
                                Button.create [ Button.text "Confirm"; Button.onClick (fun _ -> confirmSend ()) ]
                                Button.create [ Button.text "Warning"; Button.onClick (fun _ -> confirmLeave ()) ]
                                Button.create [ Button.text "Critical"; Button.onClick (fun _ -> confirmDelete "the draft") ]
                                Button.create [ Button.text "Three choices"; Button.onClick (fun _ -> askUnsaved ()) ]
                                Button.create [ Button.text "Inform"; Button.onClick (fun _ -> sessionExpired ()) ]
                                Button.create [ Button.text "Custom"; Button.onClick (fun _ -> counting ()) ]
                                Button.create [ Button.text "In sequence"; Button.onClick (fun _ -> closeDraft ()) ]
                            ]
                        ]
                        TextBlock.create [ TextBlock.text answer.Current; TextBlock.foreground Palette.textDim ]
                        // A Modal stands anywhere in the view and takes no space there.
                        Modal.create [
                            Modal.isOpen modalOpen.Current
                            Modal.onOpenChanged modalOpen.Set
                            Modal.lightDismiss true
                            Modal.title "A modal"
                            Modal.content (
                                modalBody "Until this closes, the app below can't be clicked, scrolled or reached with Tab. Escape or a click outside closes it; a toast still shows above it." [
                                    Button.create [ Button.text "Toast"; Button.onClick (fun _ -> toaster.Show "From the modal") ]
                                    Button.create [ Button.text "Close"; Button.onClick (fun _ -> modalOpen.Set false) ]
                                ])
                        ]
                    ]
                ])
            section "Flyout" "Content in its own layer next to a button. Escape or a click elsewhere closes it (that click does nothing else); focus moves in, and back when it closes. The menu is controlled: choosing an item closes it." (
                Button.create [
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Button.text "Actions"
                    Button.flyout (actionsMenu toaster actionsOpen)
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
