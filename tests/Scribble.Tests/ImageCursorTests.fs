/// M8 step 3b (f): image cursors (`Cursor.fromImage`) and the inspector's IMAGES section, on the fake platform with
/// decoding inline (DESIGN.md, "M8 step 3b", (f)). The arrow-until-ready test is in ImageLoadingTests (it downloads).
module Scribble.Tests.ImageCursorTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Inspector
open Scribble.Tests.AppTests

let private unique (name: string) = name + "-" + Guid.NewGuid().ToString("N")
let private move (w: FakeWindow) x y = w.Send(PointerMoved(Mouse, 0, { X = x; Y = y }, Modifiers.None))

let private settle (w: FakeWindow) =
    let mutable n = 0
    while w.DeliverFrame() && n < 10 do
        n <- n + 1

/// An opaque picture from straight RGB colours, one row per string of colours.
let private picture (name: string) (rows: (byte * byte * byte) list list) =
    let h = rows.Length
    let w = rows.Head.Length
    let px = Array.zeroCreate<byte> (w * h * 4)
    rows |> List.iteri (fun y row ->
        row |> List.iteri (fun x (r, g, b) ->
            let i = (y * w + x) * 4
            px[i] <- r
            px[i + 1] <- g
            px[i + 2] <- b
            px[i + 3] <- 255uy))
    Image.fromPixels (unique name, w, h, px) []

let private red = (255uy, 0uy, 0uy)
let private blue = (0uy, 0uy, 255uy)

/// The app showing `view` in a 400 × 300 window, its errors collected; decoding inline on this thread.
let private show (view: View) (extra: AppSetting<unit> list) =
    let errors = List<exn>()
    let w, _, app = start ([ App.view view; App.onError (fun _ e -> errors.Add e) ] @ extra)
    settle w
    w, app :?> ElementHost, errors

/// A 200 × 100 box at the top left with `cursor`.
let private box (cursor: Cursor) =
    Border.create [
        Border.name "box"
        Border.width 200.0
        Border.height 100.0
        Border.horizontalAlignment HorizontalAlignment.Left
        Border.verticalAlignment VerticalAlignment.Top
        Border.cursor cursor
    ]

/// The window's cursor over the box, as a bitmap at `scale`.
let private bitmapOver (cursor: Cursor) (scale: float) =
    let w, _, errors = show (box cursor) []
    move w 50.0 50.0
    Assert.Empty errors
    match w.Cursor with
    | Cursor.Image image -> image.Render scale
    | other -> failwith ("not an image cursor: " + string other)

/// The pixels as BGRA quads, row by row.
let private quads (b: CursorBitmap) =
    [ for i in 0 .. b.Width * b.Height - 1 -> b.Pixels[i * 4], b.Pixels[i * 4 + 1], b.Pixels[i * 4 + 2], b.Pixels[i * 4 + 3] ]

let private R = (0uy, 0uy, 255uy, 255uy) // red as BGRA
let private B = (255uy, 0uy, 0uy, 255uy) // blue as BGRA

// --- Bitmaps ------------------------------------------------------------------------------------

[<Fact>]
let ``at its own size the pixels are used as they are`` () =
    let b = bitmapOver (Cursor.fromImage (picture "own" [ [ red; blue ] ]) []) 1.0
    Assert.Equal((2, 1), (b.Width, b.Height))
    Assert.Equal<_ list>([ R; B ], quads b)

