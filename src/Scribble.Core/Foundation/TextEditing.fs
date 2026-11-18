namespace Scribble.Core

open System
open System.Globalization
open System.Text

/// What a text editor holds: the text and the selection in it, as UTF-16 indices into `Text`. The selection runs
/// from `Anchor` (where it started) to `Caret` (where the caret stands, and what the keys move); equal: no
/// selection. Both always stand between grapheme clusters (what a reader sees as one character), never inside an
/// emoji or between a letter and its accent.
[<Struct>]
type EditState =
    { Text: string
      Anchor: int
      Caret: int }

    /// The selection's first index.
    member s.Start = min s.Anchor s.Caret
    /// The index after the selection's last character.
    member s.End = max s.Anchor s.Caret
    member s.HasSelection = s.Anchor <> s.Caret
    member s.SelectedText = s.Text.Substring(s.Start, s.End - s.Start)

/// Where a key moves the caret.
[<RequireQualifiedAccess>]
type CaretMove =
    /// One grapheme cluster back or on.
    | Left
    | Right
    /// To the start of this word, or of the one before (Ctrl+Left).
    | WordLeft
    /// To the start of the next word (Ctrl+Right).
    | WordRight
    | Start
    | End

/// The editing a TextBox does, with no UI in it (DESIGN.md, "M9", decision 6): every function takes a state and
/// gives the next one, so a history of states is an undo history (9b) and the rules are tested as properties.
/// One line of text: line breaks never get in.
module TextEdit =
    let empty = { Text = ""; Anchor = 0; Caret = 0 }

    /// The start of the grapheme cluster `index` is in (itself when it stands between two).
    let stopAtOrBefore (text: string) (index: int) =
        let index = max 0 (min text.Length index)
        let mutable i = 0
        let mutable result = 0
        while i < index do
            result <- i
            i <- i + StringInfo.GetNextTextElementLength(text, i)
        if i = index then index else result

    /// The next place a caret can stand after `index` (the text's end when there is none).
    let nextStop (text: string) (index: int) =
        let i = stopAtOrBefore text index
        if i >= text.Length then text.Length else i + StringInfo.GetNextTextElementLength(text, i)

    /// The place a caret can stand before `index` (0 when there is none).
    let previousStop (text: string) (index: int) =
        let index = stopAtOrBefore text index
        if index = 0 then 0 else stopAtOrBefore text (index - 1)

    /// How many grapheme clusters the text has.
    let graphemes (text: string) =
        let mutable i = 0
        let mutable n = 0
        while i < text.Length do
            i <- i + StringInfo.GetNextTextElementLength(text, i)
            n <- n + 1
        n

    /// 0: white space; 1: letters, digits and '_'; 2: anything else (punctuation, symbols, emoji). A word is a run
    /// of one kind that isn't white space.
    let private kindAt (text: string) (index: int) =
        let rune = Rune.GetRuneAt(text, index)
        if Rune.IsWhiteSpace rune then 0
        elif Rune.IsLetterOrDigit rune || rune.Value = int '_' then 1
        else
            match Rune.GetUnicodeCategory rune with
            // Accents and joiners belong to the letters they follow.
            | UnicodeCategory.NonSpacingMark | UnicodeCategory.SpacingCombiningMark | UnicodeCategory.EnclosingMark -> 1
            | _ -> 2

    /// The start of the word before `index` (Ctrl+Left): back over white space, then over one run.
    let previousWord (text: string) (index: int) =
        let mutable i = stopAtOrBefore text index
        while i > 0 && kindAt text (previousStop text i) = 0 do
            i <- previousStop text i
        if i > 0 then
            let kind = kindAt text (previousStop text i)
            while i > 0 && kindAt text (previousStop text i) = kind do
                i <- previousStop text i
        i

    /// The start of the word after `index` (Ctrl+Right): on over this run, then over white space.
    let nextWord (text: string) (index: int) =
        let mutable i = stopAtOrBefore text index
        if i < text.Length then
            let kind = kindAt text i
            if kind <> 0 then
                while i < text.Length && kindAt text i = kind do
                    i <- nextStop text i
            while i < text.Length && kindAt text i = 0 do
                i <- nextStop text i
        i

    /// The run of one kind around `index` (a word, a run of spaces, a run of punctuation): what a double-click
    /// selects. At the text's end, the run before it.
    let wordAt (text: string) (index: int) : struct (int * int) =
        if text.Length = 0 then struct (0, 0)
        else
            let at = stopAtOrBefore text index
            let at = if at >= text.Length then previousStop text at else at
            let kind = kindAt text at
            let mutable first = at
            while first > 0 && kindAt text (previousStop text first) = kind do
                first <- previousStop text first
            let mutable last = nextStop text at
            while last < text.Length && kindAt text last = kind do
                last <- nextStop text last
            struct (first, last)

    /// The state with its indices inside the text and between grapheme clusters.
    let normalize (s: EditState) =
        let text = if isNull s.Text then "" else s.Text
        { Text = text; Anchor = stopAtOrBefore text s.Anchor; Caret = stopAtOrBefore text s.Caret }

    /// A state showing `text` with the caret at its end.
    let ofText (text: string) =
        let text = if isNull text then "" else text
        { Text = text; Anchor = text.Length; Caret = text.Length }

    /// Other text from outside (a controlled TextBox whose view answered with something else than was typed):
    /// the caret and selection stay where they were, clamped into it.
    let withText (text: string) (s: EditState) = normalize { s with Text = text }

    /// The caret at `index`; with `extend` the anchor stays (Shift+click, a drag), else the selection goes.
    let moveTo (index: int) (extend: bool) (s: EditState) =
        let caret = stopAtOrBefore s.Text index
        { s with Caret = caret; Anchor = (if extend then s.Anchor else caret) }

    /// A key's move. With a selection and no `extend`, Left and Right go to that end of the selection (and no
    /// further), as editors do.
    let move (how: CaretMove) (extend: bool) (s: EditState) =
        let target =
            match how with
            | CaretMove.Left when s.HasSelection && not extend -> s.Start
            | CaretMove.Right when s.HasSelection && not extend -> s.End
            | CaretMove.Left -> previousStop s.Text s.Caret
            | CaretMove.Right -> nextStop s.Text s.Caret
            | CaretMove.WordLeft -> previousWord s.Text s.Caret
            | CaretMove.WordRight -> nextWord s.Text s.Caret
            | CaretMove.Start -> 0
            | CaretMove.End -> s.Text.Length
        moveTo target extend s

    let selectAll (s: EditState) = { s with Anchor = 0; Caret = s.Text.Length }

    /// Selects from `first` to `last`, the caret at `last`.
    let select (first: int) (last: int) (s: EditState) =
        { s with Anchor = stopAtOrBefore s.Text first; Caret = stopAtOrBefore s.Text last }

    /// Selects the word (or run of spaces or punctuation) at `index`: a double-click.
    let selectWordAt (index: int) (s: EditState) =
        let struct (first, last) = wordAt s.Text index
        { s with Anchor = first; Caret = last }

    /// What may be typed or pasted into one line: no control characters; a line break or tab becomes a space.
    let clean (text: string) =
        if isNull text then ""
        else
            let mutable needed = false
            for c in text do
                if Char.IsControl c then needed <- true
            if not needed then text
            else
                let sb = StringBuilder(text.Length)
                let mutable i = 0
                while i < text.Length do
                    let c = text[i]
                    if c = '\r' && i + 1 < text.Length && text[i + 1] = '\n' then
                        sb.Append ' ' |> ignore
                        i <- i + 1
                    elif c = '\n' || c = '\r' || c = '\t' then sb.Append ' ' |> ignore
                    elif not (Char.IsControl c) then sb.Append c |> ignore
                    i <- i + 1
                sb.ToString()

    /// The first `count` grapheme clusters of `text`.
    let private firstGraphemes (text: string) (count: int) =
        let mutable i = 0
        let mutable n = 0
        while i < text.Length && n < count do
            i <- i + StringInfo.GetNextTextElementLength(text, i)
            n <- n + 1
        text.Substring(0, i)

    /// Removes the selection (nothing without one); the caret stands where it was.
    let deleteSelection (s: EditState) =
        if not s.HasSelection then s
        else { Text = s.Text.Remove(s.Start, s.End - s.Start); Anchor = s.Start; Caret = s.Start }

    /// Types or pastes `text` over the selection, cleaned for one line; the caret ends after it. `maxLength`
    /// (0: no limit) is the most grapheme clusters the text may have: what doesn't fit is left out.
    let insert (text: string) (maxLength: int) (s: EditState) =
        let text = clean text
        let cleared = deleteSelection s
        let text =
            if maxLength <= 0 then text
            else firstGraphemes text (max 0 (maxLength - graphemes cleared.Text))
        // Nothing to put in (only control characters, or no room): nothing changes, the selection included.
        if text.Length = 0 then s
        else
            // Joined to what's before it the new text may form a cluster with it (an accent typed after a letter):
            // the caret goes to the cluster's end.
            let joined = cleared.Text.Insert(cleared.Caret, text)
            let after = cleared.Caret + text.Length
            let caret = if stopAtOrBefore joined after = after then after else nextStop joined after
            { Text = joined; Anchor = caret; Caret = caret }

    let private removeRange (first: int) (last: int) (s: EditState) =
        if last <= first then s else { Text = s.Text.Remove(first, last - first); Anchor = first; Caret = first }

    /// Backspace: the selection, else the grapheme cluster before the caret.
    let backspace (s: EditState) =
        if s.HasSelection then deleteSelection s else removeRange (previousStop s.Text s.Caret) s.Caret s

    /// Delete: the selection, else the grapheme cluster after the caret.
    let delete (s: EditState) =
        if s.HasSelection then deleteSelection s else removeRange s.Caret (nextStop s.Text s.Caret) s

    /// Ctrl+Backspace: the selection, else back to the start of the word.
    let backspaceWord (s: EditState) =
        if s.HasSelection then deleteSelection s else removeRange (previousWord s.Text s.Caret) s.Caret s

    /// Ctrl+Delete: the selection, else on to the start of the next word.
    let deleteWord (s: EditState) =
        if s.HasSelection then deleteSelection s else removeRange s.Caret (nextWord s.Text s.Caret) s
