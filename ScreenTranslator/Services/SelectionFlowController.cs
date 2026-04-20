using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ScreenTranslator.Interop;
using ScreenTranslator.Windows;

namespace ScreenTranslator.Services;

public sealed class SelectionFlowController
{
  private readonly SettingsService _settings;
  private readonly TranslationService _translation;
  private readonly OcrService _ocr;
  private readonly Func<CancellationToken, Task<string?>> _tryCaptureSelectedTextAsync;
  private readonly Action _startOverlaySelection;
  private readonly Func<string, CancellationToken, Task> _showSelectedTextTranslationAsync;
  private readonly Func<string, string?>? _applyHotkey;
  private readonly Func<string, string?>? _applyPasteHistoryHotkey;
  private readonly Func<string, string?>? _applyScreenshotHotkey;
  private readonly Action<int>? _updateClipboardHistoryMaxItems;
  private readonly Action? _suspendHotkeys;
  private readonly Action? _resumeHotkeys;

  private OverlayWindow? _overlay;
  private BubbleWindow? _bubble;
  private SettingsWindow? _settingsWindow;
  private CancellationTokenSource? _cts;

  public SelectionFlowController(
    SettingsService settings,
    ClipboardHistoryService? clipboardHistory = null,
    Func<string, string?>? applyHotkey = null,
    Func<string, string?>? applyPasteHistoryHotkey = null,
    Func<string, string?>? applyScreenshotHotkey = null,
    Action<int>? updateClipboardHistoryMaxItems = null,
    Action? suspendHotkeys = null,
    Action? resumeHotkeys = null)
    : this(
      settings,
      new TranslationService(settings),
      new OcrService(),
      new SelectedTextCaptureService(clipboardHistory),
      applyHotkey,
      applyPasteHistoryHotkey,
      applyScreenshotHotkey,
      updateClipboardHistoryMaxItems,
      suspendHotkeys,
      resumeHotkeys)
  {
  }

  internal SelectionFlowController(
    SettingsService settings,
    Func<CancellationToken, Task<string?>> tryCaptureSelectedTextAsync,
    Action startOverlaySelection,
    Func<string, CancellationToken, Task> showSelectedTextTranslationAsync)
    : this(
      settings,
      new TranslationService(settings),
      new OcrService(),
      tryCaptureSelectedTextAsync,
      startOverlaySelection,
      showSelectedTextTranslationAsync,
      null,
      null,
      null,
      null,
      null,
      null)
  {
  }

  private SelectionFlowController(
    SettingsService settings,
    TranslationService translation,
    OcrService ocr,
    SelectedTextCaptureService selectedTextCapture,
    Func<string, string?>? applyHotkey,
    Func<string, string?>? applyPasteHistoryHotkey,
    Func<string, string?>? applyScreenshotHotkey,
    Action<int>? updateClipboardHistoryMaxItems,
    Action? suspendHotkeys,
    Action? resumeHotkeys)
    : this(
      settings,
      translation,
      ocr,
      selectedTextCapture.TryCaptureAsync,
      () => { },
      (_, _) => Task.CompletedTask,
      applyHotkey,
      applyPasteHistoryHotkey,
      applyScreenshotHotkey,
      updateClipboardHistoryMaxItems,
      suspendHotkeys,
      resumeHotkeys)
  {
    _startOverlaySelection = StartSelectionOverlay;
    _showSelectedTextTranslationAsync = ShowSelectedTextTranslationAsync;
  }

  private SelectionFlowController(
    SettingsService settings,
    TranslationService translation,
    OcrService ocr,
    Func<CancellationToken, Task<string?>> tryCaptureSelectedTextAsync,
    Action startOverlaySelection,
    Func<string, CancellationToken, Task> showSelectedTextTranslationAsync,
    Func<string, string?>? applyHotkey,
    Func<string, string?>? applyPasteHistoryHotkey,
    Func<string, string?>? applyScreenshotHotkey,
    Action<int>? updateClipboardHistoryMaxItems,
    Action? suspendHotkeys,
    Action? resumeHotkeys)
  {
    _settings = settings;
    _translation = translation;
    _ocr = ocr;
    _tryCaptureSelectedTextAsync = tryCaptureSelectedTextAsync;
    _startOverlaySelection = startOverlaySelection;
    _showSelectedTextTranslationAsync = showSelectedTextTranslationAsync;
    _applyHotkey = applyHotkey;
    _applyPasteHistoryHotkey = applyPasteHistoryHotkey;
    _applyScreenshotHotkey = applyScreenshotHotkey;
    _updateClipboardHistoryMaxItems = updateClipboardHistoryMaxItems;
    _suspendHotkeys = suspendHotkeys;
    _resumeHotkeys = resumeHotkeys;
  }

  public async Task StartSelectionOrTranslateSelectedTextAsync()
  {
    if (_overlay is not null)
      return;

    var selectedText = await _tryCaptureSelectedTextAsync(CancellationToken.None);
    if (!string.IsNullOrWhiteSpace(selectedText))
    {
      await _showSelectedTextTranslationAsync(selectedText, CancellationToken.None);
      return;
    }

    _startOverlaySelection();
  }

