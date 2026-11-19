/// Scribble.Imaging's `Vector128` loops against their scalar twins (DESIGN.md, "Vectorising the PNG decoder",
/// decision 6): the same rows through both, compared exactly, for every filter, every bytes-a-pixel step and widths
/// from 1 to 40 (so every tail length), with guard bytes around what they write.
module Scribble.Tests.ImagingVectorTests

#nowarn "9"

open System
open Microsoft.FSharp.NativeInterop
open Xunit
open Scribble.Imaging

let private guard = 32
let private guardByte = 0xA5uy

/// Runs `f` on a copy of `row` with guard bytes on both sides, checks it wrote nothing outside, and returns the row.
let private run (label: string) (row: byte[]) (f: nativeptr<byte> -> unit) : byte[] =
    let buffer = Array.create (row.Length + guard * 2) guardByte
    Array.blit row 0 buffer guard row.Length
    (
        use p = fixed buffer
        f (NativePtr.add p guard)
    )
    for i in 0 .. guard - 1 do
        if buffer[i] <> guardByte || buffer[guard + row.Length + i] <> guardByte then
            Assert.Fail(label + ": wrote outside its row")
    Array.sub buffer guard row.Length

/// Rows that exercise different things: any bytes; small values (many Paeth ties); only 0 and 255 (carries).
let private fill (random: Random) (kind: int) (count: int) =
    Array.init count (fun _ ->
        match kind with
        | 0 -> byte (random.Next 256)
        | 1 -> byte (random.Next 4)
        | _ -> if random.Next 2 = 0 then 0uy else 255uy)

[<Fact>]
let ``unfiltering a row gives the same bytes with and without vectors`` () =
    let random = Random(21)
    let mutable rows = 0
    for filter in 0uy .. 4uy do
        for step in [ 1; 2; 3; 4; 6; 8 ] do
            for pixels in 1..40 do
                for kind in 0..2 do
                    for firstRow in [ false; true ] do
                        let count = step * pixels
                        let cur = fill random kind count
                        // The row above, exactly `count` bytes, so a read past it is a read past its array.
                        let up = if firstRow then Array.zeroCreate count else fill random kind count
                        let label = $"filter {filter}, step {step}, {pixels} pixels, kind {kind}, first row {firstRow}"
                        use upPtr = fixed up
                        let scalar = run label cur (fun p -> Rows.unfilterScalar filter p upPtr count step)
                        let vector = run label cur (fun p -> Rows.unfilterVector filter p upPtr count step)
                        if scalar <> vector then
                            let i = Seq.findIndex id (Seq.map2 (<>) scalar vector)
                            Assert.Fail($"{label}: byte {i} is {vector[i]} with vectors, {scalar[i]} without")
                        rows <- rows + 1
    Assert.Equal(5 * 6 * 40 * 3 * 2, rows)

[<Fact>]
let ``a row of sub-byte pixels (any byte count, step 1) unfilters the same with and without vectors`` () =
    let random = Random(22)
    for filter in 0uy .. 4uy do
        for count in 1..70 do
            let cur = fill random 0 count
            let up = fill random 0 count
            use upPtr = fixed up
            let scalar = run "step 1" cur (fun p -> Rows.unfilterScalar filter p upPtr count 1)
            let vector = run "step 1" cur (fun p -> Rows.unfilterVector filter p upPtr count 1)
            Assert.True((scalar = vector), $"filter {filter}, {count} bytes")

/// A conversion's output for `width` pixels, a pixel every `dstStep` bytes, the bytes between left as they were.
let private convert (label: string) (src: byte[]) (width: int) (dstStep: int) (f: nativeptr<byte> -> nativeptr<byte> -> int -> int -> unit) =
    let dst = Array.create ((width - 1) * dstStep + 4) 0x5Auy
    use srcPtr = fixed src
    run label dst (fun p -> f srcPtr p width dstStep)

