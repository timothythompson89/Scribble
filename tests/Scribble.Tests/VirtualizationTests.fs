/// M8 step 1b: ItemsView with items of very different heights. Every scenario checks the same
/// invariants after every step (`Harness.Check`): realized items back to back, the viewport covered,
/// the realized count bounded by the viewport (not the item count), the reported first visible item
/// really at the top, the offset inside the content. The scroll anchor (the first visible item, by
/// key) keeps its place on screen while sizes or items above it change.
module Scribble.Tests.VirtualizationTests

open System
open System.Diagnostics
open Xunit
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Tests.TestShaping

/// A row: a box `Height` tall, or (with `Text`) wrapping text; `Hidden` rows have `isVisible false`.
type Row = { Id: int; Height: float; Text: string; Hidden: bool }

let private row id height = { Id = id; Height = height; Text = ""; Hidden = false }

/// Heights from 20 to 400, with a 3,000-high outlier about one row in 200 (seeded).
let private mixed (seed: int) (n: int) =
    let r = Random(seed)
    Array.init n (fun i -> row i (if r.Next 200 = 0 then 3000.0 else float (r.Next(20, 401))))

let private sentence = "The quick brown fox jumps over the lazy dog. "

/// A ScrollViewer (`width` × `height`) holding an ItemsView of `rows`.
type private Harness(initial: Row[], ?width: float, ?height: float) =
    let mutable rows = initial
    let mutable width = defaultArg width 300.0
    let height = defaultArg height 200.0
    let scroller = Scroller()
    let tree = ElementTree(textSystem ())
    let viewOf (rows: Row[]) =
        ScrollViewer.create [
            ScrollViewer.content (
                ItemsView.create [
                    ItemsView.source rows
                    ItemsView.key (fun (r: Row) -> r.Id)
                    ItemsView.view (fun (r: Row) ->
                        if r.Text <> "" then
                            TextBlock.create [ TextBlock.name (string r.Id); TextBlock.text r.Text; TextBlock.textWrapping Wrap ]
                        else
                            Border.create [ Border.name (string r.Id); Border.height r.Height; Border.isVisible (not r.Hidden) ])
                    ItemsView.scroller scroller
                ])
        ]
    do
        tree.Update(viewOf rows)
        tree.UpdateLayout(Size.Create(width, height))

    member _.Tree = tree
    member _.Scroller = scroller
    member _.Viewer = tree.Root.Value
    member this.Items = this.Viewer.Children[0]
    member _.Height = height
    member _.Rows = rows
    member _.Layout() = tree.UpdateLayout(Size.Create(width, height))

    member this.SetRows(next: Row[]) =
        rows <- next
        tree.Update(viewOf next)
        this.Layout()

    member this.SetWidth(w: float) =
        width <- w
        this.Layout()

    member this.ScrollBy(dy: float) =
        scroller.ScrollBy(dy, false)
        this.Layout()

    member this.ScrollToIndex(i: int, align: ScrollAlign) =
        scroller.ScrollToIndex(i, align, false)
        this.Layout()

    /// The ScrollViewer's vertical offset.
    member this.Offset = let struct (_, y, _, _) = ScrollViewer.viewportOf this.Viewer in y

    /// Realized items in order: (row index, top and bottom on screen, element).
    member this.Realized =
        let index = Collections.Generic.Dictionary<int, int>()
        rows |> Array.iteri (fun i r -> index[r.Id] <- i)
        [ for c in this.Items.Children ->
              let top = c.PositionInTree.Y - this.Viewer.PositionInTree.Y
              let h = if c.IsVisible then c.Bounds.Height else 0.0
              struct (index[int c.Name], top, top + h, c) ]
        |> List.sortBy (fun (struct (i, _, _, _)) -> i)

    /// The on-screen top of the row with this id (it must be realized).
    member this.TopOf(id: int) =
        this.Realized |> List.pick (fun (struct (_, top, _, c)) -> if c.Name = string id then Some top else None)

    /// The id of the row at the viewport's top (the first visible item).
    member this.FirstVisibleId =
        this.Realized
        |> List.pick (fun (struct (i, top, bottom, _)) -> if top <= 0.001 && bottom > 0.001 then Some rows[i].Id else None)

    /// The invariants, after any step. `maxRealized`: what the viewport can need (the default is
    /// generous for 200 DIPs and 20-DIP rows).
    member this.Check(what: string, ?maxRealized: int) =
        // Rows are drawn on whole pixels while the offset may be fractional: positions compared
        // with the offset may differ by up to half a pixel.
        let px = 0.51
        let fail (message: string) = Assert.Fail(what + ": " + message)
        let n = rows.Length
        let realized = this.Realized
        let y = this.Offset
        let contentHeight = this.Items.Bounds.Height
        // The offset is inside the content.
        if y < -0.01 then fail $"offset {y:F2} is above the content"
        if y > max 0.0 (contentHeight - height) + 0.01 then fail $"offset {y:F2} is past the end ({contentHeight:F2} high)"
        if n > 0 then
            if realized.IsEmpty then fail "nothing realized"
            // Realized items are a run of consecutive rows, back to back.
            for (struct (i, _, bottom, _)), (struct (j, top, _, _)) in List.pairwise realized do
                if j <> i + 1 then fail $"rows {i} and {j} are realized without the ones between"
                if abs (top - bottom) > 0.01 then fail $"row {j} starts at {top:F2}, row {i} ends at {bottom:F2}"
            // The viewport is covered.
            let struct (first, firstTop, _, _) = List.head realized
            let struct (last, _, lastBottom, _) = List.last realized
            if first > 0 && firstTop > px then fail $"a gap above row {first} (it starts at {firstTop:F2})"
            if first = 0 && abs (firstTop + y) > px then fail $"row 0 starts at {firstTop:F2} with the offset at {y:F2}"
            if last < n - 1 && lastBottom < height - px then fail $"a gap below row {last} (it ends at {lastBottom:F2})"
            // Bounded by the viewport, not the item count.
            let extra = 3.0 * max 100.0 (height / 2.0) + 1.0
            for struct (i, top, bottom, _) in realized do
                if bottom < -extra || top > height + extra then fail $"row {i} is realized at {top:F2}, far outside the viewport"
            let limit = defaultArg maxRealized 120
            if realized.Length > limit then fail $"{realized.Length} rows realized (at most {limit} expected)"
            // The reported first visible item is the one at the top, at the right offset.
            match this.Scroller.FirstVisible with
            | ValueSome p ->
                let id = this.FirstVisibleId
                let reported = p.Key :?> int
                let realizedIds = realized |> List.map (fun (struct (i, _, _, _)) -> rows[i].Id)
                if not (List.contains reported realizedIds) then fail $"first visible reported as {reported}, which isn't realized"
                let top = this.TopOf reported
                if reported <> id && not (abs top < px || abs (top + (this.Realized |> List.pick (fun (struct (i, t, b, _)) -> if rows[i].Id = reported then Some(b - t) else None))) < px) then
                    fail $"first visible reported as {reported} (at {top:F2}), but row id {id} is at the top"
                if abs (p.Offset + top) > px then fail $"first visible offset {p.Offset:F2}, but it starts at {top:F2}"
            | ValueNone -> fail "no first visible item reported"

    /// Once every row has been measured, the content is exactly as high as the rows.
    member this.CheckExactHeight(what: string) =
        let sum = rows |> Array.sumBy (fun r -> if r.Hidden then 0.0 else r.Height)
        Assert.True(abs (this.Items.Bounds.Height - sum) < 0.01, $"{what}: content {this.Items.Bounds.Height:F2} high, rows add up to {sum:F2}")

