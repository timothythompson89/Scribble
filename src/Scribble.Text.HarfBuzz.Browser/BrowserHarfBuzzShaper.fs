namespace Scribble.Text.HarfBuzz.Browser

open System
open System.Collections.Generic
open System.IO
open System.Threading.Tasks
open Scribble.Platform.Browser.Js
open Scribble.Text

module internal ShaperJs =
    [<Literal>]
    let ModuleName = "scribble-harfbuzz"

    let stageWasm = JsFunction(ModuleName, "stageWasm", T.void', [ T.bytes ])
    let init = JsFunction(ModuleName, "init", T.task, [])
    let loadFont = JsFunction(ModuleName, "loadFont", T.void', [ T.double; T.bytes ])
    let shape = JsFunction(ModuleName, "shape", T.doubles, [ T.double; T.string ])

/// Text shaping in the browser with HarfBuzz compiled to WebAssembly. Gives the same results as
/// the native HarfBuzzShaper (same HarfBuzz, same font units). Create with `CreateAsync`.
[<Sealed>]
type BrowserHarfBuzzShaper private () =
    let loaded = HashSet<int>()

    /// Loads the JavaScript module and the HarfBuzz WebAssembly module.
    static member CreateAsync() : Task<BrowserHarfBuzzShaper> =
        task {
            let assembly = typeof<BrowserHarfBuzzShaper>.Assembly
            do! importEmbeddedModule ShaperJs.ModuleName assembly "scribble-harfbuzz.js"
            let wasm =
                use stream = assembly.GetManifestResourceStream "harfbuzz.wasm"
                use memory = new MemoryStream()
                stream.CopyTo memory
                memory.ToArray()
            let stage = ShaperJs.stageWasm.Args()
            stage[2].ToJS(Span<byte> wasm)
            ShaperJs.stageWasm.Invoke stage
            let init = ShaperJs.init.Args()
            ShaperJs.init.Invoke init
            do! returnedTask init
            return new BrowserHarfBuzzShaper()
        }

    interface IShaper with
        member _.Name = "HarfBuzz (WebAssembly)"

        member _.Shape(font, text) =
            if String.IsNullOrEmpty text then
                [||]
            else
                if loaded.Add font.Id then
                    let args = ShaperJs.loadFont.Args()
                    args[2].ToJS(float font.Id)
                    args[3].ToJS(Span<byte> font.Data)
                    ShaperJs.loadFont.Invoke args
                let args = ShaperJs.shape.Args()
                args[2].ToJS(float font.Id)
                args[3].ToJS text
                ShaperJs.shape.Invoke args
                let r = returnedDoubles args
                Array.init (r.Length / 6) (fun i ->
                    { Glyph = int r[i * 6]
                      Cluster = int r[i * 6 + 1]
                      XAdvance = int r[i * 6 + 2]
                      YAdvance = int r[i * 6 + 3]
                      XOffset = int r[i * 6 + 4]
                      YOffset = int r[i * 6 + 5] })

        // The page owns the WebAssembly instance; nothing to free per shaper.
        member _.Dispose() = ()
