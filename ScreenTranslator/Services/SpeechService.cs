using System.Collections.Frozen;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.SpeechSynthesis;
using WpfMediaPlayer = System.Windows.Media.MediaPlayer;

namespace ScreenTranslator.Services;

/// <summary>
/// Reads text aloud with the built-in Windows speech synthesis (offline, no extra packages).
/// Must be created and used on the UI thread; playback events arrive through the WPF dispatcher.
/// </summary>
public sealed class SpeechService : IDisposable
{
  private static readonly FrozenDictionary<string, string> VoiceHintMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
  {
    ["zh"] = "zh-CN",
    ["zh-Hans"] = "zh-CN",
    ["zh-CHS"] = "zh-CN",
    ["zh-Hant"] = "zh-TW",
    ["zh-CHT"] = "zh-TW",
    ["ja"] = "ja-JP",
    ["ko"] = "ko-KR",
    ["fr"] = "fr-FR",
    ["de"] = "de-DE",
    ["es"] = "es-ES",
    ["it"] = "it-IT",
    ["ru"] = "ru-RU",
    ["pt"] = "pt-BR",
    ["ar"] = "ar-SA",
    ["hi"] = "hi-IN",
    ["id"] = "id-ID",
    ["th"] = "th-TH",
    ["vi"] = "vi-VN",
  }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

  private readonly SpeechSynthesizer _synthesizer = new();
  private readonly WpfMediaPlayer _player = new();
  private int _generation;
  private bool _disposed;
  private string? _tempFile;

  public bool IsSpeaking { get; private set; }

  /// <summary>Raised when playback finishes on its own; not raised after an explicit Stop.</summary>
  public event EventHandler? SpeakCompleted;

  public SpeechService()
  {
    _player.MediaEnded += (_, _) => OnPlaybackFinished();
    _player.MediaFailed += (_, _) => OnPlaybackFinished();
  }

  public async Task SpeakAsync(string text, string? languageTag, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(text) || _disposed)
      return;

    ct.ThrowIfCancellationRequested();
    Stop();

    var generation = _generation;
    var tempFile = Path.Combine(Path.GetTempPath(), $"transtools_tts_{Guid.NewGuid():N}.wav");

