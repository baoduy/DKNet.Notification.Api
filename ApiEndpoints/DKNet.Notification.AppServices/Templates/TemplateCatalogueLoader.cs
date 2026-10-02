using System.Text.RegularExpressions;
using DKNet.Notification.Domains.Templates;

namespace DKNet.Notification.AppServices.Templates;

/// <summary>
///     Builds the <see cref="ITemplateCatalogue" /> from the settings and the template folder, once, at start-up.
/// </summary>
public static partial class TemplateCatalogueLoader
{
    #region Fields

    private const string EmailChannel = "email";
    private const string TeamsChannel = "teams";
    private const int MaxSubjectLength = 500;
    private const int MaxTextLength = 200;

    #endregion

    #region Methods

    /// <summary>
    ///     Checks every registration and reads each version's file once.
    /// </summary>
    /// <param name="registrations">The <c>Notifications:Templates</c> settings section.</param>
    /// <param name="templateFolder">The release's template folder. Every version's file must sit inside it.</param>
    /// <returns>The catalogue.</returns>
    /// <exception cref="InvalidOperationException">
    ///     A registration breaks a rule. The message is <c>Template '&lt;id&gt;': &lt;rule&gt;</c>.
    /// </exception>
    public static ITemplateCatalogue Load(IEnumerable<TemplateRegistration> registrations, string templateFolder)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateFolder);

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(templateFolder)) +
                     Path.DirectorySeparatorChar;
        var templates = new Dictionary<string, NotificationTemplate>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            var template = LoadTemplate(registration, folder);
            if (!templates.TryAdd(template.TemplateId, template))
            {
                throw Broken(template.TemplateId, "R1", "another template has the same id.");
            }
        }

        return new TemplateCatalogue(templates);
    }

    private static NotificationTemplate LoadTemplate(TemplateRegistration registration, string folder)
    {
        var id = registration.TemplateId ?? string.Empty;
        if (!TemplateIdPattern().IsMatch(id))
        {
            throw Broken(id, "R1", "the id must match ^[a-z0-9-]{1,100}$.");
        }

        if (registration.Description?.Length > MaxTextLength)
        {
            throw Broken(id, "R5", $"the description is longer than {MaxTextLength} characters.");
        }

        if (registration.Versions is not { Count: > 0 })
        {
            throw Broken(id, "R2", "the template has no version.");
        }

        var versions = new List<TemplateVersion>(registration.Versions.Count);
        foreach (var version in registration.Versions)
        {
            var loaded = LoadVersion(id, version, folder);
            if (versions.Exists(v => v.Channel == loaded.Channel))
            {
                throw Broken(id, "R2", $"the template has more than 1 {loaded.Channel} version.");
            }

            versions.Add(loaded);
        }

        return new NotificationTemplate(id, registration.Description ?? string.Empty, versions.ToArray());
    }

    private static TemplateVersion LoadVersion(string id, TemplateVersionRegistration version, string folder)
    {
        var channel = version.Channel;
        var format = (channel, version.Format) switch
        {
            (EmailChannel, TemplateFormat.Html) => TemplateFormat.Html,
            (TeamsChannel, TemplateFormat.Markdown) => TemplateFormat.Markdown,
            (EmailChannel or TeamsChannel, _) => throw Broken(id, "R3",
                $"the {channel} version must be in {(channel == EmailChannel ? "Html" : "Markdown")}."),
            _ => throw Broken(id, "R2", $"the channel must be {EmailChannel} or {TeamsChannel}.")
        };

        if (channel == EmailChannel && version.Subject is not { Length: > 0 and <= MaxSubjectLength })
        {
            throw Broken(id, "R3", $"an email version needs a subject of 1 to {MaxSubjectLength} characters.");
        }

        if (version.Title?.Length > MaxTextLength)
        {
            throw Broken(id, "R5", $"the title is longer than {MaxTextLength} characters.");
        }

        var file = version.File ?? string.Empty;
        var body = ReadBody(id, file, ResolvePath(id, file, folder));
        return new TemplateVersion(channel, file, format, version.Subject, version.Title, body);
    }

    private static string ResolvePath(string id, string file, string folder)
    {
        if (file.Length > MaxTextLength)
        {
            throw Broken(id, "R4", $"the file name is longer than {MaxTextLength} characters.");
        }

        var path = Path.GetFullPath(Path.Combine(folder, file));
        if (Path.IsPathRooted(file) || !path.StartsWith(folder, StringComparison.Ordinal))
        {
            throw Broken(id, "R4", "the file must sit inside the template folder.");
        }

        if (!File.Exists(path))
        {
            throw Broken(id, "R4", $"the file '{file}' does not exist.");
        }

        return path;
    }

    private static string ReadBody(string id, string file, string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Broken(id, "R4", $"the file '{file}' cannot be read.", error);
        }
    }

    private static InvalidOperationException Broken(string id, string rule, string reason, Exception? inner = null) =>
        new($"Template '{id}': {rule} {reason}", inner);

    // \z, not $: $ also matches before a trailing new line.
    [GeneratedRegex(@"^[a-z0-9-]{1,100}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateIdPattern();

    #endregion
}
