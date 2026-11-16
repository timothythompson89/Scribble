/// Icons on the GPU: shapes from path data (lines, arcs, cubics, a hole), sizes, rotation and
/// flips, the inherited text color, and buttons with icons; at 1x and 2x.
module Scribble.Tests.Rendering.IconRenderingTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests

let menu = Icon.fromPath "M3 18h18v-2H3zm0-5h18v-2H3zm0-7v2h18V6z" [ Icon.name "menu" ]
/// A ring: the inner circle runs the other way, so it stays empty.
let ring = Icon.fromPath "M12 2a10 10 0 1 0 0 20 10 10 0 1 0 0-20zm0 3a7 7 0 1 1 0 14 7 7 0 1 1 0-14z" [ Icon.name "ring" ]
let star = Icon.fromPath "M12 2l3.1 6.3 6.9 1-5 4.9 1.2 6.8L12 17.8 5.8 21l1.2-6.8-5-4.9 6.9-1z" [ Icon.name "star" ]
let heart =
    Icon.fromPath "M12 21s-7-4.4-9.3-8.6C1 9.2 2.7 5 6.6 5c2.2 0 3.6 1.2 5.4 3.2C13.8 6.2 15.2 5 17.4 5c3.9 0 5.6 4.2 3.9 7.4C19 16.6 12 21 12 21z" [ Icon.name "heart" ]
let arrow = Icon.fromPath "M4 11h12.2l-5.6-5.6L12 4l8 8-8 8-1.4-1.4 5.6-5.6H4z" [ Icon.name "arrow" ]
let square = Icon.fromPath "M0 0h24v24H0z" [ Icon.name "square" ]

let private red = Color.Hex 0xF7768E

let scene () =
    let row children =
        StackPanel.create [ StackPanel.orientation Horizontal; StackPanel.spacing 12.0; StackPanel.children children ]
    StackPanel.create [
        StackPanel.margin 16.0
        StackPanel.spacing 12.0
        StackPanel.children [
            row [
                for icon in [ menu; ring; star; heart; arrow ] do
                    IconView.create [ IconView.icon icon ]
                IconView.create [ IconView.icon heart; IconView.size 48.0 ]
                IconView.create [ IconView.icon star; IconView.size 12.0 ]
            ]
            row [
                IconView.create [ IconView.icon (arrow |> Icon.rotate 90.0) ]
                IconView.create [ IconView.icon (arrow |> Icon.rotate 45.0) ]
                IconView.create [ IconView.icon (arrow |> Icon.flipHorizontal) ]
                IconView.create [ IconView.icon (heart |> Icon.flipVertical) ]
                // The text color around it: set on the row, inherited by the icons.
                StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 12.0
                    StackPanel.foreground red
                    StackPanel.children [
                        IconView.create [ IconView.name "inherited"; IconView.icon square; IconView.size 16.0 ]
                        IconView.create [ IconView.icon star ]
                    ]
                ]
            ]
            row [
                Button.create [ Button.icon arrow; Button.text "Next" ]
                Button.create [ Button.icon menu ]
                Button.create [ Button.icon heart; Button.text "Disabled"; Button.isEnabled false ]
            ]
        ]
    ]

type IconRenderingTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    member private _.Render(scale: float) =
        let tree = ElementTree(fixture.Text)
        tree.Theme <- ValueSome(Theme.dark [])
        tree.Update(scene ())
        tree.UpdateLayout(Size.Create(420.0, 190.0))
        let size: PixelSize = { Width = int (420.0 * scale); Height = int (190.0 * scale) }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, scale, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
        tree, image

    [<Fact>]
    member this.``icons at 1x``() =
        let tree, image = this.Render 1.0
        // The inherited color fills the middle of the square icon.
        // Second row, the red StackPanel, its first icon.
        let square = tree.Root.Value.Children[1].Children[4].Children[0]
        let p = square.PositionInTree
        let (r, g, b, _) = image.GetPixel(int p.X + 8, int p.Y + 8)
        let near (x: byte) (y: int) = abs (int x - y) <= 2
        Assert.True(near r 0xF7 && near g 0x76 && near b 0x8E, $"{r} {g} {b}")
        match Snapshot.check "icons@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member this.``icons at 2x``() =
        let _, image = this.Render 2.0
        match Snapshot.check "icons@2x" image with
        | Some error -> Assert.Fail error
        | None -> ()
