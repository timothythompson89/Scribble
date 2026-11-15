module Scribble.Tests.ColorTests

open Xunit
open Scribble.Rendering

let private close (expected: float32) (actual: float32) = Assert.InRange(actual, expected - 0.001f, expected + 0.001f)

[<Fact>]
let ``hex parses channels`` () =
    let c = Color.Hex 0xFF8000
    close 1.0f c.R
    close (128.0f / 255.0f) c.G
    close 0.0f c.B
    close 1.0f c.A

[<Theory>]
[<InlineData(0.0, 1.0, 0.0, 0.0)>]
[<InlineData(120.0, 0.0, 1.0, 0.0)>]
[<InlineData(240.0, 0.0, 0.0, 1.0)>]
[<InlineData(360.0, 1.0, 0.0, 0.0)>]
[<InlineData(-120.0, 0.0, 0.0, 1.0)>]
let ``hsv primaries`` (hue: float, r: float, g: float, b: float) =
    let c = Color.Hsv(hue, 1.0, 1.0)
    close (float32 r) c.R
    close (float32 g) c.G
    close (float32 b) c.B

[<Fact>]
let ``hsv with zero saturation is grey`` () =
    let c = Color.Hsv(200.0, 0.0, 0.5)
    close 0.5f c.R
    close 0.5f c.G
    close 0.5f c.B
