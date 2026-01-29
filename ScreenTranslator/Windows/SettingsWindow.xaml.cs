using System.Windows;
using System.Windows.Controls;
using ScreenTranslator.Models;
using ScreenTranslator.Services;

namespace ScreenTranslator.Windows;

public partial class SettingsWindow : Window
{
  private readonly SettingsService _settings;
  private readonly Func<string, string?>? _applyHotkey;
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
    //new("fr", "French"),
    //new("de", "German"),
    //new("es", "Spanish"),
    //new("it", "Italian"),
    new("ru", "Russian"),
    //new("pt", "Portuguese"),
    //new("ar", "Arabic"),
    //new("hi", "Hindi"),
    //new("id", "Indonesian"),
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
    //new("fr", "French"),
    //new("de", "German"),
    //new("es", "Spanish"),
    //new("it", "Italian"),
    new("ru", "Russian"),
    //new("pt", "Portuguese"),
    //new("ar", "Arabic"),
    //new("hi", "Hindi"),
    //new("id", "Indonesian"),
    new("th", "Thai"),
    new("vi", "Vietnamese"),
  ];
  private readonly List<ProviderChoice> _providers =
  [
    new("mock", "Mock (no network)"),
    new("youdao", "Youdao"),
    new("deepl", "DeepL"),
    new("azure", "Microsoft Translator"),
    new("google", "Google Translate"),
    new("libretranslate", "LibreTranslate (self-hosted)"),
  ];

  public SettingsWindow(SettingsService settings, Func<string, string?>? applyHotkey = null)
  {
    InitializeComponent();
    _settings = settings;
    _applyHotkey = applyHotkey;

    ProviderCombo.ItemsSource = _providers;
    ProviderCombo.DisplayMemberPath = nameof(ProviderChoice.Name);
    ProviderCombo.SelectedValuePath = nameof(ProviderChoice.Id);
    ProviderCombo.SelectionChanged += (_, _) => LoadProviderFields();

    FromLangCombo.ItemsSource = _fromLanguages;
    FromLangCombo.DisplayMemberPath = nameof(LanguageChoice.Name);
    FromLangCombo.SelectedValuePath = nameof(LanguageChoice.Id);

    ToLangCombo.ItemsSource = _toLanguages;
    ToLangCombo.DisplayMemberPath = nameof(LanguageChoice.Name);
    ToLangCombo.SelectedValuePath = nameof(LanguageChoice.Id);

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

    SaveButton.Click += (_, _) => Save();

    LoadFromSettings();
  }

  private void LoadFromSettings()
  {
    ProviderCombo.SelectedValue = _settings.Settings.ActiveProviderId;
    LoadProviderFields();
    HotkeyText.Text = string.IsNullOrWhiteSpace(_settings.Settings.Hotkey) ? "Ctrl+Alt+T" : _settings.Settings.Hotkey;
    FromLangCombo.SelectedValue = string.IsNullOrWhiteSpace(_settings.Settings.DefaultFrom) ? "auto" : _settings.Settings.DefaultFrom;
    ToLangCombo.SelectedValue = string.IsNullOrWhiteSpace(_settings.Settings.DefaultTo) ? "zh-Hans" : _settings.Settings.DefaultTo;
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
    var hotkeyValue = (HotkeyText.Text ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(hotkeyValue))
      hotkeyValue = "Ctrl+Alt+T";

    if (_applyHotkey is not null)
    {
      var error = _applyHotkey(hotkeyValue);
      if (!string.IsNullOrWhiteSpace(error))
      {
        System.Windows.MessageBox.Show($"Hotkey invalid: {error}", "ScreenTranslator", MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
      }
    }

    var providerId = (ProviderCombo.SelectedValue as string) ?? "mock";
    _settings.Settings.ActiveProviderId = providerId;
    _settings.Settings.Hotkey = hotkeyValue;
    _settings.Settings.DefaultFrom = (FromLangCombo.SelectedValue as string) ?? "auto";
    _settings.Settings.DefaultTo = (ToLangCombo.SelectedValue as string) ?? "zh-Hans";

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

      // Clear generic key field to avoid confusion.
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

    _settings.Save();
    _clearKeyRequested = false;
    _clearYoudaoSecretRequested = false;
    _existingKeyProtected = ps.KeyProtected;
    _existingYoudaoSecretProtected = ps.AppSecretProtected;
    YoudaoSecretHint.Visibility = string.IsNullOrWhiteSpace(ps.AppSecretProtected) ? Visibility.Collapsed : Visibility.Visible;
    System.Windows.MessageBox.Show("Saved.", "ScreenTranslator", MessageBoxButton.OK, MessageBoxImage.Information);
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

    KeyLabel.Visibility = isYoudao ? Visibility.Collapsed : Visibility.Visible;
    KeyRow.Visibility = isYoudao ? Visibility.Collapsed : Visibility.Visible;

    YoudaoAppIdLabel.Visibility = isYoudao ? Visibility.Visible : Visibility.Collapsed;
    YoudaoAppIdText.Visibility = isYoudao ? Visibility.Visible : Visibility.Collapsed;
    YoudaoSecretLabel.Visibility = isYoudao ? Visibility.Visible : Visibility.Collapsed;
    YoudaoSecretRow.Visibility = isYoudao ? Visibility.Visible : Visibility.Collapsed;

    // Endpoint is useful for Youdao (default is openapi.youdao.com/api) and for self-hosted providers.
    EndpointLabel.Visibility = Visibility.Visible;
    EndpointText.Visibility = Visibility.Visible;

    // Region is only used by some providers (kept for future Azure support).
    RegionLabel.Visibility = Visibility.Visible;
    RegionText.Visibility = Visibility.Visible;
  }

  private sealed record ProviderChoice(string Id, string Name);
  private sealed record LanguageChoice(string Id, string Name);
}