  private void StartSelectionOverlay()
  {
    if (_overlay is not null)
      return;

    CancelInFlight();

    if (!NativeMethods.GetCursorPos(out var p))
      return;

    var screen = Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));

    _overlay = new OverlayWindow(screen);
    _overlay.SelectionCompleted += async (_, rectPx) =>
    {
      _overlay = null;
      await HandleSelectionAsync(screen, rectPx);
    };
    _overlay.SelectionCancelled += (_, _) => _overlay = null;

    _overlay.Show();
    _overlay.Activate();
  }

  public void ShowSettings()
  {
    System.Windows.Application.Current.Dispatcher.Invoke(() =>
    {
      try
      {
        if (_settingsWindow is null)
        {
          _settingsWindow = new SettingsWindow(_settings, _applyHotkey, _applyPasteHistoryHotkey, _applyScreenshotHotkey, _updateClipboardHistoryMaxItems, _suspendHotkeys, _resumeHotkeys);
          _settingsWindow.Closed += (_, _) => _settingsWindow = null;
          _settingsWindow.Show();
        }
        else
        {
          _settingsWindow.Activate();
        }
      }
      catch (Exception ex)
      {
        var errorPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "transtools_error.txt");
        System.IO.File.WriteAllText(errorPath, $"[{DateTime.Now}] Failed to open settings:\n{ex}");
        System.Windows.MessageBox.Show($"Error logged to: {errorPath}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
      }
    });
  }

  private async Task HandleSelectionAsync(Screen screen, Rectangle rectPx)
  {
    if (rectPx.Width < 8 || rectPx.Height < 8)
      return;

    CancelInFlight();
    _cts = new CancellationTokenSource();
    var ct = _cts.Token;

    // Show bubble immediately with placeholder.
    _bubble?.Close();
    _bubble = new BubbleWindow(screen, _settings.Settings.Bubble);
    _bubble.ShowPlaceholder(rectPx);
    var bubble = _bubble;

    var text = string.Empty;
    try
    {
      using var bmp = CaptureService.CaptureRegion(rectPx);
      text = await _ocr.RecognizeAsync(bmp, _settings.Settings.DefaultFrom, ct);
      var translated = await _translation.TranslateAsync(text, _settings.Settings.DefaultFrom, _settings.Settings.DefaultTo, ct);

      if (!ct.IsCancellationRequested)
      {
        if (bubble.IsVisible)
          bubble.SetTranslation(rectPx, translated, text);
      }
    }
    catch (OperationCanceledException)
    {
      // ignore
    }
    catch (Exception ex)
    {
      if (!ct.IsCancellationRequested)
      {
        if (bubble.IsVisible)
        {
          var fullMessage = $"(failed) {ex.Message}";
          var displayMessage = fullMessage.Length <= 400 ? fullMessage : fullMessage[..400] + "...";
          bubble.SetTranslation(rectPx, displayMessage, text, fullMessage);
        }
      }
    }
  }

  private async Task ShowSelectedTextTranslationAsync(string text, CancellationToken externalCt)
  {
    if (string.IsNullOrWhiteSpace(text))
      return;

    CancelInFlight();
    _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
    var ct = _cts.Token;

    if (!NativeMethods.GetCursorPos(out var p))
      return;

    var screen = Screen.FromPoint(new System.Drawing.Point(p.X, p.Y));
    var anchorRect = CreateCursorAnchor(screen, p);

    _bubble?.Close();
    _bubble = new BubbleWindow(screen, _settings.Settings.Bubble);
    _bubble.ShowPlaceholder(anchorRect);
    var bubble = _bubble;

    try
    {
      var translated = await _translation.TranslateAsync(text, _settings.Settings.DefaultFrom, _settings.Settings.DefaultTo, ct);
      if (!ct.IsCancellationRequested && bubble.IsVisible)
        bubble.SetTranslation(anchorRect, translated, text);
    }
    catch (OperationCanceledException)
    {
      // ignore
    }
    catch (Exception ex)
    {
      if (!ct.IsCancellationRequested && bubble.IsVisible)
      {
        var fullMessage = $"(failed) {ex.Message}";
        var displayMessage = fullMessage.Length <= 400 ? fullMessage : fullMessage[..400] + "...";
        bubble.SetTranslation(anchorRect, displayMessage, text, fullMessage);
      }
    }
  }

  private static Rectangle CreateCursorAnchor(Screen screen, NativeMethods.POINT cursorPos)
  {
    const int AnchorWidth = 18;
    const int AnchorHeight = 18;

    var work = screen.WorkingArea;
    var left = Math.Clamp(cursorPos.X, work.Left, Math.Max(work.Left, work.Right - AnchorWidth));
    var top = Math.Clamp(cursorPos.Y, work.Top, Math.Max(work.Top, work.Bottom - AnchorHeight));
    return new Rectangle(left, top, AnchorWidth, AnchorHeight);
  }

  private void CancelInFlight()
  {
    var cts = Interlocked.Exchange(ref _cts, null);
    if (cts is null)
      return;

    try
    {
      cts.Cancel();
    }
    catch { }
    finally
    {
      cts.Dispose();
    }
  }
}
