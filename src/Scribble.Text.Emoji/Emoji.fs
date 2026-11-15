/// Color emoji for Scribble, in a separate package so apps that don't need emoji don't ship
/// the 1.4 MB font.
///
/// Attribution: the emoji art is Twemoji by Twitter, Inc. and other contributors, licensed under
/// CC-BY 4.0 (https://creativecommons.org/licenses/by/4.0/); apps that show it should credit it,
/// for example in an About box. The font file is under the SIL Open Font License 1.1.
module Scribble.Text.Emoji

open System.IO
open Scribble.Text

type private Marker = class end

let private twemoji =
    lazy
        (use stream = typeof<Marker>.Assembly.GetManifestResourceStream "Twemoji.ttf"
         use memory = new MemoryStream()
         stream.CopyTo memory
         Font("Twemoji", memory.ToArray()))

/// Twemoji, a color emoji font. Use it as a fallback:
/// `TextSystem(device, shaper, fallbacks = [ Emoji.Twemoji() ])`.
let Twemoji () = twemoji.Value

/// The attribution text the CC-BY 4.0 license asks for.
let Attribution = "Emoji graphics: Twemoji by Twitter, Inc. and other contributors, licensed under CC-BY 4.0."
