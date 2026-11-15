module Scribble.Tests.TextLayoutTests

open System
open Xunit
open Scribble.Text
open Scribble.Tests.TestShaping

let private layout (text: string) maxWidth wrapping trimming maxLines =
    (textSystem ()).Layout(text, Fonts.Regular(), 20.0, maxWidth, wrapping, trimming, maxLines)

let private lines (l: TextLayout) = l.Lines |> Array.map _.Text |> List.ofArray

[<Fact>]
let ``unconstrained text is one line per paragraph`` () =
    let l = layout "hello world\nsecond" Double.PositiveInfinity Wrap NoTrimming 0
    Assert.Equal<string list>([ "hello world"; "second" ], lines l)
    Assert.Equal(110.0, l.Width, 6)
    Assert.Equal(2.0 * l.LineHeight, l.Height, 6)

[<Fact>]
let ``wrapping breaks after spaces and drops them from the width`` () =
    // 10 DIPs per character: "hello world again" in 120 DIPs.
    let l = layout "hello world again" 120.0 Wrap NoTrimming 0
    Assert.Equal<string list>([ "hello world"; "again" ], lines l)
    Assert.Equal(110.0, l.Lines[0].Width, 6)

[<Fact>]
let ``a word wider than the line breaks inside the word`` () =
    let l = layout "abcdefghij" 40.0 Wrap NoTrimming 0
    Assert.Equal<string list>([ "abcd"; "efgh"; "ij" ], lines l)

[<Fact>]
let ``hyphens are break opportunities`` () =
    let l = layout "well-known" 60.0 Wrap NoTrimming 0
    Assert.Equal<string list>([ "well-"; "known" ], lines l)

[<Fact>]
let ``ideographs break anywhere`` () =
    let l = layout "日本語の文章" 30.0 Wrap NoTrimming 0
    Assert.Equal<string list>([ "日本語"; "の文章" ], lines l)

[<Fact>]
let ``NoWrap ignores the width`` () =
    let l = layout "hello world" 50.0 NoWrap NoTrimming 0
    Assert.Equal<string list>([ "hello world" ], lines l)
    Assert.False l.Truncated

[<Fact>]
let ``trimming ends an overlong line with an ellipsis that fits`` () =
    let l = layout "hello world" 60.0 NoWrap CharacterEllipsis 0
    Assert.Equal<string list>([ "hello…" ], lines l)
    Assert.True(l.Width <= 60.0)
    Assert.True l.Truncated

[<Fact>]
let ``MaxLines cuts off and trims the last visible line`` () =
    let l = layout "one two three four" 80.0 Wrap CharacterEllipsis 2
    Assert.Equal(2, l.Lines.Length)
    Assert.Equal("one two", l.Lines[0].Text)
    Assert.EndsWith("…", l.Lines[1].Text)
    Assert.True l.Truncated

[<Fact>]
let ``empty text is one empty line`` () =
    let l = layout "" 100.0 Wrap NoTrimming 0
    Assert.Equal(1, l.Lines.Length)
    Assert.Equal(0.0, l.Width)
    Assert.Equal(l.LineHeight, l.Height, 6)
