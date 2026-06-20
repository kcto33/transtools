using System.IO;
using ScreenTranslator.Models;
using ScreenTranslator.Services;

using Xunit;

namespace ScreenTranslator.Tests;

public sealed class FloatingNoteStorageServiceTests
{
  [Fact]
  public void ResolveNoteDirectory_UsesConfiguredDirectory_WhenSet()
  {
    var configured = Path.Combine(AppContext.BaseDirectory, "FloatingNoteTests", Guid.NewGuid().ToString("N"));
    var settings = new AppSettings
    {
      FloatingNotes = new FloatingNoteSettings
      {
        SaveDirectory = configured
      }
    };

    var storage = new FloatingNoteStorageService(settings);

    Assert.Equal(configured, storage.ResolveNoteDirectory());
  }

  [Fact]
  public void GenerateNewNotePath_UsesTimestampedRtfFileName()
  {
    var configured = Path.Combine(AppContext.BaseDirectory, "FloatingNoteTests", Guid.NewGuid().ToString("N"));
    var settings = new AppSettings
    {
      FloatingNotes = new FloatingNoteSettings
      {
        SaveDirectory = configured
      }
    };
    var storage = new FloatingNoteStorageService(settings, () => new DateTime(2026, 6, 2, 20, 45, 12));

    var path = storage.GenerateNewNotePath();

    Assert.Equal(Path.Combine(configured, "Note_20260602_204512.rtf"), path);
  }

  [Fact]
  public void GenerateUniqueNewNotePath_AddsSuffix_WhenTimestampNameAlreadyExists()
  {
    var configured = Path.Combine(AppContext.BaseDirectory, "FloatingNoteTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(configured);
    File.WriteAllText(Path.Combine(configured, "Note_20260602_204512.rtf"), "{\\rtf1 existing}");
    var settings = new AppSettings
    {
      FloatingNotes = new FloatingNoteSettings
      {
        SaveDirectory = configured
      }
    };
    var storage = new FloatingNoteStorageService(settings, () => new DateTime(2026, 6, 2, 20, 45, 12));

    var path = storage.GenerateUniqueNewNotePath();

    Assert.Equal(Path.Combine(configured, "Note_20260602_204512_2.rtf"), path);
  }

  [Fact]
  public void ListSavedNotes_ReturnsRtfFilesNewestFirst()
  {
    var configured = Path.Combine(AppContext.BaseDirectory, "FloatingNoteTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(configured);
    var older = Path.Combine(configured, "Note_20260602_200000.rtf");
    var newer = Path.Combine(configured, "Note_20260602_210000.rtf");
    var ignored = Path.Combine(configured, "Note_20260602_220000.txt");
    File.WriteAllText(older, "{\\rtf1 older}");
    File.WriteAllText(newer, "{\\rtf1 newer}");
    File.WriteAllText(ignored, "ignored");
    File.SetLastWriteTime(older, new DateTime(2026, 6, 2, 20, 0, 0));
    File.SetLastWriteTime(newer, new DateTime(2026, 6, 2, 21, 0, 0));

    var settings = new AppSettings
    {
      FloatingNotes = new FloatingNoteSettings
      {
        SaveDirectory = configured
      }
    };
    var storage = new FloatingNoteStorageService(settings);

    var notes = storage.ListSavedNotes().ToArray();

    Assert.Equal([newer, older], notes.Select(note => note.Path).ToArray());
  }

  [Fact]
  public void DecodeRtfPreview_RemovesFontTable()
  {
    const string rtf = @"{\rtf1{\fonttbl{\f0 Times New Roman;}{\f1 Segoe UI;}}\f1\fs24 Hello note\par}";

    var preview = FloatingNoteStorageService.DecodeRtfPreview(rtf);

    Assert.Equal("Hello note", preview);
  }

  [Fact]
  public void DecodeRtfPreview_DecodesUnicodeEscapes()
  {
    const string rtf = @"{\rtf1\ansi\uc1 \u35760?\u31508?\u35760?...}";

    var preview = FloatingNoteStorageService.DecodeRtfPreview(rtf);

    Assert.Equal("记笔记...", preview);
  }
}
