namespace Scribble.Core

open Scribble.Rendering
open Scribble.Text

module TextProps =
    let text = Prop.define "Text" "" Affects.Measure
    let font = Prop.define "Font" (Fonts.Regular()) Affects.Measure
    let fontSize = Prop.define "FontSize" 14.0 Affects.Measure
    let foreground = Prop.define "Foreground" Color.Black Affects.Render
    /// Horizontal alignment of each line within the element.
    let textAlignment = Prop.define "TextAlignment" TextAlignment.Start Affects.Render
    let textWrapping = Prop.define "TextWrapping" NoWrap Affects.Measure
    let textTrimming = Prop.define "TextTrimming" NoTrimming Affects.Measure
    /// Maximum number of lines; 0 means no limit.
    let maxLines = Prop.define "MaxLines" 0 Affects.Measure
    /// The lines from the last measure (or arrange, if it was narrower), drawn by Render.
    let layout: Prop<TextLayout voption> = Prop.local "TextLayout" ValueNone

module internal TextBlockType =
    let value =
        { ElementType.Default with
            Name = "TextBlock"
            Measure =
                fun ctx available ->
                    let layout =
                        ctx.Text.Layout(
                            ctx.Get TextProps.text, ctx.Get TextProps.font, ctx.Get TextProps.fontSize, available.Width,
                            ctx.Get TextProps.textWrapping, ctx.Get TextProps.textTrimming, ctx.Get TextProps.maxLines)
                    ctx.SetLocal(TextProps.layout, ValueSome layout)
                    Size.Create(layout.Width, layout.Height)
            Arrange =
                fun ctx size ->
                    // Narrower than measured (a parent gave less than asked): wrapping and trimming change.
                    match ctx.Get TextProps.layout with
                    | ValueSome l when size.Width < l.Width - 0.01 && (ctx.Get TextProps.textWrapping = Wrap || ctx.Get TextProps.textTrimming <> NoTrimming) ->
                        let layout =
                            ctx.Text.Layout(
                                ctx.Get TextProps.text, ctx.Get TextProps.font, ctx.Get TextProps.fontSize, size.Width,
                                ctx.Get TextProps.textWrapping, ctx.Get TextProps.textTrimming, ctx.Get TextProps.maxLines)
                        ctx.SetLocal(TextProps.layout, ValueSome layout)
                    | _ -> ()
            Render =
                fun ctx list ->
                    match ctx.Get TextProps.layout with
                    | ValueSome l when (ctx.Get TextProps.text).Length > 0 ->
                        ctx.Text.DrawLayout(list, l, 0.0, 0.0, ctx.Size.Width, ctx.Get TextProps.textAlignment, ctx.Get TextProps.foreground)
                    | _ -> () }

/// Displays text in one font, size and color, with optional wrapping, trimming ("…") and a line limit.
[<AbstractClass>]
type TextBlock =
    inherit Layoutable
    static member Type: ElementType = TextBlockType.value
    static member create(settings: Setting<TextBlock> list) : View = View.Create(TextBlockType.value, settings)
    static member text<'k when 'k :> TextBlock>(value: string) : Setting<'k> = Setting.create TextProps.text (if isNull value then "" else value)
    static member font<'k when 'k :> TextBlock>(value: Font) : Setting<'k> = Setting.create TextProps.font value
    static member fontSize<'k when 'k :> TextBlock>(value: float) : Setting<'k> = Setting.create TextProps.fontSize value
    static member foreground<'k when 'k :> TextBlock>(value: Color) : Setting<'k> = Setting.create TextProps.foreground value
    static member textAlignment<'k when 'k :> TextBlock>(value: TextAlignment) : Setting<'k> = Setting.create TextProps.textAlignment value
    static member textWrapping<'k when 'k :> TextBlock>(value: TextWrapping) : Setting<'k> = Setting.create TextProps.textWrapping value
    static member textTrimming<'k when 'k :> TextBlock>(value: TextTrimming) : Setting<'k> = Setting.create TextProps.textTrimming value
    static member maxLines<'k when 'k :> TextBlock>(value: int) : Setting<'k> = Setting.create TextProps.maxLines value
