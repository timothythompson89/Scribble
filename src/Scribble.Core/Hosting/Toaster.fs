namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Accessibility
open Scribble.Rendering
open Scribble.Text

/// How serious something told or asked is. A toast's look and how long it stays; a confirmation's
/// accept button and where its focus starts.
[<RequireQualifiedAccess>]
type Severity =
    /// Ordinary (default). A toast in the surface colour that goes after 4 seconds; a confirmation's accept
    /// button in the accent colour, with focus.
    | Informational
    /// Something to be careful about. A toast in the warning colour that goes after 8 seconds; a
    /// confirmation's accept button in the warning colour, with focus.
    | Warning
    /// Something that went wrong or can't be undone. A toast in the danger colour that stays until closed;
    /// a confirmation's accept button in the danger colour, with focus on cancel, so Enter doesn't do it
    /// by accident.
    | Critical

    // Written out: a union's generated ToString uses %A, which NativeAOT can't run.
    override s.ToString() =
        match s with
        | Informational -> "Informational"
        | Warning -> "Warning"
        | Critical -> "Critical"

/// What the Toast settings configure.
type internal ToastOptions =
    { Text: string
      Severity: Severity
      Duration: TimeSpan voption
      Action: struct (string * (unit -> unit)) voption
      Content: View voption }

/// One setting for a toast: `Toast.text "Saved"`, `Toast.severity Severity.Critical`.
[<Struct>]
type ToastSetting = internal ToastSetting of (ToastOptions -> ToastOptions)

/// A toast's settings, for `toaster.Show [ ... ]`:
///
///     toaster.Show [ Toast.text "Message deleted"; Toast.action ("Undo", fun () -> dispatch Undo) ]
///     toaster.Show [ Toast.severity Severity.Critical; Toast.text e.Message ]
[<AbstractClass; Sealed>]
type Toast =
    /// What it says.
    static member text(value: string) = ToastSetting(fun o -> { o with Text = value })
    /// Informational (default), Warning (the theme's warning colour) or Critical (the danger colour; it
    /// stays until closed).
    static member severity(value: Severity) = ToastSetting(fun o -> { o with Severity = value })
    /// How long it stays (default: 4 seconds, 8 for a Warning; a Critical one stays until closed). The
    /// time starts again when the pointer leaves it.
    static member duration(value: TimeSpan) = ToastSetting(fun o -> { o with Duration = ValueSome value })
    /// A button after the text ("Undo"); clicking it closes the toast.
    static member action(label: string, handler: unit -> unit) = ToastSetting(fun o -> { o with Action = ValueSome(struct (label, handler)) })
    /// A view in place of the text (give `text` too: it stays the toast's name for screen readers).
    static member content(view: View) = ToastSetting(fun o -> { o with Content = ValueSome view })
    /// A style for toasts' boxes: `Toast.style [] [ Border.cornerRadius 12.0 ]` (a toast's box is a Border underneath). A Warning toast has
    /// the class `warning` and a Critical one `critical`:
    /// `Toast.style [ Style.cls "critical" ] [ Border.background c ]`.
    static member style (parts: SelectorPart list) (settings: Setting<ToastPresenter> list) : Style = ToastPresenter.style parts settings

/// What the Toaster's toasts share.
module internal ToastLooks =
    let defaultDuration = TimeSpan.FromSeconds 4.0
    /// A Warning stays twice as long.
    let warningDuration = TimeSpan.FromSeconds 8.0
    let maxShown = 3
    /// The most that wait for a place; past it the oldest go (those that aren't Critical first).
    let maxWaiting = 50
    /// How far a toast slides as it comes and goes, in DIPs.
    let slide = 12.0
    /// A bold cross, in a 24 × 24 box.
    let cross = Icon.fromPath "M6.4 5 12 10.6 17.6 5 19 6.4 13.4 12 19 17.6 17.6 19 12 13.4 6.4 19 5 17.6 10.6 12 5 6.4z" [ Icon.name "close" ]

