namespace ScreenTranslator.Translation;

public interface ITranslationProvider
{
  string Id { get; }
  string DisplayName { get; }

  Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct);
}
