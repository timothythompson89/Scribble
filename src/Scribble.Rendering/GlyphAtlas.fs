namespace Scribble.Rendering

open System

/// A texture that glyph bitmaps are packed into, kept on the CPU and mirrored to the GPU.
/// One byte per pixel holds coverage (ordinary glyphs); four bytes hold premultiplied RGBA
/// (color glyphs such as emoji). Text code allocates and writes regions; backends upload the dirty part
/// before drawing.
///
/// Packing is "shelf" style: rows of glyphs of similar height. Every region gets 1 px of empty
/// padding so neighbours never bleed into each other.
[<Sealed>]
type GlyphAtlas(size: int, bytesPerPixel: int) =
    do if bytesPerPixel <> 1 && bytesPerPixel <> 4 then invalidArg (nameof bytesPerPixel) "Must be 1 (coverage) or 4 (RGBA)."
    let pixels: byte[] = Array.zeroCreate (size * size * bytesPerPixel)
    let mutable shelfY = 0
    let mutable shelfHeight = 0
    let mutable cursorX = 0
    let mutable dirty = Rect.Create(0.0, 0.0, 0.0, 0.0)
    let mutable generation = 0

    let markDirty (x: int) (y: int) (w: int) (h: int) =
        let r = Rect.Create(float x, float y, float w, float h)
        dirty <-
            if dirty.IsEmpty then r
            else Rect.FromEdges(min dirty.Left r.Left, min dirty.Top r.Top, max dirty.Right r.Right, max dirty.Bottom r.Bottom)

    /// A 2048 × 2048 coverage atlas.
    new() = GlyphAtlas(2048, 1)
    new(size: int) = GlyphAtlas(size, 1)

    member _.Size = size
    member _.BytesPerPixel = bytesPerPixel
    /// Row-major pixels, `Size × Size × BytesPerPixel` bytes.
    member _.Pixels = pixels

    /// Increases every time the atlas is cleared; cached glyph positions from an older
    /// generation are invalid.
    member _.Generation = generation

    /// Reserves a `width × height` region. Returns its top-left corner, or None when the atlas is full.
    member _.TryAllocate(width: int, height: int) : struct (int * int) voption =
        let w, h = width + 1, height + 1 // 1 px padding to the right and below
        if w > size || h > size then
            ValueNone
        else
            if cursorX + w > size then
                shelfY <- shelfY + shelfHeight
                shelfHeight <- 0
                cursorX <- 0
            if shelfY + h > size then
                ValueNone
            else
                let x = cursorX
                cursorX <- cursorX + w
                shelfHeight <- max shelfHeight h
                ValueSome(struct (x, shelfY))

    /// Copies a `width × height` bitmap (BytesPerPixel bytes per pixel) into the atlas at (x, y).
    member _.Write(x: int, y: int, width: int, height: int, bitmap: ReadOnlySpan<byte>) =
        let rowBytes = width * bytesPerPixel
        for row in 0 .. height - 1 do
            bitmap.Slice(row * rowBytes, rowBytes).CopyTo(Span<byte>(pixels, ((y + row) * size + x) * bytesPerPixel, rowBytes))
        markDirty x y width height

    /// Empties the atlas (when it's full). Bumps Generation so caches know to start over.
    member _.Clear() =
        Array.Clear pixels
        shelfY <- 0
        shelfHeight <- 0
        cursorX <- 0
        generation <- generation + 1
        markDirty 0 0 size size

    /// For backends: the region changed since the last call (empty when nothing changed).
    /// Calling this marks everything as uploaded.
    member _.TakeDirtyRegion() : Rect =
        let r = dirty
        dirty <- Rect.Create(0.0, 0.0, 0.0, 0.0)
        r
