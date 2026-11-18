/// Errors in app code (project review 2026-11-17, decision 22): with `App.onError` they're reported
/// and the app keeps running, losing only what failed; without it they end the app.
module Scribble.Tests.ErrorTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Platform
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.AppTests

let private click (w: FakeWindow) x y =
    w.Send(PointerPressed(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))
    w.Send(PointerReleased(Mouse, 0, { X = x; Y = y }, MouseButton.Left, Modifiers.None))

let private settle (w: FakeWindow) =
    let mutable n = 0
    while n < 100 && w.DeliverFrame() do
        n <- n + 1

let rec private all (e: Element) : Element seq =
    seq {
        yield e
        for c in e.Children do
            yield! all c
    }

let private texts (app: IDisposable) =
    let host = app :?> ElementHost
    all host.Tree.Root.Value
    |> Seq.filter (fun e -> obj.ReferenceEquals(e.Type, TextBlock.Type))
    |> Seq.map (fun e -> e.Get TextProps.text)
    |> List.ofSeq

/// Children stacked from the top left, so the n-th 30-DIP button is at y = 30n.
let private column (children: View list) =
    StackPanel.create [
        StackPanel.horizontalAlignment HorizontalAlignment.Left
        StackPanel.verticalAlignment VerticalAlignment.Top
        StackPanel.children children
    ]

let private button (label: string) onClick =
    Button.create [ Button.width 100.0; Button.height 30.0; Button.text label; Button.onClick onClick ]

/// An element whose render (or measure) throws while its property says so; it draws one
/// rectangle first, which the failed render must take back.
module private Failing =
    let failRender = Prop.define "FailRender" false Affects.Render
    let failMeasure = Prop.define "FailMeasure" false Affects.Measure

    let Type =
        Element.define "Failing" [
            Element.measure (fun ctx _ ->
                if ctx.Get failMeasure then invalidOp "measure failed"
                Size.Create(50.0, 20.0))
            Element.render (fun ctx list ->
                list.FillRect(Rect.Create(0.0, 0.0, 50.0, 20.0), Color.White)
                if ctx.Get failRender then invalidOp "render failed")
        ]

    let create (render: bool) (measure: bool) =
        View.Create(Type, [ Setting.create failRender render; Setting.create failMeasure measure ])

type private Msg =
    | Add
    | Fail

[<Fact>]
let ``with onError, a click handler or update that throws is reported and the app keeps running`` () =
    let errors = List<exn>()
    let update msg count =
        match msg with
        | Add -> count + 1
        | Fail -> invalidOp "update failed"
    let view count dispatch =
        column [
            button "boom" (fun _ -> invalidOp "handler failed")
            button "fail" (fun _ -> dispatch Fail)
            button "add" (fun _ -> dispatch Add)
            TextBlock.create [ TextBlock.text ("Count: " + string count) ]
        ]
    let w, _, app = start [ App.program 0 update view; App.onError errors.Add ]
    settle w
    click w 50.0 15.0
    click w 50.0 45.0
    click w 50.0 75.0
    settle w
    Assert.Equal<string list>([ "handler failed"; "update failed" ], [ for e in errors -> e.Message ])
    // The failed update left the model as it was; the next one worked.
    Assert.Contains("Count: 1", texts app)

[<Fact>]
let ``without onError, an error in a click handler escapes, and so does one in posted work`` () =
    let w, _, _ = start [ App.view (column [ button "boom" (fun _ -> invalidOp "handler failed") ]) ]
    settle w
    let e = Assert.Throws<InvalidOperationException>(Action(fun () -> click w 50.0 15.0))
    Assert.Equal("handler failed", e.Message)
    let d = Dispatcher(ignore)
    d.Post(fun () -> invalidOp "posted work failed")
    let e = Assert.Throws<InvalidOperationException>(Action(fun () -> d.RunPending()))
    Assert.Equal("posted work failed", e.Message)

[<Fact>]
let ``a view that throws keeps the elements of the last one, until the model changes again`` () =
    let errors = List<exn>()
    let view count dispatch =
        if count = 1 then invalidOp "view failed"
        column [ button "add" (fun _ -> dispatch Add); TextBlock.create [ TextBlock.text ("Count: " + string count) ] ]
    let w, _, app = start [ App.program 0 (fun Add c -> c + 1) view; App.onError errors.Add ]
    settle w
    click w 50.0 15.0
    settle w
    Assert.Equal(1, errors.Count)
    Assert.Contains("Count: 0", texts app)
    click w 50.0 15.0
    settle w
    Assert.Contains("Count: 2", texts app)