[<Fact>]
let ``enlarged pixelated, pixels are repeated; enlarged smooth, they are blended`` () =
    let image = picture "up" [ [ red; blue ] ]
    let pixelated = bitmapOver (Cursor.fromImage image [ Cursor.scaling ImageScaling.Pixelated ]) 2.0
    Assert.Equal((4, 2), (pixelated.Width, pixelated.Height))
    Assert.Equal<_ list>([ R; R; B; B; R; R; B; B ], quads pixelated)
    let smooth = bitmapOver (Cursor.fromImage image []) 2.0
    Assert.Equal<_ list>([ R; (64uy, 0uy, 191uy, 255uy); (191uy, 0uy, 64uy, 255uy); B ], quads smooth |> List.take 4)
    // At 1.5×: three pixels across, the middle one half and half (127.5 rounds to even: 128).
    let between = bitmapOver (Cursor.fromImage image []) 1.5
    Assert.Equal((3, 2), (between.Width, between.Height))
    Assert.Equal<_ list>([ R; (128uy, 0uy, 128uy, 255uy); B ], quads between |> List.take 3)
    let betweenPixelated = bitmapOver (Cursor.fromImage image [ Cursor.scaling ImageScaling.Pixelated ]) 1.5
    Assert.Equal<_ list>([ R; B; B ], quads betweenPixelated |> List.take 3)

[<Fact>]
let ``shrunk, each pixel averages what it covers, pixelated or not`` () =
    let image = picture "down" [ [ red; red; blue; blue ]; [ red; blue; red; blue ] ]
    for scaling in [ ImageScaling.Smooth; ImageScaling.Pixelated ] do
        let b = bitmapOver (Cursor.fromImage image [ Cursor.size 2.0; Cursor.scaling scaling ]) 1.0
        Assert.Equal((2, 1), (b.Width, b.Height))
        // Left: three reds and a blue; right: one red, three blues.
        Assert.Equal<_ list>([ (64uy, 0uy, 191uy, 255uy); (191uy, 0uy, 64uy, 255uy) ], quads b)

// --- Size and hotspot ---------------------------------------------------------------------------

let private blank (name: string) (w: int) (h: int) density =
    Image.fromPixels (unique name, w, h, Array.create (w * h * 4) 255uy) [ Image.density density ]

[<Fact>]
let ``its size is the image's in DIPs, or Cursor.size wide keeping the aspect ratio`` () =
    let own = bitmapOver (Cursor.fromImage (blank "dense" 32 16 2.0) []) 1.0
    Assert.Equal((16, 8), (own.Width, own.Height))
    let sized = bitmapOver (Cursor.fromImage (blank "sized" 20 10 1.0) [ Cursor.size 40.0 ]) 1.0
    Assert.Equal((40, 20), (sized.Width, sized.Height))
    let sharp = bitmapOver (Cursor.fromImage (blank "sharp" 20 10 1.0) [ Cursor.size 40.0 ]) 2.0
    Assert.Equal((80, 40), (sharp.Width, sharp.Height))

[<Fact>]
let ``the hotspot is its centre unless given, in DIPs of the size asked for`` () =
    let centre = bitmapOver (Cursor.fromImage (blank "centre" 32 32 1.0) []) 2.0
    Assert.Equal((32, 32), (centre.HotspotX, centre.HotspotY))
    let given = bitmapOver (Cursor.fromImage (blank "given" 16 16 1.0) [ Cursor.size 32.0; Cursor.hotspot (16.0, 3.0) ]) 2.0
    Assert.Equal((64, 64), (given.Width, given.Height))
    Assert.Equal((32, 6), (given.HotspotX, given.HotspotY))

[<Fact>]
let ``over 128 DIPs it shrinks to fit, with a debug warning`` () =
    let warnings = List<string>()
    let listener = Action<string>(fun m -> lock warnings (fun () -> warnings.Add m))
    DebugWarnings.Listener <- Delegate.Combine(DebugWarnings.Listener, listener) :?> Action<string>
    try
        let image = blank "big" 200 100 1.0
        let b = bitmapOver (Cursor.fromImage image []) 1.0
        Assert.Equal((128, 64), (b.Width, b.Height))
        Assert.Equal((64, 32), (b.HotspotX, b.HotspotY))
        let hinted = bitmapOver (Cursor.fromImage image [ Cursor.hotspot (200.0, 100.0) ]) 1.0
        Assert.Equal((127, 63), (hinted.HotspotX, hinted.HotspotY)) // the far corner, kept inside
