using System.Runtime.InteropServices;
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
      ClipboardSequenceNumbers = [10, 11],
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
    Assert.False(platform.Cleared);
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
      ClipboardSequenceNumbers = [10, 10, 10],
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
      ClipboardSequenceNumbers = [10, 10, 10, 10, 10, 10, 11],
      ClipboardTexts = [" delayed text "],
    };
    var service = new SelectedTextCaptureService(
      platform,
      () => null);

    var result = await service.TryCaptureAsync(CancellationToken.None);

    Assert.Equal("delayed text", result);
    Assert.Equal("original", platform.RestoredSnapshot?.Text);
  }

  [Fact]
  public async Task TryCaptureAsync_DefaultBudget_AllowsModeratelyDelayedClipboardText()
  {
    var platform = new FakePlatform
    {
      Snapshot = new SelectedTextCaptureService.ClipboardSnapshot("original", hasData: true),
      ClipboardSequenceNumbers = [10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 11],
      ClipboardTexts = [" slower text "],
    };
    var service = new SelectedTextCaptureService(
      platform,
      () => null);

    var result = await service.TryCaptureAsync(CancellationToken.None);

    Assert.Equal("slower text", result);
    Assert.Equal("original", platform.RestoredSnapshot?.Text);
  }

  [Fact]
  public async Task TryCaptureAsync_IgnoresExistingClipboardText_UntilClipboardChanges()
  {
    var platform = new FakePlatform
    {
      Snapshot = new SelectedTextCaptureService.ClipboardSnapshot("original", hasData: true),
      ClipboardSequenceNumbers = [10, 10, 10, 11],
      ClipboardTexts = [" selected after copy "],
    };
    var service = new SelectedTextCaptureService(
      platform,
      () => null,
      timeoutMs: 120,
      settleDelayMs: 0,
      pollIntervalMs: 1);

    var result = await service.TryCaptureAsync(CancellationToken.None);

    Assert.Equal("selected after copy", result);
    Assert.False(platform.Cleared);
    Assert.Equal("original", platform.RestoredSnapshot?.Text);
  }

  [Fact]
  public async Task TryCaptureAsync_ContinuesWhenClipboardSnapshotContainsInvalidData()
  {
    var platform = new FakePlatform
    {
      CaptureClipboardException = new COMException("bad clipboard data", unchecked((int)0x800401D3)),
      ClipboardSequenceNumbers = [10, 11],
      ClipboardTexts = [" selected despite bad snapshot "],
    };
    var service = new SelectedTextCaptureService(
      platform,
      () => null,
      timeoutMs: 120,
      settleDelayMs: 0,
      pollIntervalMs: 1);

    var result = await service.TryCaptureAsync(CancellationToken.None);

    Assert.Equal("selected despite bad snapshot", result);
    Assert.Equal(1, platform.SendCopyCount);
    Assert.Null(platform.RestoredSnapshot);
  }

  private sealed class FakePlatform : SelectedTextCaptureService.IPlatform
  {
    private int _readIndex;
    private int _sequenceIndex;

    public SelectedTextCaptureService.ClipboardSnapshot Snapshot { get; set; } = new(null, false);
    public Exception? CaptureClipboardException { get; set; }
    public List<uint> ClipboardSequenceNumbers { get; set; } = [];
    public List<string?> ClipboardTexts { get; set; } = [];
    public bool Cleared { get; private set; }
    public int SendCopyCount { get; private set; }
    public SelectedTextCaptureService.ClipboardSnapshot? RestoredSnapshot { get; private set; }

    public bool CanCapture() => true;

    public Task<SelectedTextCaptureService.ClipboardSnapshot> CaptureClipboardAsync(CancellationToken ct)
    {
      if (CaptureClipboardException is not null)
        return Task.FromException<SelectedTextCaptureService.ClipboardSnapshot>(CaptureClipboardException);

      return Task.FromResult(Snapshot);
    }

    public Task ClearClipboardAsync(CancellationToken ct)
    {
      Cleared = true;
      return Task.CompletedTask;
    }

    public uint GetClipboardSequenceNumber()
    {
      if (ClipboardSequenceNumbers.Count == 0)
        return 0;

      var index = Math.Min(_sequenceIndex, ClipboardSequenceNumbers.Count - 1);
      var value = ClipboardSequenceNumbers[index];
      if (_sequenceIndex < ClipboardSequenceNumbers.Count - 1)
        _sequenceIndex++;
      return value;
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
