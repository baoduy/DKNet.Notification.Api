using System.Text.Json;
using System.Text.Json.Serialization;

namespace DKNet.Notification.Client;

/// <summary>
/// Reads and writes <see cref="NotificationStatus" /> as the service's lower-case literals. Any other value, a number
/// or a differently cased literal included, is refused with a <see cref="JsonException" /> that names it.
/// </summary>
internal sealed class NotificationStatusConverter : JsonConverter<NotificationStatus>
{
    public override NotificationStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"The notification status must be a string, not {reader.TokenType}.");
        }

        var value = reader.GetString();
        return value switch
        {
            "pending" => NotificationStatus.Pending,
            "success" => NotificationStatus.Success,
            "failed" => NotificationStatus.Failed,
            _ => throw new JsonException($"Unknown notification status '{value}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, NotificationStatus value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            NotificationStatus.Pending => "pending",
            NotificationStatus.Success => "success",
            NotificationStatus.Failed => "failed",
            _ => throw new JsonException($"Unknown notification status '{value}'.")
        });
    }
}
