namespace Scribble.Core

open System
open System.Collections.Generic
open System.Threading.Tasks
open Scribble.Rendering
open Scribble.Text

/// How serious what a confirmation asks about is: the look of its accept button, and which button
/// has focus when it opens.
[<RequireQualifiedAccess>]
type Severity =
    /// An ordinary question (default): the accept button in the accent colour, with focus.
    | Informational
    /// Something to be careful about: the accept button in the warning colour, with focus.
    | Warning
    /// Something that can't be undone: the accept button in the danger colour, and focus on cancel,
    /// so Enter doesn't do it by accident.
    | Critical

    // Written out: a union's generated ToString uses %A, which NativeAOT can't run.
    override s.ToString() =
        match s with
        | Informational -> "Informational"
        | Warning -> "Warning"
        | Critical -> "Critical"

type internal InformOptions = { Title: string; Text: string; Button: string; OnClosed: (unit -> unit) voption }

/// One setting for `dialogs.Inform [ ... ]`.
[<Struct>]
type InformSetting = internal InformSetting of (InformOptions -> InformOptions)

/// The settings of a dialog that tells the user something:
/// `dialogs.Inform [ Inform.title "Couldn't open the file"; Inform.text e.Message ]`.
[<AbstractClass; Sealed>]
type Inform =
    /// A heading (and the dialog's name for screen readers).
    static member title(value: string) = InformSetting(fun o -> { o with Title = value })
    /// What it says.
    static member text(value: string) = InformSetting(fun o -> { o with Text = value })
    /// The button's text (default "OK").
    static member button(value: string) = InformSetting(fun o -> { o with Button = value })
    /// Called when it's closed (the button, or Escape).
    static member onClosed(handler: unit -> unit) = InformSetting(fun o -> { o with OnClosed = ValueSome handler })

type internal ConfirmOptions =
    { Title: string
      Text: string
      Accept: string
      Cancel: string
      Severity: Severity
      OnAccept: (unit -> unit) voption
      OnCancel: (unit -> unit) voption }

/// One setting for `dialogs.Confirm [ ... ]`.
[<Struct>]
type ConfirmSetting = internal ConfirmSetting of (ConfirmOptions -> ConfirmOptions)

/// A confirmation's settings:
/// `dialogs.Confirm [ Confirm.title "Delete the draft?"; Confirm.accept "Delete"; Confirm.severity Severity.Critical ]`.
[<AbstractClass; Sealed>]
type Confirm =
    /// The question, as a heading (and the dialog's name for screen readers).
    static member title(value: string) = ConfirmSetting(fun o -> { o with Title = value })
    /// More about it, under the heading.
    static member text(value: string) = ConfirmSetting(fun o -> { o with Text = value })
    /// The accept button's text (default "OK"): the answer is `true`.
    static member accept(value: string) = ConfirmSetting(fun o -> { o with Accept = value })
    /// The cancel button's text (default "Cancel"): the answer is `false`, as for Escape.
    static member cancel(value: string) = ConfirmSetting(fun o -> { o with Cancel = value })
    /// Informational (default), Warning or Critical: the accept button's colour, and where focus starts
    /// (on cancel for Critical).
    static member severity(value: Severity) = ConfirmSetting(fun o -> { o with Severity = value })
    /// Called when the accept button is clicked.
    static member onAccept(handler: unit -> unit) = ConfirmSetting(fun o -> { o with OnAccept = ValueSome handler })
    /// Called when the cancel button is clicked, or Escape pressed.
    static member onCancel(handler: unit -> unit) = ConfirmSetting(fun o -> { o with OnCancel = ValueSome handler })

