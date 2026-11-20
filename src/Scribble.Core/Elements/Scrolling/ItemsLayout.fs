namespace Scribble.Core

open System
open System.Collections.Generic
open System.Diagnostics
open Scribble.Rendering

/// How an ItemsView lays out its items. Variable heights by default; the fixed kinds are laid out
/// by arithmetic (exact scrollbar from the start).
[<RequireQualifiedAccess>]
type ItemsLayout =
    internal
    | Stack
    | FixedStack of itemHeight: float
    | Wrap of cellWidth: float * cellHeight: float
    | WrapFill of minWidth: float * cellHeight: float
    | Grid of columns: int

module ItemsLayout =
    /// One item per row, each as tall as it wants (measured as it's realized; unmeasured ones are
    /// estimated from the average, so the scrollbar may adjust a little while scrolling).
    let stack = ItemsLayout.Stack
    /// One item per row, all `itemHeight` DIPs tall: exact and fastest.
    let fixedStack (itemHeight: float) = ItemsLayout.FixedStack itemHeight
    /// Cells of `width` × `height` DIPs, left to right, as many per row as fit.
    let wrap (width: float) (height: float) = ItemsLayout.Wrap(width, height)
    /// As many cells per row as fit at `minWidth` DIPs, each then widened equally so the row fills the width;
    /// `height` DIPs tall. The width of a cell follows the list's width alone: a short last row, or a list with
    /// fewer items than a row holds, has cells of the same width at the left.
    let wrapFill (minWidth: float) (height: float) = ItemsLayout.WrapFill(minWidth, height)
    /// `columns` cells per row sharing the width; every cell as tall as the first item.
    let grid (columns: int) = ItemsLayout.Grid(max 1 columns)