/// One toast the Toaster holds.
[<Sealed>]
type internal ToastItem(id: int, options: ToastOptions) =
    member _.Id = id
    member _.Options = options
    /// 0 waiting for room, 1 coming (clear, below its place), 2 shown, 3 going.
    member val Stage = 0 with get, set
    member val Hovered = false with get, set
    /// Bumped whenever its timer starts again or stops: a timer that sees another number is stale.
    member val Generation = 0 with get, set
    /// How many times it was shown: the same toast shown again while this one shows or waits adds here.
    member val Count = 1 with get, set
    /// When it got its place among those showing (they stand in that order, newest lowest).
    member val Order = 0 with get, set

/// One host's toasts: the list, their timers and the view of their layer. The UI thread only.
[<Sealed>]
type internal ToastStack(dispatcher: Dispatcher, update: View -> unit) =
    let items = List<ToastItem>()
    let mutable nextId = 0
    let mutable nextOrder = 0

    member _.Dispatcher = dispatcher
    member _.Count = items.Count

    member private this.View() : View =
        let quiet (severity: Severity) : Setting<Button> list =
            [ Button.background Color.Transparent
              Button.borderThickness 0.0
              Setting.create BorderProps.shadow ValueNone
              // Toasts never take the keyboard: a press on these moves no focus.
              Button.focusable false
              // The toast's own text colour (a Button would take the theme's).
              Button.foreground (if severity = Severity.Informational then Palette.text else Palette.onAccent)
              Button.verticalAlignment VerticalAlignment.Center ]
        let toast (item: ToastItem) =
            let o = item.Options
            let here = item.Stage = 2
            ToastPresenter.create [
                ToastPresenter.key item.Id
                // Its name for screen readers (not its content: that would read the Close button into it).
                Semantics.label (if item.Count > 1 then o.Text + ", " + string item.Count + " times" else o.Text)
                ToastPresenter.classes [
                    match o.Severity with
                    | Severity.Informational -> ()
                    | Severity.Warning -> "warning"
                    | Severity.Critical -> "critical"
                ]
                ToastPresenter.opacity (if here then 1.0 else 0.0)
                Setting.create ToastProps.offset (if here then 0.0 else ToastLooks.slide)
                ToastPresenter.onPointerEnter (fun _ -> this.Hover(item, true))
                ToastPresenter.onPointerLeave (fun _ -> this.Hover(item, false))
                ToastPresenter.content (
                    DockPanel.create [
                        DockPanel.spacing 6.0
                        DockPanel.children [
                            match o.Content with
                            | ValueSome view -> view.With(DockProps.dock, box Dock.Fill)
                            | ValueNone ->
                                TextBlock.create [
                                    DockPanel.dock Dock.Fill
                                    // Shown again while it showed or waited: one toast, with how many times.
                                    TextBlock.text (if item.Count > 1 then o.Text + "  ×" + string item.Count else o.Text)
                                    TextBlock.textWrapping Wrap
                                    TextBlock.verticalAlignment VerticalAlignment.Center
                                ]
                            // The action, then the close button, in a strip of their own at the right: two children
                            // docked Right would stand in the opposite order to the one they're written in.
                            StackPanel.create [
                                DockPanel.dock Dock.Right
                                StackPanel.orientation Orientation.Horizontal
                                StackPanel.children [
                                    match o.Action with
                                    | ValueSome(struct (label, handler)) ->
                                        Button.create (
                                            quiet o.Severity
                                            @ [ Button.text label
                                                Button.padding (8.0, 4.0)
                                                Button.onClick (fun _ ->
                                                    this.Dismiss item
                                                    handler ()) ]
                                        )
                                    | ValueNone -> ()
                                    Button.create (
                                        quiet o.Severity
                                        @ [ Semantics.label "Close"
                                            Button.icon ToastLooks.cross
                                            // Beside the toast's text: on the middle of its letters, not of its line.
                                            Setting.create ButtonProps.iconBesideText true
                                            Button.iconSize 14.0
                                            // 25 DIPs tall with the 14-DIP icon, like the action button: an odd height, as a line of text has, so
                                            // the button's middle and the text's can fall on the same place.
                                            Button.padding (5.0, 5.5)
                                            Button.onClick (fun _ -> this.Dismiss item) ]
                                    )
                                ]
                            ]
                        ]
                    ]
                )
            ]
        // What waits has no place yet, so it would be out of sight: a line above the stack says how many, and
        // "Close all" clears everything at once (an error that keeps happening is one click, not one each).
        let waiting = items |> Seq.filter (fun i -> i.Stage = 0) |> Seq.sumBy (fun i -> i.Count)
        let more =
            ToastPresenter.create [
                ToastPresenter.key "more"
                ToastPresenter.classes [ "more" ]
                Semantics.label (string waiting + " more waiting")
                ToastPresenter.content (
                    DockPanel.create [
                        DockPanel.spacing 6.0
                        DockPanel.children [
                            TextBlock.create [
                                DockPanel.dock Dock.Fill
                                TextBlock.text ("+" + string waiting + " more")
                                TextBlock.verticalAlignment VerticalAlignment.Center
                            ]
                            Button.create (
                                quiet Severity.Informational
                                @ [ DockPanel.dock Dock.Right; Button.text "Close all"; Button.padding (8.0, 4.0); Button.onClick (fun _ -> this.CloseAll()) ]
                            )
                        ]
                    ]
                )
            ]
        let shown = items |> Seq.filter (fun i -> i.Stage > 0) |> Seq.sortBy (fun i -> i.Order)
        View.Create(ToastPanel.Type, [ Setting.create PanelProps.children [ if waiting > 0 then yield more
                                                                            for item in shown do
                                                                                yield toast item ] ])

    member this.Refresh() = update (this.View())

    /// Starts (again) the wait after which the toast goes, if it goes by itself.
    member private this.StartTimer(item: ToastItem) =
        item.Generation <- item.Generation + 1
        let generation = item.Generation
        let duration =
            match item.Options.Duration, item.Options.Severity with
            | ValueSome d, _ -> ValueSome d
            | ValueNone, Severity.Critical -> ValueNone
            | ValueNone, Severity.Warning -> ValueSome ToastLooks.warningDuration
            | ValueNone, Severity.Informational -> ValueSome ToastLooks.defaultDuration
        match duration with
        | ValueSome d -> dispatcher.PostAfter(d, fun () -> if item.Generation = generation && not item.Hovered then this.Dismiss item)
        | ValueNone -> ()

    /// Gives waiting toasts the free places: each comes in clear and below its place, then slides up.
    /// Critical ones first, then the oldest: a waiting error shows before a waiting "Saved".
    member private this.Promote() =
        let mutable places = ToastLooks.maxShown - (items |> Seq.filter (fun i -> i.Stage > 0) |> Seq.length)
        while places > 0 do
            let waiting = items |> Seq.filter (fun i -> i.Stage = 0)
            let next =
                match waiting |> Seq.tryFind (fun i -> i.Options.Severity = Severity.Critical) with
                | Some critical -> Some critical
                | None -> Seq.tryHead waiting
            match next with
            | Some item ->
                places <- places - 1
                nextOrder <- nextOrder + 1
                item.Order <- nextOrder
                item.Stage <- 1
                dispatcher.Post(fun () ->
                    if item.Stage = 1 then
                        item.Stage <- 2
                        this.Refresh()
                        this.StartTimer item)
            | None -> places <- 0

    /// The same toast as `options`, showing or waiting: same words and severity, with nothing of its own (an
    /// action or a view) that a merge would lose.
    member private _.Same(options: ToastOptions) =
        if options.Text = "" || options.Action.IsSome || options.Content.IsSome then None
        else
            items
            |> Seq.tryFind (fun i ->
                i.Stage < 3
                && i.Options.Text = options.Text
                && i.Options.Severity = options.Severity
                && i.Options.Action.IsNone
                && i.Options.Content.IsNone)

    member this.Add(options: ToastOptions) =
        match this.Same options with
        | Some item ->
            // Shown again: one toast, counted, and its time starts over.
            item.Count <- item.Count + 1
            if item.Stage = 2 && not item.Hovered then this.StartTimer item
            this.Refresh()
        | None ->
            nextId <- nextId + 1
            items.Add(ToastItem(nextId, options))
            // Too many waiting: the oldest go, those that aren't Critical first.
            let waiting = items |> Seq.filter (fun i -> i.Stage = 0) |> List.ofSeq
            if waiting.Length > ToastLooks.maxWaiting then
                let drop =
                    match waiting |> List.tryFind (fun i -> i.Options.Severity <> Severity.Critical) with
                    | Some item -> item
                    | None -> waiting.Head
                items.Remove drop |> ignore
            this.Promote()
            this.Refresh()

    /// Everything goes: what waits is dropped, what shows slides away.
    member this.CloseAll() =
        items.RemoveAll(fun i -> i.Stage = 0) |> ignore
        for item in items.ToArray() do
            this.Dismiss item
        this.Refresh()

    member private this.Hover(item: ToastItem, over: bool) =
        item.Hovered <- over
        // Over it, the wait stops; leaving, it starts again from the beginning.
        if over then item.Generation <- item.Generation + 1
        elif item.Stage = 2 then this.StartTimer item

    /// The toast goes: it fades and slides away, then the next waiting one takes its place.
    member private this.Dismiss(item: ToastItem) =
        if item.Stage <> 3 && items.Contains item then
            item.Stage <- 3
            item.Generation <- item.Generation + 1
            this.Refresh()
            let remove () =
                if items.Remove item then
                    this.Promote()
                    this.Refresh()
            dispatcher.PostAfter(ToastPresenter.moveTime, remove)