type internal AskOptions<'T> =
    { Title: string
      Text: string
      Options: struct ('T * string) list
      /// Called with the value of the option picked (the choices of `Ask.choice` run themselves through it).
      OnChoice: ('T -> unit) voption
      OnDismiss: (unit -> unit) voption }

/// One setting for `dialogs.Ask [ ... ]`.
[<Struct>]
type AskSetting<'T> = internal AskSetting of (AskOptions<'T> -> AskOptions<'T>)

/// A choice's settings: `dialogs.Ask [ Ask.title "Unsaved changes"; Ask.option (Save, "Save"); Ask.option (Discard, "Discard") ]`.
[<AbstractClass; Sealed>]
type Ask =
    /// The question, as a heading (and the dialog's name for screen readers).
    static member title<'T>(value: string) : AskSetting<'T> = AskSetting(fun o -> { o with Title = value })
    /// More about it, under the heading.
    static member text<'T>(value: string) : AskSetting<'T> = AskSetting(fun o -> { o with Text = value })
    /// One choice: the value the answer gives, and its button's text. Buttons stand in the order given; the first has focus.
    static member option<'T>(value: 'T, label: string) : AskSetting<'T> = AskSetting(fun o -> { o with Options = o.Options @ [ struct (value, label) ] })
    /// One choice that does something: its button's text and what picking it does (`ctx.useAsk [ Ask.choice ("Save", fun () -> ...) ]`).
    static member choice(label: string, handler: unit -> unit) : AskSetting<unit -> unit> =
        AskSetting(fun o -> { o with Options = o.Options @ [ struct (handler, label) ]; OnChoice = ValueSome(fun (picked: unit -> unit) -> picked ()) })
    /// Called when it's dismissed with Escape (no choice).
    static member onDismiss<'T>(handler: unit -> unit) : AskSetting<'T> = AskSetting(fun o -> { o with OnDismiss = ValueSome handler })

type internal DialogOptions =
    { Title: string
      Content: View voption
      CloseOnEscape: bool
      LightDismiss: bool
      OnClosed: (unit -> unit) voption }

/// One setting for `ctx.useDialog (fun close -> [ ... ])`.
[<Struct>]
type DialogSetting = internal DialogSetting of (DialogOptions -> DialogOptions)

/// The settings of a dialog with content of your own (`ctx.useDialog`):
///
///     let rename =
///         ctx.useDialog (fun close -> [
///             Dialog.title "Rename the file"
///             Dialog.content (renameForm name close)
///         ])
///     Button.onClick (fun _ -> rename ())
[<AbstractClass; Sealed>]
type Dialog =
    /// A heading above the content (and the dialog's name for screen readers).
    static member title(value: string) = DialogSetting(fun o -> { o with Title = value })
    /// What it shows: your view. It follows the component's state while the dialog is open.
    static member content(view: View) = DialogSetting(fun o -> { o with Content = ValueSome view })
    /// False: Escape doesn't close it (default true).
    static member closeOnEscape(value: bool) = DialogSetting(fun o -> { o with CloseOnEscape = value })
    /// True: a press outside the box closes it too (default false).
    static member lightDismiss(value: bool) = DialogSetting(fun o -> { o with LightDismiss = value })
    /// Called when it closes, however that happened: `close ()`, Escape, a press outside.
    static member onClosed(handler: unit -> unit) = DialogSetting(fun o -> { o with OnClosed = ValueSome handler })
    /// On something focusable inside the content: it takes focus when the dialog opens, in place of the first Tab
    /// stop (`Button.create [ Dialog.initialFocus true; Button.text "Save" ]`).
    static member initialFocus<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create ModalProps.initialFocus value
    /// A style for every dialog's box (Inform, Confirm, Ask and your own; not Modals written in the view):
    /// `Dialog.style [] [ Modal.cornerRadius 12.0 ]`. The box is a Modal element, so the settings are Modal's.
    static member style (parts: SelectorPart list) (settings: Setting<Modal> list) : Style = Modal.style (Style.cls "dialog" :: parts) settings

/// One dialog waiting or showing: its view, how to give up on it, and (once a window has it)
/// how to say it was answered.
[<Sealed>]
type internal DialogRequest(view: unit -> View, cancel: unit -> unit) =
    member _.View = view
    member _.Cancel = cancel
    member val Answered: DialogRequest -> unit = ignore with get, set

/// One window's dialogs: the one showing and those waiting. The UI thread only.
[<Sealed>]
type internal DialogQueue(dispatcher: Dispatcher, update: View -> unit) =
    let waiting = Queue<DialogRequest>()
    let mutable current: DialogRequest voption = ValueNone

    /// Nothing showing: a Modal's place with no modal in it.
    static member Nothing: View = StackPanel.create [ StackPanel.isVisible false ]

    member _.Dispatcher = dispatcher
    member _.Count = waiting.Count + (if current.IsSome then 1 else 0)

    member private this.ShowNext() =
        if current.IsNone && waiting.Count > 0 then
            let request = waiting.Dequeue()
            current <- ValueSome request
            update (request.View())

    member this.Add(request: DialogRequest) =
        request.Answered <- this.Done
        waiting.Enqueue request
        this.ShowNext()

    /// The showing dialog was answered: it closes, and the next one (if any) opens afresh on the next turn.
    member private this.Done(request: DialogRequest) =
        match current with
        | ValueSome c when obj.ReferenceEquals(c, request) ->
            current <- ValueNone
            update DialogQueue.Nothing
            dispatcher.Post this.ShowNext
        | _ -> ()

    /// The showing dialog's view changed (a live one: `ctx.useDialog`): shown again if it's this one.
    member _.Refresh(request: DialogRequest) =
        match current with
        | ValueSome c when obj.ReferenceEquals(c, request) -> update (request.View())
        | _ -> ()

    /// The window is closing: what's open or waiting is cancelled (their tasks end as cancelled).
    member _.CancelAll() =
        current |> ValueOption.iter (fun r -> r.Cancel())
        current <- ValueNone
        for r in waiting do
            r.Cancel()
        waiting.Clear()

/// A window's dialogs: questions with fixed content, asked as actions that give back an
/// answer (DESIGN.md, "4d", rounds 2 and 3). Every window has one; get it where you are:
///
///     let dialogs = ctx.useDialogs ()                       // in a component
///     task {
///         let! delete = dialogs.Confirm [ Confirm.title "Delete the draft?"; Confirm.accept "Delete"; Confirm.severity Severity.Critical ]
///         if delete then dispatch DeleteDraft
///     }
///
///     model, Cmd.confirm [ Confirm.title "Delete the draft?" ] DeleteAnswered     // from an Elmish update
///
/// `Inform` tells (one button), `Confirm` asks yes or no, `Ask` offers choices of your own type
/// (`ctx.useInform ()`, `ctx.useConfirm ()` and `ctx.useAsk ()` give each call on its own). Escape
/// closes an Inform, is cancel for a Confirm and no choice for an Ask; a press outside does nothing.
/// Two asked at once show one after the other. Safe from any thread. For a dialog whose content
/// follows the model (a form, a picker), put a `Modal` in the view.
[<Sealed>]
type Dialogs internal (ui: unit -> Dispatcher voption, find: unit -> DialogQueue voption) =

    /// A dialog with a heading, a text and buttons at the right. `buttons` gets `answer`: what a button calls with
    /// its answer and its handler (if any). `onEscape` is what Escape answers, `onDismiss` its handler.
    member private this.Show<'R>(title: string, text: string, buttons: (('R -> (unit -> unit) voption -> unit) -> View list), onEscape: 'R, onDismiss: (unit -> unit) voption) : Task<'R> =
        let result = TaskCompletionSource<'R>()
        let mutable request = Unchecked.defaultof<DialogRequest>
        // The dialog closes, the handler runs (an error in it goes where the click's or the key's would), and the
        // task completes whatever the handler did.
        let answer (value: 'R) (handler: (unit -> unit) voption) =
            request.Answered request
            try
                handler |> ValueOption.iter (fun h -> h ())
            finally
                result.TrySetResult value |> ignore
        let view () =
            Modal.create [
                Modal.isOpen true
                Modal.onOpenChanged (fun isOpen ->
                    if not isOpen then answer onEscape onDismiss)
                Modal.title title
                // So styles can tell a dialog's box from other modals: `Modal.style [ Style.cls "dialog" ] [ ... ]`.
                Modal.classes [ "dialog" ]
                Modal.minWidth 280.0
                Modal.content (
                    StackPanel.create [
                        StackPanel.spacing 16.0
                        StackPanel.children [
                            if text <> "" then TextBlock.create [ TextBlock.text text; TextBlock.textWrapping Wrap ]
                            WrapPanel.create [
                                WrapPanel.horizontalAlignment HorizontalAlignment.Right
                                WrapPanel.spacing 8.0
                                WrapPanel.lineSpacing 8.0
                                WrapPanel.children (buttons answer)
                            ]
                        ]
                    ])
            ]
        request <- DialogRequest(view, fun () -> result.TrySetCanceled() |> ignore)
        let noWindow () =
            // Not in a window (a test's bare tree, a view rendered to an image): nobody can answer.
#if DEBUG
            DebugWarnings.Once("dialog-no-window", "A dialog was asked for from a view that isn't in a window: its task is cancelled.")
#endif
            result.TrySetCanceled() |> ignore
        this.Post(request, noWindow)
        result.Task

    /// Queues a dialog in the window, on the UI thread (the window's dialogs and their layer are made there, the
    /// first time); `noWindow` runs instead when no window shows this view.
    member internal _.Post(request: DialogRequest, noWindow: unit -> unit) =
        match ui () with
        | ValueSome dispatcher ->
            dispatcher.Post(fun () ->
                match find () with
                | ValueSome queue -> queue.Add request
                | ValueNone -> noWindow ())
        | ValueNone -> noWindow ()

    /// A showing dialog's view changed: the window shows it again (on the UI thread, after this turn).
    member internal _.Refresh(request: DialogRequest) =
        match ui () with
        | ValueSome dispatcher -> dispatcher.Post(fun () -> find () |> ValueOption.iter (fun queue -> queue.Refresh request))
        | ValueNone -> ()

    /// Tells the user something; one button. The task ends when it's closed (the button, or Escape).
    member this.Inform(text: string) : Task = this.Inform [ Inform.text text ]

    /// Tells the user something: `dialogs.Inform [ Inform.title "Couldn't open the file"; Inform.text e.Message ]`.
    member this.Inform(settings: InformSetting list) : Task =
        let o = settings |> List.fold (fun o (InformSetting apply) -> apply o) { Title = ""; Text = ""; Button = "OK"; OnClosed = ValueNone }
        let buttons (answer: unit -> (unit -> unit) voption -> unit) =
            [ Button.create [ Button.text o.Button; Button.minWidth 72.0; Button.onClick (fun _ -> answer () o.OnClosed) ] ]
        this.Show<unit>(o.Title, o.Text, buttons, (), o.OnClosed) :> Task

    /// Asks yes or no: `true` for the accept button, `false` for cancel or Escape.
    member this.Confirm(settings: ConfirmSetting list) : Task<bool> =
        let defaults = { Title = ""; Text = ""; Accept = "OK"; Cancel = "Cancel"; Severity = Severity.Informational; OnAccept = ValueNone; OnCancel = ValueNone }
        let o = settings |> List.fold (fun o (ConfirmSetting apply) -> apply o) defaults
        let color =
            match o.Severity with
            | Severity.Informational -> Palette.accent
            | Severity.Warning -> Palette.warning
            | Severity.Critical -> Palette.danger
        let critical = o.Severity = Severity.Critical
        let buttons (answer: bool -> (unit -> unit) voption -> unit) =
            [ Button.create [
                  Button.text o.Cancel
                  Button.minWidth 72.0
                  // Something that can't be undone: Enter on opening cancels.
                  Modal.initialFocus critical
                  Button.onClick (fun _ -> answer false o.OnCancel)
              ]
              Button.create [
                  Button.text o.Accept
                  Button.minWidth 72.0
                  Button.background color
                  Button.borderBrush color
                  Button.foreground Palette.onAccent
                  Modal.initialFocus (not critical)
                  Button.onClick (fun _ -> answer true o.OnAccept)
              ] ]
        this.Show<bool>(o.Title, o.Text, buttons, false, o.OnCancel)

    /// Offers choices of your own type: the one picked, or none for Escape.
    member this.Ask<'T>(settings: AskSetting<'T> list) : Task<'T voption> =
        let o = settings |> List.fold (fun o (AskSetting apply) -> apply o) { Title = ""; Text = ""; Options = []; OnChoice = ValueNone; OnDismiss = ValueNone }
        let buttons (answer: 'T voption -> (unit -> unit) voption -> unit) =
            [ for struct (value, label) in o.Options ->
                  Button.create [
                      Button.text label
                      Button.minWidth 72.0
                      Button.onClick (fun _ -> answer (ValueSome value) (o.OnChoice |> ValueOption.map (fun picked -> fun () -> picked value)))
                  ] ]
        this.Show<'T voption>(o.Title, o.Text, buttons, ValueNone, o.OnDismiss)

    /// How many the window holds (showing or waiting). For tests and tools; the UI thread.
    member _.Count =
        match find () with
        | ValueSome queue -> queue.Count
        | ValueNone -> 0

/// A dialog with content of the app's own, kept by `ctx.useDialog`: opened and closed by calls, and
/// shown again whenever its component runs while it's open, so its content follows the component.
[<Sealed>]
type internal LiveDialog(dialogs: Dialogs) =
    let mutable request: DialogRequest voption = ValueNone
    /// The settings as of the component's last run (given `close`).
    member val Settings: unit -> DialogSetting list = (fun () -> []) with get, set
    /// What it was opened with (the form with an argument).
    member val Argument: obj = null with get, set

    member _.IsOpen = request.IsSome

    member private this.Options() =
        let defaults = { Title = ""; Content = ValueNone; CloseOnEscape = true; LightDismiss = false; OnClosed = ValueNone }
        this.Settings() |> List.fold (fun o (DialogSetting apply) -> apply o) defaults

    member private this.View() : View =
        let o = this.Options()
        Modal.create [
            Modal.isOpen true
            // Escape, or a press outside with lightDismiss.
            Modal.onOpenChanged (fun isOpen -> if not isOpen then this.Close())
            Modal.title o.Title
            Modal.classes [ "dialog" ]
            Modal.closeOnEscape o.CloseOnEscape
            Modal.lightDismiss o.LightDismiss
            match o.Content with
            | ValueSome view -> Modal.content view
            | ValueNone -> ()
        ]

    member this.Open() =
        if request.IsNone then
            let r = DialogRequest(this.View, fun () -> request <- ValueNone)
            request <- ValueSome r
            // Not in a window: nothing opens.
            dialogs.Post(r, fun () -> request <- ValueNone)

    /// Closes it (if it's open), then tells `Dialog.onClosed`.
    member this.Close() =
        match request with
        | ValueSome r ->
            let closed = this.Options().OnClosed
            request <- ValueNone
            r.Answered r
            closed |> ValueOption.iter (fun handler -> handler ())
        | ValueNone -> ()

    /// Its component ran: if it's open, it shows this run's content.
    member _.Refresh() = request |> ValueOption.iter dialogs.Refresh

    /// Its component went away: it closes without a word.
    member _.Drop() =
        match request with
        | ValueSome r ->
            request <- ValueNone
            r.Answered r
        | ValueNone -> ()

/// What a host offers the components in its trees and the commands of its program: its toasts and
/// its dialogs (made the first time something asks).
[<Sealed>]
type internal HostServices() =
    /// Set by the host once it can make them; until then (and in a tree no host runs) there are none.
    member val FindToasts: unit -> ToastStack voption = (fun () -> ValueNone) with get, set
    member val FindDialogs: unit -> DialogQueue voption = (fun () -> ValueNone) with get, set

    /// The services a tree or an app's values carry, if a host runs them.
    static member Of(services: obj) : HostServices voption =
        match services with
        | :? HostServices as s -> ValueSome s
        | _ -> ValueNone

/// Commands for an Elmish `update` to show its window's toasts and dialogs.
[<AutoOpen>]
module DialogCommands =
    module Cmd =
        let private toaster (values: AppValues) = Toaster((fun () -> values.Dispatcher), fun () -> HostServices.Of values.Services |> ValueOption.bind (fun s -> s.FindToasts()))
        let private dialogs (values: AppValues) = Dialogs((fun () -> values.Dispatcher), fun () -> HostServices.Of values.Services |> ValueOption.bind (fun s -> s.FindDialogs()))

        /// Sends `ofResult answer` when the task ends; a cancelled one (the window closed) sends nothing.
        let private answered (work: Task<'R>) (ofResult: 'R -> 'Msg) (dispatch: 'Msg -> unit) =
            // On the thread that answers (the UI thread), so the message is sent at once.
            work.ContinueWith((fun (t: Task<'R>) -> if t.IsCompletedSuccessfully then dispatch (ofResult t.Result)), TaskContinuationOptions.ExecuteSynchronously)
            |> ignore

        /// Shows a toast saying `text`: `model, Cmd.toast "Saved"`.
        let toast (text: string) : Cmd<'Msg> = Cmd [ fun values _ -> (toaster values).Show text ]

        /// Shows a toast: `Cmd.toastWith [ Toast.kind ToastKind.Error; Toast.text "Couldn't save" ]`.
        let toastWith (settings: ToastSetting list) : Cmd<'Msg> = Cmd [ fun values _ -> (toaster values).Show settings ]

        /// Tells the user something, and sends `msg` once it's closed: `Cmd.inform [ Inform.text "Done." ] Acknowledged`.
        let inform (settings: InformSetting list) (msg: 'Msg) : Cmd<'Msg> =
            Cmd [ fun values dispatch ->
                    let work = (dialogs values).Inform settings
                    work.ContinueWith((fun (t: Task) -> if t.IsCompletedSuccessfully then dispatch msg), TaskContinuationOptions.ExecuteSynchronously)
                    |> ignore ]

        /// Asks yes or no, and sends `ofAnswer true` or `ofAnswer false`: `Cmd.confirm [ Confirm.title "Delete?" ] DeleteAnswered`.
        let confirm (settings: ConfirmSetting list) (ofAnswer: bool -> 'Msg) : Cmd<'Msg> =
            Cmd [ fun values dispatch -> answered ((dialogs values).Confirm settings) ofAnswer dispatch ]

        /// Offers choices, and sends `ofChoice` with the one picked (none for Escape).
        let ask (settings: AskSetting<'T> list) (ofChoice: 'T voption -> 'Msg) : Cmd<'Msg> =
            Cmd [ fun values dispatch -> answered ((dialogs values).Ask settings) ofChoice dispatch ]
