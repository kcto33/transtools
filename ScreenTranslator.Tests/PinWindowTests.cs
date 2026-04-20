using ScreenTranslator.Windows;

using Xunit;

namespace ScreenTranslator.Tests;

public sealed class PinWindowTests
{
  [Theory]
  [InlineData(true, 1, "Ignore")]
  [InlineData(false, 1, "StartDrag")]
  [InlineData(false, 2, "Close")]
  public void ResolveLeftMouseDownAction_ReturnsExpectedAction(
    bool isInteractiveElementHit,
    int clickCount,
    string expected)
  {
    var action = PinWindow.ResolveLeftMouseDownAction(isInteractiveElementHit, clickCount);

    Assert.Equal(expected, action.ToString());
  }
}
