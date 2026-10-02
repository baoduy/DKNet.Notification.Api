using DKNet.Notification.Infra.Contexts;

namespace DKNet.Notification.Infra.Services;

internal sealed class MembershipService(CoreDbContext dbContext)
    : SequenceService(dbContext, Sequences.Membership), IMembershipService;