[<Fact>]
let ``a render that throws leaves out that element only, and is reported once while it keeps failing`` () =
    let errors = List<exn>()
    // The model moves every frame (an animation); the element fails for 10 frames, draws for
    // 10, then fails again.
    let view (n: int) _ =
        let failing = n % 20 < 10
        column [ Failing.create failing false; TextBlock.create [ TextBlock.text "sibling" ] ]
    let w, device, app = start [ App.program 0 (fun (d: int) n -> n + d) view; App.onFrame (fun _ -> Some 1); App.onError errors.Add ]
    let shapes = HashSet<int>()
    for _ in 1..35 do
        Assert.True(w.DeliverFrame())
        shapes.Add device.LastShapes |> ignore
    Assert.Equal(2, errors.Count) // once per failing stretch, not once per frame
    Assert.All(errors, fun e -> Assert.Equal("render failed", e.Message))
    // Frames were drawn throughout; a failed render took back the rectangle it drew.
    Assert.Equal(2, shapes.Count)
    Assert.Equal(1, (Seq.max shapes) - (Seq.min shapes))
    Assert.Contains("sibling", texts app)

[<Fact>]
let ``a measure that throws drops the frame; the last one stays on screen`` () =
    let errors = List<exn>()
    let view (failing: bool) dispatch =
        column [ button "toggle" (fun _ -> dispatch ()); Failing.create false failing ]
    let w, device, _ = start [ App.program false (fun () b -> not b) view; App.onError errors.Add ]
    settle w
    let drawn = device.Frames
    click w 50.0 15.0
    settle w
    Assert.Equal(drawn, device.Frames)
    Assert.Equal(1, errors.Count) // reported once, though the hover fade asked for more frames
    Assert.Equal("measure failed", errors[0].Message)
    click w 50.0 15.0
    settle w
    Assert.True(device.Frames > drawn)

[<Fact>]
let ``a component that throws keeps showing its last output, and runs again when its state changes`` () =
    let errors = List<exn>()
    let flaky =
        Component.define "Flaky" (fun ctx () ->
            let n = ctx.useState 0
            if n.Current = 1 then invalidOp "component failed"
            // A hook rule broken on one run is an error like any other.
            if n.Current = 3 then ctx.useState "extra" |> ignore
            column [ button "next" (fun _ -> n.Update(fun v -> v + 1)); TextBlock.create [ TextBlock.text ("n = " + string n.Current) ] ])
    let w, _, app = start [ App.view (flaky ()); App.onError errors.Add ]
    settle w
    click w 50.0 15.0
    settle w
    Assert.Contains("n = 0", texts app)
    click w 50.0 15.0
    settle w
    Assert.Contains("n = 2", texts app)
    click w 50.0 15.0
    settle w
    Assert.Contains("n = 2", texts app)
    click w 50.0 15.0
    settle w
    Assert.Contains("n = 4", texts app)
    Assert.Equal(2, errors.Count)
    Assert.Equal("component failed", errors[0].Message)
    Assert.Contains("more hooks", errors[1].Message)

[<Fact>]
let ``an effect that throws doesn't stop the others`` () =
    let errors = List<exn>()
    let effects =
        Component.define "Effects" (fun ctx () ->
            let s = ctx.useState "waiting"
            ctx.useEffect((), fun () -> invalidOp "effect failed")
            ctx.useEffect(1, fun (_: int) -> s.Set "second ran")
            TextBlock.create [ TextBlock.text s.Current ])
    let w, _, app = start [ App.view (effects ()); App.onError errors.Add ]
    settle w
    Assert.Equal(1, errors.Count)
    Assert.Contains("second ran", texts app)

[<Fact>]
let ``an ItemsView item whose view throws is left out; the others show`` () =
    let errors = List<exn>()
    let items =
        ItemsView.create [
            ItemsView.source [| 1..5 |]
            ItemsView.keySelector (fun (i: int) -> i)
            ItemsView.viewSelector (fun (i: int) -> if i = 3 then invalidOp "item failed" else TextBlock.create [ TextBlock.text ("item " + string i) ])
        ]
    let w, _, app = start [ App.view items; App.onError errors.Add ]
    settle w
    let shown = texts app
    Assert.Equal<string list>([ "item 1"; "item 2"; "item 4"; "item 5" ], shown)
    Assert.Equal(1, errors.Count)
