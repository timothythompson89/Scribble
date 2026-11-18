/// The Images page (M8 step 3b (d) and (e)): ImageView's stretch modes and directions, smooth against pixelated, a round
/// avatar and a 2× picture, an image made from pixels, image cursors, two from URLs (one missing), a broken one reporting its
/// error, and saving as PNG (a captured card, the Gallery rendered with no window). The embedded pictures are drawn by
/// tools/MakeGalleryImages.fsx and embedded in the Gallery (Images/*.png).
module Scribble.Samples.Gallery.ImagesPage

open System
open Scribble.Text
open Scribble.Rendering
open Scribble.Core
open Scribble.Accessibility
open Scribble.Samples.Gallery.Common

type private Marker = class end

let private resource (name: string) settings = Image.fromResource (typeof<Marker>.Assembly, name) settings

let private landscape = resource "landscape.png" []
let private potion = resource "potion.png" []
let private avatar = resource "avatar.png" [ Image.density 2.0 ]

/// A labelled box `w × h` showing `image`, its background showing where the image doesn't reach.
let private boxed (label: string) (w: float) (h: float) (settings: Setting<ImageView> list) =
    StackPanel.create [
        StackPanel.spacing 4.0
        StackPanel.children [
            Border.create [
                Border.width w
                Border.height h
                Border.background subtle
                Border.borderBrush line
                Border.borderThickness 1.0
                Border.content (ImageView.create settings)
            ]
            TextBlock.create [ TextBlock.text label; TextBlock.fontSize 12.0; TextBlock.foreground Palette.textDim ]
        ]
    ]

let private wrap (children: View list) =
    WrapPanel.create [ WrapPanel.spacing 12.0; WrapPanel.lineSpacing 12.0; WrapPanel.children children ]

/// A `size`-square pattern of rings and waves, straight RGBA, changing with `phase`.
let private pattern (size: int) (phase: float) =
    let px = Array.zeroCreate<byte> (size * size * 4)
    for y in 0 .. size - 1 do
        for x in 0 .. size - 1 do
            let u = float x / float size - 0.5
            let v = float y / float size - 0.5
            let d = sqrt (u * u + v * v)
            let t = sin (d * 40.0 - phase) * 0.5 + 0.5
            let s = sin (u * 12.0 + phase * 0.7) * sin (v * 12.0 - phase * 0.4) * 0.5 + 0.5
            let i = (y * size + x) * 4
            px[i] <- byte (40.0 + 200.0 * t)
            px[i + 1] <- byte (60.0 + 160.0 * s)
            px[i + 2] <- byte (180.0 + 70.0 * (1.0 - t))
            // Fades out at the edge of a circle: straight alpha, premultiplied by Image.fromPixels.
            px[i + 3] <- byte (255.0 * Math.Clamp((0.5 - d) * 20.0, 0.0, 1.0))
    px

/// A 32-pixel target, straight RGBA: red and white rings, transparent outside, a dark dot in the middle.
let private targetPixels =
    let n = 32
    let px = Array.zeroCreate<byte> (n * n * 4)
    for y in 0 .. n - 1 do
        for x in 0 .. n - 1 do
            let d = sqrt ((float x + 0.5 - 16.0) ** 2.0 + (float y + 0.5 - 16.0) ** 2.0)
            let i = (y * n + x) * 4
            let r, g, b =
                if d < 2.5 then 30.0, 20.0, 40.0
                elif int (d / 3.5) % 2 = 0 then 235.0, 60.0, 70.0
                else 250.0, 250.0, 250.0
            px[i] <- byte r
            px[i + 1] <- byte g
            px[i + 2] <- byte b
            px[i + 3] <- byte (255.0 * Math.Clamp(15.5 - d, 0.0, 1.0))
    px

/// The potion at 32 DIPs (twice its pixels), square pixels, pointing from the bottle's mouth; the target at its own
/// 32 DIPs, smooth, pointing at its middle (DESIGN.md, "M8 step 3b", (f) 8). Defined once, as values.
let private potionCursor =
    Cursor.fromImage potion [ Cursor.size 32.0; Cursor.scaling ImageScaling.Pixelated; Cursor.hotspot (16.0, 3.0) ]
