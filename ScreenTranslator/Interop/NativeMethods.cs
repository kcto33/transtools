using System.Runtime.InteropServices;

namespace ScreenTranslator.Interop;

internal static class NativeMethods
{
  internal const int WH_MOUSE_LL = 14;
  internal const int WH_KEYBOARD_LL = 13;
  internal const int WM_LBUTTONDOWN = 0x0201;
  internal const int WM_MBUTTONDOWN = 0x0207;
  internal const int WM_RBUTTONDOWN = 0x0204;
  internal const int WM_MOUSEWHEEL = 0x020A;
  internal const int WM_MOUSEHWHEEL = 0x020E;

  internal const int WM_KEYDOWN = 0x0100;
  internal const int WM_SYSKEYDOWN = 0x0104;
  internal const int WM_CLIPBOARDUPDATE = 0x031D;

  internal const int GWL_EXSTYLE = -20;
  internal const int WS_EX_TOOLWINDOW = 0x00000080;
  internal const int WS_EX_NOACTIVATE = 0x08000000;
  internal const int WS_EX_TRANSPARENT = 0x00000020;
  internal const int RGN_DIFF = 4;
  internal const int CURSOR_SHOWING = 0x00000001;
  internal const int DI_NORMAL = 0x0003;
  internal const int IDC_ARROW = 32512;
  internal const int IDC_IBEAM = 32513;
  internal const int IDC_HAND = 32649;

  internal const uint SWP_NOACTIVATE = 0x0010;
  internal const uint SWP_NOSIZE = 0x0001;
  internal const uint SWP_NOMOVE = 0x0002;
  internal const uint SWP_NOZORDER = 0x0004;
  internal const uint SWP_SHOWWINDOW = 0x0040;

  internal static readonly IntPtr HWND_MESSAGE = new(-3);

  [DllImport("user32.dll")]
  internal static extern bool GetCursorPos(out POINT lpPoint);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool GetCursorInfo(ref CURSORINFO pci);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool DrawIconEx(
    IntPtr hdc,
    int xLeft,
    int yTop,
    IntPtr hIcon,
    int cxWidth,
    int cyWidth,
    uint istepIfAniCur,
    IntPtr hbrFlickerFreeDraw,
    int diFlags);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

  [DllImport("user32.dll")]
  internal static extern bool SetCursorPos(int X, int Y);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
  private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
  private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

  internal static int GetWindowLong(IntPtr hWnd, int nIndex)
  {
    return (int)(IntPtr.Size == 8
      ? GetWindowLongPtr64(hWnd, nIndex)
      : GetWindowLongPtr32(hWnd, nIndex));
  }

  [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
  private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

  [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
  private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

  internal static int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
  {
    return (int)(IntPtr.Size == 8
      ? SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong))
      : SetWindowLongPtr32(hWnd, nIndex, new IntPtr(dwNewLong)));
  }

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool SetWindowPos(
    IntPtr hWnd,
    IntPtr hWndInsertAfter,
    int X,
    int Y,
    int cx,
    int cy,
    uint uFlags);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

  [DllImport("gdi32.dll", SetLastError = true)]
  internal static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

  [DllImport("gdi32.dll", SetLastError = true)]
  internal static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

  [DllImport("gdi32.dll", SetLastError = true)]
  internal static extern bool DeleteObject(IntPtr hObject);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  [DllImport("user32.dll")]
  internal static extern IntPtr GetWindowFromPoint(POINT point);

  [DllImport("user32.dll")]
  internal static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  internal static extern IntPtr GetForegroundWindow();

  // Windows 10 2004+ (build 19041): excludes window from screen capture.
  internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

  internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

  internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

  internal const int INPUT_KEYBOARD = 1;
  internal const int INPUT_MOUSE = 0;
  internal const uint KEYEVENTF_KEYUP = 0x0002;
  internal const uint MOUSEEVENTF_WHEEL = 0x0800;
  internal const uint VK_PRIOR = 0x21;
  internal const uint VK_NEXT = 0x22;
  internal const uint VK_SPACE = 0x20;
  internal const uint VK_ESCAPE = 0x1B;
  internal const uint VK_UP = 0x26;
  internal const uint VK_DOWN = 0x28;
  internal const uint VK_CONTROL = 0x11;
  internal const uint VK_C = 0x43;

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

  [StructLayout(LayoutKind.Sequential)]
  internal struct POINT
  {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct RECT
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct CURSORINFO
  {
    public int cbSize;
    public int flags;
    public IntPtr hCursor;
    public POINT ptScreenPos;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct ICONINFO
  {
    [MarshalAs(UnmanagedType.Bool)]
    public bool fIcon;
    public uint xHotspot;
    public uint yHotspot;
    public IntPtr hbmMask;
    public IntPtr hbmColor;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct MSLLHOOKSTRUCT
  {
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct KBDLLHOOKSTRUCT
  {
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct INPUT
  {
    public uint type;
    public INPUTUNION u;
  }

  [StructLayout(LayoutKind.Explicit)]
  internal struct INPUTUNION
  {
    [FieldOffset(0)]
    public KEYBDINPUT ki;
    [FieldOffset(0)]
    public MOUSEINPUT mi;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct MOUSEINPUT
  {
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct KEYBDINPUT
  {
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
  }
}
