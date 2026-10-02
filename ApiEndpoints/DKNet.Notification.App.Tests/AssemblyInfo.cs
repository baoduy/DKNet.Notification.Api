// PerHostFeatureStateTests (Integration/HostFeatureState) mutates the process-wide
// FeatureManagement__RequireAuthorization environment variable while building a WebApplicationFactory host —
// the only input Program.cs's eager FeatureOptions bind reads early enough (see that test's remarks). xUnit
// runs each test class as its own collection and collections run concurrently by default, so any other
// integration test building a host (ApiFixture-based classes included) inside that window would race and
// intermittently inherit the wrong RequireAuthorization value. Disabling assembly-wide parallelization
// removes the race without pinning every host-building class into a hand-maintained shared collection.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
