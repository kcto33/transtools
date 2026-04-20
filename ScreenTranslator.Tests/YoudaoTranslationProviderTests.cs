using System.Net;
using System.Net.Http;
using ScreenTranslator.Translation;
using Xunit;

namespace ScreenTranslator.Tests;

public sealed class YoudaoTranslationProviderTests
{
  [Fact]
  public async Task TranslateAsync_DoesNotSendDomainFields_WhenNotConfigured()
  {
    var handler = new CaptureHandler();
    using var httpClient = new HttpClient(handler);
    var provider = new YoudaoTranslationProvider(
      "https://openapi.youdao.test/api",
      "app-id",
      "app-secret",
      domain: null,
      rejectFallback: null,
      httpClient: httpClient);

    var translated = await provider.TranslateAsync("hello world", "en", "zh-Hans", CancellationToken.None);

    Assert.Equal("你好", translated);
    Assert.NotNull(handler.LastRequestBody);
    Assert.Contains("q=hello+world", handler.LastRequestBody, StringComparison.Ordinal);
    Assert.DoesNotContain("domain=", handler.LastRequestBody, StringComparison.Ordinal);
    Assert.DoesNotContain("rejectFallback=", handler.LastRequestBody, StringComparison.Ordinal);
  }

  [Fact]
  public async Task TranslateAsync_SendsDomainFields_WhenConfigured()
  {
    var handler = new CaptureHandler();
    using var httpClient = new HttpClient(handler);
    var provider = new YoudaoTranslationProvider(
      "https://openapi.youdao.test/api",
      "app-id",
      "app-secret",
      domain: "computers",
      rejectFallback: true,
      httpClient: httpClient);

    var translated = await provider.TranslateAsync("hello world", "en", "zh-Hans", CancellationToken.None);

    Assert.Equal("你好", translated);
    Assert.NotNull(handler.LastRequestBody);
    Assert.Contains("domain=computers", handler.LastRequestBody, StringComparison.Ordinal);
    Assert.Contains("rejectFallback=true", handler.LastRequestBody, StringComparison.Ordinal);
  }

  private sealed class CaptureHandler : HttpMessageHandler
  {
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      LastRequestBody = request.Content is null
        ? null
        : await request.Content.ReadAsStringAsync(cancellationToken);

      return new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent("""{"errorCode":"0","translation":["你好"]}"""),
      };
    }
  }
}
