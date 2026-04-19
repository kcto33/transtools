using System.Runtime.InteropServices;
using System.Windows;
using ScreenTranslator.Interop;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataObject = System.Windows.IDataObject;
using WpfTextDataFormat = System.Windows.TextDataFormat;

namespace ScreenTranslator.Services;

public sealed class SelectedTextCaptureService
{
  private readonly IPlatform _platform;
  private readonly Func<IDisposable?> _suppressClipboardHistory;
  private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
  private readonly int _timeoutMs;
  private readonly int _settleDelayMs;
  private readonly int _pollIntervalMs;

  public SelectedTextCaptureService(
    ClipboardHistoryService? clipboardHistory = null,
    int timeoutMs = 220,
    int settleDelayMs = 20,
    int pollIntervalMs = 20)
    : this(
      new Platform(),
      () => clipboardHistory?.SuppressTracking(),
      (delay, ct) => Task.Delay(delay, ct),
      timeoutMs,
      settleDelayMs,
      pollIntervalMs)
  {
  }

  internal SelectedTextCaptureService(
    IPlatform platform,
    Func<IDisposable?> suppressClipboardHistory,
    int timeoutMs = 220,
    int settleDelayMs = 20,
    int pollIntervalMs = 20)
    : this(
      platform,
      suppressClipboardHistory,
      (delay, ct) => Task.Delay(delay, ct),
      timeoutMs,
      settleDelayMs,
      pollIntervalMs)
  {
  }

  internal SelectedTextCaptureService(
    IPlatform platform,
    Func<IDisposable?> suppressClipboardHistory,
    Func<TimeSpan, CancellationToken, Task> delayAsync,
    int timeoutMs,
    int settleDelayMs,
    int pollIntervalMs)
  {
    _platform = platform;
    _suppressClipboardHistory = suppressClipboardHistory;
    _delayAsync = delayAsync;
    _timeoutMs = Math.Max(1, timeoutMs);
    _settleDelayMs = Math.Max(0, settleDelayMs);
    _pollIntervalMs = Math.Max(1, pollIntervalMs);
  }

  public async Task<string?> TryCaptureAsync(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (!_platform.CanCapture())
      return null;

    ClipboardSnapshot? snapshot = null;
    using var suppression = _suppressClipboardHistory();

    try
    {
      snapshot = await _platform.CaptureClipboardAsync(ct);
      await _platform.ClearClipboardAsync(ct);

      if (_settleDelayMs > 0)
        await _delayAsync(TimeSpan.FromMilliseconds(_settleDelayMs), ct);

      await _platform.SendCopyAsync(ct);

      var attempts = Math.Max(1, (int)Math.Ceiling((double)_timeoutMs / _pollIntervalMs));
      for (var attempt = 0; attempt < attempts; attempt++)
      {
        ct.ThrowIfCancellationRequested();

        var text = await _platform.ReadClipboardTextAsync(ct);
        if (!string.IsNullOrWhiteSpace(text))
          return text.Trim();

        if (attempt + 1 < attempts)
          await _delayAsync(TimeSpan.FromMilliseconds(_pollIntervalMs), ct);
      }
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch
    {
      return null;
    }
    finally
    {
      if (snapshot is not null)
      {
        try
        {
          await _platform.RestoreClipboardAsync(snapshot, CancellationToken.None);
        }
        catch
        {
          // Clipboard restore is best effort.
        }
      }
    }

    return null;
  }

  internal interface IPlatform
  {
    bool CanCapture();
    Task<ClipboardSnapshot> CaptureClipboardAsync(CancellationToken ct);
    Task ClearClipboardAsync(CancellationToken ct);
    Task<string?> ReadClipboardTextAsync(CancellationToken ct);
    Task RestoreClipboardAsync(ClipboardSnapshot snapshot, CancellationToken ct);
    Task SendCopyAsync(CancellationToken ct);
  }

  internal sealed class ClipboardSnapshot
  {
    public ClipboardSnapshot(string? text, bool hasData)
      : this(null, text, hasData)
    {
    }

    public ClipboardSnapshot(WpfDataObject? dataObject, string? text, bool hasData)
    {
      DataObject = dataObject;
      Text = text;
      HasData = hasData;
    }

    public WpfDataObject? DataObject { get; }
    public string? Text { get; }
    public bool HasData { get; }
  }

  private sealed class Platform : IPlatform
  {
    public bool CanCapture() => NativeMethods.GetForegroundWindow() != IntPtr.Zero;

    public async Task<ClipboardSnapshot> CaptureClipboardAsync(CancellationToken ct)
    {
      for (var attempt = 0; attempt < 3; attempt++)
      {
        ct.ThrowIfCancellationRequested();
        try
        {
          var dataObject = WpfClipboard.GetDataObject();
          var text = WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText)
            ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
            : null;
          return new ClipboardSnapshot(dataObject, text, dataObject is not null);
        }
        catch (Exception ex) when (IsClipboardBusy(ex))
        {
          await Task.Delay(20 * (attempt + 1), ct);
        }
      }

      return new ClipboardSnapshot(null, WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText)
        ? WpfClipboard.GetText(WpfTextDataFormat.UnicodeText)
        : null, false);
    }

