// Decodes PngSuite with WIC (Windows Imaging Component) and writes what WIC saw beside each file, as the
// reference Scribble.Imaging's PNG decoder is checked against (DESIGN.md, "M8 step 3b", (b) decisions):
//
//   dotnet fsi tools/MakePngReferences.fsx
//
// Windows only, test-only: run once, and the .wic files are checked in. For each tests/Scribble.Tests/PngSuite/*.png
// that isn't one of the corrupt x*.png files it writes <name>.wic: zlib-compressed, u32 width, u32 height (little
// endian), then the pixels as 16-bit straight (not premultiplied) RGBA, little endian, row by row. 16 bits keep
// WIC's own 16-to-8 rounding out of the reference: the test rounds, as the decoder does.
//
// WIC is called through its COM vtables directly (F# Interactive's dynamic assemblies can't host ComImport
// interfaces reliably). Slot numbers count IUnknown's three methods first.

open System
open System.IO
open System.IO.Compression
open System.Runtime.InteropServices

[<DllImport("ole32.dll")>]
extern int CoInitializeEx(nativeint reserved, uint32 coInit)

[<DllImport("ole32.dll")>]
extern int CoCreateInstance(Guid& clsid, nativeint outer, uint32 context, Guid& iid, nativeint& result)

[<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
type ReleaseFn = delegate of nativeint -> uint32

[<UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)>]
type CreateDecoderFromFilenameFn =
    delegate of nativeint * [<MarshalAs(UnmanagedType.LPWStr)>] fileName: string * vendor: nativeint * access: uint32 * options: int * decoder: nativeint byref -> int

[<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
type CreateFormatConverterFn = delegate of nativeint * converter: nativeint byref -> int

[<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
type GetFrameFn = delegate of nativeint * index: uint32 * frame: nativeint byref -> int

[<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
type GetSizeFn = delegate of nativeint * width: uint32 byref * height: uint32 byref -> int

[<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
type GetPixelFormatFn = delegate of nativeint * format: Guid byref -> int

[<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
type CopyPixelsFn = delegate of nativeint * rect: nativeint * stride: uint32 * size: uint32 * buffer: nativeint -> int

[<UnmanagedFunctionPointer(CallingConvention.StdCall)>]
type ConverterInitializeFn =
    delegate of nativeint * source: nativeint * format: Guid byref * dither: int * palette: nativeint * alphaThreshold: float * paletteType: int -> int

let slot<'D when 'D :> Delegate> (obj: nativeint) (index: int) : 'D =
    let vtable = Marshal.ReadIntPtr obj
    Marshal.GetDelegateForFunctionPointer<'D>(Marshal.ReadIntPtr(vtable, index * IntPtr.Size))

let check (what: string) (hr: int) =
    if hr < 0 then failwith (what + " failed: 0x" + hr.ToString("X8"))

let release (obj: nativeint) =
    if obj <> 0n then (slot<ReleaseFn> obj 2).Invoke obj |> ignore

let format64bppRGBA = Guid("6fddc324-4e03-4bfe-b185-3d77768dc916")

CoInitializeEx(0n, 2u (* COINIT_APARTMENTTHREADED *)) |> ignore

let factory =
    let mutable clsid = Guid("cacaf262-9370-4615-a13b-9f5539da4c0a") // CLSID_WICImagingFactory
    let mutable iid = Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70") // IID_IWICImagingFactory
    let mutable result = 0n
    CoCreateInstance(&clsid, 0n, 1u (* CLSCTX_INPROC_SERVER *), &iid, &result) |> check "CoCreateInstance"
    result

/// The image's pixels as WIC decodes them, 16-bit straight RGBA; or an error message.
let decode (path: string) : Result<int * int * byte[], string> =
    let mutable decoder = 0n
    let mutable frame = 0n
    let mutable converter = 0n
    try
        try
            (slot<CreateDecoderFromFilenameFn> factory 3).Invoke(factory, path, 0n, 0x80000000u, 0, &decoder)
            |> check "CreateDecoderFromFilename"
            (slot<GetFrameFn> decoder 13).Invoke(decoder, 0u, &frame) |> check "GetFrame"
            (slot<CreateFormatConverterFn> factory 10).Invoke(factory, &converter) |> check "CreateFormatConverter"
            let mutable target = format64bppRGBA
            (slot<ConverterInitializeFn> converter 8).Invoke(converter, frame, &target, 0, 0n, 0.0, 0)
            |> check "IWICFormatConverter.Initialize"
            let mutable actual = Guid.Empty
            (slot<GetPixelFormatFn> converter 4).Invoke(converter, &actual) |> check "GetPixelFormat"
            if actual <> format64bppRGBA then failwith ("converted to " + string actual)
            let mutable w = 0u
            let mutable h = 0u
            (slot<GetSizeFn> converter 3).Invoke(converter, &w, &h) |> check "GetSize"
            let stride = int w * 8
            let pixels = Array.zeroCreate<byte> (stride * int h)
            let pin = GCHandle.Alloc(pixels, GCHandleType.Pinned)
            try
                (slot<CopyPixelsFn> converter 7).Invoke(converter, 0n, uint32 stride, uint32 pixels.Length, pin.AddrOfPinnedObject())
                |> check "CopyPixels"
            finally
                pin.Free()
            Ok(int w, int h, pixels)
        with e ->
            Error e.Message
    finally
        release converter
        release frame
        release decoder

let folder = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "tests", "Scribble.Tests", "PngSuite"))

for path in Directory.GetFiles(folder, "*.png") |> Array.sort do
    let name = Path.GetFileNameWithoutExtension path
    match decode path with
    | Ok(w, h, pixels) when not (name.StartsWith "x") ->
        use file = File.Create(Path.ChangeExtension(path, ".wic"))
        use z = new ZLibStream(file, CompressionLevel.SmallestSize)
        z.Write(BitConverter.GetBytes(uint32 w), 0, 4)
        z.Write(BitConverter.GetBytes(uint32 h), 0, 4)
        z.Write(pixels, 0, pixels.Length)
    | Ok _ -> Console.WriteLine(name + ": corrupt, but WIC decoded it (no reference written)")
    | Error message when name.StartsWith "x" -> Console.WriteLine(name + ": corrupt, WIC refused it: " + message)
    | Error message -> Console.WriteLine(name + ": WIC FAILED: " + message)

release factory
Console.WriteLine("Done: " + folder)
