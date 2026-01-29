using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace ScreenTranslator.Services;

public sealed class TrayService : IDisposable
{
  private readonly SettingsService _settings;
  private readonly AutoStartService _autoStart = new();
  private NotifyIcon? _icon;
  private System.Drawing.Icon? _trayIcon;
  private bool _trayIconOwned;

  public event EventHandler? StartSelectionRequested;
  public event EventHandler? ShowSettingsRequested;
  public event EventHandler? ToggleAutoStartRequested;
  public event EventHandler? ExitRequested;

  public TrayService(SettingsService settings)
  {
    _settings = settings;
  }

  public void Initialize()
  {
    _icon = new NotifyIcon
    {
      Visible = true,
      Text = "ScreenTranslator",
      Icon = GetTrayIcon(),
      ContextMenuStrip = BuildMenu(),
    };

    _icon.DoubleClick += (_, _) => StartSelectionRequested?.Invoke(this, EventArgs.Empty);
  }

  private ContextMenuStrip BuildMenu()
  {
    var menu = new ContextMenuStrip();

    var start = new ToolStripMenuItem("Start Selection")
    {
      ShortcutKeys = Keys.Control | Keys.Alt | Keys.T,
      ShowShortcutKeys = true,
    };
    start.Click += (_, _) => StartSelectionRequested?.Invoke(this, EventArgs.Empty);

    var settings = new ToolStripMenuItem("Settings");
    settings.Click += (_, _) => ShowSettingsRequested?.Invoke(this, EventArgs.Empty);

    var autoStart = new ToolStripMenuItem("Start with Windows")
    {
      Checked = _autoStart.IsEnabled(),
      CheckOnClick = false,
    };
    autoStart.Click += (_, _) => ToggleAutoStartRequested?.Invoke(this, EventArgs.Empty);
    menu.Opening += (_, _) => autoStart.Checked = _autoStart.IsEnabled();

    var exit = new ToolStripMenuItem("Exit");
    exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

    menu.Items.Add(start);
    menu.Items.Add(new ToolStripSeparator());
    menu.Items.Add(settings);
    menu.Items.Add(autoStart);
    menu.Items.Add(new ToolStripSeparator());
    menu.Items.Add(exit);
    return menu;
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
      System.Windows.MessageBox.Show($"Failed to update auto-start: {ex.Message}", "ScreenTranslator");
    }
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

    var assetIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
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
        // ignore and fall back
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
