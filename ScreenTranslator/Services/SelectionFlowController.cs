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
  private readonly Func<string, string?>? _applyHotkey;

  private OverlayWindow? _overlay;
  private BubbleWindow? _bubble;
  private SettingsWindow? _settingsWindow;
  private CancellationTokenSource? _cts;

  public SelectionFlowController(SettingsService settings, Func<string, string?>? applyHotkey = null)
  {
    _settings = settings;
    _translation = new TranslationService(settings);
    _ocr = new OcrService();
    _applyHotkey = applyHotkey;
  }

  public void StartSelection()
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
      if (_settingsWindow is null)
      {
        _settingsWindow = new SettingsWindow(_settings, _applyHotkey);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
      }
      else
      {
        _settingsWindow.Activate();
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
    _bubble = new BubbleWindow(screen);
    _bubble.ShowPlaceholder(rectPx);
    var bubble = _bubble;

    var text = string.Empty;
    try
    {
      using var bmp = CaptureService.CaptureRegion(rectPx);
      text = await _ocr.RecognizeAsync(bmp, _settings.Settings.DefaultFrom, ct);
      var provider = _translation.CreateProvider();
      var translated = await provider.TranslateAsync(text, _settings.Settings.DefaultFrom, _settings.Settings.DefaultTo, ct);

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

  private void CancelInFlight()
  {
    try
    {
      _cts?.Cancel();
      _cts?.Dispose();
    }
    catch { }
    finally
    {
      _cts = null;
    }
  }
}
