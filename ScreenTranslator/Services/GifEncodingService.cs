using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenTranslator.Services;

public sealed class GifEncodingService
{
  public byte[] Encode(IReadOnlyList<BitmapSource> frames, int frameIntervalMs)
  {
    if (frameIntervalMs <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(frameIntervalMs), frameIntervalMs, "Frame interval must be greater than zero.");
    }

    if (frames.Count == 0)
    {
      throw new InvalidOperationException("frames must contain at least one image.");
    }

    var encoder = new GifBitmapEncoder();
    var delays = BuildFrameDelays(frameIntervalMs, frames.Count);

    for (var index = 0; index < frames.Count; index++)
    {
      var normalizedFrame = NormalizeFrame(frames[index]);
      var metadata = new BitmapMetadata("gif");
      metadata.SetQuery("/grctlext/Delay", delays[index]);
      metadata.SetQuery("/grctlext/Disposal", (byte)2);

      var frame = BitmapFrame.Create(normalizedFrame, null, metadata, null);
      encoder.Frames.Add(frame);
    }

    using var stream = new MemoryStream();
    encoder.Save(stream);
    var bytes = stream.ToArray();
    PatchGraphicControlExtensions(bytes, delays, frames.Count);
    return bytes;
  }

  internal static IReadOnlyList<ushort> BuildFrameDelays(int frameIntervalMs, int frameCount)
  {
    if (frameCount <= 0)
    {
      return Array.Empty<ushort>();
    }

    var delays = new ushort[frameCount];
    var baseDelay = frameIntervalMs / 10;
    var remainder = frameIntervalMs % 10;
    var accumulator = 0;

    for (var index = 0; index < frameCount; index++)
    {
      var delay = baseDelay;
      accumulator += remainder;
      if (accumulator >= 10)
      {
        delay++;
        accumulator -= 10;
      }

      delays[index] = (ushort)Math.Max(1, delay);
    }

    return delays;
  }

  private static BitmapSource NormalizeFrame(BitmapSource frame)
  {
    if (frame.Format == PixelFormats.Bgra32)
    {
      return frame;
    }

    var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
    if (converted.CanFreeze)
    {
      converted.Freeze();
    }

    return converted;
  }

  private static void PatchGraphicControlExtensions(byte[] bytes, IReadOnlyList<ushort> delays, int expectedFrameCount)
  {
    var offset = 0;
    var gceCount = 0;
    var delayIndex = 0;

    RequireBytes(bytes, ref offset, 13);
    var packed = bytes[10];
    offset = 13;

    if ((packed & 0x80) != 0)
    {
      var colorTableSize = 3 * (1 << ((packed & 0x07) + 1));
      RequireBytes(bytes, ref offset, colorTableSize);
      offset += colorTableSize;
    }

    while (offset < bytes.Length)
    {
      var introducer = bytes[offset++];
      switch (introducer)
      {
        case 0x3B:
          if (gceCount != expectedFrameCount || delayIndex != delays.Count)
          {
            throw new InvalidOperationException(
              $"Expected {expectedFrameCount} GIF frame metadata blocks but patched {gceCount}.");
          }

          return;
        case 0x21:
          offset = PatchExtensionBlock(bytes, delays, ref offset, ref delayIndex, ref gceCount);
          break;
        case 0x2C:
          offset = SkipImageDescriptor(bytes, ref offset);
          break;
        default:
          throw new InvalidOperationException($"Unexpected GIF block introducer 0x{introducer:X2}.");
      }
    }

    throw new InvalidOperationException(
      $"Expected {expectedFrameCount} GIF frame metadata blocks but patched {gceCount}.");
  }

  private static int PatchExtensionBlock(
    byte[] bytes,
    IReadOnlyList<ushort> delays,
    ref int offset,
    ref int delayIndex,
    ref int gceCount)
  {
    RequireBytes(bytes, ref offset, 1);
    var label = bytes[offset++];

    if (label == 0xF9)
    {
      RequireBytes(bytes, ref offset, 6);
      if (bytes[offset] != 0x04)
      {
        throw new InvalidOperationException("Malformed GIF graphic control extension.");
      }

      if (delayIndex >= delays.Count)
      {
        throw new InvalidOperationException("Encountered more GIF frame metadata blocks than expected.");
      }

      bytes[offset + 1] = (byte)((bytes[offset + 1] & ~0x1C) | (2 << 2));
      var delay = delays[delayIndex++];
      bytes[offset + 2] = (byte)(delay & 0xFF);
      bytes[offset + 3] = (byte)(delay >> 8);

      if (bytes[offset + 5] != 0x00)
      {
        throw new InvalidOperationException("Malformed GIF graphic control extension terminator.");
      }

      offset += 6;
      gceCount++;
      return offset;
    }

    while (true)
    {
      RequireBytes(bytes, ref offset, 1);
      var blockSize = bytes[offset++];
      if (blockSize == 0)
      {
        return offset;
      }

      RequireBytes(bytes, ref offset, blockSize);
      offset += blockSize;
    }
  }

  private static int SkipImageDescriptor(byte[] bytes, ref int offset)
  {
    RequireBytes(bytes, ref offset, 9);
    var packed = bytes[offset + 8];
    offset += 9;

    if ((packed & 0x80) != 0)
    {
      var colorTableSize = 3 * (1 << ((packed & 0x07) + 1));
      RequireBytes(bytes, ref offset, colorTableSize);
      offset += colorTableSize;
    }

    RequireBytes(bytes, ref offset, 1);
    offset++;

    while (true)
    {
      RequireBytes(bytes, ref offset, 1);
      var blockSize = bytes[offset++];
      if (blockSize == 0)
      {
        return offset;
      }

      RequireBytes(bytes, ref offset, blockSize);
      offset += blockSize;
    }
  }

  private static void RequireBytes(byte[] bytes, ref int offset, int count)
  {
    if (offset + count > bytes.Length)
    {
      throw new InvalidOperationException("Malformed GIF data.");
    }
  }
}
