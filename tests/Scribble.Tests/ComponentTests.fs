/// Components (M5 step 3): hooks, what re-runs and when, effects and subscriptions, hook-order
/// checks, and animation timing, on a bare element tree (no window).
module Scribble.Tests.ComponentTests

open System
open System.Collections.Generic
open Xunit
open Scribble.Core
open Scribble.Tests.TestShaping

let private tree () = ElementTree(textSystem ())

/// One frame: re-run changed components, lay out, then what runs after a frame (effects).
let private frame (t: ElementTree) =
    t.UpdateLayout(Size.Create(400.0, 300.0))
    t.RunAfterFrame()

let private textOf (e: Element) = e.Get TextProps.text

/// The first TextBlock text under an element, depth first.
let rec private firstText (e: Element) : string =
    if obj.ReferenceEquals(e.Type, TextBlock.Type) then textOf e
    else e.Children |> Seq.map firstText |> Seq.tryFind (fun s -> s <> "") |> Option.defaultValue ""

let private ms (v: float) = TimeSpan.FromMilliseconds v

[<Fact>]
let ``state lives across runs and a change re-runs only that component`` () =
    let runs = List<string>()
    let mutable states: State<int> list = []
    let counter =
        Component.define "Counter" (fun ctx (name: string) ->
            runs.Add name
            let count = ctx.useState 0
            states <- states @ [ count ]
            TextBlock.create [ TextBlock.text $"{name} {count.Current}" ])
    let t = tree ()
    t.Update(StackPanel.create [ StackPanel.children [ counter "a"; counter "b" ] ])
    frame t
    Assert.Equal<string list>([ "a"; "b" ], List.ofSeq runs)
    let a = states[0]
    runs.Clear()
    a.Update(fun c -> c + 1)
    a.Update(fun c -> c + 1)
    Assert.Empty runs // not until the next frame
    frame t
    Assert.Equal<string list>([ "a" ], List.ofSeq runs)
    let row = t.Root.Value
    Assert.Equal("a 2", firstText row.Children[0])
    Assert.Equal("b 0", firstText row.Children[1])
    runs.Clear()
    a.Set 2 // equal: nothing
    frame t
    Assert.Empty runs

[<Fact>]
let ``a parent's update re-runs a component only when its props change`` () =
    let runs = List<int>()
    let mutable state: State<string> voption = ValueNone
    let label =
        Component.define "Label" (fun ctx (n: int) ->
            runs.Add n
            let suffix = ctx.useState "!"
            state <- ValueSome suffix
            TextBlock.create [ TextBlock.text $"{n}{suffix.Current}" ])
    let t = tree ()
    let view n = StackPanel.create [ StackPanel.spacing 4.0; StackPanel.children [ label n ] ]
    t.Update(view 1)
    t.Update(view 1) // equal props: skipped
    Assert.Equal<int list>([ 1 ], List.ofSeq runs)
    state.Value.Set "?"
    frame t
    t.Update(view 2) // new props: runs, and keeps its state
    Assert.Equal<int list>([ 1; 1; 2 ], List.ofSeq runs)
    Assert.Equal("2?", firstText t.Root.Value)

[<Fact>]
let ``keyed components keep their state when the list reorders`` () =
    let states = Dictionary<string, State<int>>()
    let item =
        Component.define "Item" (fun ctx (name: string) ->
            let clicks = ctx.useState 0
            states[name] <- clicks
            TextBlock.create [ TextBlock.text $"{name}:{clicks.Current}" ])
    let t = tree ()
    let view names = StackPanel.create [ StackPanel.items names id item ]
    t.Update(view [ "x"; "y"; "z" ])
    states["y"].Set 5
    frame t
    t.Update(view [ "z"; "y" ]) // x removed, z moved to the front
    frame t
    let texts = [ for c in t.Root.Value.Children -> firstText c ]
    Assert.Equal<string list>([ "z:0"; "y:5" ], texts)

[<Fact>]
let ``useMemo computes again only when its dependencies change`` () =
    let computed = List<int>()
    let mutable bump: State<int> voption = ValueNone
    let view =
        Component.define "Memo" (fun ctx (n: int) ->
            let other = ctx.useState 0
            bump <- ValueSome other
            let square = ctx.useMemo(n, fun n -> computed.Add n; n * n)
            TextBlock.create [ TextBlock.text $"{square} {other.Current}" ])
    let t = tree ()
    t.Update(view 3)
    bump.Value.Set 1
    frame t
    t.Update(view 4)
    Assert.Equal<int list>([ 3; 4 ], List.ofSeq computed)
    Assert.Equal("16 1", firstText t.Root.Value)

