/// A stand-in shaper for tests: one glyph per UTF-16 unit, 1024 font units each. With Inter
/// (2048 units per em) at size 20 that's exactly 10 DIPs per character.
module Scribble.Tests.TestShaping

open Scribble.Rendering
open Scribble.Text

type FixedShaper() =
    interface IShaper with
        member _.Name = "fixed"
        member _.Shape(_, text) =
            [| for i in 0 .. text.Length - 1 -> { Glyph = 0; Cluster = i; XAdvance = 1024; YAdvance = 0; XOffset = 0; YOffset = 0 } |]
        member _.Dispose() = ()

let textSystem () = TextSystem(GlyphAtlas(256), new FixedShaper())
