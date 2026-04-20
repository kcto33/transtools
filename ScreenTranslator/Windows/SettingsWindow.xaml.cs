using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScreenTranslator.Models;
using ScreenTranslator.Services;
using WpfColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WinFormsColorDialog = System.Windows.Forms.ColorDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;

namespace ScreenTranslator.Windows;

public partial class SettingsWindow : Window
{
  private readonly SettingsService _settings;
  private readonly AutoStartService _autoStart = new();
  private readonly Func<string, string?>? _applyHotkey;
  private readonly Func<string, string?>? _applyPasteHistoryHotkey;
  private readonly Func<string, string?>? _applyScreenshotHotkey;
  private readonly Action<int>? _updateClipboardHistoryMaxItems;
  private readonly Action? _suspendHotkeys;
  private readonly Action? _resumeHotkeys;
  private bool _clearKeyRequested;
  private bool _clearYoudaoSecretRequested;
  private string? _existingKeyProtected;
  private string? _existingYoudaoSecretProtected;

  private readonly List<LanguageChoice> _fromLanguages =
  [
    new("auto", "Auto"),
    new("en", "English"),
    new("zh-Hans", "Chinese (Simplified)"),
    new("zh-Hant", "Chinese (Traditional)"),
    new("ja", "Japanese"),
    new("ko", "Korean"),
    new("ru", "Russian"),
    new("th", "Thai"),
    new("vi", "Vietnamese"),
  ];

  private readonly List<LanguageChoice> _toLanguages =
  [
    new("en", "English"),
    new("zh-Hans", "Chinese (Simplified)"),
    new("zh-Hant", "Chinese (Traditional)"),
    new("ja", "Japanese"),
    new("ko", "Korean"),
    new("ru", "Russian"),
    new("th", "Thai"),
    new("vi", "Vietnamese"),
  ];

  private readonly List<ProviderChoice> _providers =
  [
    new("mock", "Mock (offline)"),
    new("youdao", "Youdao"),
    new("deepl", "DeepL"),
    new("google", "Google Translate"),
  ];

  private readonly List<string> _fontFamilies =
  [
    "Segoe UI",
    "Microsoft YaHei",
    "Microsoft YaHei UI",
    "SimSun",
    "SimHei",
    "KaiTi",
    "FangSong",
    "Arial",
    "Consolas",
    "Courier New",
  ];

