namespace Scribble.Core

open System

/// A size in DIPs. Either dimension may be infinite during measuring ("as much as you like").
[<Struct>]
type Size =
    { Width: float
      Height: float }

    static member Create(width, height) : Size = { Width = width; Height = height }
    static member Zero: Size = { Width = 0.0; Height = 0.0 }
    static member Infinite: Size = { Width = Double.PositiveInfinity; Height = Double.PositiveInfinity }

/// Space around or inside an element, in DIPs.
[<Struct>]
type Thickness =
    { Left: float
      Top: float
      Right: float
      Bottom: float }

    static member Zero = Thickness.Uniform 0.0
    static member Uniform(value: float) = { Left = value; Top = value; Right = value; Bottom = value }
    static member Symmetric(horizontal: float, vertical: float) = { Left = horizontal; Top = vertical; Right = horizontal; Bottom = vertical }
    static member Create(left, top, right, bottom) = { Left = left; Top = top; Right = right; Bottom = bottom }
    member t.Horizontal = t.Left + t.Right
    member t.Vertical = t.Top + t.Bottom

type HorizontalAlignment =
    | Stretch
    | Left
    | Center
    | Right

type VerticalAlignment =
    | Stretch
    | Top
    | Center
    | Bottom

type Orientation =
    | Vertical
    | Horizontal

/// A drop shadow, like CSS box-shadow: blur radius, offset and spread in DIPs.
[<Struct>]
type BoxShadow =
    { Blur: float
      OffsetX: float
      OffsetY: float
      Spread: float
      Color: Scribble.Rendering.Color }

/// A width or height. Plain numbers in settings mean Px (`Border.width 36.0`); strings work too
/// (`"36px"`, `"36"`, `"50%"`, `"2*"`, `"*"`, `"auto"`).
type Length =
    /// Size to content (or stretch, per alignment).
    | Auto
    /// Fixed, in device-independent pixels (DIPs), like CSS px.
    | Px of float
    /// A share of the space the parent offers, margin included. In unlimited space it acts like Auto.
    | Percent of float
    /// A weighted share of leftover space, in panels that share it out (StackPanel, later Grid).
    /// Elsewhere it acts like Stretch.
    | Star of float

    override l.ToString() =
        let n (v: float) = v.ToString("0.###", Globalization.CultureInfo.InvariantCulture)
        match l with
        | Auto -> "auto"
        | Px v -> n v + "px"
        | Percent v -> n v + "%"
        | Star 1.0 -> "*"
        | Star v -> n v + "*"

module Length =
    let private number (text: string) =
        match Double.TryParse(text.Trim(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
        | true, v when v >= 0.0 && not (Double.IsInfinity v) -> ValueSome v
        | _ -> ValueNone

    /// Reads "45px", "45", "45%", "2*", "*" or "auto" (case and surrounding spaces don't matter).
    let tryParse (text: string) : Length voption =
        if isNull text then ValueNone
        else
            let t = text.Trim().ToLowerInvariant()
            if t = "auto" then ValueSome Auto
            elif t = "*" then ValueSome(Star 1.0)
            elif t.EndsWith "px" then number t[.. t.Length - 3] |> ValueOption.map Px
            elif t.EndsWith "%" then number t[.. t.Length - 2] |> ValueOption.map Percent
            elif t.EndsWith "*" then number t[.. t.Length - 2] |> ValueOption.map Star
            else number t |> ValueOption.map Px

    let parse (text: string) : Length =
        match tryParse text with
        | ValueSome l -> l
        | ValueNone ->
            invalidArg "text" ("'" + text + "' isn't a length. Use a number of DIPs (\"45\" or \"45px\"), a percentage (\"45%\"), a star share (\"2*\" or \"*\") or \"auto\".")
