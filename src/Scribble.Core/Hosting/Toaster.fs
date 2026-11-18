namespace Scribble.Core

open System
open System.Collections.Generic
open Scribble.Accessibility
open Scribble.Rendering
open Scribble.Text

/// What a toast is about: its look, and whether it goes away by itself.
[<RequireQualifiedAccess>]
type ToastKind =
    /// Something happened ("Saved"): it goes after a few seconds.
    | Info
    /// Something went wrong: it stays until it's closed.
    | Error

    // Written out: a union's generated ToString uses %A, which NativeAOT can't run.
    override k.ToString() =
        match k with
        | Info -> "Info"
        | Error -> "Error"

/// What the Toast settings configure.
type internal ToastOptions =
    { Text: string
      Kind: ToastKind
      Duration: TimeSpan voption
      Action: struct (string * (unit -> unit)) voption
      Content: View voption }

/// One setting for a toast: `Toast.text "Saved"`, `Toast.kind ToastKind.Error`.
[<Struct>]
type ToastSetting = internal ToastSetting of (ToastOptions -> ToastOptions)

/// A toast's settings, for `toaster.Show [ ... ]`:
///
///     toaster.Show [ Toast.text "Message deleted"; Toast.action ("Undo", fun () -> dispatch Undo) ]
///     toaster.Show [ Toast.kind ToastKind.Error; Toast.text e.Message ]
[<AbstractClass; Sealed>]
type Toast =
    /// What it says.
    static member text(value: string) = ToastSetting(fun o -> { o with Text = value })
    /// Info (default) or Error (the theme's danger colour; it stays until closed).
    static member kind(value: ToastKind) = ToastSetting(fun o -> { o with Kind = value })
    /// How long it stays (default: 4 seconds; an Error stays until closed). The time starts again
    /// when the pointer leaves it.
    static member duration(value: TimeSpan) = ToastSetting(fun o -> { o with Duration = ValueSome value })
    /// A button after the text ("Undo"); clicking it closes the toast.
    static member action(label: string, handler: unit -> unit) = ToastSetting(fun o -> { o with Action = ValueSome(struct (label, handler)) })
    /// A view in place of the text (give `text` too: it stays the toast's name for screen readers).
    static member content(view: View) = ToastSetting(fun o -> { o with Content = ValueSome view })

/// What the Toaster's toasts share.
module internal ToastLooks =
    let defaultDuration = TimeSpan.FromSeconds 4.0
    let maxShown = 3
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

/// One host's toasts: the list, their timers and the view of their layer. The UI thread only.
[<Sealed>]
type internal ToastStack(dispatcher: Dispatcher, update: View -> unit) =
    let items = List<ToastItem>()
    let mutable nextId = 0

    member _.Dispatcher = dispatcher
    member _.Count = items.Count

    member private this.View() : View =
        let quiet (kind: ToastKind) : Setting<Button> list =
            [ Button.background Color.Transparent
              Button.borderThickness 0.0
              Setting.create BorderProps.shadow ValueNone
              // Toasts never take the keyboard: a press on these moves no focus.
              Button.focusable false
              // The toast's own text colour (a Button would take the theme's).
              Button.foreground (if kind = ToastKind.Error then Palette.onAccent else Palette.text)
              Button.verticalAlignment VerticalAlignment.Center ]
        let toast (item: ToastItem) =
            let o = item.Options
            let here = item.Stage = 2
            ToastPresenter.create [
                ToastPresenter.key item.Id
                // Its name for screen readers (not its content: that would read the Close button into it).
                Semantics.label o.Text
                ToastPresenter.classes [ if o.Kind = ToastKind.Error then "error" ]
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
                                    TextBlock.text o.Text
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
                                            quiet o.Kind
                                            @ [ Button.text label
                                                Button.padding (8.0, 4.0)
                                                Button.onClick (fun _ ->
                                                    this.Dismiss item
                                                    handler ()) ]
                                        )
                                    | ValueNone -> ()
                                    Button.create (
                                        quiet o.Kind
                                        @ [ Semantics.label "Close"
                                            Button.icon ToastLooks.cross
                                            Button.iconSize 14.0
                                            Button.padding 5.0
                                            Button.onClick (fun _ -> this.Dismiss item) ]
                                    )
                                ]
                            ]
                        ]
                    ]
                )
            ]
        View.Create(ToastPanel.Type, [ Setting.create PanelProps.children [ for item in items do if item.Stage > 0 then toast item ] ])

    member this.Refresh() = update (this.View())

    /// Starts (again) the wait after which the toast goes, if it goes by itself.
    member private this.StartTimer(item: ToastItem) =
        item.Generation <- item.Generation + 1
        let generation = item.Generation
        let duration =
            match item.Options.Duration, item.Options.Kind with
            | ValueSome d, _ -> ValueSome d
            | ValueNone, ToastKind.Error -> ValueNone
            | ValueNone, ToastKind.Info -> ValueSome ToastLooks.defaultDuration
        match duration with
        | ValueSome d -> dispatcher.PostAfter(d, fun () -> if item.Generation = generation && not item.Hovered then this.Dismiss item)
        | ValueNone -> ()

    /// Gives waiting toasts the free places: each comes in clear and below its place, then slides up.
    member private this.Promote() =
        let mutable shown = 0
        for item in items do
            if shown < ToastLooks.maxShown then
                shown <- shown + 1
                if item.Stage = 0 then
                    item.Stage <- 1
                    dispatcher.Post(fun () ->
                        if item.Stage = 1 then
                            item.Stage <- 2
                            this.Refresh()
                            this.StartTimer item)

    member this.Add(options: ToastOptions) =
        nextId <- nextId + 1
        items.Add(ToastItem(nextId, options))
        this.Promote()
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
///     App.onError (fun app e -> app.Toaster.Show [ Toast.kind ToastKind.Error; Toast.text e.Message ])
///
/// A toast stays 4 seconds (`Toast.duration`), waits while the pointer is over it, and has a close
/// button; an Error stays until closed. At most three show, the rest wait their turn. They never
/// take the keyboard. `Show` is safe from any thread: keep the toaster and call it from a task.
[<Sealed>]
type Toaster internal (ui: unit -> Dispatcher voption, find: unit -> ToastStack voption) =

    /// Shows a toast saying `text`.
    member this.Show(text: string) = this.Show [ Toast.text text ]

    /// Shows a toast: `toaster.Show [ Toast.kind ToastKind.Error; Toast.text "Couldn't save" ]`. Safe from any thread.
    member _.Show(settings: ToastSetting list) =
        let defaults = { Text = ""; Kind = ToastKind.Info; Duration = ValueNone; Action = ValueNone; Content = ValueNone }
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
