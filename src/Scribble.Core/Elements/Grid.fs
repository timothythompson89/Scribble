namespace Scribble.Core

open System
open Scribble.Rendering

/// A Grid row's or column's size: a `Length` with limits in DIPs.
type GridTrack =
    { Size: Length
      Min: float
      Max: float }

    static member Of(size: Length) = { Size = size; Min = 0.0; Max = Double.PositiveInfinity }

    override t.ToString() =
        let n (v: float) = v.ToString("0.###", Globalization.CultureInfo.InvariantCulture)
        let limits =
            (if t.Min > 0.0 then " min " + n t.Min else "")
            + (if Double.IsPositiveInfinity t.Max then "" else " max " + n t.Max)
        t.Size.ToString() + limits

/// One column of a Grid, made with `GridColumn.create [ ... ]`.
type GridColumnDefinition = internal { Column: GridTrack }

/// One row of a Grid, made with `GridRow.create [ ... ]`.
type GridRowDefinition = internal { Row: GridTrack }

/// One setting of a Grid column (`GridColumn.width "*"`).
[<Struct>]
type GridColumnSetting = internal GridColumnSetting of (GridTrack -> GridTrack)

/// One setting of a Grid row (`GridRow.height "auto"`).
[<Struct>]
type GridRowSetting = internal GridRowSetting of (GridTrack -> GridTrack)

/// Settings for one Grid column: `GridColumn.create [ GridColumn.width "*"; GridColumn.minWidth 120.0 ]`.
[<AbstractClass; Sealed>]
type GridColumn =
    /// Fixed DIPs.
    static member width(value: float) = GridColumnSetting(fun t -> { t with Size = Px value })
    /// Auto, Px, Percent (of the Grid's width) or Star (a weighted share of what's left).
    static member width(value: Length) = GridColumnSetting(fun t -> { t with Size = value })
    /// "auto", "200", "200px", "25%", "*" or "2*".
    static member width(value: string) = GridColumnSetting(fun t -> { t with Size = Length.parse value })
    static member minWidth(value: float) = GridColumnSetting(fun t -> { t with Min = value })
    static member maxWidth(value: float) = GridColumnSetting(fun t -> { t with Max = value })

/// Settings for one Grid row: `GridRow.create [ GridRow.height "auto"; GridRow.maxHeight 300.0 ]`.
[<AbstractClass; Sealed>]
type GridRow =
    static member height(value: float) = GridRowSetting(fun t -> { t with Size = Px value })
    /// Auto, Px, Percent (of the Grid's height) or Star (a weighted share of what's left).
    static member height(value: Length) = GridRowSetting(fun t -> { t with Size = value })
    /// "auto", "200", "200px", "25%", "*" or "2*".
    static member height(value: string) = GridRowSetting(fun t -> { t with Size = Length.parse value })
    static member minHeight(value: float) = GridRowSetting(fun t -> { t with Min = value })
    static member maxHeight(value: float) = GridRowSetting(fun t -> { t with Max = value })

module GridColumn =
    /// A column (a star share, "*", unless `GridColumn.width` says otherwise).
    let create (settings: GridColumnSetting list) : GridColumnDefinition =
        { Column = settings |> List.fold (fun t (GridColumnSetting f) -> f t) (GridTrack.Of(Star 1.0)) }

module GridRow =
    /// A row (a star share, "*", unless `GridRow.height` says otherwise).
    let create (settings: GridRowSetting list) : GridRowDefinition =
        { Row = settings |> List.fold (fun t (GridRowSetting f) -> f t) (GridTrack.Of(Star 1.0)) }

