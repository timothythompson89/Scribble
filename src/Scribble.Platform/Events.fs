namespace Scribble.Platform

/// What kind of device produced a pointer event. Touch and pen arrive with the mobile ports.
type PointerKind =
    | Mouse
    | Touch
    | Pen

type MouseButton =
    | Left
    | Right
    | Middle
    | XButton1
    | XButton2

/// Events a platform window raises. The core never sees OS messages, only these.
///
/// Positions are in physical pixels; divide by the window's scale to get DIPs.
/// Key codes are the platform's raw codes for now; a portable Key type arrives with the input milestone.
type PlatformEvent =
    /// Time to draw. Sent after `IWindow.RequestFrame`, after every resize, and when the OS needs
    /// the window repainted (uncovered, restored). Draw the whole window here.
    | Frame
    /// The drawable area changed size (physical pixels). Zero while minimized. A `Frame` follows.
    | Resized of size: PixelSize
    /// The window moved to a monitor with a different scale factor (1.0 = 96 DPI).
    | ScaleChanged of scale: float
    /// The user started (true) or finished (false) dragging the window's edge or title bar.
    /// Renderers can lower latency during a live resize so content keeps up with the frame.
    | LiveResize of active: bool
    /// The user asked to close the window. The app decides whether to call Close.
    | CloseRequested
    | PointerMoved of kind: PointerKind * pointerId: int * position: PixelPoint
    | PointerPressed of kind: PointerKind * pointerId: int * position: PixelPoint * button: MouseButton
    | PointerReleased of kind: PointerKind * pointerId: int * position: PixelPoint * button: MouseButton
    | PointerWheel of position: PixelPoint * deltaX: float * deltaY: float
    | KeyDown of platformKey: int * isRepeat: bool
    | KeyUp of platformKey: int
    | TextInput of text: string
    | FocusChanged of hasFocus: bool
