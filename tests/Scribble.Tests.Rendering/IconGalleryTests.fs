/// The Icons sample: all 4,304 Material Symbols in a ScrollViewer. Its top as a picture, and how
/// long building it, switching Outlined/Filled, picking an icon and drawing a frame take (steady
/// state: warmed up, best of 5).
module Scribble.Tests.Rendering.IconGalleryTests

open System
open System.Diagnostics
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let rec private texts (e: Element) : string list =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then [ e.Get TextProps.text ] else e.Children |> Seq.collect texts |> List.ofSeq

let private click (e: Element) = (e.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))

let private width, height = 920.0, 720.0

type IconGalleryTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Build() =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome(Theme.dark [])
        tree.Update(Scribble.Samples.IconGallery.gallery ())
        tree.UpdateLayout(Size.Create(width, height))
        tree

    member private _.Frame(tree: ElementTree) =
        let size: PixelSize = { Width = int width; Height = int height }
        fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
            f.Clear(Color.Hex 0x181825)
            tree.Render f.DrawList)

    [<Fact>]
    member this.``the gallery shows every icon, Filled switches them, and a click shows the F# name``() =
        let tree = this.Build()
        let find name = all tree.Root.Value |> Seq.find (fun e -> e.Name = name)
        // An ItemsView: all 4,304 icons, but only the tiles on screen (and a little more) exist.
        let struct (realized, total) = (ItemsView.realizedCount (find "icons")).Value
        Assert.Equal(4304, total)
        Assert.InRange(realized, 40, 120)
        Assert.Equal(realized, (find "icons").Children.Count)
        let button text = all tree.Root.Value |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && texts e = [ text ])
        // The first tile is Google's first icon: 10k.
        let first () = (find "icons").Children[0]
        click (first ())
        tree.UpdateLayout(Size.Create(width, height))
        Assert.Equal("Material._10k", (find "code").Get TextProps.text)
        click (button "Filled")
        tree.UpdateLayout(Size.Create(width, height))
        Assert.Equal("Material._10k.filled", (find "code").Get TextProps.text)
        let icon = (first ()).Children[0].Children[0].Get IconViewProps.icon
        Assert.Equal("10k-filled", icon.Value.Name)
        click (button "Outlined")
        tree.UpdateLayout(Size.Create(width, height))
        match Snapshot.check "icon-gallery@1x" (this.Frame tree) with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``timings at steady state``() =
        let best (n: int) (f: unit -> unit) =
            f ()
            [ for _ in 1..n -> (let sw = Stopwatch.StartNew() in f (); sw.Elapsed.TotalMilliseconds) ] |> List.min
        let build = best 5 (fun () -> this.Build() |> ignore)
        let tree = this.Build()
        let button text = all tree.Root.Value |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && texts e = [ text ])
        let mutable filled = false
        let switch =
            best 5 (fun () ->
                filled <- not filled
                click (button (if filled then "Filled" else "Outlined"))
                tree.UpdateLayout(Size.Create(width, height)))
        let tiles = all tree.Root.Value |> Seq.find (fun e -> e.Name = "icons")
        let mutable i = 0
        let pick =
            best 5 (fun () ->
                i <- i + 1
                click tiles.Children[i]
                tree.UpdateLayout(Size.Create(width, height)))
        let draw = best 5 (fun () -> tree.Render(DrawList 1.0))
        let resize =
            best 5 (fun () ->
                tree.UpdateLayout(Size.Create(width - 120.0, height))
                tree.UpdateLayout(Size.Create(width, height)))
        let elements = all tree.Root.Value |> Seq.length
        Console.WriteLine(
            $"icon gallery: {elements} elements; build {build:F1} ms, Outlined/Filled {switch:F1} ms, pick {pick:F1} ms, "
            + $"draw list {draw:F2} ms, two re-layouts (narrower, back) {resize:F1} ms")

    [<Fact>]
    member this.``scroll step and startup breakdown``() =
        let best (n: int) (f: unit -> unit) =
            f ()
            [ for _ in 1..n -> (let sw = Stopwatch.StartNew() in f (); sw.Elapsed.TotalMilliseconds) ] |> List.min
        let tree = this.Build()
        let scroller = all tree.Root.Value |> Seq.find (fun e -> e.Name = "icon scroller")
        let mutable offset = 0.0
        let scrollLayout =
            best 20 (fun () ->
                offset <- offset + 50.0
                ScrollViewer.jumpTo scroller offset
                tree.UpdateLayout(Size.Create(width, height)))
        let render = best 20 (fun () -> tree.Render(DrawList 1.0))
        // Startup parts: the icon values, the view, realizing it, the first measure (text layout).
        let names = Scribble.Icons.Material.names () |> Array.ofSeq
        let icons = best 3 (fun () -> for n in names do Scribble.Icons.Material.icon n |> ignore)
        let realize =
            best 3 (fun () ->
                let t = ElementTree(fixture.Text)
                t.Theme <- ValueSome(Theme.dark [])
                t.Update(Scribble.Samples.IconGallery.gallery ()))
        let texts = best 3 (fun () -> for n in names do fixture.Text.Measure(n, Scribble.Text.Fonts.Regular(), 11.0) |> ignore)
        Console.WriteLine(
            $"scroll step: layout {scrollLayout:F2} ms, draw list {render:F2} ms; startup parts: 4,304 icons {icons:F1} ms, "
            + $"realize {realize:F1} ms, measuring 4,304 names {texts:F1} ms (all cached after the first time)")
