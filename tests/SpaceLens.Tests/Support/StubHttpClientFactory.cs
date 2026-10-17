using System.Net.Http;

namespace SpaceLens.Tests.Support;

/// <summary>Hands out clients over one shared handler, like the real factory does within a handler lifetime.</summary>
public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
