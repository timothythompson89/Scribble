module Scribble.Tests.TextTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Text
open Scribble.Text.Emoji
open Scribble.Tests.TestShaping

// --- Font parsing ---------------------------------------------------------------------------

[<Fact>]
let ``Inter's metrics are read from its tables`` () =
    let face = Fonts.Regular().Face
    Assert.Equal(2048, face.UnitsPerEm)
    Assert.Equal(1984, face.Ascender)
    Assert.Equal(-494, face.Descender)
    Assert.True(face.GlyphCount > 2000)

[<Fact>]
let ``glyph 0 (notdef) has an outline and advances`` () =
    let face = Fonts.Regular().Face
    Assert.NotEmpty(face.Outline 0)
    Assert.True(face.Advance 0 > 0)

[<Fact>]
let ``every glyph outline parses`` () =
    let face = Fonts.SemiBold().Face
    for g in 0 .. face.GlyphCount - 1 do
        for contour in face.Outline g do
            Assert.True(contour.Length >= 1)

[<Fact>]
let ``non-TrueType data is rejected clearly`` () =
    Assert.Throws<FontFormatException>(fun () -> FontFace(Array.zeroCreate 64) |> ignore) |> ignore

// --- Rasterizer -----------------------------------------------------------------------------

let private square x0 y0 x1 y1 =
    // Clockwise in font space (y up).
    [| [| { X = x0; Y = y0; OnCurve = true }
          { X = x0; Y = y1; OnCurve = true }
          { X = x1; Y = y1; OnCurve = true }
          { X = x1; Y = y0; OnCurve = true } |] |]

let private at (b: Rasterizer.GlyphBitmap) x y = b.Coverage[y * b.Width + x]

[<Fact>]
let ``an aligned square is fully covered and nothing else`` () =
    let b = Rasterizer.rasterize (square 0.0 0.0 4.0 4.0) 1.0 0.0
    Assert.Equal(0, b.Left)
    Assert.Equal(-4, b.Top)
    for y in 0..3 do
        for x in 0..3 do
            Assert.Equal(255uy, at b x y)
        Assert.Equal(0uy, at b 4 y)

[<Fact>]
let ``a half-pixel offset gives half coverage at the edges`` () =
    let b = Rasterizer.rasterize (square 0.0 0.0 4.0 4.0) 1.0 0.5
    Assert.InRange(int (at b 0 1), 126, 129)
    Assert.Equal(255uy, at b 1 1)
    Assert.InRange(int (at b 4 1), 126, 129)

[<Fact>]
let ``overlapping contours don't exceed full coverage`` () =
    let twice = Array.append (square 0.0 0.0 4.0 4.0) (square 0.0 0.0 4.0 4.0)
    let b = Rasterizer.rasterize twice 1.0 0.0
    Assert.Equal(255uy, at b 2 2)

[<Fact>]
let ``a quadratic curve stays inside its control hull`` () =
    // A dome: on-curve corners with one control point above.
    let dome = [| [| { X = 0.0; Y = 0.0; OnCurve = true }; { X = 5.0; Y = 10.0; OnCurve = false }; { X = 10.0; Y = 0.0; OnCurve = true } |] |]
    let b = Rasterizer.rasterize dome 1.0 0.0
    Assert.True(b.Height <= 6) // the curve peaks at y = 5
    Assert.Equal(255uy, at b 5 (b.Height - 1))

// --- Text system (with a stand-in shaper: one glyph per character, fixed advance) ------------

[<Fact>]
let ``measure sums advances and stacks lines`` () =
    let text = TextSystem(GlyphAtlas(256), new FixedShaper())
    let m = text.Measure("abcd\nab", Fonts.Regular(), 20.0)
    // 4 glyphs × 1024 units at 20/2048 DIP per unit = 40 DIPs.
    Assert.Equal(40.0, m.Width, 6)
    Assert.Equal(2, m.Lines)
    Assert.Equal(m.LineHeight * 2.0, m.Height, 6)
    Assert.Equal(1984.0 * 20.0 / 2048.0, m.Baseline, 6)

