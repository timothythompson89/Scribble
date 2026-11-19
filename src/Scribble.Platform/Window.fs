namespace Scribble.Platform

open System

/// The native handles a GPU backend needs to create a drawing surface for a window.
/// One case per windowing system; backends match on the cases they support.
type NativeSurfaceHandle =
    | Win32Handle of hinstance: nativeint * hwnd: nativeint
    /// A <canvas> element in a web page, by its DOM id.
    | BrowserCanvas of elementId: string

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

    /// The pointer's look while it is over the window's drawable area.
    abstract SetCursor: cursor: Cursor -> unit

    /// Leaves a key combination to the app: the platform doesn't use it itself (in the browser,
    /// F12 or Ctrl+Shift+C no longer open the browser's own tools while the page has focus). The
    /// key still arrives as usual.
    abstract ClaimKey: key: Key * modifiers: Modifiers -> unit

    /// The colour the app clears its window to (sRGB, 0 to 255): a platform that shows anything around or behind
    /// the app's drawing shows this (a browser: the page behind the canvas, which a phone's browser can leave a
    /// strip of when its bars move). Told when it changes.
    abstract SetBackground: red: byte * green: byte * blue: byte -> unit

    /// Says whether the focused element takes typed text, and where its caret is (DESIGN.md, "9d", decision 50).
    /// With a request: input methods are on (`TextComposition` events come, candidates show beside the caret) and
    /// a phone shows its keyboard; called again whenever the caret's box or the keyboard changes. With none: they
    /// are off, anything being composed is dropped without an event, and a phone's keyboard goes.
    abstract SetTextInput: request: TextInputRequest voption -> unit

    /// Drops what an input method is composing, without committing it and without an event: the app has taken the
    /// composing text itself (a click elsewhere in the text). Text input stays on.
    abstract ClearComposition: unit -> unit

/// Options for creating a window. Sizes are in DIPs, so a window looks the same size at any scaling.
type WindowOptions =
    { Title: string
      Size: DipSize
      Resizable: bool
      /// A window this one belongs to (a tool window): it always stays above its owner, minimizes
      /// and closes with it, and has no taskbar button of its own. Ignored where an app has one
      /// window (the browser).
      Owner: IWindow voption }

    static member Default =
        { Title = "Scribble"
          Size = { Width = 960.0; Height = 600.0 }
          Resizable = true
          Owner = ValueNone }

/// Entry point to a windowing system.
type IPlatform =
    inherit IDisposable

    abstract Name: string

    /// True when `CreateWindow` can be called again while a window is open (desktop); false where
    /// an app gets one window (the browser canvas).
    abstract SupportsMultipleWindows: bool

    /// Creates a hidden window. Events are delivered on the UI thread to `onEvent`.
    abstract CreateWindow: options: WindowOptions * onEvent: (PlatformEvent -> unit) -> IWindow

    /// Sets what runs on the UI thread after `Wake` (Scribble's dispatcher draining its queue).
    /// Call once, on the UI thread, before the first Wake.
    abstract SetWakeHandler: handler: (unit -> unit) -> unit

    /// Makes the platform call the wake handler on the UI thread soon, even while it's idle or the
    /// user is dragging a window. Safe from any thread; several wakes may become one call.
    abstract Wake: unit -> unit

    /// Time since the platform started, from a monotonic clock. Everything that moves with time
    /// (frame timing, animations, delayed work) reads it, so a test platform can control time.
    abstract Elapsed: TimeSpan

    /// Runs `action` on the UI thread once, `delay` from now (a one-shot OS timer: nothing runs
    /// unless something asked). Call on the UI thread.
    abstract After: delay: TimeSpan * action: (unit -> unit) -> unit

    /// Starts delivering events.
    ///
    /// Desktop: runs the message loop and returns when every window is closed. While nothing has
    /// requested a frame, the loop sleeps until the OS has input for it.
    ///
    /// Browser: returns immediately; the browser keeps delivering events and frames for as long as
    /// the page is open. Don't dispose anything after it returns.
    abstract Run: unit -> unit

    /// Lets the person save `bytes` as a file (DESIGN.md, "Rendering to an image and saving it", decision 7): the
    /// desktop shows a save dialog (over `owner`) suggesting `suggestedName`, its type taken from the extension; the
    /// browser downloads it under that name. True when it was saved, false when the person cancelled (always true
    /// in the browser, which can't tell). Call on the UI thread; the task may finish on another thread.
    abstract SaveFile: owner: IWindow voption * suggestedName: string * bytes: byte[] -> System.Threading.Tasks.Task<bool>

    /// Puts `text` on the system clipboard (DESIGN.md, "9b: clipboard and undo", decision 22). Call on the UI thread;
    /// in the browser, while handling a key or a press (it refuses otherwise). A failure is not reported.
    abstract SetClipboardText: text: string -> unit

    /// The clipboard's text ("" when it has none, or when the browser refuses). The desktop answers at once (a
    /// finished task); the browser later: from its paste event after Ctrl+V or Shift+Insert, else by asking the
    /// person for permission. Call on the UI thread; the task may finish on another thread.
    abstract GetClipboardText: unit -> System.Threading.Tasks.Task<string>
