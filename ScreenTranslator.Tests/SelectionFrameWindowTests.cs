using System.Windows.Media;

using ScreenTranslator.Windows;

using Xunit;

namespace ScreenTranslator.Tests;

public sealed class SelectionFrameWindowTests
{
  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void GetFrameBorderColor_ReturnsAccentBlue_ForAllStates(bool locked)
  {
    var color = SelectionFrameWindow.GetFrameBorderColor(locked);

    Assert.Equal(Color.FromRgb(30, 136, 229), color);
  }
}
