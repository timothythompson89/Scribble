/// The Material Symbols package: names, the filled versions, icons from text, and every path.
module Scribble.Tests.MaterialTests

open System
open Xunit
open Scribble.Rendering
open Scribble.Text
open Scribble.Core
open Scribble.Icons

[<Fact>]
let ``every icon has an outlined and a filled path, and all 8,608 parse`` () =
    let names = List.ofSeq (Material.names ())
    Assert.Equal(4304, names.Length)
    for name in names do
        let outlined = Material.icon name
        let filled = outlined |> Material.filled
        PathGeometry.Parse outlined.PathData |> ignore
        PathGeometry.Parse filled.PathData |> ignore

[<Fact>]
let ``members are Material icons in Google's box, keywords in backticks`` () =
    let home = Material.home
    Assert.Equal("home", home.Name)
    Assert.Equal(Rect.Create(0.0, -960.0, 960.0, 960.0), home.ViewBox)
    Assert.Equal(home, Material.home) // equal values, though each access makes a new one
    Assert.Equal("arrow_upward", Material.arrowUpward.Name)
    Assert.Equal("10k", Material._10k.Name)
    Assert.Equal("class", Material.``class``.Name)

[<Fact>]
let ``filled and outlined switch the shape and keep rotation and flips`` () =
    let star = Material.star |> Icon.rotate 90.0 |> Icon.flipHorizontal
    let filled = star |> Material.filled
    Assert.Equal("star-filled", filled.Name)
    Assert.NotEqual<string>(star.PathData, filled.PathData)
    Assert.Equal(90.0, filled.Rotation)
    Assert.True filled.FlipHorizontal
    Assert.Equal(star, filled |> Material.outlined)
    Assert.Equal(filled, filled |> Material.filled)
    // Not a Material icon: unchanged.
    let own = Icon.fromPath "M0 0h24v24H0z" [ Icon.name "star" ]
    Assert.Equal(own, own |> Material.filled)
    // The member shortcuts are the same functions.
    Assert.Equal(Material.star |> Material.filled, Material.star.filled)
    Assert.Equal(Material.star, Material.star.filled.outlined)
    Assert.Equal(own, own.filled)

[<Fact>]
let ``icons from text read the spec format, and describe writes it back`` () =
    let icon = Material.icon "star-filled-rotate90"
    Assert.Equal(Material.star |> Material.filled |> Icon.rotate 90.0, icon)
    Assert.Equal("star-filled-rotate90", Icon.describe icon)
    Assert.Equal(icon, Material.icon (Icon.describe icon))
    Assert.Equal(Material.arrowUpward |> Icon.flipHorizontal, Material.icon "arrow-upward-fliph")
    Assert.Equal(Material.arrowUpward, Material.icon "Arrow Upward")
    Assert.Equal(Material.star, Material.icon "star-filled-outlined")
    Assert.Equal(Material.flip, Material.icon "flip") // a name that is also an option word stays a name

[<Fact>]
let ``unknown names throw, or give ValueNone with tryIcon`` () =
    let error = Assert.Throws<ArgumentException>(fun () -> Material.icon "no_such_icon" |> ignore)
    Assert.Contains("no_such_icon", error.Message)
    Assert.Equal(ValueNone, Material.tryIcon "no-such-icon-rotate90")
    Assert.Equal(ValueSome Material.home, Material.tryIcon "home")

[<Fact>]
let ``the Icons sample's F# names are Material's members`` () =
    let members =
        typeof<Material>.GetProperties(Reflection.BindingFlags.Public ||| Reflection.BindingFlags.Static)
        |> Array.map (fun p -> p.Name)
        |> set
    Assert.Equal(4304, members.Count)
    for name in Material.names () do
        let shown = Scribble.Samples.IconGallery.memberName name
        Assert.True(members.Contains(shown.Trim '`'), shown)
    Assert.Equal("``class``", Scribble.Samples.IconGallery.memberName "class")
    Assert.Equal("_10k", Scribble.Samples.IconGallery.memberName "10k")
    Assert.Equal("arrowUpward", Scribble.Samples.IconGallery.memberName "arrow_upward")
