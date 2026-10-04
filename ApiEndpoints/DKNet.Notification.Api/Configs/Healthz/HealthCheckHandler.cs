namespace DKNet.Notification.Api.Configs.Healthz;

/// <summary>Liveness only, and silent: a monitor probes it often, so it writes no log entry.</summary>
[ExcludeFromCodeCoverage]
internal sealed class HealthCheckHandler : IHealthCheck
{
    #region Methods

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy());

    #endregion
}