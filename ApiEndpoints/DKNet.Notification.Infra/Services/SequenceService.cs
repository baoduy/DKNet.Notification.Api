namespace DKNet.Notification.Infra.Services;

internal abstract class SequenceService(DbContext dbContext, Sequences sequence) : ISequenceServices
{
    #region Methods

    public virtual async ValueTask<string> NextValueAsync() =>
        dbContext.IsNpgsql()
            ? await dbContext.NextSeqValueWithFormat(sequence)
            : Guid.NewGuid().ToString();

    #endregion
}