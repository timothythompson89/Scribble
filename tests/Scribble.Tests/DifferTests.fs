module Scribble.Tests.DifferTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Core
open Scribble.Tests.TestShaping
open Scribble.Tests.LayoutTests

let private tree () = ElementTree(textSystem ())

let private same (a: Element) (b: Element) = Assert.Same(a, b)
let private different (a: Element) (b: Element) = Assert.NotSame(a, b)

[<Fact>]
let ``a changed setting patches the same element`` () =
    let t = tree ()
    t.Update(Border.create [ Border.background Color.White; Border.padding 4.0 ])
    let first = t.Root.Value
    t.Update(Border.create [ Border.background Color.Black; Border.padding 4.0 ])
    same first t.Root.Value
    Assert.Equal(Color.Black, first.Get BorderProps.background)

[<Fact>]
let ``a setting the new view leaves out goes back to its default`` () =
    let t = tree ()
    t.Update(Border.create [ Border.background Color.White; Border.padding 4.0 ])
    t.Update(Border.create [ Border.padding 4.0 ])
    Assert.Equal(BorderProps.background.Default, t.Root.Value.Get BorderProps.background)

[<Fact>]
let ``an element type's defaults apply until a view sets its own, and come back when it stops`` () =
    let t = tree ()
    let buttonBackground = Color.Hex 0x3A3A55
    t.Update(Button.create [ Button.text "Go" ])
    let b = t.Root.Value
    Assert.Equal(buttonBackground, b.Get BorderProps.background)
    // Layout defaults too: Button aligns left (the engine keeps a typed copy of layout values).
    Assert.Equal(HorizontalAlignment.Left, b.Get LayoutProps.horizontalAlignment)
    t.UpdateLayout(Size.Create(400.0, 100.0))
    Assert.Equal(0.0, b.Bounds.X)
    Assert.True(b.Bounds.Width < 400.0)
    t.Update(Button.create [ Button.text "Go"; Button.background Color.White ])
    Assert.Equal(Color.White, b.Get BorderProps.background)
    // Left out again: back to Button's default, not the property's (transparent).
    t.Update(Button.create [ Button.text "Go" ])
    Assert.Equal(buttonBackground, b.Get BorderProps.background)
    // Views no longer carry the defaults.
    Assert.Equal(1, b.CurrentView.Value.Settings.Length)

[<Fact>]
let ``extend starts from the base type, define from a plain container`` () =
    let fancy = Element.extend Border.Type "Fancy" [ Element.defaults [ Border.padding 10.0 ] ]
    let plain = Element.define "Plain" []
    Assert.Equal("Fancy", fancy.Name)
    Assert.Same(Border.Type.Measure, fancy.Measure)
    Assert.Equal("Plain", plain.Name)
    Assert.Empty(plain.Defaults)
    // Later defaults replace earlier ones for the same property.
    let twice = Element.extend fancy "Twice" [ Element.defaults [ Border.padding 2.0 ] ]
    Assert.Equal(1, twice.Defaults.Length)

[<Fact>]
let ``element defaults can't hold children`` () =
    Assert.Throws<ArgumentException>(fun () -> Element.defaults [ Border.child (TextBlock.create []) ] |> ignore) |> ignore

[<Fact>]
let ``a different element type is rebuilt`` () =
    let t = tree ()
    t.Update(StackPanel.create [ StackPanel.children [ Border.create [] ] ])
    let border = child t.Root.Value [ 0 ]
    t.Update(StackPanel.create [ StackPanel.children [ TextBlock.create [ TextBlock.text "hi" ] ] ])
    let text = child t.Root.Value [ 0 ]
    Assert.Equal("TextBlock", text.Type.Name)
    Assert.True border.Parent.IsNone

[<Fact>]
let ``children match by position: appending keeps the existing ones`` () =
    let t = tree ()
    let item (s: string) = TextBlock.create [ TextBlock.text s ]
    t.Update(StackPanel.create [ StackPanel.children [ item "a"; item "b" ] ])
    let a, b = child t.Root.Value [ 0 ], child t.Root.Value [ 1 ]
    t.Update(StackPanel.create [ StackPanel.children [ item "a"; item "b"; item "c" ] ])
    same a (child t.Root.Value [ 0 ])
    same b (child t.Root.Value [ 1 ])
    Assert.Equal(3, t.Root.Value.Children.Count)

[<Fact>]
let ``without keys, inserting at the top shifts identities`` () =
    let t = tree ()
    let item (s: string) = TextBlock.create [ TextBlock.text s ]
    t.Update(StackPanel.create [ StackPanel.children [ item "a"; item "b" ] ])
    let a = child t.Root.Value [ 0 ]
    t.Update(StackPanel.create [ StackPanel.children [ item "new"; item "a"; item "b" ] ])
    // The old "a" element now shows "new": correct on screen, but its identity moved.
    same a (child t.Root.Value [ 0 ])
    Assert.Equal("new", a.Get TextProps.text)

[<Fact>]
let ``keys keep each item's element through inserts and reorders`` () =
    let t = tree ()
    let item (s: string) = TextBlock.create [ TextBlock.key s; TextBlock.text s ]
    t.Update(StackPanel.create [ StackPanel.children [ item "a"; item "b"; item "c" ] ])
    let a, b, c = child t.Root.Value [ 0 ], child t.Root.Value [ 1 ], child t.Root.Value [ 2 ]
    t.Update(StackPanel.create [ StackPanel.children [ item "new"; item "c"; item "a" ] ])
    let r = t.Root.Value
    Assert.Equal("new", (child r [ 0 ]).Get TextProps.text)
    same c (child r [ 1 ])
    same a (child r [ 2 ])
    Assert.True b.Parent.IsNone

