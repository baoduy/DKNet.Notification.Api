using System.Text.Json;
using DKNet.Notification.AppServices.Notifications;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DKNet.Notification.Api.ApiEndpoints.Notifications;

/// <summary>
///     The body of a send call, read here rather than by the framework: a framework bind failure answers before any
///     endpoint filter runs, with no error code, no log entry and no count. Reading never fails;
///     <see cref="SendNotificationBodyFilter" /> decides what each outcome answers.
/// </summary>
internal sealed class SendNotificationBody
{
    #region Fields

    /// <summary>The largest body the call takes (64 KB).</summary>
    public const int MaxBytes = 65_536;

    private static readonly SendNotificationBody TooLarge = new(null, isTooLarge: true);

    #endregion

    #region Constructors

    private SendNotificationBody(SendNotificationRequest? request, bool isTooLarge)
    {
        Request = request;
        IsTooLarge = isTooLarge;
    }

    #endregion

    #region Properties

    /// <summary>Gets the body read as JSON, or <see langword="null" /> when it is not JSON of the call's shape.</summary>
    public SendNotificationRequest? Request { get; }

    /// <summary>Gets whether the body is larger than <see cref="MaxBytes" />.</summary>
    public bool IsTooLarge { get; }

    #endregion

    #region Methods

    /// <summary>
    ///     Reads at most <see cref="MaxBytes" /> + 1 bytes, so the limit holds on the bytes read whether or not the
    ///     call declares a <c>Content-Length</c>.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>The body; never <see langword="null" />.</returns>
    public static async ValueTask<SendNotificationBody?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A declared size over the limit is refused unread: past the host's own limit, reading it would throw.
        if (context.Request.ContentLength > MaxBytes)
        {
            return TooLarge;
        }

        var buffer = new byte[MaxBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length &&
               (read = await context.Request.Body.ReadAsync(buffer.AsMemory(length), context.RequestAborted)) > 0)
        {
            length += read;
        }

        if (length > MaxBytes)
        {
            return TooLarge;
        }

        var json = context.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        try
        {
            return new SendNotificationBody(
                JsonSerializer.Deserialize<SendNotificationRequest>(buffer.AsSpan(0, length), json),
                isTooLarge: false);
        }
        catch (JsonException)
        {
            // Not JSON, or a field of the wrong type, such as a parameter value that is not a string.
            return new SendNotificationBody(null, isTooLarge: false);
        }
    }

    #endregion
}
