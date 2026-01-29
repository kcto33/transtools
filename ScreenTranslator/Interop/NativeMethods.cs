using System.Runtime.InteropServices;

namespace ScreenTranslator.Interop;

internal static class NativeMethods
{
  internal const int WH_MOUSE_LL = 14;
  internal const int WM_LBUTTONDOWN = 0x0201;
  internal const int WM_RBUTTONDOWN = 0x0204;

  internal const int GWL_EXSTYLE = -20;
  internal const int WS_EX_TOOLWINDOW = 0x00000080;
  internal const int WS_EX_NOACTIVATE = 0x08000000;

  internal const uint SWP_NOACTIVATE = 0x0010;
  internal const uint SWP_NOSIZE = 0x0001;
  internal const uint SWP_NOMOVE = 0x0002;
  internal const uint SWP_NOZORDER = 0x0004;
  internal const uint SWP_SHOWWINDOW = 0x0040;

  [DllImport("user32.dll")]
  internal static extern bool GetCursorPos(out POINT lpPoint);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

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
  internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

  internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

  [DllImport("user32.dll", SetLastError = true)]
  internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

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
  internal struct MSLLHOOKSTRUCT
  {
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
  }
}