    try
    {
      var voice = PickVoice(text, languageTag);
      if (voice is not null)
        _synthesizer.Voice = voice;

      using var stream = await _synthesizer.SynthesizeTextToStreamAsync(text);
      if (generation != _generation || _disposed)
        return;

      // Windows TTS voices render very quiet (typically ~-38 dB RMS); normalize
      // to a healthy peak level so speech stays audible at low system volumes.
      using (var input = stream.AsStreamForRead())
      using (var output = File.Create(tempFile))
      {
        await NormalizeWavAsync(input, output, ct);
      }

      if (generation != _generation || _disposed)
        return;

      _tempFile = tempFile;
      IsSpeaking = true;
      _player.Volume = 1.0;
      _player.Open(new Uri(tempFile));
      _player.Play();
      tempFile = null; // Ownership moved to this service.
    }
    finally
    {
      if (tempFile is not null)
        TryDeleteFile(tempFile);
    }
  }

  public void Stop()
  {
    _generation++;
    IsSpeaking = false;
    try
    {
      _player.Stop();
      _player.Close();
    }
    catch { }
    DeleteTempFile();
  }

  private void OnPlaybackFinished()
  {
    if (!IsSpeaking)
      return;

    IsSpeaking = false;
    try { _player.Close(); } catch { }
    DeleteTempFile();
    SpeakCompleted?.Invoke(this, EventArgs.Empty);
  }

  private global::Windows.Media.SpeechSynthesis.VoiceInformation? PickVoice(string text, string? languageTag)
  {
    var tag = languageTag;
    if (string.IsNullOrWhiteSpace(tag) || string.Equals(tag, "auto", StringComparison.OrdinalIgnoreCase))
      tag = DetectLanguageFromScript(text);

    if (string.IsNullOrWhiteSpace(tag))
      return null;

    tag = tag.Trim();
    if (VoiceHintMap.TryGetValue(tag, out var mapped))
      tag = mapped;

    var voices = SpeechSynthesizer.AllVoices;
    return voices.FirstOrDefault(v => string.Equals(v.Language, tag, StringComparison.OrdinalIgnoreCase))
      ?? voices.FirstOrDefault(v => LanguageMatchesPrimarySubtag(v.Language, tag));
  }

  private static bool LanguageMatchesPrimarySubtag(string voiceLanguage, string tag)
  {
    var separator = tag.IndexOf('-');
    var primary = separator > 0 ? tag[..separator] : tag;
    return string.Equals(voiceLanguage, primary, StringComparison.OrdinalIgnoreCase)
      || voiceLanguage.StartsWith(primary + "-", StringComparison.OrdinalIgnoreCase);
  }

  private const float NormalizeTargetPeak = 0.85f;
  private const float NormalizeMaxGain = 8f;
  private const int TargetSampleRate = 44100;

  /// <summary>
  /// Converts the synthesized WAV (16 kHz mono) to 44.1 kHz stereo with peak
  /// normalization. Media Foundation on some machines (e.g. certain Realtek
  /// drivers) renders 16 kHz mono silently, so the format must be converted.
  /// </summary>
  private static async Task NormalizeWavAsync(Stream input, Stream output, CancellationToken ct)
  {
    using var buffer = new MemoryStream();
    await input.CopyToAsync(buffer, ct);
    var bytes = buffer.ToArray();

    var (dataOffset, dataLength, sampleRate, channels) = ParseWav(bytes);
    if (dataLength <= 1)
      return; // Unrecognized layout; pass through unchanged.

    var count = dataLength / 2;
    var peak = 0;
    for (var i = 0; i < count; i++)
    {
      var v = Math.Abs(BitConverter.ToInt16(bytes, dataOffset + i * 2));
      if (v > peak)
        peak = v;
    }

    var gain = Math.Min(NormalizeTargetPeak * short.MaxValue / Math.Max(peak, 1), NormalizeMaxGain);
    var samples = new short[count];
    for (var i = 0; i < count; i++)
    {
      var scaled = (int)Math.Round(BitConverter.ToInt16(bytes, dataOffset + i * 2) * gain);
      samples[i] = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }

    byte[] data;
    if (sampleRate == TargetSampleRate && channels == 2)
    {
      data = new byte[dataLength];
      Buffer.BlockCopy(bytes, dataOffset, data, 0, dataLength);
    }
    else
    {
      var outCount = (int)((long)count * TargetSampleRate / sampleRate);
      data = new byte[outCount * 4]; // 16-bit stereo
      for (var j = 0; j < outCount; j++)
      {
        var src = j * (double)sampleRate / TargetSampleRate;
        var i0 = (int)src;
        var i1 = Math.Min(i0 + 1, count - 1);
        var frac = src - i0;
        var v = (short)Math.Clamp((int)Math.Round(samples[i0] + (samples[i1] - samples[i0]) * frac), short.MinValue, short.MaxValue);
        var lo = (byte)(v & 0xFF);
        var hi = (byte)((v >> 8) & 0xFF);
        data[j * 4] = lo;
        data[j * 4 + 1] = hi;
        data[j * 4 + 2] = lo;
        data[j * 4 + 3] = hi;
      }
    }

    var header = new byte[44];
    WriteAscii(header, 0, "RIFF");
    WriteInt(header, 4, 36 + data.Length);
    WriteAscii(header, 8, "WAVE");
    WriteAscii(header, 12, "fmt ");
    WriteInt(header, 16, 16);
    WriteShort(header, 20, 1); // PCM
    WriteShort(header, 22, 2);
    WriteInt(header, 24, TargetSampleRate);
    WriteInt(header, 28, TargetSampleRate * 4);
    WriteShort(header, 32, 4);
    WriteShort(header, 34, 16);
    WriteAscii(header, 36, "data");
    WriteInt(header, 40, data.Length);

    await output.WriteAsync(header, ct);
    await output.WriteAsync(data, ct);
  }

  /// <summary>Returns the offset/length of the 16-bit PCM data chunk plus sample rate and channel count.</summary>
  private static (int DataOffset, int DataLength, int SampleRate, int Channels) ParseWav(byte[] bytes)
  {
    if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F')
      return (0, 0, 0, 0);

    var rate = 0;
    var channels = 0;
    var pos = 12; // Skip RIFF header and "WAVE" tag.
    while (pos + 8 <= bytes.Length)
    {
      var id = BitConverter.ToInt32(bytes, pos);
      var size = BitConverter.ToInt32(bytes, pos + 4);
      if (id == 0x20746D66) // "fmt "
      {
        if (pos + 8 + 16 > bytes.Length)
          return (0, 0, 0, 0);
        channels = BitConverter.ToInt16(bytes, pos + 8 + 2);
        rate = BitConverter.ToInt32(bytes, pos + 8 + 4);
      }
      else if (id == 0x61746164) // "data"
      {
        var length = Math.Min(size, bytes.Length - pos - 8);
        return (pos + 8, length, rate, channels);
      }

      pos += 8 + size + (size & 1); // Chunks are word-aligned.
    }

    return (0, 0, 0, 0);
  }

  private static void WriteAscii(byte[] b, int i, string s)
  {
    for (var k = 0; k < s.Length; k++)
      b[i + k] = (byte)s[k];
  }

  private static void WriteInt(byte[] b, int i, int v)
  {
    b[i] = (byte)v;
    b[i + 1] = (byte)(v >> 8);
    b[i + 2] = (byte)(v >> 16);
    b[i + 3] = (byte)(v >> 24);
  }

  private static void WriteShort(byte[] b, int i, int v)
  {
    b[i] = (byte)v;
    b[i + 1] = (byte)(v >> 8);
  }

  // Kana must be checked before CJK: Japanese kanji fall into the CJK range.
  private static string? DetectLanguageFromScript(string text)
  {
    foreach (var ch in text)
    {
      if (ch >= '\u3040' && ch <= '\u30FF')
        return "ja";
      if (ch >= '\uAC00' && ch <= '\uD7AF')
        return "ko";
      if (ch >= '\u0400' && ch <= '\u04FF')
        return "ru";
      if (ch >= '\u4E00' && ch <= '\u9FFF')
        return "zh";
    }

    return null;
  }

  private void DeleteTempFile()
  {
    var file = _tempFile;
    _tempFile = null;
    if (file is not null)
      TryDeleteFile(file);
  }

  private static void TryDeleteFile(string path)
  {
    try { File.Delete(path); } catch { }
  }

  public void Dispose()
  {
    if (_disposed)
      return;

    _disposed = true;
    Stop();
    _synthesizer.Dispose();
  }
}
