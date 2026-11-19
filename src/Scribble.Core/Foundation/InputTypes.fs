namespace Scribble.Core

// The platform's input types, under Scribble.Core too, so apps don't need `open Scribble.Platform`.
type Cursor = Scribble.Platform.Cursor
type Key = Scribble.Platform.Key
type Modifiers = Scribble.Platform.Modifiers
type MouseButton = Scribble.Platform.MouseButton
type PointerKind = Scribble.Platform.PointerKind
type KeyboardKind = Scribble.Platform.KeyboardKind

/// What every routed event tells its handlers.
///
/// Routed events go to a target element (the one under the pointer, or the focused one for keys)
/// and then to each of its ancestors; their Preview versions go the other way first, from the
/// root down. Call `Handle()` to stop the event there.
[<AbstractClass>]
type RoutedEventArgs internal (modifiers: Modifiers) =
    let mutable handled = false
    /// The top-left of the element whose handler is running, in window DIPs (set by the router).
    member val internal Origin = Point.Zero with get, set
    /// Modifier keys held when the event happened.
    member _.Modifiers = modifiers
    member _.IsHandled = handled
    /// Stops the event: elements further along its route don't see it. Handling a Preview event
    /// also stops the normal one (a press handled in Preview doesn't click; a key handled anywhere
    /// skips built-in behavior such as Tab moving focus and App.onKey).
    member _.Handle() = handled <- true

/// A routed event at a position (pointer, wheel). Positions are in DIPs.
[<AbstractClass>]
type InputEventArgs internal (position: Point, modifiers: Modifiers) =
    inherit RoutedEventArgs(modifiers)
    /// Where the pointer is, relative to the window.
    member _.Position = position
    /// Where the pointer is, relative to the element whose handler is running.
    member this.LocalPosition = Point(position.X - this.Origin.X, position.Y - this.Origin.Y)

/// A pointer event: enter, leave, move, down or up.
[<Sealed>]
type PointerEventArgs internal (kind: PointerKind, pointerId: int, position: Point, button: MouseButton voption, modifiers: Modifiers) =
    inherit InputEventArgs(position, modifiers)
    member _.PointerKind = kind
    /// Tells touches apart; the mouse is always 0 on desktop.
    member _.PointerId = pointerId
    /// The button that went down or up (ValueNone for move, enter and leave).
    member _.Button = button

/// Mouse wheel or touchpad scrolling, in notches (positive = away from the user / to the right).
[<Sealed>]
type WheelEventArgs internal (position: Point, deltaX: float, deltaY: float, modifiers: Modifiers) =
    inherit InputEventArgs(position, modifiers)
    member _.DeltaX = deltaX
    member _.DeltaY = deltaY

/// Text typed while an element has focus (after the key that made it). Goes to the focused element and bubbles.
[<Sealed>]
type TextInputEventArgs internal (text: string) =
    inherit RoutedEventArgs(Modifiers.None)
    /// What was typed: usually one character, more for a paste by the keyboard or an input method.
    member _.Text = text

/// Text an input method (an IME, a phone's keyboard) is composing: not typed yet. Each one replaces the one before;
/// an empty text ends the composition, and what was committed, if anything, comes as typed text. Goes to the first
/// element type from the focused element up that takes it (`Element.textComposition`).
[<Sealed>]
type TextCompositionEventArgs internal (text: string, caret: int) =
    inherit RoutedEventArgs(Modifiers.None)
    /// What is being composed; empty: the composition has ended.
    member _.Text = text
    /// Where the input method's caret stands in it (a UTF-16 index, 0 to the text's length).
    member _.Caret = max 0 (min text.Length caret)

/// A key going down or up. Goes to the focused element (the root when nothing has focus).
[<Sealed>]
type KeyEventArgs internal (key: Key, modifiers: Modifiers, isRepeat: bool) =
    inherit RoutedEventArgs(modifiers)
    member _.Key = key
    /// The key is held and the OS is repeating it (key down only).
    member _.IsRepeat = isRepeat

/// How a click arrived.
[<RequireQualifiedAccess>]
type ClickSource =
    /// A press and release over the element (mouse, touch or pen).
    | Pointer of kind: PointerKind
    /// Enter, or Space released, on the focused element.
    | Keyboard
    /// Asked for by assistive technology (a screen reader's click: `SemanticAction.Click`).
    | Assistive

/// A click: `Button.onClick (fun e -> if e.Modifiers.HasFlag Modifiers.Shift then ... else ...)`.
/// Clicks aren't routed: they go to the nearest element with onClick.
[<Sealed>]
type ClickEventArgs internal (source: ClickSource, modifiers: Modifiers) =
    member _.Source = source
    /// Modifier keys held when the click happened (on release for a pointer; with the key for
    /// Enter and Space).
    member _.Modifiers = modifiers