[<Fact>]
let ``items takes keys from the collection`` () =
    let t = tree ()
    let view (todos: (int * string) list) =
        StackPanel.create [ StackPanel.items todos fst (fun (_, title) -> TextBlock.create [ TextBlock.text title ]) ]
    t.Update(view [ 1, "one"; 2, "two" ])
    let two = child t.Root.Value [ 1 ]
    t.Update(view [ 2, "two"; 1, "one" ])
    same two (child t.Root.Value [ 0 ])

[<Fact>]
let ``a single child is patched in place`` () =
    let t = tree ()
    t.Update(Border.create [ Border.child (TextBlock.create [ TextBlock.text "a" ]) ])
    let text = child t.Root.Value [ 0 ]
    t.Update(Border.create [ Border.child (TextBlock.create [ TextBlock.text "b" ]) ])
    same text (child t.Root.Value [ 0 ])
    Assert.Equal("b", text.Get TextProps.text)
    t.Update(Border.create [])
    Assert.Equal(0, t.Root.Value.Children.Count)

[<Fact>]
let ``an update re-measures only what changed`` () =
    let t = tree ()
    let view (margin: float) =
        StackPanel.create [ StackPanel.children [ box 10.0 10.0 []; box 10.0 10.0 [ Box.margin margin ]; box 10.0 10.0 [] ] ]
    t.Update(view 0.0)
    t.UpdateLayout(Size.Create(100.0, 100.0))
    t.Update(view 2.0)
    t.UpdateLayout(Size.Create(100.0, 100.0))
    let r = t.Root.Value
    Assert.Equal(1, measures (child r [ 0 ]))
    Assert.Equal(2, measures (child r [ 1 ]))
    Assert.Equal(1, measures (child r [ 2 ]))

[<Fact>]
let ``the same view object is skipped, a new one wins over changes made with Set`` () =
    let t = tree ()
    let view = Border.create [ Border.padding 4.0 ]
    t.Update view
    let b = t.Root.Value
    b.Set(Border.padding 9.0)
    t.Update view
    Assert.Equal(Thickness.Uniform 9.0, b.Get BorderProps.padding)
    t.Update(Border.create [ Border.padding 4.0 ])
    Assert.Equal(Thickness.Uniform 4.0, b.Get BorderProps.padding)

[<Fact>]
let ``the same view in two places becomes two elements`` () =
    let t = tree ()
    let shared = TextBlock.create [ TextBlock.text "x" ]
    t.Update(StackPanel.create [ StackPanel.children [ shared; shared ] ])
    different (child t.Root.Value [ 0 ]) (child t.Root.Value [ 1 ])

[<Timing.TimingFact; Trait("Category", "Timing")>]
let ``rebuilding and diffing a 1,000-element view with one change is fast`` () =
    let view changed =
        StackPanel.create [
            StackPanel.children [
                for row in 0..39 ->
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.children [
                            for col in 0..24 ->
                                Border.create [
                                    Border.width 8.0
                                    Border.height 8.0
                                    Border.background (if row = 20 && col = 12 && changed then Color.White else Color.Black)
                                ]
                        ]
                    ]
            ]
        ]
    let t = tree ()
    t.Update(view false)
    let size = Size.Create(1000.0, 1000.0)
    t.UpdateLayout size
    let mutable flip = false
    let step () =
        flip <- not flip
        t.Update(view flip)
        t.UpdateLayout size
    for _ in 1..300 do step ()
    let mutable best = Double.MaxValue
    for _ in 1..20 do
        let sw = Diagnostics.Stopwatch.StartNew()
        step ()
        best <- min best sw.Elapsed.TotalMilliseconds
    Assert.True(best < 10.0, $"view + diff + layout took {best:F2} ms")
    Assert.Equal(1041, (let rec count (e: Element) = 1 + Seq.sumBy count e.Children in count t.Root.Value))
    Console.WriteLine($"1k-element view: build + diff + layout with one change {best:F3} ms")

/// A type and property used only by the next test: warnings are said once per process.
module private Twice =
    let width = Prop.define "TwiceWidth" 0.0 Affects.Measure
    let Type = Element.define "Twice" []

[<Fact>]
let ``a settings list that sets a property twice warns once in Debug builds`` () =
    let heard = Collections.Concurrent.ConcurrentQueue<string>()
    let listener = Action<string>(heard.Enqueue)
    DebugWarnings.Listener <- listener
    try
        for _ in 1..3 do
            View.Create(Twice.Type, [ Setting.create Twice.width 1.0; Setting.create Twice.width 2.0 ]) |> ignore
        View.Create(Twice.Type, [ Setting.create Twice.width 1.0 ]) |> ignore
    finally
        DebugWarnings.Listener <- null
    let mine = heard |> Seq.filter (fun m -> m.Contains "TwiceWidth") |> List.ofSeq
#if DEBUG
    Assert.Equal<string list>([ "Twice: its settings set TwiceWidth more than once; the last one wins." ], mine)
#else
    Assert.Empty mine
#endif
