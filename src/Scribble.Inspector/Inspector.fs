namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// Which side of the window the inspector's panel sits on.
[<RequireQualifiedAccess>]
type DockSide =
    | Left
    | Right

type internal InspectorOptions =
    { Side: DockSide
      ToggleKey: Key * Modifiers
      PickKey: Key * Modifiers
      IsOpen: bool
      /// Where the pop-out window comes from (none: no pop-out).
      Platform: IPlatform voption
      /// Name the pop-out after `WindowTitle.current` and follow it (in an app), else after the
      /// window's title when it opens.
      FollowAppTitle: bool
      /// The app's own formatting, tried before ours (`Inspector.format`).
      Format: obj -> string voption }

    static member Default =
        { Side = DockSide.Right
          ToggleKey = (Key.F 12, Modifiers.None)
          PickKey = (Key.Char 'c', Modifiers.Control ||| Modifiers.Shift)
          IsOpen = false
          Platform = ValueNone
          FollowAppTitle = false
          Format = fun _ -> ValueNone }

/// Where the panel is.
[<RequireQualifiedAccess>]
type internal PanelMode =
    | Docked
    | Floating
    | PoppedOut

/// What a drag on the panel does.
[<RequireQualifiedAccess>]
type internal DragKind =
    | Move
    | ResizeCorner
    | ResizeEdge
    /// The divider between the tree and the properties.
    | Split

[<Struct>]
type internal Drag = { Kind: DragKind; Start: Point; StartRect: Rect }

module internal PanelSize =
    let dockedWidth = 380.0
    let minWidth = 300.0
    let minHeight = 160.0
    /// Around a docked panel and between a floating one and the window's edges when clamped.
    let gap = 8.0
    let minProperties = 60.0
    /// What the header, the footer and a minimal tree take next to the properties.
    let besideProperties = 170.0

/// One inspector setting: `Inspector.side DockSide.Left`.
[<Struct>]
type InspectorSetting = internal InspectorSetting of (InspectorOptions -> InspectorOptions)

/// One row of the tree: a layer's heading or an element at a depth.
[<RequireQualifiedAccess>]
type internal Row =
    | Layer of Layer
    | Element of Element * depth: int

/// Text for keys and values, by our own code (never `string` on unions: it isn't AOT-safe).
/// Names the inspector gives its own elements, to find them again.
module internal Names =
    /// The selected row's chevron and type name (scrolled into view sideways after a pick).
    [<Literal>]
    let SelectedLabel = "inspectorSelectedLabel"

module internal Format =
    let key (key: Key, mods: Modifiers) =
        let name =
            match key with
            | Key.F n -> "F" + n.ToString()
            | Key.Char c -> Char.ToUpperInvariant(c).ToString()
            | Key.Escape -> "Esc"
            | Key.Enter -> "Enter"
            | Key.Space -> "Space"
            | Key.Tab -> "Tab"
            | Key.Other n -> n
            | _ -> "key"
        let has (m: Modifiers) = mods &&& m = m
        (if has Modifiers.Control then "Ctrl+" else "")
        + (if has Modifiers.Alt then "Alt+" else "")
        + (if has Modifiers.Shift then "Shift+" else "")
        + (if has Modifiers.Meta then "Meta+" else "")
        + name

    let clip (max: int) (text: string) =
        let oneLine = text.Replace('\n', ' ').Replace('\r', ' ')
        if oneLine.Length <= max then oneLine else oneLine.Substring(0, max - 1) + "…"

    /// The type's name, or the component's for a component's host element.
    let label (e: Element) =
        match e.ComponentName with
        | ValueSome n -> n
        | ValueNone -> e.Type.Name

    /// "realized 23 of 10,000" for an ItemsView, else "".
    let realized (e: Element) =
        let count (n: int) = n.ToString("N0", Globalization.CultureInfo.InvariantCulture)
        match ItemsView.realizedCount e with
        | ValueSome(struct (realized, total)) -> "realized " + count realized + " of " + count total
        | ValueNone -> ""

    /// A hint after a collapsed row's name: a TextBlock's text, or the text of a TextBlock directly inside.
    let hint (e: Element) =
        let textOf (t: Element) = "\"" + clip 28 (t.Get TextProps.text) + "\""
        if e.Type.Name = "TextBlock" then textOf e
        else
            match e.Children |> Seq.tryFind (fun c -> c.Type.Name = "TextBlock") with
            | Some t when e.Children.Count = 1 -> textOf t
            | _ -> ""

    let size (width: float) (height: float) = $"{width:F0} × {height:F0}"

    let private inv = Globalization.CultureInfo.InvariantCulture

    let number (v: float) =
        // Inter has no ∞ glyph.
        if Double.IsPositiveInfinity v then "any"
        elif Double.IsNegativeInfinity v then "-any"
        elif Double.IsNaN v then "NaN"
        else v.ToString("0.##", inv)

    /// "W × H", with "any" for unlimited space.
    let sizeOf (s: Size) = number s.Width + " × " + number s.Height

    let color (c: Color) =
        if c.A = 0.0f && c.R = 0.0f && c.G = 0.0f && c.B = 0.0f then "transparent"
        else
            let byte (v: float32) = (int (Math.Round(float (max 0.0f (min 1.0f v)) * 255.0))).ToString("X2", inv)
            "#" + byte c.R + byte c.G + byte c.B + (if c.A < 1.0f then byte c.A else "")

    /// One number when all four are equal, two (horizontal, vertical) when the sides pair up.
    let private four (l: float) (t: float) (r: float) (b: float) =
        if l = t && t = r && r = b then number l
        elif l = r && t = b then number l + ", " + number t
        else number l + ", " + number t + ", " + number r + ", " + number b

    let rec private isFunction (t: Type) =
        if isNull t then false
        elif t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<FSharpFunc<int, int>> then true
        else isFunction t.BaseType

    /// A type's name as F# writes it: `voption<Color>`, `float`, `Model`.
    let rec typeName (t: Type) =
        if t = typeof<float> then "float"
        elif t = typeof<float32> then "float32"
        elif t = typeof<int> then "int"
        elif t = typeof<string> then "string"
        elif t = typeof<bool> then "bool"
        elif isFunction t then "function"
        elif t.IsGenericType then
            let def = t.GetGenericTypeDefinition()
            let name =
                if def = typedefof<int option> then "option"
                elif def = typedefof<int voption> then "voption"
                elif def = typedefof<int list> then "list"
                else
                    let n = t.Name
                    let i = n.IndexOf '`'
                    if i > 0 then n.Substring(0, i) else n
            name + "<" + String.Join(", ", t.GetGenericArguments() |> Array.map typeName) + ">"
        else t.Name

    let private horizontal (a: HorizontalAlignment) =
        match a with
        | HorizontalAlignment.Stretch -> "stretch"
        | HorizontalAlignment.Left -> "left"
        | HorizontalAlignment.Center -> "center"
        | HorizontalAlignment.Right -> "right"

    let private vertical (a: VerticalAlignment) =
        match a with
        | VerticalAlignment.Stretch -> "stretch"
        | VerticalAlignment.Top -> "top"
        | VerticalAlignment.Center -> "center"
        | VerticalAlignment.Bottom -> "bottom"

    let private cursor (c: Cursor) =
        match c with
        | Cursor.Default -> "default"
        | Cursor.Hand -> "hand"
        | Cursor.Text -> "text"
        | Cursor.Crosshair -> "crosshair"
        | Cursor.Move -> "move"
        | Cursor.ResizeLeftRight -> "resize left-right"
        | Cursor.ResizeUpDown -> "resize up-down"
        | Cursor.ResizeDiagonal -> "resize diagonal"
        | Cursor.NotAllowed -> "not allowed"
        | Cursor.Wait -> "wait"
        // Cursor.fromIcon's key starts "icon|<the icon's description>|...".
        | Cursor.Image image ->
            let parts = image.Key.Split('|', 3)
            if parts.Length > 1 && parts[0] = "icon" then "icon " + parts[1] else "image"

    let private shadow (s: BoxShadow) =
        "blur " + number s.Blur + " at " + number s.OffsetX + ", " + number s.OffsetY
        + (if s.Spread <> 0.0 then " spread " + number s.Spread else "") + " " + color s.Color

    let private viewName (v: View) = v.Type.Name
    let private handler (isSet: bool) = if isSet then "handler" else "none"

    /// Text for any value, by our own code: `string`/`ToString()` on F# records and unions is `%A`
    /// underneath, which crashes under NativeAOT. Unknown types show their type name. `custom` is
    /// the app's formatter (`Inspector.format`), tried first, also for values inside lists.
    let rec value (custom: obj -> string voption) (v: obj) : string =
        match (if isNull v then ValueNone else custom v) with
        | ValueSome s -> s
        | ValueNone ->
            match v with
            | null -> "none"
            | :? string as s -> "\"" + clip 60 s + "\""
            | :? bool as b -> if b then "true" else "false"
            | :? float as f -> number f
            | :? float32 as f -> number (float f)
            | :? int as i -> i.ToString(inv)
            | :? int64 as i -> i.ToString(inv)
            | :? decimal as d -> d.ToString(inv)
            | :? char as c -> "'" + c.ToString() + "'"
            | :? Color as c -> color c
            | :? Thickness as t -> four t.Left t.Top t.Right t.Bottom
            | :? CornerRadius as r -> four r.TopLeft r.TopRight r.BottomRight r.BottomLeft
            | :? Length as l -> l.ToString() // Length formats itself without %A
            | :? BoxShadow as s -> shadow s
            | :? (BoxShadow voption) as s -> (match s with ValueSome x -> shadow x | ValueNone -> "none")
            | :? Size as s -> sizeOf s
            | :? Rect as r -> number r.X + ", " + number r.Y + "  " + number r.Width + " × " + number r.Height
            | :? Point as p -> number p.X + ", " + number p.Y
            | :? HorizontalAlignment as a -> horizontal a
            | :? VerticalAlignment as a -> vertical a
            | :? Orientation as o -> (match o with Vertical -> "vertical" | Horizontal -> "horizontal")
            | :? Cursor as c -> cursor c
            | :? (Cursor voption) as c -> (match c with ValueSome x -> cursor x | ValueNone -> "inherited")
            | :? TextWrapping as w -> (match w with NoWrap -> "no wrap" | Wrap -> "wrap")
            | :? TextTrimming as t -> (match t with NoTrimming -> "none" | CharacterEllipsis -> "ellipsis")
            | :? TextAlignment as a -> (match a with TextAlignment.Start -> "start" | TextAlignment.Center -> "center" | TextAlignment.End -> "end")
            | :? Font as f -> f.Name
            | :? Icon as i -> Icon.describe i
            | :? (Icon voption) as i -> (match i with ValueSome x -> Icon.describe x | ValueNone -> "none")
            | :? Style as s -> s.Description
            | :? ElementState as s -> ":" + s.Name
            | :? View as v -> viewName v
            | :? (View voption) as v -> (match v with ValueSome x -> viewName x | ValueNone -> "none")
            | :? (View list) as l -> (match l.Length with 1 -> "1 child" | n -> n.ToString(inv) + " children")
            | :? Element as e -> label e
            | :? (obj voption) as o -> (match o with ValueSome x -> value custom x | ValueNone -> "none")
            | :? (string option) as s -> (match s with Some x -> value custom x | None -> "none")
            | :? (float voption) as f -> (match f with ValueSome x -> number x | ValueNone -> "none")
            | :? ((ClickEventArgs -> unit) voption) as h -> handler h.IsSome
            | :? ((PointerEventArgs -> unit) voption) as h -> handler h.IsSome
            | :? ((KeyEventArgs -> unit) voption) as h -> handler h.IsSome
            | :? ((WheelEventArgs -> unit) voption) as h -> handler h.IsSome
            | :? exn as e -> e.GetType().Name + ": " + clip 60 e.Message
            | :? Enum as e -> e.ToString() // enums format themselves under NativeAOT
            | :? Delegate -> "handler"
            | _ when isFunction (v.GetType()) -> "handler"
            | :? Collections.IEnumerable as items ->
                let shown = List<string>()
                let mutable n = 0
                for item in items do
                    if n < 3 then shown.Add(value custom item)
                    n <- n + 1
                "[" + n.ToString(inv) + ": " + String.Join(", ", shown) + (if n > 3 then ", …" else "") + "]"
            | _ -> typeName (v.GetType())

