using System.Net;
using System.Text;
using ClaimPartners.Users.Providers.Graph;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace ClaimPartners.Users.Tests.Shared;

/// <summary>
/// Fake Microsoft Graph: answers each request with the next queued response and records what was sent.
/// </summary>
internal sealed class StubGraphHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public StubGraphHandler Returns(HttpStatusCode status, string body = "")
    {
        responses.Enqueue((status, body));
        return this;
    }

    public StubGraphHandler ReturnsError(HttpStatusCode status, string code, string message) =>
        Returns(status, $$"""{ "error": { "code": "{{code}}", "message": "{{message}}" } }""");

    public PartnerUsersGraphClient CreateClient() =>
        new(new GraphServiceClient(new HttpClient(this), new AnonymousAuthenticationProvider()));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));

        if (!responses.TryDequeue(out var response))
        {
            throw new InvalidOperationException($"Unexpected Graph request: {request.Method} {request.RequestUri}");
        }

        return new HttpResponseMessage(response.Status)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body)
{
    public string Path => Uri.AbsolutePath;

    public string Query => Uri.UnescapeDataString(Uri.Query);
}
