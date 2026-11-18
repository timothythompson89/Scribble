namespace Scribble.Inspector

open System
open System.Collections.Generic
open System.Diagnostics
open System.Threading.Tasks
open Scribble.Platform
open Scribble.Rendering
open Scribble.Text
open Scribble.Core

/// Text for keys and values, by our own code (never `string` on unions: it isn't AOT-safe).
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
        // Cursor.fromIcon's key starts "icon|<the icon's description>|...", Cursor.fromImage's "image|<the image's key>|...".
        | Cursor.Image image ->
            let parts = image.Key.Split('|', 3)
            if parts.Length > 1 && (parts[0] = "icon" || parts[0] = "image") then parts[0] + " " + parts[1] else "image"

    /// An image by its key ("resource:Assembly/name", "url:...", ...), and its density when not 1.
    let image (i: Image) =
        if i.Density = 1.0 then i.Key else i.Key + " at density " + number i.Density

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
            | :? Image as i -> image i
            | :? (Image voption) as i -> (match i with ValueSome x -> image x | ValueNone -> "none")
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
