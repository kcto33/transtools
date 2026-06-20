using System.Windows;
using System.Windows.Input;

using ScreenTranslator.Services;

namespace ScreenTranslator.Windows;

public partial class FloatingNoteListWindow : Window
{
  private readonly FloatingNoteStorageService _storage;

  public event EventHandler<string>? OpenNoteRequested;

  public FloatingNoteListWindow(FloatingNoteStorageService storage)
  {
    InitializeComponent();
    TrySetWindowIcon();
    _storage = storage;
    Refresh();
  }

  private void TrySetWindowIcon()
  {
    try
    {
      var iconUri = new Uri("pack://application:,,,/transtools;component/Assets/tray.ico", UriKind.Absolute);
      Icon = new System.Windows.Media.Imaging.BitmapImage(iconUri);
    }
    catch
    {
      // Icon may be unavailable in some publish modes.
    }
  }

  public void Refresh()
  {
    NotesList.ItemsSource = _storage.ListSavedNotes();
  }

  private void OpenSelectedNote()
  {
    if (NotesList.SelectedItem is not FloatingNoteFileInfo note)
      return;

    OpenNoteRequested?.Invoke(this, note.Path);
  }

  private void NotesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
  {
    OpenSelectedNote();
  }

  private void RefreshButton_Click(object sender, RoutedEventArgs e)
  {
    Refresh();
  }

  private void OpenButton_Click(object sender, RoutedEventArgs e)
  {
    OpenSelectedNote();
  }

  private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    DragMove();
  }

  private void MinimizeButton_Click(object sender, RoutedEventArgs e)
  {
    WindowState = WindowState.Minimized;
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }
}
