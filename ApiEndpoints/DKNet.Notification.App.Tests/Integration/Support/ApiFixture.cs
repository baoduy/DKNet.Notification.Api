using DKNet.Notification.App.TestSupport;

namespace DKNet.Notification.App.Tests.Integration.Support;

public sealed class ApiFixture : TestApiFactoryBase, IAsyncLifetime
{
    #region Methods

    public Task InitializeAsync()
    {
        _ = CreateClient();
        return Task.CompletedTask;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    #endregion
}
