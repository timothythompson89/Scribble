/// M8 step 4d, rounds 2 and 3: dialogs as actions on the fake platform (DESIGN.md, "4d"): a window's own
/// Dialogs (Inform, Confirm with its severity, Ask, each giving back an answer), Escape, the scrim, queueing,
/// threads, focus, closing; the hooks (useDialogs, useInform, useConfirm, useAsk) and the commands.
module Scribble.Tests.DialogsTests

open System.Collections.Generic
open System.Threading.Tasks
open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests

type Choice =
    | Save
    | Discard
    | Stay

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

/// Runs what was posted to the UI thread, then the frames it asked for.
let private pump (w: FakeWindow) (host: ElementHost) =
    for _ in 1..4 do
        host.Dispatcher.Value.RunPending()
        let mutable n = 0
        while w.DeliverFrame() && n < 30 do
            n <- n + 1

let private startWith (settings: AppSetting<'Msg> list) =
    let w, _, a = start settings
    let host = a :?> ElementHost
    pump w host
    w, host

let private show () : FakeWindow * ElementHost =
    startWith [ App.view (Button.create [ Button.name "app"; Button.text "App"; Button.horizontalAlignment HorizontalAlignment.Left; Button.verticalAlignment VerticalAlignment.Top ]) ]

let private dialogLayer (host: ElementHost) = host.Layers |> Seq.tryFind (fun l -> l.Name = "Modal")
let private isOpen (host: ElementHost) = (dialogLayer host).IsSome
let private inDialog (host: ElementHost) = all (dialogLayer host).Value.Tree.Root.Value
let private textOf (e: Element) =
    all e |> Seq.filter (fun x -> obj.ReferenceEquals(x.Type, TextBlock.Type)) |> Seq.map (fun x -> x.Get TextProps.text) |> String.concat " "
let private buttons (host: ElementHost) = inDialog host |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, Button.Type)) |> List.ofSeq
let private button (host: ElementHost) (label: string) = buttons host |> List.find (fun b -> textOf b = label)
let private texts (host: ElementHost) = textOf (dialogLayer host).Value.Tree.Root.Value
let private named (host: ElementHost) (name: string) = all host.Tree.Root.Value |> Seq.find (fun e -> e.Name = name)

let private clickOn (w: FakeWindow) (host: ElementHost) (e: Element) =
    let p = e.PositionInTree
    let at: PixelPoint = { X = p.X + e.Bounds.Width / 2.0; Y = p.Y + e.Bounds.Height / 2.0 }
    w.Send(PointerPressed(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, at, MouseButton.Left, Modifiers.None))
    pump w host

let private key (w: FakeWindow) (host: ElementHost) k =
    w.Send(KeyDown(k, Modifiers.None, false))
    w.Send(KeyUp(k, Modifiers.None))
    pump w host

[<Fact>]
let ``Inform shows its title, text and one button, and ends when the button is clicked or Escape is pressed`` () =
    let w, host = show ()
    let dialogs = host.Dialogs
    Assert.False(isOpen host)
    let first = dialogs.Inform [ Inform.title "Couldn't open the file"; Inform.text "It is in use."; Inform.button "Close" ]
    pump w host
    Assert.Equal("Couldn't open the file It is in use. Close", texts host)
    Assert.False first.IsCompleted
    clickOn w host (button host "Close")
    Assert.True first.IsCompletedSuccessfully
    Assert.False(isOpen host)
    let second = dialogs.Inform "Your session has expired."
    pump w host
    Assert.Equal("Your session has expired. OK", texts host)
    key w host Key.Escape
    Assert.True second.IsCompletedSuccessfully
    Assert.False(isOpen host)
    Assert.Equal(0, dialogs.Count)

