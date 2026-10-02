using DKNet.EfCore.Abstractions.Entities;
using DKNet.EfCore.DataAuthorization;

namespace DKNet.Notification.Infra.Contexts;

internal class CoreDbContext(DbContextOptions options, IEnumerable<IDataOwnerProvider>? dataKeyProviders = null)
    : DbContext(options), IDataOwnerDbContext
{
    #region Properties

    public IEnumerable<string> AccessibleKeys =>
        _dataKeyProvider is not null ? _dataKeyProvider.GetAccessibleKeys() : [];

    #endregion

    private readonly IDataOwnerProvider? _dataKeyProvider = dataKeyProviders?.FirstOrDefault();

    #region Methods

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureOwnershipResolvable();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureOwnershipResolvable();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnsureOwnershipResolvable();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Fails closed, before EF Core attempts the insert, when an authenticated caller's ownership key cannot be
    /// resolved and a new row would be left with no <see cref="IAuditedProperties.CreatedBy"/> — otherwise EF
    /// Core's own required-property check throws a raw <see cref="DbUpdateException"/> that leaks column/entity
    /// names into the response (DRK-899 R3: null key means "no stamp", never a crash).
    /// </summary>
    private void EnsureOwnershipResolvable()
    {
        if (_dataKeyProvider is null) return;
        if (!string.IsNullOrEmpty(_dataKeyProvider.GetOwnershipKey())) return;

        var hasUnattributableInsert = ChangeTracker.Entries()
            .Any(e => e.State == EntityState.Added
                      && e.Entity is IAuditedProperties { CreatedBy: null or "" }
                      && e.Metadata.FindProperty(nameof(IAuditedProperties.CreatedBy)) is { IsNullable: false });

        if (hasUnattributableInsert) throw new OwnershipRequiredException();
    }

    #endregion
}
