/// Dashed and dotted borders (M8 step 2): square and rounded, thin and thick, at 1× and 2×, plus
/// dashed and dotted lines (a box no thicker than its border).
module Scribble.Tests.Rendering.DashTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Tests.Rendering.TextRenderingTests

let private draw (list: DrawList) =
    let ink = Color.Hex 0xE0E0F0
    let box x y w h r t style =
        list.DrawBox(Rect.Create(x, y, w, h), CornerRadius.Uniform r, Color.Hex 0x2A2A42, t, ink, style = style)
    let styles = [ StrokeStyle.Dashed(6.0, 4.0); StrokeStyle.Dashed(3.0, 2.0); StrokeStyle.Dotted ]
    styles
    |> List.iteri (fun row style ->
        let y = 12.0 + float row * 76.0
        box 12.0 y 90.0 60.0 0.0 1.0 style
        box 114.0 y 90.0 60.0 12.0 1.0 style
        box 216.0 y 90.0 60.0 30.0 2.0 style
        box 318.0 y 60.0 60.0 30.0 3.0 style
        box 390.0 y 120.0 60.0 8.0 4.0 style)
    // Lines: horizontal and vertical, as thin boxes.
    list.StrokeRect(Rect.Create(12.0, 246.0, 300.0, 1.0), CornerRadius.Zero, 1.0, ink, style = StrokeStyle.Dashed(4.0, 4.0))
    list.StrokeRect(Rect.Create(12.0, 258.0, 300.0, 2.0), CornerRadius.Zero, 2.0, ink, style = StrokeStyle.Dotted)
    list.StrokeRect(Rect.Create(330.0, 236.0, 1.0, 34.0), CornerRadius.Zero, 1.0, ink, style = StrokeStyle.Dashed(4.0, 3.0))

type DashTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    [<Theory>]
    [<InlineData(1.0)>]
    [<InlineData(2.0)>]
    member _.``dashed and dotted borders and lines``(scale: float) =
        let size: PixelSize = { Width = int (522.0 * scale); Height = int (282.0 * scale) }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, scale, fun f ->
                f.Clear(Color.Hex 0x181825)
                draw f.DrawList)
        match Snapshot.check ("dashes@" + string (int scale) + "x") image with
        | Some error -> Assert.Fail error
        | None -> ()