[<Fact>]
let ``Confirm answers true for accept, false for cancel and for Escape`` () =
    let w, host = show ()
    let dialogs = host.Dialogs
    let ask () =
        let t = dialogs.Confirm [ Confirm.title "Delete the draft?"; Confirm.text "It can't be brought back."; Confirm.accept "Delete"; Confirm.cancel "Keep" ]
        pump w host
        t
    let t = ask ()
    Assert.Equal("Delete the draft? It can't be brought back. Keep Delete", texts host)
    clickOn w host (button host "Delete")
    Assert.True t.Result
    let t = ask ()
    clickOn w host (button host "Keep")
    Assert.False t.Result
    let t = ask ()
    key w host Key.Escape
    Assert.False t.Result
    Assert.False(isOpen host)
    // The defaults.
    let t = dialogs.Confirm [ Confirm.title "Sure?" ]
    pump w host
    Assert.Equal("Sure? Cancel OK", texts host)
    clickOn w host (button host "OK")
    Assert.True t.Result

[<Fact>]
let ``severity gives the accept button its colour and decides where focus starts: on cancel for Critical`` () =
    let theme = Theme.dark []
    let w, host = show ()
    for severity, color, focused in
        [ Severity.Informational, theme.Get Palette.accent, "OK"
          Severity.Warning, theme.Get Palette.warning, "OK"
          Severity.Critical, theme.Get Palette.danger, "Cancel" ] do
        let t = host.Dialogs.Confirm [ Confirm.title "Sure?"; Confirm.severity severity ]
        pump w host
        Assert.Equal(color, (button host "OK").Get BorderProps.background)
        Assert.Equal(focused, textOf host.Focused.Value)
        // Enter does what has focus.
        key w host Key.Enter
        Assert.Equal((severity <> Severity.Critical), t.Result)

[<Fact>]
let ``Ask gives back the value of the option picked, and none for Escape; the first option has focus`` () =
    let w, host = show ()
    let ask () =
        let t = host.Dialogs.Ask [ Ask.title "Unsaved changes"; Ask.option (Save, "Save"); Ask.option (Discard, "Discard"); Ask.option (Stay, "Cancel") ]
        pump w host
        t
    let t = ask ()
    Assert.Equal<string list>([ "Save"; "Discard"; "Cancel" ], buttons host |> List.map textOf)
    Assert.Equal("Save", textOf host.Focused.Value)
    clickOn w host (button host "Discard")
    Assert.Equal(ValueSome Discard, t.Result)
    let t = ask ()
    key w host Key.Escape
    Assert.Equal(ValueNone, t.Result)

[<Fact>]
let ``a press on the scrim answers nothing, and the app below gets no clicks`` () =
    let w, host = show ()
    let t = host.Dialogs.Confirm [ Confirm.title "Sure?" ]
    pump w host
    w.Send(PointerPressed(Mouse, 0, { X = 390.0; Y = 290.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 390.0; Y = 290.0 }, MouseButton.Left, Modifiers.None))
    pump w host
    Assert.False t.IsCompleted
    Assert.True(isOpen host)

[<Fact>]
let ``two asked at once show one after the other, in order`` () =
    let w, host = show ()
    let dialogs = host.Dialogs
    let first = dialogs.Inform "First"
    let second = dialogs.Confirm [ Confirm.title "Second" ]
    pump w host
    Assert.Equal(2, dialogs.Count)
    Assert.Equal("First OK", texts host)
    Assert.Equal(1, host.Layers |> Seq.filter (fun l -> l.Name = "Modal") |> Seq.length)
    clickOn w host (button host "OK")
    Assert.True first.IsCompletedSuccessfully
    Assert.Equal("Second Cancel OK", texts host)
    // Opened afresh: focus is on its own button.
    Assert.Equal("OK", textOf host.Focused.Value)
    clickOn w host (button host "OK")
    Assert.True second.Result
    Assert.False(isOpen host)

[<Fact>]
let ``asked from another thread, it shows on the UI thread`` () =
    let w, host = show ()
    let dialogs = host.Dialogs
    let asked = Task.Run<Task<bool>>(fun () -> dialogs.Confirm [ Confirm.title "From a task" ]).Result
    pump w host
    Assert.Equal("From a task Cancel OK", texts host)
    clickOn w host (button host "OK")
    Assert.True asked.Result

