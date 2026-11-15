namespace Scribble.Text

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open Scribble.Rendering
open Scribble.Text.Rasterizer

/// Size and vertical metrics of a piece of text, in DIPs.
[<Struct>]
type TextMetrics =
    { Width: float
      Height: float
      /// Baseline of the first line, measured down from the top.
      Baseline: float
      LineHeight: float
      Lines: int }

/// The fonts that ship inside Scribble.Text (Inter, SIL Open Font License; see Fonts/Inter-OFL.txt).
module Fonts =
    let private load (name: string) (resource: string) =
        lazy
            (use stream = typeof<Font>.Assembly.GetManifestResourceStream resource
             use memory = new MemoryStream()
             stream.CopyTo memory
             Font(name, memory.ToArray()))

    let private regular = load "Inter" "Inter-Regular.ttf"
    let private semiBold = load "Inter SemiBold" "Inter-SemiBold.ttf"

    /// Inter Regular: the default UI font.
    let Regular () = regular.Value
    /// Inter SemiBold: for headings and emphasis.
    let SemiBold () = semiBold.Value

/// A stretch of text shaped with one font (the requested font or a fallback).
[<Struct>]
type internal Run = { Font: Font; Glyphs: ShapedGlyph[] }

/// Shapes, caches, rasterizes and draws text. One per app (per render device), shared by all
/// windows: it owns the glyph caches that fill the device's glyph atlases.
///
/// Font fallback: text is split into user-perceived characters (grapheme clusters, so emoji
/// sequences like 👨‍👩‍👧 stay together). Each one is drawn with the requested font if it has it,
/// otherwise with the first fallback font that does. Emoji prefer a color font. Neighbouring
/// characters using the same font are shaped together as one run.
///
/// Glyphs are rasterized at their exact pixel size and at one of four horizontal subpixel
/// positions, so text stays sharp at any scale and spacing stays even.
[<Sealed>]
type TextSystem(atlas: GlyphAtlas, colorAtlas: GlyphAtlas, shaper: IShaper, fallbacks: Font list) =
    static let subpixelSteps = 4

    let mutable fallbacks = fallbacks
    // Shaping results are size independent (font units): cache per font and string.
    let runs = Dictionary<struct (int * string), Run[]>()
    // Rasterized glyphs, placed in an atlas: key = font, glyph, size in 1/16 px, subpixel step.
    let glyphs = Dictionary<struct (int * int * int * int), struct (GlyphBitmap * int * int * bool)>()
    let mutable atlasGeneration = atlas.Generation
    let mutable colorGeneration = colorAtlas.Generation

    let isVariationOrJoiner (cp: int) =
        cp = 0x200D // zero-width joiner
        || (cp >= 0xFE00 && cp <= 0xFE0F) // variation selectors
        || (cp >= 0x1F3FB && cp <= 0x1F3FF) // skin tone modifiers
        || cp = 0x20E3 // combining enclosing keycap
        || (cp >= 0xE0020 && cp <= 0xE007F) // tag characters (flag sequences)

    /// Picks the font for one grapheme cluster.
    let fontFor (primary: Font) (cluster: string) =
        let codepoints = [| for rune in cluster.EnumerateRunes() -> rune.Value |]
        let first = codepoints[0]
        let chain = primary :: fallbacks
        let emoji = Array.contains 0xFE0F codepoints || first >= 0x1F000 || (first >= 0x2600 && first <= 0x27BF && codepoints.Length > 1)
        let covers (font: Font) =
            codepoints |> Array.forall (fun cp -> isVariationOrJoiner cp || cp < 0x20 || font.Face.HasCharacter cp)
        let colorFont =
            if emoji then chain |> List.tryFind (fun f -> f.Face.HasColorGlyphs && f.Face.HasCharacter first) else None
        colorFont
        |> Option.orElse (List.tryFind covers chain)
        |> Option.orElse (List.tryFind (fun (f: Font) -> f.Face.HasCharacter first) chain)
        |> Option.defaultValue primary

    /// Splits a line into runs of one font and shapes each run. Cached.
    let shapeLine (font: Font) (line: string) : Run[] =
        let key = struct (font.Id, line)
        match runs.TryGetValue key with
        | true, cached -> cached
        | _ ->
            // Keep the cache bounded; text that's drawn every frame gets re-added immediately.
            if runs.Count > 8192 then runs.Clear()
            let result = ResizeArray<Run>()
            let mutable runFont = font
            let mutable runStart = 0
            let mutable i = 0
            let flush (until: int) =
                if until > runStart then
                    let glyphs = shaper.Shape(runFont, line.Substring(runStart, until - runStart))
                    result.Add { Font = runFont; Glyphs = glyphs |> Array.map (fun g -> { g with Cluster = g.Cluster + runStart }) }
            while i < line.Length do
                let length = StringInfo.GetNextTextElementLength(line, i)
                let clusterFont = if fallbacks.IsEmpty then font else fontFor font (line.Substring(i, length))
                if clusterFont.Id <> runFont.Id then
                    flush i
                    runFont <- clusterFont
                    runStart <- i
                i <- i + length
            flush line.Length
            let shaped = result.ToArray()
            runs[key] <- shaped
            shaped

    let allocate (target: GlyphAtlas) (bitmap: GlyphBitmap) =
        match target.TryAllocate(bitmap.Width, bitmap.Height) with
        | ValueSome(struct (x, y)) ->
            target.Write(x, y, bitmap.Width, bitmap.Height, ReadOnlySpan bitmap.Coverage)
            ValueSome(struct (x, y))
        | ValueNone -> ValueNone

    /// Rasterizes a glyph (if needed) and returns its bitmap metrics, atlas position, and whether
    /// it lives in the color atlas.
    let rec glyph (font: Font) (glyphId: int) (sizePx: float) (step: int) (afterClear: bool) =
        if atlas.Generation <> atlasGeneration || colorAtlas.Generation <> colorGeneration then
            glyphs.Clear()
            atlasGeneration <- atlas.Generation
            colorGeneration <- colorAtlas.Generation
        let key = struct (font.Id, glyphId, int (Math.Round(sizePx * 16.0)), step)
        match glyphs.TryGetValue key with
        | true, entry -> entry
        | _ ->
            let scale = sizePx / float font.Face.UnitsPerEm
            let offset = float step / float subpixelSteps
            let isColor, bitmap, target =
                match font.Face.ColorLayers glyphId with
                | ValueSome layers -> true, rasterizeColor font.Face layers scale offset (struct (0uy, 0uy, 0uy, 255uy)), colorAtlas
                | ValueNone -> false, rasterize (font.Face.Outline glyphId) scale offset, atlas
            if bitmap.Width = 0 then
                let entry = struct (bitmap, 0, 0, isColor)
                glyphs[key] <- entry
                entry
            else
                match allocate target bitmap with
                | ValueSome(struct (x, y)) ->
                    let entry = struct (bitmap, x, y, isColor)
                    glyphs[key] <- entry
                    entry
                | ValueNone when not afterClear ->
                    // Atlas full: start over. Glyphs already drawn this frame may show the wrong
                    // pixels for one frame; a multi-page atlas will remove that limitation.
                    target.Clear()
                    glyph font glyphId sizePx step true
                | ValueNone ->
                    // Bigger than the whole atlas (a huge font size): skip it rather than loop.
                    let entry = struct (GlyphBitmap.Empty, 0, 0, isColor)
                    glyphs[key] <- entry
                    entry

    let lines (text: string) = text.Split '\n'

    /// A text system without fallback fonts.
    new(atlas: GlyphAtlas, shaper: IShaper) = TextSystem(atlas, GlyphAtlas(256, 4), shaper, [])

    /// A text system drawing into a render device's atlases, with optional fallback fonts
    /// (for example `Emoji.Twemoji()` from Scribble.Text.Emoji).
    new(device: IRenderDevice, shaper: IShaper, ?fallbacks: Font list) =
        TextSystem(device.GlyphAtlas, device.ColorGlyphAtlas, shaper, defaultArg fallbacks [])

    member _.Shaper = shaper

    /// Fonts tried, in order, for characters the requested font doesn't have.
    member _.Fallbacks
        with get () = fallbacks
        and set value =
            fallbacks <- value
            runs.Clear()

    /// Line height in DIPs for a font at `size` DIPs (ascender − descender + line gap).
    member _.LineHeight(font: Font, size: float) =
        let f = font.Face
        float (f.Ascender - f.Descender + f.LineGap) * size / float f.UnitsPerEm

    /// Measures text at `size` DIPs. Lines are split on '\n'.
    member this.Measure(text: string, font: Font, size: float) : TextMetrics =
        let mutable width = 0.0
        let parts = lines text
        for line in parts do
            let mutable advance = 0.0
            for run in shapeLine font line do
                let scale = size / float run.Font.Face.UnitsPerEm
                for g in run.Glyphs do
                    advance <- advance + float g.XAdvance * scale
            width <- max width advance
        let lineHeight = this.LineHeight(font, size)
        { Width = width
          Height = lineHeight * float parts.Length
          Baseline = float font.Face.Ascender * size / float font.Face.UnitsPerEm
          LineHeight = lineHeight
          Lines = parts.Length }

    /// Fonts' line breaking rules; replaceable (for example with a full UAX #14 implementation).
    member val LineBreaker = LineBreaking.simple with get, set

    /// Lays out text in a box `maxWidth` DIPs wide (infinity for unconstrained). Paragraphs are
    /// split on '\n'. With Wrap, lines break at the line breaker's opportunities, or inside a word
    /// that's wider than the line. `maxLines` (0 = no limit) cuts the text off; trimming then ends
    /// the last visible line with "…".
    member this.Layout(text: string, font: Font, size: float, maxWidth: float, wrapping: TextWrapping, trimming: TextTrimming, maxLines: int) : TextLayout =
        let lineHeight = this.LineHeight(font, size)
        let result = ResizeArray<TextLine>()
        let mutable truncated = false

        // Advance of each UTF-16 position (glyph advances summed at their cluster's first index).
        let advances (paragraph: string) =
            let a = Array.zeroCreate<float> (paragraph.Length + 1)
            for run in shapeLine font paragraph do
                let scale = size / float run.Font.Face.UnitsPerEm
                for g in run.Glyphs do
                    if g.Cluster < a.Length then a[g.Cluster] <- a[g.Cluster] + float g.XAdvance * scale
            a

        // Grapheme clusters as (start, length, width).
        let clusters (paragraph: string) (a: float[]) =
            let list = ResizeArray<struct (int * int * float)>()
            let mutable i = 0
            while i < paragraph.Length do
                let length = StringInfo.GetNextTextElementLength(paragraph, i)
                let mutable w = 0.0
                for k in i .. i + length - 1 do
                    w <- w + a[k]
                list.Add(struct (i, length, w))
                i <- i + length
            list

        /// A line from clusters [first, last), with trailing whitespace excluded from its width.
        let makeLine (paragraph: string) (cs: ResizeArray<struct (int * int * float)>) (first: int) (last: int) =
            let mutable visibleEnd = last
            while visibleEnd > first && (let struct (s, _, _) = cs[visibleEnd - 1] in LineBreaking.isWhitespace paragraph[s]) do
                visibleEnd <- visibleEnd - 1
            let mutable width = 0.0
            for k in first .. visibleEnd - 1 do
                let struct (_, _, w) = cs[k]
                width <- width + w
            if first >= last then
                { Text = ""; Width = 0.0 }
            else
                let struct (start, _, _) = cs[first]
                let struct (lastStart, lastLength, _) = cs[visibleEnd - 1 |> max first]
                let finish = if visibleEnd > first then lastStart + lastLength else start
                { Text = paragraph.Substring(start, finish - start); Width = width }

        let wraps = wrapping = Wrap && not (Double.IsInfinity maxWidth)
        for paragraph in text.Split '\n' do
            let a = advances paragraph
            let cs = clusters paragraph a
            if not wraps || cs.Count = 0 then
                result.Add(makeLine paragraph cs 0 cs.Count)
            else
                let mutable lineStart = 0
                let mutable width = 0.0
                let mutable lastBreak = -1
                let mutable k = 0
                while k < cs.Count do
                    let struct (start, _, w) = cs[k]
                    if k > lineStart && this.LineBreaker.CanBreakBefore(paragraph, start) then lastBreak <- k
                    let isSpace = LineBreaking.isWhitespace paragraph[start]
                    if width + w > maxWidth && k > lineStart && not isSpace then
                        // Break at the last opportunity on this line, or right here if there is none.
                        let finish = if lastBreak > lineStart then lastBreak else k
                        result.Add(makeLine paragraph cs lineStart finish)
                        lineStart <- finish
                        lastBreak <- -1
                        width <- 0.0
                        for m in lineStart .. k - 1 do
                            let struct (_, _, mw) = cs[m]
                            width <- width + mw
                        // Re-examine cluster k against the new line.
                    else
                        width <- width + w
                        k <- k + 1
                result.Add(makeLine paragraph cs lineStart cs.Count)

        // MaxLines: keep the first lines.
        if maxLines > 0 && result.Count > maxLines then
            result.RemoveRange(maxLines, result.Count - maxLines)
            truncated <- true

        // Ellipsis: on the last line when text was cut off, and on any line wider than the box.
        if trimming = CharacterEllipsis && not (Double.IsInfinity maxWidth) then
            let ellipsis = "…"
            let ellipsisWidth = this.Measure(ellipsis, font, size).Width
            let trim (line: TextLine) (forced: bool) =
                if not forced && line.Width <= maxWidth then
                    line
                else
                    let a = advances line.Text
                    let cs = clusters line.Text a
                    let mutable width = 0.0
                    let mutable count = 0
                    let mutable fits = true
                    while fits && count < cs.Count do
                        let struct (_, _, w) = cs[count]
                        if width + w + ellipsisWidth <= maxWidth then
                            width <- width + w
                            count <- count + 1
                        else
                            fits <- false
                    let kept = makeLine line.Text cs 0 count
                    truncated <- true
                    { Text = kept.Text + ellipsis; Width = kept.Width + ellipsisWidth }
            for i in 0 .. result.Count - 1 do
                let isLast = i = result.Count - 1
                result[i] <- trim result[i] (isLast && truncated)

        let lines = result.ToArray()
        { Lines = lines
          Width = (if lines.Length = 0 then 0.0 else lines |> Array.map _.Width |> Array.max)
          Height = lineHeight * float lines.Length
          LineHeight = lineHeight
          Baseline = float font.Face.Ascender * size / float font.Face.UnitsPerEm
          Truncated = truncated
          Font = font
          Size = size }

    /// Draws a layout with the top-left of its first line at (x, y). Lines are aligned within
    /// `boxWidth` DIPs.
    member this.DrawLayout(list: DrawList, layout: TextLayout, x: float, y: float, boxWidth: float, alignment: TextAlignment, color: Color) =
        for i in 0 .. layout.Lines.Length - 1 do
            let line = layout.Lines[i]
            let dx =
                match alignment with
                | Start -> 0.0
                | Center -> (boxWidth - line.Width) / 2.0
                | End -> boxWidth - line.Width
            this.Draw(list, line.Text, layout.Font, layout.Size, x + dx, y + float i * layout.LineHeight, color)

    /// Draws text with the top-left of its first line box at (x, y), in DIPs.
    member this.Draw(list: DrawList, text: string, font: Font, size: float, x: float, y: float, color: Color) =
        let t = list.Transform
        let sizePx = size * abs t.ScaleY
        if sizePx > 0.5 && color.A > 0.0f then
            let lineHeightPx = this.LineHeight(font, size) * abs t.ScaleY
            let originX = t.OffsetX + t.ScaleX * x
            let firstBaseline = t.OffsetY + t.ScaleY * y + float font.Face.Ascender * sizePx / float font.Face.UnitsPerEm
            let mutable lineIndex = 0
            for line in lines text do
                // Baselines land on whole pixels so every line renders identically.
                let baseline = Math.Round(firstBaseline + float lineIndex * lineHeightPx)
                let mutable pen = originX
                for run in shapeLine font line do
                    let scale = sizePx / float run.Font.Face.UnitsPerEm
                    for g in run.Glyphs do
                        let gx = pen + float g.XOffset * scale
                        let gy = baseline - float g.YOffset * scale
                        let whole = Math.Floor gx
                        let mutable step = int (Math.Round((gx - whole) * float subpixelSteps))
                        let mutable px = int whole
                        if step = subpixelSteps then
                            step <- 0
                            px <- px + 1
                        let struct (bitmap, ax, ay, isColor) = glyph run.Font g.Glyph sizePx step false
                        if bitmap.Width > 0 then
                            let gx, gy = px + bitmap.Left, int (Math.Round gy) + bitmap.Top
                            if isColor then list.DrawColorGlyph(gx, gy, bitmap.Width, bitmap.Height, ax, ay, color)
                            else list.DrawGlyph(gx, gy, bitmap.Width, bitmap.Height, ax, ay, color)
                        pen <- pen + float g.XAdvance * scale
                lineIndex <- lineIndex + 1

[<AutoOpen>]
module DrawListTextExtensions =
    type DrawList with
        /// Draws text with the top-left of its first line box at (x, y), in DIPs.
        member list.DrawText(text: TextSystem, value: string, font: Font, size: float, x: float, y: float, color: Color) =
            text.Draw(list, value, font, size, x, y, color)
