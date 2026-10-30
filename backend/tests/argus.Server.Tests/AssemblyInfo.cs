using Xunit;

// Isolation tests share one PostgreSQL database and process-wide environment
// variables, so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