[<Fact>]
let ``when it closes, the keyboard is back in the app where it was`` () =
    let w, host = show ()
    let app = named host "app"
    app.Focus()
    host.Dialogs.Inform "Hello" |> ignore
    pump w host
    Assert.Equal("OK", textOf host.Focused.Value)
    key w host Key.Enter
    Assert.False(isOpen host)
    Assert.True app.IsFocused
    Assert.Equal("app", host.Focused.Value.Name)

[<Fact>]
let ``the host closing cancels what's open and what's waiting`` () =
    let w, host = show ()
    let first = host.Dialogs.Inform "First"
    let second = host.Dialogs.Confirm [ Confirm.title "Second" ]
    pump w host
    host.Close()
    Assert.True first.IsCanceled
    Assert.True second.IsCanceled
    Assert.Equal(0, host.Dialogs.Count)

[<Fact>]
let ``every host has its own dialogs`` () =
    let w1, host1 = show ()
    let w2, host2 = show ()
    host1.Dialogs.Inform "In the first" |> ignore
    pump w1 host1
    pump w2 host2
    Assert.True(isOpen host1)
    Assert.False(isOpen host2)
    Assert.Equal(0, host2.Dialogs.Count)

[<Fact>]
let ``a dialog's box is a Modal with the class dialog, so styles can tell it from other modals`` () =
    let w, host = show ()
    host.Dialogs.Inform "Hello" |> ignore
    pump w host
    let box = inDialog host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Modal.Type))
    Assert.Contains("dialog", box.Get LayoutProps.classes)

// --- Handlers as settings ------------------------------------------------------------------------

[<Fact>]
let ``handlers given as settings run with the answer, on the task forms too, before the task completes`` () =
    let log = List<string>()
    let w, host = show ()
    let dialogs = host.Dialogs
    let confirm () =
        let t =
            dialogs.Confirm [ Confirm.title "Sure?"; Confirm.onAccept (fun () -> log.Add "accept"); Confirm.onCancel (fun () -> log.Add "cancel") ]
        t.ContinueWith((fun (t: Task<bool>) -> log.Add("task " + string t.Result)), TaskContinuationOptions.ExecuteSynchronously) |> ignore
        pump w host
    confirm ()
    clickOn w host (button host "OK")
    confirm ()
    clickOn w host (button host "Cancel")
    confirm ()
    key w host Key.Escape
    Assert.Equal<string list>([ "accept"; "task True"; "cancel"; "task False"; "cancel"; "task False" ], List.ofSeq log)
    log.Clear()
    dialogs.Inform [ Inform.text "Done."; Inform.onClosed (fun () -> log.Add "closed") ] |> ignore
    pump w host
    key w host Key.Escape
    dialogs.Ask [ Ask.title "Which?"; Ask.choice ("One", fun () -> log.Add "one"); Ask.choice ("Two", fun () -> log.Add "two"); Ask.onDismiss (fun () -> log.Add "dismissed") ]
    |> ignore
    pump w host
    clickOn w host (button host "Two")
    dialogs.Ask [ Ask.option (Save, "Save"); Ask.onDismiss (fun () -> log.Add "dismissed") ] |> ignore
    pump w host
    key w host Key.Escape
    Assert.Equal<string list>([ "closed"; "two"; "dismissed" ], List.ofSeq log)

[<Fact>]
let ``a handler that throws goes to App.onError; the dialog still closes and its task completes`` () =
    let errors = List<string>()
    let w, host =
        startWith [ App.view (Button.create [ Button.name "app"; Button.text "App" ]); App.onError (fun _ e -> errors.Add e.Message) ]
    let t = host.Dialogs.Confirm [ Confirm.title "Sure?"; Confirm.onAccept (fun () -> failwith "The handler broke") ]
    pump w host
    clickOn w host (button host "OK")
    Assert.Equal<string list>([ "The handler broke" ], List.ofSeq errors)
    Assert.False(isOpen host)
    Assert.True t.Result

