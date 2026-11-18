/// Where an ItemsView scroll step's time goes (step 0 of "cheaper row reuse", 2026-11-17).
///
///     dotnet run -c Release --project tools/Scribble.Bench
///
/// Rows like the Gallery's 10,000 messages (a Border with wrapping text, real HarfBuzz shaping), in an
/// 800 × 600 window, scrolled in 48-DIP steps (about a wheel notch). Each scenario runs several times and
/// reports its best run (steady state: the code is warmed up first). Times come from `ItemsProfile`
/// (Core's internal counters, on only here).
module Program

open System
open System.Diagnostics
open Scribble.Rendering
open Scribble.Text
open Scribble.Text.HarfBuzz
open Scribble.Core

type Message = { Id: int; Text: string }

let private words =
    [| "layout"; "pixels"; "the"; "scroller"; "keeps"; "only"; "what"; "shows"; "and"; "a"; "little"; "more";
       "measured"; "as"; "it"; "scrolls"; "estimates"; "for"; "everything"; "else"; "GPU"; "frames"; "on"; "request" |]

/// The Gallery's message text (3 to about 60 words); `salt` gives other strings of the same lengths.
let private messageText (n: int) (salt: int) =
    let count = 3 + (n * 7919 % 13) * (1 + n % 5)
    let picked = [ for k in 0 .. count - 1 -> words[(n * 31 + k * 17 + salt) % words.Length] ]
    "#" + n.ToString("N0", Globalization.CultureInfo.InvariantCulture) + " " + String.Join(" ", picked) + " " + string salt

let private messages (n: int) (salt: int) = Array.init n (fun i -> { Id = i + 1; Text = messageText (i + 1) salt })

let private rowAlt = ThemeValue.define "RowAlt" (Color.Hex 0x2A2A42)

/// The Gallery's row.
let private row (m: Message) =
    Border.create [
        Border.padding (10.0, 6.0)
        Border.background (if m.Id % 2 = 0 then Palette.surface else rowAlt)
        Border.content (TextBlock.create [ TextBlock.text m.Text; TextBlock.textWrapping Wrap ])
    ]

/// The same row with a Button (theme styles, hover/pressed states) at the end.
let private rowWithButton (m: Message) =
    Border.create [
        Border.padding (10.0, 6.0)
        Border.background (if m.Id % 2 = 0 then Palette.surface else rowAlt)
        Border.content (
            DockPanel.create [
                DockPanel.children [
                    Button.create [ DockPanel.dock Dock.Right; Button.text "Reply" ]
                    TextBlock.create [ TextBlock.text m.Text; TextBlock.textWrapping Wrap ]
                ]
            ])
    ]

let private size = Size.Create(800.0, 600.0)

/// A tree with a ScrollViewer and an ItemsView of `items`.
type private Bench(text: TextSystem, themed: bool, rowView: Message -> View, items: Message[]) =
    let tree = ElementTree(text)
    let scroller = Scroller()
    let list = DrawList 1.0
    let mutable items = items
    let viewOf (ms: Message[]) =
        ScrollViewer.create [
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source ms
                    ItemsView.key (fun (m: Message) -> m.Id)
                    ItemsView.view rowView
                    ItemsView.scroller scroller
                ])
        ]
    do
        if themed then tree.Theme <- ValueSome(Theme.dark [])
        tree.Update(viewOf items)
        tree.UpdateLayout size

    member val LayoutTicks = 0L with get, set
    member val RenderTicks = 0L with get, set
    /// Elements whose restyle was still queued after their frame (they'd draw with stale or no styles
    /// until the next frame something else asks for).
    member val LeftOver = 0 with get, set

    /// One frame (layout, then the draw list), as ElementHost draws it; then any restyle left queued is
    /// counted and run (the next frame's work, not timed).
    member this.Frame() =
        let t0 = Stopwatch.GetTimestamp()
        tree.UpdateLayout size
        let t1 = Stopwatch.GetTimestamp()
        list.Reset()
        tree.Render list
        let t2 = Stopwatch.GetTimestamp()
        this.LayoutTicks <- this.LayoutTicks + (t1 - t0)
        this.RenderTicks <- this.RenderTicks + (t2 - t1)
        let before = ItemsProfile.Restyled
        let ticks = ItemsProfile.Restyle
        tree.RunScheduledStyles()
        this.LeftOver <- this.LeftOver + (ItemsProfile.Restyled - before)
        ItemsProfile.Restyle <- ticks

    member this.ScrollBy(dy: float) =
        scroller.ScrollBy(dy, false)
        this.Frame()

    member this.ScrollToTop() =
        scroller.ScrollToOffset(0.0, false)
        this.Frame()

    /// Appends one message (as the Gallery's "Add message" does) and draws.
    member this.Append(m: Message) =
        items <- Array.append items [| m |]
        tree.Update(viewOf items)
        this.Frame()

