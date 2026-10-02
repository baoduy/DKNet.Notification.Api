using System.Text.Json;
using System.Text.Json.Serialization;

namespace DKNet.Notification.Share;

/// <summary>
///     Provides shared constants used throughout the application.
/// </summary>
public static class SharedConsts
{
    #region Properties

    /// <summary>
    ///     Gets the connection string name for Azure Service Bus.
    /// </summary>
    public static string AzureBusConnectionString => "AzureBus";

    /// <summary>
    ///     Gets the connection string name for the application database.
    /// </summary>
    public static string DbConnectionString => "AppDb";

    /// <summary>
    ///     Gets the connection string name for Redis cache.
    /// </summary>
    public static string RedisConnectionString => "Redis";

    /// <summary>
    ///     Gets the system account identifier.
    /// </summary>
    public static string SystemAccount => "System";

    /// <summary>
    ///     Gets the demonstration authentication provider's fixed acting-user identity. Uses the
    ///     <c>.invalid</c> TLD (reserved by RFC 2606) so it can never resolve to a real address.
    /// </summary>
    public static string DemoAccount => "demo-user@not-a-real-identity.invalid";

    #endregion

    /// <summary>
    ///     The name of the API application.
    /// </summary>
    public const string ApiName = "DKNet.Notification.Api";

    /// <summary>
    ///     Gets the default JSON serializer options for the application.
    /// </summary>
    public static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}