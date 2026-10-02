using System.Reflection;
using System.Text.RegularExpressions;

namespace DKNet.Notification.App.Tests.Scaffold;

/// <summary>
/// The DKNet.Notification repo as the DRK-1994 scaffold tests read it: the repo root (the folder holding
/// <c>DKNet.Notification.sln</c>), the projects the solution lists, and the files outside build output.
/// </summary>
internal static partial class ScaffoldRepo
{
    public const string SolutionFileName = "DKNet.Notification.sln";

    private static readonly string[] SkippedDirectoryNames = ["bin", "obj", "TestResults", "node_modules"];

    public static string Root { get; } = FindRoot();

    /// <summary>Every project file the solution lists, as a full path.</summary>
    public static IReadOnlyList<string> SolutionProjects { get; } = ReadSolutionProjects();

    /// <summary>The solution's own service projects: every listed project except the test projects.</summary>
    public static IReadOnlyList<string> ServiceProjectNames { get; } = SolutionProjects
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => !name!.EndsWith("Tests", StringComparison.Ordinal) &&
                       !name.EndsWith("TestSupport", StringComparison.Ordinal))
        .Select(name => name!)
        .ToArray();

    /// <summary>The compiled assemblies of <see cref="ServiceProjectNames" />, loaded by name.</summary>
    public static IReadOnlyList<Assembly> ServiceAssemblies() =>
        ServiceProjectNames.Select(name => Assembly.Load(new AssemblyName(name))).ToArray();

    /// <summary>
    /// Files under the repo root with the given extension, skipping build output and hidden folders
    /// (<c>.git</c>, editor and tool caches). Paths are returned relative to the root, with '/' separators.
    /// </summary>
    public static IReadOnlyList<string> Files(string extension) =>
        Directory.EnumerateFiles(Root, "*" + extension, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Where(relative => !relative.Split('/')[..^1].Any(segment =>
                segment.StartsWith('.') || SkippedDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static string FullPath(string relativePath) => Path.Combine(Root, relativePath);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"No folder above {AppContext.BaseDirectory} holds {SolutionFileName}.");
    }

    private static string[] ReadSolutionProjects() =>
        SolutionProjectLine().Matches(File.ReadAllText(Path.Combine(Root, SolutionFileName)))
            .Select(match => Path.GetFullPath(Path.Combine(Root, match.Groups["path"].Value.Replace('\\', '/'))))
            .ToArray();

    [GeneratedRegex(@"^Project\(""\{[^}]+\}""\) = ""[^""]+"", ""(?<path>[^""]+\.csproj)""", RegexOptions.Multiline)]
    private static partial Regex SolutionProjectLine();
}