// --- a. Mixed heights, down and back up --------------------------------------------------------

[<Fact>]
let ``mixed heights: scrolling to the bottom and back keeps every invariant`` () =
    let h = Harness(mixed 1 300)
    h.Check "start"
    let mutable step = 0
    while h.Offset < h.Items.Bounds.Height - h.Height - 0.01 do
        h.ScrollBy 97.0
        step <- step + 1
        h.Check $"down, step {step}"
    Assert.Equal(299, (let struct (i, _, _, _) = List.last h.Realized in i))
    h.CheckExactHeight "at the bottom"
    while h.Offset > 0.0 do
        h.ScrollBy -83.0
        step <- step + 1
        h.Check $"up, step {step}"
    Assert.Equal(0, h.FirstVisibleId)
    Assert.Equal(0.0, h.TopOf 0, 6)

// --- b. A wrong first estimate ------------------------------------------------------------------

[<Fact>]
let ``small rows first, then large: scrolling to the end shows the last row flush with the bottom`` () =
    let h = Harness(Array.init 2000 (fun i -> row i (if i < 50 then 20.0 else 300.0)))
    h.Check "start"
    h.ScrollToIndex(1999, ScrollAlign.End)
    h.Check "at the end"
    Assert.Equal(h.Height, (let struct (_, _, bottom, _) = List.last h.Realized in bottom), 2)
    Assert.Equal(h.Items.Bounds.Height - h.Height, h.Offset, 2)

