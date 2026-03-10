using System.Globalization;
using System.Windows;

using WpfApplication = System.Windows.Application;

namespace ScreenTranslator.Services;

public sealed class LocalizationService
{
  private static LocalizationService? _instance;
  private ResourceDictionary? _currentDictionary;

  public static LocalizationService Instance => _instance ??= new LocalizationService();

  public string CurrentLanguage { get; private set; } = "zh-CN";

  public static IReadOnlyList<LanguageInfo> SupportedLanguages { get; } = new List<LanguageInfo>
  {
    new("zh-CN", "简体中文"),
    new("en", "English"),
  };

  private LocalizationService()
  {
  }

  public void Initialize(string? language = null)
  {
    if (string.IsNullOrEmpty(language))
    {
      var culture = CultureInfo.CurrentUICulture;
      language = culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en";
    }

    SetLanguage(language);
  }

  public void SetLanguage(string language)
  {
    var supported = SupportedLanguages.Any(l => l.Code == language);
    if (!supported)
    {
      language = "en";
    }

    CurrentLanguage = language;
    var resourcePath = $"pack://application:,,,/ScreenTranslator;component/Resources/Strings.{language}.xaml";

    try
    {
      var newDictionary = new ResourceDictionary { Source = new Uri(resourcePath) };

      if (_currentDictionary != null)
      {
        WpfApplication.Current.Resources.MergedDictionaries.Remove(_currentDictionary);
      }

      WpfApplication.Current.Resources.MergedDictionaries.Add(newDictionary);
      _currentDictionary = newDictionary;
    }
    catch (Exception ex)
    {
      System.Diagnostics.Debug.WriteLine($"Failed to load language resource: {ex.Message}");
    }
  }

  public static string GetString(string key, string? fallback = null)
  {
    try
    {
      var value = WpfApplication.Current.TryFindResource(key);
      if (value is string str)
      {
        return str;
      }
    }
    catch
    {
      // Resource not found.
    }

    return fallback ?? key;
  }

  public static string S(string key, string? fallback = null) => GetString(key, fallback);
}

public sealed class LanguageInfo
{
  public string Code { get; }
  public string DisplayName { get; }

  public LanguageInfo(string code, string displayName)
  {
    Code = code;
    DisplayName = displayName;
  }

  public override string ToString() => DisplayName;
}