#if DEBUG
        Assert.Contains(lock warnings (fun () -> List.ofSeq warnings), fun (m: string) -> m.Contains(image.Key) && m.Contains "128")
#endif
    finally
        DebugWarnings.Listener <- Delegate.Remove(DebugWarnings.Listener, listener) :?> Action<string>

[<Fact>]
let ``icon-only settings on an image cursor warn and change nothing`` () =
    let warnings = List<string>()
    let listener = Action<string>(fun m -> lock warnings (fun () -> warnings.Add m))
    DebugWarnings.Listener <- Delegate.Combine(DebugWarnings.Listener, listener) :?> Action<string>
    try
        let image = picture "icon-only" [ [ red; blue ] ]
        let plain = bitmapOver (Cursor.fromImage image []) 1.0
        let filled = bitmapOver (Cursor.fromImage image [ Cursor.fill Color.White; Cursor.outlineWidth 3.0 ]) 1.0
        Assert.Equal<_ list>(quads plain, quads filled)
    finally
        DebugWarnings.Listener <- Delegate.Remove(DebugWarnings.Listener, listener) :?> Action<string>

[<Fact>]
let ``cursors are equal when their images and settings are`` () =
    let image = picture "equal" [ [ red ] ]
    Assert.Equal(Cursor.fromImage image [ Cursor.size 8.0 ], Cursor.fromImage image [ Cursor.size 8.0 ])
    Assert.NotEqual(Cursor.fromImage image [ Cursor.size 8.0 ], Cursor.fromImage image [ Cursor.size 9.0 ])
    Assert.NotEqual(Cursor.fromImage image [], Cursor.fromImage image [ Cursor.scaling ImageScaling.Pixelated ])

// --- Holding, failures ----------------------------------------------------------------------------

[<Fact>]
let ``the element holds its cursor's picture while it shows, before the pointer comes`` () =
    let image = picture "held" [ [ red; blue ] ]
    let w, host, _ = show (box (Cursor.fromImage image [])) []
    let e = host.App.Tree.Root.Value
    Assert.Equal(1, e.HeldImageCount)
    Assert.Equal(ImageStatus.Ready, (List.exactlyOne e.HeldImages).Status)
    move w 50.0 50.0
    Assert.True(match w.Cursor with Cursor.Image _ -> true | _ -> false)
    // Away and back: the same cursor value, still held.
    move w 300.0 200.0
    Assert.Equal(Cursor.Default, w.Cursor)
    move w 60.0 60.0
    Assert.Equal(Cursor.fromImage image [], w.Cursor)
    // Another cursor: changing a cursor draws no frame (power rule), so the picture is let go when the element next
    // draws.
    host.App.Update(box Cursor.Hand)
    settle w
    w.Size <- { Width = 500; Height = 300 }
    w.Send(Resized w.Size)
    settle w
    Assert.Equal(0, e.HeldImageCount)

[<Fact>]
let ``a picture that fails is reported once, and the arrow shows`` () =
    let broken = Image.fromBytes (unique "broken", [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0uy |]) []
    let w, host, errors = show (box (Cursor.fromImage broken [])) []
    // Reported when the element first shows, before any pointer.
    Assert.Single errors |> ignore
    move w 50.0 50.0
    Assert.Equal(Cursor.Default, w.Cursor)
    move w 60.0 60.0
    // More frames: still once (the element keeps holding the failed picture).
    w.Size <- { Width = 500; Height = 300 }
    w.Send(Resized w.Size)
    settle w
    move w 70.0 70.0
    Assert.Equal(Cursor.Default, w.Cursor)
    Assert.Equal(1, host.App.Tree.Root.Value.HeldImageCount)
    let e = Assert.Single errors
    Assert.IsType<ImageLoadException>(e) |> ignore

// --- The inspector -------------------------------------------------------------------------------

