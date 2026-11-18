/// CRC-32 (ISO 3309, the one PNG chunks use), slicing by 8: eight tables, eight bytes a step (a megabyte-sized
/// IDAT chunk is checked about five times faster than a byte at a time).
module internal Scribble.Imaging.Crc32

let private tables =
    let t = Array.zeroCreate<uint32> (8 * 256)
    for n in 0..255 do
        let mutable c = uint32 n
        for _ in 0..7 do
            c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
        t[n] <- c
    for k in 1..7 do
        for n in 0..255 do
            let prev = t[(k - 1) * 256 + n]
            t[k * 256 + n] <- t[int (prev &&& 0xFFu)] ^^^ (prev >>> 8)
    t

/// The CRC of `count` bytes of `data` from `start`.
let compute (data: byte[]) (start: int) (count: int) : uint32 =
    let t = tables
    let mutable c = 0xFFFFFFFFu
    let mutable i = start
    let last = start + count
    while last - i >= 8 do
        let lo =
            c
            ^^^ (uint32 data[i] ||| (uint32 data[i + 1] <<< 8) ||| (uint32 data[i + 2] <<< 16) ||| (uint32 data[i + 3] <<< 24))
        let hi = uint32 data[i + 4] ||| (uint32 data[i + 5] <<< 8) ||| (uint32 data[i + 6] <<< 16) ||| (uint32 data[i + 7] <<< 24)
        c <-
            t[7 * 256 + int (lo &&& 0xFFu)]
            ^^^ t[6 * 256 + int ((lo >>> 8) &&& 0xFFu)]
            ^^^ t[5 * 256 + int ((lo >>> 16) &&& 0xFFu)]
            ^^^ t[4 * 256 + int (lo >>> 24)]
            ^^^ t[3 * 256 + int (hi &&& 0xFFu)]
            ^^^ t[2 * 256 + int ((hi >>> 8) &&& 0xFFu)]
            ^^^ t[1 * 256 + int ((hi >>> 16) &&& 0xFFu)]
            ^^^ t[int (hi >>> 24)]
        i <- i + 8
    while i < last do
        c <- t[int ((c ^^^ uint32 data[i]) &&& 0xFFu)] ^^^ (c >>> 8)
        i <- i + 1
    c ^^^ 0xFFFFFFFFu
