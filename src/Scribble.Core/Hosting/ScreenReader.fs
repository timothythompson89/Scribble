namespace Scribble.Core

open System

module internal ScreenReaderValues =
    // Made on first use (an app value's definition is shared state: `Theme.current` does the same).
    let enabled = lazy (AppValue.define "ScreenReader.enabled" true)
    let bridge = lazy (AppValue.define "ScreenReader.bridge" (ValueNone: (ElementHost -> IDisposable) voption))

/// The screen-reader bridge of an app's windows: what carries each window's semantic tree to the platform's
/// assistive technology, and its actions back (DESIGN.md, "AccessKit: the screen-reader bridge"). `App.run` on
/// the desktop gives every window one; `App.screenReader false` leaves them out. A bridge does nothing until
/// something asks the window what is in it.
[<AbstractClass; Sealed>]
type ScreenReader =
    /// Whether the app's windows get a bridge (`App.screenReader` starts it; default true).
    static member enabled: AppValue<bool> = ScreenReaderValues.enabled.Value

    /// What makes a bridge for a host's window, before that window is first shown: the package that runs the app
    /// sets it (Scribble.Desktop: AccessKit's). None: the platform has no bridge, and `attach` does nothing.
    static member bridge: AppValue<(ElementHost -> IDisposable) voption> = ScreenReaderValues.bridge.Value

    /// Gives `host`'s window the bridge that `app`'s values name, if the app wants one; it goes when the host
    /// closes. Call it before the window is first shown. For a tool that opens a window of its own beside an
    /// app's (the inspector): `ScreenReader.attach (appHost, toolHost)`.
    static member attach(app: ElementHost, host: ElementHost) : unit =
        if app.AppValues.Get ScreenReader.enabled then
            match app.AppValues.Get ScreenReader.bridge with
            | ValueSome make ->
                try
                    let bridge = make host
                    host.Closed.Add(fun () -> bridge.Dispose())
                with e ->
                    // A bridge that can't start (its native library is missing) leaves the app as it is without one.
                    DebugWarnings.Once(
                        "ScreenReader.attach",
                        "The screen-reader bridge could not start, so the app runs without one: " + e.Message)
            | ValueNone -> ()

    /// Gives `host`'s window the bridge its own app values name.
    static member attach(host: ElementHost) : unit = ScreenReader.attach (host, host)
