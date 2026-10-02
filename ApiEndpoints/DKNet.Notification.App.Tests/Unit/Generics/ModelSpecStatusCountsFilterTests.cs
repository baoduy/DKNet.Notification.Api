using DKNet.Notification.AppServices.Share.Generics;
using DKNet.Notification.Domains.Share;

namespace DKNet.Notification.App.Tests.Unit.Generics;

/// <summary>
/// A dknet.notification <see cref="DomainEntity"/> double that lets a test pin <see cref="CreatedOn"/> directly, since
/// the real feature entities only ever set it once at construction time (via <c>SetCreatedBy</c>).
/// </summary>
file sealed class ProbeEntity(DateTimeOffset createdOn) : DomainEntity(Guid.NewGuid(), "probe", createdOn);

/// <summary>
/// DRK-506 §5 "Status counts over the full history": the supplied From/To bounds must filter the same way
/// no matter which UTC offset the caller expressed them in, because <see cref="DateTimeOffset"/> comparisons
/// compare absolute instants, not local clock faces.
/// </summary>
public class ModelSpecStatusCountsFilterTests
{
    #region Methods

    [Fact]
    public void FilterQuery_ShouldAgree_ForUtcAndAsiaHoChiMinhRepresentationsOfTheSameInstant()
    {
        var utcBound = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hoChiMinhBound = utcBound.ToOffset(TimeSpan.FromHours(7));

        var utcSpec = new ModelSpecStatusCounts<ProbeEntity>(new GenericStatusCountsParameters { From = utcBound });
        var hoChiMinhSpec =
            new ModelSpecStatusCounts<ProbeEntity>(new GenericStatusCountsParameters { From = hoChiMinhBound });

        var beforeBound = new ProbeEntity(utcBound.AddSeconds(-1));
        var atBound = new ProbeEntity(utcBound);
        var afterBound = new ProbeEntity(utcBound.AddSeconds(1));

        var utcFilter = utcSpec.FilterQuery!.Compile();
        var hoChiMinhFilter = hoChiMinhSpec.FilterQuery!.Compile();

        foreach (var probe in new[] { beforeBound, atBound, afterBound })
        {
            hoChiMinhFilter(probe).ShouldBe(utcFilter(probe),
                $"UTC bound and its Asia/Ho_Chi_Minh-offset equivalent must agree for CreatedOn={probe.CreatedOn:O}");
        }

        utcFilter(beforeBound).ShouldBeFalse("a record created before the From instant must be excluded.");
        utcFilter(atBound).ShouldBeTrue("a record created exactly at the From instant must be included.");
        utcFilter(afterBound).ShouldBeTrue("a record created after the From instant must be included.");
    }

    #endregion
}