[<Fact>]
let ``effects run after the frame, again when dependencies change, and can set state`` () =
    let log = List<string>()
    let effects =
        Component.define "Effects" (fun ctx (id: int) ->
            let loaded = ctx.useState ""
            ctx.useEffect((), fun () -> log.Add "once")
            ctx.useEffect(id, fun id ->
                log.Add $"load {id}"
                loaded.Set $"item {id}")
            TextBlock.create [ TextBlock.text loaded.Current ])
    let t = tree ()
    t.Update(effects 1)
    Assert.Empty log // nothing before the frame
    frame t
    Assert.Equal<string list>([ "once"; "load 1" ], List.ofSeq log)
    frame t // the state the effect set: re-run, no effects (dependencies unchanged)
    Assert.Equal("item 1", firstText t.Root.Value)
    Assert.Equal(2, log.Count)
    t.Update(effects 2)
    frame t
    Assert.Equal<string list>([ "once"; "load 1"; "load 2" ], List.ofSeq log)

[<Fact>]
let ``subscriptions are disposed when dependencies change and when the component goes away`` () =
    let log = List<string>()
    let subscriber =
        Component.define "Subscriber" (fun ctx (topic: string) ->
            ctx.useSubscription(topic, fun topic ->
                log.Add $"subscribe {topic}"
                { new IDisposable with member _.Dispose() = log.Add $"dispose {topic}" })
            TextBlock.create [ TextBlock.text topic ])
    let t = tree ()
    let view (topics: string list) = StackPanel.create [ StackPanel.children [ for topic in topics -> subscriber topic ] ]
    t.Update(view [ "news" ])
    frame t
    t.Update(view [ "sport" ]) // same position, new props: re-subscribe
    frame t
    t.Update(view [])
    frame t
    Assert.Equal<string list>([ "subscribe news"; "dispose news"; "subscribe sport"; "dispose sport" ], List.ofSeq log)

[<Fact>]
let ``useElmish keeps a model and dispatches messages`` () =
    let mutable send: (int -> unit) voption = ValueNone
    let elmish =
        Component.define "Elmish" (fun ctx () ->
            let model, dispatch = ctx.useElmish(10, fun (msg: int) model -> model + msg)
            send <- ValueSome dispatch
            TextBlock.create [ TextBlock.text $"{model}" ])
    let t = tree ()
    t.Update(elmish ())
    send.Value 5
    send.Value -2
    frame t
    Assert.Equal("13", firstText t.Root.Value)

[<Fact>]
let ``hooks called in a different order fail with the component's name`` () =
    let flip = ref false
    let mutable state: State<int> voption = ValueNone
    let bad =
        Component.define "Fickle" (fun ctx () ->
            let s = ctx.useState 0
            state <- ValueSome s
            if flip.Value then ctx.useEffect((), ignore) else ctx.useMemo((), ignore)
            TextBlock.create [ TextBlock.text "" ])
    let t = tree ()
    t.Update(bad ())
    flip.Value <- true
    state.Value.Set 1
    let error = Assert.Throws<InvalidOperationException>(fun () -> frame t)
    Assert.Contains("Fickle", error.Message)
    Assert.Contains("useMemo", error.Message)

[<Fact>]
let ``setting state while the component runs fails`` () =
    let bad =
        Component.define "Eager" (fun ctx () ->
            let s = ctx.useState 0
            s.Set 1
            TextBlock.create [ TextBlock.text "" ])
    let t = tree ()
    let error = Assert.Throws<InvalidOperationException>(fun () -> t.Update(bad ()))
    Assert.Contains("Eager", error.Message)

[<Fact>]
let ``useAnimated moves over its duration, re-running each frame, then stops`` () =
    let values = List<float>()
    let mutable target: State<float> voption = ValueNone
    let bar =
        Component.define "Bar" (fun ctx () ->
            let goal = ctx.useState 0.0
            target <- ValueSome goal
            let v = ctx.useAnimated(goal.Current, ms 100.0, Easing.linear)
            values.Add v
            TextBlock.create [ TextBlock.text "" ])
    let t = tree ()
    t.Update(bar ())
    frame t
    Assert.Equal<float list>([ 0.0 ], List.ofSeq values) // the first target is taken at once
    target.Value.Set 1.0
    for time in [ 0.0; 50.0; 100.0; 150.0 ] do
        t.Time <- ms time
        frame t
    // Ran at 0, 50 and 100 ms; arrived at 100, so nothing asked for a run at 150.
    Assert.Equal<float list>([ 0.0; 0.0; 0.5; 1.0 ], List.ofSeq values)

