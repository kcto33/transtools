using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

  [Fact]
  public void SetImage_Stretches_Image_To_Dpi_Adjusted_Content_Size()
  {
    RunInSta(() =>
    {
      EnsureApplicationResources();

      var window = new PinWindow();
      var bitmap = new WriteableBitmap(350, 210, 96, 96, PixelFormats.Bgra32, null);

      window.SetImage(bitmap, dpiScaleX: 1.75, dpiScaleY: 1.75);

      Assert.Equal(200, window.PinnedImage.Width);
      Assert.Equal(120, window.PinnedImage.Height);
      Assert.Equal(Stretch.Fill, window.PinnedImage.Stretch);
    });
  }

  private static void RunInSta(Action action)
  {
    Exception? exception = null;
    var thread = new Thread(() =>
    {
      try
      {
        action();
      }
      catch (Exception ex)
      {
        exception = ex;
      }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();

    if (exception is not null)
    {
      throw exception;
    }
  }

  private static void EnsureApplicationResources()
  {
    var app = System.Windows.Application.Current ?? new System.Windows.Application();
    app.Resources["AccentBrush"] = Brushes.DeepSkyBlue;
  }
}
