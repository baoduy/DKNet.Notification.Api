using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using DKNet.Notification.Client;

namespace DKNet.Notification.App.Tests.Client.Support;

/// <summary>One endpoint the API serves: HTTP method and route, normalised (<c>v1</c> for the version
/// segment, no route constraints, no trailing slash).</summary>
public sealed record ApiEndpoint(string Method, string Path)
{
    public override string ToString() => $"{Method} {Path}";
}

/// <summary>One method of the client, with the route its Refit attribute declares (null when none).</summary>
public sealed record ClientMethod(string Name, string? Method, string? Path);

/// <summary>Outcome of comparing the API's endpoints with the client's methods.</summary>
public sealed record ParityReport(
    IReadOnlyList<string> EndpointsWithoutClientMethod,
    IReadOnlyList<string> EndpointsWithMoreThanOneClientMethod,
    IReadOnlyList<string> ClientMethodsWithoutEndpoint)
{
    public bool IsInStep =>
        EndpointsWithoutClientMethod.Count == 0 &&
        EndpointsWithMoreThanOneClientMethod.Count == 0 &&
        ClientMethodsWithoutEndpoint.Count == 0;

    /// <summary>One line per kind of drift, naming every offending endpoint or client method.</summary>
    public string FailureMessage => string.Join("\n", new[]
        {
            Line("Endpoints with no client method", EndpointsWithoutClientMethod),
            Line("Endpoints with more than one client method", EndpointsWithMoreThanOneClientMethod),
            Line("Client methods that match no endpoint", ClientMethodsWithoutEndpoint)
        }.Where(l => l is not null));

    private static string? Line(string label, IReadOnlyList<string> names) =>
        names.Count == 0 ? null : $"{label}: {string.Join(", ", names)}";
}

/// <summary>
/// The endpoint check (DRK-1789 R4): every API endpoint has exactly one client method and every client
/// method matches exactly one endpoint. Addresses the API serves that are not API endpoints — the health
/// probes, the OpenAPI document and the docs browser — are left out on both sides.
/// </summary>
public static partial class EndpointParity
{
    /// <summary>The addresses that are not API endpoints. Each covers its own path and every path under it.</summary>
    public static readonly IReadOnlyDictionary<string, string> NonApiAddresses =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["public status probe"] = "/healthz",
            ["root status probe"] = "/",
            ["detailed health report"] = "/healthz/detail",
            ["OpenAPI document"] = "/openapi/{documentName}.json",
            ["docs browser"] = "/docs"
        };

    public static bool IsNonApiAddress(string path) =>
        NonApiAddresses.Values.Any(address =>
            string.Equals(path, address, StringComparison.OrdinalIgnoreCase) ||
            (address != "/" && path.StartsWith(address + "/", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Every route endpoint the running API serves, normalised; non-API addresses included.</summary>
    public static IReadOnlyList<ApiEndpoint> ApiEndpoints(IServiceProvider apiServices) =>
        apiServices.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e =>
            {
                var path = Normalise(e.RoutePattern.RawText ?? string.Empty);
                var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                return methods is { Count: > 0 }
                    ? methods.Select(m => new ApiEndpoint(m.ToUpperInvariant(), path))
                    : [new ApiEndpoint("ANY", path)];
            })
            .Distinct()
            .ToList();

    /// <summary>Every public method of every public interface in the client assembly.</summary>
    public static IReadOnlyList<ClientMethod> ClientMethods() =>
        typeof(IProductsClient).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface)
            .SelectMany(t => t.GetMethods().Select(m =>
            {
                var route = m.GetCustomAttribute<Refit.HttpMethodAttribute>(inherit: true);
                return new ClientMethod(
                    $"{t.Name}.{m.Name}",
                    route?.Method.Method.ToUpperInvariant(),
                    route is null ? null : Normalise(route.Path.Split('?')[0]));
            }))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    public static ParityReport Compare(IEnumerable<ApiEndpoint> apiEndpoints, IEnumerable<ClientMethod> clientMethods)
    {
        var endpoints = apiEndpoints.Where(e => !IsNonApiAddress(e.Path)).Distinct().ToList();
        var methods = clientMethods.ToList();

        var matches = endpoints.ToDictionary(
            e => e,
            e => methods.Where(m => m.Method is not null && Key(m.Method, m.Path!) == Key(e.Method, e.Path))
                .Select(m => m.Name)
                .ToList());

        return new ParityReport(
            matches.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key.ToString())
                .OrderBy(n => n, StringComparer.Ordinal).ToList(),
            matches.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key} ({string.Join(", ", kv.Value)})")
                .OrderBy(n => n, StringComparer.Ordinal).ToList(),
            methods.Where(m => m.Method is null ||
                               endpoints.Count(e => Key(e.Method, e.Path) == Key(m.Method, m.Path!)) != 1)
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// <c>v{version:apiVersion}</c> becomes <c>v1</c>; <c>{id:guid}</c>, <c>{name?}</c> and <c>{**rest}</c>
    /// become <c>{id}</c>, <c>{name}</c> and <c>{rest}</c> (§9 Q2); a trailing slash is dropped.
    /// </summary>
    public static string Normalise(string route)
    {
        var path = "/" + route.Trim().TrimStart('/');
        path = path.Replace("{version:apiVersion}", "1", StringComparison.OrdinalIgnoreCase);
        path = RouteParameter().Replace(path, "{$1}");
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    /// <summary>Route parameter names do not decide identity: <c>/products/{id}</c> and
    /// <c>/products/{productId}</c> are the same endpoint.</summary>
    private static string Key(string method, string path) =>
        $"{method} {RouteParameter().Replace(path, "{}")}".ToUpperInvariant();

    [GeneratedRegex(@"\{\**([A-Za-z_][A-Za-z0-9_]*)[^}]*\}")]
    private static partial Regex RouteParameter();
}
