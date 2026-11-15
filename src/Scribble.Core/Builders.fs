/// EXPERIMENTAL: a second syntax for the same views, for comparison with settings lists:
///
///     VStack(spacing = 16.0).margin(24.0) {
///         Text($"Count: {count}").fontSize(28.0)
///         if count > 0 then Button("Reset", fun () -> dispatch Reset)
///     }
///
/// Constructors take the essentials; optional settings chain as modifiers, which exist only where
/// they apply (`.fontSize` on Text, `.padding` on Border-like elements). Children go in a block
/// that allows `if` and `for`; a panel's own modifiers come before its block (F# can't chain a
/// method call after a closing brace). Everything here produces ordinary Views, so both syntaxes mix.
/// `open type Scribble.Core.Builders.Ui` brings the constructors into scope. Names are provisional.
namespace Scribble.Core.Builders

open System.Runtime.CompilerServices
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// Something modifiers chain onto: a node, or a panel's builder before its block. 'k is the
/// element kind (which modifiers apply), 'r what a modifier returns.
type IModifiable<'k, 'r> =
    /// Adds any setting, for those without a modifier: `.With(TextBlock.textWrapping Wrap)`.
    abstract With: Setting<'k> -> 'r

/// A View whose element kind is known, so modifiers for that kind can be chained onto it.
[<Sealed>]
type Node<'k> internal (elementType: ElementType, settings: struct (Prop * obj)[]) =
    inherit View(elementType, settings)

    member _.With(setting: Setting<'k>) : Node<'k> =
        Node<'k>(elementType, Array.append settings [| struct (setting.Key, setting.BoxedValue) |])

    interface IModifiable<'k, Node<'k>> with
        member this.With setting = this.With setting

    static member internal Of(elementType: ElementType, settings: Setting<'k> list) =
        Node<'k>(elementType, [| for s in settings -> struct (s.Key, s.BoxedValue) |])

/// The `{ ... }` block of a panel: any number of children, with `if` and `for`.
type ChildrenBuilder<'k when 'k :> Panel> internal (elementType: ElementType, settings: Setting<'k> list) =
    interface IModifiable<'k, ChildrenBuilder<'k>> with
        member _.With setting = ChildrenBuilder<'k>(elementType, settings @ [ setting ])

    member _.Yield(view: View) : View list = [ view ]
    member _.Zero() : View list = []
    member _.Combine(first: View list, rest: unit -> View list) = first @ rest ()
    member _.Delay(f: unit -> View list) = f
    member _.For(items: 'T seq, body: 'T -> View list) = [ for item in items do yield! body item ]
    member _.Run(children: unit -> View list) : Node<'k> = Node<'k>.Of(elementType, settings @ [ Panel.children (children ()) ])

/// The `{ ... }` block of a Border: one child.
type ChildBuilder<'k when 'k :> Border> internal (elementType: ElementType, settings: Setting<'k> list) =
    interface IModifiable<'k, ChildBuilder<'k>> with
        member _.With setting = ChildBuilder<'k>(elementType, settings @ [ setting ])

    member _.Yield(view: View) = view
    member _.Delay(f: unit -> View) = f
    member _.Run(child: unit -> View) : Node<'k> = Node<'k>.Of(elementType, settings @ [ Border.child (child ()) ])

/// The constructors (`open type Scribble.Core.Builders.Ui`).
[<AbstractClass; Sealed>]
type Ui =
    static member VStack(?spacing: float) =
        ChildrenBuilder<StackPanel>(StackPanel.Type, [ StackPanel.spacing (defaultArg spacing 0.0) ])

    static member HStack(?spacing: float) =
        ChildrenBuilder<StackPanel>(StackPanel.Type, [ StackPanel.orientation Horizontal; StackPanel.spacing (defaultArg spacing 0.0) ])

    static member Frame() = ChildBuilder<Border>(Border.Type, [])

    static member Text(value: string) : Node<TextBlock> = Node<TextBlock>.Of(TextBlock.Type, [ TextBlock.text value ])

    static member Button(label: string, onClick: unit -> unit) : Node<Button> =
        // The same defaults as Button.create, plus the label and the handler.
        let (view: View) = Button.create [ Button.text label; Button.onClick onClick ]
        Node<Button>(Button.Type, view.Settings)

/// Modifiers: chained settings. Each exists only for the elements it applies to.
[<Extension; AbstractClass; Sealed>]
type Modifiers =
    [<Extension>]
    static member margin<'k, 'r when 'k :> Layoutable>(target: IModifiable<'k, 'r>, value: float) : 'r = target.With(Layoutable.margin value)
    [<Extension>]
    static member margin<'k, 'r when 'k :> Layoutable>(target: IModifiable<'k, 'r>, horizontal: float, vertical: float) : 'r = target.With(Layoutable.margin (horizontal, vertical))
    [<Extension>]
    static member width<'k, 'r when 'k :> Layoutable>(target: IModifiable<'k, 'r>, value: float) : 'r = target.With(Layoutable.width value)
    [<Extension>]
    static member height<'k, 'r when 'k :> Layoutable>(target: IModifiable<'k, 'r>, value: float) : 'r = target.With(Layoutable.height value)
    [<Extension>]
    static member onClick<'k, 'r when 'k :> Layoutable>(target: IModifiable<'k, 'r>, handler: unit -> unit) : 'r = target.With(Layoutable.onClick handler)
    [<Extension>]
    static member key<'k, 'r when 'k :> Layoutable>(target: IModifiable<'k, 'r>, value: obj) : 'r = target.With(Layoutable.key value)
    [<Extension>]
    static member background<'k, 'r when 'k :> Border>(target: IModifiable<'k, 'r>, value: Color) : 'r = target.With(Border.background value)
    [<Extension>]
    static member padding<'k, 'r when 'k :> Border>(target: IModifiable<'k, 'r>, value: float) : 'r = target.With(Border.padding value)
    [<Extension>]
    static member cornerRadius<'k, 'r when 'k :> Border>(target: IModifiable<'k, 'r>, value: float) : 'r = target.With(Border.cornerRadius value)
    [<Extension>]
    static member fontSize<'k, 'r when 'k :> TextBlock>(target: IModifiable<'k, 'r>, value: float) : 'r = target.With(TextBlock.fontSize value)
    [<Extension>]
    static member foreground<'k, 'r when 'k :> TextBlock>(target: IModifiable<'k, 'r>, value: Color) : 'r = target.With(TextBlock.foreground value)
    [<Extension>]
    static member font<'k, 'r when 'k :> TextBlock>(target: IModifiable<'k, 'r>, value: Font) : 'r = target.With(TextBlock.font value)
