/// The Material Symbols data behind `Material`: two sets of "name<TAB>path data" lines, stored as
/// UTF-8 text (the browser's .NET has no Brotli decoder, and a browser publish compresses the DLL
/// anyway), each read on first use into bytes and an index; a path's string is made when its icon
/// is first asked for. All of it is safe to use from any thread.
module internal Scribble.Icons.MaterialData

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text
open Scribble.Rendering
open Scribble.Core

/// One decoded set: the text and where each icon's path data is in it.
type private IconSet = { Bytes: byte[]; Index: Dictionary<string, struct (int * int)>; Names: string[] }

let private load (variant: string) =
    let assembly = typeof<IconSet>.Assembly
    use stream = assembly.GetManifestResourceStream("Scribble.Icons.Material." + variant + ".txt")
    if isNull stream then failwith ("The Material " + variant + " data is missing from the assembly.")
    let data = Array.zeroCreate<byte> (int stream.Length)
    stream.ReadExactly(data, 0, data.Length)
    let index = Dictionary<string, struct (int * int)>(4500, StringComparer.Ordinal)
    let names = ResizeArray<string>(4500)
    let mutable start = 0
    while start < data.Length do
        let finish = match Array.IndexOf(data, 10uy, start) with -1 -> data.Length | i -> i
        let tab = Array.IndexOf(data, 9uy, start, finish - start)
        if tab > start then
            let name = Encoding.ASCII.GetString(data, start, tab - start)
            index[name] <- struct (tab + 1, finish - tab - 1)
            names.Add name
        start <- finish + 1
    { Bytes = data; Index = index; Names = names.ToArray() }

let private outlinedSet = lazy (load "outlined")
let private filledSet = lazy (load "filled")
let private paths = ConcurrentDictionary<struct (string * bool), string>()

/// Google's box: 960 × 960, from y = -960.
let private googleBox (icon: Icon) = Icon.viewBox (0.0, -960.0, 960.0, 960.0) icon

let private tryPath (name: string) (filled: bool) : string voption =
    let set = if filled then filledSet.Value else outlinedSet.Value
    match set.Index.TryGetValue name with
    | true, struct (offset, length) ->
        ValueSome(paths.GetOrAdd(struct (name, filled), fun _ -> Encoding.UTF8.GetString(set.Bytes, offset, length)))
    | _ -> ValueNone

let private make (name: string) (filled: bool) (data: string) =
    Icon.fromPath data [ Icon.name (if filled then name + "-filled" else name); googleBox ]

let private notFound (name: string) =
    ArgumentException(
        "There's no Material icon named '" + name + "'. Names are Google's, lowercase with underscores "
        + "(\"arrow_upward\"); see https://fonts.google.com/icons or Material.names ().")

/// The outlined icon `name` (used by the generated members, whose names are all known).
let named (name: string) : Icon =
    match tryPath name false with
    | ValueSome data -> make name false data
    | ValueNone -> raise (notFound name)

/// "Arrow Upward" / "arrow-upward" / "Arrow_Upward" -> "arrow_upward".
let private normalize (name: string) = name.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_')

type private Spec = { Name: string; Filled: bool; Rotate: float; FlipH: bool; FlipV: bool }

/// "name-option-option": options are read from the end (filled, outlined, fliph, flipv,
/// rotate<degrees>); the first word that isn't one ends them, and the rest is the name. When
/// an option is given twice ("filled-outlined"), the later one wins.
let private parse (text: string) : Spec =
    let words = (if isNull text then "" else text).Trim().Split('-', StringSplitOptions.RemoveEmptyEntries)
    let mutable filled, rotate = ValueNone, ValueNone
    let mutable flipH, flipV = false, false
    let mutable last = words.Length - 1
    let mutable reading = true
    // Keep at least one word for the name.
    while reading && last >= 1 do
        let word = words[last].ToLowerInvariant()
        let mutable degrees = 0.0
        if word = "filled" then (if filled.IsNone then filled <- ValueSome true)
        elif word = "outlined" then (if filled.IsNone then filled <- ValueSome false)
        elif word = "fliph" then flipH <- true
        elif word = "flipv" then flipV <- true
        elif word.StartsWith("rotate", StringComparison.Ordinal)
             && Double.TryParse(word.AsSpan 6, NumberStyles.Float, CultureInfo.InvariantCulture, &degrees) then
            if rotate.IsNone then rotate <- ValueSome degrees
        else reading <- false
        if reading then last <- last - 1
    let name = normalize (String.Join("_", words, 0, max 0 (last + 1)))
    { Name = name
      Filled = ValueOption.defaultValue false filled
      Rotate = ValueOption.defaultValue 0.0 rotate
      FlipH = flipH
      FlipV = flipV }

let private build (spec: Spec) : Icon voption =
    match tryPath spec.Name spec.Filled with
    | ValueSome data ->
        let icon = make spec.Name spec.Filled data |> Icon.rotate spec.Rotate
        let icon = if spec.FlipH then Icon.flipHorizontal icon else icon
        ValueSome(if spec.FlipV then Icon.flipVertical icon else icon)
    | ValueNone -> ValueNone

let tryFromText (text: string) : Icon voption = build (parse text)

let fromText (text: string) : Icon =
    let spec = parse text
    match build spec with
    | ValueSome icon -> icon
    | ValueNone -> raise (notFound spec.Name)

/// The same Material icon, filled or outlined, keeping rotation and flips; any other icon unchanged.
let variant (filled: bool) (icon: Icon) : Icon =
    let name = if icon.Name.EndsWith("-filled", StringComparison.Ordinal) then icon.Name.Substring(0, icon.Name.Length - 7) else icon.Name
    match (if name = "" then ValueNone else tryPath name filled) with
    | ValueSome data when icon.ViewBox = Rect.Create(0.0, -960.0, 960.0, 960.0) ->
        { icon with Name = (if filled then name + "-filled" else name); PathData = data }
    | _ -> icon

let names () : string seq = outlinedSet.Value.Names :> string seq
