namespace Scribble.Icons

open Scribble.Core

/// Shortcuts on icons, there with `open Scribble.Icons`: `Material.home.filled`.
[<AutoOpen>]
module MaterialExtensions =
    type Icon with
        /// The filled version of a Material icon (rotation and flips kept), as `Material.filled`;
        /// any other icon comes back unchanged.
        member icon.filled = MaterialData.variant true icon
        /// The outlined version of a Material icon (rotation and flips kept), as `Material.outlined`.
        member icon.outlined = MaterialData.variant false icon
