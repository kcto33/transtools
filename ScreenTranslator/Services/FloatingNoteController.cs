using System.Windows;

using ScreenTranslator.Models;
using ScreenTranslator.Windows;
using WpfApplication = System.Windows.Application;

namespace ScreenTranslator.Services;

public sealed class FloatingNoteController : IDisposable
{
  private readonly AppSettings _settings;
  private readonly SettingsService _settingsService;
  private readonly FloatingNoteStorageService _storage;
  private readonly List<FloatingNoteWindow> _windows = [];
  private FloatingNoteListWindow? _listWindow;

  public FloatingNoteController(SettingsService settingsService)
  {
    _settingsService = settingsService;
    _settings = settingsService.Settings;
    _settings.FloatingNotes ??= new FloatingNoteSettings();
    _storage = new FloatingNoteStorageService(_settings);
  }

  public void CreateNewNote()
  {
    WpfApplication.Current.Dispatcher.Invoke(() =>
    {
      var window = CreateWindow(null, null, new FloatingNoteMetadata
      {
        Width = _settings.FloatingNotes.DefaultWidth,
        Height = _settings.FloatingNotes.DefaultHeight,
        Left = ResolveNextLeft(),
        Top = ResolveNextTop(),
        IsPinned = true,
        Color = _settings.FloatingNotes.DefaultColor,
      });

      window.Show();
      window.Activate();
    });
  }

  public void ShowList()
  {
    WpfApplication.Current.Dispatcher.Invoke(() =>
    {
      if (_listWindow is null)
      {
        _listWindow = new FloatingNoteListWindow(_storage);
        _listWindow.OpenNoteRequested += (_, path) => OpenSavedNote(path);
        _listWindow.Closed += (_, _) => _listWindow = null;
        _listWindow.Show();
      }
      else
      {
        _listWindow.Refresh();
        _listWindow.Activate();
      }
    });
  }

  private void OpenSavedNote(string path)
  {
    WpfApplication.Current.Dispatcher.Invoke(() =>
    {
      var rtf = _storage.LoadRtf(path);
      var metadata = _storage.LoadMetadata(path);
      var window = CreateWindow(path, rtf, metadata);
      window.Show();
      window.Activate();
    });
  }

  private FloatingNoteWindow CreateWindow(string? path, string? rtf, FloatingNoteMetadata metadata)
  {
    var window = new FloatingNoteWindow(_settings, _storage, path, rtf, metadata);
    window.NewNoteRequested += (_, _) => CreateNewNote();
    window.ListRequested += (_, _) => ShowList();
    window.Closed += (_, _) =>
    {
      _windows.Remove(window);
      _settingsService.Save();
      _listWindow?.Refresh();
    };
    _windows.Add(window);
    return window;
  }

  public void CloseAll()
  {
    _listWindow?.Close();
    _listWindow = null;

    foreach (var window in _windows.ToList())
    {
      window.Close();
    }

    _windows.Clear();
  }

  public void Dispose()
  {
    CloseAll();
  }

  private double ResolveNextLeft()
  {
    if (!double.IsNaN(_settings.FloatingNotes.LastLeft))
      return _settings.FloatingNotes.LastLeft + (_windows.Count * 24);

    return Math.Max(24, SystemParameters.WorkArea.Left + 80 + (_windows.Count * 24));
  }

  private double ResolveNextTop()
  {
    if (!double.IsNaN(_settings.FloatingNotes.LastTop))
      return _settings.FloatingNotes.LastTop + (_windows.Count * 24);

    return Math.Max(24, SystemParameters.WorkArea.Top + 80 + (_windows.Count * 24));
  }
}
