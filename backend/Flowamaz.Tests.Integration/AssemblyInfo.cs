// Integration tests spin up real Docker containers (Postgres/Redis) and boot the API host.
// Running collections in parallel contends for those resources and makes tests flaky, so the
// whole assembly runs sequentially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
