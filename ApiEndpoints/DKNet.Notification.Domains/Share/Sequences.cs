using DKNet.EfCore.Abstractions.Attributes;

namespace DKNet.Notification.Domains.Share;

[SqlSequence]
public enum Sequences
{
    None = 0,

    [Sequence(typeof(int), FormatString = "T{DateTime:yyMMdd}{1:00000}", Max = 99999)]
    Membership = 1
}