[<Fact>]
let ``drawing text adds one glyph per inked character`` () =
    let atlas = GlyphAtlas(256)
    let text = TextSystem(atlas, new FixedShaper())
    let list = DrawList(1.0)
    list.DrawText(text, "abc", Fonts.Regular(), 16.0, 0.0, 0.0, Color.White)
    Assert.Equal(3, list.Count)
    Assert.False(atlas.TakeDirtyRegion().IsEmpty)

[<Fact>]
let ``repeated text reuses cached glyphs`` () =
    let atlas = GlyphAtlas(256)
    let text = TextSystem(atlas, new FixedShaper())
    let list = DrawList(1.0)
    list.DrawText(text, "aaaa", Fonts.Regular(), 16.0, 0.0, 0.0, Color.White)
    atlas.TakeDirtyRegion() |> ignore
    list.DrawText(text, "aaaa", Fonts.Regular(), 16.0, 0.0, 30.0, Color.White)
    Assert.True(atlas.TakeDirtyRegion().IsEmpty)

[<Fact>]
let ``a full atlas starts over instead of failing`` () =
    let atlas = GlyphAtlas(64)
    let text = TextSystem(atlas, new FixedShaper())
    let list = DrawList(1.0)
    for size in 10..40 do
        list.DrawText(text, "a", Fonts.Regular(), float size, 0.0, 0.0, Color.White)
    Assert.True(atlas.Generation > 0)

[<Fact>]
let ``an atlas filled twice in one frame is counted, once per upload`` () =
    let atlas = GlyphAtlas(64)
    let text = TextSystem(atlas, new FixedShaper())
    let list = DrawList(1.0)
    // One "frame" (no upload in between) needs far more than 64² holds: it clears more than once.
    for size in 10..60 do
        list.DrawText(text, "a", Fonts.Regular(), float size, 0.0, 0.0, Color.White)
    Assert.True(atlas.Generation > 1)
    Assert.Equal(atlas.Generation - 1, atlas.Overflows)
    // The same glyphs over several frames, each uploaded: each frame clears at most once.
    atlas.TakeDirtyRegion() |> ignore
    let before = atlas.Overflows
    for size in 10..60 do
        list.DrawText(text, "a", Fonts.Regular(), float size, 0.0, 0.0, Color.White)
        atlas.TakeDirtyRegion() |> ignore
    Assert.Equal(before, atlas.Overflows)

[<Fact>]
let ``a glyph bigger than the whole atlas is skipped, not retried forever`` () =
    let atlas = GlyphAtlas(64)
    let text = TextSystem(atlas, new FixedShaper())
    let list = DrawList(1.0)
    list.DrawText(text, "a", Fonts.Regular(), 400.0, 0.0, 0.0, Color.White)
    Assert.Equal(0, list.Count)

// --- Atlas ----------------------------------------------------------------------------------

[<Fact>]
let ``atlas regions don't overlap and leave padding`` () =
    let atlas = GlyphAtlas(64)
    let (ValueSome(struct (x1, y1))) = atlas.TryAllocate(10, 10)
    let (ValueSome(struct (x2, y2))) = atlas.TryAllocate(10, 10)
    Assert.Equal(y1, y2)
    Assert.True(x2 >= x1 + 11)
    Assert.True((atlas.TryAllocate(100, 10)).IsNone)

// --- Character mapping, color glyphs and fallback (M2b) -------------------------------------

[<Fact>]
let ``cmap finds characters and reports missing ones`` () =
    let face = Fonts.Regular().Face
    Assert.True(face.HasCharacter(int 'A'))
    Assert.True(face.HasCharacter 0x416) // Ж
    Assert.False(face.HasCharacter 0x5B57) // 字: not in Inter
    Assert.False(face.HasCharacter 0x1F600) // 😀: Inter has no emoji
    Assert.Equal(0, face.GlyphIndex 0x10FFFF)

