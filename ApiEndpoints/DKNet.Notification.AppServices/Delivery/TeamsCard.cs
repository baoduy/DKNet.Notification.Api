using System.Buffers;
using System.Text.Json;
using DKNet.Notification.Domains.Notifications;

namespace DKNet.Notification.AppServices.Delivery;

/// <summary>
///     The Teams message of a rendered notification: 1 Adaptive Card with an optional title block and one Markdown
///     text block, written by the JSON serializer. The bytes measured are the bytes posted.
/// </summary>
public static class TeamsCard
{
    #region Fields

    /// <summary>The largest Teams message the service posts: 28,672 bytes (28 × 1,024).</summary>
    public const int MaxBytes = 28_672;

    #endregion

    #region Methods

    /// <summary>Writes the Teams message of <paramref name="message" />.</summary>
    /// <param name="message">The filled title and Markdown body.</param>
    /// <returns>The UTF-8 bytes of the message, exactly as they are posted.</returns>
    public static byte[] Serialize(RenderedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        // The default encoder escapes every value, so no value can change the card's structure.
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("type", "message");
            json.WriteStartArray("attachments");
            json.WriteStartObject();
            json.WriteString("contentType", "application/vnd.microsoft.card.adaptive");
            json.WriteStartObject("content");
            json.WriteString("type", "AdaptiveCard");
            json.WriteString("$schema", "http://adaptivecards.io/schemas/adaptive-card.json");
            json.WriteString("version", "1.4");
            json.WriteStartArray("body");
            if (message.Subject.Length > 0)
            {
                json.WriteStartObject();
                json.WriteString("type", "TextBlock");
                json.WriteString("text", message.Subject);
                json.WriteBoolean("wrap", true);
                json.WriteString("weight", "Bolder");
                json.WriteString("size", "Medium");
                json.WriteEndObject();
            }

            json.WriteStartObject();
            json.WriteString("type", "TextBlock");
            json.WriteString("text", message.Body);
            json.WriteBoolean("wrap", true);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    #endregion
}
