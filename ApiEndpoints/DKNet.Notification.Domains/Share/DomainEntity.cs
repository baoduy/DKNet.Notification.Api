using DKNet.EfCore.Abstractions.Entities;

namespace DKNet.Notification.Domains.Share;

public abstract class DomainEntity : AuditedEntity<Guid>
{
    #region Constructors

    /// <inheritdoc />
    protected DomainEntity(Guid id, string createdBy, DateTimeOffset? createdOn = null) : base(id)
    {
        SetCreatedBy(createdBy, createdOn);
    }

    /// <inheritdoc />
    protected DomainEntity()
    {
    }

    #endregion
}