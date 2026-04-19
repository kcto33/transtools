using ScreenTranslator.Models;
using ScreenTranslator.Services;
using ScreenTranslator.Windows;
using Xunit;

using WinRect = System.Drawing.Rectangle;

namespace ScreenTranslator.Tests;

public sealed class LongScreenshotCoordinatorTests
{
  [Fact]
  public void ConfigureCaptureHooks_AssignsBeforeAndAfterCaptureCallbacks()
  {
    var session = new LongScreenshotSession(new WinRect(0, 0, 100, 100), new LongScreenshotSettings());
    var beforeCalled = false;
    var afterCalled = false;

    LongScreenshotSessionCoordinator.ConfigureCaptureHooks(
      session,
      () => beforeCalled = true,
      () => afterCalled = true);

    session.BeforeCapture?.Invoke();
    session.AfterCapture?.Invoke();

    Assert.True(beforeCalled);
    Assert.True(afterCalled);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void ShouldAutoCopyResult_FollowsScreenshotAutoCopySetting(bool enabled)
  {
    var settings = new AppSettings
    {
      ScreenshotAutoCopy = enabled,
    };

    var shouldAutoCopy = LongScreenshotSessionCoordinator.ShouldAutoCopyResult(settings);

    Assert.Equal(enabled, shouldAutoCopy);
  }

  [Fact]
  public void PrimeProcessedScrollAttemptsAfterBaseline_ConsumesPendingStartupAttempts()
  {
    var processed = LongScreenshotSession.PrimeProcessedScrollAttemptsAfterBaseline(3, 0);

    Assert.Equal(3, processed);
  }

  [Fact]
  public void PrimeProcessedScrollAttemptsAfterBaseline_DoesNotMoveBackward()
  {
    var processed = LongScreenshotSession.PrimeProcessedScrollAttemptsAfterBaseline(2, 5);

    Assert.Equal(5, processed);
  }

  [Fact]
  public void ShouldHandleStartupClick_ReturnsFalse_InsideDebounceWindow()
  {
    var shouldHandle = LongScreenshotControlWindow.ShouldHandleStartupClick(
      shownAtUtc: DateTime.UtcNow,
      nowUtc: DateTime.UtcNow.AddMilliseconds(120));

    Assert.False(shouldHandle);
  }

  [Fact]
  public void ShouldHandleStartupClick_ReturnsTrue_AfterDebounceWindow()
  {
    var shownAt = DateTime.UtcNow;

    var shouldHandle = LongScreenshotControlWindow.ShouldHandleStartupClick(
      shownAtUtc: shownAt,
      nowUtc: shownAt.AddMilliseconds(400));

    Assert.True(shouldHandle);
  }

  [Fact]
  public void ShouldAutoStopForNoChange_ReturnsFalse_BeforeSecondFrameIsAccepted()
  {
    var shouldStop = LongScreenshotSession.ShouldAutoStopForNoChange(
      noChangeCount: 3,
      noChangeRequired: 3,
      acceptedFrames: 1);

    Assert.False(shouldStop);
  }

  [Fact]
  public void ShouldAutoStopForNoChange_ReturnsTrue_AfterSecondFrameIsAccepted()
  {
    var shouldStop = LongScreenshotSession.ShouldAutoStopForNoChange(
      noChangeCount: 3,
      noChangeRequired: 3,
      acceptedFrames: 2);

    Assert.True(shouldStop);
  }

  [Fact]
  public void BuildResultHint_Includes_StopReason_And_FrameCounts()
  {
    var result = new LongScreenshotResult
    {
      StopReason = LongScreenshotStopReason.AutoReachedBottom,
      CapturedFrames = 4,
      AcceptedFrames = 2,
    };

    var hint = LongScreenshotControlWindow.BuildResultHint(result, autoSavedPath: null);

    Assert.Contains("Auto", hint, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("4", hint, StringComparison.Ordinal);
    Assert.Contains("2", hint, StringComparison.Ordinal);
  }

  [Fact]
  public void WrapCaptureHook_InvokesHookThroughDispatcher()
  {
    var invokedOnDispatcher = false;
    var hookCalled = false;

    var wrapped = LongScreenshotSessionCoordinator.WrapCaptureHook(
      () => hookCalled = true,
      callback =>
      {
        invokedOnDispatcher = true;
        callback();
      });

    wrapped();

    Assert.True(invokedOnDispatcher);
    Assert.True(hookCalled);
  }
}