let private targetCursor = Cursor.fromImage (Image.fromPixels ("cursor-target", 32, 32, targetPixels) []) []

/// A box to hover for `cursor`.
let private hoverBox (label: string) (cursor: Cursor) =
    Border.create [
        Border.width 150.0
        Border.height 90.0
        Border.background subtle
        Border.borderBrush line
        Border.borderThickness 1.0
        Border.cornerRadius 6.0
        Border.cursor cursor
        Border.content (
            TextBlock.create [
                TextBlock.text label
                TextBlock.fontSize 12.0
                TextBlock.foreground Palette.textDim
                TextBlock.horizontalAlignment HorizontalAlignment.Center
                TextBlock.verticalAlignment VerticalAlignment.Center
            ]
        )
    ]

/// A public-domain picture on Wikimedia Commons (172 × 178), sent with Access-Control-Allow-Origin so the browser
/// may show it, and an address there that gives 404.
let private webPicture = Image.fromUrl "https://upload.wikimedia.org/wikipedia/commons/7/70/Example.png" []
let private missingPicture = Image.fromUrl "https://upload.wikimedia.org/wikipedia/commons/0/00/Scribble-no-such-picture.png" []

/// Not a PNG past its first bytes: decoding fails, and the error goes to App.onError (a toast here).
let private brokenBytes = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy; 0uy; 0uy; 0uy; 13uy |]

// --- Saving as PNG (DESIGN.md, "Rendering to an image and saving it", round 2, decision 10) ---------------------

/// The Gallery as rendered: the window's size at 2×.
let private renderSize = 720.0, 560.0

