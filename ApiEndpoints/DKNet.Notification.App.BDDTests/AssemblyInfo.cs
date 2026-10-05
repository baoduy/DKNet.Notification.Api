// Features (NUnit fixtures) run in parallel; the scenarios of one feature run one at a time. Each feature gets its
// own Redis and Mailpit containers (FeatureKey), so a scenario's flush or clear stays inside its feature.
// Four features hold nearly all the run time, so four workers are enough.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