/// What the inspector shows and does. Both of its layers draw from this; `Changed` tells them to
/// update. UI thread only.
[<Sealed>]
type internal InspectorState(host: ElementHost, options: InspectorOptions) =
    let changed = Event<unit>()
    /// Bumped by every change except the panel's own geometry (moving, resizing, the divider), so
    /// the tree and properties aren't rebuilt while the panel is dragged.
    let mutable contentVersion = 0
    let notify () =
        contentVersion <- contentVersion + 1
        changed.Trigger()
    /// Only the panel's place or size changed.
    let moved () = changed.Trigger()
    let expanded = HashSet<Element>(HashIdentity.Reference)
    let seenRoots = HashSet<Element>(HashIdentity.Reference)
    let collapsedLayers = HashSet<Layer>(HashIdentity.Reference)
    let clock = Stopwatch.StartNew()
    let mutable isOpen = options.IsOpen
    let mutable side = options.Side
    let mutable picking = false
    /// Pick mode: where the pointer is over the app (window DIPs), so what it points at can be
    /// checked again after each frame: the app can move under a pointer that stands still (a list
    /// scrolled by the wheel recycles the row under it to somewhere else).
    let mutable pickPoint: Point voption = ValueNone
    let mutable hovered: Element voption = ValueNone
    let mutable selected: Element voption = ValueNone
    /// The selected element's tree row should be scrolled into view (after a pick, which can
    /// select something whose row is far down); asked once the tree lists it.
    let mutable revealSelected = false
    /// The last `Rows()` had the selected element's row.
    let mutable selectedListed = false
    /// After the tree scrolled to the selected row: bring its name into view sideways once the row exists.
    let mutable revealSideways = false
    /// Scrolls the tree (an ItemsView) by key.
    let treeScroller = Scroller()
    let mutable own: Layer list = []
    let mutable dirty = false
    let mutable lastRefresh = -1000.0
    let mutable refreshScheduled = false
    let mutable mode = PanelMode.Docked
    /// The in-window mode to go back to when the pop-out window closes.
    let mutable modeBeforePopOut = PanelMode.Docked
    let mutable dockedWidth = PanelSize.dockedWidth
    let mutable floating = Rect.Create(0.0, 0.0, PanelSize.dockedWidth, 400.0)
    let mutable collapsed = false
    let mutable drag: Drag voption = ValueNone
    let mutable lastTitlePress = -1000.0
    let mutable lastTitlePoint = Point(0.0, 0.0)
    let popOut = Event<unit>()
    let popIn = Event<unit>()
    let mutable showDefaults = false
    /// Elements with states forced from the panel (cleared when the inspector closes).
    let forcedOn = HashSet<Element>(HashIdentity.Reference)
    /// The properties' share of the panel, in DIPs (the divider changes it).
    let mutable propertiesHeight = 300.0
    // Frame stats. They're shown as of the last refresh only: the inspector's own frames must not
    // update them, or every frame showing new stats would make new stats (frames forever).
    let mutable lastStats: FrameStats voption = ValueNone
    let mutable statsText = ""
    /// Something in the app's layers changed since the last frame.
    let mutable appChanged = false
    /// When the frames the app caused were drawn (the last second's worth), for frames/s.
    let appFrames = Queue<float>()
    let mutable idleCheckScheduled = false

    /// The app window's size in DIPs.
    let windowSize () =
        let s = host.Window.ClientSize
        let scale = host.Window.Scale
        Size.Create(float s.Width / scale, float s.Height / scale)

    let clamp (lo: float) (hi: float) (v: float) = max lo (min (max lo hi) v)

    /// A floating rectangle kept inside the window (at least its size allows).
    let keepInside (r: Rect) =
        let s = windowSize ()
        let w = clamp PanelSize.minWidth s.Width r.Width
        let h = clamp PanelSize.minHeight s.Height r.Height
        Rect.Create(clamp 0.0 (s.Width - w) r.X, clamp 0.0 (s.Height - (if collapsed then 40.0 else h)) r.Y, w, h)

    let dockedRect () =
        let s = windowSize ()
        let w = clamp PanelSize.minWidth (s.Width - 2.0 * PanelSize.gap) dockedWidth
        let x = if side = DockSide.Right then s.Width - PanelSize.gap - w else PanelSize.gap
        Rect.Create(x, PanelSize.gap, w, max 0.0 (s.Height - 2.0 * PanelSize.gap))

    let isOwn (layer: Layer) = own |> List.exists (fun l -> obj.ReferenceEquals(l, layer))
    let alive (e: Element voption) =
        match e with
        | ValueSome x when x.Root.IsSome -> e
        | _ -> ValueNone

    let trimFrames (now: float) =
        while appFrames.Count > 0 && now - appFrames.Peek() > 1000.0 do
            appFrames.Dequeue() |> ignore

    let refresh () =
        dirty <- false
        let now = clock.Elapsed.TotalMilliseconds
        lastRefresh <- now
        hovered <- alive hovered
        selected <- alive selected
        trimFrames now
        let inv = Globalization.CultureInfo.InvariantCulture
        let frames =
            match appFrames.Count with
            | 0 -> "app idle"
            | n -> n.ToString(inv) + " app frames/s"
        statsText <-
            match lastStats with
            | ValueSome s ->
                "layout " + s.LayoutMilliseconds.ToString("0.00", inv) + " · render " + s.RenderMilliseconds.ToString("0.00", inv)
                + " ms · " + s.Shapes.ToString("N0", inv) + " shapes · " + frames
            | ValueNone -> frames
        notify ()

    /// The topmost element under a point in any layer but the inspector's own.
    let elementAt (p: Point) =
        let mutable found = ValueNone
        let mutable i = host.Layers.Count - 1
        while found.IsNone && i >= 0 do
            let l = host.Layers[i]
            if not (isOwn l) then found <- l.Tree.HitTest(p.X, p.Y)
            i <- i - 1
        found

    member _.Changed = changed.Publish
    /// Changes with everything but the panel's geometry (see `notify`).
    member _.ContentVersion = contentVersion
    member _.IsOpen = isOpen
    member _.IsPicking = picking
    member _.Hovered = hovered
    member _.Selected = selected
    member _.Options = options

    member _.Own
        with get () = own
        and set v =
            own <- v
            notify () // the tree leaves its own layers out

    /// What the highlight shows: the hovered element (a row or, in pick mode, the app), else the selection.
    member _.Target =
        if not isOpen then ValueNone
        else
            match alive hovered with
            | ValueSome e -> ValueSome e
            | ValueNone -> alive selected

    /// The highlight shows the selection, not something pointed at: then it's only an outline,
    /// so the element's own looks (hover tint, press, forced states) stay visible.
    member this.TargetIsSelection =
        isOpen && (alive hovered).IsNone && this.Target.IsSome

    member _.IsExpanded(e: Element) = expanded.Contains e
    member _.IsCollapsed(l: Layer) = collapsedLayers.Contains l

    /// The rows to show: every layer but the inspector's, and the elements under expanded ones.
    /// Scrolls the tree by key (the tree's ItemsView uses it).
    member _.TreeScroller = treeScroller

    member _.Rows() : Row[] =
        let rows = List<Row>()
        selectedListed <- false
        let rec add (e: Element) depth =
            rows.Add(Row.Element(e, depth))
            match selected with
            | ValueSome s when obj.ReferenceEquals(s, e) -> selectedListed <- true
            | _ -> ()
            if expanded.Contains e then
                for c in e.Children do
                    add c (depth + 1)
        for layer in host.Layers do
            if not (isOwn layer) then
                rows.Add(Row.Layer layer)
                match layer.Tree.Root with
                | ValueSome root ->
                    // Roots start expanded; after that the user decides.
                    if seenRoots.Add root then expanded.Add root |> ignore
                    if not (collapsedLayers.Contains layer) then add root 1
                | ValueNone -> ()
        rows.ToArray()

    member this.Toggle() =
        // Hiding a popped-out inspector closes its window (the panel goes back in for next time).
        if isOpen && mode = PanelMode.PoppedOut then this.PopIn()
        isOpen <- not isOpen
        // Closing it leaves no state forced in the app.
        if not isOpen then this.ClearForced()
        picking <- picking && isOpen
        hovered <- ValueNone
        pickPoint <- ValueNone
        refresh ()

    member _.TogglePick() =
        picking <- not picking
        if picking then isOpen <- true
        hovered <- ValueNone
        pickPoint <- ValueNone
        refresh ()

    member _.StopPicking() =
        picking <- false
        hovered <- ValueNone
        pickPoint <- ValueNone
        notify ()

    // --- Where the panel is ------------------------------------------------------------------

    member _.Mode = mode
    member _.Side = side
    member _.IsPanelCollapsed = collapsed
    member _.DockedRect = dockedRect ()
    member _.FloatingRect = keepInside floating

    /// True when there's a platform that can open the pop-out window.
    member _.CanPopOut =
        match options.Platform with
        | ValueSome p -> p.SupportsMultipleWindows
        | ValueNone -> false

    /// The pop-out window should open (the inspector's attach code listens) or close.
    member _.PopOutRequested = popOut.Publish
    member _.PopInRequested = popIn.Publish

    member _.Dock(value: DockSide) =
        side <- value
        mode <- PanelMode.Docked
        notify ()

    member this.ToggleCollapsed() =
        collapsed <- not collapsed
        notify ()

    member this.PopOut() =
        if this.CanPopOut && mode <> PanelMode.PoppedOut then
            modeBeforePopOut <- mode
            mode <- PanelMode.PoppedOut
            isOpen <- true
            popOut.Trigger()
            notify ()

    /// Back into the app's window, where it was before.
    member _.PopIn() =
        if mode = PanelMode.PoppedOut then
            mode <- modeBeforePopOut
            popIn.Trigger()
            notify ()

    /// A press on the title area (move), the corner grip or a docked panel's inner edge. Two
    /// quick presses on the title area collapse or expand the panel instead.
    member this.BeginDrag(kind: DragKind, p: Point) =
        let now = clock.Elapsed.TotalMilliseconds
        let isDouble =
            kind = DragKind.Move && now - lastTitlePress < 400.0
            && abs (p.X - lastTitlePoint.X) < 4.0 && abs (p.Y - lastTitlePoint.Y) < 4.0
        if kind = DragKind.Move then
            lastTitlePress <- (if isDouble then -1000.0 else now)
            lastTitlePoint <- p
        if isDouble then
            drag <- ValueNone
            this.ToggleCollapsed()
        else
            let start =
                match kind, mode with
                | DragKind.Move, PanelMode.Docked ->
                    // Undocked, the panel keeps its width and gets a height that leaves room to move.
                    let d = dockedRect ()
                    Rect.Create(d.X, d.Y, d.Width, max PanelSize.minHeight (min d.Height ((windowSize ()).Height * 0.6)))
                | DragKind.ResizeEdge, _ -> dockedRect ()
                | DragKind.Split, _ -> Rect.Create(0.0, 0.0, 0.0, this.PropertiesHeight)
                | _ -> keepInside floating
            drag <- ValueSome { Kind = kind; Start = p; StartRect = start }

    member this.DragTo(p: Point) =
        match drag with
        | ValueSome d ->
            let dx, dy = p.X - d.Start.X, p.Y - d.Start.Y
            let r = d.StartRect
            match d.Kind with
            | DragKind.Move ->
                // A docked panel comes off once the pointer has really moved (not on a click).
                if mode = PanelMode.Docked && abs dx + abs dy > 4.0 then mode <- PanelMode.Floating
                if mode = PanelMode.Floating then
                    floating <- keepInside (Rect.Create(r.X + dx, r.Y + dy, r.Width, r.Height))
                    moved ()
            | DragKind.Split ->
                // Up makes the properties taller.
                propertiesHeight <- max PanelSize.minProperties (r.Height - dy)
                propertiesHeight <- this.PropertiesHeight
                moved ()
            | DragKind.ResizeCorner ->
                floating <- keepInside (Rect.Create(r.X, r.Y, r.Width + dx, r.Height + dy))
                moved ()
            | DragKind.ResizeEdge ->
                let s = windowSize ()
                let w = r.Width + (if side = DockSide.Right then -dx else dx)
                dockedWidth <- clamp PanelSize.minWidth (s.Width - 2.0 * PanelSize.gap) w
                moved ()
        | ValueNone -> ()

    member _.EndDrag() = drag <- ValueNone

    // --- The properties ------------------------------------------------------------------------

    /// Property defaults are listed too (off by default; type defaults always are).
    member _.ShowDefaults = showDefaults

    member _.ToggleShowDefaults() =
        showDefaults <- not showDefaults
        notify ()

    /// The properties' height, kept small enough to leave the tree some room in the app's window.
    member _.PropertiesHeight =
        let panelHeight =
            match mode with
            | PanelMode.Floating -> (keepInside floating).Height
            | PanelMode.Docked -> (dockedRect ()).Height
            | PanelMode.PoppedOut -> Double.PositiveInfinity
        max PanelSize.minProperties (min propertiesHeight (panelHeight - PanelSize.besideProperties))

    /// Frame stats as of the last refresh, and the app's frames in the second before it.
    member _.StatsText = statsText

    member _.Format(v: obj) = Format.value options.Format v

    member _.ToggleExpanded(e: Element) =
        if not (expanded.Remove e) then expanded.Add e |> ignore
        notify ()

    member _.ToggleLayer(l: Layer) =
        if not (collapsedLayers.Remove l) then collapsedLayers.Add l |> ignore
        notify ()

    /// Selects an element and expands the rows down to it.
    member _.Select(e: Element) =
        selected <- ValueSome e
        revealSelected <- true
        let mutable p = e.Parent
        let mutable top = e
        while p.IsSome do
            expanded.Add p.Value |> ignore
            top <- p.Value
            p <- p.Value.Parent
        for l in host.Layers do
            match l.Tree.Root with
            | ValueSome r when obj.ReferenceEquals(r, top) -> collapsedLayers.Remove l |> ignore
            | _ -> ()
        notify ()

    // --- Forced states ------------------------------------------------------------------------

    /// Forces `s` on `e` to the opposite of what it is now (on if off, off if on), or stops forcing it
    /// (looks only; see `element.ForceState`).
    member _.ToggleForced(e: Element, s: ElementState) =
        if e.ForcedStates |> List.exists (fun (f, _) -> obj.ReferenceEquals(f, s)) then
            e.UnforceState s
            if e.ForcedStates.IsEmpty then forcedOn.Remove e |> ignore
        else
            let isOn = e.ActiveStates |> List.exists (fun a -> obj.ReferenceEquals(a, s))
            e.ForceState(s, not isOn)
            forcedOn.Add e |> ignore
        notify ()

    /// Some element has forced states.
    member _.HasForced = forcedOn.Count > 0

    /// Stops forcing states everywhere (also when the inspector closes or goes away).
    member _.ClearForced() =
        if forcedOn.Count > 0 then
            for e in forcedOn do
                e.ClearForcedStates()
            forcedOn.Clear()
            notify ()

    /// Clears the selection (the panel shows its hint again).
    member _.Deselect() =
        if selected.IsSome then
            selected <- ValueNone
            revealSelected <- false
            revealSideways <- false
            notify ()

    /// A click on a tree row: selects it, or deselects it when it's already selected.
    member this.ClickRow(e: Element) =
        match selected with
        | ValueSome s when obj.ReferenceEquals(s, e) -> this.Deselect()
        | _ -> this.Select e

    member _.HoverRow(e: Element) =
        if not picking then
            hovered <- ValueSome e
            notify ()

    member _.LeaveRow(e: Element) =
        match hovered with
        | ValueSome h when not picking && obj.ReferenceEquals(h, e) ->
            hovered <- ValueNone
            notify ()
        | _ -> ()

    /// Pick mode: the pointer moved to `p` (window DIPs).
    /// Only while picking: the press that picks ends pick mode, and browsers send a move before its
    /// release (the highlight still has the pointer then); picking again there left an element hovered.
    member _.PickAt(p: Point) =
        if picking then
            pickPoint <- ValueSome p
            let e = elementAt p
            let same =
                match e, hovered with
                | ValueSome a, ValueSome b -> obj.ReferenceEquals(a, b)
                | ValueNone, ValueNone -> true
                | _ -> false
            if not same then
                hovered <- e
                notify ()

    /// Pick mode: a press at `p` selects what's there and ends pick mode.
    member this.PickSelect(p: Point) =
        picking <- false
        hovered <- ValueNone
        pickPoint <- ValueNone
        match elementAt p with
        | ValueSome e -> this.Select e
        | ValueNone -> notify ()

    /// Shortcuts, seen before any layer gets the key.
    member this.OnKey (key: Key) (mods: Modifiers) =
        if (key, mods) = options.ToggleKey then
            this.Toggle()
            true
        elif (key, mods) = options.PickKey then
            this.TogglePick()
            true
        elif picking && key = Key.Escape then
            this.StopPicking()
            true
        else false

    /// A layer changed: if it's one of the app's, the tree may need refreshing.
    member _.LayerChanged(layer: Layer) =
        if not (isOwn layer) then
            appChanged <- true
            if isOpen then dirty <- true

    /// After each frame: refresh the tree if the app changed, at most about 10 times a second.
    /// Once the app has been still for a second, one more refresh shows it idle (and then nothing).
    /// Pick mode: the pointer went over the panel or out of the window; nothing to check again.
    member _.PickLeft() = pickPoint <- ValueNone

    member this.AfterFrame(stats: FrameStats) =
        // Pick mode: what the still pointer points at now (a change shows in the next frame).
        match pickPoint with
        | ValueSome p when picking -> this.PickAt p
        | _ -> ()
        // The selected row, once the tree lists it (it refreshes a little later): into view, by key
        // (the tree is an ItemsView; the row may never have been realized).
        match selected with
        | ValueSome e when revealSelected && selectedListed ->
            revealSelected <- false
            revealSideways <- true
            treeScroller.ScrollTo(box e, ScrollAlign.Nearest, false) // at once, as devtools do
        | _ when revealSideways ->
            // The row exists now (the frame after the scroll): its chevron and name into view sideways,
            // with a little indent to their left; nothing moves when they already show.
            let rec named (e: Element) : Element voption =
                if e.Name = Names.SelectedLabel then ValueSome e
                else
                    let mutable found = ValueNone
                    let mutable i = 0
                    while found.IsNone && i < e.Children.Count do
                        found <- named e.Children[i]
                        i <- i + 1
                    found
            let label = own |> List.tryPick (fun l -> match l.Tree.Root with ValueSome r -> (match named r with ValueSome x -> Some x | ValueNone -> None) | ValueNone -> None)
            match label with
            | Some l ->
                revealSideways <- false
                l.BringIntoView()
            | None -> if selected.IsNone then revealSideways <- false
        | _ -> ()
        let now = clock.Elapsed.TotalMilliseconds
        lastStats <- ValueSome stats
        if appChanged then
            appChanged <- false
            appFrames.Enqueue now
        trimFrames now
        let later (wait: float) (action: unit -> unit) =
            match host.Dispatcher with
            | ValueSome d ->
                Task.Delay(TimeSpan.FromMilliseconds wait).ContinueWith(fun (_: Task) -> d.Post action) |> ignore
                true
            | ValueNone -> false
        let checkIdle () =
            if isOpen && not idleCheckScheduled then
                idleCheckScheduled <-
                    later 1100.0 (fun () ->
                        idleCheckScheduled <- false
                        trimFrames clock.Elapsed.TotalMilliseconds
                        if isOpen && not dirty && appFrames.Count = 0 then refresh ())
        if dirty && isOpen && not refreshScheduled then
            let wait = 100.0 - (now - lastRefresh)
            if wait > 0.0 then
                refreshScheduled <-
                    later wait (fun () ->
                        refreshScheduled <- false
                        if dirty && isOpen then
                            refresh ()
                            checkIdle ())
                if not refreshScheduled then refresh ()
            else
                refresh ()
                checkIdle ()