/// The part of the page that renders: on a button press only, so the page's own picture doesn't depend on it.
let private saving =
    Component.define "Saving" (fun ctx () ->
        let card = ctx.useCapture ()
        let whole = ctx.useAppValue wholeGallery
        let rendered = ctx.useState<Image voption> ValueNone
        // Errors (no GPU readback, the dialog failing) show as a toast.
        let run (work: unit -> Threading.Tasks.Task) =
            task {
                try
                    do! work ()
                with e ->
                    toaster.Show [ Toast.kind ToastKind.Error; Toast.text e.Message ]
            }
            |> ignore
        let save (name: string) (image: Image) =
            task {
                let! png = Image.toPng image
                let! saved = Files.save name png
                toaster.Show(if saved then $"Saved {name} ({png.Length / 1024} KB)" else "Not saved")
            }
        let export () =
            run (fun () ->
                task {
                    // The shadow falls outside the card: 16 DIPs of margin keep it; 2× for a sharp picture.
                    let! image = card.ToImage [ Capture.margin 16.0; Capture.scale 2.0 ]
                    do! save "card.png" image
                })
        let render () =
            run (fun () ->
                task {
                    match whole.Current with
                    | ValueSome gallery ->
                        // A fresh Gallery: its first tab, nothing hovered, in the window's theme.
                        let! image = ViewImage.render (gallery ()) [ ViewImage.size renderSize; ViewImage.scale 2.0 ]
                        rendered.Set(ValueSome image)
                    | ValueNone -> toaster.Show "Nothing to render: the Gallery sets wholeGallery when it starts."
                })
        let w, h = renderSize
        StackPanel.create [
            StackPanel.spacing 12.0
            StackPanel.children [
                Border.create [
                    Border.capture card
                    Border.width 220.0
                    Border.padding 14.0
                    Border.horizontalAlignment HorizontalAlignment.Left
                    Border.cornerRadius 12.0
                    Border.background Palette.surface
                    Border.shadow floatShadow
                    Border.content (
                        StackPanel.create [
                            StackPanel.spacing 8.0
                            StackPanel.children [
                                TextBlock.create [ TextBlock.text "This card, as a PNG"; TextBlock.fontSize 15.0 ]
                                ImageView.create [
                                    ImageView.image potion
                                    ImageView.width 48.0
                                    ImageView.height 48.0
                                    ImageView.stretchDirection StretchDirection.Both
                                    ImageView.scaling ImageScaling.Pixelated
                                    ImageView.horizontalAlignment HorizontalAlignment.Left
                                ]
                                TextBlock.create [
                                    TextBlock.text "Captured as it is on screen, shadow and all."
                                    TextBlock.fontSize 12.0
                                    TextBlock.foreground Palette.textDim
                                    TextBlock.textWrapping TextWrapping.Wrap
                                ]
                            ]
                        ]
                    )
                ]
                Button.create [
                    Button.text "Export PNG"
                    Button.horizontalAlignment HorizontalAlignment.Left
                    Button.onClick (fun _ -> export ())
                ]
                TextBlock.create [
                    TextBlock.text "The whole Gallery, drawn again with no window: a fresh copy (its first tab, nothing hovered), not this screen."
                    TextBlock.fontSize 12.0
                    TextBlock.foreground Palette.textDim
                    TextBlock.textWrapping TextWrapping.Wrap
                    TextBlock.margin (0.0, 8.0, 0.0, 0.0)
                ]
                WrapPanel.create [
                    WrapPanel.spacing 8.0
                    WrapPanel.children [
                        Button.create [ Button.text "Render the Gallery"; Button.onClick (fun _ -> render ()) ]
                        Button.create [
                            Button.text "Save as PNG"
                            Button.isEnabled rendered.Current.IsSome
                            Button.onClick (fun _ -> rendered.Current |> ValueOption.iter (fun i -> run (fun () -> save "gallery.png" i)))
                        ]
                    ]
                ]
                // A frame there from the start, the picture at half size inside its border, so the page doesn't grow
                // (at its bottom, a picture added below the buttons would land out of sight). Wrapped to sit left.
                wrap [
                    boxed $"{w:F0} × {h:F0} at 2×, shown at half size" (w / 2.0 + 2.0) (h / 2.0 + 2.0) [
                        match rendered.Current with
                        | ValueSome image -> ImageView.image image
                        | ValueNone -> ()
                    ]
                ]
            ]
        ])

