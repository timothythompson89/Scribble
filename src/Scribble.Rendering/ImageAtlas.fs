namespace Scribble.Rendering

open System
open System.Collections.Generic
open Scribble.Imaging

/// What a GPU backend does for the image atlas: page textures (RGBA8, premultiplied) and uploads into them.
type IImagePages =
    /// Creates page `page`, a `width × height` RGBA8 texture.
    abstract Create: page: int * width: int * height: int -> unit
    /// Destroys page `page` (it has no images left).
    abstract Destroy: page: int -> unit
    /// Copies a mip level into page `page` with its top-left texel at (x, y).
    abstract Upload: page: int * x: int * y: int * level: ImageLevel -> unit

/// Consecutive shapes drawn with one image page bound: one draw call (DESIGN.md, "M8 step 3b", (c) 10). Page -1
/// when the run has no images (the backend binds its 1 × 1 placeholder).
[<Struct>]
type ImageRun = { First: int; Count: int; Page: int }

/// A region of a page.
[<Sealed; AllowNullLiteral>]
type internal PackedSlot(shelf: int, x: int, y: int, width: int) =
    member val Used = true with get, set
    member _.Shelf = shelf
    member _.X = x
    member _.Y = y
    member _.Width = width

[<Sealed>]
type internal Shelf(y: int, height: int) =
    member _.Y = y
    member _.Height = height
    member val CursorX = 0 with get, set
    member val Slots = ResizeArray<PackedSlot>()

/// Shelf packing with reuse ((c) 7): rows of regions of similar height; a freed region is reused by an image that
/// fits it, freed regions at the end of a shelf and empty shelves at the bottom give their space back. No padding:
/// the shader never reads outside an image's own texels.
[<Sealed>]
type internal ShelfPacker(width: int, height: int) =
    let shelves = ResizeArray<Shelf>()
    let mutable used = 0

    let tryShelf (s: Shelf) (w: int) =
        let free = s.Slots |> Seq.tryFind (fun slot -> not slot.Used && slot.Width >= w)
        match free with
        | Some slot ->
            slot.Used <- true
            slot
        | None when s.CursorX + w <= width ->
            let slot = PackedSlot(shelves.IndexOf s, s.CursorX, s.Y, w)
            s.Slots.Add slot
            s.CursorX <- s.CursorX + w
            slot
        | None -> null

    member _.Width = width
    member _.Height = height
    member _.IsEmpty = used = 0
    member _.Used = used

    /// A `w × h` region, or null when none fits.
    member _.TryAllocate(w: int, h: int) : PackedSlot =
        if w > width || h > height then
            null
        else
            let pick (fits: Shelf -> bool) =
                let mutable result = null
                let mutable k = 0
                while isNull result && k < shelves.Count do
                    if fits shelves[k] then result <- tryShelf shelves[k] w
                    k <- k + 1
                result
            // A shelf of about the right height first, then a new shelf, then any shelf tall enough.
            let mutable slot = pick (fun s -> s.Height >= h && s.Height <= 2 * h)
            if isNull slot then
                let top = if shelves.Count = 0 then 0 else shelves[shelves.Count - 1].Y + shelves[shelves.Count - 1].Height
                if top + h <= height then
                    let s = Shelf(top, h)
                    shelves.Add s
                    slot <- tryShelf s w
            if isNull slot then slot <- pick (fun s -> s.Height >= h)
            if not (isNull slot) then used <- used + 1
            slot

    member _.Free(slot: PackedSlot) =
        if slot.Used then
            slot.Used <- false
            used <- used - 1
            let s = shelves[slot.Shelf]
            while s.Slots.Count > 0 && not s.Slots[s.Slots.Count - 1].Used do
                s.CursorX <- s.Slots[s.Slots.Count - 1].X
                s.Slots.RemoveAt(s.Slots.Count - 1)
            while shelves.Count > 0 && shelves[shelves.Count - 1].Slots.Count = 0 do
                shelves.RemoveAt(shelves.Count - 1)

[<Struct>]
type internal Placement = { Page: int; Slot: PackedSlot }

[<Sealed>]
type internal ImagePage(id: int, packer: ShelfPacker) =
    member _.Id = id
    member _.Packer = packer

