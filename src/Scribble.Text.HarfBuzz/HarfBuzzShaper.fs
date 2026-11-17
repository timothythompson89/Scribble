namespace Scribble.Text.HarfBuzz

#nowarn "9"

open System
open System.Collections.Generic
open System.Reflection
open System.Runtime.InteropServices
open Microsoft.FSharp.NativeInterop
open Scribble.Text

/// The handful of HarfBuzz C functions Scribble needs. Blittable signatures only (NativeAOT-safe).
module internal Native =
    [<Literal>]
    let Lib = "harfbuzz"

    /// hb_glyph_info_t
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type GlyphInfo =
        { Codepoint: uint32
          Mask: uint32
          Cluster: uint32
          Var1: uint32
          Var2: uint32 }

    /// hb_glyph_position_t
    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type GlyphPosition =
        { XAdvance: int
          YAdvance: int
          XOffset: int
          YOffset: int
          Var: uint32 }

    [<Literal>]
    let MemoryModeReadOnly = 1

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint hb_blob_create(nativeint data, uint32 length, int mode, nativeint userData, nativeint destroy)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_blob_destroy(nativeint blob)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint hb_face_create(nativeint blob, uint32 index)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_face_destroy(nativeint face)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern uint32 hb_face_get_upem(nativeint face)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint hb_font_create(nativeint face)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_font_set_scale(nativeint font, int xScale, int yScale)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_font_destroy(nativeint font)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern nativeint hb_buffer_create()

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_buffer_clear_contents(nativeint buffer)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_buffer_destroy(nativeint buffer)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_buffer_add_utf16(nativeint buffer, nativeint text, int textLength, uint32 itemOffset, int itemLength)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_buffer_guess_segment_properties(nativeint buffer)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern void hb_shape(nativeint font, nativeint buffer, nativeint features, uint32 featureCount)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern GlyphInfo* hb_buffer_get_glyph_infos(nativeint buffer, uint32* length)

    [<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
    extern GlyphPosition* hb_buffer_get_glyph_positions(nativeint buffer, uint32* length)

    /// HarfBuzz ships under different file names: libHarfBuzzSharp.dll (SkiaSharp's Windows build),
    /// harfbuzz.dll, libharfbuzz.so.0 (Linux distributions), libharfbuzz.dylib (Homebrew).
    let private candidates =
        [ "libHarfBuzzSharp"; "harfbuzz"; "libharfbuzz"; "libharfbuzz.so.0"; "libharfbuzz-0" ]

    let private resolver =
        DllImportResolver(fun name assembly searchPath ->
            if name <> Lib then
                0n
            else
                candidates
                |> List.tryPick (fun candidate ->
                    match NativeLibrary.TryLoad(candidate, assembly, searchPath) with
                    | true, handle -> Some handle
                    | _ -> None)
                |> Option.defaultValue 0n)

    let private registered = lazy (NativeLibrary.SetDllImportResolver(typeof<GlyphInfo>.Assembly, resolver))

    /// Must run before the first HarfBuzz call.
    let ensureResolver () = registered.Force()

/// Text shaping with native HarfBuzz.
[<Sealed>]
type HarfBuzzShaper() =
    do Native.ensureResolver ()

    let buffer = Native.hb_buffer_create ()
    /// Per font: pinned copy of the file, blob, face, font. Fonts live as long as the shaper.
    let fonts = Dictionary<int, struct (GCHandle * nativeint * nativeint * nativeint)>()
    let mutable disposed = false

    let hbFont (font: Font) =
        match fonts.TryGetValue font.Id with
        | true, struct (_, _, _, handle) -> handle
        | _ ->
            let pin = GCHandle.Alloc(font.Data, GCHandleType.Pinned)
            let blob = Native.hb_blob_create(pin.AddrOfPinnedObject(), uint32 font.Data.Length, Native.MemoryModeReadOnly, 0n, 0n)
            let face = Native.hb_face_create(blob, 0u)
            let hb = Native.hb_font_create face
            // Positions come back in font units, so one shaping result serves every size.
            let upem = int (Native.hb_face_get_upem face)
            Native.hb_font_set_scale(hb, upem, upem)
            fonts[font.Id] <- struct (pin, blob, face, hb)
            hb

    interface IShaper with
        member _.Name = "HarfBuzz (native)"

        member _.Shape(font, text) =
            if disposed then raise (ObjectDisposedException(nameof HarfBuzzShaper))
            if String.IsNullOrEmpty text then
                [||]
            else
                let hb = hbFont font
                Native.hb_buffer_clear_contents buffer
                // Pinned with a handle, not `fixed`: a background GC (2026-11-16, .NET 10) read the
                // `fixed` local (a pinned char& into the string) as an object start and crashed the
                // runtime ("Internal CLR error") while HarfBuzz ran. HarfBuzz copies the text here.
                let pin = GCHandle.Alloc(text, GCHandleType.Pinned)
                try Native.hb_buffer_add_utf16(buffer, pin.AddrOfPinnedObject(), text.Length, 0u, text.Length)
                finally pin.Free()
                Native.hb_buffer_guess_segment_properties buffer
                Native.hb_shape(hb, buffer, 0n, 0u)
                let mutable count = 0u
                let infos = Native.hb_buffer_get_glyph_infos(buffer, &&count)
                let positions = Native.hb_buffer_get_glyph_positions(buffer, &&count)
                Array.init (int count) (fun i ->
                    let info = NativePtr.get infos i
                    let pos = NativePtr.get positions i
                    { Glyph = int info.Codepoint
                      Cluster = int info.Cluster
                      XAdvance = pos.XAdvance
                      YAdvance = pos.YAdvance
                      XOffset = pos.XOffset
                      YOffset = pos.YOffset })

        member _.Dispose() =
            if not disposed then
                disposed <- true
                Native.hb_buffer_destroy buffer
                for struct (pin, blob, face, hb) in fonts.Values do
                    Native.hb_font_destroy hb
                    Native.hb_face_destroy face
                    Native.hb_blob_destroy blob
                    pin.Free()