// --- The highlight -----------------------------------------------------------------------------------

module internal HighlightProps =
    let target = Prop.define "Target" (ValueNone: Element voption) Affects.Render
    let outlineOnly = Prop.define "OutlineOnly" false Affects.Render

/// Draws the devtools-style highlight over an element (bounds, margin, a label with the size), or
/// just a thin outline around it (the selection, while nothing is pointed at).
/// Reads the target's position while drawing, so it's exact in every frame.
[<AbstractClass>]
type internal Highlight =
    inherit Layoutable
    static member target<'k when 'k :> Highlight>(value: Element voption) : Setting<'k> = Setting.create HighlightProps.target value
    static member outlineOnly<'k when 'k :> Highlight>(value: bool) : Setting<'k> = Setting.create HighlightProps.outlineOnly value

module internal Highlight =
    let private boxColor = Color.Hex 0x6FA8DC
    let private marginColor = (Color.Hex 0xF6B26B).WithAlpha 0.35f
    let private gridColor = Color.Hex 0xC792EA

    /// A Grid's column and row lines (dashed) and its gaps (shaded), like a browser's grid overlay.
    let private gridLines (list: DrawList) (t: Element) (r: Rect) =
        match Grid.edges t with
        | ValueSome(struct (colStarts, colEnds, rowStarts, rowEnds)) ->
            let dash = StrokeStyle.Dashed(4.0, 3.0)
            let vertical (x: float) = list.StrokeRect(Rect.Create(r.X + x, r.Y, 1.0, r.Height), CornerRadius.Zero, 1.0, gridColor, style = dash)
            let horizontal (y: float) = list.StrokeRect(Rect.Create(r.X, r.Y + y, r.Width, 1.0), CornerRadius.Zero, 1.0, gridColor, style = dash)
            for k in 0 .. colStarts.Length - 1 do
                if k > 0 && colStarts[k] > colEnds[k - 1] then
                    list.FillRect(Rect.Create(r.X + colEnds[k - 1], r.Y, colStarts[k] - colEnds[k - 1], r.Height), gridColor.WithAlpha 0.18f)
                if colStarts[k] > 0.0 then vertical colStarts[k]
                if colEnds[k] < r.Width && (k = colStarts.Length - 1 || colStarts[k + 1] > colEnds[k]) then vertical (colEnds[k] - 1.0)
            for k in 0 .. rowStarts.Length - 1 do
                if k > 0 && rowStarts[k] > rowEnds[k - 1] then
                    list.FillRect(Rect.Create(r.X, r.Y + rowEnds[k - 1], r.Width, rowStarts[k] - rowEnds[k - 1]), gridColor.WithAlpha 0.18f)
                if rowStarts[k] > 0.0 then horizontal rowStarts[k]
                if rowEnds[k] < r.Height && (k = rowStarts.Length - 1 || rowStarts[k + 1] > rowEnds[k]) then horizontal (rowEnds[k] - 1.0)
        | ValueNone -> ()

    /// Which of an element's edges a clip (a ScrollViewer's viewport) cuts off, given its full rect
    /// and its visible part.
    [<Struct>]
    type internal Cuts =
        { Left: bool; Top: bool; Right: bool; Bottom: bool }
        static member Of(full: Rect, visible: Rect) =
            { Left = visible.X > full.X + 0.5; Top = visible.Y > full.Y + 0.5
              Right = visible.Right < full.Right - 0.5; Bottom = visible.Bottom < full.Bottom - 0.5 }

    /// A dashed line along a cut edge of `v`, meaning "the element goes on past here": blue dashes
    /// on a dark edge, like the corner marks, so it stands out on light, dark and blue alike.
    let private cutEdges (list: DrawList) (window: Size) (v: Rect) (cuts: Cuts) =
        let t, dash, gap = 2.0, 8.0, 5.0
        let dark = Color.Hex 0x11111B
        let dashes (horizontal: bool) (x: float) (y: float) (length: float) =
            let mutable at = 0.0
            while at < length do
                let n = min dash (length - at)
                let r = if horizontal then Rect.Create(x + at, y, n, t) else Rect.Create(x, y + at, t, n)
                list.FillRect(Rect.Create(r.X - 1.0, r.Y - 1.0, r.Width + 2.0, r.Height + 2.0), dark)
                list.FillRect(r, boxColor)
                at <- at + dash + gap
        // Just outside the cut, as far out as the corner marks (3 DIPs), so they line up with them
        // and don't sit on the element's own content (text in a list). Just inside when outside
        // would be off the window (a list at the window's edge).
        let out = 3.0
        let before (edge: float) = if edge - out - t - 1.0 >= 0.0 then edge - out - t else edge + 1.0
        let after (edge: float) (limit: float) = if edge + out + t + 1.0 <= limit then edge + out else edge - t - 1.0
        if cuts.Top then dashes true v.X (before v.Y) v.Width
        if cuts.Bottom then dashes true v.X (after v.Bottom window.Height) v.Width
        if cuts.Left then dashes false (before v.X) v.Y v.Height
        if cuts.Right then dashes false (after v.Right window.Width) v.Y v.Height

    let private render (ctx: RenderContext) (list: DrawList) =
        match ctx.Get HighlightProps.target with
        | ValueSome t when t.Root.IsSome && t.Bounds.Width >= 0.0 && t.Bounds.Height >= 0.0 ->
            let p = t.PositionInTree
            let b = t.Bounds
            let r = Rect.Create(p.X, p.Y, b.Width, b.Height)
            // Only the part the ancestors' clips let show (a ScrollViewer's viewport); nothing when
            // it's scrolled out entirely.
            let v = t.VisibleBounds
            if v.IsEmpty then () else
            let cuts = Cuts.Of(r, v)
            list.PushClip v
            gridLines list t r
            list.PopClip()
            if ctx.Get HighlightProps.outlineOnly then
                // Corner marks 3 DIPs outside the element, like a viewfinder: the sides stay clear, so
                // its edges, border and shadow show, and the corners still show its exact extent.
                // Blue on a dark edge, so they stand out on light, dark and blue alike. Only at
                // corners that show; a cut edge gets a dashed line instead.
                let t, gap = 2.0, 3.0
                let o = Rect.Create(r.X - gap - t, r.Y - gap - t, r.Width + 2.0 * (gap + t), r.Height + 2.0 * (gap + t))
                // Arms up to 8 long, shorter on small elements so the marks never meet.
                let armX, armY = min 8.0 (o.Width / 2.0 - 1.0), min 8.0 (o.Height / 2.0 - 1.0)
                let marks (grow: float) (color: Color) =
                    let bar (x: float) (y: float) (w: float) (h: float) =
                        list.FillRect(Rect.Create(x - grow, y - grow, w + 2.0 * grow, h + 2.0 * grow), color)
                    let right, bottom = o.X + o.Width, o.Y + o.Height
                    if not (cuts.Top || cuts.Left) then (bar o.X o.Y armX t; bar o.X o.Y t armY)
                    if not (cuts.Top || cuts.Right) then (bar (right - armX) o.Y armX t; bar (right - t) o.Y t armY)
                    if not (cuts.Bottom || cuts.Left) then (bar o.X (bottom - t) armX t; bar o.X (bottom - armY) t armY)
                    if not (cuts.Bottom || cuts.Right) then (bar (right - armX) (bottom - t) armX t; bar (right - t) (bottom - armY) t armY)
                marks 1.0 (Color.Hex 0x11111B)
                marks 0.0 boxColor
                cutEdges list ctx.Size v cuts
            else
            let m = t.Get LayoutProps.margin
            // The margin bands, cut by the same clips (they're outside the element, but inside its parent).
            list.PushClip(t.VisiblePart(Rect.Create(-m.Left, -m.Top, b.Width + m.Left + m.Right, b.Height + m.Top + m.Bottom)))
            let outerWidth = r.Width + m.Left + m.Right
            if m.Top > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y - m.Top, outerWidth, m.Top), marginColor)
            if m.Bottom > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y + r.Height, outerWidth, m.Bottom), marginColor)
            if m.Left > 0.0 then list.FillRect(Rect.Create(r.X - m.Left, r.Y, m.Left, r.Height), marginColor)
            if m.Right > 0.0 then list.FillRect(Rect.Create(r.X + r.Width, r.Y, m.Right, r.Height), marginColor)
            list.PopClip()
            list.PushClip v
            list.FillRect(r, boxColor.WithAlpha 0.3f)
            list.StrokeRect(r, CornerRadius.Zero, 1.0, boxColor)
            list.PopClip()
            cutEdges list ctx.Size v cuts
            // The label (the full size): above the element and its margin, else below, else just
            // inside the top of what shows (a cut top edge, or no room either way).
            let text = Format.label t + "  " + Format.size b.Width b.Height
            let layout = ctx.Text.Layout(text, ctx.Get TextProps.font, 12.0, Double.PositiveInfinity, NoWrap, NoTrimming, 0)
            let w, h = layout.Width + 12.0, layout.Height + 6.0
            let above = r.Y - m.Top - h - 4.0
            let below = r.Y + r.Height + m.Bottom + 4.0
            let y =
                if not cuts.Top && above >= 0.0 then above
                elif not cuts.Bottom && below + h <= ctx.Size.Height then below
                else min (v.Y + 4.0) (ctx.Size.Height - h)
            let left = if cuts.Left then v.X else r.X - m.Left
            let x = max 0.0 (min left (ctx.Size.Width - w))
            list.FillRoundedRect(Rect.Create(x, y, w, h), CornerRadius.Uniform 4.0, Color.Hex 0x11111B)
            ctx.Text.DrawLayout(list, layout, x + 6.0, y + 3.0, layout.Width, TextAlignment.Start, Color.Hex 0xE6E6F0)
        | _ -> ()

    let Type = Element.define "InspectorHighlight" [ Element.render render ]
    let create (settings: Setting<Highlight> list) = View.Create(Type, settings)

