namespace DKNet.Notification.App.Tests;

/// <summary>
///     Test classes run in parallel, except the classes in this collection, which run alone after the rest. Each one
///     listens to something process-wide (an Azure SDK event source or diagnostic listener, or every delivery
///     activity) or asserts a tight time bound, so another class running at the same time would change what it sees.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialTestsCollection
{
    public const string Name = "Process-wide listeners and timing";
}
