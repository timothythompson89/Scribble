/// Image files to pixels, the format found from the data's first bytes. PNG so far; JPEG next (DESIGN.md,
/// "M8 step 3b", decision 2).
module Scribble.Imaging.ImageDecoder

let private unknown () : 'T =
    raise (ImageFormatException "Not an image format Scribble reads (PNG is the only one so far).")

/// The image's size from its header, without decoding it. Throws `ImageFormatException` for an unknown format
/// or a corrupt header.
let readInfo (data: byte[]) : ImageInfo =
    if Png.isPng data then Png.readInfo data else unknown ()

/// Decodes an image file to premultiplied RGBA. Throws `ImageFormatException` when it can't.
let decode (data: byte[]) : DecodedImage =
    if Png.isPng data then Png.decode data else unknown ()
