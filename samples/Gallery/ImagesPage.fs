/// The Images page (M8 step 3b (d) and (e)): ImageView's stretch modes and directions, smooth against pixelated, a round
/// avatar and a 2× picture, an image made from pixels, two from URLs (one missing), and a broken one reporting its
/// error. The embedded pictures are drawn by tools/MakeGalleryImages.fsx and embedded in the Gallery (Images/*.png).
module Scribble.Samples.Gallery.ImagesPage

open System
open Scribble.Text
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
                Border.child (ImageView.create settings)
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

/// A public-domain picture on Wikimedia Commons (172 × 178), sent with Access-Control-Allow-Origin so the browser
/// may show it, and an address there that gives 404.
let private webPicture = Image.fromUrl "https://upload.wikimedia.org/wikipedia/commons/7/70/Example.png" []
let private missingPicture = Image.fromUrl "https://upload.wikimedia.org/wikipedia/commons/0/00/Scribble-no-such-picture.png" []

/// Not a PNG past its first bytes: decoding fails, and the error goes to App.onError (a toast here).
let private brokenBytes = [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy; 0uy; 0uy; 0uy; 13uy |]

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
                section "From a URL" "Downloaded when shown, at their own size once they arrive (0 × 0 until then). The second gives 404: one toast. Needs the internet; nothing is asked for until you press the button." (
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
            ]
        ])

let view () =
    page "Images" "ImageView shows an image: fitted into its box, smooth or pixelated, rounded, at any density." [ images () ]