[<Fact>]
let ``small rows first, then large: the content height settles to the exact sum`` () =
    let h = Harness(Array.init 400 (fun i -> row i (if i < 50 then 20.0 else 300.0)))
    let mutable step = 0
    while h.Offset < h.Items.Bounds.Height - h.Height - 0.01 do
        h.ScrollBy 180.0
        step <- step + 1
        h.Check $"step {step}"
    h.CheckExactHeight "at the bottom"

// --- c. Deep jump, then all the way up -----------------------------------------------------------

[<Fact>]
let ``after a deep jump, scrolling up all the way puts row 0 at the top`` () =
    let h = Harness(mixed 2 2000)
    h.ScrollToIndex(1500, ScrollAlign.Start)
    h.Check "after the jump"
    Assert.Equal(1500, h.FirstVisibleId)
    let mutable step = 0
    while h.Offset > 0.0 && step < 10_000 do
        h.ScrollBy -180.0
        step <- step + 1
        h.Check $"up, step {step}"
    Assert.Equal(0, h.FirstVisibleId)
    Assert.Equal(0.0, h.TopOf 0, 6)
    Assert.Equal(0.0, h.Offset)

[<Fact>]
let ``after a jump to 9,000, scrolling to the top shows row 0 at 0`` () =
    let h = Harness(mixed 3 10_000)
    h.ScrollToIndex(9000, ScrollAlign.Start)
    h.Check "after the jump"
    h.Scroller.ScrollToTop false
    h.Layout()
    h.Check "at the top"
    Assert.Equal(0, h.FirstVisibleId)
    Assert.Equal(0.0, h.TopOf 0, 6)
    Assert.Equal(0.0, h.Offset)

// --- d. A row taller than the viewport ---------------------------------------------------------

[<Fact>]
let ``a row taller than the viewport stays realized while it shows, and its neighbours appear on time`` () =
    let rows = Array.init 60 (fun i -> row i (if i = 20 then 3000.0 else 40.0))
    let h = Harness rows
    let mutable step = 0
    while h.Offset < h.Items.Bounds.Height - h.Height - 0.01 do
        h.ScrollBy 50.0
        step <- step + 1
        h.Check $"step {step}"
        if h.Offset > 800.0 && h.Offset < 800.0 + 3000.0 - h.Height then Assert.Equal(20, h.FirstVisibleId)

// --- e. A row changes size after it's shown -------------------------------------------------------

let private resized (rows: Row[]) (id: int) (height: float) =
    rows |> Array.map (fun r -> if r.Id = id then { r with Height = height } else r)

[<Fact>]
let ``a realized row above the viewport grows or shrinks: what shows stays put`` () =
    let h = Harness(Array.init 500 (fun i -> row i 40.0))
    h.ScrollToIndex(100, ScrollAlign.Start)
    h.ScrollBy 15.0
    let anchor, top = h.FirstVisibleId, h.TopOf h.FirstVisibleId
    for height in [ 200.0; 5.0; 0.0; 40.0 ] do
        h.SetRows(resized h.Rows 98 height) // in the overscan above
        h.Check $"row 98 at {height}"
        Assert.Equal(anchor, h.FirstVisibleId)
        Assert.True(abs (h.TopOf anchor - top) < 0.51, $"the first visible row moved from {top} to {h.TopOf anchor}")

[<Fact>]
let ``the first visible row grows or shrinks: its top stays put`` () =
    let h = Harness(Array.init 500 (fun i -> row i 40.0))
    h.ScrollToIndex(100, ScrollAlign.Start)
    h.ScrollBy 15.0
    let anchor, top = h.FirstVisibleId, h.TopOf h.FirstVisibleId
    for height in [ 300.0; 30.0; 40.0 ] do
        h.SetRows(resized h.Rows anchor height)
        h.Check $"first visible at {height}"
        Assert.True(abs (h.TopOf anchor - top) < 0.51, $"the first visible row moved from {top} to {h.TopOf anchor}")

[<Fact>]
let ``a row below the first visible grows: what shows above it stays put`` () =
    let h = Harness(Array.init 500 (fun i -> row i 40.0))
    h.ScrollToIndex(100, ScrollAlign.Start)
    h.SetRows(resized h.Rows 102 400.0)
    h.Check "row 102 grown"
    // The rows above 100 were never measured: the new average moves their estimate (and the
    // offset), but row 100 stays at the top.
    Assert.Equal(100, h.FirstVisibleId)
    Assert.Equal(0.0, h.TopOf 100, 6)
    Assert.Equal(40.0, h.TopOf 101, 6)

// --- f. Rows inserted or removed ------------------------------------------------------------------