[<Fact>]
let ``tweens start from where the value is when the target changes`` () =
    let tween = Tween<float>(Lerp.float, 0.0)
    Assert.Equal(0.0, tween.Update(0.0, ms 0.0, ms 100.0, Easing.linear))
    Assert.Equal(0.0, tween.Update(10.0, ms 0.0, ms 100.0, Easing.linear))
    Assert.Equal(5.0, tween.Update(10.0, ms 50.0, ms 100.0, Easing.linear))
    // Back to 0 from halfway: starts at 5.
    Assert.Equal(5.0, tween.Update(0.0, ms 50.0, ms 100.0, Easing.linear))
    Assert.Equal(2.5, tween.Update(0.0, ms 100.0, ms 100.0, Easing.linear))
    Assert.False(tween.IsMoving(ms 150.0))
    Assert.Equal(0.0, tween.ValueAt(ms 150.0))

[<Fact>]
let ``easings start at 0 and end at 1`` () =
    for ease in [ Easing.linear; Easing.easeIn; Easing.easeOut; Easing.easeInOut ] do
        Assert.Equal(0.0, ease 0.0, 9)
        Assert.Equal(1.0, ease 1.0, 9)
    Assert.True(Easing.easeOut 0.5 > 0.5)
    Assert.True(Easing.easeIn 0.5 < 0.5)

[<Fact>]
let ``in an app, a pressed button animates for a moment and then frames stop`` () =
    let w, _, _ = Scribble.Tests.AppTests.start [ App.view (Button.create [ Button.width 100.0; Button.height 30.0 ]) ]
    w.DeliverFrame() |> ignore
    Assert.False(w.DeliverFrame()) // idle
    w.Send(Scribble.Platform.PointerPressed(Scribble.Platform.Mouse, 0, { X = 50.0; Y = 150.0 }, MouseButton.Left, Modifiers.None))
    let started = w.Time.Now
    let mutable frames = 0
    while frames < 200 && w.DeliverFrame() do
        frames <- frames + 1
    // The 120 ms press keeps frames coming (16 ms of fake time each), then they stop by themselves.
    let ms = (w.Time.Now - started).TotalMilliseconds
    Assert.True(frames > 1, $"{frames} frames")
    Assert.True(ms >= 112.0 && ms < 300.0, string ms + " ms")

[<Fact>]
let ``in an app, effects run after the frame and the state they set gets its own frame`` () =
    let loader =
        Component.define "Loader" (fun ctx () ->
            let text = ctx.useState "loading"
            ctx.useEffect((), fun () -> text.Set "loaded")
            TextBlock.create [ TextBlock.text text.Current ])
    let w, _, app = Scribble.Tests.AppTests.start [ App.view (loader ()) ]
    w.DeliverFrame() |> ignore
    Assert.True(w.DeliverFrame()) // the effect's change asked for another frame
    let host = app :?> ElementHost
    Assert.Equal("loaded", firstText host.Tree.Root.Value)
    Assert.False(w.DeliverFrame())

// --- ctx.Animate: a value animated inside a render function (no property behind it) -----------------

module private AnimatedProps =
    let target = Prop.define "Target" 0.0 Affects.Render

/// Draws nothing; remembers the animated value its render function saw.
let mutable private seen = Double.NaN

let private animatedType =
    Element.define "Animated" [
        Element.measure (fun _ _ -> Size.Create(10.0, 10.0))
        Element.render (fun ctx _ -> seen <- ctx.Animate("glide", ctx.Get AnimatedProps.target, ms 100.0, Easing.linear))
    ]

[<Fact>]
let ``ctx.Animate moves over its duration, asks for frames while it moves, then stops`` () =
    let t = tree ()
    let mutable invalidated = 0
    t.OnInvalidated <- fun () -> invalidated <- invalidated + 1
    let view (target: float) =
        let settings: Setting<Layoutable> list = [ Setting.create AnimatedProps.target target ]
        View.Create(animatedType, settings)
    let draw () =
        t.UpdateLayout(Size.Create(100.0, 100.0))
        t.Render(Scribble.Rendering.DrawList(1.0))
    t.Time <- TimeSpan.Zero
    t.Update(view 0.0)
    draw ()
    Assert.Equal(0.0, seen) // the first target is taken at once
    t.RunAfterFrame()
    t.Update(view 10.0)
    draw ()
    Assert.Equal(0.0, seen) // just started
    // While it moves, each frame asks for the next one.
    invalidated <- 0
    t.RunAfterFrame()
    Assert.True(invalidated > 0)
    t.Time <- ms 50.0
    draw ()
    Assert.Equal(5.0, seen, 6)
    t.RunAfterFrame()
    // A new target midway heads there from where it is.
    t.Update(view 0.0)
    draw ()
    Assert.Equal(5.0, seen, 6)
    t.Time <- ms 100.0
    draw ()
    Assert.Equal(2.5, seen, 6)
    t.Time <- ms 150.0
    draw ()
    Assert.Equal(0.0, seen)
    // Arrived: no more frames.
    t.RunAfterFrame()
    invalidated <- 0
    draw ()
    t.RunAfterFrame()
    Assert.Equal(0, invalidated)