module GridProps =
    let columns: Prop<GridTrack list> = Prop.define "Columns" [] Affects.Measure
    let rows: Prop<GridTrack list> = Prop.define "Rows" [] Affects.Measure
    let rowSpacing = Prop.define "RowSpacing" 0.0 Affects.Measure
    let columnSpacing = Prop.define "ColumnSpacing" 0.0 Affects.Measure
    /// Attached: the row a child starts in (0-based).
    let row = Prop.attached "Grid" "Row" 0 Affects.Measure
    let column = Prop.attached "Grid" "Column" 0 Affects.Measure
    let rowSpan = Prop.attached "Grid" "RowSpan" 1 Affects.Measure
    let columnSpan = Prop.attached "Grid" "ColumnSpan" 1 Affects.Measure
    /// Attached: row and column together (`Grid.cell (1, 2)`); wins over `row` and `column`.
    let cell: Prop<struct (int * int) voption> = Prop.attached "Grid" "Cell" ValueNone Affects.Measure

/// Lays children out in rows and columns. Define them with `Grid.columns "auto, *, 200"` (or, for
/// limits, `Grid.columns [ GridColumn.create [ GridColumn.width "*"; GridColumn.minWidth 120.0 ] ]`)
/// and place children with attached settings: `Button.create [ Grid.row 1; Grid.column 2 ]`,
/// `Grid.cell (1, 2)`, `Grid.columnSpan 2`. Children without a place go in the first cell.
///
/// Sizing (WPF's rules, simplified): fixed and Auto tracks first (an Auto track is as big as the
/// largest child in it; a child that also spans a star track leaves Auto tracks alone), then
/// star tracks share what's left by weight, held within their min/max. In unlimited space (a
/// ScrollViewer's direction) stars act as Auto. `Percent` is a share of the Grid's own size. Row and
/// column lines land on whole physical pixels, so cells tile with no gaps. With no definitions a
/// Grid is one cell: its children overlap (a badge over an icon), ordered by `zIndex`.
[<AbstractClass>]
type Grid =
    inherit Panel

    static member internal Parse (what: string) (spec: string) : GridTrack list =
        if String.IsNullOrWhiteSpace spec then []
        else
            [ for part in spec.Split ',' ->
                  match Length.tryParse part with
                  | ValueSome l -> GridTrack.Of l
                  | ValueNone ->
                      invalidArg what ("'" + part.Trim() + "' in \"" + spec + "\" isn't a size. Use \"auto\", a number of DIPs (\"200\"), a percentage (\"25%\") or a star share (\"*\", \"2*\"), separated by commas.") ]

    /// Columns from sizes separated by commas: "auto, *, 200" ("2*" for a double share, "25%").
    static member columns<'k when 'k :> Grid>(spec: string) : Setting<'k> = Setting.create GridProps.columns (Grid.Parse "columns" spec)
    /// Columns with limits: `[ GridColumn.create [ GridColumn.width "*"; GridColumn.minWidth 120.0 ] ]`.
    static member columns<'k when 'k :> Grid>(definitions: GridColumnDefinition list) : Setting<'k> =
        Setting.create GridProps.columns [ for d in definitions -> d.Column ]
    /// Rows from sizes separated by commas: "auto, *, 40".
    static member rows<'k when 'k :> Grid>(spec: string) : Setting<'k> = Setting.create GridProps.rows (Grid.Parse "rows" spec)
    /// Rows with limits: `[ GridRow.create [ GridRow.height "*"; GridRow.maxHeight 300.0 ] ]`.
    static member rows<'k when 'k :> Grid>(definitions: GridRowDefinition list) : Setting<'k> =
        Setting.create GridProps.rows [ for d in definitions -> d.Row ]
    /// The gap between rows (DIPs).
    static member rowSpacing<'k when 'k :> Grid>(value: float) : Setting<'k> = Setting.create GridProps.rowSpacing value
    /// The gap between columns (DIPs).
    static member columnSpacing<'k when 'k :> Grid>(value: float) : Setting<'k> = Setting.create GridProps.columnSpacing value
    /// Attached, on a child: the row it's in (0-based).
    static member row<'k when 'k :> Layoutable>(value: int) : Setting<'k> = Setting.create GridProps.row value
    /// Attached, on a child: the column it's in (0-based).
    static member column<'k when 'k :> Layoutable>(value: int) : Setting<'k> = Setting.create GridProps.column value
    /// Attached, on a child: how many rows it covers (default 1).
    static member rowSpan<'k when 'k :> Layoutable>(value: int) : Setting<'k> = Setting.create GridProps.rowSpan value
    /// Attached, on a child: how many columns it covers (default 1).
    static member columnSpan<'k when 'k :> Layoutable>(value: int) : Setting<'k> = Setting.create GridProps.columnSpan value
    /// Attached, on a child: its row and column at once.
    static member cell<'k when 'k :> Layoutable>(row: int, column: int) : Setting<'k> = Setting.create GridProps.cell (ValueSome(struct (row, column)))

