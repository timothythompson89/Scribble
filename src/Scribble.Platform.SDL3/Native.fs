/// Hand-written SDL3 declarations, only what Scribble uses, checked against the SDL 3.4.20 headers
/// (build/Sdl3.targets pins the version). Rules as for wgpu: blittable parameters and fields only, C `bool` as
/// `byte`, strings as pinned UTF-8 bytes, so nothing needs runtime marshalling and everything works under NativeAOT.
module internal Scribble.Platform.SDL3.Native

#nowarn "9"

open System
open System.Runtime.InteropServices
open System.Text
open Microsoft.FSharp.NativeInterop

[<Literal>]
let Lib = "SDL3"

// ---------------------------------------------------------------------------------------------
// Events (SDL_events.h). SDL_Event is a 128-byte union; the fields Scribble reads, at their offsets.
// ---------------------------------------------------------------------------------------------

[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_Event =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(8)>] val mutable Timestamp: uint64
    /// Every window, keyboard, text, mouse and user event: the window it's for.
    [<FieldOffset(16)>] val mutable WindowId: uint32
    // SDL_WindowEvent
    [<FieldOffset(20)>] val mutable Data1: int
    [<FieldOffset(24)>] val mutable Data2: int

[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_KeyboardEvent =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(16)>] val mutable WindowId: uint32
    [<FieldOffset(24)>] val mutable Scancode: int
    [<FieldOffset(28)>] val mutable Key: uint32
    [<FieldOffset(32)>] val mutable Mod: uint16
    [<FieldOffset(36)>] val mutable Down: byte
    [<FieldOffset(37)>] val mutable Repeat: byte

[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_TextInputEvent =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(16)>] val mutable WindowId: uint32
    [<FieldOffset(24)>] val mutable Text: nativeint

/// SDL_TextEditingEvent: what an input method is composing. `Start` is its caret in the text, in characters
/// (-1: not said).
[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_TextEditingEvent =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(16)>] val mutable WindowId: uint32
    [<FieldOffset(24)>] val mutable Text: nativeint
    [<FieldOffset(32)>] val mutable Start: int
    [<FieldOffset(36)>] val mutable Length: int

/// SDL_Rect: whole window coordinates.
[<Struct; StructLayout(LayoutKind.Sequential)>]
type SDL_Rect =
    val mutable X: int
    val mutable Y: int
    val mutable W: int
    val mutable H: int

[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_MouseMotionEvent =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(16)>] val mutable WindowId: uint32
    [<FieldOffset(20)>] val mutable Which: uint32
    [<FieldOffset(24)>] val mutable State: uint32
    [<FieldOffset(28)>] val mutable X: float32
    [<FieldOffset(32)>] val mutable Y: float32

[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_MouseButtonEvent =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(16)>] val mutable WindowId: uint32
    [<FieldOffset(20)>] val mutable Which: uint32
    [<FieldOffset(24)>] val mutable Button: byte
    [<FieldOffset(25)>] val mutable Down: byte
    [<FieldOffset(26)>] val mutable Clicks: byte
    [<FieldOffset(28)>] val mutable X: float32
    [<FieldOffset(32)>] val mutable Y: float32

[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_MouseWheelEvent =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(16)>] val mutable WindowId: uint32
    [<FieldOffset(20)>] val mutable Which: uint32
    [<FieldOffset(24)>] val mutable X: float32
    [<FieldOffset(28)>] val mutable Y: float32
    [<FieldOffset(32)>] val mutable Direction: int
    [<FieldOffset(36)>] val mutable MouseX: float32
    [<FieldOffset(40)>] val mutable MouseY: float32

[<Struct; StructLayout(LayoutKind.Explicit, Size = 128)>]
type SDL_UserEvent =
    [<FieldOffset(0)>] val mutable Type: uint32
    [<FieldOffset(16)>] val mutable WindowId: uint32
    [<FieldOffset(20)>] val mutable Code: int

// SDL_DisplayMode (SDL_video.h): displayID, format, w, h, pixel_density, refresh_rate, ...
let DisplayModeRefreshRateOffset = 20

