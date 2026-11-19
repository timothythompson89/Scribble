namespace Scribble.Core

open Scribble.Accessibility
open Scribble.Rendering
open Scribble.Text

module ModalProps =
    /// A heading above the content, and the modal's name for screen readers.
    let title = Prop.define "Title" "" Affects.Nothing |> AppState.mark
    /// True: a press on the scrim asks to close it too.
    let lightDismiss = Prop.define "LightDismiss" false Affects.Nothing
    /// False: Escape doesn't ask to close it (a modal that must be answered).
    let closeOnEscape = Prop.define "CloseOnEscape" true Affects.Nothing
    /// Attached, on something inside a modal: it takes focus when the modal opens (else the first Tab stop does).
    let initialFocus = Prop.attached "Modal" "InitialFocus" false Affects.Nothing

/// The dimming a modal lays over everything below it (the host makes it; `Scrim.style` restyles it,
/// `Theme.scrim` sets its colour).
[<AbstractClass>]
type Scrim =
    inherit Border

module Scrim =
    /// The box takes what it wants, up to the scrim's size (so it never passes the window's edges).
    let private measure (ctx: MeasureContext) (available: Size) =
        let mutable w, h = 0.0, 0.0
        for child in ctx.Children do
            let d = ctx.Measure(child, available)
            w <- max w (min d.Width available.Width)
            h <- max h (min d.Height available.Height)
        Size.Create(w, h)

    /// The box in the middle, on whole DIPs.
    let private arrange (ctx: ArrangeContext) (size: Size) =
        for child in ctx.Children do
            let d = ctx.DesiredSize child
            let w, h = min d.Width size.Width, min d.Height size.Height
            ctx.Arrange(child, Rect.Create(System.Math.Floor((size.Width - w) / 2.0), System.Math.Floor((size.Height - h) / 2.0), w, h))

    let Type =
        Element.extend Border.Type "Scrim" [
            Element.measure measure
            Element.arrange arrange
            Element.defaults [ Border.background Palette.scrim ]
        ]
    let internal create (settings: Setting<Scrim> list) = View.Create(Type, settings)
    let style parts (settings: Setting<Scrim> list) = Style.forType Type parts settings

/// Where a Modal stands in the view: it takes no space and draws nothing; the host shows its modal
/// in a layer of its own while the view says it's open. Tools show it as "Modal", the name the app wrote.
module internal ModalAnchor =
    let Type =
        Element.define "Modal" [
            Element.measure (fun _ _ -> Size.Create(0.0, 0.0))
            // Not visible: panels give it no room and no gap. It is still in the tree, which is what the host looks at.
            Element.defaults [ Layoutable.isVisible false ]
        ]

