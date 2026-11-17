/// Scrolling the Counter's 10,000 messages up and down hard while drawing on the GPU. Tim saw
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
open Scribble.Samples.CounterDemo

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
        tree.Theme <- ValueSome Common.darkTheme
        tree.Update(Scribble.Samples.Counter.demo ())
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
        click "Layers"
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