let private insertAt (rows: Row[]) (index: int) (added: Row[]) =
    Array.concat [ rows[.. index - 1]; added; rows[index ..] ]

let private newRows (firstId: int) (count: int) (height: float) = Array.init count (fun i -> row (firstId + i) height)

[<Fact>]
let ``rows inserted above the viewport: the first visible row stays put`` () =
    let h = Harness(mixed 4 500)
    h.ScrollToIndex(200, ScrollAlign.Start)
    h.ScrollBy 25.0
    let anchor, top = h.FirstVisibleId, h.TopOf h.FirstVisibleId
    // Near (realized, in the overscan) and far (estimated) above.
    for at, count in [ 199, 3; 0, 50; 150, 1 ] do
        h.SetRows(insertAt h.Rows at (newRows (10_000 + h.Rows.Length) count 77.0))
        h.Check $"{count} inserted at {at}"
        Assert.Equal(anchor, h.FirstVisibleId)
        Assert.True(abs (h.TopOf anchor - top) < 0.51, $"the first visible row moved from {top} to {h.TopOf anchor}")

[<Fact>]
let ``rows removed above the viewport: the first visible row stays put`` () =
    let h = Harness(mixed 5 500)
    h.ScrollToIndex(200, ScrollAlign.Start)
    h.ScrollBy 25.0
    let anchor, top = h.FirstVisibleId, h.TopOf h.FirstVisibleId
    for at, count in [ 198, 2; 0, 40; 100, 1 ] do
        h.SetRows(Array.append h.Rows[.. at - 1] h.Rows[at + count ..])
        h.Check $"{count} removed at {at}"
        Assert.Equal(anchor, h.FirstVisibleId)
        Assert.True(abs (h.TopOf anchor - top) < 0.51, $"the first visible row moved from {top} to {h.TopOf anchor}")

[<Fact>]
let ``rows inserted or removed inside or below the viewport: the first visible row stays put`` () =
    let h = Harness(mixed 6 500)
    h.ScrollToIndex(200, ScrollAlign.Start)
    let anchor, top = h.FirstVisibleId, h.TopOf h.FirstVisibleId
    let index () = h.Rows |> Array.findIndex (fun r -> r.Id = anchor)
    h.SetRows(insertAt h.Rows (index () + 1) (newRows 20_000 2 60.0))
    h.Check "inserted just after the first visible"
    h.SetRows(insertAt h.Rows (index () + 40) (newRows 20_100 5 60.0))
    h.Check "inserted below"
    h.SetRows(Array.append h.Rows[.. index ()] h.Rows[index () + 2 ..])
    h.Check "removed just after the first visible"
    Assert.Equal(anchor, h.FirstVisibleId)
    Assert.True(abs (h.TopOf anchor - top) < 0.51, $"the first visible row moved from {top} to {h.TopOf anchor}")

[<Fact>]
let ``the first visible row itself removed: what was below takes its place`` () =
    let h = Harness(Array.init 500 (fun i -> row i 40.0))
    h.ScrollToIndex(200, ScrollAlign.Start)
    h.SetRows(h.Rows |> Array.filter (fun r -> r.Id <> 200))
    h.Check "removed"
    Assert.Equal(201, h.FirstVisibleId)
    Assert.Equal(0.0, h.TopOf 201, 6)

// --- g. Width changes ---------------------------------------------------------------------------------

[<Fact>]
let ``a width change re-wraps every row and keeps the first visible row at the top`` () =
    let r = Random 7
    let rows = Array.init 400 (fun i -> { Id = i; Height = 0.0; Text = String.replicate (r.Next(1, 12)) sentence; Hidden = false })
    let h = Harness(rows, width = 400.0)
    h.ScrollToIndex(150, ScrollAlign.Start)
    h.ScrollBy 10.0
    for w in [ 250.0; 600.0; 180.0; 400.0 ] do
        let anchor = h.FirstVisibleId
        h.SetWidth w
        h.Check $"at width {w}"
        Assert.Equal(anchor, h.FirstVisibleId)

// --- h. Zero-height and hidden rows -------------------------------------------------------------

/// A trace listener that keeps what's written (the debug-build warnings).
type private Collector() =
    inherit TraceListener()
    member val Lines = Collections.Generic.List<string>()
    override this.Write(message: string) = this.Lines.Add message
    override this.WriteLine(message: string) = this.Lines.Add message

