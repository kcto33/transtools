using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using ScreenTranslator.Models;
using ScreenTranslator.Services;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfDataFormats = System.Windows.DataFormats;
using WpfOrientation = System.Windows.Controls.Orientation;

namespace ScreenTranslator.Windows;

public partial class FloatingNoteWindow : Window
{
  private const string DefaultPlaceholder = "记笔记...";
  private readonly FloatingNoteStorageService _storage;
  private readonly AppSettings _settings;
  private string _notePath;
  private string _currentColor;
  private bool _isPinned = true;
  private bool _isShowingPlaceholder = true;
  private bool _isClosing;

  public event EventHandler? NewNoteRequested;
  public event EventHandler? ListRequested;

  public FloatingNoteWindow(
    AppSettings settings,
    FloatingNoteStorageService storage,
    string? notePath = null,
    string? initialRtf = null,
    FloatingNoteMetadata? metadata = null)
  {
    InitializeComponent();
    TrySetWindowIcon();

    _settings = settings;
    _settings.FloatingNotes ??= new FloatingNoteSettings();
    _storage = storage;
    _notePath = string.IsNullOrWhiteSpace(notePath) ? _storage.GenerateUniqueNewNotePath() : notePath;

    metadata ??= new FloatingNoteMetadata();
    _currentColor = string.IsNullOrWhiteSpace(metadata.Color)
      ? NormalizeColor(_settings.FloatingNotes.DefaultColor)
      : NormalizeColor(metadata.Color);
    _isPinned = metadata.IsPinned;

    Width = Math.Clamp(metadata.Width > 0 ? metadata.Width : _settings.FloatingNotes.DefaultWidth, 220, 1200);
    Height = Math.Clamp(metadata.Height > 0 ? metadata.Height : _settings.FloatingNotes.DefaultHeight, 180, 1000);
    if (!double.IsNaN(metadata.Left))
      Left = metadata.Left;
    if (!double.IsNaN(metadata.Top))
      Top = metadata.Top;

    Topmost = _isPinned;
    ApplyColor(_currentColor);
    UpdatePinnedIndicator();

    if (!string.IsNullOrWhiteSpace(initialRtf))
      LoadRtf(initialRtf);
    else
      ShowPlaceholder();

    Activated += (_, _) => SetChromeVisible(true);
    Deactivated += (_, _) => SetChromeVisible(false);
    PreviewMouseDown += OnPreviewMouseDown;
    NoteEditor.GotKeyboardFocus += (_, _) => ClearPlaceholder();

    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
    {
      NoteEditor.Focus();
      NoteEditor.CaretPosition = NoteEditor.Document.ContentEnd;
    }));
  }

  public static bool ShouldTogglePinnedForMouseButton(MouseButton button)
  {
    return button == MouseButton.Middle;
  }

  public static bool TogglePinnedState(bool current)
  {
    return !current;
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

  private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
  {
    if (!ShouldTogglePinnedForMouseButton(e.ChangedButton))
      return;

    SetPinned(TogglePinnedState(_isPinned));
    e.Handled = true;
  }

  private void SetPinned(bool pinned)
  {
    _isPinned = pinned;
    Topmost = pinned;
    UpdatePinnedIndicator();
  }

  private void UpdatePinnedIndicator()
  {
    PinnedIndicator.Visibility = _isPinned ? Visibility.Visible : Visibility.Collapsed;
  }

  private void SetChromeVisible(bool visible)
  {
    if (_isClosing)
      return;

    TitleRow.Height = visible ? new GridLength(34) : new GridLength(0);
    ToolbarRow.Height = visible ? new GridLength(36) : new GridLength(0);
    TitleBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    Toolbar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
  }

  private void ShowPlaceholder()
  {
    _isShowingPlaceholder = true;
    NoteEditor.Document.Blocks.Clear();
    NoteEditor.Document.Blocks.Add(new Paragraph(new Run(DefaultPlaceholder)
    {
      Foreground = new SolidColorBrush(WpfColor.FromRgb(138, 131, 95))
    }));
  }

  private void ClearPlaceholder()
  {
    if (!_isShowingPlaceholder)
      return;

    _isShowingPlaceholder = false;
    NoteEditor.Document.Blocks.Clear();
    NoteEditor.Document.Blocks.Add(new Paragraph());
  }

  private void LoadRtf(string rtf)
  {
    _isShowingPlaceholder = false;
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
    var range = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd);
    try
    {
      range.Load(stream, WpfDataFormats.Rtf);
    }
    catch
    {
      NoteEditor.Document.Blocks.Clear();
      NoteEditor.Document.Blocks.Add(new Paragraph(new Run(rtf)));
    }
  }

  private string GetRtf()
  {
    if (_isShowingPlaceholder)
    {
      NoteEditor.Document.Blocks.Clear();
      _isShowingPlaceholder = false;
    }

    var range = new TextRange(NoteEditor.Document.ContentStart, NoteEditor.Document.ContentEnd);
    using var stream = new MemoryStream();
    range.Save(stream, WpfDataFormats.Rtf);
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
  {
    _isClosing = true;
    SaveNote();
    base.OnClosing(e);
  }

  private void SaveNote()
  {
    try
    {
      var metadata = new FloatingNoteMetadata
      {
        Left = RestoreBounds.Left,
        Top = RestoreBounds.Top,
        Width = RestoreBounds.Width,
        Height = RestoreBounds.Height,
        IsPinned = _isPinned,
        Color = _currentColor,
      };

      _storage.SaveRtf(_notePath, GetRtf(), metadata);

      _settings.FloatingNotes.LastLeft = Left;
      _settings.FloatingNotes.LastTop = Top;
      _settings.FloatingNotes.DefaultWidth = Width;
      _settings.FloatingNotes.DefaultHeight = Height;
      _settings.FloatingNotes.DefaultColor = _currentColor;
    }
    catch
    {
      // Best effort: closing a note should not block the user.
    }
  }

  private void ApplyColor(string colorText)
  {
    var background = (WpfColor)WpfColorConverter.ConvertFromString(colorText);
    var title = Lighten(background, 0.08);
    var border = Darken(background, 0.20);
    var toolbar = Lighten(background, 0.04);

    Chrome.Background = new SolidColorBrush(background);
    Chrome.BorderBrush = new SolidColorBrush(border);
    TitleBarFill.Background = new SolidColorBrush(title);
    Toolbar.Background = new SolidColorBrush(toolbar);
    Toolbar.BorderBrush = new SolidColorBrush(Darken(background, 0.12));
    NoteEditor.Foreground = new SolidColorBrush(GetReadableTextColor(background));
  }

  private static string NormalizeColor(string? color)
  {
    if (string.IsNullOrWhiteSpace(color))
      return "#FFF7CF";

    try
    {
      _ = (WpfColor)WpfColorConverter.ConvertFromString(color);
      return color.Trim();
    }
    catch
    {
      return "#FFF7CF";
    }
  }

  private static WpfColor GetReadableTextColor(WpfColor background)
  {
    var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
    return luminance < 140 ? Colors.White : WpfColor.FromRgb(63, 58, 31);
  }

  private static WpfColor Lighten(WpfColor color, double amount)
  {
    return WpfColor.FromArgb(
      color.A,
      (byte)Math.Clamp(color.R + ((255 - color.R) * amount), 0, 255),
      (byte)Math.Clamp(color.G + ((255 - color.G) * amount), 0, 255),
      (byte)Math.Clamp(color.B + ((255 - color.B) * amount), 0, 255));
  }

  private static WpfColor Darken(WpfColor color, double amount)
  {
    return WpfColor.FromArgb(
      color.A,
      (byte)Math.Clamp(color.R * (1 - amount), 0, 255),
      (byte)Math.Clamp(color.G * (1 - amount), 0, 255),
      (byte)Math.Clamp(color.B * (1 - amount), 0, 255));
  }

  private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    DragMove();
  }

  private void NewNoteButton_Click(object sender, RoutedEventArgs e)
  {
    NewNoteRequested?.Invoke(this, EventArgs.Empty);
  }

  private void ListButton_Click(object sender, RoutedEventArgs e)
  {
    ListRequested?.Invoke(this, EventArgs.Empty);
  }

  private void MoreButton_Click(object sender, RoutedEventArgs e)
  {
    var menu = new ContextMenu();
    var colors = new[]
    {
      ("#FFF7CF", "Yellow"),
      ("#B7F0AE", "Green"),
      ("#F7B1D7", "Pink"),
      ("#D5B5F8", "Purple"),
      ("#A7DDF3", "Blue"),
      ("#D0D0D0", "Gray"),
      ("#707070", "Dark Gray"),
    };

    var colorPanel = new StackPanel
    {
      Orientation = WpfOrientation.Horizontal,
      Margin = new Thickness(8, 6, 8, 6)
    };

    foreach (var (color, name) in colors)
    {
      var button = new WpfButton
      {
        Width = 32,
        Height = 32,
        Margin = new Thickness(0),
        Padding = new Thickness(0),
        Background = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color)),
        BorderBrush = color.Equals(_currentColor, StringComparison.OrdinalIgnoreCase)
          ? WpfBrushes.Black
          : WpfBrushes.Transparent,
        BorderThickness = color.Equals(_currentColor, StringComparison.OrdinalIgnoreCase)
          ? new Thickness(2)
          : new Thickness(1),
        ToolTip = name,
      };

      button.Click += (_, _) =>
      {
        _currentColor = color;
        ApplyColor(_currentColor);
        menu.IsOpen = false;
      };

      colorPanel.Children.Add(button);
    }

    menu.Items.Add(colorPanel);

    var listItem = new MenuItem { Header = LocalizationService.GetString("FloatingNote_Menu_List", "Note List") };
    listItem.Click += (_, _) => ListRequested?.Invoke(this, EventArgs.Empty);
    menu.Items.Add(listItem);

    var pinItem = new MenuItem
    {
      Header = _isPinned
        ? LocalizationService.GetString("FloatingNote_Menu_Unpin", "Unpin")
        : LocalizationService.GetString("FloatingNote_Menu_Pin", "Pin"),
    };
    pinItem.Click += (_, _) => SetPinned(TogglePinnedState(_isPinned));
    menu.Items.Add(pinItem);

    menu.PlacementTarget = MoreButton;
    menu.IsOpen = true;
  }

  private void CloseButton_Click(object sender, RoutedEventArgs e)
  {
    Close();
  }

  private void BoldButton_Click(object sender, RoutedEventArgs e)
  {
    EditingCommands.ToggleBold.Execute(null, NoteEditor);
    NoteEditor.Focus();
  }

  private void ItalicButton_Click(object sender, RoutedEventArgs e)
  {
    EditingCommands.ToggleItalic.Execute(null, NoteEditor);
    NoteEditor.Focus();
  }

  private void UnderlineButton_Click(object sender, RoutedEventArgs e)
  {
    EditingCommands.ToggleUnderline.Execute(null, NoteEditor);
    NoteEditor.Focus();
  }

  private void StrikeButton_Click(object sender, RoutedEventArgs e)
  {
    ClearPlaceholder();
    var range = new TextRange(NoteEditor.Selection.Start, NoteEditor.Selection.End);
    var current = range.GetPropertyValue(Inline.TextDecorationsProperty);
    var hasStrike = current is TextDecorationCollection decorations &&
                    decorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough);
    range.ApplyPropertyValue(
      Inline.TextDecorationsProperty,
      hasStrike ? null : TextDecorations.Strikethrough);
    NoteEditor.Focus();
  }

  private void BulletButton_Click(object sender, RoutedEventArgs e)
  {
    EditingCommands.ToggleBullets.Execute(null, NoteEditor);
    NoteEditor.Focus();
  }

  private void NoteEditor_SelectionChanged(object sender, RoutedEventArgs e)
  {
    BoldButton.IsChecked = IsSelectionProperty(TextElement.FontWeightProperty, FontWeights.Bold);
    ItalicButton.IsChecked = IsSelectionProperty(TextElement.FontStyleProperty, FontStyles.Italic);
    UnderlineButton.IsChecked = NoteEditor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) == TextDecorations.Underline;
  }

  private bool IsSelectionProperty(DependencyProperty property, object expected)
  {
    var value = NoteEditor.Selection.GetPropertyValue(property);
    return value != DependencyProperty.UnsetValue && value.Equals(expected);
  }
}