module Grid =
    [<Struct>]
    type private Kind =
        | Fixed
        | AutoTrack
        | StarTrack of weight: float

    let private clamp (v: float) (low: float) (high: float) = max low (min v high)

    /// Whether the last measure had limited space along each axis (else stars and percentages act as Auto).
    let private boundedWidth = Prop.local "GridBoundedWidth" false
    let private boundedHeight = Prop.local "GridBoundedHeight" false

    /// The tracks of an axis (one star track when none are defined).
    let private tracksOf (defined: GridTrack list) =
        if defined.IsEmpty then [| GridTrack.Of(Star 1.0) |] else Array.ofList defined

    /// A child's first row, first column and spans, within the grid.
    let internal placement (child: Element) (rowCount: int) (columnCount: int) =
        let r, c =
            match child.Get GridProps.cell with
            | ValueSome(struct (r, c)) -> r, c
            | ValueNone -> child.Get GridProps.row, child.Get GridProps.column
        let r = Math.Clamp(r, 0, rowCount - 1)
        let c = Math.Clamp(c, 0, columnCount - 1)
        let rs = Math.Clamp(child.Get GridProps.rowSpan, 1, rowCount - r)
        let cs = Math.Clamp(child.Get GridProps.columnSpan, 1, columnCount - c)
        struct (r, c, rs, cs)

    /// Sizes one axis. `spans` are the children's (first track, track count) on it, `desire i` the
    /// length child i wants (margin included). Stars share `available` (finite) by weight within
    /// their limits; with `bounded` false they act as Auto, and so do percentages.
    let private solve (tracks: GridTrack[]) (available: float) (bounded: bool) (spacing: float)
                      (spans: struct (int * int)[]) (desire: int -> float) =
        let n = tracks.Length
        let kinds = Array.zeroCreate<Kind> n
        let sizes = Array.zeroCreate<float> n
        for i in 0 .. n - 1 do
            let t = tracks[i]
            match t.Size with
            | Px v ->
                kinds[i] <- Fixed
                sizes[i] <- clamp v t.Min t.Max
            | Percent p when bounded ->
                kinds[i] <- Fixed
                sizes[i] <- clamp (available * p / 100.0) t.Min t.Max
            | Star w when bounded && w > 0.0 ->
                kinds[i] <- StarTrack w
                sizes[i] <- 0.0
            | _ ->
                kinds[i] <- AutoTrack
                sizes[i] <- t.Min
        // Auto tracks grow to fit the children in them, single-track children first. A child that
        // also spans a star track doesn't: the stars take up what it needs (as in CSS Grid), so long
        // content spanning into an Auto column can't push the Grid past its space.
        let order = Array.init spans.Length id |> Array.sortBy (fun i -> let struct (_, count) = spans[i] in count)
        for i in order do
            let struct (first, count) = spans[i]
            let mutable autos = 0
            let mutable stars = false
            let mutable taken = spacing * float (count - 1)
            for k in first .. first + count - 1 do
                match kinds[k] with
                | AutoTrack ->
                    autos <- autos + 1
                    taken <- taken + sizes[k]
                | Fixed -> taken <- taken + sizes[k]
                | StarTrack _ -> stars <- true
            if autos > 0 && not stars then
                let need = desire i - taken
                if need > 0.0 then
                    // Spread over the Auto tracks it covers, each within its maximum.
                    let mutable left = need
                    let mutable open' = autos
                    for k in first .. first + count - 1 do
                        if kinds[k] = AutoTrack && left > 0.0 then
                            let share = left / float open'
                            let grown = min (sizes[k] + share) tracks[k].Max
                            left <- left - (grown - sizes[k])
                            sizes[k] <- grown
                            open' <- open' - 1
        // Stars share what's left; one held at its min or max leaves the rest to the others.
        let mutable leftover = available - spacing * float (max 0 (n - 1))
        let isStar k = match kinds[k] with StarTrack _ -> true | _ -> false
        for k in 0 .. n - 1 do
            if not (isStar k) then leftover <- leftover - sizes[k]
        let active = Collections.Generic.List<int>()
        for k in 0 .. n - 1 do
            if isStar k then active.Add k
        let mutable settled = false
        while active.Count > 0 && not settled do
            let room = max 0.0 leftover
            let total = active |> Seq.sumBy (fun k -> match kinds[k] with StarTrack w -> w | _ -> 0.0)
            let share k = match kinds[k] with StarTrack w -> room * w / total | _ -> 0.0
            let under = active |> Seq.filter (fun k -> share k < tracks[k].Min) |> List.ofSeq
            let over = active |> Seq.filter (fun k -> share k > tracks[k].Max) |> List.ofSeq
            let held, value = if not under.IsEmpty then under, (fun k -> tracks[k].Min) elif not over.IsEmpty then over, (fun k -> tracks[k].Max) else [], (fun _ -> 0.0)
            if held.IsEmpty then
                for k in active do
                    sizes[k] <- share k
                settled <- true
            else
                for k in held do
                    sizes[k] <- value k
                    leftover <- leftover - sizes[k]
                    active.Remove k |> ignore
        struct (kinds, sizes)

    /// The axis's natural length: stars at the size their content needs (by weight), not their share.
    let private natural (kinds: Kind[]) (sizes: float[]) (tracks: GridTrack[]) (spacing: float)
                        (spans: struct (int * int)[]) (desire: int -> float) (available: float) =
        let mutable unit = 0.0
        for i in 0 .. spans.Length - 1 do
            let struct (first, count) = spans[i]
            let mutable weights = 0.0
            let mutable taken = spacing * float (count - 1)
            for k in first .. first + count - 1 do
                match kinds[k] with
                | StarTrack w -> weights <- weights + w
                | AutoTrack | Fixed -> taken <- taken + sizes[k]
            if weights > 0.0 then unit <- max unit ((desire i - taken) / weights)
        let mutable total = spacing * float (max 0 (kinds.Length - 1))
        for k in 0 .. kinds.Length - 1 do
            total <-
                total
                + match kinds[k] with
                  | StarTrack w -> clamp (unit * w) tracks[k].Min tracks[k].Max
                  | _ -> sizes[k]
        min total available

    /// Where each track starts and ends, the lines snapped to whole physical pixels (so cells tile).
    let private snapEdges (sizes: float[]) (spacing: float) (scale: float) =
        let snap (v: float) = Math.Round(v * scale, MidpointRounding.AwayFromZero) / scale
        let starts = Array.zeroCreate sizes.Length
        let ends = Array.zeroCreate sizes.Length
        let mutable x = 0.0
        for k in 0 .. sizes.Length - 1 do
            starts[k] <- snap x
            x <- x + sizes[k]
            ends[k] <- snap x
            x <- x + spacing
        struct (starts, ends)

    let private spanLength (sizes: float[]) (spacing: float) (first: int) (count: int) =
        let mutable total = spacing * float (count - 1)
        for k in first .. first + count - 1 do
            total <- total + sizes[k]
        total

    let private measure (ctx: MeasureContext) (available: Size) =
        let columns, rows = tracksOf (ctx.Get GridProps.columns), tracksOf (ctx.Get GridProps.rows)
        let cs, rs = ctx.Get GridProps.columnSpacing, ctx.Get GridProps.rowSpacing
        let boundedW, boundedH = not (Double.IsInfinity available.Width), not (Double.IsInfinity available.Height)
        ctx.SetLocal(boundedWidth, boundedW)
        ctx.SetLocal(boundedHeight, boundedH)
        let children = ctx.Children
        let places = [| for c in children -> placement c rows.Length columns.Length |]
        let colSpans = places |> Array.map (fun (struct (_, c, _, s)) -> struct (c, s))
        let rowSpans = places |> Array.map (fun (struct (r, _, s, _)) -> struct (r, s))
        // Columns: Auto ones from the children's natural widths.
        let struct (colKinds, colSizes) =
            solve columns available.Width boundedW cs colSpans (fun i ->
                ctx.Measure(children[i], Size.Create(Double.PositiveInfinity, Double.PositiveInfinity)).Width)
        // Rows: with the column widths known, so text wraps where it will be.
        let width i = let struct (c, s) = colSpans[i] in spanLength colSizes cs c s
        let struct (rowKinds, rowSizes) =
            solve rows available.Height boundedH rs rowSpans (fun i ->
                ctx.Measure(children[i], Size.Create(width i, Double.PositiveInfinity)).Height)
        // Every child in its cell.
        let height i = let struct (r, s) = rowSpans[i] in spanLength rowSizes rs r s
        let desired = [| for i in 0 .. children.Count - 1 -> ctx.Measure(children[i], Size.Create(width i, height i)) |]
        let w = natural colKinds colSizes columns cs colSpans (fun i -> desired[i].Width) available.Width
        let h = natural rowKinds rowSizes rows rs rowSpans (fun i -> desired[i].Height) available.Height
        Size.Create(w, h)

    /// The column and row edges for a final size (also used by the inspector's overlay).
    let internal layout (e: Element) (size: Size) =
        let columns, rows = tracksOf (e.Get GridProps.columns), tracksOf (e.Get GridProps.rows)
        let cs, rs = e.Get GridProps.columnSpacing, e.Get GridProps.rowSpacing
        let children = e.Children
        let places = [| for c in children -> placement c rows.Length columns.Length |]
        let colSpans = places |> Array.map (fun (struct (_, c, _, s)) -> struct (c, s))
        let rowSpans = places |> Array.map (fun (struct (r, _, s, _)) -> struct (r, s))
        let desired i = children[i].DesiredSize
        let struct (_, colSizes) = solve columns size.Width (e.Get boundedWidth) cs colSpans (fun i -> (desired i).Width)
        let struct (_, rowSizes) = solve rows size.Height (e.Get boundedHeight) rs rowSpans (fun i -> (desired i).Height)
        let scale = e.Scale
        let struct (colStarts, colEnds) = snapEdges colSizes cs scale
        let struct (rowStarts, rowEnds) = snapEdges rowSizes rs scale
        struct (places, colStarts, colEnds, rowStarts, rowEnds)

    let private arrange (ctx: ArrangeContext) (size: Size) =
        let struct (places, colStarts, colEnds, rowStarts, rowEnds) = layout ctx.Element size
        let children = ctx.Children
        for i in 0 .. children.Count - 1 do
            let struct (r, c, rspan, cspan) = places[i]
            let x, y = colStarts[c], rowStarts[r]
            ctx.Arrange(children[i], Rect.Create(x, y, colEnds[c + cspan - 1] - x, rowEnds[r + rspan - 1] - y))

    let Type = Element.define "Grid" [ Element.measure measure; Element.arrange arrange ]

    let create (settings: Setting<Grid> list) : View = View.Create(Type, settings)

    /// For tools (the inspector's overlay): where a Grid element's columns and rows start and end,
    /// in its own coordinates, as laid out last (column starts, column ends, row starts, row ends).
    let edges (e: Element) : struct (float[] * float[] * float[] * float[]) voption =
        if not (obj.ReferenceEquals(e.Type, Type)) then ValueNone
        else
            let struct (_, cs, ce, rs, re) = layout e (Size.Create(e.Bounds.Width, e.Bounds.Height))
            ValueSome(struct (cs, ce, rs, re))
    /// A style for Grids: `Grid.style [ Style.cls "form" ] [ Grid.columnSpacing 12.0 ]` (this type exactly).
    let style (parts: SelectorPart list) (settings: Setting<Grid> list) : Style = Style.forType Type parts settings