  public SettingsWindow(
    SettingsService settings,
    Func<string, string?>? applyHotkey = null,
    Func<string, string?>? applyPasteHistoryHotkey = null,
    Func<string, string?>? applyScreenshotHotkey = null,
    Action<int>? updateClipboardHistoryMaxItems = null,
    Action? suspendHotkeys = null,
    Action? resumeHotkeys = null)
  {
    InitializeComponent();
    TrySetWindowIcon();
    _settings = settings;
    _applyHotkey = applyHotkey;
    _applyPasteHistoryHotkey = applyPasteHistoryHotkey;
    _applyScreenshotHotkey = applyScreenshotHotkey;
    _updateClipboardHistoryMaxItems = updateClipboardHistoryMaxItems;
    _suspendHotkeys = suspendHotkeys;
    _resumeHotkeys = resumeHotkeys;

    InitializeUILanguageControls();
    InitializeProviderControls();
    InitializeLanguageControls();
    InitializeBubbleControls();
    InitializeHotkeyCapture();
    InitializeEventHandlers();

    LoadFromSettings();
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
      // Icon not available in single-file publish, ignore
    }
  }

  private void InitializeUILanguageControls()
  {
    LanguageCombo.ItemsSource = LocalizationService.SupportedLanguages;
    LanguageCombo.DisplayMemberPath = nameof(LanguageInfo.DisplayName);
    LanguageCombo.SelectedValuePath = nameof(LanguageInfo.Code);
  }

  private void InitializeProviderControls()
  {
    ProviderCombo.ItemsSource = _providers;
    ProviderCombo.DisplayMemberPath = nameof(ProviderChoice.Name);
    ProviderCombo.SelectedValuePath = nameof(ProviderChoice.Id);
    ProviderCombo.SelectionChanged += (_, _) => LoadProviderFields();
  }

  private void InitializeLanguageControls()
  {
    FromLangCombo.ItemsSource = _fromLanguages;
    FromLangCombo.DisplayMemberPath = nameof(LanguageChoice.Name);
    FromLangCombo.SelectedValuePath = nameof(LanguageChoice.Id);

    ToLangCombo.ItemsSource = _toLanguages;
    ToLangCombo.DisplayMemberPath = nameof(LanguageChoice.Name);
    ToLangCombo.SelectedValuePath = nameof(LanguageChoice.Id);
  }

  private void InitializeHotkeyCapture()
  {
    // Setup hotkey capture for all hotkey textboxes
    SetupHotkeyTextBox(HotkeyText);
    SetupHotkeyTextBox(PasteHistoryHotkeyText);
    SetupHotkeyTextBox(ScreenshotHotkeyText);
  }

  private void SetupHotkeyTextBox(System.Windows.Controls.TextBox textBox)
  {
    var placeholder = LocalizationService.GetString("Settings_HotkeyPlaceholder", "Press key combination...");
    
    textBox.GotFocus += (_, _) =>
    {
      if (!textBox.Text.StartsWith("..."))
      {
        textBox.Tag = textBox.Text; // Store current value
        textBox.Text = placeholder;
      }
      // Suspend global hotkeys while editing
      _suspendHotkeys?.Invoke();
    };

    textBox.LostFocus += (_, _) =>
    {
      _resumeHotkeys?.Invoke();
      if (textBox.Text == placeholder && textBox.Tag is string oldValue)
      {
        textBox.Text = oldValue;
      }
    };

    textBox.PreviewKeyDown += (_, e) =>
    {
      e.Handled = true;

      // Get modifiers
      var modifiers = Keyboard.Modifiers;
      var key = e.Key == Key.System ? e.SystemKey : e.Key;

      // Ignore modifier-only keys
      if (key == Key.LeftCtrl || key == Key.RightCtrl ||
          key == Key.LeftAlt || key == Key.RightAlt ||
          key == Key.LeftShift || key == Key.RightShift ||
          key == Key.LWin || key == Key.RWin ||
          key == Key.System)
      {
        return;
      }

      // ESC to cancel
      if (key == Key.Escape)
      {
        if (textBox.Tag is string oldValue)
        {
          textBox.Text = oldValue;
        }
        Keyboard.ClearFocus();
        return;
      }

      // Build hotkey string
      var parts = new List<string>();
      
      if ((modifiers & ModifierKeys.Control) != 0)
        parts.Add("Ctrl");
      if ((modifiers & ModifierKeys.Alt) != 0)
        parts.Add("Alt");
      if ((modifiers & ModifierKeys.Shift) != 0)
        parts.Add("Shift");
      if ((modifiers & ModifierKeys.Windows) != 0)
        parts.Add("Win");

      // Require at least one modifier
      if (parts.Count == 0)
      {
        return;
      }

      // Convert key to string
      var keyStr = KeyToString(key);
      if (string.IsNullOrEmpty(keyStr))
      {
        return;
      }

      parts.Add(keyStr);
      var newHotkey = string.Join("+", parts);

      // Check for conflicts with other hotkeys in this app
      var conflictWith = CheckInternalHotkeyConflict(textBox, newHotkey);
      if (!string.IsNullOrEmpty(conflictWith))
      {
        System.Windows.MessageBox.Show(
          string.Format(LocalizationService.GetString("Msg_HotkeyConflictInternal", "This hotkey conflicts with: {0}"), conflictWith),
          "transtools",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }

      // Check for conflicts with other applications
      if (!HotkeyService.TestHotkeyAvailable(newHotkey, out var error))
      {
        System.Windows.MessageBox.Show(
          error,
          "transtools",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }

      textBox.Text = newHotkey;
      textBox.Tag = textBox.Text;
      
      // Move focus away
      Keyboard.ClearFocus();
    };
  }

  private string? CheckInternalHotkeyConflict(System.Windows.Controls.TextBox currentTextBox, string newHotkey)
  {
    var normalizedNew = newHotkey.ToLowerInvariant().Replace(" ", "");

    if (currentTextBox != HotkeyText)
    {
      var existing = (HotkeyText.Text ?? "").ToLowerInvariant().Replace(" ", "");
      if (existing == normalizedNew)
        return LocalizationService.GetString("Settings_HotkeyTranslate", "Translate");
    }

    if (currentTextBox != PasteHistoryHotkeyText)
    {
      var existing = (PasteHistoryHotkeyText.Text ?? "").ToLowerInvariant().Replace(" ", "");
      if (existing == normalizedNew)
        return LocalizationService.GetString("Settings_HotkeyPasteHistory", "Paste History");
    }

    if (currentTextBox != ScreenshotHotkeyText)
    {
      var existing = (ScreenshotHotkeyText.Text ?? "").ToLowerInvariant().Replace(" ", "");
      if (existing == normalizedNew)
        return LocalizationService.GetString("Settings_HotkeyScreenshot", "Screenshot");
    }

    return null;
  }

  private static string KeyToString(Key key)
  {
    return key switch
    {
      // Letters
      >= Key.A and <= Key.Z => key.ToString(),
      
      // Numbers
      >= Key.D0 and <= Key.D9 => key.ToString().Substring(1),
      >= Key.NumPad0 and <= Key.NumPad9 => "Num" + key.ToString().Substring(6),
      
      // Function keys
      >= Key.F1 and <= Key.F24 => key.ToString(),
      
      // Special keys
      Key.Space => "Space",
      Key.Enter or Key.Return => "Enter",
      Key.Tab => "Tab",
      Key.Back => "Backspace",
      Key.Delete => "Delete",
      Key.Insert => "Insert",
      Key.Home => "Home",
      Key.End => "End",
      Key.PageUp => "PageUp",
      Key.PageDown => "PageDown",
      Key.Up => "Up",
      Key.Down => "Down",
      Key.Left => "Left",
      Key.Right => "Right",
      Key.PrintScreen => "PrintScreen",
      Key.Pause => "Pause",
      Key.Scroll => "ScrollLock",
      Key.NumLock => "NumLock",
      Key.CapsLock => "CapsLock",
      
      // Punctuation
      Key.OemTilde => "`",
      Key.OemMinus => "-",
      Key.OemPlus => "=",
      Key.OemOpenBrackets => "[",
      Key.OemCloseBrackets => "]",
      Key.OemPipe => "\\",
      Key.OemSemicolon => ";",
      Key.OemQuotes => "'",
      Key.OemComma => ",",
      Key.OemPeriod => ".",
      Key.OemQuestion => "/",
      
      // Numpad
      Key.Multiply => "Num*",
      Key.Add => "Num+",
      Key.Subtract => "Num-",
      Key.Divide => "Num/",
      Key.Decimal => "Num.",
      
      _ => string.Empty
    };
  }

  private void InitializeBubbleControls()
  {
    // Font family combo
    FontFamilyCombo.ItemsSource = _fontFamilies;

    // Slider value changed handlers
    FontSizeSlider.ValueChanged += (_, _) => UpdateBubblePreview();
    CornerRadiusSlider.ValueChanged += (_, _) => UpdateBubblePreview();
    PaddingSlider.ValueChanged += (_, _) => UpdateBubblePreview();
    MaxWidthSlider.ValueChanged += (_, _) => UpdateBubblePreview();

    // Color text changed handlers
    BgColorText.TextChanged += (_, _) => UpdateColorPreview(BgColorText, BgColorPreview);
    TextColorText.TextChanged += (_, _) => UpdateColorPreview(TextColorText, TextColorPreview);
    BorderColorText.TextChanged += (_, _) => UpdateColorPreview(BorderColorText, BorderColorPreview);

    BgColorText.LostFocus += (_, _) => UpdateBubblePreview();
    TextColorText.LostFocus += (_, _) => UpdateBubblePreview();
    BorderColorText.LostFocus += (_, _) => UpdateBubblePreview();
    FontFamilyCombo.SelectionChanged += (_, _) => UpdateBubblePreview();

    // Color preview click to open color picker
    BgColorPreview.MouseLeftButtonDown += (_, _) => ShowColorPicker(BgColorText, BgColorPreview, UpdateBubblePreview);
    TextColorPreview.MouseLeftButtonDown += (_, _) => ShowColorPicker(TextColorText, TextColorPreview, UpdateBubblePreview);
    BorderColorPreview.MouseLeftButtonDown += (_, _) => ShowColorPicker(BorderColorText, BorderColorPreview, UpdateBubblePreview);

    // Paste history bubble controls
    PH_FontFamilyCombo.ItemsSource = _fontFamilies;

    PH_FontSizeSlider.ValueChanged += (_, _) => UpdatePasteHistoryBubblePreview();
    PH_CornerRadiusSlider.ValueChanged += (_, _) => UpdatePasteHistoryBubblePreview();
    PH_PaddingSlider.ValueChanged += (_, _) => UpdatePasteHistoryBubblePreview();
    PH_MaxWidthSlider.ValueChanged += (_, _) => UpdatePasteHistoryBubblePreview();

    PH_BgColorText.TextChanged += (_, _) => UpdateColorPreview(PH_BgColorText, PH_BgColorPreview);
    PH_TextColorText.TextChanged += (_, _) => UpdateColorPreview(PH_TextColorText, PH_TextColorPreview);
    PH_BorderColorText.TextChanged += (_, _) => UpdateColorPreview(PH_BorderColorText, PH_BorderColorPreview);

    PH_BgColorText.LostFocus += (_, _) => UpdatePasteHistoryBubblePreview();
    PH_TextColorText.LostFocus += (_, _) => UpdatePasteHistoryBubblePreview();
    PH_BorderColorText.LostFocus += (_, _) => UpdatePasteHistoryBubblePreview();
    PH_FontFamilyCombo.SelectionChanged += (_, _) => UpdatePasteHistoryBubblePreview();

    PH_BgColorPreview.MouseLeftButtonDown += (_, _) => ShowColorPicker(PH_BgColorText, PH_BgColorPreview, UpdatePasteHistoryBubblePreview);
    PH_TextColorPreview.MouseLeftButtonDown += (_, _) => ShowColorPicker(PH_TextColorText, PH_TextColorPreview, UpdatePasteHistoryBubblePreview);
    PH_BorderColorPreview.MouseLeftButtonDown += (_, _) => ShowColorPicker(PH_BorderColorText, PH_BorderColorPreview, UpdatePasteHistoryBubblePreview);
  }

  private void InitializeEventHandlers()
  {
    // Clipboard history slider handler
    ClipboardHistorySlider.ValueChanged += (_, _) => UpdateClipboardHistoryLabel();
    LongWheelNotchesSlider.ValueChanged += (_, _) => UpdateLongScreenshotLabels();
    LongFrameIntervalSlider.ValueChanged += (_, _) => UpdateLongScreenshotLabels();
    LongMaxFramesSlider.ValueChanged += (_, _) => UpdateLongScreenshotLabels();
    LongMaxHeightSlider.ValueChanged += (_, _) => UpdateLongScreenshotLabels();
    LongNoChangeThresholdSlider.ValueChanged += (_, _) => UpdateLongScreenshotLabels();
    LongNoChangeCountSlider.ValueChanged += (_, _) => UpdateLongScreenshotLabels();

    // API Key handlers
    ShowKeyCheck.Checked += (_, _) => SetKeyVisibility(true);
    ShowKeyCheck.Unchecked += (_, _) => SetKeyVisibility(false);
    KeyPassword.PasswordChanged += (_, _) => _clearKeyRequested = false;
    KeyText.TextChanged += (_, _) => _clearKeyRequested = false;
    ClearKeyButton.Click += (_, _) =>
    {
      KeyPassword.Password = string.Empty;
      KeyText.Text = string.Empty;
      _clearKeyRequested = true;
    };

    // Youdao secret handlers
    YoudaoShowSecretCheck.Checked += (_, _) => SetYoudaoSecretVisibility(true);
    YoudaoShowSecretCheck.Unchecked += (_, _) => SetYoudaoSecretVisibility(false);
    YoudaoSecretPassword.PasswordChanged += (_, _) => _clearYoudaoSecretRequested = false;
    YoudaoSecretText.TextChanged += (_, _) => _clearYoudaoSecretRequested = false;
    YoudaoClearSecretButton.Click += (_, _) =>
    {
      YoudaoSecretPassword.Password = string.Empty;
      YoudaoSecretText.Text = string.Empty;
      _clearYoudaoSecretRequested = true;
      YoudaoSecretHint.Visibility = Visibility.Collapsed;
    };

    // Button handlers
    SaveButton.Click += (_, _) => Save();
    ResetBubbleButton.Click += (_, _) => ResetBubbleSettings();
    ResetPasteHistoryBubbleButton.Click += (_, _) => ResetPasteHistoryBubbleSettings();

    // Screenshot save path browse button
    BrowseSavePathButton.Click += (_, _) => BrowseSavePath();
  }

  private void BrowseSavePath()
  {
    var dialog = new System.Windows.Forms.FolderBrowserDialog
    {
      Description = LocalizationService.GetString("Settings_SelectSavePath", "Select screenshot save folder"),
      ShowNewFolderButton = true
    };

    if (!string.IsNullOrWhiteSpace(ScreenshotSavePathText.Text) &&
        System.IO.Directory.Exists(ScreenshotSavePathText.Text))
    {
      dialog.SelectedPath = ScreenshotSavePathText.Text;
    }
    else
    {
      dialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
    {
      ScreenshotSavePathText.Text = dialog.SelectedPath;
    }
  }

  private void LoadFromSettings()
  {
    // General settings
    AutoStartCheck.IsChecked = _autoStart.IsEnabled();
    HotkeyText.Text = string.IsNullOrWhiteSpace(_settings.Settings.Hotkey) ? "Ctrl+Alt+T" : _settings.Settings.Hotkey;
    PasteHistoryHotkeyText.Text = string.IsNullOrWhiteSpace(_settings.Settings.PasteHistoryHotkey)
      ? "Ctrl+Shift+V"
      : _settings.Settings.PasteHistoryHotkey;
    ScreenshotHotkeyText.Text = string.IsNullOrWhiteSpace(_settings.Settings.ScreenshotHotkey)
      ? "Ctrl+Alt+S"
      : _settings.Settings.ScreenshotHotkey;
    ClipboardHistorySlider.Value = Math.Clamp(_settings.Settings.ClipboardHistoryMaxItems, 1, 20);
    UpdateClipboardHistoryLabel();
    FromLangCombo.SelectedValue = string.IsNullOrWhiteSpace(_settings.Settings.DefaultFrom) ? "auto" : _settings.Settings.DefaultFrom;
    ToLangCombo.SelectedValue = string.IsNullOrWhiteSpace(_settings.Settings.DefaultTo) ? "zh-Hans" : _settings.Settings.DefaultTo;

    // UI Language
    var currentLang = string.IsNullOrWhiteSpace(_settings.Settings.Language)
      ? LocalizationService.Instance.CurrentLanguage
      : _settings.Settings.Language;
    LanguageCombo.SelectedValue = currentLang;

    // Screenshot settings
    ScreenshotAutoCopyCheck.IsChecked = _settings.Settings.ScreenshotAutoCopy;
    ScreenshotAutoSaveCheck.IsChecked = _settings.Settings.ScreenshotAutoSave;
    ScreenshotSavePathText.Text = _settings.Settings.ScreenshotSavePath ?? "";
    ScreenshotFileNameFormatText.Text = string.IsNullOrWhiteSpace(_settings.Settings.ScreenshotFileNameFormat)
      ? "Screenshot_{0:yyyyMMdd_HHmmss}"
      : _settings.Settings.ScreenshotFileNameFormat;

    _settings.Settings.LongScreenshot ??= new LongScreenshotSettings();
    var longSettings = _settings.Settings.LongScreenshot;
    LongWheelNotchesSlider.Value = Math.Clamp(longSettings.WheelNotchesPerStep, 1, 12);
    LongFrameIntervalSlider.Value = Math.Clamp(longSettings.FrameIntervalMs, 100, 2000);
    LongMaxFramesSlider.Value = Math.Clamp(longSettings.MaxFrames, 20, 500);
    LongMaxHeightSlider.Value = Math.Clamp(longSettings.MaxTotalHeightPx, 5000, 200000);
    LongNoChangeThresholdSlider.Value = Math.Clamp(longSettings.NoChangeDiffThresholdPercent, 0.1, 10.0);
    LongNoChangeCountSlider.Value = Math.Clamp(longSettings.NoChangeConsecutiveCount, 2, 10);
    UpdateLongScreenshotLabels();

    // Provider settings
    ProviderCombo.SelectedValue = _settings.Settings.ActiveProviderId;
    LoadProviderFields();

    // Bubble settings
    LoadBubbleSettings();

    // Paste history bubble settings
    LoadPasteHistoryBubbleSettings();
  }

  private void LoadBubbleSettings()
  {
    var bubble = _settings.Settings.Bubble ?? new BubbleSettings();

    BgColorText.Text = bubble.BackgroundColor;
    TextColorText.Text = bubble.TextColor;
    BorderColorText.Text = bubble.BorderColor;

    var fontIndex = _fontFamilies.FindIndex(f => f.Equals(bubble.FontFamily, StringComparison.OrdinalIgnoreCase));
    FontFamilyCombo.SelectedIndex = fontIndex >= 0 ? fontIndex : 0;

    FontSizeSlider.Value = bubble.FontSize;
    CornerRadiusSlider.Value = bubble.CornerRadius;
    PaddingSlider.Value = bubble.Padding;
    MaxWidthSlider.Value = bubble.MaxWidthRatio;

    UpdateColorPreview(BgColorText, BgColorPreview);
    UpdateColorPreview(TextColorText, TextColorPreview);
    UpdateColorPreview(BorderColorText, BorderColorPreview);
    UpdateBubblePreview();
  }

  private void UpdateColorPreview(System.Windows.Controls.TextBox textBox, System.Windows.Controls.Border preview)
  {
    try
    {
      var color = (WpfColor)WpfColorConverter.ConvertFromString(textBox.Text);
      preview.Background = new System.Windows.Media.SolidColorBrush(color);
    }
    catch
    {
      preview.Background = WpfBrushes.Transparent;
    }
  }

  private void ShowColorPicker(System.Windows.Controls.TextBox textBox, System.Windows.Controls.Border preview, Action updatePreview)
  {
    using var dialog = new WinFormsColorDialog
    {
      FullOpen = true,
      AnyColor = true
    };

    // Try to set initial color from current text
    try
    {
      var currentColor = (WpfColor)WpfColorConverter.ConvertFromString(textBox.Text);
      dialog.Color = System.Drawing.Color.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B);
    }
    catch
    {
      // Ignore if current color is invalid
    }

    if (dialog.ShowDialog() == WinFormsDialogResult.OK)
    {
      var selectedColor = dialog.Color;
      // Format as #AARRGGBB if alpha != 255, otherwise #RRGGBB
      if (selectedColor.A == 255)
      {
        textBox.Text = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
      }
      else
      {
        textBox.Text = $"#{selectedColor.A:X2}{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
      }
      UpdateColorPreview(textBox, preview);
      updatePreview();
    }
  }

  private void UpdateBubblePreview()
  {
    // Update labels
    FontSizeLabel.Text = $"{FontSizeSlider.Value:F0} px";
    CornerRadiusLabel.Text = $"{CornerRadiusSlider.Value:F0} px";
    PaddingLabel.Text = $"{PaddingSlider.Value:F0} px";
    MaxWidthLabel.Text = $"{MaxWidthSlider.Value:P0}";

    // Update preview
    try
    {
      var bgColor = (WpfColor)WpfColorConverter.ConvertFromString(BgColorText.Text);
      var textColor = (WpfColor)WpfColorConverter.ConvertFromString(TextColorText.Text);
      var borderColor = (WpfColor)WpfColorConverter.ConvertFromString(BorderColorText.Text);

      BubblePreview.Background = new System.Windows.Media.SolidColorBrush(bgColor);
      BubblePreview.BorderBrush = new System.Windows.Media.SolidColorBrush(borderColor);
      BubblePreview.BorderThickness = new Thickness(1);
      BubblePreview.CornerRadius = new CornerRadius(CornerRadiusSlider.Value);
      BubblePreview.Padding = new Thickness(PaddingSlider.Value);

      BubblePreviewText.Foreground = new System.Windows.Media.SolidColorBrush(textColor);
      BubblePreviewText.FontSize = FontSizeSlider.Value;

      if (FontFamilyCombo.SelectedItem is string fontFamily)
      {
        BubblePreviewText.FontFamily = new WpfFontFamily(fontFamily);
      }
    }
    catch
    {
      // Ignore invalid color values
    }
  }

  private void ResetBubbleSettings()
  {
    var defaults = new BubbleSettings();
    BgColorText.Text = defaults.BackgroundColor;
    TextColorText.Text = defaults.TextColor;
    BorderColorText.Text = defaults.BorderColor;

    var fontIndex = _fontFamilies.FindIndex(f => f.Equals(defaults.FontFamily, StringComparison.OrdinalIgnoreCase));
    FontFamilyCombo.SelectedIndex = fontIndex >= 0 ? fontIndex : 0;

    FontSizeSlider.Value = defaults.FontSize;
    CornerRadiusSlider.Value = defaults.CornerRadius;
    PaddingSlider.Value = defaults.Padding;
    MaxWidthSlider.Value = defaults.MaxWidthRatio;

    UpdateColorPreview(BgColorText, BgColorPreview);
    UpdateColorPreview(TextColorText, TextColorPreview);
    UpdateColorPreview(BorderColorText, BorderColorPreview);
    UpdateBubblePreview();
  }

  private void LoadPasteHistoryBubbleSettings()
  {
    var bubble = _settings.Settings.PasteHistoryBubble ?? new BubbleSettings();

    PH_BgColorText.Text = bubble.BackgroundColor;
    PH_TextColorText.Text = bubble.TextColor;
    PH_BorderColorText.Text = bubble.BorderColor;

    var fontIndex = _fontFamilies.FindIndex(f => f.Equals(bubble.FontFamily, StringComparison.OrdinalIgnoreCase));
    PH_FontFamilyCombo.SelectedIndex = fontIndex >= 0 ? fontIndex : 0;

    PH_FontSizeSlider.Value = bubble.FontSize;
    PH_CornerRadiusSlider.Value = bubble.CornerRadius;
    PH_PaddingSlider.Value = bubble.Padding;
    PH_MaxWidthSlider.Value = bubble.MaxWidthRatio;

    UpdateColorPreview(PH_BgColorText, PH_BgColorPreview);
    UpdateColorPreview(PH_TextColorText, PH_TextColorPreview);
    UpdateColorPreview(PH_BorderColorText, PH_BorderColorPreview);
    UpdatePasteHistoryBubblePreview();
  }

  private void UpdatePasteHistoryBubblePreview()
  {
    // Update labels
    PH_FontSizeLabel.Text = $"{PH_FontSizeSlider.Value:F0} px";
    PH_CornerRadiusLabel.Text = $"{PH_CornerRadiusSlider.Value:F0} px";
    PH_PaddingLabel.Text = $"{PH_PaddingSlider.Value:F0} px";
    PH_MaxWidthLabel.Text = $"{PH_MaxWidthSlider.Value:P0}";

    // Update preview
    try
    {
      var bgColor = (WpfColor)WpfColorConverter.ConvertFromString(PH_BgColorText.Text);
      var textColor = (WpfColor)WpfColorConverter.ConvertFromString(PH_TextColorText.Text);
      var borderColor = (WpfColor)WpfColorConverter.ConvertFromString(PH_BorderColorText.Text);

      PH_BubblePreview.Background = new System.Windows.Media.SolidColorBrush(bgColor);
      PH_BubblePreview.BorderBrush = new System.Windows.Media.SolidColorBrush(borderColor);
      PH_BubblePreview.BorderThickness = new Thickness(1);
      PH_BubblePreview.CornerRadius = new CornerRadius(PH_CornerRadiusSlider.Value);
      PH_BubblePreview.Padding = new Thickness(PH_PaddingSlider.Value);

      PH_BubblePreviewText.Foreground = new System.Windows.Media.SolidColorBrush(textColor);
      PH_BubblePreviewText.FontSize = PH_FontSizeSlider.Value;

      if (PH_FontFamilyCombo.SelectedItem is string fontFamily)
      {
        PH_BubblePreviewText.FontFamily = new WpfFontFamily(fontFamily);
      }
    }
    catch
    {
      // Ignore invalid color values
    }
  }

  private void ResetPasteHistoryBubbleSettings()
  {
    var defaults = new BubbleSettings();
    PH_BgColorText.Text = defaults.BackgroundColor;
    PH_TextColorText.Text = defaults.TextColor;
    PH_BorderColorText.Text = defaults.BorderColor;

    var fontIndex = _fontFamilies.FindIndex(f => f.Equals(defaults.FontFamily, StringComparison.OrdinalIgnoreCase));
    PH_FontFamilyCombo.SelectedIndex = fontIndex >= 0 ? fontIndex : 0;

    PH_FontSizeSlider.Value = defaults.FontSize;
    PH_CornerRadiusSlider.Value = defaults.CornerRadius;
    PH_PaddingSlider.Value = defaults.Padding;
    PH_MaxWidthSlider.Value = defaults.MaxWidthRatio;

    UpdateColorPreview(PH_BgColorText, PH_BgColorPreview);
    UpdateColorPreview(PH_TextColorText, PH_TextColorPreview);
    UpdateColorPreview(PH_BorderColorText, PH_BorderColorPreview);
    UpdatePasteHistoryBubblePreview();
  }

  private void LoadProviderFields()
  {
    var providerId = (ProviderCombo.SelectedValue as string) ?? _settings.Settings.ActiveProviderId;
    if (!_settings.Settings.Providers.TryGetValue(providerId, out var ps))
      ps = new ProviderSettings();

    EndpointText.Text = ps.Endpoint ?? string.Empty;
    RegionText.Text = ps.Region ?? string.Empty;

    YoudaoAppIdText.Text = ps.AppId ?? string.Empty;

    // Do not auto-fill the key; user can paste/update.
    KeyPassword.Password = string.Empty;
    KeyText.Text = string.Empty;

    YoudaoSecretPassword.Password = string.Empty;
    YoudaoSecretText.Text = string.Empty;

    _existingKeyProtected = ps.KeyProtected;
    _existingYoudaoSecretProtected = ps.AppSecretProtected;
    YoudaoSecretHint.Visibility = string.IsNullOrWhiteSpace(ps.AppSecretProtected) ? Visibility.Collapsed : Visibility.Visible;

    ApplyProviderVisibility(providerId);
  }

  private void Save()
  {
    // Validate hotkey
    var hotkeyValue = (HotkeyText.Text ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(hotkeyValue))
      hotkeyValue = "Ctrl+Alt+T";

    var pasteHotkeyValue = (PasteHistoryHotkeyText.Text ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(pasteHotkeyValue))
      pasteHotkeyValue = "Ctrl+Shift+V";

    var screenshotHotkeyValue = (ScreenshotHotkeyText.Text ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(screenshotHotkeyValue))
      screenshotHotkeyValue = "Ctrl+Alt+S";

    // Check for hotkey conflicts
    var hotkeys = new[] { hotkeyValue, pasteHotkeyValue, screenshotHotkeyValue };
    var uniqueHotkeys = hotkeys.Select(h => h.ToLowerInvariant()).Distinct().Count();
    if (uniqueHotkeys != hotkeys.Length)
    {
      System.Windows.MessageBox.Show(
        LocalizationService.GetString("Msg_HotkeyConflictAllDifferent", "Hotkeys must all be different."),
        "transtools",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
      return;
    }

    if (_applyHotkey is not null)
    {
      var error = _applyHotkey(hotkeyValue);
      if (!string.IsNullOrWhiteSpace(error))
      {
        System.Windows.MessageBox.Show(
          string.Format(LocalizationService.GetString("Msg_HotkeyApplyFailed", "Failed to apply translate hotkey: {0}"), error),
          "transtools",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }
    }

    if (_applyPasteHistoryHotkey is not null)
    {
      var error = _applyPasteHistoryHotkey(pasteHotkeyValue);
      if (!string.IsNullOrWhiteSpace(error))
      {
        System.Windows.MessageBox.Show(
          string.Format(LocalizationService.GetString("Msg_PasteHotkeyApplyFailed", "Failed to apply paste history hotkey: {0}"), error),
          "transtools",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }
    }

    if (_applyScreenshotHotkey is not null)
    {
      var error = _applyScreenshotHotkey(screenshotHotkeyValue);
      if (!string.IsNullOrWhiteSpace(error))
      {
        System.Windows.MessageBox.Show(
          string.Format(LocalizationService.GetString("Msg_ScreenshotHotkeyApplyFailed", "Failed to apply screenshot hotkey: {0}"), error),
          "transtools",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
        return;
      }
    }

    // Save general settings
    _settings.Settings.Hotkey = hotkeyValue;
    _settings.Settings.PasteHistoryHotkey = pasteHotkeyValue;
    _settings.Settings.ScreenshotHotkey = screenshotHotkeyValue;
    _settings.Settings.ClipboardHistoryMaxItems = (int)ClipboardHistorySlider.Value;
    _settings.Settings.DefaultFrom = (FromLangCombo.SelectedValue as string) ?? "auto";
    _settings.Settings.DefaultTo = (ToLangCombo.SelectedValue as string) ?? "zh-Hans";

    // Save UI language
    var oldLanguage = _settings.Settings.Language;
    _settings.Settings.Language = (LanguageCombo.SelectedValue as string) ?? "";

    // Save screenshot settings
    _settings.Settings.ScreenshotAutoCopy = ScreenshotAutoCopyCheck.IsChecked == true;
    _settings.Settings.ScreenshotAutoSave = ScreenshotAutoSaveCheck.IsChecked == true;
    _settings.Settings.ScreenshotSavePath = ScreenshotSavePathText.Text?.Trim() ?? "";
    _settings.Settings.ScreenshotFileNameFormat = string.IsNullOrWhiteSpace(ScreenshotFileNameFormatText.Text)
      ? "Screenshot_{0:yyyyMMdd_HHmmss}"
      : ScreenshotFileNameFormatText.Text.Trim();
    _settings.Settings.LongScreenshot ??= new LongScreenshotSettings();
    _settings.Settings.LongScreenshot.WheelNotchesPerStep = Math.Clamp((int)LongWheelNotchesSlider.Value, 1, 12);
    _settings.Settings.LongScreenshot.FrameIntervalMs = Math.Clamp((int)LongFrameIntervalSlider.Value, 100, 2000);
    _settings.Settings.LongScreenshot.MaxFrames = Math.Clamp((int)LongMaxFramesSlider.Value, 20, 500);
    _settings.Settings.LongScreenshot.MaxTotalHeightPx = Math.Clamp((int)LongMaxHeightSlider.Value, 5000, 200000);
    _settings.Settings.LongScreenshot.NoChangeDiffThresholdPercent = Math.Clamp(LongNoChangeThresholdSlider.Value, 0.1, 10.0);
    _settings.Settings.LongScreenshot.NoChangeConsecutiveCount = Math.Clamp((int)LongNoChangeCountSlider.Value, 2, 10);

    _updateClipboardHistoryMaxItems?.Invoke((int)ClipboardHistorySlider.Value);

    // Handle auto start
    try
    {
      var exePath = Environment.ProcessPath;
      if (!string.IsNullOrWhiteSpace(exePath))
      {
        if (AutoStartCheck.IsChecked == true && !_autoStart.IsEnabled())
          _autoStart.Enable(exePath);
        else if (AutoStartCheck.IsChecked != true && _autoStart.IsEnabled())
          _autoStart.Disable();
      }
    }
    catch (Exception ex)
    {
      System.Windows.MessageBox.Show(
        string.Format(LocalizationService.GetString("Msg_AutoStartUpdateFailed", "Failed to update startup setting: {0}"), ex.Message),
        "transtools",
        MessageBoxButton.OK,
        MessageBoxImage.Warning);
    }

    // Save provider settings
    var providerId = (ProviderCombo.SelectedValue as string) ?? "mock";
    _settings.Settings.ActiveProviderId = providerId;

    if (!_settings.Settings.Providers.TryGetValue(providerId, out var ps))
    {
      ps = new ProviderSettings();
      _settings.Settings.Providers[providerId] = ps;
    }

    ps.Endpoint = string.IsNullOrWhiteSpace(EndpointText.Text) ? null : EndpointText.Text.Trim();
    ps.Region = string.IsNullOrWhiteSpace(RegionText.Text) ? null : RegionText.Text.Trim();

    if (string.Equals(providerId, "youdao", StringComparison.OrdinalIgnoreCase))
    {
      ps.AppId = string.IsNullOrWhiteSpace(YoudaoAppIdText.Text) ? null : YoudaoAppIdText.Text.Trim();

      var secretPlain = YoudaoShowSecretCheck.IsChecked == true ? YoudaoSecretText.Text : YoudaoSecretPassword.Password;
      secretPlain = secretPlain?.Trim() ?? string.Empty;
      if (_clearYoudaoSecretRequested)
      {
        ps.AppSecretProtected = null;
      }
      else if (!string.IsNullOrWhiteSpace(secretPlain))
      {
        ps.AppSecretProtected = SecretProtector.ProtectString(secretPlain);
      }
      else if (!string.IsNullOrWhiteSpace(_existingYoudaoSecretProtected))
      {
        ps.AppSecretProtected = _existingYoudaoSecretProtected;
      }

      ps.KeyProtected = null;
    }
    else
    {
      ps.AppId = null;
      ps.AppSecretProtected = null;

      var keyPlain = ShowKeyCheck.IsChecked == true ? KeyText.Text : KeyPassword.Password;
      keyPlain = keyPlain?.Trim() ?? string.Empty;
      if (_clearKeyRequested)
      {
        ps.KeyProtected = null;
      }
      else if (!string.IsNullOrWhiteSpace(keyPlain))
      {
        ps.KeyProtected = SecretProtector.ProtectString(keyPlain);
      }
      else if (!string.IsNullOrWhiteSpace(_existingKeyProtected))
      {
        ps.KeyProtected = _existingKeyProtected;
      }
    }

    // Save bubble settings
    _settings.Settings.Bubble ??= new BubbleSettings();
    _settings.Settings.Bubble.BackgroundColor = BgColorText.Text?.Trim() ?? "#F7F7F5";
    _settings.Settings.Bubble.TextColor = TextColorText.Text?.Trim() ?? "#111111";
    _settings.Settings.Bubble.BorderColor = BorderColorText.Text?.Trim() ?? "#22000000";
    _settings.Settings.Bubble.FontFamily = (FontFamilyCombo.SelectedItem as string) ?? "Segoe UI";
    _settings.Settings.Bubble.FontSize = FontSizeSlider.Value;
    _settings.Settings.Bubble.CornerRadius = CornerRadiusSlider.Value;
    _settings.Settings.Bubble.Padding = PaddingSlider.Value;
    _settings.Settings.Bubble.MaxWidthRatio = MaxWidthSlider.Value;

    // Save paste history bubble settings
    _settings.Settings.PasteHistoryBubble ??= new BubbleSettings();
    _settings.Settings.PasteHistoryBubble.BackgroundColor = PH_BgColorText.Text?.Trim() ?? "#F7F7F5";
    _settings.Settings.PasteHistoryBubble.TextColor = PH_TextColorText.Text?.Trim() ?? "#111111";
    _settings.Settings.PasteHistoryBubble.BorderColor = PH_BorderColorText.Text?.Trim() ?? "#22000000";
    _settings.Settings.PasteHistoryBubble.FontFamily = (PH_FontFamilyCombo.SelectedItem as string) ?? "Segoe UI";
    _settings.Settings.PasteHistoryBubble.FontSize = PH_FontSizeSlider.Value;
    _settings.Settings.PasteHistoryBubble.CornerRadius = PH_CornerRadiusSlider.Value;
    _settings.Settings.PasteHistoryBubble.Padding = PH_PaddingSlider.Value;
    _settings.Settings.PasteHistoryBubble.MaxWidthRatio = PH_MaxWidthSlider.Value;

    _settings.Save();

    // Apply language change if needed - restart app
    if (oldLanguage != _settings.Settings.Language)
    {
      var result = System.Windows.MessageBox.Show(
        LocalizationService.GetString("Msg_RestartRequired"),
        "transtools",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

      if (result == MessageBoxResult.Yes)
      {
        RestartApplication();
        return;
      }
    }

    _clearKeyRequested = false;
    _clearYoudaoSecretRequested = false;
    _existingKeyProtected = ps.KeyProtected;
    _existingYoudaoSecretProtected = ps.AppSecretProtected;
    YoudaoSecretHint.Visibility = string.IsNullOrWhiteSpace(ps.AppSecretProtected) ? Visibility.Collapsed : Visibility.Visible;

    System.Windows.MessageBox.Show(
      Services.LocalizationService.GetString("Msg_SettingsSaved"),
      "transtools",
      MessageBoxButton.OK,
      MessageBoxImage.Information);
  }

  private static void RestartApplication()
  {
    var exePath = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(exePath))
    {
      System.Diagnostics.Process.Start(exePath);
      System.Windows.Application.Current.Shutdown();
    }
  }

  private void SetKeyVisibility(bool visible)
  {
    if (visible)
    {
      KeyText.Text = KeyPassword.Password;
      KeyText.Visibility = Visibility.Visible;
      KeyPassword.Visibility = Visibility.Collapsed;
    }
    else
    {
      KeyPassword.Password = KeyText.Text;
      KeyPassword.Visibility = Visibility.Visible;
      KeyText.Visibility = Visibility.Collapsed;
    }
  }

  private void SetYoudaoSecretVisibility(bool visible)
  {
    if (visible)
    {
      YoudaoSecretText.Text = YoudaoSecretPassword.Password;
      YoudaoSecretText.Visibility = Visibility.Visible;
      YoudaoSecretPassword.Visibility = Visibility.Collapsed;
    }
    else
    {
      YoudaoSecretPassword.Password = YoudaoSecretText.Text;
      YoudaoSecretPassword.Visibility = Visibility.Visible;
      YoudaoSecretText.Visibility = Visibility.Collapsed;
    }
  }

  private void ApplyProviderVisibility(string providerId)
  {
    var isYoudao = string.Equals(providerId, "youdao", StringComparison.OrdinalIgnoreCase);

    KeyRow.Visibility = isYoudao ? Visibility.Collapsed : Visibility.Visible;
    YoudaoAppIdRow.Visibility = isYoudao ? Visibility.Visible : Visibility.Collapsed;
    YoudaoSecretRow.Visibility = isYoudao ? Visibility.Visible : Visibility.Collapsed;
  }

  private void UpdateClipboardHistoryLabel()
  {
    ClipboardHistoryLabel.Text = $"{ClipboardHistorySlider.Value:F0}";
  }

  private void UpdateLongScreenshotLabels()
  {
    LongWheelNotchesLabel.Text = $"{LongWheelNotchesSlider.Value:F0}";
    LongFrameIntervalLabel.Text = $"{LongFrameIntervalSlider.Value:F0} ms";
    LongMaxFramesLabel.Text = $"{LongMaxFramesSlider.Value:F0}";
    LongMaxHeightLabel.Text = $"{LongMaxHeightSlider.Value:F0} px";
    LongNoChangeThresholdLabel.Text = $"{LongNoChangeThresholdSlider.Value:F1}%";
    LongNoChangeCountLabel.Text = $"{LongNoChangeCountSlider.Value:F0}";
  }

  private sealed record ProviderChoice(string Id, string Name);
  private sealed record LanguageChoice(string Id, string Name);
}