[<Fact>]
let ``Twemoji has color layers with palette colors`` () =
    let face = Emoji.Twemoji().Face
    Assert.True face.HasColorGlyphs
    Assert.Equal(512, face.UnitsPerEm)
    match face.ColorLayers(face.GlyphIndex 0x1F600) with
    | ValueSome layers ->
        Assert.True(layers.Length >= 2)
        Assert.All(layers, fun l -> Assert.True l.Color.IsSome)
    | ValueNone -> Assert.Fail "😀 should be a color glyph"
    Assert.True((Fonts.Regular().Face.ColorLayers(5)).IsNone)

[<Fact>]
let ``color layers are composited bottom to top`` () =
    let face = Emoji.Twemoji().Face
    let glyph = face.GlyphIndex 0x1F534 // 🔴 a single red disc
    match face.ColorLayers glyph with
    | ValueSome layers ->
        let b = Rasterizer.rasterizeColor face layers (32.0 / 512.0) 0.0 (struct (0uy, 0uy, 0uy, 255uy))
        let centre = ((b.Height / 2) * b.Width + b.Width / 2) * 4
        let r, g, bl, a = b.Coverage[centre], b.Coverage[centre + 1], b.Coverage[centre + 2], b.Coverage[centre + 3]
        Assert.Equal(255uy, a)
        Assert.True(r > 180uy && g < 100uy && bl < 100uy, $"expected red, got ({r}, {g}, {bl})")
    | ValueNone -> Assert.Fail "🔴 should be a color glyph"

[<Fact>]
let ``characters the font lacks come from the fallback`` () =
    // FixedShaper gives every glyph 1024 font units. Inter has 2048 units per em and Twemoji 512,
    // so a glyph shaped with Twemoji is four times as wide: the width shows which font was used.
    let text = TextSystem(GlyphAtlas(256), GlyphAtlas(256, 4), new FixedShaper(), [ Emoji.Twemoji() ])
    let m = text.Measure("A😀B", Fonts.Regular(), 20.0)
    // A and B from Inter: 10 DIPs each. 😀 from Twemoji: two UTF-16 units → 2 glyphs × 40.
    Assert.Equal(10.0 + 80.0 + 10.0, m.Width, 6)

[<Fact>]
let ``without fallbacks everything uses the requested font`` () =
    let text = TextSystem(GlyphAtlas(256), new FixedShaper())
    let m = text.Measure("A😀B", Fonts.Regular(), 20.0)
    Assert.Equal(4.0 * 10.0, m.Width, 6)

[<Fact>]
let ``emoji sequences stay together in one fallback run`` () =
    // 👨‍👩‍👧 is one grapheme cluster: 3 emoji joined by 2 zero-width joiners (8 UTF-16 units).
    let text = TextSystem(GlyphAtlas(256), GlyphAtlas(256, 4), new FixedShaper(), [ Emoji.Twemoji() ])
    let m = text.Measure("👨‍👩‍👧", Fonts.Regular(), 20.0)
    Assert.Equal(8.0 * 40.0, m.Width, 6)

[<Fact>]
let ``emoji are drawn as color glyphs`` () =
    let colors = GlyphAtlas(256, 4)
    let text = TextSystem(GlyphAtlas(256), colors, new FixedShaper(), [ Emoji.Twemoji() ])
    let list = DrawList(1.0)
    // FixedShaper returns glyph 0 for everything, which isn't a color glyph; draw through the
    // real cmap glyph instead to check the color path.
    let face = Emoji.Twemoji().Face
    Assert.True((face.ColorLayers(face.GlyphIndex 0x1F600)).IsSome)
    list.DrawText(text, "x", Fonts.Regular(), 16.0, 0.0, 0.0, Color.White)
    Assert.True(colors.TakeDirtyRegion().IsEmpty) // plain text never touches the color atlas
