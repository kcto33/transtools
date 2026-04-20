using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenTranslator.Services;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class GifEncodingServiceTests
{
  [Fact]
  public void Encode_Throws_When_Frame_Collection_Is_Empty()
  {
    var service = new GifEncodingService();

    Assert.Throws<InvalidOperationException>(() => service.Encode([], GifRecordingDefaults.FrameIntervalMs));
  }

  [Fact]
  public void BuildFrameDelays_Alternates_12_And_13_Centiseconds_For_125Ms()
  {
    var delays = GifEncodingService.BuildFrameDelays(GifRecordingDefaults.FrameIntervalMs, 4);

    Assert.Equal(new ushort[] { 12, 13, 12, 13 }, delays);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Encode_Throws_When_Frame_Interval_Is_Not_Positive(int frameIntervalMs)
  {
    var service = new GifEncodingService();

    Assert.Throws<ArgumentOutOfRangeException>(() =>
      service.Encode([CreateSolidFrame(4, 4, Colors.Red)], frameIntervalMs));
  }

  [Fact]
  public void Encode_Normalizes_Non_Bgra32_Frames_Before_Encoding()
  {
    var service = new GifEncodingService();
    var frame = CreateGray8Frame(6, 4);

    var bytes = service.Encode([frame], GifRecordingDefaults.FrameIntervalMs);

    using var stream = new MemoryStream(bytes);
    var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

    Assert.Single(decoder.Frames);
    Assert.Equal(6, decoder.Frames[0].PixelWidth);
    Assert.Equal(4, decoder.Frames[0].PixelHeight);
  }

  [Fact]
  public void Encode_Returns_Animated_Gif_With_Expected_Frame_Metadata()
  {
    var service = new GifEncodingService();
    var frames = new[]
    {
      CreateSolidFrame(6, 4, Colors.Red),
      CreateSolidFrame(6, 4, Colors.Blue),
    };

    var bytes = service.Encode(frames, GifRecordingDefaults.FrameIntervalMs);

    using var stream = new MemoryStream(bytes);
    var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

    Assert.Equal(2, decoder.Frames.Count);
    Assert.Equal(6, decoder.Frames[0].PixelWidth);
    Assert.Equal(4, decoder.Frames[0].PixelHeight);

    var firstMetadata = Assert.IsType<BitmapMetadata>(decoder.Frames[0].Metadata);
    var secondMetadata = Assert.IsType<BitmapMetadata>(decoder.Frames[1].Metadata);

    Assert.Equal((ushort)12, Assert.IsType<ushort>(firstMetadata.GetQuery("/grctlext/Delay")));
    Assert.Equal((ushort)13, Assert.IsType<ushort>(secondMetadata.GetQuery("/grctlext/Delay")));
    Assert.Equal((byte)2, Assert.IsType<byte>(firstMetadata.GetQuery("/grctlext/Disposal")));
    Assert.Equal((byte)2, Assert.IsType<byte>(secondMetadata.GetQuery("/grctlext/Disposal")));
  }

  private static BitmapSource CreateSolidFrame(int width, int height, Color color)
  {
    var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
    var pixels = new byte[width * height * 4];

    for (var index = 0; index < pixels.Length; index += 4)
    {
      pixels[index + 0] = color.B;
      pixels[index + 1] = color.G;
      pixels[index + 2] = color.R;
      pixels[index + 3] = color.A;
    }

    bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    bitmap.Freeze();
    return bitmap;
  }

  private static BitmapSource CreateGray8Frame(int width, int height)
  {
    var pixels = new byte[width * height];

    for (var index = 0; index < pixels.Length; index++)
    {
      pixels[index] = (byte)(index * 11);
    }

    var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
    bitmap.Freeze();
    return bitmap;
  }
}