// --- Hooks ---------------------------------------------------------------------------------------

/// A component that declares its dialogs with the shortcut hooks and opens them from buttons.
let private hooked (log: List<string>) =
    Component.define "Asker" (fun ctx () ->
        let runs = ctx.useState 0
        let inform = ctx.useInform [ Inform.text ("Done " + string runs.Current + "."); Inform.onClosed (fun () -> log.Add "informed") ]
        let confirm =
            ctx.useConfirm [ Confirm.title "Delete?"; Confirm.accept "Delete"; Confirm.onAccept (fun () -> log.Add "confirmed"); Confirm.onCancel (fun () -> log.Add "cancelled") ]
        // With an argument: what's asked, and what the answer does, come from what the button passes.
        let confirmFile =
            ctx.useConfirm (fun (file: string) -> [ Confirm.title ("Delete " + file + "?"); Confirm.onAccept (fun () -> log.Add("deleted " + file)) ])
        let ask =
            ctx.useAsk [ Ask.title "Unsaved"; Ask.choice ("Save", fun () -> log.Add "save"); Ask.choice ("Discard", fun () -> log.Add "discard"); Ask.onDismiss (fun () -> log.Add "none") ]
        StackPanel.create [
            StackPanel.horizontalAlignment HorizontalAlignment.Left
            StackPanel.verticalAlignment VerticalAlignment.Top
            StackPanel.children [
                Button.create [ Button.name "inform"; Button.text "Inform"; Button.onClick (fun _ -> inform ()) ]
                Button.create [ Button.name "confirm"; Button.text "Confirm"; Button.onClick (fun _ -> confirm ()) ]
                Button.create [ Button.name "file"; Button.text "File"; Button.onClick (fun _ -> confirmFile "notes.txt") ]
                Button.create [ Button.name "ask"; Button.text "Ask"; Button.onClick (fun _ -> ask ()) ]
                Button.create [ Button.name "again"; Button.text "Again"; Button.onClick (fun _ -> runs.Set(runs.Current + 1)) ]
            ]
        ]) ()

[<Fact>]
let ``useInform, useConfirm and useAsk declare a dialog with what its answers do, and give a function that opens it`` () =
    let log = List<string>()
    let w, host = startWith [ App.view (hooked log) ]
    clickOn w host (named host "inform")
    Assert.Equal("Done 0. OK", texts host)
    clickOn w host (button host "OK")
    clickOn w host (named host "confirm")
    Assert.Equal("Delete? Cancel Delete", texts host)
    clickOn w host (button host "Delete")
    clickOn w host (named host "confirm")
    key w host Key.Escape
    clickOn w host (named host "ask")
    Assert.Equal<string list>([ "Save"; "Discard" ], buttons host |> List.map textOf)
    clickOn w host (button host "Discard")
    clickOn w host (named host "ask")
    key w host Key.Escape
    Assert.Equal<string list>([ "informed"; "confirmed"; "cancelled"; "discard"; "none" ], List.ofSeq log)
    // They are the host's own.
    Assert.Equal(0, host.Dialogs.Count)

[<Fact>]
let ``the form with an argument asks about what the handler passes`` () =
    let log = List<string>()
    let w, host = startWith [ App.view (hooked log) ]
    clickOn w host (named host "file")
    Assert.Equal("Delete notes.txt? Cancel OK", texts host)
    clickOn w host (button host "OK")
    Assert.Equal<string list>([ "deleted notes.txt" ], List.ofSeq log)

[<Fact>]
let ``a hook's settings are the latest run's, so the dialog follows the component's state`` () =
    let log = List<string>()
    let w, host = startWith [ App.view (hooked log) ]
    clickOn w host (named host "again")
    clickOn w host (named host "again")
    clickOn w host (named host "inform")
    Assert.Equal("Done 2. OK", texts host)

