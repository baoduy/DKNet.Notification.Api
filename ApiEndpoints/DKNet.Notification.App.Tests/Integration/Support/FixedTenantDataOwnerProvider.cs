using DKNet.EfCore.DataAuthorization;

namespace DKNet.Notification.App.Tests.Integration.Support;

/// <summary>
/// Always resolves ownership to one fixed tenant key, regardless of who is signed in — the fake that lets
/// <see cref="AuthOnFixedTenantApiFixture" /> prove the acting-user stamp and the tenant-ownership key are
/// read from two different sources, since <see cref="DKNet.Notification.Api.Configs.Handlers.PrincipalProvider" /> would
/// otherwise resolve both from the same claim.
/// </summary>
public sealed class FixedTenantDataOwnerProvider(string tenantKey) : IDataOwnerProvider
{
    public ICollection<string> GetAccessibleKeys() => [tenantKey];

    public string? GetOwnershipKey() => tenantKey;
}
