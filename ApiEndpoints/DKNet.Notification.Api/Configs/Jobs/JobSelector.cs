namespace DKNet.Notification.Api.Configs.Jobs;

/// <summary>
///     The job the process was launched to run, selected from its command-line arguments alone (DRK-1255 §6 R1) —
///     no environment check and no configuration value ever participates in this decision.
/// </summary>
/// <param name="RequestedJobName">
///     The first non-option argument, or <c>null</c> when the arguments carried none (R2).
/// </param>
/// <param name="IsRecognized">
///     Whether <paramref name="RequestedJobName" /> matches one of <paramref name="KnownJobNames" />
///     (case-insensitive). Meaningless when <see cref="HasJobName" /> is <c>false</c>.
/// </param>
/// <param name="KnownJobNames">Every job name the registry recognises, for the R3 failure message.</param>
internal sealed record JobSelection(string? RequestedJobName, bool IsRecognized, IReadOnlyList<string> KnownJobNames)
{
    /// <summary>True when a non-option argument was present at all — recognized or not (R3).</summary>
    public bool HasJobName => RequestedJobName is not null;
}

/// <summary>
///     Selects the job name from process arguments per R2: the first argument that is not an option (an option
///     begins with '-') and is not the value of the option before it. A "--key value" pair's value belongs to
///     that option and is never itself a job-name candidate; a "--key=value" option carries its own value, so
///     the argument after it is still a job-name candidate.
/// </summary>
internal static class JobSelector
{
    public static JobSelection Select(IReadOnlyList<string> args, IReadOnlyCollection<string> knownJobNames)
    {
        string? requestedJobName = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith('-'))
            {
                // "--key=value" already carries its value; only the separated "--key value" form takes the
                // next argument, and that value is never itself a job-name candidate.
                if (!arg.Contains('=', StringComparison.Ordinal))
                {
                    i++;
                }

                continue;
            }

            requestedJobName = arg;
            break;
        }

        var isRecognized = requestedJobName is not null &&
            knownJobNames.Any(name => string.Equals(name, requestedJobName, StringComparison.OrdinalIgnoreCase));

        return new JobSelection(requestedJobName, isRecognized, knownJobNames.ToArray());
    }
}