module internal ChevronProps =
    let isOpen = Prop.define "IsOpen" false Affects.Render
    let color = Prop.define "Color" Color.White Affects.Render

/// A small triangle: pointing right (closed) or down (open). Inter has no triangle glyphs, so it's
/// drawn as thin strips.
[<AbstractClass>]
type internal Chevron =
    inherit Layoutable
    static member isOpen<'k when 'k :> Chevron>(value: bool) : Setting<'k> = Setting.create ChevronProps.isOpen value
    static member color<'k when 'k :> Chevron>(value: Color) : Setting<'k> = Setting.create ChevronProps.color value

module internal Chevron =
    let private render (ctx: RenderContext) (list: DrawList) =
        let color = ctx.Get ChevronProps.color
        let cx, cy = ctx.Size.Width / 2.0, ctx.Size.Height / 2.0
        let strips = 8
        let step = 0.5
        for i in 0 .. strips - 1 do
            // From the triangle's base (full length) to its tip.
            let along = float i * step
            let length = 8.0 * (1.0 - float i / float strips)
            let r =
                if ctx.Get ChevronProps.isOpen then Rect.Create(cx - length / 2.0, cy - 2.0 + along, length, step)
                else Rect.Create(cx - 2.0 + along, cy - length / 2.0, step, length)
            list.FillRect(r, color, EdgeMode.Precise)

    let Type =
        Element.define "InspectorChevron" [
            Element.measure (fun _ _ -> Size.Create(12.0, 12.0))
            Element.render render
        ]

    let create (settings: Setting<Chevron> list) = View.Create(Type, settings)

