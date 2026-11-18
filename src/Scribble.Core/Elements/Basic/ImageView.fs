namespace Scribble.Core

open System
open Scribble.Accessibility
open Scribble.Rendering

module ImageViewProps =
    let image: Prop<Image voption> = Prop.define "Image" ValueNone Affects.Measure
    let stretch = Prop.define "Stretch" ImageStretch.Uniform Affects.Render
    let stretchDirection = Prop.define "StretchDirection" StretchDirection.DownOnly Affects.Render
    let scaling = Prop.define "Scaling" ImageScaling.Smooth Affects.Render
    let cornerRadius = Prop.define "CornerRadius" CornerRadius.Zero Affects.Render

/// Shows an image, centred in its space: its own size (pixels ÷ density) unless `width`/`height` or alignment
/// give it another box, which the image is fitted into by `stretch` (Uniform by default) as far as
/// `stretchDirection` allows (DownOnly by default: never bigger than its own size). Like IconView (DESIGN.md,
/// "M8 step 3b", (d) 6): `ImageView.create [ ImageView.image photo; ImageView.stretch ImageStretch.UniformToFill ]`.
/// Nothing is drawn while the image loads or after it failed. Wrap it in a Border for a frame or background.
[<AbstractClass>]
type ImageView =
    inherit Layoutable
    static member image<'k when 'k :> ImageView>(value: Image) : Setting<'k> = Setting.create ImageViewProps.image (ValueSome value)
    /// How the image fits its box (default Uniform).
    static member stretch<'k when 'k :> ImageView>(value: ImageStretch) : Setting<'k> = Setting.create ImageViewProps.stretch value
    /// Which way it may scale to fit (default DownOnly).
    static member stretchDirection<'k when 'k :> ImageView>(value: StretchDirection) : Setting<'k> =
        Setting.create ImageViewProps.stretchDirection value
    /// Smooth (default) or Pixelated: pixel art stays square when enlarged.
    static member scaling<'k when 'k :> ImageView>(value: ImageScaling) : Setting<'k> = Setting.create ImageViewProps.scaling value
    /// Rounds the drawn image's corners (a round avatar: a square image, half its size).
    static member cornerRadius<'k when 'k :> ImageView>(value: CornerRadius) : Setting<'k> = Setting.create ImageViewProps.cornerRadius value
    static member cornerRadius<'k when 'k :> ImageView>(uniform: float) : Setting<'k> =
        Setting.create ImageViewProps.cornerRadius (CornerRadius.Uniform uniform)

module ImageView =
    /// Where an image of `natural` size (DIPs) goes in `area` under `stretch` and `direction`: centred, possibly
    /// bigger than `area` (UniformToFill, None).
    let internal fit (natural: Size) (area: Rect) (stretch: ImageStretch) (direction: StretchDirection) : Rect =
        let limit s =
            match direction with
            | StretchDirection.DownOnly -> min s 1.0
            | StretchDirection.UpOnly -> max s 1.0
            | StretchDirection.Both -> s
        let sx, sy =
            if natural.Width <= 0.0 || natural.Height <= 0.0 then 1.0, 1.0
            else
                let fx = area.Width / natural.Width
                let fy = area.Height / natural.Height
                match stretch with
                | ImageStretch.None -> 1.0, 1.0
                | ImageStretch.Fill -> limit fx, limit fy
                | ImageStretch.Uniform ->
                    let s = limit (min fx fy)
                    s, s
                | ImageStretch.UniformToFill ->
                    let s = limit (max fx fy)
                    s, s
        let w = natural.Width * sx
        let h = natural.Height * sy
        Rect.Create(area.X + (area.Width - w) / 2.0, area.Y + (area.Height - h) / 2.0, w, h)

    /// Draws `image` fitted into `area` (for elements that show an image inside, as IconView.draw): what sticks out
    /// of `area` is left out by drawing only the part of the image that shows.
    let internal draw (ctx: RenderContext) (list: DrawList) (image: Image) (area: Rect) =
        match ctx.Image image, ctx.ImageSize image with
        | ValueSome decoded, ValueSome natural ->
            // Centred on whole pixels, keeping its size: a half-pixel position would round both edges the same
            // way (or blur a 1:1 image).
            let placed = fit natural area (ctx.Get ImageViewProps.stretch) (ctx.Get ImageViewProps.stretchDirection)
            let s = ctx.Element.Scale
            let placed = Rect.Create(Math.Round(placed.X * s) / s, Math.Round(placed.Y * s) / s, placed.Width, placed.Height)
            let shown = placed.Intersect area
            if not shown.IsEmpty && placed.Width > 0.0 && placed.Height > 0.0 then
                let kx = float decoded.Width / placed.Width
                let ky = float decoded.Height / placed.Height
                let source = Rect.Create((shown.X - placed.X) * kx, (shown.Y - placed.Y) * ky, shown.Width * kx, shown.Height * ky)
                list.DrawImage(shown, decoded, ctx.Get ImageViewProps.cornerRadius, ctx.Get ImageViewProps.scaling, source)
        | _ -> ()

    let Type =
        Element.define "ImageView" [
            Element.defaults [ Semantics.role Role.Image ]
            Element.measure (fun ctx _ ->
                match ctx.Get ImageViewProps.image with
                | ValueSome image ->
                    match ctx.ImageSize image with
                    | ValueSome s -> s
                    | ValueNone -> Size.Create(0.0, 0.0)
                | ValueNone -> Size.Create(0.0, 0.0))
            Element.render (fun ctx list ->
                match ctx.Get ImageViewProps.image with
                | ValueSome image -> draw ctx list image (Rect.Create(0.0, 0.0, ctx.Size.Width, ctx.Size.Height))
                | ValueNone -> ())
        ]

    let create (settings: Setting<ImageView> list) : View = View.Create(Type, settings)
    /// A style for ImageViews: `ImageView.style [] [ ImageView.cornerRadius 8.0 ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<ImageView> list) : Style = Style.forType Type parts settings
