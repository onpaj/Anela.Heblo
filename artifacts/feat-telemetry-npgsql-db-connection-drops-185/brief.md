telemetry-signal: exception:Npgsql.PostgresException@NpgsqlConnector.ReadMessageLong

## Signal

In the last 7 days (P7D, ending 2026-06-12T15:12Z), the `exceptions` table shows a cluster of database-layer faults:

| Type | problemId snippet | Count |
|---|---|---|
| Npgsql.PostgresException | NpgsqlConnector+<ReadMessageLong>d__233.MoveNext | 120 |
| System.Net.Sockets.SocketException | Npgsql.TaskTimeoutAndCancellation+<ExecuteAsync> | 30 |
| System.Net.Sockets.SocketException | Polly.Outcome`1.GetResultOrRethrow | 23 |
| System.Net.Sockets.SocketException | NpgsqlConnector.Connect | 7 |
| System.TimeoutException | Npgsql.TaskTimeoutAndCancellation | 2 |
| System.TaskCanceledException / OperationCanceledException | NpgsqlCommand / NpgsqlConnector | 2 |
| System.TimeoutException | NpgsqlConnector.Connect | 1 |

**Total: ~185 Npgsql/DB-layer exceptions over 7 days (~26/day).**

The rate has dropped in the last 2 days (~6 PostgresException + 2 SocketException = 8 in P2D), but the first 5 days averaged ~35/day — elevated enough to indicate real instability.

## Correlation

The exception spike predates the June 11 merge wave. No merged PR in this window explicitly addresses connection pooling, Azure PostgreSQL configuration, or Npgsql retry logic. The pattern — `ReadMessageLong` (mid-read disconnect), `Connect` failures, Polly retries — is consistent with transient Azure PostgreSQL connection drops, possibly pool exhaustion under load.

The `System.InvalidOperationException at ArticleRepository.GetFeedbackStatsAsync` (3 occurrences) and `GetFeedbackPagedAsync` (1 occurrence) EF Core concurrency exceptions overlap in time but were separately addressed by PR #2915 (DbContext concurrency fix, merged 2026-06-11).

## Next step

Check Azure PostgreSQL connection pool metrics (active connections, connection wait times) for June 5–10. Verify that Npgsql `Max Pool Size` is set appropriately for the expected concurrency. If the rate continues to decline it may be self-resolving; if it recurs, consider adding explicit Polly retry/fallback around EF Core query execution and investigating whether a max-connection limit on the Azure DB SKU is being hit under load.