/// The corner grip of a floating panel: a small triangle of dots.
[<AbstractClass>]
type internal Grip =
    inherit Layoutable

module internal Grip =
    let Type =
        Element.define "InspectorGrip" [
            Element.measure (fun _ _ -> Size.Create(16.0, 16.0))
            Element.render (fun ctx list ->
                let color = (Color.Hex 0x8A8AA0).WithAlpha 0.8f
                let s = ctx.Size
                for i, j in [ 0, 0; 1, 0; 0, 1; 2, 0; 1, 1; 0, 2 ] do
                    list.FillRect(Rect.Create(s.Width - 5.0 - float i * 4.0, s.Height - 5.0 - float j * 4.0, 2.0, 2.0), color))
        ]

    let create (settings: Setting<Grip> list) = View.Create(Type, settings)

/// Children left to right, wrapping onto the next line when the width runs out (the state toggles;
/// until M8's wrap panel). Lines are as tall as their tallest child; children are centered in them.
[<AbstractClass>]
type internal WrapRow =
    inherit Panel

module internal WrapRow =
    let private gap = 4.0

    /// Each child's top-left and the size used, given each child's size.
    let private place (width: float) (sizes: Size list) =
        let mutable x, y, line = 0.0, 0.0, 0.0
        let spots =
            [ for s in sizes do
                  if x > 0.0 && x + s.Width > width then
                      x <- 0.0
                      y <- y + line + gap
                      line <- 0.0
                  yield (x, y)
                  x <- x + s.Width + gap
                  line <- max line s.Height ]
        spots, y + line

    let Type =
        Element.define "InspectorWrapRow" [
            Element.measure (fun ctx available ->
                let sizes = [ for c in ctx.Children -> ctx.Measure(c, Size.Create(available.Width, Double.PositiveInfinity)) ]
                let _, height = place available.Width sizes
                let widest = if sizes.IsEmpty then 0.0 else sizes |> List.sumBy (fun s -> s.Width + gap)
                Size.Create(min available.Width widest, height))
            Element.arrange (fun ctx size ->
                let children = List.ofSeq ctx.Children
                let sizes = [ for c in children -> ctx.DesiredSize c ]
                let spots, _ = place size.Width sizes
                // Line heights, to center each child in its line.
                let lines = Dictionary<float, float>()
                for (_, y), s in List.zip spots sizes do
                    lines[y] <- max s.Height (match lines.TryGetValue y with | true, h -> h | _ -> 0.0)
                for c, ((x, y), s) in List.zip children (List.zip spots sizes) do
                    ctx.Arrange(c, Rect.Create(x, y + (lines[y] - s.Height) / 2.0, s.Width, s.Height)))
        ]

    let create (settings: Setting<WrapRow> list) = View.Create(Type, settings)

/// The panel component's props: the shared state, and whether it fills the pop-out window.
type internal PanelProps = { State: InspectorState; IsPoppedOut: bool }

/// The header's props: what its buttons depend on (equal props, no re-run).
type internal HeaderProps =
    { State: InspectorState
      IsPoppedOut: bool
      IsPicking: bool
      IsFloating: bool
      Side: DockSide
      IsCollapsed: bool
      CanPopOut: bool }

/// Props for the parts that only change with the content (`InspectorState.ContentVersion`), not
/// with the panel's place or size: equal props, no re-run.
type internal SectionProps = { State: InspectorState; Version: int }

/// The toolbar's icons: Google's Material Symbols (outlined, weight 400), © Google, Apache License 2.0
/// (https://www.apache.org/licenses/LICENSE-2.0). Path data in Google's box (0, -960, 960, 960).
module internal InspectorIcons =
    let private material name data =
        Icon.fromPath data [ Icon.name name; Icon.viewBox (0.0, -960.0, 960.0, 960.0) ]

    let pick = material "arrow_selector_tool" "M320 -410 399 -520H569L320 -716ZM551 -80 406 -392 240 -160V-880L800 -440H516L660 -131ZM399 -520Z"
    /// A window with a narrow pane on the left (Material calls it dock_to_right: the content docks right).
    let dockLeft =
        material
            "dock_left"
            "M200 -120Q167 -120 144 -144Q120 -167 120 -200V-760Q120 -793 144 -816Q167 -840 200 -840H760Q793 -840 816 -816Q840 -793 840 -760V-200Q840 -167 816 -144Q793 -120 760 -120ZM320 -200V-760H200Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200ZM400 -200H760Q760 -200 760 -200Q760 -200 760 -200V-760Q760 -760 760 -760Q760 -760 760 -760H400ZM320 -200H200Q200 -200 200 -200Q200 -200 200 -200Q200 -200 200 -200Q200 -200 200 -200H320Z"
    /// The same, mirrored: the pane on the right.
    let dockRight = dockLeft |> Icon.flipHorizontal
    let popOut =
        material
            "open_in_new"
            "M200 -120Q167 -120 144 -144Q120 -167 120 -200V-760Q120 -793 144 -816Q167 -840 200 -840H480V-760H200Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200H760Q760 -200 760 -200Q760 -200 760 -200V-480H840V-200Q840 -167 816 -144Q793 -120 760 -120ZM388 -332 332 -388 704 -760H560V-840H840V-560H760V-704Z"
    let popIn = material "close_fullscreen" "M136 -80 80 -136 344 -400H160V-480H480V-160H400V-344ZM480 -480V-800H560V-616L824 -880L880 -824L616 -560H800V-480Z"
    /// Folding the panel to its title bar, like a window's minimize; restoring it, like a window's square.
    let fold = material "minimize" "M240 -120V-200H720V-120Z"
    let unfold =
        material
            "crop_square"
            "M200 -120Q167 -120 144 -144Q120 -167 120 -200V-760Q120 -793 144 -816Q167 -840 200 -840H760Q793 -840 816 -816Q840 -793 840 -760V-200Q840 -167 816 -144Q793 -120 760 -120ZM200 -200H760Q760 -200 760 -200Q760 -200 760 -200V-760Q760 -760 760 -760Q760 -760 760 -760H200Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200ZM200 -200Q200 -200 200 -200Q200 -200 200 -200V-760Q200 -760 200 -760Q200 -760 200 -760Q200 -760 200 -760Q200 -760 200 -760V-200Q200 -200 200 -200Q200 -200 200 -200Z"
    let close = material "close" "M256 -200 200 -256 424 -480 200 -704 256 -760 480 -536 704 -760 760 -704 536 -480 760 -256 704 -200 480 -424Z"

// --- The views ---------------------------------------------------------------------------------------

