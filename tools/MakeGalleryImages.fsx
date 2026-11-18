// Draws the Gallery's pictures (procedural, so the repository holds no photos: DESIGN.md, "M8 step 3b", (d) 11)
// and writes them as PNGs into samples/Gallery/Images, which the Gallery embeds. Deterministic; run it again only
// when a picture changes:
//
//     dotnet fsi tools/MakeGalleryImages.fsx

open System
open System.IO
open System.IO.Compression

let outDir = Path.Combine(__SOURCE_DIRECTORY__, "..", "samples", "Gallery", "Images")
Directory.CreateDirectory outDir |> ignore

// --- A small PNG writer: 8-bit RGBA, filter 0 on every row ------------------------------------------------

let crcTable =
    Array.init 256 (fun n ->
        let mutable c = uint32 n
        for _ in 0..7 do
            c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
        c)

let crc (bytes: byte[]) =
    let mutable c = 0xFFFFFFFFu
    for b in bytes do
        c <- crcTable[int ((c ^^^ uint32 b) &&& 0xFFu)] ^^^ (c >>> 8)
    c ^^^ 0xFFFFFFFFu

let bigEndian (v: uint32) = [| byte (v >>> 24); byte (v >>> 16); byte (v >>> 8); byte v |]

let chunk (kind: string) (data: byte[]) =
    let typed = Array.append (Text.Encoding.ASCII.GetBytes kind) data
    Array.concat [ bigEndian (uint32 data.Length); typed; bigEndian (crc typed) ]

let writePng (name: string) (w: int) (h: int) (pixel: int -> int -> struct (float * float * float * float)) =
    let raw = Array.zeroCreate<byte> (h * (w * 4 + 1))
    let clamp (v: float) = byte (Math.Round(Math.Clamp(v, 0.0, 1.0) * 255.0))
    for y in 0 .. h - 1 do
        let row = y * (w * 4 + 1)
        for x in 0 .. w - 1 do
            let struct (r, g, b, a) = pixel x y
            let i = row + 1 + x * 4
            raw[i] <- clamp r
            raw[i + 1] <- clamp g
            raw[i + 2] <- clamp b
            raw[i + 3] <- clamp a
    use packed = new MemoryStream()
    (use z = new ZLibStream(packed, CompressionLevel.SmallestSize, true)
     z.Write(raw, 0, raw.Length))
    let header = Array.concat [ bigEndian (uint32 w); bigEndian (uint32 h); [| 8uy; 6uy; 0uy; 0uy; 0uy |] ]
    let png =
        Array.concat [
            [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy; 0x0Duy; 0x0Auy; 0x1Auy; 0x0Auy |]
            chunk "IHDR" header
            chunk "IDAT" (packed.ToArray())
            chunk "IEND" [||]
        ]
    let path = Path.Combine(outDir, name)
    File.WriteAllBytes(path, png)
    Console.WriteLine(name + ": " + string w + " × " + string h + ", " + string png.Length + " bytes")

let mix (a: float) (b: float) (t: float) = a + (b - a) * t
let smooth (e0: float) (e1: float) (x: float) =
    let t = Math.Clamp((x - e0) / (e1 - e0), 0.0, 1.0)
    t * t * (3.0 - 2.0 * t)

// --- A landscape: a sky, a sun, three rows of hills, a lake (photo-like gradients) ------------------------

