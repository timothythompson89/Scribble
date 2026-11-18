namespace Scribble.Imaging

open System

/// An image file's size, read from its header without decoding it (so the first layout is right).
[<Struct>]
type ImageInfo = { Width: int; Height: int }

/// Decoded pixels: 8-bit RGBA, alpha premultiplied, row by row from the top with no padding
/// (`Pixels.Length = Width * Height * 4`). Colours are sRGB: embedded colour profiles and gamma are ignored.
type DecodedImage = { Width: int; Height: int; Pixels: byte[] }

/// The data isn't an image Scribble can read: an unknown format, a corrupt or truncated file, or one too big to
/// decode. Decoders throw only this (anything else would be a bug in the decoder).
type ImageFormatException(message: string) =
    inherit Exception(message)

/// The colour arithmetic decoders share, so every format rounds the same way.
module internal Pixels =
    /// A 16-bit channel rounded to the nearest 8-bit one (`v * 257` comes back as `v`).
    let round16 (v: int) = (v * 255 + 32767) / 65535

    /// A straight colour channel times alpha, rounded to nearest.
    let premultiply (c: int) (a: int) = (c * a + 127) / 255

    /// The largest pixel count a decoded image may have (its RGBA array must fit in one .NET array).
    let maxPixels = int64 Array.MaxLength / 4L
