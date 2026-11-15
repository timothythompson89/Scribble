/// Shader sources shared by every WebGPU backend.
module Scribble.Rendering.Shaders

open System.IO

let private load (name: string) =
    use stream = typeof<DrawList>.Assembly.GetManifestResourceStream name
    use reader = new StreamReader(stream)
    reader.ReadToEnd()

/// WGSL for rounded rects, borders and shadows. Entry points: vs_main, fs_main.
/// Bindings: group 0, binding 0 = Globals uniform (16 bytes), binding 1 = Shape storage array.
let shapesWgsl = lazy (load "shapes.wgsl")

/// Size of the Globals uniform in bytes.
[<Literal>]
let GlobalsSize = 16
