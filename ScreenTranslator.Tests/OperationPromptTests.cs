using Xunit;

namespace ScreenTranslator.Tests;

public sealed class OperationPromptTests
{
  [Theory]
  [InlineData("Strings.en.xaml")]
  [InlineData("Strings.zh-CN.xaml")]
  public void ResourceFiles_Contain_OperationPromptStrings(string fileName)
  {
    var content = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Resources", fileName));

    Assert.Contains("x:Key=\"SelectionOverlay_Hint\"", content);
    Assert.Contains("x:Key=\"Bubble_Tooltip\"", content);
    Assert.Contains("x:Key=\"Bubble_CopyStatus\"", content);
    Assert.Contains("x:Key=\"ScreenshotOverlay_Hint\"", content);
    Assert.Contains("x:Key=\"Screenshot_Status_Copied\"", content);
    Assert.Contains("x:Key=\"Screenshot_Status_SavedTo\"", content);
    Assert.Contains("x:Key=\"Screenshot_Status_Pinned\"", content);
    Assert.Contains("x:Key=\"FloatingNote_TitleTip\"", content);
    Assert.Contains("x:Key=\"Settings_HotkeyTip\"", content);
    Assert.Contains("x:Key=\"FloatingNoteList_Hint\"", content);
    Assert.Contains("x:Key=\"FloatingNoteList_Empty\"", content);
    Assert.Contains("x:Key=\"Tray_Status_HotkeysDisabled\"", content);
    Assert.Contains("x:Key=\"Tray_Status_HotkeysEnabled\"", content);
    Assert.Contains("x:Key=\"Tray_Status_AutoStartEnabled\"", content);
    Assert.Contains("x:Key=\"Tray_Status_AutoStartDisabled\"", content);
  }

  [Fact]
  public void OverlayWindow_Xaml_Contains_SelectionHint()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "OverlayWindow.xaml"));

    Assert.Contains("x:Name=\"SelectionHint\"", xaml);
    Assert.Contains("SelectionOverlay_Hint", xaml);
  }

  [Fact]
  public void ScreenshotOverlayWindow_Xaml_Contains_Hint_And_Status_Text()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "ScreenshotOverlayWindow.xaml"));

    Assert.Contains("x:Name=\"OverlayHint\"", xaml);
    Assert.Contains("ScreenshotOverlay_Hint", xaml);
    Assert.Contains("x:Name=\"StatusText\"", xaml);
  }

  [Fact]
  public void FloatingNoteWindow_Xaml_Contains_Minimal_OperationHint()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "FloatingNoteWindow.xaml"));

    Assert.Contains("FloatingNote_TitleTip", xaml);
  }

  [Fact]
  public void BubbleWindow_Xaml_Uses_Transparent_Custom_Tooltip()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "BubbleWindow.xaml"));

    Assert.Contains("<ToolTip", xaml);
    Assert.Contains("Background=\"Transparent\"", xaml);
    Assert.Contains("Background=\"#99333333\"", xaml);
    Assert.Contains("Bubble_Tooltip", xaml);
  }

  [Fact]
  public void BubbleWindow_Xaml_Contains_CopyStatusBadge()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "BubbleWindow.xaml"));

    Assert.Contains("x:Name=\"CopyStatusBadge\"", xaml);
    Assert.Contains("x:Name=\"CopyStatusText\"", xaml);
    Assert.Contains("Bubble_CopyStatus", xaml);
  }

  [Fact]
  public void FloatingNoteListWindow_Xaml_Contains_ListHint_And_EmptyState()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "FloatingNoteListWindow.xaml"));

    Assert.Contains("FloatingNoteList_Hint", xaml);
    Assert.Contains("x:Name=\"EmptyText\"", xaml);
  }

  [Fact]
  public void SettingsWindow_Xaml_Contains_Hotkey_Tooltips()
  {
    var xaml = File.ReadAllText(GetSourceFilePath("ScreenTranslator", "Windows", "SettingsWindow.xaml"));

    Assert.Contains("Settings_HotkeyTip", xaml);
    Assert.Contains("Settings_LongWheelNotchesTip", xaml);
    Assert.Contains("Settings_APIKeyTip", xaml);
  }

  private static string GetSourceFilePath(params string[] relativeParts)
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
      var candidate = Path.Combine([directory.FullName, .. relativeParts]);
      if (File.Exists(candidate))
      {
        return candidate;
      }

      directory = directory.Parent;
    }

    throw new FileNotFoundException($"Could not find source file: {Path.Combine(relativeParts)}");
  }
}
