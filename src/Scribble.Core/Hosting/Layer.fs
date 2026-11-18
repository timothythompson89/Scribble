namespace Scribble.Core

open Scribble.Platform
open Scribble.Text

/// What the Layer settings configure.
type internal LayerOptions =
    { Name: string
      Order: int
      Modal: bool
      View: View voption
      AppStyles: bool
      WheelThrough: bool
      Keyboard: bool }

/// One setting for a layer: `Layer.order 100`, `Layer.modal true`, `Layer.view (toasts ())`.
[<Struct>]
type LayerSetting = internal LayerSetting of (LayerOptions -> LayerOptions)

/// A tree of elements drawn above the app (or above other layers): popups, toasts, dialogs, the
/// inspector. Every layer fills the window, is laid out on its own (it never moves the app), and
/// has its own keyboard focus and Tab order.
///
/// Pointer input goes to the topmost layer with an element under the pointer and falls through
/// everywhere else, so give a layer's content a size or an alignment rather than letting it
/// stretch over the window. A modal layer takes all pointer and keyboard input instead, wherever
/// it is: the layers below it get none until it goes.
///
/// Add one with `App.layer [ ... ]` or `ElementHost.AddLayer`; the app itself is the layer with
/// order 0.
[<Sealed>]
type Layer internal (text: TextSystem, options: LayerOptions, setCursor: Cursor -> unit) =
    let tree = ElementTree(text)
    let input = InputRouter(tree, setCursor)

    /// A name for tools such as the inspector.
    static member name(value: string) = LayerSetting(fun o -> { o with Name = value })

    /// Where the layer goes: higher is drawn on top and gets the pointer first. The app is 0;
    /// layers with the same order stack in the order they were added. Default: 100.
    static member order(value: int) = LayerSetting(fun o -> { o with Order = value })

    /// Takes all pointer and keyboard input while it shows something, so nothing below can be
    /// used (a dialog); showing nothing (no view, or only hidden elements), it blocks nothing, so
    /// a dialog layer can stay in place. Default: false (the pointer falls through wherever the
    /// layer has nothing).
    static member modal(value: bool) = LayerSetting(fun o -> { o with Modal = value })

    /// What the layer shows. Components inside keep their own state; `Update` shows another view.
    static member view(value: View) = LayerSetting(fun o -> { o with View = ValueSome value })

    /// Whether the app's styles (`App.styles`) apply in this layer. Default: true. Tools such as the
    /// inspector turn it off, so an app's styles don't restyle them.
    static member appStyles(value: bool) = LayerSetting(fun o -> { o with AppStyles = value })

    /// The mouse wheel goes on to the layer below, even where this layer has an element under the
    /// pointer: an overlay that takes clicks but shouldn't stop scrolling (the inspector's pick mode).
    /// Default: false. A modal layer that shows something takes the wheel anyway.
    static member letWheelThrough(value: bool) = LayerSetting(fun o -> { o with WheelThrough = value })

    /// False: a press in this layer doesn't bring the keyboard to it, so keys stay where they were (toasts).
    /// Default: true.
    static member keyboard(value: bool) = LayerSetting(fun o -> { o with Keyboard = value })

    member _.Name = options.Name
    member internal _.UsesAppStyles = options.AppStyles
    member internal _.LetsWheelThrough = options.WheelThrough
    member internal _.TakesKeyboard = options.Keyboard
    member _.Order = options.Order
    member _.IsModal = options.Modal
    member _.Tree = tree

    /// Shows a view (builds the elements the first time, patches them after that).
    member _.Update(view: View) = tree.Update view

    /// The element with keyboard focus in this layer, if any.
    member _.Focused: Element voption = input.Focused

    /// Moves this layer's focus as Tab (Next) or Shift+Tab (Previous) would.
    member _.MoveFocus(direction: FocusDirection) = input.MoveFocus direction

    member internal _.Input = input
    /// The view from `Layer.view`, shown once the host has set the tree up.
    member internal _.InitialView = options.View

    static member internal Create(text: TextSystem, settings: LayerSetting list, setCursor: Cursor -> unit) =
        let defaults = { Name = "Layer"; Order = 100; Modal = false; View = ValueNone; AppStyles = true; WheelThrough = false; Keyboard = true }
        let options = settings |> List.fold (fun o (LayerSetting apply) -> apply o) defaults
        Layer(text, options, setCursor)

    override _.ToString() = options.Name
