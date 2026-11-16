namespace Scribble.Core

open System
open System.Globalization
open Scribble.Rendering
open Scribble.Text

/// What Icon settings configure.
type internal IconOptions =
    { Name: string
      ViewBox: Rect
      Rotation: float
      FlipHorizontal: bool
      FlipVertical: bool }

/// One setting for `Icon.fromPath`: `Icon.viewBox (0.0, 0.0, 24.0, 24.0)`.
[<Struct>]
type IconSetting = internal IconSetting of (IconOptions -> IconOptions)

/// An icon: a shape from SVG path data, drawn in one color (the text color around it, unless
/// set). Immutable; equal icons compare equal.
/// ```
/// let menu = Icon.fromPath "M3 18h18v-2H3zm0-5h18v-2H3zm0-7v2h18V6z" [ Icon.viewBox (0.0, 0.0, 24.0, 24.0) ]
/// IconView.create [ IconView.icon menu ]
/// Button.create [ Button.icon (menu.Rotated 90.0); Button.text "Menu" ]
/// ```
/// Shapes are filled with the nonzero rule (as in fonts); SVG's even-odd rule isn't supported.
[<Sealed>]
type Icon private (path: Lazy<PathGeometry>, data: string, options: IconOptions) =
    static let defaults =
        { Name = ""
          ViewBox = Rect.Create(0.0, 0.0, 24.0, 24.0)
          Rotation = 0.0
          FlipHorizontal = false
          FlipVertical = false }

    /// The name shown by tools (the inspector); empty for unnamed icons.
    member _.Name = options.Name
    /// The SVG path data.
    member _.PathData = data
    /// The coordinate box the path is drawn in; it's scaled to the icon's size.
    member _.ViewBox = options.ViewBox
    /// Clockwise rotation in degrees, around the center.
    member _.Rotation = options.Rotation
    member _.IsFlippedHorizontally = options.FlipHorizontal
    member _.IsFlippedVertically = options.FlipVertical
    /// The parsed shape (parsed on first use).
    member _.Path = path.Value

    member private _.With(change: IconOptions -> IconOptions) = Icon(path, data, change options)
    /// The same icon rotated clockwise by `degrees` (replacing any rotation).
    member this.Rotated(degrees: float) = this.With(fun o -> { o with Rotation = degrees })
    /// Mirrored left to right (toggles).
    member this.FlippedHorizontally = this.With(fun o -> { o with FlipHorizontal = not o.FlipHorizontal })
    /// Mirrored top to bottom (toggles).
    member this.FlippedVertically = this.With(fun o -> { o with FlipVertical = not o.FlipVertical })

    /// An icon from SVG path data, in a 24 × 24 box unless `Icon.viewBox` says otherwise. The data
    /// is checked when the icon is first drawn (a FormatException then means it's malformed).
    static member fromPath (pathData: string) (settings: IconSetting list) : Icon =
        if String.IsNullOrWhiteSpace pathData then invalidArg (nameof pathData) "Path data can't be empty."
        let options = settings |> List.fold (fun o (IconSetting apply) -> apply o) defaults
        Icon(lazy (PathGeometry.Parse pathData), pathData, options)

    /// The path's coordinate box (default 0, 0, 24, 24, as most icon sets use).
    static member viewBox(x: float, y: float, width: float, height: float) =
        IconSetting(fun o -> { o with ViewBox = Rect.Create(x, y, width, height) })

    /// A name for tools (the inspector shows it).
    static member name(value: string) = IconSetting(fun o -> { o with Name = (if isNull value then "" else value) })

    /// Clockwise rotation in degrees, around the center.
    static member rotate(degrees: float) = IconSetting(fun o -> { o with Rotation = degrees })
    static member flipHorizontal = IconSetting(fun o -> { o with FlipHorizontal = true })
    static member flipVertical = IconSetting(fun o -> { o with FlipVertical = true })

    /// "name" or "path", with any rotation and flips: "arrow-rotate90-fliph".
    member _.Description =
        let parts = [
            yield (if options.Name.Length > 0 then options.Name else "path")
            if options.Rotation <> 0.0 then yield "rotate" + options.Rotation.ToString(CultureInfo.InvariantCulture)
            if options.FlipHorizontal then yield "fliph"
            if options.FlipVertical then yield "flipv"
        ]
        String.Join("-", parts)

    override this.ToString() = this.Description

    override this.Equals(other: obj) =
        match other with
        | :? Icon as o ->
            obj.ReferenceEquals(this, o)
            || (String.Equals(data, o.PathData, StringComparison.Ordinal)
                && options.ViewBox = o.ViewBox
                && options.Rotation = o.Rotation
                && options.FlipHorizontal = o.IsFlippedHorizontally
                && options.FlipVertical = o.IsFlippedVertically
                && String.Equals(options.Name, o.Name, StringComparison.Ordinal))
        | _ -> false

    override _.GetHashCode() =
        HashCode.Combine(data.Length, StringComparer.Ordinal.GetHashCode data, options.Rotation, options.FlipHorizontal, options.FlipVertical)

    interface IEquatable<Icon> with
        member this.Equals(other: Icon) = this.Equals(box other)