let SDL_EVENT_QUIT = 0x100u
let SDL_EVENT_WINDOW_SHOWN = 0x202u
let SDL_EVENT_WINDOW_EXPOSED = 0x204u
let SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED = 0x207u
let SDL_EVENT_WINDOW_MINIMIZED = 0x209u
let SDL_EVENT_WINDOW_RESTORED = 0x20Bu
let SDL_EVENT_WINDOW_MOUSE_ENTER = 0x20Cu
let SDL_EVENT_WINDOW_MOUSE_LEAVE = 0x20Du
let SDL_EVENT_WINDOW_FOCUS_GAINED = 0x20Eu
let SDL_EVENT_WINDOW_FOCUS_LOST = 0x20Fu
let SDL_EVENT_WINDOW_CLOSE_REQUESTED = 0x210u
let SDL_EVENT_WINDOW_DISPLAY_SCALE_CHANGED = 0x214u
let SDL_EVENT_WINDOW_DESTROYED = 0x219u
let SDL_EVENT_WINDOW_FIRST = 0x202u
let SDL_EVENT_WINDOW_LAST = 0x21Au
let SDL_EVENT_KEY_DOWN = 0x300u
let SDL_EVENT_KEY_UP = 0x301u
let SDL_EVENT_TEXT_EDITING = 0x302u
let SDL_EVENT_TEXT_INPUT = 0x303u
let SDL_EVENT_MOUSE_MOTION = 0x400u
let SDL_EVENT_MOUSE_BUTTON_DOWN = 0x401u
let SDL_EVENT_MOUSE_BUTTON_UP = 0x402u
let SDL_EVENT_MOUSE_WHEEL = 0x403u

let SDL_INIT_VIDEO = 0x20u

let SDL_WINDOW_HIDDEN = 0x8UL
let SDL_WINDOW_RESIZABLE = 0x20UL
let SDL_WINDOW_HIGH_PIXEL_DENSITY = 0x2000UL

let SDL_KMOD_SHIFT = 0x0003us
let SDL_KMOD_CTRL = 0x00C0us
let SDL_KMOD_ALT = 0x0300us
let SDL_KMOD_GUI = 0x0C00us

/// Keycodes for keys without a character are their scancode with this bit set (SDL_keycode.h).
let SDLK_SCANCODE_MASK = 0x40000000u

/// Mouse events made from touch or pen (SDL_touch.h, SDL_pen.h).
let SDL_TOUCH_MOUSEID = 0xFFFFFFFFu
let SDL_PEN_MOUSEID = 0xFFFFFFFEu

let SDL_BUTTON_LEFT = 1uy
let SDL_BUTTON_MIDDLE = 2uy
let SDL_BUTTON_RIGHT = 3uy
let SDL_BUTTON_X1 = 4uy

// SDL_SystemCursor
let SDL_SYSTEM_CURSOR_DEFAULT = 0
let SDL_SYSTEM_CURSOR_TEXT = 1
let SDL_SYSTEM_CURSOR_WAIT = 2
let SDL_SYSTEM_CURSOR_CROSSHAIR = 3
let SDL_SYSTEM_CURSOR_NWSE_RESIZE = 5
let SDL_SYSTEM_CURSOR_EW_RESIZE = 7
let SDL_SYSTEM_CURSOR_NS_RESIZE = 8
let SDL_SYSTEM_CURSOR_MOVE = 9
let SDL_SYSTEM_CURSOR_NOT_ALLOWED = 10
let SDL_SYSTEM_CURSOR_POINTER = 11

/// B, G, R, A in memory on little-endian machines: our cursor pixels' order.
let SDL_PIXELFORMAT_ARGB8888 = 0x16362004u

/// typedef void (SDLCALL *SDL_DialogFileCallback)(void *userdata, const char * const *filelist, int filter);
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type DialogFileCallback = delegate of nativeint * nativeint * int -> unit

/// typedef struct SDL_DialogFileFilter { const char *name; const char *pattern; } (SDL_dialog.h)
[<Struct; StructLayout(LayoutKind.Sequential)>]
type SDL_DialogFileFilter =
    val mutable Name: nativeint
    val mutable Pattern: nativeint

/// typedef bool (SDLCALL *SDL_EventFilter)(void *userdata, SDL_Event *event);
[<UnmanagedFunctionPointer(CallingConvention.Cdecl)>]
type EventFilter = delegate of nativeint * nativeint -> byte

