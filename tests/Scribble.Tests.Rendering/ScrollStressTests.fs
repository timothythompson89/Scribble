/// Scrolling the Gallery's 10,000 messages (the Counter's, before the Gallery) up and down hard while drawing on the GPU. Tim saw
/// "Internal CLR error" doing that by hand (2026-11-16): shaping that much new text set off
/// background GCs, and one misread HarfBuzzShaper's `fixed` pin of the string (now a GCHandle).
/// Before the fix this crashed the test host about one run in five, so a pass here is evidence,
/// not proof.
module Scribble.Tests.Rendering.ScrollStressTests

open System
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests
open Scribble.Samples.Gallery

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let rec private firstText (e: Element) : string =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then e.Get TextProps.text
    else e.Children |> Seq.map firstText |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""

type ScrollStressTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    [<Fact>]
    member _.``scrolling 10,000 messages hard while drawing every frame``() =
        let width, height = 920.0, 720.0
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome Scribble.Samples.GalleryApp.darkTheme
        tree.Update(Scribble.Samples.GalleryApp.gallery ())
        let size = Size.Create(width, height)
        let layout () =
            tree.UpdateLayout size
            tree.RunAfterFrame()
        layout ()
        let click (text: string) =
            let button = all tree.Root.Value |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && firstText e = text)
            (button.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
            layout ()
        let pixels: PixelSize = { Width = int width; Height = int height }
        let frame () =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
            |> ignore
        click "Scrolling"
        for b in [ "Jump to #1"; "#5,000"; "#9,000"; "Random"; "Add message"; "Add message" ] do
            click b
            frame ()
        let list = all tree.Root.Value |> Seq.find (fun e -> e.Name = "messages")
        let viewer = list.Parent.Value
        let random = Random 1
        let mutable y = 0.0
        for i in 1..1500 do
            // A thumb dragged up and down fast: big jumps, sometimes small steps.
            y <- (if i % 7 = 0 then y + 13.0 else random.NextDouble() * 900_000.0)
            ScrollViewer.jumpTo viewer y
            layout ()
            if i % 3 = 0 then frame ()
        Assert.True(list.Children.Count < 200)

    /// M8 step 1b: rows of very different heights (one line to forty sentences of wrapping text),
    /// scrolled hard with thumb drags, wheel steps and jumps while rows are inserted, drawn on the
    /// GPU as it goes; at the end, the top is exactly row 0.
    [<Fact>]
    member _.``scrolling 5,000 rows of very different heights hard while drawing``() =
        let width, height = 600.0, 500.0
        let random = Random 5
        let sentence = "Rows of very different heights, scrolled hard. "
        let mutable rows = Array.init 5000 (fun i -> struct (i, String.replicate (if random.Next 50 = 0 then 40 else random.Next(1, 8)) sentence))
        let scroller = Scroller()
        let viewOf (rows: struct (int * string)[]) =
            ScrollViewer.create [
                ScrollViewer.content (
                    ItemsView.create [
                        ItemsView.source rows
                        ItemsView.keySelector (fun (struct (id, _): struct (int * string)) -> id)
                        ItemsView.viewSelector (fun (struct (id, text): struct (int * string)) ->
                            Border.create [
                                Border.name (string id)
                                Border.padding 8.0
                                Border.borderThickness 1.0
                                Border.borderBrush (Color.Hex 0x34345A)
                                Border.content (TextBlock.create [ TextBlock.text (string id + ". " + text); TextBlock.textWrapping Scribble.Text.Wrap ])
                            ])
                        ItemsView.scroller scroller
                    ])
            ]
        let tree = ElementTree(fixture.Text)
        tree.Update(viewOf rows)
        let size = Size.Create(width, height)
        let layout () = tree.UpdateLayout size
        layout ()
        let viewer = tree.Root.Value
        let list = viewer.Children[0]
        let pixels: PixelSize = { Width = int width; Height = int height }
        let frame () =
            fixture.Gpu.Device.RenderOffscreen(pixels, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
            |> ignore
        let mutable nextId = 100_000
        for i in 1..1200 do
            match random.Next 10 with
            | 0 | 1 | 2 -> ScrollViewer.jumpTo viewer (random.NextDouble() * list.Bounds.Height)
            | 3 -> scroller.ScrollToIndex(random.Next rows.Length, ScrollAlign.Start, false)
            | 4 ->
                let at = random.Next rows.Length
                rows <- Array.concat [ rows[.. at - 1]; [| struct (nextId, sentence) |]; rows[at ..] ]
                nextId <- nextId + 1
                tree.Update(viewOf rows)
            | _ -> scroller.ScrollBy(float (random.Next(-300, 301)), false)
            layout ()
            if i % 3 = 0 then frame ()
            Assert.True(list.Children.Count < 120, $"step {i}: {list.Children.Count} rows realized")
        scroller.ScrollToTop false
        layout ()
        frame ()
        let struct (_, y, _, _) = ScrollViewer.viewportOf viewer
        Assert.Equal(0.0, y)
        Assert.Equal(0.0, (list.Children |> Seq.find (fun c -> c.Name = "0")).PositionInTree.Y)
