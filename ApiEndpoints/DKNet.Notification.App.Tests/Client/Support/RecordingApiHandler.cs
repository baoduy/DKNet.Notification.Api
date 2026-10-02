using System.Net.Http.Headers;
using System.Text;

namespace DKNet.Notification.App.Tests.Client.Support;

/// <summary>
/// In-memory stand-in for the API in the <c>@unit</c> client scenarios: records every request that
/// leaves the client and answers each with a fixed JSON body.
/// </summary>
public sealed class RecordingApiHandler(string responseJson) : HttpMessageHandler
{
    public List<ReceivedRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(new ReceivedRequest(
            request.Method.Method,
            request.RequestUri?.AbsolutePath ?? string.Empty,
            request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("X-Idempotency-Key", out var keys) ? string.Join(",", keys) : null));
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(responseJson, Encoding.UTF8, new MediaTypeHeaderValue("application/json"))
        };
        return Task.FromResult(response);
    }
}

/// <summary>A downstream system's own handler that attaches its bearer token to every request.</summary>
public sealed class BearerTokenHandler(string token) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, cancellationToken);
    }
}