/// Content shown over a scrim, in its own layer, taking all input until it closes (DESIGN.md, "4d:
/// Dialog and Toast"). Put it anywhere in the view; it takes no space there:
///
///     Modal.create [
///         Modal.isOpen model.Confirming
///         Modal.onOpenChanged (fun isOpen -> if not isOpen then dispatch Cancel)   // Escape
///         Modal.title "Delete the file?"
///         Modal.content (
///             StackPanel.create [ StackPanel.children [ ...; Button.create [ Button.text "Delete"; Button.onClick (fun _ -> dispatch Delete) ] ] ])
///     ]
///
/// The view decides whether it's open (`isOpen`); Escape only asks, through `onOpenChanged false`
/// (`closeOnEscape false` for a modal that must be answered; `lightDismiss true` lets a press on the
/// scrim ask too). Opening, focus goes to the first Tab stop inside and Tab stays inside; closing, it
/// goes back to what had it. The Modal is also the box its content sits in: a Border the theme
/// gives the surface colour, a line, rounded corners, padding and a shadow, in the middle of the
/// window, at most 560 DIPs wide and 16 DIPs from the window's edges (`Modal.style` for all).
[<AbstractClass>]
type Modal =
    inherit Border

    /// Whether it shows. The view decides: Escape and (with `lightDismiss`) a press on the scrim
    /// only call `onOpenChanged false`.
    static member isOpen<'k when 'k :> Modal>(value: bool) : Setting<'k> = Setting.create FlyoutProps.isOpen (ValueSome value)
    /// It asks to close (`false`): Escape, or a press on the scrim with `lightDismiss`.
    static member onOpenChanged<'k when 'k :> Modal>(handler: bool -> unit) : Setting<'k> =
        Setting.create FlyoutProps.onOpenChanged (ValueSome handler)
    /// A heading above the content, and the modal's name for screen readers.
    static member title<'k when 'k :> Modal>(text: string) : Setting<'k> = Setting.create ModalProps.title text
    /// True: a press on the scrim asks to close it, as Escape does (default false).
    static member lightDismiss<'k when 'k :> Modal>(value: bool) : Setting<'k> = Setting.create ModalProps.lightDismiss value
    /// False: Escape doesn't ask to close it (default true).
    static member closeOnEscape<'k when 'k :> Modal>(value: bool) : Setting<'k> = Setting.create ModalProps.closeOnEscape value
    /// Attached, on something focusable inside a modal: it takes focus when the modal opens, in place of the first
    /// Tab stop (`Button.create [ Modal.initialFocus true; Button.text "Cancel" ]`).
    static member initialFocus<'k when 'k :> Layoutable>(value: bool) : Setting<'k> = Setting.create ModalProps.initialFocus value

module Modal =
    /// How close the box may come to the window's edges, and the most it's wide, in DIPs.
    let private edge = 16.0
    let private maxWidth = 560.0

    let Type =
        Element.extend Border.Type "Modal" [
            Element.defaults [
                Semantics.role Role.Dialog
                Semantics.labelFrom ModalProps.title
                Layoutable.maxWidth maxWidth
                Layoutable.margin edge
                // Its look at rest, by theme value: the surface colour, a line, the theme's corners, a wide shadow.
                Border.background Palette.surface
                Border.borderBrush Palette.border
                Border.borderThickness 1.0
                Border.cornerRadius Palette.cornerRadius
                Border.padding 20.0
                Setting.fromThemeWith BorderProps.shadow Palette.shadow ThemeConvert.modalShadow
            ]
        ]

    /// The box: its settings, with the title (if any) as a heading above the content.
    let private boxView (settings: Setting<Modal> list) =
        let title = settings |> List.tryFindBack (fun s -> obj.ReferenceEquals(s.Prop, ModalProps.title)) |> Option.map (fun s -> unbox<string> s.Value)
        let content =
            settings
            |> List.tryFindBack (fun s -> obj.ReferenceEquals(s.Prop, BorderProps.content))
            |> Option.bind (fun s -> unbox<View voption> s.Value |> ValueOption.toOption)
        match title with
        | Some text when text <> "" ->
            let stacked =
                StackPanel.create [
                    StackPanel.spacing 12.0
                    StackPanel.children [ TextBlock.create [ TextBlock.fontSize 18.0; TextBlock.text text; TextBlock.textWrapping Wrap ]; yield! Option.toList content ]
                ]
            // The content goes into the stack only: left in the list as well, the same view would be given to two
            // places, and what's inside it would be made again on every update (losing focus and state).
            let others = settings |> List.filter (fun s -> not (obj.ReferenceEquals(s.Prop, BorderProps.content)))
            View.Create(Type, others @ [ Setting.create BorderProps.content (ValueSome stacked) ])
        | _ -> View.Create(Type, settings)

    /// A Modal in the view: the place it stands (nothing shows there) holding the box the host shows.
    let create (settings: Setting<Modal> list) : View =
        View.Create(ModalAnchor.Type, [ Setting.create FlyoutProps.flyout (ValueSome(boxView settings)) ])
    /// A style for Modals (the box): `Modal.style [] [ Modal.cornerRadius 12.0 ]`.
    let style (parts: SelectorPart list) (settings: Setting<Modal> list) : Style = Style.forType Type parts settings