/// A window's toasts: short messages at its edge that go by themselves (DESIGN.md, "4d: Dialog and
/// Toast"). Every window has one; get it where you are and show toasts with it:
///
///     let toaster = ctx.useToaster ()                       // in a component
///     toaster.Show "Saved"
///     toaster.Show [ Toast.text "Message deleted"; Toast.action ("Undo", fun () -> dispatch Undo) ]
///
///     model, Cmd.toast "Saved"                              // from an Elmish update
///     App.onError (fun app e -> app.Toaster.Show [ Toast.severity Severity.Critical; Toast.text e.Message ])
///
/// A toast stays 4 seconds (`Toast.duration`), waits while the pointer is over it, and has a close
/// button; an Error stays until closed. At most three show, the rest wait their turn. They never
/// take the keyboard. `Show` is safe from any thread: keep the toaster and call it from a task.
[<Sealed>]
type Toaster internal (ui: unit -> Dispatcher voption, find: unit -> ToastStack voption) =

    /// Shows a toast saying `text`.
    member this.Show(text: string) = this.Show [ Toast.text text ]

    /// Shows a toast: `toaster.Show [ Toast.severity Severity.Critical; Toast.text "Couldn't save" ]`. Safe from any thread.
    member _.Show(settings: ToastSetting list) =
        let defaults = { Text = ""; Severity = Severity.Informational; Duration = ValueNone; Action = ValueNone; Content = ValueNone }
        let options = settings |> List.fold (fun o (ToastSetting apply) -> apply o) defaults
        let noWindow () =
#if DEBUG
            DebugWarnings.Once("toast-no-window", "A toast was shown from a view that isn't in a window (a test's bare tree, a view rendered to an image): nothing shows.")
#else
            ()
#endif
        // On the UI thread: the window's toasts (and their layer) are made there, the first time.
        match ui () with
        | ValueSome dispatcher ->
            dispatcher.Post(fun () ->
                match find () with
                | ValueSome stack -> stack.Add options
                | ValueNone -> noWindow ())
        | ValueNone -> noWindow ()

    /// How many toasts the window holds (showing, coming, going or waiting). For tests and tools; the UI thread.
    member _.Count =
        match find () with
        | ValueSome stack -> stack.Count
        | ValueNone -> 0