[<Fact>]
let ``8-bit RGBA rows premultiply the same with and without vectors`` () =
    let random = Random(23)
    for width in 1..40 do
        for kind in 0..2 do
            for dstStep in [ 4; 8; 32 ] do
                let src = fill random kind (width * 4)
                // Some fully opaque and fully clear pixels among the rest.
                for x in 0 .. width - 1 do
                    match random.Next 4 with
                    | 0 -> src[x * 4 + 3] <- 255uy
                    | 1 -> src[x * 4 + 3] <- 0uy
                    | _ -> ()
                let label = $"width {width}, kind {kind}, a pixel every {dstStep} bytes"
                let scalar = convert label src width dstStep Rows.rgba8Scalar
                let vector = convert label src width dstStep Rows.rgba8Vector
                Assert.True((scalar = vector), label)

[<Fact>]
let ``the vector premultiply is right for every colour and alpha`` () =
    // 65,536 pixels: every channel value with every alpha (and its complement and a mix in the other channels).
    let src = Array.zeroCreate<byte> (65536 * 4)
    for a in 0..255 do
        for c in 0..255 do
            let i = (a * 256 + c) * 4
            src[i] <- byte c
            src[i + 1] <- byte (255 - c)
            src[i + 2] <- byte (c ^^^ 0x55)
            src[i + 3] <- byte a
    let out = convert "every colour and alpha" src 65536 4 Rows.rgba8Vector
    for p in 0..65535 do
        let a = int src[p * 4 + 3]
        for k in 0..2 do
            let expected = Pixels.premultiply (int src[p * 4 + k]) a
            if int out[p * 4 + k] <> expected then
                Assert.Fail($"{src[p * 4 + k]} × {a}: {out[p * 4 + k]}, expected {expected}")
        Assert.Equal(src[p * 4 + 3], out[p * 4 + 3])

[<Fact>]
let ``8-bit RGB rows become RGBA the same with and without vectors`` () =
    let random = Random(24)
    for width in 1..40 do
        for dstStep in [ 4; 8; 32 ] do
            let src = fill random 0 (width * 3)
            let label = $"width {width}, a pixel every {dstStep} bytes"
            let scalar = convert label src width dstStep Rows.rgb8Scalar
            let vector = convert label src width dstStep Rows.rgb8Vector
            Assert.True((scalar = vector), label)
            for x in 0 .. width - 1 do
                Assert.Equal<byte[]>(Array.append src[x * 3 .. x * 3 + 2] [| 255uy |], vector[x * dstStep .. x * dstStep + 3])

[<Fact>]
let ``a mip level is the same with and without vectors, and the average of each 2 × 2 block`` () =
    let random = Random(25)
    for w in 1..24 do
        for h in 1..9 do
            for kind in [ 0; 2 ] do
                let level: ImageLevel = { Width = w; Height = h; Pixels = fill random kind (w * h * 4) }
                let scalar = Pixels.halveWith false level
                let vector = Pixels.halveWith true level
                Assert.Equal((scalar.Width, scalar.Height), (vector.Width, vector.Height))
                Assert.True((scalar.Pixels = vector.Pixels), $"{w} × {h}, kind {kind}")
                Assert.Equal(((w + 1) / 2, (h + 1) / 2), (vector.Width, vector.Height))
                for y in 0 .. vector.Height - 1 do
                    for x in 0 .. vector.Width - 1 do
                        for k in 0..3 do
                            let at (px: int) (py: int) = int level.Pixels[(min py (h - 1) * w + min px (w - 1)) * 4 + k]
                            let sum = at (x * 2) (y * 2) + at (x * 2 + 1) (y * 2) + at (x * 2) (y * 2 + 1) + at (x * 2 + 1) (y * 2 + 1)
                            Assert.Equal((sum + 2) / 4, int vector.Pixels[(y * vector.Width + x) * 4 + k])
