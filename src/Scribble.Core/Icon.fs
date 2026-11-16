namespace Scribble.Core

open System
open System.Collections.Concurrent
open System.Globalization
open Scribble.Rendering
open Scribble.Text

/// An icon: a shape from SVG path data, drawn in one color (the text color around it, unless
/// set). A plain value: equal icons compare equal. Make one with `Icon.fromPath`; change one with
/// the same functions that configure it:
/// ```
/// let menu = Icon.fromPath "M3 18h18v-2H3zm0-5h18v-2H3zm0-7v2h18V6z" [ Icon.name "menu" ]
/// IconView.create [ IconView.icon menu ]
/// Button.create [ Button.icon (menu |> Icon.rotate 90.0); Button.text "Menu" ]
/// ```
/// Shapes are filled with the nonzero rule (as in fonts); SVG's even-odd rule isn't supported.
type Icon =
    { /// The name shown by tools (the inspector); empty for unnamed icons.
      Name: string
      /// The SVG path data.
      PathData: string
      /// The coordinate box the path is drawn in; it's scaled to the icon's size.
      ViewBox: Rect
      /// Clockwise rotation in degrees, around the center.
      Rotation: float
      /// Mirrored left to right.
      FlipHorizontal: bool
      /// Mirrored top to bottom.
      FlipVertical: bool }

    /// "name" or "path", with any rotation and flips: "arrow-rotate90-fliph" (also `Icon.describe`).
    override this.ToString() =
        let parts = [
            yield (if this.Name.Length > 0 then this.Name else "path")
            if this.Rotation <> 0.0 then yield "rotate" + this.Rotation.ToString(CultureInfo.InvariantCulture)
            if this.FlipHorizontal then yield "fliph"
            if this.FlipVertical then yield "flipv"
        ]
        String.Join("-", parts)

module Icon =
    /// Parsed paths by their data, shared by every icon with the same data (and every thread).
    let private parsed = ConcurrentDictionary<string, PathGeometry>(StringComparer.Ordinal)

    /// The icon's parsed shape (parsed on first use; a FormatException means the data is malformed).
    let internal geometry (icon: Icon) = parsed.GetOrAdd(icon.PathData, fun data -> PathGeometry.Parse data)

    /// A name for tools (the inspector shows it).
    let name (value: string) (icon: Icon) = { icon with Name = (if isNull value then "" else value) }

    /// The path's coordinate box (default 0, 0, 24, 24, as most icon sets use).
    let viewBox (x: float, y: float, width: float, height: float) (icon: Icon) =
        { icon with ViewBox = Rect.Create(x, y, width, height) }

    /// Clockwise rotation in degrees, around the center (replacing any rotation).
    let rotate (degrees: float) (icon: Icon) = { icon with Rotation = degrees }

    /// Mirrored left to right.
    let flipHorizontal (icon: Icon) = { icon with FlipHorizontal = true }

    /// Mirrored top to bottom.
    let flipVertical (icon: Icon) = { icon with FlipVertical = true }

    /// An icon from SVG path data, in a 24 × 24 box unless `Icon.viewBox` says otherwise. The data
    /// is checked when the icon is first drawn (a FormatException then means it's malformed).
    let fromPath (pathData: string) (settings: (Icon -> Icon) list) : Icon =
        if String.IsNullOrWhiteSpace pathData then invalidArg (nameof pathData) "Path data can't be empty."
        let start =
            { Name = ""
              PathData = pathData
              ViewBox = Rect.Create(0.0, 0.0, 24.0, 24.0)
              Rotation = 0.0
              FlipHorizontal = false
              FlipVertical = false }
        settings |> List.fold (fun icon apply -> apply icon) start

    /// "name" or "path", with any rotation and flips: "arrow-rotate90-fliph".
    let describe (icon: Icon) = icon.ToString()