/// Every visible TextBlock's text in a layer, in view order.
let private texts (layer: Layer) =
    let rec collect (e: Element) =
        [ if e.IsVisible then
              if e.Type.Name = "TextBlock" then e.Get TextProps.text
              for c in e.Children do
                  yield! collect c ]
    match layer.Tree.Root with
    | ValueSome r -> collect r
    | ValueNone -> []

/// Picks what's at (x, y) with the inspector and returns the panel's text.
let private inspect (w: FakeWindow) (host: ElementHost) x y =
    let ctrlShift = Modifiers.Control ||| Modifiers.Shift
    w.Send(KeyDown(Key.Char 'c', ctrlShift, false))
    w.Send(KeyUp(Key.Char 'c', ctrlShift))
    settle w
    move w x y
    settle w
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    settle w
    texts (host.Layers |> Seq.find (fun l -> l.Name = "Inspector"))

let private section (name: string) (shown: string list) =
    let at = shown |> List.findIndex ((=) name)
    shown |> List.skip (at + 1)

[<Fact>]
let ``IMAGES shows what an ImageView holds: source, sizes, state, holders and a thumbnail`` () =
    let image = Image.fromPixels (unique "inspected", 64, 32, Array.create (64 * 32 * 4) 200uy) [ Image.density 2.0 ]
    let view =
        ImageView.create [
            ImageView.image image
            ImageView.horizontalAlignment HorizontalAlignment.Left
            ImageView.verticalAlignment VerticalAlignment.Top
        ]
    let w, host, _ = show view [ Inspector.enable [] ]
    w.Size <- { Width = 900; Height = 500 }
    w.Send(Resized w.Size)
    settle w
    let shown = inspect w host 10.0 10.0 |> section "IMAGES"
    Assert.Equal<string list>(
        [ "source"; image.Key + " at density 2"; "size"; "64 × 32 px, 32 × 16 DIPs"; "state"; "ready"; "held by"; "1 element" ],
        shown |> List.take 8
    )
    // The thumbnail: at the image's own size in DIPs (it fits 96), drawn without holding the picture.
    let panel = host.Layers |> Seq.find (fun l -> l.Name = "Inspector")
    let rec thumbnails (e: Element) =
        [ if e.Type.Name = "InspectorThumbnail" then e
          for c in e.Children do
              yield! thumbnails c ]
    let t = thumbnails panel.Tree.Root.Value |> List.exactlyOne
    Assert.Equal((32.0, 16.0), (t.Bounds.Width, t.Bounds.Height))
    Assert.Equal(0, t.HeldImageCount)
    // PROPERTIES names the image by its source too.
    Assert.Contains(image.Key + " at density 2", inspect w host 10.0 10.0 |> section "PROPERTIES")

[<Fact>]
let ``IMAGES shows an image cursor's picture, and a failure with its error`` () =
    let image = picture "cursor-inspected" [ [ red; blue ] ]
    let broken = Image.fromBytes (unique "broken-inspected", [| 1uy; 2uy; 3uy |]) []
    let view =
        StackPanel.create [
            StackPanel.children [
                box (Cursor.fromImage image [])
                Border.create [
                    Border.width 200.0
                    Border.height 100.0
                    Border.horizontalAlignment HorizontalAlignment.Left
                    Border.cursor (Cursor.fromImage broken [])
                ]
            ]
        ]
    let w, host, errors = show view [ Inspector.enable [] ]
    w.Size <- { Width = 900; Height = 500 }
    w.Send(Resized w.Size)
    settle w
    let shown = inspect w host 50.0 50.0
    Assert.Contains("image " + image.Key, shown) // the cursor property
    let images = shown |> section "IMAGES"
    Assert.Equal<string list>([ "source"; image.Key; "size"; "2 × 1 px, 2 × 1 DIPs"; "state"; "ready" ], images |> List.take 6)
    let failed = inspect w host 50.0 150.0 |> section "IMAGES"
    Assert.Equal("state", failed[2])
    Assert.StartsWith("failed: ", failed[3])
    Assert.Single errors |> ignore
