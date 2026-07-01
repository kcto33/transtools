using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ScreenTranslator.Models;

namespace ScreenTranslator.Services;

public sealed class FloatingNoteStorageService
{
  private const string AppFolderName = "transtools";
  private const string NotesFolderName = "notes";
  private readonly AppSettings _settings;
  private readonly Func<DateTime> _now;

  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
  };

  public FloatingNoteStorageService(AppSettings settings, Func<DateTime>? now = null)
  {
    _settings = settings;
    _settings.FloatingNotes ??= new FloatingNoteSettings();
    _now = now ?? (() => DateTime.Now);
  }

  public string ResolveNoteDirectory()
  {
    var configured = _settings.FloatingNotes?.SaveDirectory;
    if (!string.IsNullOrWhiteSpace(configured))
      return configured.Trim();

    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    return Path.Combine(appData, AppFolderName, NotesFolderName);
  }

  public string GenerateNewNotePath()
  {
    var fileName = $"Note_{_now():yyyyMMdd_HHmmss}.rtf";
    return Path.Combine(ResolveNoteDirectory(), fileName);
  }

  public string GenerateUniqueNewNotePath()
  {
    var basePath = GenerateNewNotePath();
    if (!File.Exists(basePath))
      return basePath;

    var dir = Path.GetDirectoryName(basePath) ?? ResolveNoteDirectory();
    var stem = Path.GetFileNameWithoutExtension(basePath);
    for (var i = 2; i < 1000; i++)
    {
      var candidate = Path.Combine(dir, $"{stem}_{i}.rtf");
      if (!File.Exists(candidate))
        return candidate;
    }

    return Path.Combine(dir, $"{stem}_{Guid.NewGuid():N}.rtf");
  }

  public void SaveRtf(string path, string rtf, FloatingNoteMetadata? metadata = null)
  {
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
    {
      Directory.CreateDirectory(dir);
    }

    File.WriteAllText(path, rtf, Encoding.UTF8);

    if (metadata is not null)
    {
      SaveMetadata(path, metadata);
    }
  }

  public string LoadRtf(string path)
  {
    if (!File.Exists(path))
      return string.Empty;

    return File.ReadAllText(path, Encoding.UTF8);
  }

  public void SaveMetadata(string rtfPath, FloatingNoteMetadata metadata)
  {
    var metadataPath = GetMetadataPath(rtfPath);
    var dir = Path.GetDirectoryName(metadataPath);
    if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
    {
      Directory.CreateDirectory(dir);
    }

    var json = JsonSerializer.Serialize(metadata, JsonOptions);
    File.WriteAllText(metadataPath, json, Encoding.UTF8);
  }

  public FloatingNoteMetadata LoadMetadata(string rtfPath)
  {
    var metadataPath = GetMetadataPath(rtfPath);
    if (!File.Exists(metadataPath))
      return new FloatingNoteMetadata();

    try
    {
      var json = File.ReadAllText(metadataPath, Encoding.UTF8);
      return JsonSerializer.Deserialize<FloatingNoteMetadata>(json, JsonOptions) ?? new FloatingNoteMetadata();
    }
    catch
    {
      return new FloatingNoteMetadata();
    }
  }

  public IReadOnlyList<FloatingNoteFileInfo> ListSavedNotes()
  {
    var dir = ResolveNoteDirectory();
    if (!Directory.Exists(dir))
      return [];

    return Directory
      .EnumerateFiles(dir, "*.rtf", SearchOption.TopDirectoryOnly)
      .Select(path =>
      {
        var info = new FileInfo(path);
        return new FloatingNoteFileInfo(
          path,
          info.Name,
          info.LastWriteTime,
          BuildPreview(path));
      })
      .OrderByDescending(note => note.LastWriteTime)
      .ToArray();
  }

  public static string GetMetadataPath(string rtfPath)
  {
    return Path.ChangeExtension(rtfPath, ".note.json");
  }

  public bool DeleteSavedNote(string rtfPath)
  {
    if (!IsDeletableNotePath(rtfPath))
      return false;

    if (!File.Exists(rtfPath))
      return false;

    File.Delete(rtfPath);

    var metadataPath = GetMetadataPath(rtfPath);
    if (File.Exists(metadataPath))
    {
      File.Delete(metadataPath);
    }

    return true;
  }

  private bool IsDeletableNotePath(string rtfPath)
  {
    if (string.IsNullOrWhiteSpace(rtfPath))
      return false;

    if (!string.Equals(Path.GetExtension(rtfPath), ".rtf", StringComparison.OrdinalIgnoreCase))
      return false;

    var noteDir = Path.GetFullPath(ResolveNoteDirectory());
    var fullPath = Path.GetFullPath(rtfPath);
    var relative = Path.GetRelativePath(noteDir, fullPath);

    return !relative.StartsWith("..", StringComparison.Ordinal) &&
           !Path.IsPathRooted(relative);
  }

  private string BuildPreview(string path)
  {
    try
    {
      var rtf = File.ReadAllText(path, Encoding.UTF8);
      var text = DecodeRtfPreview(rtf);
      return text.Length > 120 ? text[..120] : text;
    }
    catch
    {
      return string.Empty;
    }
  }

  internal static string DecodeRtfPreview(string rtf)
  {
    if (string.IsNullOrWhiteSpace(rtf))
      return string.Empty;

    rtf = RemoveRtfGroup(rtf, "fonttbl");
    rtf = RemoveRtfGroup(rtf, "colortbl");
    rtf = RemoveRtfGroup(rtf, "stylesheet");
    rtf = RemoveRtfGroup(rtf, "info");

    rtf = DecodeUnicodeEscapes(rtf);

    var text = Regex.Replace(rtf, @"\\'[0-9a-fA-F]{2}", " ");
    text = Regex.Replace(text, @"\\par[d]?", Environment.NewLine);
    text = Regex.Replace(text, @"\\[a-zA-Z]+\d* ?", string.Empty);
    text = text.Replace("{", string.Empty).Replace("}", string.Empty);
    text = text.Replace("\\", string.Empty);
    text = Regex.Replace(text, @"\s+", " ");
    return text.Trim();
  }

  private static string DecodeUnicodeEscapes(string rtf)
  {
    var fallbackCount = 1;
    var builder = new StringBuilder(rtf.Length);

    for (var i = 0; i < rtf.Length; i++)
    {
      if (rtf[i] != '\\')
      {
        builder.Append(rtf[i]);
        continue;
      }

      if (i + 2 < rtf.Length && rtf[i + 1] == 'u' && (char.IsDigit(rtf[i + 2]) || rtf[i + 2] == '-'))
      {
        var valueStart = i + 2;
        var valueEnd = valueStart;
        if (rtf[valueEnd] == '-')
          valueEnd++;

        while (valueEnd < rtf.Length && char.IsDigit(rtf[valueEnd]))
        {
          valueEnd++;
        }

        if (int.TryParse(rtf[valueStart..valueEnd], out var value))
        {
          if (value < 0)
            value += 65536;

          builder.Append(char.ConvertFromUtf32(value));

          if (valueEnd < rtf.Length && rtf[valueEnd] == ' ')
            valueEnd++;

          i = Math.Min(rtf.Length - 1, valueEnd + Math.Max(0, fallbackCount) - 1);
          continue;
        }
      }

      if (i + 3 < rtf.Length && rtf[i + 1] == 'u' && rtf[i + 2] == 'c' && char.IsDigit(rtf[i + 3]))
      {
        var valueStart = i + 3;
        var valueEnd = valueStart;
        while (valueEnd < rtf.Length && char.IsDigit(rtf[valueEnd]))
        {
          valueEnd++;
        }

        if (int.TryParse(rtf[valueStart..valueEnd], out var parsedFallbackCount))
        {
          fallbackCount = Math.Clamp(parsedFallbackCount, 0, 8);
        }
      }

      builder.Append(rtf[i]);
    }

    return builder.ToString();
  }

  private static string RemoveRtfGroup(string rtf, string groupName)
  {
    var marker = @"{\" + groupName;
    var index = rtf.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    while (index >= 0)
    {
      var depth = 0;
      var end = index;
      for (; end < rtf.Length; end++)
      {
        if (rtf[end] == '{')
        {
          depth++;
        }
        else if (rtf[end] == '}')
        {
          depth--;
          if (depth == 0)
          {
            end++;
            break;
          }
        }
      }

      if (end <= index || end > rtf.Length)
        break;

      rtf = rtf.Remove(index, end - index);
      index = rtf.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    }

    return rtf;
  }
}

public sealed record FloatingNoteFileInfo(
  string Path,
  string FileName,
  DateTime LastWriteTime,
  string Preview);

public sealed class FloatingNoteMetadata
{
  public double Left { get; set; } = double.NaN;
  public double Top { get; set; } = double.NaN;
  public double Width { get; set; } = 306;
  public double Height { get; set; } = 430;
  public bool IsPinned { get; set; } = true;
  public string Color { get; set; } = "#FFF7CF";
}
