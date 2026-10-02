using DKNet.Notification.App.Tests.Scaffold;

namespace DKNet.Notification.App.Tests.Unit.Notifications;

/// <summary>
/// DRK-2020 §5 scenario "No released settings file holds an SMTP password" (@unit), with its presence sibling:
/// the base settings file holds the email section and keeps email off (§3 "The base settings keep email off").
/// </summary>
public sealed class ReleasedSettingsFilesTests
{
    private static IReadOnlyList<string> SettingsFiles() =>
        ScaffoldRepo.Files(".json")
            .Where(path => Path.GetFileName(path).StartsWith("appsettings", StringComparison.Ordinal))
            .ToArray();

    private static IEnumerable<string> PropertyNames(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .SelectMany(property => PropertyNames(property.Value).Prepend(property.Name)),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
            _ => []
        };

    private static JsonDocument Read(string relativePath) =>
        JsonDocument.Parse(
            File.ReadAllText(Path.Combine(ScaffoldRepo.Root, relativePath)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

    [Fact(DisplayName = "No released settings file holds an SMTP password")]
    public void No_released_settings_file_holds_an_SMTP_password()
    {
        var files = SettingsFiles();
        files.ShouldContain("ApiEndpoints/DKNet.Notification.Api/appsettings.json");
        // The search runs over a release that holds the SMTP settings section, where a password would sit.
        using (var released = Read("ApiEndpoints/DKNet.Notification.Api/appsettings.json"))
        {
            released.RootElement.GetProperty("Notifications").GetProperty("Email").ValueKind.ShouldBe(JsonValueKind.Object);
        }

        foreach (var file in files)
        {
            using var json = Read(file);
            PropertyNames(json.RootElement)
                .ShouldNotContain(name => string.Equals(name, "Password", StringComparison.OrdinalIgnoreCase), file);
        }
    }

    [Fact(DisplayName = "The base settings file keeps email off")]
    public void The_base_settings_file_keeps_email_off()
    {
        using var json = Read("ApiEndpoints/DKNet.Notification.Api/appsettings.json");

        var email = json.RootElement.GetProperty("Notifications").GetProperty("Email");
        email.GetProperty("Enabled").ValueKind.ShouldBe(JsonValueKind.False);
    }
}
