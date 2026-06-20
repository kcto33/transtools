using System.Windows.Input;
using ScreenTranslator.Models;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;

using Xunit;

namespace ScreenTranslator.Tests;

public sealed class FloatingNoteWindowStateTests
{
  [Theory]
  [InlineData(MouseButton.Middle, true)]
  [InlineData(MouseButton.Left, false)]
  [InlineData(MouseButton.Right, false)]
  public void ShouldTogglePinnedForMouseButton_ReturnsTrueOnlyForMiddleButton(MouseButton button, bool expected)
  {
    Assert.Equal(expected, FloatingNoteWindow.ShouldTogglePinnedForMouseButton(button));
  }

  [Theory]
  [InlineData(false, true)]
  [InlineData(true, false)]
  public void TogglePinnedState_InvertsCurrentPinnedState(bool current, bool expected)
  {
    Assert.Equal(expected, FloatingNoteWindow.TogglePinnedState(current));
  }

  [Fact]
  public void Constructor_UsesThickerRoundedOuterBorder()
  {
    RunInSta(() =>
    {
      var settings = new AppSettings
      {
        FloatingNotes = new FloatingNoteSettings
        {
          SaveDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "FloatingNoteTests", Guid.NewGuid().ToString("N"))
        }
      };
      var storage = new FloatingNoteStorageService(settings);
      var window = new FloatingNoteWindow(settings, storage);

      Assert.Equal(new System.Windows.CornerRadius(14), window.Chrome.CornerRadius);
      Assert.Equal(new System.Windows.Thickness(2), window.Chrome.BorderThickness);

      window.Close();
    });
  }

  [Fact]
  public void Constructor_ShowsWindowInTaskbar()
  {
    RunInSta(() =>
    {
      var settings = new AppSettings
      {
        FloatingNotes = new FloatingNoteSettings
        {
          SaveDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "FloatingNoteTests", Guid.NewGuid().ToString("N"))
        }
      };
      var storage = new FloatingNoteStorageService(settings);
      var window = new FloatingNoteWindow(settings, storage);

      Assert.True(window.ShowInTaskbar);

      window.Close();
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
}
