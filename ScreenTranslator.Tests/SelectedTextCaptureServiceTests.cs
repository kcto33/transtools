using ScreenTranslator.Services;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class SelectedTextCaptureServiceTests
{
  [Fact]
  public async Task TryCaptureAsync_ReturnsTrimmedSelectedText_AndRestoresClipboard()
  {
    var platform = new FakePlatform
    {
      Snapshot = new SelectedTextCaptureService.ClipboardSnapshot("original", hasData: true),
      ClipboardTexts = [null, "  selected text  "],
    };
    var suppression = new TrackingDisposable();
    var service = new SelectedTextCaptureService(
      platform,
      () => suppression,
      timeoutMs: 120,
      settleDelayMs: 0,
      pollIntervalMs: 1);

    var result = await service.TryCaptureAsync(CancellationToken.None);

    Assert.Equal("selected text", result);
    Assert.True(platform.Cleared);
    Assert.Equal(1, platform.SendCopyCount);
    Assert.Equal("original", platform.RestoredSnapshot?.Text);
    Assert.True(suppression.Disposed);
  }

  [Fact]
  public async Task TryCaptureAsync_ReturnsNull_WhenCopyDoesNotProduceTextWithinTimeout()
  {
    var platform = new FakePlatform
    {
      Snapshot = new SelectedTextCaptureService.ClipboardSnapshot("original", hasData: true),
      ClipboardTexts = [null, null, null],
    };
    var service = new SelectedTextCaptureService(
      platform,
      () => null,
      timeoutMs: 3,
      settleDelayMs: 0,
      pollIntervalMs: 1);

    var result = await service.TryCaptureAsync(CancellationToken.None);

    Assert.Null(result);
    Assert.Equal("original", platform.RestoredSnapshot?.Text);
  }

  [Fact]
  public async Task TryCaptureAsync_DefaultBudget_WaitsLongEnoughForSlightlyDelayedClipboardText()
  {
    var platform = new FakePlatform
    {
      Snapshot = new SelectedTextCaptureService.ClipboardSnapshot("original", hasData: true),
      ClipboardTexts = [null, null, null, null, null, null, " delayed text "],
    };
    var service = new SelectedTextCaptureService(
      platform,
      () => null);

    var result = await service.TryCaptureAsync(CancellationToken.None);

    Assert.Equal("delayed text", result);
    Assert.Equal("original", platform.RestoredSnapshot?.Text);
  }

  private sealed class FakePlatform : SelectedTextCaptureService.IPlatform
  {
    private int _readIndex;

    public SelectedTextCaptureService.ClipboardSnapshot Snapshot { get; set; } = new(null, false);
    public List<string?> ClipboardTexts { get; set; } = [];
    public bool Cleared { get; private set; }
    public int SendCopyCount { get; private set; }
    public SelectedTextCaptureService.ClipboardSnapshot? RestoredSnapshot { get; private set; }

    public bool CanCapture() => true;

    public Task<SelectedTextCaptureService.ClipboardSnapshot> CaptureClipboardAsync(CancellationToken ct) =>
      Task.FromResult(Snapshot);

    public Task ClearClipboardAsync(CancellationToken ct)
    {
      Cleared = true;
      return Task.CompletedTask;
    }

    public Task<string?> ReadClipboardTextAsync(CancellationToken ct)
    {
      if (_readIndex >= ClipboardTexts.Count)
        return Task.FromResult<string?>(null);

      return Task.FromResult(ClipboardTexts[_readIndex++]);
    }

    public Task RestoreClipboardAsync(SelectedTextCaptureService.ClipboardSnapshot snapshot, CancellationToken ct)
    {
      RestoredSnapshot = snapshot;
      return Task.CompletedTask;
    }

    public Task SendCopyAsync(CancellationToken ct)
    {
      SendCopyCount++;
      return Task.CompletedTask;
    }
  }

  private sealed class TrackingDisposable : IDisposable
  {
    public bool Disposed { get; private set; }

    public void Dispose()
    {
      Disposed = true;
    }
  }
}