[<Fact>]
let ``useDialogs gives the same Dialogs every run, and from a modal's own content it reaches the same window`` () =
    let seen = List<Dialogs>()
    let inner =
        Component.define "Inner" (fun ctx () ->
            let more = ctx.useInform [ Inform.text "From inside a dialog" ]
            Button.create [ Button.name "more"; Button.text "More"; Button.onClick (fun _ -> more ()) ])
    let outer =
        Component.define "Outer" (fun ctx () ->
            seen.Add(ctx.useDialogs ())
            let count = ctx.useState 0
            StackPanel.create [
                StackPanel.children [
                    Button.create [ Button.name "again"; Button.text (string count.Current); Button.onClick (fun _ -> count.Set(count.Current + 1)) ]
                    // A Modal in the view: its content lives in another layer's tree, of the same window.
                    Modal.create [ Modal.isOpen true; Modal.content (inner ()) ]
                ]
            ]) ()
    let w, host = startWith [ App.view outer ]
    Assert.True(seen.Count >= 1)
    let more = host.Layers |> Seq.collect (fun l -> l.Tree.Root |> ValueOption.toArray |> Seq.collect all) |> Seq.find (fun e -> e.Name = "more")
    clickOn w host more
    Assert.Equal(1, host.Dialogs.Count)
    Assert.Equal(2, host.Layers |> Seq.filter (fun l -> l.Name = "Modal") |> Seq.length)
    Assert.True(seen |> Seq.forall (fun d -> obj.ReferenceEquals(d, seen[0])))

[<Fact>]
let ``in a tree no host runs, a dialog's task is cancelled and the hooks' functions do nothing`` () =
    let mutable dialogs = Unchecked.defaultof<Dialogs>
    let mutable confirm = Unchecked.defaultof<unit -> unit>
    let view =
        Component.define "Loose" (fun ctx () ->
            dialogs <- ctx.useDialogs ()
            confirm <- ctx.useConfirm [ Confirm.title "Anyone?"; Confirm.onAccept (fun () -> failwith "Nobody can accept") ]
            Border.create []) ()
    let tree = ElementTree(Scribble.Tests.TestShaping.textSystem ())
    tree.Update view
    confirm ()
    Assert.True((dialogs.Confirm [ Confirm.title "Anyone?" ]).IsCanceled)

// --- useDialog: content of the app's own ---------------------------------------------------------

/// A component with a counter and a dialog of its own that shows the count, adds to it and closes itself.
let private custom (log: List<string>) (extra: DialogSetting list) =
    Component.define "Custom" (fun ctx () ->
        let count = ctx.useState 0
        let shown = ctx.useState true
        let dialog =
            ctx.useDialog (fun close ->
                [ Dialog.title "Counting"
                  Dialog.content (
                      StackPanel.create [
                          StackPanel.children [
                              TextBlock.create [ TextBlock.name "count"; TextBlock.text ("Count " + string count.Current) ]
                              Button.create [ Button.name "add"; Button.text "Add"; Button.onClick (fun _ -> count.Set(count.Current + 1)) ]
                              Button.create [ Button.name "done"; Button.text "Done"; Dialog.initialFocus true; Button.onClick (fun _ -> close ()) ]
                          ]
                      ])
                  Dialog.onClosed (fun () -> log.Add("closed at " + string count.Current)) ]
                @ extra)
        // With an argument: what it shows comes from what the button passes.
        let about =
            ctx.useDialog (fun (what: string) close ->
                [ Dialog.title ("About " + what)
                  Dialog.content (Button.create [ Button.name "ok"; Button.text ("OK, " + what); Button.onClick (fun _ -> close ()) ]) ])
        StackPanel.create [
            StackPanel.horizontalAlignment HorizontalAlignment.Left
            StackPanel.verticalAlignment VerticalAlignment.Top
            StackPanel.children [
                Button.create [ Button.name "open"; Button.text "Open"; Button.onClick (fun _ -> dialog ()) ]
                Button.create [ Button.name "about"; Button.text "About"; Button.onClick (fun _ -> about "cats") ]
                Button.create [ Button.name "outside"; Button.text "Add outside"; Button.onClick (fun _ -> count.Set(count.Current + 10)) ]
            ]
        ])

