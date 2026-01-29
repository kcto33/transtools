using System.Windows;

namespace ScreenTranslator;

public partial class App : System.Windows.Application
{
  private Services.SettingsService? _settings;
  private Services.TrayService? _tray;
  private Services.HotkeyService? _hotkeys;
  private Services.SelectionFlowController? _flow;

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    ShutdownMode = ShutdownMode.OnExplicitShutdown;

    _settings = new Services.SettingsService();
    _settings.Load();

    _flow = new Services.SelectionFlowController(_settings, ApplyHotkey);

    _tray = new Services.TrayService(_settings);
    _tray.StartSelectionRequested += (_, _) => _flow.StartSelection();
    _tray.ExitRequested += (_, _) => Shutdown();
    _tray.ShowSettingsRequested += (_, _) => _flow.ShowSettings();
    _tray.ToggleAutoStartRequested += (_, _) => _tray.ToggleAutoStart();
    _tray.Initialize();

    _hotkeys = new Services.HotkeyService();
    _hotkeys.HotkeyPressed += (_, _) => _flow.StartSelection();
    TryRegisterStartupHotkey();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    _hotkeys?.Dispose();
    _tray?.Dispose();
    base.OnExit(e);
  }

  private void TryRegisterStartupHotkey()
  {
    var hotkey = _settings?.Settings.Hotkey;
    try
    {
      _hotkeys?.RegisterHotkey(hotkey);
    }
    catch (Exception ex)
    {
      try { _hotkeys?.RegisterDefaultHotkey(); } catch { }
      System.Windows.MessageBox.Show($"Hotkey registration failed: {ex.Message}", "ScreenTranslator");
    }
  }

  private string? ApplyHotkey(string hotkey)
  {
    if (_hotkeys is null)
      return "Hotkey service is not initialized.";

    try
    {
      _hotkeys.RegisterHotkey(hotkey);
      return null;
    }
    catch (Exception ex)
    {
      return ex.Message;
    }
  }
}