/// A GPU device's image atlas (DESIGN.md, "M8 step 3b", (c)): pages of `pageSize`² (2048 by default) created when the
/// first image is drawn, a page of its own for a mip level bigger than that, only the levels drawn uploaded, an
/// emptied page given back. `Prepare` places a frame's images and splits the frame into runs. Device-independent:
/// the backend's `IImagePages` makes the textures. UI thread only.
[<Sealed>]
type ImageAtlas(pages: IImagePages, maxTextureSize: int, ?pageSize: int) =
    let pageSize = min (defaultArg pageSize 2048) maxTextureSize
    let pageList = ResizeArray<ImagePage>()
    let byId = Dictionary<int, ImagePage>()
    let resident = Dictionary<DecodedImage, Placement voption[]>(HashIdentity.Reference)
    let runs = ResizeArray<ImageRun>()
    let mutable nextPage = 0
    let mutable releasesSeen = DecodedImage.ReleaseCount
    let mutable sweepAgain = false
    let mutable uploads = 0
    let mutable disposed = false

    let newPage (w: int) (h: int) =
        let page = ImagePage(nextPage, ShelfPacker(w, h))
        nextPage <- nextPage + 1
        pages.Create(page.Id, w, h)
        pageList.Add page
        byId[page.Id] <- page
        page

    let free (p: Placement) =
        let page = byId[p.Page]
        page.Packer.Free p.Slot
        if page.Packer.IsEmpty then
            pages.Destroy page.Id
            pageList.Remove page |> ignore
            byId.Remove page.Id |> ignore

    /// Gives back the space of released images, only when some were released since the last look.
    let sweep () =
        let count = DecodedImage.ReleaseCount
        if count <> releasesSeen || sweepAgain then
            releasesSeen <- count
            sweepAgain <- false
            let gone = resident.Keys |> Seq.filter (fun image -> image.IsReleased) |> Seq.toArray
            for image in gone do
                for p in resident[image] do
                    if p.IsSome then free p.Value
                resident.Remove image |> ignore

    let place (image: DecodedImage) (k: int) : Placement =
        let levels =
            match resident.TryGetValue image with
            | true, l -> l
            | _ ->
                let l = Array.create image.LevelCount ValueNone
                resident[image] <- l
                l
        match levels[k] with
        | ValueSome p -> p
        | ValueNone ->
            let level = image.Level k
            let placement =
                if level.Width > pageSize || level.Height > pageSize then
                    let page = newPage level.Width level.Height
                    { Page = page.Id; Slot = page.Packer.TryAllocate(level.Width, level.Height) }
                else
                    let mutable found = ValueNone
                    let mutable i = 0
                    while found.IsNone && i < pageList.Count do
                        let page = pageList[i]
                        if page.Packer.Width = pageSize && page.Packer.Height = pageSize then
                            let slot = page.Packer.TryAllocate(level.Width, level.Height)
                            if not (isNull slot) then found <- ValueSome { Page = page.Id; Slot = slot }
                        i <- i + 1
                    match found with
                    | ValueSome p -> p
                    | ValueNone ->
                        let page = newPage pageSize pageSize
                        { Page = page.Id; Slot = page.Packer.TryAllocate(level.Width, level.Height) }
            pages.Upload(placement.Page, placement.Slot.X, placement.Slot.Y, level)
            uploads <- uploads + 1
            levels[k] <- ValueSome placement
            placement

    /// The level drawn: the one the draw list chose, or the first after it that fits this device ((c) 8).
    let fit (image: DecodedImage) (chosen: int) =
        let mutable k = min chosen (image.LevelCount - 1)
        let mutable w, h = image.Width, image.Height
        for _ in 1..k do
            w <- max 1 ((w + 1) / 2)
            h <- max 1 ((h + 1) / 2)
        while (w > maxTextureSize || h > maxTextureSize) && k < image.LevelCount - 1 do
            w <- max 1 ((w + 1) / 2)
            h <- max 1 ((h + 1) / 2)
            k <- k + 1
        k


    /// The size of an ordinary page.
    member _.PageSize = pageSize

    /// Pages that exist now.
    member _.PageCount = pageList.Count

    /// Mip levels uploaded since the atlas was made.
    member _.Uploads = uploads

    /// Mip levels in pages now.
    member _.ResidentLevels = resident.Values |> Seq.sumBy (fun l -> l |> Array.sumBy (fun p -> if p.IsSome then 1 else 0))

    /// Places the frame's images (uploading what isn't in a page yet), writes where each reads into the draw list,
    /// and returns the frame's runs: one draw call each. Empty when the list is.
    member _.Prepare(drawList: DrawList) : IReadOnlyList<ImageRun> =
        if disposed then raise (ObjectDisposedException "ImageAtlas")
        sweep ()
        runs.Clear()
        let mutable start = 0
        let mutable page = -1
        for d in drawList.Images do
            let k = fit d.Image d.Level
            let p = place d.Image k
            // An image released after it was drawn into this list still draws this frame; its space goes next time.
            if d.Image.IsReleased then sweepAgain <- true
            let level = d.Image.Level k
            let fx = float level.Width / float d.Image.Width
            let fy = float level.Height / float d.Image.Height
            let target = drawList.InstanceRect d.Instance
            let sx = d.Source.X * fx
            let sy = d.Source.Y * fy
            let sw = d.Source.Width * fx
            let sh = d.Source.Height * fy
            let originX = int (Math.Round((sx + float p.Slot.X) * 256.0))
            let originY = int (Math.Round((sy + float p.Slot.Y) * 256.0))
            let step (s: float) (t: float) = Math.Clamp(int (Math.Round(s / t * 65536.0)), 1, (1 <<< 24) - 1)
            let x0 = p.Slot.X + Math.Clamp(int (floor sx), 0, level.Width - 1)
            let y0 = p.Slot.Y + Math.Clamp(int (floor sy), 0, level.Height - 1)
            let x1 = p.Slot.X + Math.Clamp(int (ceil (sx + sw)) - 1, 0, level.Width - 1)
            let y1 = p.Slot.Y + Math.Clamp(int (ceil (sy + sh)) - 1, 0, level.Height - 1)
            drawList.PlaceImage(
                d.Instance,
                originX >>> 8,
                originY >>> 8,
                step sw target.Width,
                step sh target.Height,
                x0,
                y0,
                x1,
                y1,
                ((originX &&& 0xFF) <<< 8) ||| (originY &&& 0xFF)
            )
            if page = -1 then page <- p.Page
            elif p.Page <> page then
                runs.Add { First = start; Count = d.Instance - start; Page = page }
                start <- d.Instance
                page <- p.Page
        if drawList.Count > start then runs.Add { First = start; Count = drawList.Count - start; Page = page }
        runs :> IReadOnlyList<ImageRun>

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                for page in pageList do
                    pages.Destroy page.Id
                pageList.Clear()
                byId.Clear()
                resident.Clear()
