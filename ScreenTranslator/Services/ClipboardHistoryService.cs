using System.Runtime.InteropServices;
using System.Windows.Interop;

using ScreenTranslator.Interop;

using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace ScreenTranslator.Services;

public sealed class ClipboardHistoryService : IDisposable
{
  private const int MaxTextLength = 100_000;

  private readonly object _lock = new();
  private readonly List<string> _recent = new();
  private int _maxItems;
  private HwndSource? _source;
  private volatile string? _suppressTextOnce;
  private bool _disposed;

  public event EventHandler? HistoryChanged;

  public ClipboardHistoryService(int maxItems = 3)
  {
    _maxItems = Math.Clamp(maxItems, 1, 20);
    InitializeListenerWindow();
  }

  /// <summary>
  /// 更新最大历史条目数，立即生效
  /// </summary>
  public void UpdateMaxItems(int maxItems)
  {
    var newMax = Math.Clamp(maxItems, 1, 20);
    lock (_lock)
    {
      _maxItems = newMax;
      // 如果当前历史条目超过新的最大值，截断多余的
      if (_recent.Count > _maxItems)
      {
        _recent.RemoveRange(_maxItems, _recent.Count - _maxItems);
      }
    }
  }

  public IReadOnlyList<string> GetRecent()
  {
    lock (_lock)
    {
      return _recent.ToArray();
    }
  }

  public async Task SetClipboardTextAsync(string text, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    text ??= string.Empty;
    if (text.Length > MaxTextLength)
      text = text[..MaxTextLength];

    // 使用 Interlocked 确保线程安全的写入
    Interlocked.Exchange(ref _suppressTextOnce, text);

    for (var attempt = 0; attempt < 3; attempt++)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        WpfClipboard.SetText(text, WpfTextDataFormat.UnicodeText);
        return;
      }
      catch (Exception ex) when (IsClipboardBusy(ex))
      {
        await Task.Delay(20 * (attempt + 1), ct);
      }
    }
  }

  public void Dispose()
  {
    if (_disposed)
      return;
    _disposed = true;

    if (_source is not null)
    {
      try { NativeMethods.RemoveClipboardFormatListener(_source.Handle); } catch { }
      try { _source.RemoveHook(WndProc); } catch { }
      try { _source.Dispose(); } catch { }
      _source = null;
    }
  }

  private void InitializeListenerWindow()
  {
    var p = new HwndSourceParameters("ScreenTranslator.ClipboardListener")
    {
      Width = 0,
      Height = 0,
      ParentWindow = NativeMethods.HWND_MESSAGE,
      WindowStyle = 0,
    };

    _source = new HwndSource(p);
    _source.AddHook(WndProc);

    NativeMethods.AddClipboardFormatListener(_source.Handle);
  }

  private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
  {
    if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
    {
      handled = true;

      _ = WpfApplication.Current.Dispatcher.BeginInvoke(async () =>
      {
        var text = await TryGetClipboardTextAsync();
        if (string.IsNullOrWhiteSpace(text))
          return;

        if (text.Length > MaxTextLength)
          text = text[..MaxTextLength];

        // 线程安全地读取并清除抑制标记
        var suppress = Interlocked.Exchange(ref _suppressTextOnce, null);
        if (suppress is not null && string.Equals(text, suppress, StringComparison.Ordinal))
        {
          return;
        }

        AddToHistory(text);
      });
    }

    return IntPtr.Zero;
  }

  private void AddToHistory(string text)
  {
    var changed = false;
    lock (_lock)
    {
      if (_recent.Count > 0 && string.Equals(_recent[0], text, StringComparison.Ordinal))
        return;

      _recent.RemoveAll(s => string.Equals(s, text, StringComparison.Ordinal));
      _recent.Insert(0, text);
      if (_recent.Count > _maxItems)
        _recent.RemoveRange(_maxItems, _recent.Count - _maxItems);

      changed = true;
    }

    if (changed)
      HistoryChanged?.Invoke(this, EventArgs.Empty);
  }

  private static async Task<string?> TryGetClipboardTextAsync()
  {
    for (var attempt = 0; attempt < 3; attempt++)
    {
      try
      {
        if (!WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText))
          return null;

        return WpfClipboard.GetText(WpfTextDataFormat.UnicodeText);
      }
      catch (Exception ex) when (IsClipboardBusy(ex))
      {
        await Task.Delay(20 * (attempt + 1));
      }
    }

    return null;
  }

  private static bool IsClipboardBusy(Exception ex) =>
    ex is COMException || ex is ExternalException;
}
