using System.Reflection;
using Refit;

namespace DKNet.Notification.Client;

/// <summary>
/// Refuses, before it leaves the process, any request whose client method takes an
/// <c>X-Idempotency-Key</c> header parameter but was called with a null or blank key.
/// </summary>
internal sealed class IdempotencyKeyGuard : DelegatingHandler
{
    public const string HeaderName = "X-Idempotency-Key";

    private static readonly HttpRequestOptionsKey<Type> InterfaceTypeKey = new(HttpRequestMessageOptions.InterfaceType);

    private static readonly HttpRequestOptionsKey<string> MethodNameKey = new(HttpRequestMessageOptions.MethodName);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (RequiresKey(request) &&
            (!request.Headers.TryGetValues(HeaderName, out var values) || string.IsNullOrWhiteSpace(string.Concat(values))))
        {
            throw new ArgumentException(
                $"An idempotency key is required: pass a non-blank value for the {HeaderName} header.");
        }

        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>True when the Refit method that built <paramref name="request" /> declares the key header.</summary>
    private static bool RequiresKey(HttpRequestMessage request) =>
        request.Options.TryGetValue(InterfaceTypeKey, out var client) &&
        request.Options.TryGetValue(MethodNameKey, out var method) &&
        client.GetMethods()
            .Where(m => m.Name == method)
            .SelectMany(m => m.GetParameters())
            .Any(p => string.Equals(
                p.GetCustomAttribute<HeaderAttribute>()?.Header, HeaderName, StringComparison.OrdinalIgnoreCase));
}
