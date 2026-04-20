using System.IO;
using System.Windows;
using System.Windows.Forms;
using Wpf = System.Windows.Controls;

namespace ScreenTranslator.Services;

public sealed class TrayService : IDisposable
{
  private readonly SettingsService _settings;
  private readonly AutoStartService _autoStart = new();
  private NotifyIcon? _icon;
  private System.Drawing.Icon? _trayIcon;
  private bool _trayIconOwned;

  public event EventHandler? StartSelectionRequested;
  public event EventHandler? ShowPasteHistoryRequested;
  public event EventHandler? StartScreenshotRequested;
  public event EventHandler? ShowSettingsRequested;
  public event EventHandler? ToggleAutoStartRequested;
  public event EventHandler? ToggleHotkeysRequested;
  public event EventHandler? ExitRequested;

  public bool HotkeysEnabled { get; private set; } = true;

  public TrayService(SettingsService settings)
  {
    _settings = settings;
  }

  public void Initialize()
  {
    _icon = new NotifyIcon
    {
      Visible = true,
      Text = "transtools",
      Icon = GetTrayIcon(),
    };

    _icon.DoubleClick += (_, _) => StartSelectionRequested?.Invoke(this, EventArgs.Empty);
    _icon.MouseUp += OnIconMouseUp;
  }

  private void OnIconMouseUp(object? sender, MouseEventArgs e)
  {
    if (e.Button == MouseButtons.Right)
    {
      ShowContextMenu();
    }
  }

  private void ShowContextMenu()
  {
    var startHotkey = string.IsNullOrWhiteSpace(_settings.Settings.Hotkey) ? "Ctrl+Alt+T" : _settings.Settings.Hotkey.Trim();
    var pasteHotkey = string.IsNullOrWhiteSpace(_settings.Settings.PasteHistoryHotkey)
      ? "Ctrl+Shift+V"
      : _settings.Settings.PasteHistoryHotkey.Trim();
    var screenshotHotkey = string.IsNullOrWhiteSpace(_settings.Settings.ScreenshotHotkey)
      ? "Ctrl+Alt+S"
      : _settings.Settings.ScreenshotHotkey.Trim();
    var gestureText = HotkeysEnabled;

    var menu = new Wpf.ContextMenu();

    var startItem = new Wpf.MenuItem
    {
      Header = LocalizationService.GetString("TrayMenu_StartSelection"),
      InputGestureText = gestureText ? startHotkey : string.Empty
    };
    startItem.Click += (_, _) => StartSelectionRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(startItem);

    var pasteItem = new Wpf.MenuItem
    {
      Header = LocalizationService.GetString("TrayMenu_PasteHistory"),
      InputGestureText = gestureText ? pasteHotkey : string.Empty
    };
    pasteItem.Click += (_, _) => ShowPasteHistoryRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(pasteItem);

    var screenshotItem = new Wpf.MenuItem
    {
      Header = LocalizationService.GetString("TrayMenu_Screenshot"),
      InputGestureText = gestureText ? screenshotHotkey : string.Empty
    };
    screenshotItem.Click += (_, _) => StartScreenshotRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(screenshotItem);

    menu.Items.Add(new Wpf.Separator());

    var disableHotkeysItem = new Wpf.MenuItem
    {
      Header = LocalizationService.GetString("TrayMenu_DisableHotkeys"),
      IsCheckable = true,
      IsChecked = !HotkeysEnabled
    };
    disableHotkeysItem.Click += (_, _) => ToggleHotkeysRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(disableHotkeysItem);

    var settingsItem = new Wpf.MenuItem { Header = LocalizationService.GetString("TrayMenu_Settings") };
    settingsItem.Click += (_, _) => ShowSettingsRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(settingsItem);

    var autoStartItem = new Wpf.MenuItem 
    { 
      Header = LocalizationService.GetString("TrayMenu_StartWithWindows"), 
      IsCheckable = true,
      IsChecked = _autoStart.IsEnabled() 
    };
    autoStartItem.Click += (_, _) => 
    {
        // Toggle logic is handled in event, but UI update is immediate here contextually, 
        // essentially we request the toggle, and let the service handle it.
        ToggleAutoStartRequested?.Invoke(this, EventArgs.Empty);
    };
    menu.Items.Add(autoStartItem);

    menu.Items.Add(new Wpf.Separator());

    var exitItem = new Wpf.MenuItem { Header = LocalizationService.GetString("TrayMenu_Exit") };
    exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(exitItem);

    // Create a hidden proxy window to host the ContextMenu
    // This ensures it receives focus, processes keyboard input, and closes correctly when clicking outside.
    var window = new Window
    {
      Title = "TrayMenuProxy",
      Width = 0,
      Height = 0,
      WindowStyle = WindowStyle.None,
      ResizeMode = ResizeMode.NoResize,
      ShowInTaskbar = false,
      Visibility = Visibility.Visible, // Must be visible to be active
      Left = -10000,
      Top = -10000,
      Background = System.Windows.Media.Brushes.Transparent
    };

    window.ContextMenu = menu;
    menu.Closed += (_, _) => window.Close();
    
    window.Show();
    window.Activate();
    menu.IsOpen = true;
  }

  public void ToggleAutoStart()
  {
    try
    {
      var exePath = Environment.ProcessPath;
      if (string.IsNullOrWhiteSpace(exePath))
        return;

      if (_autoStart.IsEnabled())
        _autoStart.Disable();
      else
        _autoStart.Enable(exePath);
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show($"Failed to update auto-start: {ex.Message}", "transtools");
    }
  }

  public void SetHotkeysEnabled(bool enabled)
  {
    HotkeysEnabled = enabled;
  }

  public void Dispose()
  {
    if (_icon is not null)
    {
      _icon.Visible = false;
      _icon.Dispose();
      _icon = null;
    }

    if (_trayIconOwned)
    {
      _trayIcon?.Dispose();
    }

    _trayIcon = null;
    _trayIconOwned = false;
  }

  private System.Drawing.Icon GetTrayIcon()
  {
    if (_trayIcon is not null)
      return _trayIcon;

    // First try to load from embedded resource
    try
    {
      var resourceUri = new Uri("pack://application:,,,/transtools;component/Assets/tray.ico", UriKind.Absolute);
      var streamInfo = System.Windows.Application.GetResourceStream(resourceUri);
      if (streamInfo?.Stream is not null)
      {
        using var stream = streamInfo.Stream;
        _trayIcon = new System.Drawing.Icon(stream);
        _trayIconOwned = true;
        return _trayIcon;
      }
    }
    catch
    {
      // ignore and try file paths
    }

    // Try multiple possible locations for the icon
    var possiblePaths = new[]
    {
      Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico"),
      Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tray.ico"),
      Path.Combine(Environment.CurrentDirectory, "Assets", "tray.ico"),
      Path.Combine(Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "", "Assets", "tray.ico"),
    };

    foreach (var assetIcon in possiblePaths)
    {
      if (File.Exists(assetIcon))
      {
        try
        {
          _trayIcon = new System.Drawing.Icon(assetIcon);
          _trayIconOwned = true;
          return _trayIcon;
        }
        catch
        {
          // ignore and try next
        }
      }
    }

    var exePath = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(exePath))
    {
      try
      {
        var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
        if (icon is not null)
        {
          _trayIconOwned = true;
          _trayIcon = (System.Drawing.Icon)icon.Clone();
          return _trayIcon;
        }
      }
      catch
      {
        // ignore and fall back
      }
    }

    _trayIconOwned = false;
    _trayIcon = System.Drawing.SystemIcons.Application;
    return _trayIcon;
  }
}