let private inLayers (host: ElementHost) (name: string) =
    host.Layers |> Seq.collect (fun l -> l.Tree.Root |> ValueOption.toArray |> Seq.collect all) |> Seq.find (fun e -> e.Name = name)

[<Fact>]
let ``useDialog opens a dialog with the component's own content, and close closes it`` () =
    let log = List<string>()
    let w, host = startWith [ App.view (custom log [] ()) ]
    Assert.False(isOpen host)
    clickOn w host (named host "open")
    Assert.True(isOpen host)
    Assert.Equal("Counting Count 0 Add Done", texts host)
    // Its box is a dialog's: a Modal with the class.
    let box = inDialog host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Modal.Type))
    Assert.Contains("dialog", box.Get LayoutProps.classes)
    // Dialog.initialFocus chose where focus starts.
    Assert.Equal("done", host.Focused.Value.Name)
    clickOn w host (inLayers host "done")
    Assert.False(isOpen host)
    Assert.Equal<string list>([ "closed at 0" ], List.ofSeq log)
    // It opens again, afresh.
    clickOn w host (named host "open")
    Assert.True(isOpen host)

[<Fact>]
let ``while it's open, a useDialog's content follows the component's state`` () =
    let log = List<string>()
    let w, host = startWith [ App.view (custom log [] ()) ]
    clickOn w host (named host "open")
    clickOn w host (inLayers host "add")
    clickOn w host (inLayers host "add")
    Assert.Equal("Count 2", (inLayers host "count").Get TextProps.text)
    // Still the one dialog, patched in place: focus stayed where it was.
    Assert.Equal(1, host.Layers |> Seq.filter (fun l -> l.Name = "Modal") |> Seq.length)
    Assert.Equal("add", host.Focused.Value.Name)
    clickOn w host (inLayers host "done")
    Assert.Equal<string list>([ "closed at 2" ], List.ofSeq log)

[<Fact>]
let ``Escape closes a useDialog and tells onClosed; closeOnEscape false stops it; lightDismiss lets a press outside close it`` () =
    let log = List<string>()
    let w, host = startWith [ App.view (custom log [] ()) ]
    clickOn w host (named host "open")
    key w host Key.Escape
    Assert.False(isOpen host)
    Assert.Equal<string list>([ "closed at 0" ], List.ofSeq log)
    // A press outside does nothing by default.
    clickOn w host (named host "open")
    w.Send(PointerPressed(Mouse, 0, { X = 395.0; Y = 295.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 395.0; Y = 295.0 }, MouseButton.Left, Modifiers.None))
    pump w host
    Assert.True(isOpen host)
    log.Clear()
    let w, host = startWith [ App.view (custom log [ Dialog.closeOnEscape false; Dialog.lightDismiss true ] ()) ]
    clickOn w host (named host "open")
    key w host Key.Escape
    Assert.True(isOpen host)
    w.Send(PointerPressed(Mouse, 0, { X = 395.0; Y = 295.0 }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = 395.0; Y = 295.0 }, MouseButton.Left, Modifiers.None))
    pump w host
    Assert.False(isOpen host)
    Assert.Equal<string list>([ "closed at 0" ], List.ofSeq log)

[<Fact>]
let ``the form of useDialog with an argument shows what the handler passes`` () =
    let w, host = startWith [ App.view (custom (List()) [] ()) ]
    clickOn w host (named host "about")
    Assert.Equal("About cats OK, cats", texts host)
    clickOn w host (inLayers host "ok")
    Assert.False(isOpen host)

