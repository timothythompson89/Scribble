/// The default Button's looks for each input state: normal, pointer over, pressed, disabled.
module Scribble.Tests.Rendering.ButtonTests

open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.Rendering.TextRenderingTests

type ButtonTests(fixture: TextFixture) =
    interface IClassFixture<TextFixture>

    [<Fact>]
    member _.``buttons in each state``() =
        let view =
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 8.0
                StackPanel.margin 16.0
                StackPanel.children [
                    for label in [ "Normal"; "Hover"; "Pressed" ] do
                        Button.create [ Button.text label ]
                    Button.create [ Button.text "Disabled"; Button.isEnabled false ]
                ]
            ]
        let tree = ElementTree(fixture.Text)
        tree.Update view
        let buttons = tree.Root.Value.Children
        // What the input router does for a pointer over (and pressing) these two.
        buttons[1].ChangePointersOver 1
        buttons[2].ChangePointersOver 1
        buttons[2].ChangePointersPressing 1
        tree.UpdateLayout(Size.Create(400.0, 70.0))
        let size: PixelSize = { Width = 400; Height = 70 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
        // The four looks differ from each other.
        // A pixel in each button's left padding, halfway down (no text, no rounded corner).
        let at (e: Element) =
            let p = e.PositionInTree
            image.GetPixel(int p.X + 6, int (p.Y + e.Bounds.Height / 2.0))
        let colors = [ for b in buttons -> at b ]
        Assert.Equal(4, List.distinct colors |> List.length)
        match Snapshot.check "buttons@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()

    [<Fact>]
    member _.``a focused button shows the ring only when focus is visible``() =
        let view =
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 16.0
                StackPanel.margin 16.0
                StackPanel.children [
                    Button.create [ Button.text "Focused (ring)" ]
                    Button.create [ Button.text "Focused (press)" ]
                    Button.create [ Button.text "Square"; Button.cornerRadius 0.0 ]
                ]
            ]
        let tree = ElementTree(fixture.Text)
        tree.Update view
        let buttons = tree.Root.Value.Children
        // What the focus manager does after Tab, and after a press.
        buttons[0].SetFocusState(true, true)
        buttons[1].SetFocusState(true, false)
        buttons[2].SetFocusState(true, true)
        tree.UpdateLayout(Size.Create(420.0, 70.0))
        let size: PixelSize = { Width = 420; Height = 70 }
        let image =
            fixture.Gpu.Device.RenderOffscreen(size, 1.0, fun f ->
                f.Clear(Color.Hex 0x181825)
                tree.Render f.DrawList)
        // Halfway down, 3 DIPs left of each button: inside the ring (2–4 DIPs out) or background.
        let ringAt (e: Element) =
            let p = e.PositionInTree
            image.GetPixel(int p.X - 3, int (p.Y + e.Bounds.Height / 2.0))
        // "Clearly different": the ring is bright; the shadow only darkens the background a shade.
        let background = image.GetPixel(2, 2)
        let ringed (r: byte, g: byte, b: byte, _: byte) =
            let r0, g0, b0, _ = background
            let d (x: byte) (y: byte) = abs (int x - int y)
            max (d r r0) (max (d g g0) (d b b0)) > 40
        Assert.True(ringed (ringAt buttons[0]))
        Assert.False(ringed (ringAt buttons[1]))
        Assert.True(ringed (ringAt buttons[2]))
        match Snapshot.check "button-focus@1x" image with
        | Some error -> Assert.Fail error
        | None -> ()
