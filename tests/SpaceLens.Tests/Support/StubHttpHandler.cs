using System.Net;
using System.Net.Http;
using System.Text;

namespace SpaceLens.Tests.Support;

/// <summary>Answers HTTP requests from a script and records what was sent.</summary>
public sealed class StubHttpHandler(params Func<HttpRequestMessage, string, HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private int _next;

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        var responder = responses[Math.Min(_next++, responses.Length - 1)];
        return responder(request, body);
    }
}
