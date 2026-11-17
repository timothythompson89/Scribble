namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// An inspector for Scribble apps: a panel over the app (it never changes the app's layout) with
/// the element tree of every layer, pick mode and a devtools-style highlight. F12 shows and hides
/// it; Ctrl+Shift+C picks an element (Escape cancels). The panel docks left or right, floats
/// (drag its title), collapses, and on the desktop pops out into a window of its own.
[<AbstractClass; Sealed>]
type Inspector =
    /// Which side the panel docks to (default: right). It can be switched from the panel.
    static member side(value: DockSide) = InspectorSetting(fun o -> { o with Side = value })

    /// The key that shows and hides the inspector (default: F12).
    static member toggleKey(key: Key, modifiers: Modifiers) = InspectorSetting(fun o -> { o with ToggleKey = (key, modifiers) })

    /// The key that starts pick mode (default: Ctrl+Shift+C).
    static member pickKey(key: Key, modifiers: Modifiers) = InspectorSetting(fun o -> { o with PickKey = (key, modifiers) })

    /// Shown from the start (default: hidden until the toggle key).
    static member isOpen(value: bool) = InspectorSetting(fun o -> { o with IsOpen = value })

    /// The platform to open the pop-out window on (`Inspector.enable` passes the app's; custom
    /// hosts pass theirs to get the "Pop out" button). Only platforms with several windows.
    static member platform(value: IPlatform) = InspectorSetting(fun o -> { o with Platform = ValueSome value })

    /// The app's own text for values the inspector shows (properties, hook values, list items),
    /// tried before the inspector's: `Inspector.format (function :? Model as m -> ValueSome m.Title | _ -> ValueNone)`.
    /// Without it, the inspector writes Scribble's own types and plain values itself and shows other
    /// types by name (never `ToString()` on F# records and unions: it isn't NativeAOT-safe). Several
    /// formatters are tried in order.
    static member format(formatter: obj -> string voption) =
        InspectorSetting(fun o ->
            let before = o.Format
            { o with Format = fun v -> match before v with ValueSome s -> ValueSome s | ValueNone -> formatter v })

    /// Adds the inspector to a running host (custom hosts; apps use `Inspector.enable`). Dispose
    /// the result to take it away again.
    static member attach(host: ElementHost, settings: InspectorSetting list) : IDisposable =
        let options = settings |> List.fold (fun o (InspectorSetting apply) -> apply o) InspectorOptions.Default
        let state = InspectorState(host, options)
        let highlightLayer =
            // Pick mode takes clicks, not the wheel: the app scrolls under the highlight.
            host.AddLayer [ Layer.name "Inspector highlight"; Layer.order (Int32.MaxValue - 1); Layer.appStyles false; Layer.letWheelThrough true; Layer.view (Views.highlight state) ]
        let panelLayer =
            host.AddLayer [ Layer.name "Inspector"; Layer.order Int32.MaxValue; Layer.appStyles false; Layer.view (Views.panel { State = state; IsPoppedOut = false }) ]
        // Its own theme, whatever the app's is (its layers ignore the app's styles and theme).
        let theme = Theme.dark []
        highlightLayer.Tree.Theme <- ValueSome theme
        panelLayer.Tree.Theme <- ValueSome theme
        state.Own <- [ highlightLayer; panelLayer ]
        let toggleKey, toggleMods = options.ToggleKey
        let pickKey, pickMods = options.PickKey
        host.Window.ClaimKey(toggleKey, toggleMods)
        host.Window.ClaimKey(pickKey, pickMods)

        // The pop-out window: its own host on the same device and text system, showing the panel.
        let popped: (ElementHost * IDisposable) voption ref = ref ValueNone
        let appTitle () =
            if options.FollowAppTitle then host.AppValues.Get WindowTitle.current else host.Window.Title
        let closeWindow () =
            match popped.Value with
            | ValueSome(h, keys) ->
                popped.Value <- ValueNone
                keys.Dispose()
                h.Close()
                (h :> IDisposable).Dispose()
            | ValueNone -> ()
        let openWindow () =
            match options.Platform with
            | ValueSome platform when popped.Value.IsNone ->
                let popHost: ElementHost voption ref = ref ValueNone
                let onEvent (e: PlatformEvent) =
                    match popHost.Value with
                    | ValueSome h ->
                        match e with
                        | CloseRequested -> state.PopIn() // closes the window
                        | _ -> h.HandleEvent e |> ignore
                    | ValueNone -> ()
                let title = "Inspector – " + appTitle ()
                let size: DipSize = { Width = 440.0; Height = 720.0 }
                // Owned by the app's window: it stays above it (also after clicks in the app), minimizes
                // with it, and has no taskbar button of its own.
                let windowOptions = { WindowOptions.Default with Title = title; Size = size; Owner = ValueSome host.Window }
                let window = platform.CreateWindow(windowOptions, onEvent)
                let hostSettings =
                    [ yield ElementHost.background (Color.Hex 0x1B1B2A)
                      yield ElementHost.theme theme
                      match host.Dispatcher with
                      | ValueSome d -> yield ElementHost.dispatcher d
                      | ValueNone -> () ]
                let h = ElementHost.create window host.Device host.Text hostSettings
                popHost.Value <- ValueSome h
                h.Update(Views.panel { State = state; IsPoppedOut = true })
                popped.Value <- ValueSome(h, h.AddKeyPreview state.OnKey)
                window.Show()
                window.RequestFrame()
            | _ -> ()

        let subscriptions =
            [ host.AddKeyPreview state.OnKey
              host.LayerChanged.Subscribe state.LayerChanged
              host.FrameDrawn.Subscribe state.AfterFrame
              state.PopOutRequested.Subscribe(fun () -> openWindow ())
              state.PopInRequested.Subscribe(fun () -> closeWindow ())
              // Closing the app closes the pop-out too (otherwise its window keeps the app running).
              host.Closed.Subscribe(fun () -> closeWindow ())
              // The pop-out keeps the app's title in its own.
              if options.FollowAppTitle then
                  host.AppValues.Subscribe(WindowTitle.current, fun t ->
                      popped.Value |> ValueOption.iter (fun (h, _) -> h.Window.Title <- "Inspector – " + t)) ]
        { new IDisposable with
            member _.Dispose() =
                for s in subscriptions do
                    s.Dispose()
                closeWindow ()
                state.ClearForced()
                host.RemoveLayer highlightLayer
                host.RemoveLayer panelLayer }

    /// Turns the inspector on for an app: `Inspector.enable [ Inspector.side DockSide.Left ]` in
    /// its settings. Leave it out of release builds with `#if DEBUG` if you like.
    static member enable<'Msg>(settings: InspectorSetting list) : AppSetting<'Msg> =
        App.onStart (fun ctx ->
            let fromApp = InspectorSetting(fun o -> { o with Platform = ValueSome ctx.Platform; FollowAppTitle = true })
            Inspector.attach (ctx.Host, fromApp :: settings) |> ignore)