module internal Views =
    // Shown in their own words on the states line ("hovered", "focused (visible)"), not by name.
    let private builtInStates = [ States.hover; States.pressed; States.focused; States.focusVisible; States.disabled ]
    let private panelBackground = (Color.Hex 0x1B1B2A).WithAlpha 0.97f
    let private dim = Color.Hex 0x8A8AA0
    let private typeColor = Color.Hex 0x8AB4F8
    let private componentColor = Color.Hex 0xC792EA
    let private nameColor = Color.Hex 0xF2C27B
    /// Forced states: their toggles (background) and the tree's marker (text). Teal, apart from the
    /// selection's blue, components' purple and names' yellow.
    let private forcedColor = Color.Hex 0x1F7A70
    let private forcedTextColor = Color.Hex 0x5CC8B8
    let private hintColor = Color.Hex 0xB8B8C8
    let private indentStep = 12.0

    /// Re-runs the component whenever the state says it changed.
    let private follow (ctx: ComponentContext) (state: InspectorState) =
        let version = ctx.useState 0
        ctx.useSubscription(state, fun (s: InspectorState) -> s.Changed.Subscribe(fun () -> version.Update((+) 1)))

    let private text (value: string) (color: Color) =
        TextBlock.create [
            TextBlock.text value
            TextBlock.fontSize 12.0
            TextBlock.foreground color
            TextBlock.verticalAlignment VerticalAlignment.Center
        ]

    let private chevron (shown: bool) (open': bool) =
        Chevron.create [
            Chevron.isOpen open'
            Chevron.color (if shown then dim else Color.Transparent)
            Chevron.verticalAlignment VerticalAlignment.Center
        ]

    let private row (state: InspectorState) (r: Row) =
        match r with
        | Row.Layer l ->
            let details = "order " + l.Order.ToString() + (if l.IsModal then " · modal" else "")
            Border.create [
                Border.height 22.0 // every row is 22 DIPs (the tree's ItemsView has fixed heights)
                Border.padding (Thickness.Create(6.0, 0.0, 6.0, 0.0))
                Border.background (Color.Hex 0x26263A)
                Border.onPointerDown (fun _ -> state.ToggleLayer l)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.spacing 6.0
                        StackPanel.children [
                            chevron true (not (state.IsCollapsed l))
                            text ("Layer " + l.Name) Color.White
                            text details dim
                        ]
                    ]
                )
            ]
        | Row.Element(e, depth) ->
            let isSelected = state.Selected |> ValueOption.exists (fun s -> obj.ReferenceEquals(s, e))
            let isHovered = state.Hovered |> ValueOption.exists (fun s -> obj.ReferenceEquals(s, e))
            let hasChildren = e.Children.Count > 0
            let isOpen = hasChildren && state.IsExpanded e
            let left = 6.0 + float depth * indentStep
            // Hidden elements (and everything inside them) are dimmed.
            let fade (c: Color) = if e.IsVisible then c else c.WithAlpha 0.4f
            // The last part of the indent sits inside the label, so bringing the label into view sideways
            // keeps a little of it to the left (M8 step 3a++).
            let inset = min left 24.0
            Border.create [
                Border.height 22.0
                Border.padding (Thickness.Create(left - inset, 0.0, 6.0, 0.0))
                Border.background (
                    if isSelected then (Color.Hex 0x3D59A1).WithAlpha 0.6f
                    elif isHovered then Color.White.WithAlpha 0.07f
                    else Color.Transparent
                )
                // The chevron toggles; anywhere else selects.
                Border.onPointerDown (fun a ->
                    if hasChildren && a.LocalPosition.X < left + 14.0 then state.ToggleExpanded e else state.ClickRow e)
                Border.onPointerEnter (fun _ -> state.HoverRow e)
                Border.onPointerLeave (fun _ -> state.LeaveRow e)
                Border.child (
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.spacing 4.0
                        StackPanel.children [
                            Border.create [
                                if isSelected then Border.name Names.SelectedLabel
                                Border.padding (Thickness.Create(inset, 0.0, 0.0, 0.0))
                                Border.child (
                                    StackPanel.create [
                                        StackPanel.orientation Horizontal
                                        StackPanel.spacing 4.0
                                        StackPanel.children [
                                            chevron hasChildren isOpen
                                            text (Format.label e) (fade (if e.ComponentName.IsSome then componentColor else typeColor))
                                        ]
                                    ]
                                )
                            ]
                            if e.Name.Length > 0 then text ("#" + e.Name) (fade nameColor)
                            if not e.ForcedStates.IsEmpty then text "• forced" forcedTextColor
                            // An ItemsView's count shows open or not; text hints only on collapsed rows.
                            let realized = Format.realized e
                            if realized.Length > 0 then text realized (fade hintColor)
                            elif not isOpen then
                                let hint = Format.hint e
                                if hint.Length > 0 then text hint (fade hintColor)
                        ]
                    ]
                )
            ]

    let private rowKey (r: Row) : obj =
        match r with
        | Row.Layer l -> box l
        | Row.Element(e, _) -> box e

    let private smallButton (label: string) (isActive: bool) (onClick: unit -> unit) =
        Button.create [
            Button.padding (7.0, 2.0)
            Button.verticalAlignment VerticalAlignment.Center
            Button.onClick (fun _ -> onClick ())
            if isActive then Button.background (Color.Hex 0x3D59A1)
            Button.child (
                TextBlock.create [
                    TextBlock.text label
                    TextBlock.fontSize 12.0
                    TextBlock.foreground (Color.Hex 0xE6E6F0)
                    TextBlock.horizontalAlignment HorizontalAlignment.Center
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        ]

    /// Press, move and release on an element drive one kind of drag (moves keep coming to the
    /// element that holds the press, wherever the pointer goes).
    let private dragHandlers<'k when 'k :> Layoutable> (state: InspectorState) (kind: DragKind) : Setting<'k> list =
        [ Layoutable.onPointerDown (fun a -> state.BeginDrag(kind, a.Position))
          Layoutable.onPointerMove (fun a -> state.DragTo a.Position)
          Layoutable.onPointerUp (fun _ -> state.EndDrag()) ]

    /// The title, and the area to drag the panel by (double-click collapses). While the pointer is
    /// over a toolbar button, it says what the button does instead.
    let private title (state: InspectorState) (draggable: bool) (hint: string) =
        Border.create (
            [ Border.width "1*"
              Border.padding (Thickness.Create(12.0, 0.0, 4.0, 0.0))
              Border.child (
                  TextBlock.create [
                      if hint.Length > 0 then
                          TextBlock.text hint
                          TextBlock.foreground (Color.Hex 0xB8B8CC)
                          TextBlock.textTrimming CharacterEllipsis
                      else
                          TextBlock.text "Inspector"
                          TextBlock.font (Fonts.SemiBold())
                          TextBlock.foreground Color.White
                      TextBlock.verticalAlignment VerticalAlignment.Center
                  ]
              ) ]
            @ (if draggable then dragHandlers state DragKind.Move else [])
        )

    /// A toolbar button: an icon, transparent until hovered (accent-filled when `isActive`);
    /// hovering shows `hint` in the title.
    let private toolButton (name: string) (icon: Icon) (hint: string) (isActive: bool) (showHint: string -> unit) (onClick: unit -> unit) =
        Button.create [
            Button.name name
            Button.icon icon
            Button.iconSize 18.0
            Button.width 28.0
            Button.height 28.0
            Button.padding 0.0
            Button.borderThickness 0.0
            Button.cornerRadius 5.0
            Setting.create BorderProps.shadow ValueNone
            Button.background (if isActive then Color.Hex 0x3D59A1 else Color.Transparent)
            Button.foreground (if isActive then Color.White else Color.Hex 0xC8C8D8)
            Button.verticalAlignment VerticalAlignment.Center
            Button.onPointerEnter (fun _ -> showHint name)
            Button.onPointerLeave (fun _ -> showHint "")
            Button.onClick (fun _ -> onClick ())
        ]

    /// A thin line between groups of toolbar buttons.
    let private toolSeparator () =
        Border.create [
            Border.width 1.0
            Border.height 16.0
            Border.margin (Thickness.Create(3.0, 0.0, 3.0, 0.0))
            Border.verticalAlignment VerticalAlignment.Center
            Border.background (Color.White.WithAlpha 0.12f)
        ]

    /// The title and the toolbar. Its own component, so hovering a button (which changes the
    /// title) re-runs only this.
    let private header =
        Component.define "InspectorHeader" (fun ctx (p: HeaderProps) ->
            let state = p.State
            // The hovered button's name; its hint shows only while that button is still there
            // (a click can remove it before the pointer leaves).
            let hovered = ctx.useState ""
            let show = hovered.Set
            let buttons = [
                yield "pick", InspectorIcons.pick, (if p.IsPicking then "Stop picking (Esc)" else "Pick (Ctrl+Shift+C)"), p.IsPicking, state.TogglePick
                if p.IsPoppedOut then
                    yield "popIn", InspectorIcons.popIn, "Pop back in", false, state.PopIn
                else
                    if p.IsFloating || p.Side = DockSide.Right then
                        yield "dockLeft", InspectorIcons.dockLeft, "Dock left", false, (fun () -> state.Dock DockSide.Left)
                    if p.IsFloating || p.Side = DockSide.Left then
                        yield "dockRight", InspectorIcons.dockRight, "Dock right", false, (fun () -> state.Dock DockSide.Right)
                    if p.CanPopOut then yield "popOut", InspectorIcons.popOut, "Pop out", false, state.PopOut
                    if p.IsCollapsed then yield "fold", InspectorIcons.unfold, "Restore", false, state.ToggleCollapsed
                    else yield "fold", InspectorIcons.fold, "Minimize", false, state.ToggleCollapsed
                    yield "close", InspectorIcons.close, "Close (F12)", false, state.Toggle
            ]
            // A hovered button's hint, else pick mode's status (the pointer is out in the app while
            // picking), else the title.
            let hint =
                buttons
                |> List.tryPick (fun (name, _, hint, _, _) -> if name = hovered.Current then Some hint else None)
                |> Option.defaultValue (if p.IsPicking then "Picking… (Esc stops)" else "")
            // Groups: pick | where the panel goes | fold and close.
            let groupOf name =
                match name with
                | "pick" -> 0
                | "fold" | "close" -> 2
                | _ -> 1
            StackPanel.create [
                StackPanel.orientation Horizontal
                StackPanel.spacing 2.0
                StackPanel.height 36.0
                StackPanel.margin (Thickness.Create(0.0, 0.0, 6.0, 0.0))
                StackPanel.children [
                    yield title state (not p.IsPoppedOut) hint
                    let mutable group = 0
                    for (name, icon, text, isActive, onClick) in buttons do
                        if groupOf name <> group then
                            group <- groupOf name
                            yield toolSeparator ()
                        yield toolButton name icon text isActive show onClick
                ]
            ])

    let private headerFor (state: InspectorState) (isPoppedOut: bool) =
        header
            { State = state
              IsPoppedOut = isPoppedOut
              IsPicking = state.IsPicking
              IsFloating = state.Mode = PanelMode.Floating
              Side = state.Side
              IsCollapsed = state.IsPanelCollapsed
              CanPopOut = state.CanPopOut }

    /// Frame stats (as of the last refresh) and, when floating, the corner grip.
    let private footer (state: InspectorState) (showGrip: bool) =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.children [
                yield
                    TextBlock.create [
                        TextBlock.width "1*"
                        TextBlock.margin (Thickness.Create(10.0, 6.0, 10.0, 8.0))
                        TextBlock.text state.StatsText
                        TextBlock.fontSize 11.0
                        TextBlock.foreground dim
                        TextBlock.textTrimming CharacterEllipsis
                    ]
                if showGrip then
                    yield
                        Grip.create (
                            [ Grip.verticalAlignment VerticalAlignment.Bottom; Grip.cursor Cursor.ResizeDiagonal ]
                            @ dragHandlers state DragKind.ResizeCorner
                        )
            ]
        ]

    /// Between the tree and the properties: drag it up or down.
    let private divider (state: InspectorState) =
        Border.create (
            [ Border.height 7.0
              Border.cursor Cursor.ResizeUpDown
              Border.background Color.Transparent
              Border.child (Border.create [ Border.height 1.0; Border.background (Color.Hex 0x3A3A55); Border.verticalAlignment VerticalAlignment.Center ]) ]
            @ dragHandlers state DragKind.Split
        )

    let private cell (value: string) (color: Color) (width: Length) =
        TextBlock.create [
            TextBlock.width width
            TextBlock.text value
            TextBlock.fontSize 12.0
            TextBlock.foreground color
            TextBlock.textTrimming CharacterEllipsis
            TextBlock.verticalAlignment VerticalAlignment.Center
        ]

    /// One line of the properties: a name, a value and (for properties) where the value comes from.
    let private line (name: string) (value: string) (valueColor: Color) (source: string) =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.height 20.0
            StackPanel.margin (Thickness.Create(10.0, 0.0, 10.0, 0.0))
            StackPanel.children [
                yield cell name dim (Px 128.0)
                yield cell value valueColor (Star 1.0)
                if source.Length > 0 then yield cell source dim (Px 52.0)
            ]
        ]

    /// A line whose value wraps instead of ending in "…" (the states, which can be many). One line
    /// tall, it looks exactly like `line`.
    let private wrappingLine (name: string) (value: string) (valueColor: Color) =
        StackPanel.create [
            StackPanel.orientation Horizontal
            StackPanel.minHeight 20.0
            StackPanel.margin (Thickness.Create(10.0, 0.0, 10.0, 0.0))
            StackPanel.children [
                cell name dim (Px 128.0)
                TextBlock.create [
                    TextBlock.width (Star 1.0)
                    TextBlock.text value
                    TextBlock.fontSize 12.0
                    TextBlock.foreground valueColor
                    TextBlock.textWrapping Wrap
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            ]
        ]

    let private heading (value: string) =
        TextBlock.create [
            TextBlock.margin (Thickness.Create(10.0, 8.0, 10.0, 2.0))
            TextBlock.text value
            TextBlock.fontSize 11.0
            TextBlock.font (Fonts.SemiBold())
            TextBlock.foreground dim
        ]

    /// Settings are written in lower camel case (`horizontalAlignment`); properties are named in Pascal case.
    let private settingName (p: Prop) =
        let camel (n: string) = if n.Length = 0 then n else Char.ToLowerInvariant(n[0]).ToString() + n.Substring 1
        // Attached settings keep their panel's name: "Grid.Row" shows as Grid.row, as written.
        if p.AttachedTo <> "" && p.Name.StartsWith(p.AttachedTo + ".") then
            p.AttachedTo + "." + camel (p.Name.Substring(p.AttachedTo.Length + 1))
        else camel p.Name

    let private sourceText (s: ValueSource) =
        match s with
        | ValueSource.View -> "view"
        | ValueSource.Code -> "code"
        | ValueSource.Style -> "style"
        | ValueSource.Theme -> "theme"
        | ValueSource.TypeDefault -> "type"
        | ValueSource.Inherited -> "inherited"
        | ValueSource.Default -> "default"

    /// A CheckBox, controlled by the inspector's state. The inspector takes no app styles or
    /// theme, so its colors are set here (the accent while on).
    let private checkBox (label: string) (isChecked: bool) (onToggle: unit -> unit) =
        CheckBox.create [
            CheckBox.margin (Thickness.Create(10.0, 4.0, 0.0, 4.0))
            CheckBox.horizontalAlignment HorizontalAlignment.Left
            CheckBox.isChecked isChecked
            CheckBox.onCheckedChanged (fun _ -> onToggle ())
            CheckBox.background Color.Transparent
            CheckBox.borderBrush (Color.Hex 0x8A8AA0)
            CheckBox.on States.isChecked [ CheckBox.background (Color.Hex 0x3D59A1); CheckBox.borderBrush (Color.Hex 0x3D59A1) ]
            CheckBox.child (
                TextBlock.create [
                    TextBlock.text label
                    TextBlock.fontSize 12.0
                    TextBlock.foreground hintColor
                    TextBlock.verticalAlignment VerticalAlignment.Center
                ]
            )
        ]

    /// What the selected element is: states, layout numbers, properties and a component's hooks.
    /// The selected element's name and the Deselect button, above the scrolling properties (so
    /// they stay in view).
    let private selectionHeader (state: InspectorState) =
        match state.Selected with
        | ValueNone -> None
        | ValueSome e ->
            Some(
                StackPanel.create [
                    StackPanel.orientation Horizontal
                    StackPanel.spacing 6.0
                    StackPanel.margin (Thickness.Create(10.0, 6.0, 10.0, 2.0))
                    StackPanel.children [
                        yield
                            TextBlock.create [
                                TextBlock.text (Format.label e)
                                TextBlock.font (Fonts.SemiBold())
                                TextBlock.foreground (if e.ComponentName.IsSome then componentColor else typeColor)
                            ]
                        if e.Name.Length > 0 then yield text ("#" + e.Name) nameColor
                        // Pushes the deselect button to the right edge.
                        yield Border.create [ Border.width "1*" ]
                        // Not "×": the title bar's × closes the whole inspector.
                        if state.HasForced then yield smallButton "Clear forced" false state.ClearForced
                        yield smallButton "Deselect" false state.Deselect
                    ]
                ]
            )

    /// One state as a toggle: filled teal while forced on, a teal outline with the name struck through
    /// while forced off, bright while on for real, dim while off. A click forces the opposite of what
    /// it shows, or stops forcing it.
    let private stateChip (label: string) (isOn: bool) (forced: bool voption) (onClick: unit -> unit) =
        let forcedOn, forcedOff = forced = ValueSome true, forced = ValueSome false
        let bright = forcedOn || forcedOff || isOn
        Border.create [
            Border.padding (6.0, 1.0)
            Border.cornerRadius 4.0
            Border.borderThickness 1.0
            Border.borderBrush (
                if forcedOff then forcedColor
                elif forcedOn || isOn then Color.Transparent
                else Color.White.WithAlpha 0.14f
            )
            Border.background (
                if forcedOn then forcedColor
                elif isOn then Color.White.WithAlpha 0.12f
                else Color.Transparent
            )
            Border.cursor Cursor.Hand
            Border.on States.hover [ Border.overlay (Color.White.WithAlpha 0.08f) ]
            Border.onClick (fun _ -> onClick ())
            Border.child (
                // One cell: the name, and over it a line through its middle when forced off.
                Grid.create [
                    Grid.children [
                        TextBlock.create [
                            TextBlock.text label
                            TextBlock.fontSize 12.0
                            TextBlock.foreground (if bright then Color.White else dim)
                        ]
                        if forcedOff then
                            Border.create [
                                Border.height 1.0
                                Border.verticalAlignment VerticalAlignment.Center
                                Border.background (Color.White.WithAlpha 0.85f)
                            ]
                    ]
                ]
            )
        ]

    /// The STATES section: every state, built-in and custom (`States.define`, `States.ofProp`), as
    /// toggles that force them, so one that is off can be forced too.
    let private statesLine (state: InspectorState) (e: Element) =
        let active = e.ActiveStates
        let forced = e.ForcedStates
        let shown = builtInStates @ List.ofArray (States.customStates ())
        // Its own section (under the STATES heading), across the whole width.
        WrapRow.create [
            WrapRow.margin (Thickness.Create(10.0, 2.0, 10.0, 2.0))
            WrapRow.children [
                for s in shown do
                    // Forced states test as forced: "on" here means on for real (only asked when not forced).
                    let isForced =
                        match forced |> List.tryFind (fun (f, _) -> obj.ReferenceEquals(f, s)) with
                        | Some(_, on) -> ValueSome on
                        | None -> ValueNone
                    let isOn = isForced.IsNone && active |> List.exists (fun x -> obj.ReferenceEquals(x, s))
                    stateChip s.Name isOn isForced (fun () -> state.ToggleForced(e, s))
                if not e.IsVisible then text "hidden" hintColor
            ]
        ]

    let private properties (state: InspectorState) =
        match state.Selected with
        | ValueNone ->
            [ TextBlock.create [
                  TextBlock.margin 10.0
                  TextBlock.text ("Click a row, or Pick (" + Format.key state.Options.PickKey + ") and click in the app.")
                  TextBlock.fontSize 12.0
                  TextBlock.foreground dim
                  TextBlock.textWrapping Wrap
              ] ]
        | ValueSome e ->
            let p = e.PositionInTree
            let b = e.Bounds
            // What the theme can set here (in any state) always shows, even at its default; a "from
            // theme ..." line appears under it once a theme style applies.
            let themed = e.ThemedProperties
            let themedRules (p: Prop) =
                themed |> List.tryPick (fun t -> if obj.ReferenceEquals(t.Prop, p) then Some t.Selectors else None)
            let all = e.Values
            let missing =
                [ for t in themed do
                      if not (all |> List.exists (fun v -> obj.ReferenceEquals(v.Prop, t.Prop))) then
                          { Prop = t.Prop; Value = e.GetBoxed t.Prop; Source = ValueSource.Default; Selector = ""; ThemeValue = "" } ]
            let values =
                all @ missing
                |> List.filter (fun v -> state.ShowDefaults || v.Source <> ValueSource.Default || (themedRules v.Prop).IsSome)
                |> List.sortBy (fun v ->
                    if v.Source <> ValueSource.Default then 0
                    elif (themedRules v.Prop).IsSome then 1
                    else 2)
            let hooks = e.Hooks
            [ yield heading "LAYOUT"
              yield line "offered" (Format.sizeOf e.OfferedSize) hintColor ""
              yield line "desired" (Format.sizeOf e.DesiredSize) hintColor ""
              yield line "bounds" (Format.number b.X + ", " + Format.number b.Y + "  " + Format.size b.Width b.Height) hintColor "parent"
              yield line "position" (Format.number p.X + ", " + Format.number p.Y) hintColor "window"
              let realized = Format.realized e
              if realized.Length > 0 then yield line "items" realized hintColor ""
              yield heading "PROPERTIES"
              yield checkBox "Show defaults" state.ShowDefaults state.ToggleShowDefaults
              for v in values do
                  let isDefault = v.Source = ValueSource.Default
                  let shown =
                      match v.Value with
                      | :? (string list) as classes when obj.ReferenceEquals(v.Prop, LayoutProps.classes) ->
                          if classes.IsEmpty then "none" else String.Join(" ", classes |> List.map (fun c -> "." + c))
                      | value -> state.Format value
                  yield line (settingName v.Prop) shown (if isDefault then dim else Color.White) (sourceText v.Source)
                  // Under the value: the theme value it reads (`Palette.surface`), then which style set
                  // it (`from Button:hover`; the source says style or theme).
                  let unders =
                      [ // An attached setting only its parent reads: say so when the parent isn't that panel.
                        if v.Prop.AttachedTo <> "" then
                            match e.Parent with
                            | ValueSome p when p.Type.Name = v.Prop.AttachedTo -> ()
                            | _ -> "ignored: parent isn't a " + v.Prop.AttachedTo
                        if v.ThemeValue.Length > 0 then v.ThemeValue
                        if v.Source = ValueSource.Style || v.Source = ValueSource.Theme then "from " + v.Selector ]
                  for under in unders do
                      yield
                          TextBlock.create [
                              // Under the value column only (the source column stays clear); wraps.
                              TextBlock.margin (Thickness.Create(138.0, -3.0, 62.0, 3.0))
                              TextBlock.text under
                              TextBlock.fontSize 11.0
                              TextBlock.foreground dim
                              TextBlock.textWrapping Wrap
                          ]
              if not hooks.IsEmpty then
                  yield heading "HOOKS"
                  for h in hooks do
                      let value =
                          match h.Value with
                          | ValueSome v -> (if h.Status.Length > 0 then h.Status + ": " else "") + state.Format v
                          | ValueNone -> h.Status
                      yield line h.Kind value Color.White ""
              yield Border.create [ Border.height 8.0 ] ]

    // The tree, the selection's name row and the properties are components re-run only when the
    // content changes, so dragging or resizing the panel only moves it (in the browser, rebuilding
    // them on every pointer move made dragging lag).
    let private treeSection =
        Component.define "InspectorTree" (fun _ (p: SectionProps) ->
            // Only the rows in view exist (M8 step 3a+): every row is 22 DIPs, so positions are exact.
            ItemsView.create [
                ItemsView.source (p.State.Rows())
                ItemsView.key rowKey
                ItemsView.view (row p.State)
                ItemsView.layout (ItemsLayout.fixedStack 22.0)
                ItemsView.scroller p.State.TreeScroller
            ])

    /// The selection's name, Deselect and its STATES: kept above the scrolling properties, so the
    /// state toggles stay in view while scrolling.
    let private selectionSection =
        Component.define "InspectorSelection" (fun _ (p: SectionProps) ->
            match selectionHeader p.State, p.State.Selected with
            | Some h, ValueSome e ->
                StackPanel.create [
                    StackPanel.children [
                        h
                        heading "STATES"
                        statesLine p.State e
                        // Where the scrolling part starts.
                        Border.create [ Border.margin (Thickness.Create(0.0, 6.0, 0.0, 0.0)); Border.height 1.0; Border.background (Color.White.WithAlpha 0.06f) ]
                    ]
                ]
            | _ -> Border.create [])

    let private propertiesSection =
        Component.define "InspectorProperties" (fun _ (p: SectionProps) ->
            StackPanel.create [ StackPanel.children (properties p.State) ])

    /// The header, the tree, the selected element's properties and the frame stats (only the
    /// header when collapsed).
    let private content (state: InspectorState) (isPoppedOut: bool) (fillWidth: bool) =
        let section: SectionProps = { State = state; Version = state.ContentVersion }
        StackPanel.create [
            if fillWidth then StackPanel.width "1*"
            StackPanel.children [
                yield headerFor state isPoppedOut
                if isPoppedOut || not state.IsPanelCollapsed then
                    yield
                        ScrollViewer.create [
                            ScrollViewer.height "1*"
                            ScrollViewer.horizontal ScrollBarMode.Auto // deep rows scroll sideways (M8 step 3a++)
                            ScrollViewer.content (treeSection section)
                        ]
                    yield divider state
                    // The divider sets the height of the whole area: the selection's name stays put
                    // at the top, the rest scrolls.
                    yield
                        StackPanel.create [
                            StackPanel.height state.PropertiesHeight
                            StackPanel.children [
                                selectionSection section
                                ScrollViewer.create [
                                    ScrollViewer.height "1*"
                                    ScrollViewer.content (propertiesSection section)
                                ]
                            ]
                        ]
                    yield footer state (not isPoppedOut && state.Mode = PanelMode.Floating)
            ]
        ]

    /// A docked panel's inner edge: drag it to change the width. Hidden where it isn't the inner edge.
    let private edge (state: InspectorState) (isShown: bool) =
        Border.create (
            [ Border.isVisible isShown
              Border.width 6.0
              Border.cursor Cursor.ResizeLeftRight
              Border.background Color.Transparent ]
            @ dragHandlers state DragKind.ResizeEdge
        )

    /// The panel: in the app's window (docked or floating), or filling the pop-out window.
    let panel =
        Component.define "Inspector" (fun ctx (props: PanelProps) ->
            let state = props.State
            follow ctx state
            if props.IsPoppedOut then
                Border.create [ Border.background panelBackground; Border.child (content state true false) ]
            else
                let looks =
                    [ Border.isVisible (state.IsOpen && state.Mode <> PanelMode.PoppedOut)
                      Border.cornerRadius 8.0
                      Border.background panelBackground
                      Border.borderThickness 1.0
                      Border.borderBrush (Color.Hex 0x3A3A55)
                      Border.clipToBounds true
                      Border.shadow { Blur = 16.0; OffsetX = 0.0; OffsetY = 4.0; Spread = 0.0; Color = Color.Black.WithAlpha 0.5f } ]
                let placement =
                    match state.Mode with
                    | PanelMode.Floating ->
                        let r = state.FloatingRect
                        [ Border.horizontalAlignment HorizontalAlignment.Left
                          Border.verticalAlignment VerticalAlignment.Top
                          Border.margin (r.X, r.Y, 0.0, 0.0)
                          Border.width r.Width
                          if not state.IsPanelCollapsed then Border.height r.Height ]
                    | _ ->
                        let right = state.Side = DockSide.Right
                        [ Border.horizontalAlignment (if right then HorizontalAlignment.Right else HorizontalAlignment.Left)
                          Border.verticalAlignment (if state.IsPanelCollapsed then VerticalAlignment.Top else VerticalAlignment.Stretch)
                          Border.margin PanelSize.gap
                          Border.width state.DockedRect.Width ]
                // The same elements in every mode (only settings change), so whatever holds the
                // pointer keeps it: undocking mid-drag must not rebuild the title being dragged.
                let docked = state.Mode = PanelMode.Docked
                let body =
                    StackPanel.create [
                        StackPanel.orientation Horizontal
                        StackPanel.children [
                            edge state (docked && state.Side = DockSide.Right)
                            content state false true
                            edge state (docked && state.Side = DockSide.Left)
                        ]
                    ]
                Border.create (looks @ placement @ [ Border.child body ]))

    /// The highlight, over everything but the panel. In pick mode it also takes the pointer, so
    /// the app doesn't see it.
    let highlight =
        Component.define "InspectorHighlight" (fun ctx (state: InspectorState) ->
            follow ctx state
            let target = state.Target
            Border.create [
                Border.isVisible (state.IsPicking || target.IsSome)
                Border.isHitTestVisible state.IsPicking
                Border.cursor Cursor.Crosshair
                Border.onPointerMove (fun a -> state.PickAt a.Position)
                Border.onPointerLeave (fun _ -> state.PickLeft())
                Border.onPointerDown (fun a ->
                    state.PickSelect a.Position
                    a.Handle())
                Border.child (Highlight.create [ Highlight.target target; Highlight.outlineOnly state.TargetIsSelection ])
            ])

/// An inspector for Scribble apps: a panel over the app (it never changes the app's layout) with
/// the element tree of every layer, pick mode and a devtools-style highlight. F12 shows and hides
/// it; Ctrl+Shift+C picks an element (Escape cancels). The panel docks left or right, floats
/// (drag its title), collapses, and on the desktop pops out into a window of its own.
[<AbstractClass; Sealed>]
type Inspector =
    /// Which side the panel docks to (default: right). It can be switched from the panel.
    static member side(value: DockSide) = InspectorSetting(fun o -> { o with Side = value })

    /// The key that shows and hides the inspector (default: F12).
    static member toggleKey(key: Key, modifiers: Modifiers) = InspectorSetting(fun o -> { o with ToggleKey = (key, modifiers) })

    /// The key that starts pick mode (default: Ctrl+Shift+C).
    static member pickKey(key: Key, modifiers: Modifiers) = InspectorSetting(fun o -> { o with PickKey = (key, modifiers) })

    /// Shown from the start (default: hidden until the toggle key).
    static member isOpen(value: bool) = InspectorSetting(fun o -> { o with IsOpen = value })

    /// The platform to open the pop-out window on (`Inspector.enable` passes the app's; custom
    /// hosts pass theirs to get the "Pop out" button). Only platforms with several windows.
    static member platform(value: IPlatform) = InspectorSetting(fun o -> { o with Platform = ValueSome value })

    /// The app's own text for values the inspector shows (properties, hook values, list items),
    /// tried before the inspector's: `Inspector.format (function :? Model as m -> ValueSome m.Title | _ -> ValueNone)`.
    /// Without it, the inspector writes Scribble's own types and plain values itself and shows other
    /// types by name (never `ToString()` on F# records and unions: it isn't NativeAOT-safe). Several
    /// formatters are tried in order.
    static member format(formatter: obj -> string voption) =
        InspectorSetting(fun o ->
            let before = o.Format
            { o with Format = fun v -> match before v with ValueSome s -> ValueSome s | ValueNone -> formatter v })

    /// Adds the inspector to a running host (custom hosts; apps use `Inspector.enable`). Dispose
    /// the result to take it away again.
    static member attach(host: ElementHost, settings: InspectorSetting list) : IDisposable =
        let options = settings |> List.fold (fun o (InspectorSetting apply) -> apply o) InspectorOptions.Default
        let state = InspectorState(host, options)
        let highlightLayer =
            // Pick mode takes clicks, not the wheel: the app scrolls under the highlight.
            host.AddLayer [ Layer.name "Inspector highlight"; Layer.order (Int32.MaxValue - 1); Layer.appStyles false; Layer.letWheelThrough true; Layer.view (Views.highlight state) ]
        let panelLayer =
            host.AddLayer [ Layer.name "Inspector"; Layer.order Int32.MaxValue; Layer.appStyles false; Layer.view (Views.panel { State = state; IsPoppedOut = false }) ]
        // Its own theme, whatever the app's is (its layers ignore the app's styles and theme).
        let theme = Theme.dark []
        highlightLayer.Tree.Theme <- ValueSome theme
        panelLayer.Tree.Theme <- ValueSome theme
        state.Own <- [ highlightLayer; panelLayer ]
        let toggleKey, toggleMods = options.ToggleKey
        let pickKey, pickMods = options.PickKey
        host.Window.ClaimKey(toggleKey, toggleMods)
        host.Window.ClaimKey(pickKey, pickMods)

        // The pop-out window: its own host on the same device and text system, showing the panel.
        let popped: (ElementHost * IDisposable) voption ref = ref ValueNone
        let appTitle () =
            if options.FollowAppTitle then host.AppValues.Get WindowTitle.current else host.Window.Title
        let closeWindow () =
            match popped.Value with
            | ValueSome(h, keys) ->
                popped.Value <- ValueNone
                keys.Dispose()
                h.Close()
                (h :> IDisposable).Dispose()
            | ValueNone -> ()
        let openWindow () =
            match options.Platform with
            | ValueSome platform when popped.Value.IsNone ->
                let popHost: ElementHost voption ref = ref ValueNone
                let onEvent (e: PlatformEvent) =
                    match popHost.Value with
                    | ValueSome h ->
                        match e with
                        | CloseRequested -> state.PopIn() // closes the window
                        | _ -> h.HandleEvent e |> ignore
                    | ValueNone -> ()
                let title = "Inspector – " + appTitle ()
                let size: DipSize = { Width = 440.0; Height = 720.0 }
                // Owned by the app's window: it stays above it (also after clicks in the app), minimizes
                // with it, and has no taskbar button of its own.
                let windowOptions = { WindowOptions.Default with Title = title; Size = size; Owner = ValueSome host.Window }
                let window = platform.CreateWindow(windowOptions, onEvent)
                let hostSettings =
                    [ yield ElementHost.background (Color.Hex 0x1B1B2A)
                      yield ElementHost.theme theme
                      match host.Dispatcher with
                      | ValueSome d -> yield ElementHost.dispatcher d
                      | ValueNone -> () ]
                let h = ElementHost.create window host.Device host.Text hostSettings
                popHost.Value <- ValueSome h
                h.Update(Views.panel { State = state; IsPoppedOut = true })
                popped.Value <- ValueSome(h, h.AddKeyPreview state.OnKey)
                window.Show()
                window.RequestFrame()
            | _ -> ()

        let subscriptions =
            [ host.AddKeyPreview state.OnKey
              host.LayerChanged.Subscribe state.LayerChanged
              host.FrameDrawn.Subscribe state.AfterFrame
              state.PopOutRequested.Subscribe(fun () -> openWindow ())
              state.PopInRequested.Subscribe(fun () -> closeWindow ())
              // Closing the app closes the pop-out too (otherwise its window keeps the app running).
              host.Closed.Subscribe(fun () -> closeWindow ())
              // The pop-out keeps the app's title in its own.
              if options.FollowAppTitle then
                  host.AppValues.Subscribe(WindowTitle.current, fun t ->
                      popped.Value |> ValueOption.iter (fun (h, _) -> h.Window.Title <- "Inspector – " + t)) ]
        { new IDisposable with
            member _.Dispose() =
                for s in subscriptions do
                    s.Dispose()
                closeWindow ()
                state.ClearForced()
                host.RemoveLayer highlightLayer
                host.RemoveLayer panelLayer }

    /// Turns the inspector on for an app: `Inspector.enable [ Inspector.side DockSide.Left ]` in
    /// its settings. Leave it out of release builds with `#if DEBUG` if you like.
    static member enable<'Msg>(settings: InspectorSetting list) : AppSetting<'Msg> =
        App.onStart (fun ctx ->
            let fromApp = InspectorSetting(fun o -> { o with Platform = ValueSome ctx.Platform; FollowAppTitle = true })
            Inspector.attach (ctx.Host, fromApp :: settings) |> ignore)