let landscape () =
    let w, h = 640, 400
    writePng "landscape.png" w h (fun x y ->
        let u = float x / float w
        let v = float y / float h
        // Sky: warm at the horizon, deep blue above.
        let sky = smooth 0.0 0.62 v
        let mutable r = mix 0.16 0.98 sky
        let mutable g = mix 0.27 0.72 sky
        let mutable b = mix 0.55 0.52 sky
        // The sun and its glow.
        let d = sqrt ((u - 0.68) ** 2.0 * 2.56 + (v - 0.46) ** 2.0)
        let glow = Math.Exp(-d * 9.0) * 0.6
        r <- r + glow
        g <- g + glow * 0.75
        b <- b + glow * 0.4
        if d < 0.055 then
            r <- 1.0
            g <- 0.93
            b <- 0.72
        // Hills, far to near, each a sum of waves.
        let hill (baseline: float) (amp: float) (freq: float) (phase: float) =
            baseline - amp * (sin (u * freq + phase) * 0.6 + sin (u * freq * 2.3 + phase * 1.7) * 0.4)
        let layers =
            [ hill 0.6 0.05 7.0 0.3, (0.45, 0.38, 0.52)
              hill 0.68 0.06 5.0 1.9, (0.27, 0.27, 0.38)
              hill 0.78 0.07 3.3 4.1, (0.13, 0.17, 0.22) ]
        for top, (lr, lg, lb) in layers do
            if v > top then
                let shade = 1.0 - (v - top) * 1.2
                r <- lr * shade
                g <- lg * shade
                b <- lb * shade
        // The lake: dark water, lighter near the shore, with the sun's broken reflection below it.
        if v > 0.86 then
            let depth = (v - 0.86) / 0.14
            let wave = sin (v * 300.0 + sin (u * 23.0) * 3.0)
            let shimmer = Math.Exp(-abs (u - 0.68) * (28.0 + depth * 12.0)) * (if wave > 0.2 then 0.75 else 0.1)
            r <- mix 0.30 0.08 depth + shimmer
            g <- mix 0.30 0.10 depth + shimmer * 0.8
            b <- mix 0.45 0.22 depth + shimmer * 0.5 + 0.015 * wave
        struct (r, g, b, 1.0))

// --- Pixel art: a 16 × 16 potion bottle (shows smooth against pixelated) ----------------------------------

let potion () =
    let art =
        [| "................"
           "......kkkk......"
           "......kwwk......"
           ".......kk......."
           "......kcck......"
           ".....kkcckk....."
           "....kgggggggk..."
           "...kgllgggggdk.."
           "...kglgggggggk.."
           "..kggggggggggdk."
           "..kgggggggggggk."
           "..kgggggggggddk."
           "..kdggggggggddk."
           "...kddgggggddk.."
           "....kkddddddk..."
           ".....kkkkkkk...." |]
    writePng "potion.png" 16 16 (fun x y ->
        match art[y][x] with
        | 'k' -> struct (0.10, 0.08, 0.16, 1.0)
        | 'w' -> struct (0.78, 0.60, 0.42, 1.0)
        | 'c' -> struct (0.85, 0.85, 0.92, 1.0)
        | 'g' -> struct (0.35, 0.80, 0.55, 1.0)
        | 'l' -> struct (0.80, 1.00, 0.85, 1.0)
        | 'd' -> struct (0.18, 0.50, 0.40, 1.0)
        | _ -> struct (0.0, 0.0, 0.0, 0.0))

// --- An avatar: a stylised face on a gradient, 256 × 256 (drawn at density 2 it's 128 DIPs) ----------------

let avatar () =
    let n = 256
    writePng "avatar.png" n n (fun x y ->
        let u = (float x + 0.5) / float n
        let v = (float y + 0.5) / float n
        let mutable r = mix 0.95 0.55 v
        let mutable g = mix 0.62 0.36 v
        let mutable b = mix 0.45 0.78 v
        let paint (cr, cg, cb) (cover: float) =
            r <- mix r cr cover
            g <- mix g cg cover
            b <- mix b cb cover
        let edge (d: float) = Math.Clamp(0.5 - d * float n, 0.0, 1.0)
        // Shoulders, head, eyes, smile.
        let shoulders = sqrt ((u - 0.5) ** 2.0 + ((v - 1.08) * 1.6) ** 2.0) - 0.42
        paint (0.20, 0.22, 0.36) (edge shoulders)
        let head = sqrt ((u - 0.5) ** 2.0 + ((v - 0.42) * 0.92) ** 2.0) - 0.2
        paint (0.98, 0.84, 0.70) (edge head)
        for ex in [ 0.43; 0.57 ] do
            paint (0.15, 0.12, 0.20) (edge (sqrt ((u - ex) ** 2.0 + (v - 0.40) ** 2.0) - 0.018))
        let smile = abs (sqrt ((u - 0.5) ** 2.0 + (v - 0.43) ** 2.0) - 0.09) - 0.008
        if v > 0.46 then paint (0.70, 0.30, 0.32) (edge smile)
        struct (r, g, b, 1.0))

landscape ()
potion ()
avatar ()
