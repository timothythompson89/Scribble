namespace Scribble.Text

open System
open System.Buffers.Binary
open System.Collections.Generic

/// A point of a glyph outline, in font units (y up). `OnCurve = false` marks a quadratic control point.
[<Struct>]
type OutlinePoint = { X: float; Y: float; OnCurve: bool }

/// One layer of a color glyph: an outline glyph painted in one color.
/// `Color` is None for "the text's foreground color" (COLR palette index 0xFFFF).
[<Struct>]
type ColorLayer =
    { Glyph: int
      /// sRGB bytes, straight alpha: R, G, B, A.
      Color: struct (byte * byte * byte * byte) voption }

/// Thrown when a font file is malformed or uses a feature Scribble doesn't read yet.
exception FontFormatException of string

/// A parsed TrueType (glyf-based OpenType) font: metrics and glyph outlines.
///
/// Scribble reads these tables itself: head, hhea, maxp, hmtx, loca, glyf, plus cmap (to know
/// which characters a font has, for fallback) and COLR v0 + CPAL (color emoji). Shaping (GSUB,
/// GPOS) is HarfBuzz's job. CFF-based fonts (.otf with PostScript outlines), COLR v1 paint graphs
/// and variable fonts aren't supported yet.
[<Sealed>]
type FontFace(data: byte[]) =
    let u16 (o: int) = int (BinaryPrimitives.ReadUInt16BigEndian(ReadOnlySpan(data, o, 2)))
    let i16 (o: int) = int (BinaryPrimitives.ReadInt16BigEndian(ReadOnlySpan(data, o, 2)))
    let u32 (o: int) = int (BinaryPrimitives.ReadUInt32BigEndian(ReadOnlySpan(data, o, 4)))

    let tables =
        if data.Length < 12 then raise (FontFormatException "File too small to be a font.")
        let version = u32 0
        if version = 0x4F54544F then raise (FontFormatException "CFF-based OpenType fonts aren't supported yet; use a TrueType (glyf) font.")
        if version <> 0x00010000 && version <> 0x74727565 then raise (FontFormatException "Not a TrueType font.")
        let count = u16 4
        dict [ for i in 0 .. count - 1 ->
                   let record = 12 + i * 16
                   Text.Encoding.ASCII.GetString(data, record, 4), struct (u32 (record + 8), u32 (record + 12)) ]

    let table (tag: string) =
        match tables.TryGetValue tag with
        | true, struct (offset, _) -> offset
        | _ -> raise (FontFormatException $"The font has no '{tag}' table.")

    let head = table "head"
    let hhea = table "hhea"
    let hmtx = table "hmtx"
    let loca = table "loca"
    let glyf = table "glyf"

    let unitsPerEm = u16 (head + 18)
    let longLoca = i16 (head + 50) = 1
    let glyphCount = u16 (table "maxp" + 4)
    let ascender = i16 (hhea + 4)
    let descender = i16 (hhea + 6)
    let lineGap = i16 (hhea + 8)
    let hMetricCount = u16 (hhea + 34)

    let glyphRange (glyph: int) =
        if glyph < 0 || glyph >= glyphCount then
            struct (0, 0)
        elif longLoca then
            struct (u32 (loca + glyph * 4), u32 (loca + glyph * 4 + 4))
        else
            struct (u16 (loca + glyph * 2) * 2, u16 (loca + glyph * 2 + 2) * 2)

    let tryTable (tag: string) =
        match tables.TryGetValue tag with
        | true, struct (offset, _) -> ValueSome offset
        | _ -> ValueNone

    // --- cmap: character → glyph -------------------------------------------------------------

    /// Picks the best Unicode subtable: format 12 (full Unicode) before format 4 (BMP only).
    let cmapSubtable =
        match tryTable "cmap" with
        | ValueNone -> ValueNone
        | ValueSome cmap ->
            let records =
                [ for i in 0 .. u16 (cmap + 2) - 1 ->
                      let r = cmap + 4 + i * 8
                      let platform, encoding = u16 r, u16 (r + 2)
                      let offset = cmap + u32 (r + 4)
                      platform, encoding, offset, u16 offset ]
            let pick (format: int) (accept: int * int -> bool) =
                records |> List.tryFind (fun (p, e, _, f) -> f = format && accept (p, e))
            [ pick 12 (fun (p, e) -> (p = 3 && e = 10) || p = 0)
              pick 4 (fun (p, e) -> (p = 3 && (e = 1 || e = 10)) || p = 0) ]
            |> List.tryPick id
            |> Option.map (fun (_, _, offset, format) -> struct (format, offset))
            |> Option.toValueOption

    let glyphIndex (codepoint: int) =
        match cmapSubtable with
        | ValueNone -> 0
        | ValueSome(struct (12, t)) ->
            // Sorted groups of (startChar, endChar, startGlyph): binary search.
            let groups = u32 (t + 12)
            let mutable lo, hi, result = 0, groups - 1, 0
            while lo <= hi do
                let mid = (lo + hi) / 2
                let g = t + 16 + mid * 12
                let first, last = u32 g, u32 (g + 4)
                if codepoint < first then hi <- mid - 1
                elif codepoint > last then lo <- mid + 1
                else
                    result <- u32 (g + 8) + (codepoint - first)
                    lo <- hi + 1
            result
        | ValueSome(struct (_, t)) ->
            // Format 4: segments over the BMP.
            if codepoint > 0xFFFF then 0
            else
                let segCount = u16 (t + 6) / 2
                let ends = t + 14
                let starts = ends + segCount * 2 + 2
                let deltas = starts + segCount * 2
                let rangeOffsets = deltas + segCount * 2
                let mutable lo, hi, result = 0, segCount - 1, 0
                while lo <= hi do
                    let mid = (lo + hi) / 2
                    if u16 (ends + mid * 2) < codepoint then lo <- mid + 1
                    else hi <- mid - 1
                if lo < segCount then
                    let start = u16 (starts + lo * 2)
                    if codepoint >= start then
                        let delta = i16 (deltas + lo * 2)
                        let rangeOffsetAt = rangeOffsets + lo * 2
                        let rangeOffset = u16 rangeOffsetAt
                        result <-
                            if rangeOffset = 0 then (codepoint + delta) &&& 0xFFFF
                            else
                                let g = u16 (rangeOffsetAt + rangeOffset + (codepoint - start) * 2)
                                if g = 0 then 0 else (g + delta) &&& 0xFFFF
                result

    // --- COLR v0 + CPAL: color layers ----------------------------------------------------------

    let colorLayers =
        match tryTable "COLR", tryTable "CPAL" with
        | ValueSome colr, ValueSome cpal ->
            let baseCount = u16 (colr + 2)
            let baseRecords = colr + u32 (colr + 4)
            let layerRecords = colr + u32 (colr + 8)
            let paletteEntries = u16 (cpal + 2)
            let colorRecords = cpal + u32 (cpal + 8)
            let firstPaletteStart = u16 (cpal + 12) // palette 0
            let color (index: int) =
                if index = 0xFFFF || index >= paletteEntries then ValueNone
                else
                    let c = colorRecords + (firstPaletteStart + index) * 4 // stored B, G, R, A
                    ValueSome(struct (data[c + 2], data[c + 1], data[c], data[c + 3]))
            let layers =
                Dictionary<int, ColorLayer[]>(baseCount)
            for i in 0 .. baseCount - 1 do
                let r = baseRecords + i * 6
                let glyph, first, count = u16 r, u16 (r + 2), u16 (r + 4)
                layers[glyph] <-
                    [| for k in first .. first + count - 1 ->
                           let l = layerRecords + k * 4
                           { Glyph = u16 l; Color = color (u16 (l + 2)) } |]
            layers
        | _ -> Dictionary<int, ColorLayer[]>()

    /// Thread-safe: font values (the built-in Inter faces especially) are shared by every text
    /// system in the process, and those may live on different threads (parallel tests, several UI threads).
    let outlines = System.Collections.Concurrent.ConcurrentDictionary<int, OutlinePoint[][]>()

    /// Reads a simple glyph's contours.
    let readSimple (offset: int) (contourCount: int) : OutlinePoint[][] =
        let endPoints = Array.init contourCount (fun i -> u16 (offset + 10 + i * 2))
        let pointCount = if contourCount = 0 then 0 else endPoints[contourCount - 1] + 1
        let instructionLength = u16 (offset + 10 + contourCount * 2)
        let mutable p = offset + 12 + contourCount * 2 + instructionLength

        // Flags, with run-length repeats.
        let flags = Array.zeroCreate<byte> pointCount
        let mutable i = 0
        while i < pointCount do
            let flag = data[p]
            p <- p + 1
            flags[i] <- flag
            i <- i + 1
            if flag &&& 8uy <> 0uy then
                let repeat = int data[p]
                p <- p + 1
                for _ in 1..repeat do
                    flags[i] <- flag
                    i <- i + 1

        // Coordinates are deltas; short (1 byte) or long (2 bytes), with "same as before" flags.
        let readCoordinates (shortBit: byte) (sameBit: byte) =
            let values = Array.zeroCreate<int> pointCount
            let mutable value = 0
            for k in 0 .. pointCount - 1 do
                let f = flags[k]
                if f &&& shortBit <> 0uy then
                    let delta = int data[p]
                    p <- p + 1
                    value <- value + (if f &&& sameBit <> 0uy then delta else -delta)
                elif f &&& sameBit = 0uy then
                    value <- value + i16 p
                    p <- p + 2
                values[k] <- value
            values

        let xs = readCoordinates 2uy 16uy
        let ys = readCoordinates 4uy 32uy
        let mutable start = 0
        [| for c in 0 .. contourCount - 1 ->
               let last = endPoints[c]
               let contour =
                   [| for k in start..last -> { X = float xs[k]; Y = float ys[k]; OnCurve = flags[k] &&& 1uy <> 0uy } |]
               start <- last + 1
               contour |]

    let rec readOutline (glyph: int) (depth: int) : OutlinePoint[][] =
        match outlines.TryGetValue glyph with
        | true, cached -> cached
        | _ ->
            let struct (start, finish) = glyphRange glyph
            let result =
                if finish <= start || depth > 8 then
                    [||]
                else
                    let offset = glyf + start
                    let contourCount = i16 offset
                    if contourCount >= 0 then readSimple offset contourCount else readComposite offset depth
            outlines[glyph] <- result
            result

    /// A composite glyph is made of other glyphs, each moved (and maybe scaled).
    and readComposite (offset: int) (depth: int) : OutlinePoint[][] =
        let contours = ResizeArray<OutlinePoint[]>()
        let mutable p = offset + 10
        let mutable more = true
        while more do
            let flags = u16 p
            let part = u16 (p + 2)
            p <- p + 4
            let dx, dy =
                if flags &&& 1 <> 0 then
                    let r = float (i16 p), float (i16 (p + 2))
                    p <- p + 4
                    r
                else
                    let r = float (sbyte data[p]), float (sbyte data[p + 1])
                    p <- p + 2
                    r
            let f2dot14 (o: int) = float (i16 o) / 16384.0
            let mutable a, b, c, d = 1.0, 0.0, 0.0, 1.0
            if flags &&& 0x08 <> 0 then
                a <- f2dot14 p
                d <- a
                p <- p + 2
            elif flags &&& 0x40 <> 0 then
                a <- f2dot14 p
                d <- f2dot14 (p + 2)
                p <- p + 4
            elif flags &&& 0x80 <> 0 then
                a <- f2dot14 p
                b <- f2dot14 (p + 2)
                c <- f2dot14 (p + 4)
                d <- f2dot14 (p + 6)
                p <- p + 8
            // Offsets are always treated as x/y values (ARGS_ARE_XY_VALUES); point matching is rare.
            for contour in readOutline part (depth + 1) do
                contours.Add [| for pt in contour -> { pt with X = a * pt.X + c * pt.Y + dx; Y = b * pt.X + d * pt.Y + dy } |]
            more <- flags &&& 0x20 <> 0
        contours.ToArray()

    /// The top of a character's outline in font units, if the font has it.
    let outlineTop (codepoint: int) =
        let contours = readOutline (glyphIndex codepoint) 0
        if contours.Length = 0 then ValueNone
        else ValueSome(contours |> Array.collect id |> Array.map (fun p -> p.Y) |> Array.max |> int)

    /// Cap height and x-height: OS/2 (version 2 and later), else the tops of 'H' and 'x', else 70%
    /// and 50% of the em.
    let capAndXHeight =
        lazy (
            let fromTable =
                match tryTable "OS/2" with
                | ValueSome os2 when u16 os2 >= 2 && i16 (os2 + 88) > 0 && i16 (os2 + 86) > 0 ->
                    ValueSome(struct (i16 (os2 + 88), i16 (os2 + 86)))
                | _ -> ValueNone
            match fromTable with
            | ValueSome v -> v
            | ValueNone ->
                let cap = outlineTop (int 'H') |> ValueOption.defaultValue (unitsPerEm * 7 / 10)
                let x = outlineTop (int 'x') |> ValueOption.defaultValue (unitsPerEm / 2)
                struct (cap, x))

    member _.Data = data
    member _.UnitsPerEm = unitsPerEm
    member _.GlyphCount = glyphCount
    /// Distance from the baseline to the top of the line box, in font units (positive).
    member _.Ascender = ascender
    /// Distance from the baseline to the bottom of the line box, in font units (negative).
    member _.Descender = descender
    member _.LineGap = lineGap
    /// Height of capital letters above the baseline, in font units.
    member _.CapHeight = let struct (cap, _) = capAndXHeight.Value in cap
    /// Height of lowercase letters such as 'x' above the baseline, in font units.
    member _.XHeight = let struct (_, x) = capAndXHeight.Value in x

    /// Horizontal advance of a glyph, in font units.
    member _.Advance(glyph: int) =
        let index = min glyph (hMetricCount - 1)
        u16 (hmtx + index * 4)

    /// Contours of a glyph in font units (y up). Empty for blank glyphs such as space.
    member _.Outline(glyph: int) = readOutline glyph 0

    /// The glyph for a Unicode code point, or 0 (".notdef") when the font doesn't have it.
    member _.GlyphIndex(codepoint: int) = glyphIndex codepoint

    /// True when the font maps this code point to a real glyph.
    member _.HasCharacter(codepoint: int) = glyphIndex codepoint <> 0

    /// True for fonts with color glyphs (COLR v0 + CPAL), such as emoji fonts.
    member _.HasColorGlyphs = colorLayers.Count > 0

    /// The color layers of a glyph, bottom to top, or None for an ordinary (monochrome) glyph.
    member _.ColorLayers(glyph: int) : ColorLayer[] voption =
        match colorLayers.TryGetValue glyph with
        | true, layers -> ValueSome layers
        | _ -> ValueNone
