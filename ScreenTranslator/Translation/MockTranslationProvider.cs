namespace ScreenTranslator.Translation;

public sealed class MockTranslationProvider : ITranslationProvider
{
  public string Id => "mock";
  public string DisplayName => "Mock (no network)";

  public Task<string> TranslateAsync(string text, string from, string to, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (string.IsNullOrWhiteSpace(text))
      return Task.FromResult(string.Empty);

    // Placeholder until you provide real API keys/providers.
    return Task.FromResult(text);
  }
}