let private images =
    Component.define "Images" (fun ctx () ->
        let phase = ctx.useState 0.0
        let showBroken = ctx.useState false
        let showWeb = ctx.useState false
        StackPanel.create [
            StackPanel.spacing 22.0
            StackPanel.children [
                section "Stretch" "The same 640 × 400 picture in four square boxes; the box's background shows where it doesn't reach." (
                    wrap [
                        for label, stretch in
                            [ "Uniform (default)", ImageStretch.Uniform
                              "UniformToFill", ImageStretch.UniformToFill
                              "Fill", ImageStretch.Fill
                              "None", ImageStretch.None ] ->
                            boxed label 130.0 130.0 [ ImageView.image landscape; ImageView.stretch stretch ]
                    ])
                section "Stretch direction" "A 16-pixel picture in 96-DIP boxes: DownOnly (default) never enlarges it." (
                    wrap [
                        for label, direction in
                            [ "DownOnly (default)", StretchDirection.DownOnly
                              "UpOnly", StretchDirection.UpOnly
                              "Both", StretchDirection.Both ] ->
                            boxed label 96.0 96.0 [ ImageView.image potion; ImageView.stretchDirection direction ]
                    ])
                section "Smooth and pixelated" "Enlarged 8×: Smooth blends neighbouring pixels, Pixelated keeps them square." (
                    wrap [
                        for label, scaling in [ "Smooth (default)", ImageScaling.Smooth; "Pixelated", ImageScaling.Pixelated ] ->
                            boxed label 128.0 128.0 [
                                ImageView.image potion
                                ImageView.stretchDirection StretchDirection.Both
                                ImageView.scaling scaling
                            ]
                    ])
                section "Round and 2×" "A 256-pixel avatar at density 2 shows at 128 DIPs, sharp on a 2× screen; cornerRadius makes it round." (
                    wrap [
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                ImageView.create [ ImageView.image avatar; Semantics.label "Avatar"; ImageView.horizontalAlignment HorizontalAlignment.Left ]
                                TextBlock.create [ TextBlock.text "Density 2: 128 DIPs"; TextBlock.fontSize 12.0; TextBlock.foreground Palette.textDim ]
                            ]
                        ]
                        StackPanel.create [
                            StackPanel.spacing 4.0
                            StackPanel.children [
                                ImageView.create [
                                    ImageView.image avatar
                                    ImageView.width 64.0
                                    ImageView.height 64.0
                                    ImageView.stretch ImageStretch.UniformToFill
                                    ImageView.cornerRadius 32.0
                                    Semantics.label "Round avatar"
                                ]
                                TextBlock.create [ TextBlock.text "64 DIPs, cornerRadius 32"; TextBlock.fontSize 12.0; TextBlock.foreground Palette.textDim ]
                            ]
                        ]
                    ])
                section "Made from pixels" "Image.fromPixels: each position of the slider is a new picture (its key names the phase)." (
                    StackPanel.create [
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            ImageView.create [
                                ImageView.image (Image.fromPixels ($"pattern-{phase.Current:F0}", 128, 128, pattern 128 (phase.Current / 10.0)) [])
                                ImageView.horizontalAlignment HorizontalAlignment.Left
                            ]
                            Slider.create [
                                Slider.value phase.Current
                                Slider.maximum 60.0
                                Slider.step 1.0
                                Slider.onValueChanged phase.Set
                                Slider.width 260.0
                                Slider.horizontalAlignment HorizontalAlignment.Left
                                Semantics.label "Phase"
                            ]
                        ]
                    ])
                section "Image cursors" "Cursor.fromImage: hover the boxes. The potion is enlarged to 32 DIPs with square pixels; the target, made with Image.fromPixels, is smooth at its own size." (
                    wrap [
                        hoverBox "Potion" potionCursor
                        hoverBox "Target" targetCursor
                    ])
                section "From a URL""Downloaded when shown, at their own size once they arrive (0 × 0 until then). The second gives 404: one toast. Needs the internet; nothing is asked for until you press the button." (
                    StackPanel.create [
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            ToggleButton.create [
                                ToggleButton.text "Load from Wikimedia"
                                ToggleButton.isChecked showWeb.Current
                                ToggleButton.onCheckedChanged showWeb.Set
                                ToggleButton.horizontalAlignment HorizontalAlignment.Left
                            ]
                            if showWeb.Current then
                                wrap [
                                    ImageView.create [ ImageView.image webPicture; Semantics.label "Example picture from Wikimedia" ]
                                    boxed "404: nothing drawn" 96.0 96.0 [ ImageView.image missingPicture ]
                                ]
                        ]
                    ])
                section "A broken image" "Its decoding fails: the error goes to App.onError once (a toast in the Gallery) and nothing is drawn." (
                    StackPanel.create [
                        StackPanel.spacing 8.0
                        StackPanel.children [
                            ToggleButton.create [
                                ToggleButton.text "Show a broken image"
                                ToggleButton.isChecked showBroken.Current
                                ToggleButton.onCheckedChanged showBroken.Set
                                ToggleButton.horizontalAlignment HorizontalAlignment.Left
                            ]
                            if showBroken.Current then
                                boxed "Nothing drawn" 96.0 96.0 [ ImageView.image (Image.fromBytes ("broken", brokenBytes) []) ]
                        ]
                    ])
                section "Saving as PNG" "Capture: an element as it is on screen; Files.save: a save dialog on the desktop, a download in the browser. ViewImage: any view drawn on its own, here the whole Gallery." (
                    saving ())
            ]
        ])

let view () =
    page "Images" "ImageView shows an image: fitted into its box, smooth or pixelated, rounded, at any density." [ images () ]
