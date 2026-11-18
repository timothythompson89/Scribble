namespace Scribble.Core

open System
open System.Threading.Tasks

/// Files the person chooses (DESIGN.md, "Rendering to an image and saving it", decision 7 and round 2, 8).
module Files =
    /// Lets the person save `bytes` as a file named `name` (its extension gives the dialog its file type): a save
    /// dialog on the desktop, a download in the browser. True when saved, false when they cancelled (always true in
    /// the browser, which can't tell). Call on the UI thread, in a running app; the task finishes on the UI thread.
    /// ```
    /// let! png = Image.toPng image
    /// let! saved = Files.save "chart.png" png
    /// ```
    let save (name: string) (bytes: byte[]) : Task<bool> =
        try
            if String.IsNullOrWhiteSpace name then invalidArg (nameof name) "A file name is needed."
            if isNull bytes then nullArg (nameof bytes)
            let env = RenderServices.Current
            match env.Platform, env.Dispatcher with
            | ValueSome platform, ValueSome dispatcher ->
                let saving = platform.SaveFile(env.Window, name, bytes)
                // Back on the UI thread, whichever thread the platform finished on.
                let result = TaskCompletionSource<bool>()
                saving.ContinueWith(
                    (fun (t: Task<bool>) ->
                        dispatcher.Post(fun () ->
                            if t.IsFaulted then result.SetException(t.Exception.InnerExceptions)
                            elif t.IsCanceled then result.SetCanceled()
                            else result.SetResult t.Result)),
                    TaskContinuationOptions.ExecuteSynchronously
                )
                |> ignore
                result.Task
            | _ -> invalidOp "Saving a file needs a running app (App.run): a tool can write the file itself."
        with e ->
            Task.FromException<bool> e
