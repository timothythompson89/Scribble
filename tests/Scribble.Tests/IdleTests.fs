/// The power rule on real apps: once the pointer stops, an app whose model doesn't change draws
/// nothing more. A hover that changes layout can move the element out from under the pointer and
/// back, forever (2026-11-17: the Counter's Layout tab kept drawing 20–144 frames a second).
module Scribble.Tests.IdleTests

open Xunit
open Scribble.Platform
open Scribble.Core
open Scribble.Tests.AppTests

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let rec private firstText (e: Element) : string =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then e.Get TextProps.text
    else e.Children |> Seq.map firstText |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""

/// Delivers frames until none is requested; returns how many were drawn (stops at `limit`).
let private settle (w: FakeWindow) limit =
    let mutable n = 0
    while n < limit && w.DeliverFrame() do
        n <- n + 1
    n

/// True when frames are still requested after `ms` of real time (animations run on the clock,
/// so a hover fade ends; a loop doesn't). Frames every 4 ms.
let private stillDrawingAfter (w: FakeWindow) (ms: float) =
    let clock = System.Diagnostics.Stopwatch.StartNew()
    let mutable drawing = true
    while drawing && clock.Elapsed.TotalMilliseconds < ms do
        drawing <- w.DeliverFrame()
        System.Threading.Thread.Sleep 4
    drawing && w.DeliverFrame()

let private tabs = [ "Elmish"; "Load"; "List"; "Layers"; "Styles"; "Layout"; "Controls" ]

[<Fact>]
let ``the Counter demo goes idle wherever the pointer stops, on every tab`` () =
    let w, _, app = start Scribble.Samples.Counter.app
    let host = app :?> ElementHost
    let size: PixelSize = { Width = 1000; Height = 900 }
    w.Size <- size
    w.Send(Resized size)
    settle w 50 |> ignore
    let looping = System.Collections.Generic.List<string>()
    for tab in tabs do
        let button =
            all host.Tree.Root.Value
            |> Seq.find (fun e -> (e.Get LayoutProps.onClick).IsSome && firstText e = tab)
        (button.Get LayoutProps.onClick).Value(ClickEventArgs(ClickSource.Keyboard, Modifiers.None))
        // A page is built on its first visit: the Load page starts its one-second load then.
        Assert.False(settle w 50 >= 50 && stillDrawingAfter w 2500.0, $"the {tab} tab keeps drawing with no pointer")
        let mutable y = 2.0
        while y < float size.Height do
            let mutable x = 2.0
            while x < float size.Width do
                w.Send(PointerMoved(Mouse, 0, { X = x; Y = y }, Modifiers.None))
                if settle w 30 >= 30 && stillDrawingAfter w 600.0 then looping.Add $"{tab} at ({x}, {y})"
                x <- x + 20.0
            y <- y + 20.0
        w.Send(PointerExited(Mouse, 0))
        settle w 50 |> ignore
    app.Dispose()
    Assert.True(looping.Count = 0, "keeps drawing with the pointer at: " + String.concat "; " (Seq.truncate 40 looping) + $" ({looping.Count} points)")