/// Decided in M8 step 1b (Tim, option A): a long run of zero-height rows is realized all at once
/// (each starts above the viewport's bottom); hidden items belong out of the source. Debug builds
/// say so. The invariants other than the count still hold.
[<Fact>]
let ``a long run of zero-height rows is realized at once, with a warning in debug builds`` () =
    let collector = new Collector()
    Trace.Listeners.Add collector |> ignore
    try
        let rows = Array.init 3_020 (fun i -> row i (if i >= 10 && i < 3_010 then 0.0 else 40.0))
        let h = Harness rows
        h.Check("start", 4_000)
        h.ScrollBy 300.0
        h.Check("past the run", 4_000)
        Assert.True(h.Realized.Length > 3_000)
#if DEBUG
        Assert.Contains(collector.Lines, fun l -> l.Contains "items realized at once" && l.Contains "filter hidden items out of the source")
#endif
    finally
        Trace.Listeners.Remove collector

[<Fact>]
let ``hidden rows take no room and the invariants hold around them`` () =
    let r = Random 8
    let rows = Array.init 600 (fun i -> { row i (float (r.Next(20, 120))) with Hidden = r.Next 4 = 0 })
    let h = Harness rows
    let mutable step = 0
    while h.Offset < h.Items.Bounds.Height - h.Height - 0.01 do
        h.ScrollBy 70.0
        step <- step + 1
        h.Check $"step {step}"
    h.CheckExactHeight "at the bottom"

// --- i. Random sequences ---------------------------------------------------------------------------

[<Theory>]
[<InlineData(11)>]
[<InlineData(12)>]
[<InlineData(13)>]
let ``random scrolling, jumps, inserts, removals, resizes and width changes`` (seed: int) =
    let r = Random seed
    let h = Harness(mixed seed 3000)
    let mutable nextId = 100_000
    for step in 1..300 do
        let n = h.Rows.Length
        let what =
            match r.Next 10 with
            | 0 | 1 | 2 ->
                let dy = float (r.Next(-400, 401))
                h.ScrollBy dy
                $"scroll by {dy}"
            | 3 ->
                let i = r.Next n
                let align = if r.Next 2 = 0 then ScrollAlign.Start else ScrollAlign.End
                h.ScrollToIndex(i, align)
                $"jump to {i} ({align})"
            | 4 ->
                let at, count = r.Next n, r.Next(1, 6)
                h.SetRows(insertAt h.Rows at (newRows nextId count (float (r.Next(0, 300)))))
                nextId <- nextId + count
                $"insert {count} at {at}"
            | 5 when n > 10 ->
                let at = r.Next(n - 5)
                let count = r.Next(1, 5)
                h.SetRows(Array.append h.Rows[.. at - 1] h.Rows[at + count ..])
                $"remove {count} at {at}"
            | 6 ->
                let id = h.Rows[r.Next n].Id
                let height = float (r.Next(0, 500))
                h.SetRows(resized h.Rows id height)
                $"row id {id} to {height}"
            | 7 ->
                let w = float (r.Next(150, 700))
                h.SetWidth w
                $"width {w}"
            | _ ->
                h.Scroller.ScrollToTop false
                h.Layout()
                // Exactly 0, even after fractional offsets (the content was arranged on whole pixels).
                Assert.Equal(0.0, h.Offset)
                "to the top"
        h.Check($"seed {seed}, step {step} ({what})", 200)

// --- j. Work proportional to what's visible --------------------------------------------------------

[<Fact>]
let ``mixed heights: a scroll step costs about the same for 100 and 10,000 items`` () =
    let measure (n: int) =
        let h = Harness(mixed 9 n)
        let mutable offset = 0.0
        let step () =
            // Down and back in the first part, which both lists have (100 rows are about 21,000 high).
            offset <- (if offset >= 9000.0 then 1000.0 else offset + 137.0)
            ScrollViewer.jumpTo h.Viewer offset
            h.Layout()
        for _ in 1..120 do step ()
        let best (f: unit -> unit) = [ for _ in 1..60 -> (let sw = Stopwatch.StartNew() in f (); sw.Elapsed.TotalMilliseconds) ] |> List.min
        let layout = best step
        layout, best (fun () -> h.Tree.Render(DrawList 1.0)), (match ItemsView.realizedCount h.Items with ValueSome(struct (c, _)) -> c | _ -> 0)
    let smallLayout, smallDraw, smallRealized = measure 100
    let bigLayout, bigDraw, bigRealized = measure 10_000
    Assert.True(bigLayout <= 1.5 * smallLayout + 0.02, $"layout: 10,000 rows {bigLayout:F4} ms, 100 rows {smallLayout:F4} ms")
    Assert.True(bigDraw <= 1.5 * smallDraw + 0.02, $"draw list: 10,000 rows {bigDraw:F4} ms, 100 rows {smallDraw:F4} ms")
    Assert.Equal(smallRealized, bigRealized)
