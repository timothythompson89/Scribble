/// M6 step 1 on the GPU: a scrolled ScrollViewer (clipped content, scrollbar on top) and a layer
/// drawn over the app.
module Scribble.Tests.Rendering.LayerTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests

let private dim = Color.Hex 0x9A9AB0

/// The app: a heading and a 120-high ScrollViewer over twelve rows.
let private appView =
    StackPanel.create [
        StackPanel.margin 16.0
        StackPanel.spacing 8.0
        StackPanel.children [
            TextBlock.create [ TextBlock.text "Twelve rows, scrolled"; TextBlock.foreground dim ]
            ScrollViewer.create [
                ScrollViewer.width 200.0
                ScrollViewer.height 120.0
                ScrollViewer.horizontalAlignment HorizontalAlignment.Left
                ScrollViewer.content (
                    StackPanel.create [
                        StackPanel.children [
                            for i in 1..12 ->
                                Border.create [
                                    Border.height 28.0
                                    Border.padding (8.0, 0.0)
                                    Border.background (if i % 2 = 0 then Color.Hex 0x24243A else Color.Hex 0x2E2E48)
                                    Border.child (
                                        TextBlock.create [
                                            TextBlock.text $"Row {i}"
                                            TextBlock.foreground Color.White
                                            TextBlock.verticalAlignment VerticalAlignment.Center
                                        ]
                                    )
                                ]
                        ]
                    ]
                )
            ]
        ]
    ]

/// A toast in a layer, at the bottom right, overlapping the app.
let private toast =
    Border.create [
        Border.horizontalAlignment HorizontalAlignment.Right
        Border.verticalAlignment VerticalAlignment.Bottom
        Border.margin 16.0
        Border.padding (14.0, 10.0)
        Border.cornerRadius 8.0
        Border.background (Color.Hex 0x3D59A1)
        Border.shadow { Blur = 12.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.5f }
        Border.child (TextBlock.create [ TextBlock.text "Saved"; TextBlock.foreground Color.White ])
    ]

type LayerTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    [<Fact>]
    member _.``a scrolled ScrollViewer with a layer on top``() =
        let size = Size.Create(300.0, 200.0)
        let app = ElementTree(fixture.Text)
        app.Update appView
        app.UpdateLayout size
        // Scroll the way the wheel does: 1.5 notches towards the user (72 DIPs).
        let input = InputRouter(app, ignore)
        input.Wheel(Point(40.0, 80.0), 0.0, -1.5, Modifiers.None)
        app.UpdateLayout size
        let layer = ElementTree(fixture.Text)
        layer.Update toast
        layer.UpdateLayout size
        let pixels: PixelSize = { Width = 300; Height = 200 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                app.Render f.DrawList
                layer.Render f.DrawList)
        match Snapshot.check "layers-scroll@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
