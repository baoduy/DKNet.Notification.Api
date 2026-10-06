using System.Reflection;
using System.Text.RegularExpressions;
using DKNet.Notification.Client;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace DKNet.Notification.App.BDDTests.Features.Client.Steps;

/// <summary>One route the service serves: HTTP method and path, normalised (<c>v1</c>, no constraints, no trailing slash).</summary>
public sealed record LiveRoute(string Method, string Path)
{
    public override string ToString() => $"{Method} {Path}";
}

/// <summary>One operation of the client, with the route its Refit attribute declares (null when none).</summary>
public sealed record ClientOperation(string Name, string? Method, string? Path);

/// <summary>
/// The route parity check (DRK-2141 §3): every live caller route has exactly one client operation and every client
/// operation matches exactly one live caller route. The health route is a platform route, never a caller route.
/// Taken from the template's check (commit <c>3bc0940</c>, <c>App.Tests/Client/Support/EndpointParity.cs</c>).
/// </summary>
public static partial class EndpointParity
{
    public const string HealthRoute = "/healthz";

    /// <summary>Every route endpoint the running service serves, normalised; the health route included.</summary>
    public static IReadOnlyList<LiveRoute> LiveRoutes(IServiceProvider services) =>
        services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e =>
            {
                var path = Normalise(e.RoutePattern.RawText ?? string.Empty);
                var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
                return methods is { Count: > 0 }
                    ? methods.Select(m => new LiveRoute(m.ToUpperInvariant(), path))
                    : [new LiveRoute("ANY", path)];
            })
            .Distinct()
            .ToList();

    /// <summary>Every method of every public interface of the client package.</summary>
    public static IReadOnlyList<ClientOperation> ClientOperations() =>
        typeof(INotificationClient).Assembly.GetExportedTypes()
            .Where(t => t.IsInterface)
            .SelectMany(t => t.GetMethods().Select(m =>
            {
                var route = m.GetCustomAttribute<Refit.HttpMethodAttribute>(inherit: true);
                return new ClientOperation(
                    $"{t.Name}.{m.Name}",
                    route?.Method.Method.ToUpperInvariant(),
                    route is null ? null : Normalise(route.Path.Split('?')[0]));
            }))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The live caller routes: every route but the health route.</summary>
    public static IReadOnlyList<LiveRoute> CallerRoutes(IEnumerable<LiveRoute> routes) =>
        routes.Where(r => !string.Equals(r.Path, HealthRoute, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>The client operations that match <paramref name="route" />.</summary>
    public static IReadOnlyList<string> OperationsFor(LiveRoute route, IEnumerable<ClientOperation> operations) =>
        operations.Where(o => o.Method is not null && Key(o.Method, o.Path!) == Key(route.Method, route.Path))
            .Select(o => o.Name)
            .ToList();

    /// <summary>The live routes that match <paramref name="operation" />; none when it declares no route.</summary>
    public static IReadOnlyList<LiveRoute> RoutesFor(ClientOperation operation, IEnumerable<LiveRoute> routes) =>
        operation.Method is null
            ? []
            : routes.Where(r => Key(r.Method, r.Path) == Key(operation.Method, operation.Path!)).ToList();

    /// <summary>
    /// <c>v{version:apiVersion}</c> becomes <c>v1</c>; <c>{id:guid}</c>, <c>{name?}</c> and <c>{**rest}</c> become
    /// <c>{id}</c>, <c>{name}</c> and <c>{rest}</c>; a trailing slash is dropped.
    /// </summary>
    public static string Normalise(string route)
    {
        var path = "/" + route.Trim().TrimStart('/');
        path = path.Replace("{version:apiVersion}", "1", StringComparison.OrdinalIgnoreCase);
        path = RouteParameter().Replace(path, "{$1}");
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    /// <summary>Parameter names do not decide identity: <c>/notifications/{id}</c> is <c>/notifications/{notificationId}</c>.</summary>
    private static string Key(string method, string path) =>
        $"{method} {RouteParameter().Replace(path, "{}")}".ToUpperInvariant();

    [GeneratedRegex(@"\{\**([A-Za-z_][A-Za-z0-9_]*)[^}]*\}")]
    private static partial Regex RouteParameter();
}
