namespace Scribble.Text

open System
open System.Threading

/// Id counters as plain static fields: a `static let` takes part in F#'s file initialization, which
/// failed ("static data accessed recursively") when several threads first used the type at once.
type internal FontIds private () =
    [<DefaultValue>] static val mutable private next: int
    static member Next() = lock typeof<FontIds> (fun () -> FontIds.next <- FontIds.next + 1; FontIds.next)

/// A font Scribble can draw with: the parsed face plus identity for caches and shapers.
[<Sealed>]
type Font(name: string, data: byte[]) =
    let id = FontIds.Next()
    let face = FontFace data

    /// Unique per Font instance; caches and shapers key on it.
    member _.Id = id
    member _.Name = name
    member _.Face = face
    /// The raw font file, for shapers that need it.
    member _.Data = data

    override _.ToString() = name

/// One positioned glyph from shaping. All values are in font units.
[<Struct>]
type ShapedGlyph =
    { Glyph: int
      /// Index (UTF-16 code unit) of the first character this glyph belongs to.
      Cluster: int
      XAdvance: int
      YAdvance: int
      XOffset: int
      YOffset: int }

/// Turns text into positioned glyphs: kerning, ligatures, mark positioning, complex scripts.
/// Results are in font units, so one shaping result serves every font size.
type IShaper =
    inherit IDisposable

    abstract Name: string
    abstract Shape: font: Font * text: string -> ShapedGlyph[]
