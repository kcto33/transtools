using System.Windows;

namespace ScreenTranslator;

public partial class App : System.Windows.Application
{
  private const int PasteHistoryHotkeyId = 0xBEEE;
  private const int ScreenshotHotkeyId = 0xBEED;

  private bool _hotkeysSuppressed;
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

    _flow = new Services.SelectionFlowController(_settings, _clipboardHistory, ApplyHotkey, ApplyPasteHistoryHotkey, ApplyScreenshotHotkey, UpdateClipboardHistoryMaxItems, SuspendHotkeys, ResumeHotkeys);

    _tray = new Services.TrayService(_settings);
    _tray.StartSelectionRequested += async (_, _) => await _flow.StartSelectionOrTranslateSelectedTextAsync();
    _tray.ShowPasteHistoryRequested += (_, _) => _pasteHistory.ShowOrClose();
    _tray.StartScreenshotRequested += async (_, _) =>
    {
      if (_screenshotController is not null)
      {
        await _screenshotController.StartScreenshotAsync();
      }
    };
    _tray.ExitRequested += (_, _) => Shutdown();
    _tray.ShowSettingsRequested += (_, _) => _flow.ShowSettings();
    _tray.ToggleAutoStartRequested += (_, _) => _tray.ToggleAutoStart();
    _tray.ToggleHotkeysRequested += (_, _) => ToggleHotkeysSuppression();
    _tray.SetHotkeysEnabled(true);
    _tray.Initialize();

    _hotkeys = new Services.HotkeyService();
    _hotkeys.HotkeyPressedById += async (_, id) =>
    {
      if (id == Services.HotkeyService.DefaultHotkeyId)
        await _flow.StartSelectionOrTranslateSelectedTextAsync();
      else if (id == PasteHistoryHotkeyId)
        _pasteHistory.ShowOrClose();
      else if (id == ScreenshotHotkeyId && _screenshotController is not null)
        await _screenshotController.StartScreenshotAsync();
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
    if (_hotkeysSuppressed)
    {
      _hotkeys?.UnregisterAll();
      return;
    }

    var hotkey = _settings?.Settings.Hotkey;
    try
    {
      _hotkeys?.RegisterHotkey(hotkey, Services.HotkeyService.DefaultHotkeyId);
    }
    catch (Exception ex)
    {
      try { _hotkeys?.RegisterHotkey("Ctrl+Alt+T", Services.HotkeyService.DefaultHotkeyId); } catch { }
      System.Windows.MessageBox.Show($"Hotkey registration failed: {ex.Message}", "transtools");
    }

    var pasteHotkey = _settings?.Settings.PasteHistoryHotkey;
    try
    {
      _hotkeys?.RegisterHotkey(pasteHotkey, PasteHistoryHotkeyId);
    }
    catch (Exception ex)
    {
      try { _hotkeys?.RegisterHotkey("Ctrl+Shift+V", PasteHistoryHotkeyId); } catch { }
      System.Windows.MessageBox.Show($"Paste history hotkey registration failed: {ex.Message}", "transtools");
    }

    var screenshotHotkey = _settings?.Settings.ScreenshotHotkey;
    try
    {
      _hotkeys?.RegisterHotkey(screenshotHotkey, ScreenshotHotkeyId);
    }
    catch (Exception ex)
    {
      try { _hotkeys?.RegisterHotkey("Ctrl+Alt+S", ScreenshotHotkeyId); } catch { }
      System.Windows.MessageBox.Show($"Screenshot hotkey registration failed: {ex.Message}", "transtools");
    }
  }

  private string? ApplyHotkey(string hotkey)
  {
    return ApplyHotkey(hotkey, Services.HotkeyService.DefaultHotkeyId);
  }

  private string? ApplyPasteHistoryHotkey(string hotkey)
  {
    return ApplyHotkey(hotkey, PasteHistoryHotkeyId);
  }

  private string? ApplyScreenshotHotkey(string hotkey)
  {
    return ApplyHotkey(hotkey, ScreenshotHotkeyId);
  }

  private string? ApplyHotkey(string hotkey, int id)
  {
    if (_hotkeys is null)
      return "Hotkey service is not initialized.";

    try
    {
      _hotkeys.RegisterHotkey(hotkey, id);
      if (_hotkeysSuppressed)
      {
        _hotkeys.Unregister(id);
      }

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
    if (_hotkeysSuppressed)
      return;

    TryRegisterStartupHotkeys();
  }

  private void ToggleHotkeysSuppression()
  {
    _hotkeysSuppressed = !_hotkeysSuppressed;
    _tray?.SetHotkeysEnabled(!_hotkeysSuppressed);

    if (_hotkeysSuppressed)
    {
      SuspendHotkeys();
      return;
    }

    ResumeHotkeys();
  }
}