let private ms (ticks: int64) = float ticks * 1000.0 / float Stopwatch.Frequency
let private us (ticks: int64) = float ticks * 1_000_000.0 / float Stopwatch.Frequency
let private f1 (v: float) = v.ToString("F1", Globalization.CultureInfo.InvariantCulture)
let private f2 (v: float) = v.ToString("F2", Globalization.CultureInfo.InvariantCulture)
let private f3 (v: float) = v.ToString("F3", Globalization.CultureInfo.InvariantCulture)

type private Result =
    { Steps: int; Mean: float; P95: float; Max: float; AllocKB: float
      Layout: int64; Render: int64; LeftOver: int
      View: int64; Attach: int64; Patch: int64; Create: int64; Measure: int64; Release: int64; Range: int64; Restyle: int64
      Reused: int; Created: int; Repatched: int; Released: int; ThemeWalks: int; Restyled: int }

let private collect (b: Bench) (times: float[]) (alloc: int64) =
    let sorted = Array.sort times
    { Steps = times.Length; Mean = Array.average times; P95 = sorted[int (float sorted.Length * 0.95)]; Max = Array.max times
      AllocKB = float alloc / 1024.0 / float times.Length
      Layout = b.LayoutTicks; Render = b.RenderTicks; LeftOver = b.LeftOver
      View = ItemsProfile.View; Attach = ItemsProfile.Attach; Patch = ItemsProfile.Patch; Create = ItemsProfile.Create
      Measure = ItemsProfile.Measure; Release = ItemsProfile.Release; Range = ItemsProfile.Range; Restyle = ItemsProfile.Restyle
      Reused = ItemsProfile.Reused; Created = ItemsProfile.Created; Repatched = ItemsProfile.Repatched; Released = ItemsProfile.Released
      ThemeWalks = ItemsProfile.ThemeWalks; Restyled = ItemsProfile.Restyled }

/// Times `steps` calls of `step` from a clean slate of counters.
let private measure (b: Bench) (steps: int) (step: int -> unit) =
    b.LayoutTicks <- 0L
    b.RenderTicks <- 0L
    b.LeftOver <- 0
    ItemsProfile.Reset()
    let times = Array.zeroCreate steps
    let alloc0 = GC.GetAllocatedBytesForCurrentThread()
    for i in 0 .. steps - 1 do
        let t0 = Stopwatch.GetTimestamp()
        step i
        times[i] <- ms (Stopwatch.GetTimestamp() - t0)
    collect b times (GC.GetAllocatedBytesForCurrentThread() - alloc0)