[<Fact>]
let ``a useDialog waits its turn with the window's other dialogs`` () =
    let w, host = startWith [ App.view (custom (List()) [] ()) ]
    host.Dialogs.Inform "First" |> ignore
    pump w host
    // The app is under the Inform, so its button can't be clicked: open the custom one through the component.
    Assert.Equal("First OK", texts host)
    Assert.Equal(1, host.Dialogs.Count)

[<Fact>]
let ``a useDialog closes when its component leaves the view`` () =
    let log = List<string>()
    let w, host = startWith [ App.view (custom log [] ()) ]
    clickOn w host (named host "open")
    Assert.True(isOpen host)
    host.Update(Border.create [])
    pump w host
    Assert.False(isOpen host)
    Assert.Equal(0, host.Dialogs.Count)

[<Fact>]
let ``Dialog.style styles dialogs' boxes and not a Modal written in the view`` () =
    let styles = [ Dialog.style [] [ Modal.padding 33.0 ] ]
    let view = Modal.create [ Modal.isOpen true; Modal.content (Button.create [ Button.name "inner"; Button.text "Inner" ]) ]
    let w, host = startWith [ App.view view; App.styles styles ]
    let padding (e: Element) = (e.Get BorderProps.padding).Left
    let plain = inDialog host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Modal.Type))
    Assert.NotEqual(33.0, padding plain)
    host.Update(Border.create [])
    pump w host
    host.Dialogs.Inform "Styled" |> ignore
    pump w host
    let box = inDialog host |> Seq.find (fun e -> obj.ReferenceEquals(e.Type, Modal.Type))
    Assert.Equal(33.0, padding box)

// --- Commands ------------------------------------------------------------------------------------

type Msg =
    | AskDelete
    | DeleteAnswered of bool
    | AskWhat
    | Chose of Choice voption
    | Tell
    | Told

[<Fact>]
let ``Cmd.confirm, Cmd.ask and Cmd.inform ask in the program's window and send the answer as a message`` () =
    let log = List<string>()
    let update (msg: Msg) (model: int) =
        match msg with
        | AskDelete -> model, Cmd.confirm [ Confirm.title "Delete?" ] DeleteAnswered
        | AskWhat -> model, Cmd.ask [ Ask.title "What now?"; Ask.option (Save, "Save"); Ask.option (Stay, "Stay") ] Chose
        | Tell -> model, Cmd.inform [ Inform.text "Done." ] Told
        | DeleteAnswered ok ->
            log.Add(if ok then "delete: yes" else "delete: no")
            model + 1, Cmd.none
        | Chose c ->
            log.Add(match c with ValueSome Save -> "chose save" | ValueSome _ -> "chose other" | ValueNone -> "chose nothing")
            model + 1, Cmd.none
        | Told ->
            log.Add "told"
            model + 1, Cmd.none
    let view (_: int) (dispatch: Msg -> unit) =
        StackPanel.create [
            StackPanel.horizontalAlignment HorizontalAlignment.Left
            StackPanel.verticalAlignment VerticalAlignment.Top
            StackPanel.children [
                Button.create [ Button.name "delete"; Button.text "Delete"; Button.onClick (fun _ -> dispatch AskDelete) ]
                Button.create [ Button.name "what"; Button.text "What"; Button.onClick (fun _ -> dispatch AskWhat) ]
                Button.create [ Button.name "tell"; Button.text "Tell"; Button.onClick (fun _ -> dispatch Tell) ]
            ]
        ]
    let w, host = startWith [ App.programWithCmd (0, Cmd.none) update view ]
    clickOn w host (named host "delete")
    clickOn w host (button host "OK")
    clickOn w host (named host "delete")
    key w host Key.Escape
    clickOn w host (named host "what")
    clickOn w host (button host "Save")
    clickOn w host (named host "what")
    key w host Key.Escape
    clickOn w host (named host "tell")
    clickOn w host (button host "OK")
    pump w host
    Assert.Equal<string list>([ "delete: yes"; "delete: no"; "chose save"; "chose nothing"; "told" ], List.ofSeq log)
