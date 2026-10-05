namespace DKNet.Notification.App.BDDTests.Support;

/// <summary>
/// The feature the calling scenario belongs to: its NUnit fixture. Features run in parallel (AssemblyInfo.cs), so
/// <see cref="RedisServer" /> and <see cref="MailCatcher" /> keep one container per feature under this key, and a
/// scenario that flushes or clears one never touches another feature's.
/// </summary>
internal static class FeatureKey
{
    /// <summary>The fixture class name, or <c>run</c> outside a test, as in the run-level hooks.</summary>
    public static string Current => TestContext.CurrentContext.Test.ClassName ?? "run";
}
