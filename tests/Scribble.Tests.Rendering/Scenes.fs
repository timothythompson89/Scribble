/// Scenes shared by the rendering tests (and handy for eyeballing output).
module Scribble.Tests.Rendering.Scenes

open Scribble.Rendering
open Scribble.Text

let background = Color.Hex 0x1E1E2E

/// A 400×300 DIP scene that exercises every shape type: shadow, box with border, pill,
/// border-only rect, per-corner radii, and a clip.
let shapes (frame: IFrame) =
    let d = frame.DrawList
    frame.Clear background

    // A card: soft shadow, white fill, 1 DIP border.
    let card = Rect.Create(40.0, 40.0, 200.0, 120.0)
    d.DrawShadow(card, CornerRadius.Uniform 12.0, 24.0, Color.Rgba(0.0f, 0.0f, 0.0f, 0.45f), offsetY = 6.0)
    d.DrawBox(card, CornerRadius.Uniform 12.0, Color.White, 1.0, Color.Hex 0xC8C8D4)

    // A pill.
    d.FillRoundedRect(Rect.Create(270.0, 50.0, 100.0, 32.0), CornerRadius.Uniform 16.0, Color.Hex 0x4F7CFF)

    // A 2 DIP square border.
    d.StrokeRect(Rect.Create(270.0, 110.0, 100.0, 60.0), CornerRadius.Zero, 2.0, Color.Hex 0xFF8A3D)

    // Per-corner radii.
    let leaf = { TopLeft = 0.0; TopRight = 24.0; BottomRight = 0.0; BottomLeft = 24.0 }
    d.FillRoundedRect(Rect.Create(270.0, 190.0, 100.0, 80.0), leaf, Color.Hex 0x3DDC97)

    // Stripes clipped to a box, with the box outlined.
    let clipBox = Rect.Create(40.0, 190.0, 200.0, 80.0)
    d.PushClip clipBox
    for i in 0..9 do
        d.FillRect(Rect.Create(20.0 + float i * 28.0, 170.0, 14.0, 120.0), Color.Hsv(float i * 36.0, 0.6, 0.95))
    d.PopClip()
    d.StrokeRect(clipBox, CornerRadius.Zero, 1.0, Color.White)

/// A 520×260 DIP scene of text: heading, kerning and accented/Cyrillic text, a measured box,
/// two lines, light-on-dark text and clipped text.
let text (system: TextSystem) (frame: IFrame) =
    let d = frame.DrawList
    let regular, semi = Fonts.Regular(), Fonts.SemiBold()
    frame.Clear background
    d.DrawBox(Rect.Create(16.0, 16.0, 488.0, 228.0), CornerRadius.Uniform 12.0, Color.White, 1.0, Color.Hex 0xC8C8D4)
    d.DrawText(system, "Scribble draws text", semi, 28.0, 32.0, 28.0, Color.Hex 0x11111B)
    d.DrawText(system, "Kerning: AVA To. Wave · Ünïcödé €→ · Привет мир", regular, 14.0, 32.0, 76.0, Color.Hex 0x44445A)
    let m = system.Measure("Measured width", regular, 16.0)
    d.FillRect(Rect.Create(32.0, 110.0, m.Width, m.Height), Color.Hex 0xDCE6FF)
    d.DrawText(system, "Measured width", regular, 16.0, 32.0, 110.0, Color.Hex 0x1E40AF)
    d.DrawText(system, "Two lines\nof text", regular, 13.0, 32.0, 150.0, Color.Hex 0x44445A)
    d.FillRoundedRect(Rect.Create(300.0, 150.0, 180.0, 40.0), CornerRadius.Uniform 20.0, Color.Hex 0x4F7CFF)
    d.DrawText(system, "Light on dark", semi, 15.0, 336.0, 160.0, Color.White)
    d.PushClip(Rect.Create(32.0, 205.0, 120.0, 20.0))
    d.DrawText(system, "Clipped text runs past its box", regular, 13.0, 32.0, 207.0, Color.Hex 0xB91C1C)
    d.PopClip()

/// A 560×250 DIP scene of emoji and fallback: color emoji in headings and body text, skin tones,
/// a ZWJ family, flags, keycaps, text vs emoji presentation, faded emoji, and missing characters.
let emoji (system: TextSystem) (frame: IFrame) =
    let d = frame.DrawList
    let regular, semi = Fonts.Regular(), Fonts.SemiBold()
    frame.Clear background
    d.DrawBox(Rect.Create(16.0, 16.0, 528.0, 218.0), CornerRadius.Uniform 12.0, Color.White, 1.0, Color.Hex 0xC8C8D4)
    d.DrawText(system, "Ship it 🚀 Scribble has emoji 🎉", semi, 24.0, 32.0, 28.0, Color.Hex 0x11111B)
    d.DrawText(system, "Skin tones 👍 👍🏻 👍🏽 👍🏿 · family 👨‍👩‍👧 · flags 🇺🇸 🇯🇵 🇩🇪", regular, 16.0, 32.0, 74.0, Color.Hex 0x44445A)
    d.DrawText(system, "Text vs emoji: ❤ ❤️ ✔ ✔️ · keycaps 1️⃣ #️⃣ · ☕ ⚡ 🌍", regular, 16.0, 32.0, 108.0, Color.Hex 0x44445A)
    d.DrawText(system, "Faded emoji 😀 via alpha", regular, 16.0, 32.0, 142.0, Color.Rgba(0.27f, 0.27f, 0.35f, 0.4f))
    d.DrawText(system, "Missing in both fonts: 字 ⟶ tofu", regular, 16.0, 32.0, 176.0, Color.Hex 0x44445A)
    d.DrawText(system, "Big 🦄", semi, 30.0, 400.0, 160.0, Color.Hex 0x11111B)
