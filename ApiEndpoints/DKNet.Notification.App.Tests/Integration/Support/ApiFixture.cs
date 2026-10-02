using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

public sealed class ApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    #region Methods

    public async Task InitializeAsync()
    {
        _ = CreateClient();
        await ResetDatabaseAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    #endregion
}