// ---------------------------------------------------------------------------------------------
// Functions
// ---------------------------------------------------------------------------------------------

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_Init(uint32 flags)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_Quit()

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_GetError()

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetHint(byte* name, byte* value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_CreateWindow(byte* title, int w, int h, uint64 flags)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_DestroyWindow(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_ShowWindow(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetWindowTitle(nativeint window, byte* title)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetWindowSize(nativeint window, int w, int h)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_GetWindowSizeInPixels(nativeint window, int* w, int* h)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern float32 SDL_GetWindowDisplayScale(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern float32 SDL_GetWindowPixelDensity(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint32 SDL_GetWindowID(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint64 SDL_GetWindowFlags(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint32 SDL_GetWindowProperties(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_GetPointerProperty(uint32 props, byte* name, nativeint defaultValue)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetWindowParent(nativeint window, nativeint parent)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint32 SDL_GetDisplayForWindow(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_GetCurrentDisplayMode(uint32 displayId)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint32 SDL_GetPrimaryDisplay()

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern float32 SDL_GetDisplayContentScale(uint32 displayId)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_SetEventFilter(nativeint filter, nativeint userdata)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_PollEvent(SDL_Event* event)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_WaitEventTimeout(SDL_Event* event, int timeoutMs)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_PushEvent(SDL_Event* event)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint32 SDL_RegisterEvents(int count)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint16 SDL_GetModState()

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_GetKeyName(uint32 key)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_StartTextInput(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_CreateSystemCursor(int id)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_CreateColorCursor(nativeint surface, int hotX, int hotY)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetCursor(nativeint cursor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_DestroyCursor(nativeint cursor)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_CreateSurfaceFrom(int width, int height, uint32 format, nativeint pixels, int pitch)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_DestroySurface(nativeint surface)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_GetMouseFocus()

/// SDL_dialog.h. The filters and default location must stay valid until the callback runs; on Windows the dialog
/// runs on its own thread and the callback comes from there.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_ShowSaveFileDialog(nativeint callback, nativeint userdata, nativeint window, SDL_DialogFileFilter* filters, int nfilters, nativeint defaultLocation)

/// SDL_keyboard.h: text input (typed text and input methods) for a window. While it is on, SDL sends
/// SDL_EVENT_TEXT_INPUT and SDL_EVENT_TEXT_EDITING, and an input method shows its candidates beside the area.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_StartTextInputWithProperties(nativeint window, uint32 props)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_StopTextInput(nativeint window)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_ClearComposition(nativeint window)

/// `rect`: where text is being typed, in window coordinates; `cursor`: the caret's offset from its left edge.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetTextInputArea(nativeint window, SDL_Rect* rect, int cursor)

/// SDL_properties.h.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern uint32 SDL_CreateProperties()

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_DestroyProperties(uint32 props)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetNumberProperty(uint32 props, byte* name, int64 value)

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetBooleanProperty(uint32 props, byte* name, byte value)

/// SDL_clipboard.h. The text is UTF-8.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern byte SDL_SetClipboardText(byte* text)

/// The clipboard's text, or an empty string: ours to free with SDL_free.
[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern nativeint SDL_GetClipboardText()

[<DllImport(Lib, CallingConvention = CallingConvention.Cdecl)>]
extern void SDL_free(nativeint memory)

// ---------------------------------------------------------------------------------------------
// Strings: UTF-8, NUL-terminated, pinned for the call.
// ---------------------------------------------------------------------------------------------

/// Calls `f` with `s` as a NUL-terminated UTF-8 string (a pinned array: `fixed` on arrays is safe; the
/// string rule in CLAUDE.md is about `fixed` on .NET strings).
let withUtf8 (s: string) (f: nativeptr<byte> -> 'T) : 'T =
    let bytes = Encoding.UTF8.GetBytes(s + "\000")
    use p = fixed bytes
    f p

/// A UTF-8 string SDL owns, read into .NET (empty for a null pointer).
let readUtf8 (p: nativeint) = if p = 0n then "" else Marshal.PtrToStringUTF8 p

let lastError () = readUtf8 (SDL_GetError())

let setHint (name: string) (value: string) =
    withUtf8 name (fun n -> withUtf8 value (fun v -> SDL_SetHint(n, v) |> ignore))

let pointerProperty (props: uint32) (name: string) =
    withUtf8 name (fun n -> SDL_GetPointerProperty(props, n, 0n))

/// The refresh rate of a display in Hz (0 when unknown).
let refreshRate (display: uint32) =
    let mode = SDL_GetCurrentDisplayMode display
    if mode = 0n then 0.0
    else float (NativePtr.read (NativePtr.ofNativeInt<float32> (mode + nativeint DisplayModeRefreshRateOffset)))
