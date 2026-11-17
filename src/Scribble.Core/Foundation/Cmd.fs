namespace Scribble.Core

open System.Threading.Tasks

/// Work an Elmish update asks for besides the new model: messages to send now or later, tasks,
/// subscriptions. Returned next to the model from `init` and `update` with `App.programWithCmd`
/// and `ctx.useElmishWithCmd`.
[<Struct>]
type Cmd<'Msg> = internal Cmd of effects: (AppValues -> ('Msg -> unit) -> unit) list

module Cmd =
    /// Nothing to do.
    let none<'Msg> : Cmd<'Msg> = Cmd []

    /// Sends a message straight after this update.
    let ofMsg (msg: 'Msg) : Cmd<'Msg> = Cmd [ fun _ dispatch -> dispatch msg ]

    /// Several commands, run in order.
    let batch (cmds: Cmd<'Msg> list) : Cmd<'Msg> = Cmd(cmds |> List.collect (fun (Cmd effects) -> effects))

    /// A command for a child's messages, as a command for the parent's (`Cmd.map ChildMsg cmd`).
    let map (f: 'A -> 'Msg) (Cmd effects: Cmd<'A>) : Cmd<'Msg> =
        Cmd [ for effect in effects -> fun values dispatch -> effect values (f >> dispatch) ]

    /// Anything that sends messages, now or later, from any thread: `Cmd.ofSub (fun dispatch -> ...)`.
    let ofSub (subscribe: ('Msg -> unit) -> unit) : Cmd<'Msg> = Cmd [ fun _ dispatch -> subscribe dispatch ]

    /// Runs a task and sends `ofSuccess result` or `ofError exn` when it finishes:
    /// `Cmd.ofTask api.Load id Loaded LoadFailed`.
    let ofTask (work: 'A -> Task<'R>) (arg: 'A) (ofSuccess: 'R -> 'Msg) (ofError: exn -> 'Msg) : Cmd<'Msg> =
        ofSub (fun dispatch ->
            let outcome =
                task {
                    try
                        let! result = work arg
                        return Ok result
                    with e ->
                        return Error e
                }
            outcome.ContinueWith(fun (t: Task<Result<'R, exn>>) ->
                match t.Result with
                | Ok result -> dispatch (ofSuccess result)
                | Error e -> dispatch (ofError e))
            |> ignore)

    /// Runs an F# async and sends `ofSuccess result` or `ofError exn` when it finishes.
    let ofAsync (work: 'A -> Async<'R>) (arg: 'A) (ofSuccess: 'R -> 'Msg) (ofError: exn -> 'Msg) : Cmd<'Msg> =
        ofSub (fun dispatch ->
            Async.StartImmediate(
                async {
                    match! Async.Catch(work arg) with
                    | Choice1Of2 result -> dispatch (ofSuccess result)
                    | Choice2Of2 e -> dispatch (ofError e)
                }
            ))

    /// Sets an app value (`AppValue.define`) for the whole app: `Cmd.setAppValue compactMode true`.
    let setAppValue (value: AppValue<'T>) (next: 'T) : Cmd<'Msg> = Cmd [ fun values _ -> values.Set(value, next) ]

    /// Scrolls the ItemsView or ScrollViewer with `name` (its `name` setting) to the item with `key`,
    /// as its scroller's `ScrollTo(key, align)` would: `Cmd.scrollTo "messages" newId ScrollAlign.End`.
    /// Applies at the next layout, so an item added by the same update can be scrolled to; a name
    /// nothing has does nothing.
    let scrollTo (name: string) (key: obj) (align: ScrollAlign) : Cmd<'Msg> =
        Cmd [ fun values _ ->
                match values.NamedScrollers.TryGetValue name with
                | true, w ->
                    match w.TryGetTarget() with
                    | true, s -> s.ScrollTo(key, align)
                    | _ -> ()
                | _ -> () ]

    /// Runs a command's effects with `dispatch`. Errors (from an effect, or from a dispatch that
    /// happens later) go to `onError` instead of being lost.
    let internal execute (onError: exn -> unit) (values: AppValues) (dispatch: 'Msg -> unit) (Cmd effects: Cmd<'Msg>) =
        let safeDispatch msg =
            try
                dispatch msg
            with e ->
                onError e
        for effect in effects do
            try
                effect values safeDispatch
            with e ->
                onError e
