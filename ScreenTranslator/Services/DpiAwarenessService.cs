using ScreenTranslator.Interop;

namespace ScreenTranslator.Services;

internal static class DpiAwarenessService
{
  public static void TryEnablePerMonitorV2()
  {
    try
    {
      NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }
    catch
    {
      // The manifest also requests PerMonitorV2. If Windows has already fixed
      // the process DPI context, continuing is safer than failing app startup.
    }
  }
}
