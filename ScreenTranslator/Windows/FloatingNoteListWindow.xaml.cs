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
    var notes = _storage.ListSavedNotes();
    NotesList.ItemsSource = notes;
    EmptyText.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
  }

  private void OpenSelectedNote()
  {
    if (NotesList.SelectedItem is not FloatingNoteFileInfo note)
      return;

    OpenNoteRequested?.Invoke(this, note.Path);
  }

  private void DeleteSelectedNote()
  {
    if (NotesList.SelectedItem is not FloatingNoteFileInfo note)
      return;

    var message = string.Format(
      Services.LocalizationService.GetString("FloatingNoteList_DeleteConfirm", "Delete note \"{0}\"?"),
      note.FileName);
    var result = System.Windows.MessageBox.Show(
      message,
      Services.LocalizationService.GetString("FloatingNoteList_Title", "Note List"),
      MessageBoxButton.YesNo,
      MessageBoxImage.Warning);

    if (result != MessageBoxResult.Yes)
      return;

    if (!_storage.DeleteSavedNote(note.Path))
    {
      System.Windows.MessageBox.Show(
        Services.LocalizationService.GetString("FloatingNoteList_DeleteFailed", "Failed to delete the selected note."),
        Services.LocalizationService.GetString("FloatingNoteList_Title", "Note List"),
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    }

    Refresh();
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

  private void DeleteButton_Click(object sender, RoutedEventArgs e)
  {
    DeleteSelectedNote();
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
