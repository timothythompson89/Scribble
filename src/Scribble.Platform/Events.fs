namespace Scribble.Platform

open System

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

/// Modifier keys held during an input event. Meta is the Windows key (Command on macOS).
[<Flags>]
type Modifiers =
    | None = 0
    | Shift = 1
    | Control = 2
    | Alt = 4
    | Meta = 8

/// A key, as the keyboard layout names it (so shortcuts follow the layout: Ctrl+S is `Char 's'`
/// with Control on QWERTY and AZERTY alike).
[<RequireQualifiedAccess>]
type Key =
    /// A key that types a character. Letters are lowercase whatever Shift does; other characters
    /// are what the layout gives (Shift+1 is '!' on a US layout).
    | Char of char
    | Space
    | Enter
    | Escape
    | Tab
    | Backspace
    | Delete
    | Insert
    | Left
    | Up
    | Right
    | Down
    | Home
    | End
    | PageUp
    | PageDown
    /// F1 to F24.
    | F of number: int
    | Shift
    | Control
    | Alt
    | Meta
    | CapsLock
    /// Anything else, by name (the browser's `key` name, or SDL's key name on the desktop).
    | Other of name: string

/// A cursor image for one scale: premultiplied BGRA pixels, rows top to bottom, and the hotspot
/// (the pixel that points) in pixels from the top-left.
type CursorBitmap =
    { Width: int
      Height: int
      HotspotX: int
      HotspotY: int
      Pixels: byte[] }

/// A cursor drawn by the app (`Cursor.fromIcon` in Core), shown by the OS as a hardware cursor.
/// The platform renders it for its window's scale (and again when the scale changes) and keeps
/// what it made by key: two images with the same key are the same cursor.
[<CustomEquality; NoComparison>]
type CursorImage =
    { /// What the image looks like, as text; equal keys mean equal images.
      Key: string
      /// The image at a scale (1.0 = 96 DPI).
      Render: float -> CursorBitmap }

    override this.Equals(other: obj) =
        match other with
        | :? CursorImage as o -> String.Equals(this.Key, o.Key, StringComparison.Ordinal)
        | _ -> false

    override this.GetHashCode() = StringComparer.Ordinal.GetHashCode this.Key
    override this.ToString() = this.Key

/// The pointer's look over a window. Elements choose one with `Layoutable.cursor`.
[<RequireQualifiedAccess>]
type Cursor =
    /// The usual arrow.
    | Default
    /// A hand: something to click.
    | Hand
    /// An I-beam: text to select or type in.
    | Text
    | Crosshair
    /// Four arrows: something to drag around.
    | Move
    | ResizeLeftRight
    | ResizeUpDown
    /// Diagonal arrows (top left to bottom right): a corner to resize from.
    | ResizeDiagonal
    | NotAllowed
    | Wait
    /// The app's own image (`Cursor.fromIcon` in Core).
    | Image of CursorImage

/// Which on-screen keyboard suits what is being typed (a phone; a desktop keyboard is what it is). Nothing is
/// checked: a Number field can still be given letters.
[<RequireQualifiedAccess>]
type KeyboardKind =
    | Text
    /// Digits only.
    | Number
    /// Digits and a decimal separator.
    | Decimal
    | Email
    | Url
    | Phone
    /// Text, with a Search key where the platform has one.
    | Search
    /// Text that isn't learned or suggested.
    | Password

/// What a window is told while the focused element takes typed text (`IWindow.SetTextInput`).
[<Struct>]
type TextInputRequest =
    { /// The caret's box in the window, in physical pixels: an input method shows its candidates beside it, and a
      /// browser's hidden field stands there.
      CaretX: float
      CaretY: float
      CaretWidth: float
      CaretHeight: float
      Keyboard: KeyboardKind
      /// Enter makes a new line (the on-screen keyboard shows that key).
      Multiline: bool }

/// Events a platform window raises. The core never sees OS messages, only these.
///
/// Positions are in physical pixels; divide by the window's scale to get DIPs.
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
    /// The pointer moved over the window, or anywhere while a button is held (the platform
    /// captures the pointer from press to release, so the release always arrives too).
    | PointerMoved of kind: PointerKind * pointerId: int * position: PixelPoint * modifiers: Modifiers
    | PointerPressed of kind: PointerKind * pointerId: int * position: PixelPoint * button: MouseButton * modifiers: Modifiers
    | PointerReleased of kind: PointerKind * pointerId: int * position: PixelPoint * button: MouseButton * modifiers: Modifiers
    /// The pointer left the window (with no button held), or a touch ended.
    | PointerExited of kind: PointerKind * pointerId: int
    /// The press was taken away (another window grabbed the mouse, the browser cancelled a touch):
    /// no release will follow, so forget anything pressed.
    | PointerCanceled of kind: PointerKind * pointerId: int
    /// Wheel movement in notches (positive = away from the user / to the right).
    | PointerWheel of position: PixelPoint * deltaX: float * deltaY: float * modifiers: Modifiers
    /// A key went down (or repeats while held: `isRepeat`).
    | KeyDown of key: Key * modifiers: Modifiers * isRepeat: bool
    | KeyUp of key: Key * modifiers: Modifiers
    /// Text was typed, or an input method committed what it composed.
    | TextInput of text: string
    /// An input method (an IME, a phone's keyboard) is composing `text`, which isn't typed yet: it replaces what was
    /// composing before. `caret` is where the method's caret stands in it (a UTF-16 index; the end when the method
    /// doesn't say). An empty text ends the composition: what was committed, if anything, comes as `TextInput`.
    /// Only sent while `IWindow.SetTextInput` has text input on.
    | TextComposition of text: string * caret: int
    | FocusChanged of hasFocus: bool