    public async Task ClearClipboardAsync(CancellationToken ct)
    {
      for (var attempt = 0; attempt < 3; attempt++)
      {
        ct.ThrowIfCancellationRequested();
        try
        {
          WpfClipboard.Clear();
          return;
        }
        catch (Exception ex) when (IsClipboardBusy(ex))
        {
          await Task.Delay(20 * (attempt + 1), ct);
        }
      }
    }

    public async Task<string?> ReadClipboardTextAsync(CancellationToken ct)
    {
      for (var attempt = 0; attempt < 3; attempt++)
      {
        ct.ThrowIfCancellationRequested();
        try
        {
          if (!WpfClipboard.ContainsText(WpfTextDataFormat.UnicodeText))
            return null;

          return WpfClipboard.GetText(WpfTextDataFormat.UnicodeText);
        }
        catch (Exception ex) when (IsClipboardBusy(ex))
        {
          await Task.Delay(20 * (attempt + 1), ct);
        }
      }

      return null;
    }

    public async Task RestoreClipboardAsync(ClipboardSnapshot snapshot, CancellationToken ct)
    {
      for (var attempt = 0; attempt < 3; attempt++)
      {
        ct.ThrowIfCancellationRequested();
        try
        {
          if (snapshot.DataObject is not null)
          {
            WpfClipboard.SetDataObject(snapshot.DataObject, true);
          }
          else if (!string.IsNullOrEmpty(snapshot.Text))
          {
            WpfClipboard.SetText(snapshot.Text, WpfTextDataFormat.UnicodeText);
          }
          else if (!snapshot.HasData)
          {
            WpfClipboard.Clear();
          }

          return;
        }
        catch (Exception ex) when (IsClipboardBusy(ex))
        {
          await Task.Delay(20 * (attempt + 1), ct);
        }
      }
    }

    public Task SendCopyAsync(CancellationToken ct)
    {
      ct.ThrowIfCancellationRequested();

      var inputs = new[]
      {
        CreateKeyInput(NativeMethods.VK_CONTROL, 0),
        CreateKeyInput(NativeMethods.VK_C, 0),
        CreateKeyInput(NativeMethods.VK_C, NativeMethods.KEYEVENTF_KEYUP),
        CreateKeyInput(NativeMethods.VK_CONTROL, NativeMethods.KEYEVENTF_KEYUP),
      };

      NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
      return Task.CompletedTask;
    }

    private static NativeMethods.INPUT CreateKeyInput(uint virtualKey, uint flags)
    {
      return new NativeMethods.INPUT
      {
        type = NativeMethods.INPUT_KEYBOARD,
        u = new NativeMethods.INPUTUNION
        {
          ki = new NativeMethods.KEYBDINPUT
          {
            wVk = (ushort)virtualKey,
            dwFlags = flags,
          }
        }
      };
    }

    private static bool IsClipboardBusy(Exception ex) =>
      ex is COMException || ex is ExternalException;
  }
}
