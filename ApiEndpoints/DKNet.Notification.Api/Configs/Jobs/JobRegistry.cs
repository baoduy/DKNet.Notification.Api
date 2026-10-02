namespace DKNet.Notification.Api.Configs.Jobs;

/// <summary>
///     Every job the process can be launched to run instead of serving. Adding a job is one entry here (R7) —
///     no new branch in the start-up path, no new project, no second container image. Names are matched
///     case-insensitively by <see cref="JobSelector" />.
/// </summary>
internal static class JobRegistry
{
    public static IReadOnlyDictionary<string, Func<WebApplicationBuilder, Task<int>>> Jobs { get; } =
        new Dictionary<string, Func<WebApplicationBuilder, Task<int>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["migration"] = MigrationJob.RunAsync
        };
}
