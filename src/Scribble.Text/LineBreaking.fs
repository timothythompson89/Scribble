namespace Scribble.Text

open System

type TextWrapping =
    /// One line per paragraph; text that doesn't fit runs past the edge (or is trimmed).
    | NoWrap
    /// Lines break at break opportunities so they fit the available width.
    | Wrap

type TextTrimming =
    | NoTrimming
    /// Text that doesn't fit ends with "…".
    | CharacterEllipsis

type TextAlignment =
    | Start
    | Center
    | End

/// Decides where a line may break. `CanBreakBefore(text, index)` is asked for each grapheme
/// cluster boundary: may a new line start at `index`?
///
/// The default rules are a deliberate simplification of Unicode line breaking (UAX #14); a full
/// implementation can replace them without changing anything else.
type ILineBreaker =
    abstract CanBreakBefore: text: string * index: int -> bool

module LineBreaking =
    let isWhitespace (c: char) = c = ' ' || c = '\t' || c = '　'

    /// Ideographic scripts (CJK, kana, Hangul, full-width forms) allow a break between any two characters.
    let isIdeographic (c: char) =
        (c >= '⺀' && c <= '鿿') || (c >= '가' && c <= '힯') || (c >= '豈' && c <= '﫿') || (c >= '＀' && c <= '￯')

    /// Breaks after spaces, after a hyphen that follows a letter or digit, and around ideographs.
    let simple =
        { new ILineBreaker with
            member _.CanBreakBefore(text, index) =
                if index <= 0 || index >= text.Length then
                    false
                else
                    let previous = text[index - 1]
                    let current = text[index]
                    (isWhitespace previous && not (isWhitespace current))
                    || (previous = '-' && index >= 2 && Char.IsLetterOrDigit text[index - 2] && not (isWhitespace current))
                    || isIdeographic current
                    || isIdeographic previous }

/// One laid-out line.
[<Struct>]
type TextLine =
    { /// What is drawn: the line without the spaces at its end.
      Text: string
      /// Width in DIPs, not counting trailing spaces.
      Width: float
      /// Where the line starts in the text that was laid out (a UTF-16 index).
      Start: int
      /// The index after the line's last character, the spaces at a wrap included (they hang past the edge,
      /// undrawn) and the line break not: the next line of the same paragraph starts here. For a line that was
      /// trimmed with "…", the untrimmed line's end.
      End: int }

/// Text broken into lines for a given width, ready to draw.
type TextLayout =
    { Lines: TextLine[]
      /// Width of the widest line, in DIPs.
      Width: float
      Height: float
      LineHeight: float
      /// Baseline of the first line, measured down from the top.
      Baseline: float
      /// True when text was cut off (by MaxLines or trimming).
      Truncated: bool
      Font: Font
      Size: float }