let private report (name: string) (r: Result) =
    let n = float r.Steps
    let per (t: int64) = f1 (us t / n)
    let rows = r.Reused + r.Created
    Console.WriteLine()
    Console.WriteLine("== " + name)
    Console.WriteLine("  per step: mean " + f3 r.Mean + " ms, p95 " + f3 r.P95 + " ms, max " + f3 r.Max + " ms; allocated " + f1 r.AllocKB + " KB")
    Console.WriteLine("  layout " + per r.Layout + " us, draw list " + per r.Render + " us (per step)")
    Console.WriteLine("  inside layout, per step (us): view " + per r.View + ", attach " + per r.Attach + ", patch " + per r.Patch
                      + ", create " + per r.Create + ", measure " + per r.Measure + ", release " + per r.Release
                      + ", range bookkeeping " + per (r.Range - r.View - r.Attach - r.Patch - r.Create - r.Measure - r.Release)
                      + ", restyle " + per r.Restyle + ", rest of layout " + per (r.Layout - r.Range - r.Restyle))
    Console.WriteLine("  rows per step: " + f2 (float rows / n) + " realized (" + string r.Reused + " reused, " + string r.Created + " created), "
                      + f2 (float r.Released / n) + " released, " + f2 (float r.Repatched / n) + " patched again")
    if rows > 0 then
        Console.WriteLine("  per realized row (us): view " + f1 (us r.View / float rows) + ", attach " + f1 (us r.Attach / float rows)
                          + ", patch " + f1 (us r.Patch / float rows) + ", create " + f1 (us r.Create / float rows)
                          + ", measure " + f1 (us r.Measure / float rows))
    Console.WriteLine("  theme walks per step " + f1 (float r.ThemeWalks / n) + "; restyled per step " + f2 (float r.Restyled / n)
                      + "; left queued after their frame: " + string r.LeftOver + " (" + f2 (float r.LeftOver / n) + " per step)")

/// The best of `runs` (lowest mean), each run from `make`.
let private best (runs: int) (make: int -> Result) =
    [ for r in 1 .. runs -> make r ] |> List.minBy _.Mean

[<EntryPoint>]
let main _ =
#if DEBUG
    Console.WriteLine("Debug build: numbers are for Release only (dotnet run -c Release --project tools/Scribble.Bench).")
#endif
    use shaper = new HarfBuzzShaper()
    let text = TextSystem(GlyphAtlas(2048), shaper)
    let n, steps, step, runs = 10_000, 1000, 48.0, 5
    ItemsProfile.On <- true
    let sweep (b: Bench) = measure b steps (fun _ -> b.ScrollBy step)

    // Warm-up: JIT and tiering (two sweeps on strings no scenario uses).
    for w in 1 .. 2 do
        let b = Bench(text, true, row, messages n (900 + w))
        sweep b |> ignore
    Console.WriteLine("ItemsView scroll steps: " + string n + " messages, 800 x 600, " + string steps + " steps of " + string step + " DIPs, best of " + string runs)

    // 1. New text: the strings were never shaped (scrolling through new content).
    report "1. Gallery rows, new text (pooling on, theme)" (best runs (fun r -> sweep (Bench(text, true, row, messages n (100 + r)))))

    // 2. Same strings again: shaping is cached; what's left is the recycling itself.
    let warm (themed: bool) (view: Message -> View) (salt: int) =
        let b = Bench(text, themed, view, messages n salt)
        sweep b |> ignore
        b.ScrollToTop()
        sweep b
    report "2. Gallery rows, text seen before (pooling on, theme)" (best runs (fun _ -> warm true row 1))

    // 3. Creating every row instead of reusing.
    ItemsProfile.NoPool <- true
    report "3. Gallery rows, text seen before, NO pooling (every row created)" (best runs (fun _ -> warm true row 1))
    ItemsProfile.NoPool <- false

    // 4. Without a theme no styles are in use: no restyles, no theme lookups.
    report "4. Gallery rows, text seen before, no theme (styles not in use)" (best runs (fun _ -> warm false row 1))

    // 5. Rows with a themed Button (styles and states inside each row).
    report "5. Rows with a Button, text seen before (pooling on, theme)" (best runs (fun _ -> warm true rowWithButton 2))

    // 6. Small steps that stay inside the realized range: the cost of a step that realizes nothing new.
    report "6. Gallery rows, 4-DIP steps (mostly nothing new to realize)" (best runs (fun _ ->
        let b = Bench(text, true, row, messages n 1)
        b.ScrollBy 20_000.0
        measure b steps (fun i -> b.ScrollBy(if (i / 25) % 2 = 0 then 4.0 else -4.0))))

    // 7. "Add message": a new items array each time; every realized row is patched again.
    report "7. Add a message (new source array; scrolled to the middle)" (best runs (fun _ ->
        let b = Bench(text, true, row, messages n 1)
        b.ScrollBy 200_000.0
        measure b 200 (fun i -> b.Append { Id = n + 1 + i; Text = messageText (n + 1 + i) 1 })))
    0
