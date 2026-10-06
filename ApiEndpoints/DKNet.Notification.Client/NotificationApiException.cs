using System.Net;
using System.Text.Json;

namespace DKNet.Notification.Client;

/// <summary>One entry of a refusal's error list, exactly as the service sent it.</summary>
public sealed record NotificationApiError
{
    /// <summary>The stable error code, such as <c>TEMPLATE_NOT_FOUND</c>.</summary>
    public string? Code { get; init; }

    /// <summary>The part of the request at fault; empty when no part is.</summary>
    public string? Field { get; init; }

    /// <summary>The service's message for the error.</summary>
    public required string Message { get; init; }
}

/// <summary>Thrown for every answer of the service that is not a success.</summary>
public sealed class NotificationApiException : Exception
{
    /// <summary>Creates the exception for one answer of the service.</summary>
    public NotificationApiException(HttpStatusCode statusCode, IReadOnlyList<NotificationApiError> errors, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
    }

    /// <summary>The status the service answered with.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The <c>errors[]</c> entries of the answer; empty when its body has none.</summary>
    public IReadOnlyList<NotificationApiError> Errors { get; }

    /// <summary>
    /// The refusal for a non-success answer: its status and the <c>errors[]</c> entries of a problem body. A body that
    /// is empty, not JSON or without an <c>errors</c> array gives an empty list; this never throws for a body.
    /// </summary>
    internal static async Task<NotificationApiException> FromResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var errors = ReadErrors(body);
        var codes = string.Join(", ", errors.Select(e => e.Code).OfType<string>());
        var message = $"The notification service answered {(int)response.StatusCode} ({response.StatusCode})"
                      + (codes.Length > 0 ? $": {codes}." : ".");
        return new NotificationApiException(response.StatusCode, errors, message);
    }

    private static IReadOnlyList<NotificationApiError> ReadErrors(string body)
    {
        // An empty or blank body is not JSON either.
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return errors.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => new NotificationApiError
                {
                    Code = Text(e, "code"),
                    Field = Text(e, "field"),
                    Message = Text(e, "message") ?? string.Empty
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
