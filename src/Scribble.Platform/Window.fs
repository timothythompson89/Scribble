namespace Scribble.Platform

open System

/// The native handles a GPU backend needs to create a drawing surface for a window.
/// One case per windowing system; backends match on the cases they support.
type NativeSurfaceHandle =
    | Win32Handle of hinstance: nativeint * hwnd: nativeint
    /// A <canvas> element in a web page, by its DOM id.
    | BrowserCanvas of elementId: string

/// Options for creating a window. Sizes are in DIPs, so a window looks the same size at any scaling.
type WindowOptions =
    { Title: string
      Size: DipSize
      Resizable: bool }

    static member Default =
        { Title = "Scribble"
          Size = { Width = 960.0; Height = 600.0 }
          Resizable = true }

/// A native top-level window.
type IWindow =
    inherit IDisposable

    /// Drawable area in physical pixels.
    abstract ClientSize: PixelSize
    /// Scale factor of the monitor the window is on (1.0 = 96 DPI, 1.5 = 144 DPI, ...).
    abstract Scale: float
    abstract Title: string with get, set
    abstract Surface: NativeSurfaceHandle
    abstract IsClosed: bool
    abstract Show: unit -> unit
    /// Destroys the window. Safe to call more than once.
    abstract Close: unit -> unit

    /// Asks for a `Frame` event at the next display refresh. Calls before that frame are merged
    /// into one. Scribble draws only on request: call this when something visible changed, and
    /// again from each `Frame` while an animation runs.
    abstract RequestFrame: unit -> unit

/// Entry point to a windowing system.
type IPlatform =
    inherit IDisposable

    abstract Name: string

    /// Creates a hidden window. Events are delivered on the UI thread to `onEvent`.
    abstract CreateWindow: options: WindowOptions * onEvent: (PlatformEvent -> unit) -> IWindow

    /// Starts delivering events.
    ///
    /// Desktop: runs the message loop and returns when every window is closed. While nothing has
    /// requested a frame, the loop sleeps until the OS has input for it.
    ///
    /// Browser: returns immediately; the browser keeps delivering events and frames for as long as
    /// the page is open. Don't dispose anything after it returns.
    abstract Run: unit -> unit
