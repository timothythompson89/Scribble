/// Hand-written Win32 declarations. Rules (see the design doc's engineering rules):
/// only blittable parameter and field types, pointers instead of `ref`/`string`, so nothing
/// needs runtime marshalling and everything works under NativeAOT.
module internal Scribble.Platform.Win32.Native

#nowarn "9"

open System.Runtime.InteropServices

// ---------------------------------------------------------------------------------------------
// Structs
// ---------------------------------------------------------------------------------------------

[<Struct; StructLayout(LayoutKind.Sequential)>]
type POINT = { mutable X: int; mutable Y: int }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type RECT =
    { mutable Left: int
      mutable Top: int
      mutable Right: int
      mutable Bottom: int }

    member r.Width = r.Right - r.Left
    member r.Height = r.Bottom - r.Top

[<Struct; StructLayout(LayoutKind.Sequential)>]
type MSG =
    { mutable hwnd: nativeint
      mutable message: uint32
      mutable wParam: nativeint
      mutable lParam: nativeint
      mutable time: uint32
      mutable pt: POINT
      mutable lPrivate: uint32 }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type WNDCLASSEXW =
    { mutable cbSize: uint32
      mutable style: uint32
      mutable lpfnWndProc: nativeint
      mutable cbClsExtra: int
      mutable cbWndExtra: int
      mutable hInstance: nativeint
      mutable hIcon: nativeint
      mutable hCursor: nativeint
      mutable hbrBackground: nativeint
      mutable lpszMenuName: nativeint
      mutable lpszClassName: nativeint
      mutable hIconSm: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type TRACKMOUSEEVENT =
    { mutable cbSize: uint32
      mutable dwFlags: uint32
      mutable hwndTrack: nativeint
      mutable dwHoverTime: uint32 }

/// LRESULT CALLBACK WindowProc(HWND, UINT, WPARAM, LPARAM)
[<UnmanagedFunctionPointer(CallingConvention.Winapi)>]
type WndProc = delegate of hwnd: nativeint * msg: uint32 * wParam: nativeint * lParam: nativeint -> nativeint

// ---------------------------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------------------------

[<Literal>]
let WM_DESTROY = 0x0002u
[<Literal>]
let WM_SIZE = 0x0005u
[<Literal>]
let WM_SETFOCUS = 0x0007u
[<Literal>]
let WM_KILLFOCUS = 0x0008u
[<Literal>]
let WM_CLOSE = 0x0010u
[<Literal>]
let WM_QUIT = 0x0012u
[<Literal>]
let WM_ERASEBKGND = 0x0014u
[<Literal>]
let WM_KEYDOWN = 0x0100u
[<Literal>]
let WM_KEYUP = 0x0101u
[<Literal>]
let WM_CHAR = 0x0102u
[<Literal>]
let WM_SYSKEYDOWN = 0x0104u
[<Literal>]
let WM_SYSKEYUP = 0x0105u
[<Literal>]
let WM_MOUSEMOVE = 0x0200u
[<Literal>]
let WM_LBUTTONDOWN = 0x0201u
[<Literal>]
let WM_LBUTTONUP = 0x0202u
[<Literal>]
let WM_RBUTTONDOWN = 0x0204u
[<Literal>]
let WM_RBUTTONUP = 0x0205u
[<Literal>]
let WM_MBUTTONDOWN = 0x0207u
[<Literal>]
let WM_MBUTTONUP = 0x0208u
[<Literal>]
let WM_MOUSEWHEEL = 0x020Au
[<Literal>]
let WM_XBUTTONDOWN = 0x020Bu
[<Literal>]
let WM_XBUTTONUP = 0x020Cu
[<Literal>]
let WM_MOUSEHWHEEL = 0x020Eu
[<Literal>]
let WM_CAPTURECHANGED = 0x0215u
[<Literal>]
let WM_MOUSELEAVE = 0x02A3u
[<Literal>]
let WM_SETCURSOR = 0x0020u
[<Literal>]
let HTCLIENT = 1
[<Literal>]
let TME_LEAVE = 0x00000002u
[<Literal>]
let VK_SHIFT = 0x10
[<Literal>]
let VK_CONTROL = 0x11
[<Literal>]
let VK_MENU = 0x12
[<Literal>]
let VK_LWIN = 0x5B
[<Literal>]
let VK_RWIN = 0x5C
let IDC_IBEAM = 32513n
let IDC_WAIT = 32514n
let IDC_CROSS = 32515n
let IDC_SIZEWE = 32644n
let IDC_SIZENS = 32645n
let IDC_SIZEALL = 32646n
let IDC_SIZENWSE = 32642n
let IDC_NO = 32648n
let IDC_HAND = 32649n
[<Literal>]
let WM_DPICHANGED = 0x02E0u
/// WM_APP + 1: Scribble's "wake up and drain the dispatcher" message.
[<Literal>]
let WM_SCRIBBLE_WAKE = 0x8001u
/// Parent for message-only windows.
let HWND_MESSAGE = -3n
[<Literal>]
let WM_PAINT = 0x000Fu
[<Literal>]
let WM_TIMER = 0x0113u
[<Literal>]
let WM_ENTERSIZEMOVE = 0x0231u
[<Literal>]
let WM_EXITSIZEMOVE = 0x0232u
[<Literal>]
let BLACK_BRUSH = 4

[<Literal>]
let WS_OVERLAPPEDWINDOW = 0x00CF0000u
[<Literal>]
let WS_THICKFRAME = 0x00040000u
[<Literal>]
let WS_MAXIMIZEBOX = 0x00010000u
[<Literal>]
let CW_USEDEFAULT = 0x80000000
[<Literal>]
let SW_SHOW = 5
[<Literal>]
let PM_REMOVE = 1u
[<Literal>]
let SWP_NOZORDER = 0x0004u
[<Literal>]
let SWP_NOACTIVATE = 0x0010u
[<Literal>]
let SWP_NOMOVE = 0x0002u
let IDC_ARROW = 32512n
[<Literal>]
let USER_DEFAULT_SCREEN_DPI = 96u
[<Literal>]
let WHEEL_DELTA = 120.0

/// DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
let DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4n

// ---------------------------------------------------------------------------------------------
// Functions
// ---------------------------------------------------------------------------------------------

[<DllImport("kernel32.dll", ExactSpelling = true)>]
extern nativeint GetModuleHandleW(nativeint lpModuleName)

[<DllImport("user32.dll", ExactSpelling = true, SetLastError = true)>]
extern uint16 RegisterClassExW(WNDCLASSEXW* lpwcx)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int UnregisterClassW(nativeint lpClassName, nativeint hInstance)

[<DllImport("user32.dll", ExactSpelling = true, SetLastError = true)>]
extern nativeint CreateWindowExW(
    uint32 dwExStyle,
    nativeint lpClassName,
    nativeint lpWindowName,
    uint32 dwStyle,
    int x,
    int y,
    int nWidth,
    int nHeight,
    nativeint hWndParent,
    nativeint hMenu,
    nativeint hInstance,
    nativeint lpParam
)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int DestroyWindow(nativeint hwnd)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int ShowWindow(nativeint hwnd, int nCmdShow)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint DefWindowProcW(nativeint hwnd, uint32 msg, nativeint wParam, nativeint lParam)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int PeekMessageW(MSG* lpMsg, nativeint hwnd, uint32 wMsgFilterMin, uint32 wMsgFilterMax, uint32 wRemoveMsg)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int TranslateMessage(MSG* lpMsg)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint DispatchMessageW(MSG* lpMsg)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int WaitMessage()

/// Safe from any thread.
[<DllImport("user32.dll", ExactSpelling = true)>]
extern int PostMessageW(nativeint hwnd, uint32 msg, nativeint wParam, nativeint lParam)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int GetClientRect(nativeint hwnd, RECT* lpRect)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int SetWindowPos(nativeint hwnd, nativeint hwndInsertAfter, int x, int y, int cx, int cy, uint32 uFlags)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int SetWindowTextW(nativeint hwnd, nativeint lpString)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint LoadCursorW(nativeint hInstance, nativeint lpCursorName)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint SetCapture(nativeint hwnd)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int ReleaseCapture()

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int ScreenToClient(nativeint hwnd, POINT* lpPoint)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint GetCapture()

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int TrackMouseEvent(TRACKMOUSEEVENT* lpEventTrack)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int16 GetKeyState(int nVirtKey)

/// uMapType 2 = MAPVK_VK_TO_CHAR: the unshifted character a virtual key types in the active layout
/// (uppercase for letters; the top bit set for dead keys; 0 for keys that type nothing).
[<DllImport("user32.dll", ExactSpelling = true)>]
extern uint32 MapVirtualKeyW(uint32 uCode, uint32 uMapType)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint SetCursor(nativeint hCursor)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern uint32 GetDpiForWindow(nativeint hwnd)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern uint32 GetDpiForSystem()

[<DllImport("user32.dll", ExactSpelling = true, SetLastError = true)>]
extern int SetProcessDpiAwarenessContext(nativeint value)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int AdjustWindowRectExForDpi(RECT* lpRect, uint32 dwStyle, int bMenu, uint32 dwExStyle, uint32 dpi)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int ValidateRect(nativeint hwnd, nativeint lpRect)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint SetTimer(nativeint hwnd, nativeint nIDEvent, uint32 uElapse, nativeint lpTimerFunc)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int KillTimer(nativeint hwnd, nativeint uIDEvent)

[<DllImport("gdi32.dll", ExactSpelling = true)>]
extern nativeint GetStockObject(int i)

// Image cursors: a 32-bit top-down DIB with straight alpha plus an all-zero mask, made into a
// cursor by CreateIconIndirect (and freed with DestroyIcon).

[<Struct; StructLayout(LayoutKind.Sequential)>]
type ICONINFO =
    { mutable fIcon: int
      mutable xHotspot: uint32
      mutable yHotspot: uint32
      mutable hbmMask: nativeint
      mutable hbmColor: nativeint }

[<Struct; StructLayout(LayoutKind.Sequential)>]
type BITMAPINFOHEADER =
    { mutable biSize: uint32
      mutable biWidth: int
      mutable biHeight: int
      mutable biPlanes: uint16
      mutable biBitCount: uint16
      mutable biCompression: uint32
      mutable biSizeImage: uint32
      mutable biXPelsPerMeter: int
      mutable biYPelsPerMeter: int
      mutable biClrUsed: uint32
      mutable biClrImportant: uint32 }

let BI_RGB = 0u
let DIB_RGB_COLORS = 0u

[<DllImport("gdi32.dll", ExactSpelling = true)>]
extern nativeint CreateDIBSection(nativeint hdc, BITMAPINFOHEADER* pbmi, uint32 usage, nativeint* ppvBits, nativeint hSection, uint32 offset)

[<DllImport("gdi32.dll", ExactSpelling = true)>]
extern nativeint CreateBitmap(int nWidth, int nHeight, uint32 nPlanes, uint32 nBitCount, nativeint lpBits)

[<DllImport("gdi32.dll", ExactSpelling = true)>]
extern int DeleteObject(nativeint ho)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern nativeint CreateIconIndirect(ICONINFO* piconinfo)

[<DllImport("user32.dll", ExactSpelling = true)>]
extern int DestroyIcon(nativeint hIcon)

// ---------------------------------------------------------------------------------------------
// Message parameter helpers (the C macros)
// ---------------------------------------------------------------------------------------------

let inline loWord (v: nativeint) = int (uint16 (int64 v &&& 0xFFFFL))
let inline hiWord (v: nativeint) = int (uint16 ((int64 v >>> 16) &&& 0xFFFFL))
/// GET_X_LPARAM: signed, because coordinates can be negative on multi-monitor setups and during capture.
let inline signedLoWord (v: nativeint) = int (int16 (int64 v &&& 0xFFFFL))
let inline signedHiWord (v: nativeint) = int (int16 ((int64 v >>> 16) &&& 0xFFFFL))
