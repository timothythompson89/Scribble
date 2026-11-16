/// The sample dashboard, built from elements, against the hand-drawn version it replaced.
module Scribble.Tests.Rendering.DashboardTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Samples.Dashboard

module HelloApp = Scribble.Samples.HelloApp

let private allCards = HelloApp.allCards
open Scribble.Tests.Rendering.TextRenderingTests

/// The Milestone 2b dashboard drawn by hand with the draw list. Positions are snapped to whole
/// pixels exactly where layout rounding snaps them, so the two versions should match.
let handDrawn (text: TextSystem) (d: DrawList) (scale: float) (width: float) (height: float) (animationSeconds: float) =
    let snap (v: float) = Math.Round(v * scale, MidpointRounding.AwayFromZero) / scale
    let ceil (v: float) = Math.Ceiling(v * scale - 1e-6) / scale
    let regular, semi = Fonts.Regular(), Fonts.SemiBold()
    d.FillRect(Rect.Create(0.0, 0.0, width, 56.0), topBarFill)
    d.FillRect(Rect.Create(0.0, 56.0, width, 1.0), cardBorder)
    for i, c in List.indexed [ 0xF38BA8; 0xF9E2AF; 0xA6E3A1 ] do
        d.FillRoundedRect(Rect.Create(24.0 + float i * 22.0, 22.0, 12.0, 12.0), CornerRadius.Uniform 6.0, Color.Hex c)
    d.DrawText(text, "Scribble", semi, 17.0, 106.0, 17.0, textPrimary)
    let titleWidth = ceil (text.Measure("Scribble", semi, 17.0).Width)
    d.DrawText(text, "Milestone 4 · views and the differ ✨", regular, 13.0, 106.0 + titleWidth + 12.0, 21.0, textSecondary)

    let margin, gap, cardHeight, minWidth = 24.0, 20.0, 136.0, 240.0
    let columns = max 1 (int ((width - 2.0 * margin + gap) / (minWidth + gap)))
    let cardWidth = (width - 2.0 * margin - float (columns - 1) * gap) / float columns
    let radius = CornerRadius.Uniform 12.0
    for i in 0 .. cards.Length - 1 do
        let x0 = margin + float (i % columns) * (cardWidth + gap)
        let y0 = 80.0 + float (i / columns) * (cardHeight + gap)
        let card = Rect.FromEdges(snap x0, snap y0, snap (x0 + cardWidth), snap (y0 + cardHeight))
        if card.Top < height then
            let w = card.Width
            let accent = accentColor i
            d.DrawShadow(card, radius, 16.0, Color.Rgba(0.0f, 0.0f, 0.0f, 0.5f), offsetY = 4.0)
            d.DrawBox(card, radius, cardFill, 1.0, cardBorder)
            d.PushClip card
            d.PushTranslate(card.X, card.Y)
            d.PushClip(Rect.Create(0.0, 0.0, w, 6.0))
            d.FillRoundedRect(Rect.Create(0.0, 0.0, w, cardHeight), radius, accent)
            d.PopClip()
            let title, subtitle, avatar = cards[i]
            d.FillRoundedRect(Rect.Create(20.0, 26.0, 36.0, 36.0), CornerRadius.Uniform 18.0, accent.WithAlpha 0.25f)
            let am = text.Measure(avatar, regular, 18.0)
            d.DrawText(text, avatar, regular, 18.0, 20.0 + snap ((36.0 - ceil am.Width) / 2.0), 26.0 + snap ((36.0 - ceil am.Height) / 2.0), textPrimary)
            d.DrawText(text, title, semi, 15.0, 68.0, 25.0, textPrimary)
            d.DrawText(text, subtitle, regular, 12.5, 68.0, 46.0, textSecondary)
            let progress = progressAt i animationSeconds
            d.DrawText(text, "Progress", regular, 12.0, 20.0, 82.0, textSecondary)
            let percent = $"{progress * 100.0:F0}" + "%"
            let pw = ceil (text.Measure(percent, semi, 12.0).Width)
            d.DrawText(text, percent, semi, 12.0, snap (w - 20.0 - pw), 82.0, textPrimary)
            let track = Rect.Create(20.0, 104.0, w - 40.0, 8.0)
            d.FillRoundedRect(track, CornerRadius.Uniform 4.0, trackFill)
            d.FillRoundedRect(Rect.Create(track.X, track.Y, track.Width * progress, track.Height), CornerRadius.Uniform 4.0, accent, Precise)
            d.PopTransform()
            d.PopClip()

type DashboardTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Render(scale: float, width: float, height: float, draw: IFrame -> unit) =
        let size: PixelSize = { Width = int (width * scale); Height = int (height * scale) }
        fixture.Gpu.Device.RenderOffscreen(size, scale, fun f ->
            f.Clear background
            draw f)

    [<Theory>]
    [<InlineData(1.0)>]
    [<InlineData(2.0)>]
    member this.``the element dashboard looks like the hand-drawn one``(scale: float) =
        let width, height = 760.0, 520.0
        let tree = ElementTree(fixture.Text, Scale = scale)
        tree.Update(view 0.0 None allCards None ignore)
        tree.UpdateLayout(Size.Create(width, height))
        let elements = this.Render(scale, width, height, fun f -> tree.Render f.DrawList)
        let byHand = this.Render(scale, width, height, fun f -> handDrawn fixture.Text f.DrawList scale width height 0.0)
        let result = Snapshot.compare byHand elements
        Console.WriteLine($"dashboard@{scale}x: {result.DifferentPixels} of {result.TotalPixels} pixels differ from the hand-drawn version (max channel difference {result.MaxDifference})")
        if not result.Passes then
            Png.save (IO.Path.Combine(Snapshot.outputDir (), $"dashboard-hand@{scale}x.png")) byHand
            Png.save (IO.Path.Combine(Snapshot.outputDir (), $"dashboard-elements@{scale}x.png")) elements
            Assert.Fail $"{result.DifferentPixels} of {result.TotalPixels} pixels differ (max {result.MaxDifference})"
        match Snapshot.check $"dashboard@{scale}x" elements with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``hovering a card patches its look but not the layout``() =
        let tree = ElementTree(fixture.Text)
        let size = Size.Create(760.0, 520.0)
        tree.Update(view 0.0 None allCards None ignore)
        tree.UpdateLayout size
        let scroller = tree.Root.Value.Children[2]
        let grid = scroller.Children[0]
        let card = grid.Children[3]
        let content = card.Children[0]
        let before, contentBefore = card.Bounds, content.Bounds
        // Somewhere inside card 3, in tree coordinates.
        let hit = tree.HitTest(scroller.Bounds.X + grid.Bounds.X + card.Bounds.X + 50.0, scroller.Bounds.Y + grid.Bounds.Y + card.Bounds.Y + 50.0)
        Assert.Equal(Some 3, cardAt hit)
        tree.Update(view 0.0 (cardAt hit) allCards None ignore)
        tree.UpdateLayout size
        // Patched, not rebuilt: the same elements, a thicker border, the same layout.
        Assert.Same(card, grid.Children[3])
        Assert.Equal(2.0, card.Get BorderProps.borderThickness)
        Assert.Equal(1.0, grid.Children[0].Get BorderProps.borderThickness)
        Assert.Equal(before, card.Bounds)
        Assert.Equal(contentBefore, content.Bounds)
        tree.Update(view 0.0 None allCards None ignore)
        Assert.Equal(1.0, card.Get BorderProps.borderThickness)

    [<Fact>]
    member _.``shuffled and removed cards keep their elements (keys)``() =
        let tree = ElementTree(fixture.Text)
        let model = HelloApp.init
        tree.Update(HelloApp.view model ignore)
        let grid () = tree.Root.Value.Children[2].Children[0] // inside the ScrollViewer
        let byName () = dict [ for c in grid().Children -> c.Name, c ]
        let before = byName ()
        // Shuffle: same cards, new order, each still the same element.
        let shuffled = HelloApp.update HelloApp.Shuffle model
        Assert.NotEqual<int list>(model.Cards, shuffled.Cards)
        tree.Update(HelloApp.view shuffled ignore)
        let after = byName ()
        for name in before.Keys do
            Assert.Same(before[name], after[name])
        Assert.Equal<string list>([ for i in shuffled.Cards -> $"card {i}" ], [ for c in grid().Children -> c.Name ])
        // Remove one: the rest stay the same elements.
        let removed = HelloApp.update HelloApp.RemoveCard shuffled
        tree.Update(HelloApp.view removed ignore)
        Assert.Equal(7, grid().Children.Count)
        for c in grid().Children do
            Assert.Same(before[c.Name], c)

    [<Fact>]
    member _.``the toolbar's messages update the model``() =
        let m = HelloApp.init
        Assert.False (HelloApp.update HelloApp.TogglePause m).Animating
        Assert.Equal(0.0, (HelloApp.update HelloApp.Restart { m with Seconds = 5.0 }).Seconds)
        // Remove down to one card, never zero; add brings back the missing ones in order.
        let one = List.fold (fun model _ -> HelloApp.update HelloApp.RemoveCard model) m [ 1..20 ]
        Assert.Equal<int list>([ 0 ], one.Cards)
        let two = HelloApp.update HelloApp.AddCard one
        Assert.Equal<int list>([ 0; 1 ], two.Cards)
        let full = List.fold (fun model _ -> HelloApp.update HelloApp.AddCard model) m [ 1..20 ]
        Assert.Equal(8, full.Cards.Length)
        // Paused: ticks don't change the model (so no rebuild, no frame).
        let paused = HelloApp.update HelloApp.TogglePause m
        Assert.Equal(paused, HelloApp.update (HelloApp.Tick 0.016) paused)

    [<Fact>]
    member this.``the Hello screen with its toolbar``() =
        let tree = ElementTree(fixture.Text)
        tree.Update(HelloApp.view HelloApp.init ignore)
        tree.UpdateLayout(Size.Create(960.0, 600.0))
        let image = this.Render(1.0, 960.0, 600.0, fun f -> tree.Render f.DrawList)
        match Snapshot.check "hello@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
