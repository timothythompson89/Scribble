namespace Scribble.Core

open Scribble.Accessibility
open Scribble.Rendering
open Scribble.Text

module DialogProps =
    /// A heading above the content, and the dialog's name for screen readers.
    let title = Prop.define "Title" "" Affects.Nothing |> AppState.mark
    /// True: a press on the scrim asks to close it too.
    let lightDismiss = Prop.define "LightDismiss" false Affects.Nothing
    /// False: Escape doesn't ask to close it (a dialog that must be answered).
    let closeOnEscape = Prop.define "CloseOnEscape" true Affects.Nothing

/// The dimming a dialog lays over everything below it (the host makes it; `Scrim.style` restyles it,
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
            Element.defaults [ Border.background (Color.Black.WithAlpha 0.55f) ]
        ]
    let internal create (settings: Setting<Scrim> list) = View.Create(Type, settings)
    let style parts (settings: Setting<Scrim> list) = Style.forType Type parts settings

/// Where a Dialog stands in the view: it takes no space and draws nothing; the host shows its dialog
/// in a layer of its own while the view says it's open.
module internal DialogAnchor =
    let Type =
        Element.define "DialogAnchor" [
            Element.measure (fun _ _ -> Size.Create(0.0, 0.0))
            Element.defaults [ Layoutable.isHitTestVisible false; Layoutable.horizontalAlignment HorizontalAlignment.Left; Layoutable.verticalAlignment VerticalAlignment.Top ]
        ]

/// Content shown over a scrim, in its own layer, taking all input until it closes (DESIGN.md, "4d:
/// Dialog and Toast"). Put it anywhere in the view; it takes no space there:
///
///     Dialog.create [
///         Dialog.isOpen model.Confirming
///         Dialog.onOpenChanged (fun isOpen -> if not isOpen then dispatch Cancel)   // Escape
///         Dialog.title "Delete the file?"
///         Dialog.content (
///             StackPanel.create [ StackPanel.children [ ...; Button.create [ Button.text "Delete"; Button.onClick (fun _ -> dispatch Delete) ] ] ])
///     ]
///
/// The view decides whether it's open (`isOpen`); Escape only asks, through `onOpenChanged false`
/// (`closeOnEscape false` for a dialog that must be answered; `lightDismiss true` lets a press on the
/// scrim ask too). Opening, focus goes to the first Tab stop inside and Tab stays inside; closing, it
/// goes back to what had it. The Dialog is also the box its content sits in: a Border the theme
/// gives the surface colour, a line, rounded corners, padding and a shadow, in the middle of the
/// window, at most 560 DIPs wide and 16 DIPs from the window's edges (`Dialog.style` for all).
[<AbstractClass>]
type Dialog =
    inherit Border

    /// Whether it shows. The view decides: Escape and (with `lightDismiss`) a press on the scrim
    /// only call `onOpenChanged false`.
    static member isOpen<'k when 'k :> Dialog>(value: bool) : Setting<'k> = Setting.create FlyoutProps.isOpen (ValueSome value)
    /// It asks to close (`false`): Escape, or a press on the scrim with `lightDismiss`.
    static member onOpenChanged<'k when 'k :> Dialog>(handler: bool -> unit) : Setting<'k> =
        Setting.create FlyoutProps.onOpenChanged (ValueSome handler)
    /// A heading above the content, and the dialog's name for screen readers.
    static member title<'k when 'k :> Dialog>(text: string) : Setting<'k> = Setting.create DialogProps.title text
    /// True: a press on the scrim asks to close it, as Escape does (default false).
    static member lightDismiss<'k when 'k :> Dialog>(value: bool) : Setting<'k> = Setting.create DialogProps.lightDismiss value
    /// False: Escape doesn't ask to close it (default true).
    static member closeOnEscape<'k when 'k :> Dialog>(value: bool) : Setting<'k> = Setting.create DialogProps.closeOnEscape value

module Dialog =
    /// How close the box may come to the window's edges, and the most it's wide, in DIPs.
    let private edge = 16.0
    let private maxWidth = 560.0

    let Type =
        Element.extend Border.Type "Dialog" [
            Element.defaults [
                Semantics.role Role.Dialog
                Semantics.labelFrom DialogProps.title
                Layoutable.maxWidth maxWidth
                Layoutable.margin edge
                // The dark base look (also without a theme).
                Border.background (Color.Hex 0x24243A)
                Border.cornerRadius 6.0
                Border.padding 20.0
            ]
        ]

    /// The box: its settings, with the title (if any) as a heading above the content.
    let private boxView (settings: Setting<Dialog> list) =
        let title = settings |> List.tryFindBack (fun s -> obj.ReferenceEquals(s.Prop, DialogProps.title)) |> Option.map (fun s -> unbox<string> s.Value)
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
            View.Create(Type, settings @ [ Setting.create BorderProps.content (ValueSome stacked) ])
        | _ -> View.Create(Type, settings)

    /// A Dialog in the view: the place it stands (nothing shows there) holding the box the host shows.
    let create (settings: Setting<Dialog> list) : View =
        View.Create(DialogAnchor.Type, [ Setting.create FlyoutProps.flyout (ValueSome(boxView settings)) ])
    /// A style for Dialogs (the box): `Dialog.style [] [ Dialog.cornerRadius 12.0 ]`.
    let style (parts: SelectorPart list) (settings: Setting<Dialog> list) : Style = Style.forType Type parts settings
