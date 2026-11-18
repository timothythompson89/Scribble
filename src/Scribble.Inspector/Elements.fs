namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

module internal ChevronProps =
    let isOpen = Prop.define "IsOpen" false Affects.Render
    let color = Prop.define "Color" Color.White Affects.Render

/// A small triangle: pointing right (closed) or down (open). Inter has no triangle glyphs, so it's
/// drawn as thin strips.
[<AbstractClass>]
type internal Chevron =
    inherit Layoutable
    static member isOpen<'k when 'k :> Chevron>(value: bool) : Setting<'k> = Setting.create ChevronProps.isOpen value
    static member color<'k when 'k :> Chevron>(value: Color) : Setting<'k> = Setting.create ChevronProps.color value

module internal Chevron =
    let private render (ctx: RenderContext) (list: DrawList) =
        let color = ctx.Get ChevronProps.color
        let cx, cy = ctx.Size.Width / 2.0, ctx.Size.Height / 2.0
        let strips = 8
        let step = 0.5
        for i in 0 .. strips - 1 do
            // From the triangle's base (full length) to its tip.
            let along = float i * step
            let length = 8.0 * (1.0 - float i / float strips)
            let r =
                if ctx.Get ChevronProps.isOpen then Rect.Create(cx - length / 2.0, cy - 2.0 + along, length, step)
                else Rect.Create(cx - 2.0 + along, cy - length / 2.0, step, length)
            list.FillRect(r, color, EdgeMode.Precise)

    let Type =
        Element.define "InspectorChevron" [
            Element.measure (fun _ _ -> Size.Create(12.0, 12.0))
            Element.render render
        ]

    let create (settings: Setting<Chevron> list) = View.Create(Type, settings)

/// The corner grip of a floating panel: a small triangle of dots.
[<AbstractClass>]
type internal Grip =
    inherit Layoutable

module internal Grip =
    let Type =
        Element.define "InspectorGrip" [
            Element.measure (fun _ _ -> Size.Create(16.0, 16.0))
            Element.render (fun ctx list ->
                let color = (Color.Hex 0x8A8AA0).WithAlpha 0.8f
                let s = ctx.Size
                for i, j in [ 0, 0; 1, 0; 0, 1; 2, 0; 1, 1; 0, 2 ] do
                    list.FillRect(Rect.Create(s.Width - 5.0 - float i * 4.0, s.Height - 5.0 - float j * 4.0, 2.0, 2.0), color))
        ]

    let create (settings: Setting<Grip> list) = View.Create(Type, settings)

module internal ThumbnailProps =
    let decoded: Prop<Scribble.Imaging.DecodedImage voption> = Prop.define "Decoded" ValueNone Affects.Render

/// A held image, drawn without holding it (so the inspector doesn't change the count it shows). Sized by the view.
[<AbstractClass>]
type internal Thumbnail =
    inherit Layoutable
    static member decoded<'k when 'k :> Thumbnail>(value: Scribble.Imaging.DecodedImage) : Setting<'k> =
        Setting.create ThumbnailProps.decoded (ValueSome value)

module internal Thumbnail =
    let Type =
        Element.define "InspectorThumbnail" [
            Element.render (fun ctx list ->
                match ctx.Get ThumbnailProps.decoded with
                // Released once nothing holds it: the next refresh drops the thumbnail.
                | ValueSome d when not d.IsReleased -> list.DrawImage(Rect.Create(0.0, 0.0, ctx.Size.Width, ctx.Size.Height), d)
                | _ -> ())
        ]

    let create (settings: Setting<Thumbnail> list) = View.Create(Type, settings)
