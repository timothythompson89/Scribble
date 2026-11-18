namespace Scribble.Core

open System.Threading.Tasks

/// The system clipboard's text (DESIGN.md, "9b: clipboard and undo", decisions 22 to 24). In a component:
///
///     let clipboard = ctx.useClipboard ()
///     Button.create [ Button.text "Copy link"; Button.onClick (fun _ -> clipboard.SetText link) ]
///
/// An element type's input handlers have the same as `ctx.Clipboard`. Use it on the UI thread, in a running app;
/// without one (a tool rendering images) nothing is copied and nothing is read.
[<Sealed>]
type Clipboard internal () =
    /// Puts `text` on the clipboard. In the browser this works from a handler of a key or a press (a click), which
    /// is where a Copy button calls it; the browser refuses it from a timer or a finished task.
    member _.SetText(text: string) : unit =
        match RenderServices.TryCurrent with
        | ValueSome { Platform = ValueSome platform } ->
            try
                platform.SetClipboardText(if isNull text then "" else text)
            with _ ->
                ()
        | _ -> ()

    /// The clipboard's text: "" when it has none or the browser refuses. The desktop answers at once (a finished
    /// task). The browser answers later, and outside a paste key (Ctrl+V, Shift+Insert) it first asks the person
    /// for permission. The task finishes on the UI thread.
    member _.GetText() : Task<string> =
        match RenderServices.TryCurrent with
        | ValueSome { Platform = ValueSome platform; Dispatcher = ValueSome dispatcher } ->
            try
                let reading = platform.GetClipboardText()
                if reading.IsCompletedSuccessfully then
                    if isNull reading.Result then Task.FromResult "" else reading
                else
                    // Back on the UI thread, whichever thread the platform finished on.
                    let result = TaskCompletionSource<string>()
                    reading.ContinueWith(
                        (fun (t: Task<string>) ->
                            dispatcher.Post(fun () ->
                                result.SetResult(if t.IsCompletedSuccessfully && not (isNull t.Result) then t.Result else ""))),
                        TaskContinuationOptions.ExecuteSynchronously
                    )
                    |> ignore
                    result.Task
            with _ ->
                Task.FromResult ""
        | _ -> Task.FromResult ""
