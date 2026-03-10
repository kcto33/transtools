using System.Windows;

namespace ScreenTranslator;

public partial class App : System.Windows.Application
{
  private const int PasteHistoryHotkeyId = 0xBEEE;
  private const int ScreenshotHotkeyId = 0xBEED;

  private Services.SettingsService? _settings;
  private Services.TrayService? _tray;
  private Services.HotkeyService? _hotkeys;
  private Services.SelectionFlowController? _flow;
  private Services.ClipboardHistoryService? _clipboardHistory;
  private Services.PasteHistoryController? _pasteHistory;
  private Services.ScreenshotController? _screenshotController;

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    ShutdownMode = ShutdownMode.OnExplicitShutdown;

    _settings = new Services.SettingsService();
    _settings.Load();

    // Initialize localization
    Services.LocalizationService.Instance.Initialize(_settings.Settings.Language);

    _clipboardHistory = new Services.ClipboardHistoryService(_settings.Settings.ClipboardHistoryMaxItems);
    _pasteHistory = new Services.PasteHistoryController(_settings, _clipboardHistory);
    _screenshotController = new Services.ScreenshotController(_settings.Settings);

    _flow = new Services.SelectionFlowController(_settings, ApplyHotkey, ApplyPasteHistoryHotkey, ApplyScreenshotHotkey, UpdateClipboardHistoryMaxItems, SuspendHotkeys, ResumeHotkeys);

    _tray = new Services.TrayService(_settings);
    _tray.StartSelectionRequested += (_, _) => _flow.StartSelection();
    _tray.ShowPasteHistoryRequested += (_, _) => _pasteHistory.ShowOrClose();
    _tray.StartScreenshotRequested += (_, _) => _screenshotController?.StartScreenshot();
    _tray.ExitRequested += (_, _) => Shutdown();
    _tray.ShowSettingsRequested += (_, _) => _flow.ShowSettings();
    _tray.ToggleAutoStartRequested += (_, _) => _tray.ToggleAutoStart();
    _tray.Initialize();

    _hotkeys = new Services.HotkeyService();
    _hotkeys.HotkeyPressedById += (_, id) =>
    {
      if (id == Services.HotkeyService.DefaultHotkeyId)
        _flow.StartSelection();
      else if (id == PasteHistoryHotkeyId)
        _pasteHistory.ShowOrClose();
      else if (id == ScreenshotHotkeyId)
        _screenshotController?.StartScreenshot();
    };
    TryRegisterStartupHotkeys();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    _hotkeys?.Dispose();
    _tray?.Dispose();
    _pasteHistory?.Dispose();
    _clipboardHistory?.Dispose();
    _screenshotController?.CloseAllPinWindows();
    base.OnExit(e);
  }

  private void TryRegisterStartupHotkeys()
  {
    var hotkey = _settings?.Settings.Hotkey;
    try
    {
      _hotkeys?.RegisterHotkey(hotkey, Services.HotkeyService.DefaultHotkeyId);
    }
    catch (Exception ex)
    {
      try { _hotkeys?.RegisterHotkey("Ctrl+Alt+T", Services.HotkeyService.DefaultHotkeyId); } catch { }
      System.Windows.MessageBox.Show($"Hotkey registration failed: {ex.Message}", "ScreenTranslator");
    }

    var pasteHotkey = _settings?.Settings.PasteHistoryHotkey;
    try
    {
      _hotkeys?.RegisterHotkey(pasteHotkey, PasteHistoryHotkeyId);
    }
    catch (Exception ex)
    {
      try { _hotkeys?.RegisterHotkey("Ctrl+Shift+V", PasteHistoryHotkeyId); } catch { }
      System.Windows.MessageBox.Show($"Paste history hotkey registration failed: {ex.Message}", "ScreenTranslator");
    }

    var screenshotHotkey = _settings?.Settings.ScreenshotHotkey;
    try
    {
      _hotkeys?.RegisterHotkey(screenshotHotkey, ScreenshotHotkeyId);
    }
    catch (Exception ex)
    {
      try { _hotkeys?.RegisterHotkey("Ctrl+Alt+S", ScreenshotHotkeyId); } catch { }
      System.Windows.MessageBox.Show($"Screenshot hotkey registration failed: {ex.Message}", "ScreenTranslator");
    }
  }

  private string? ApplyHotkey(string hotkey)
  {
    if (_hotkeys is null)
      return "Hotkey service is not initialized.";

    try
    {
      _hotkeys.RegisterHotkey(hotkey, Services.HotkeyService.DefaultHotkeyId);
      return null;
    }
    catch (Exception ex)
    {
      return ex.Message;
    }
  }

  private string? ApplyPasteHistoryHotkey(string hotkey)
  {
    if (_hotkeys is null)
      return "Hotkey service is not initialized.";

    try
    {
      _hotkeys.RegisterHotkey(hotkey, PasteHistoryHotkeyId);
      return null;
    }
    catch (Exception ex)
    {
      return ex.Message;
    }
  }

  private string? ApplyScreenshotHotkey(string hotkey)
  {
    if (_hotkeys is null)
      return "Hotkey service is not initialized.";

    try
    {
      _hotkeys.RegisterHotkey(hotkey, ScreenshotHotkeyId);
      return null;
    }
    catch (Exception ex)
    {
      return ex.Message;
    }
  }

  private void UpdateClipboardHistoryMaxItems(int maxItems)
  {
    _clipboardHistory?.UpdateMaxItems(maxItems);
  }

  private void SuspendHotkeys()
  {
    _hotkeys?.UnregisterAll();
  }

  private void ResumeHotkeys()
  {
    TryRegisterStartupHotkeys();
  }
}
