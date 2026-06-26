# Database Connection Health — App Insights / KQL Snippets

Use these queries in the Azure Portal Application Insights Logs blade to verify pool health and audit the FR-3 telemetry surface.

## 1. Npgsql-originated exceptions in the last hour

```kusto
exceptions
| where timestamp > ago(1h)
| where type startswith "Npgsql."
    or (type == "System.Net.Sockets.SocketException" and outerAssembly startswith "Npgsql")
    or (type == "System.TimeoutException" and outerMethod has "NpgsqlConnector")
| summarize count() by type, bin(timestamp, 5m)
| render timechart
```

## 2. Pool exhaustion waits (custom metric)

```kusto
customMetrics
| where name == "npgsql.pool.exhaustion_wait_seconds"
| summarize p50 = percentile(value, 50), p95 = percentile(value, 95), n = count() by bin(timestamp, 5m)
| render timechart
```

## 3. Retry events from structured logs

```kusto
traces
| where message startswith "DbTransientRetry"
| extend attempt = toint(customDimensions["Attempt"]), exType = tostring(customDimensions["ExceptionType"])
| summarize count() by exType, bin(timestamp, 15m)
| render timechart
```

## 4. Alert query — Npgsql exception spike

Used by alert rule `alert-heblo-db-npgsql-spike`:

```kusto
exceptions
| where timestamp > ago(1h)
| where type startswith "Npgsql."
    or (type == "System.Net.Sockets.SocketException" and outerAssembly startswith "Npgsql")
| count
```

Threshold: `Count > 10` for 2 consecutive evaluations (hourly).

## 5. Alert query — pool exhaustion

Used by alert rule `alert-heblo-db-pool-exhaustion`:

```kusto
customMetrics
| where timestamp > ago(5m)
| where name == "npgsql.pool.exhaustion_wait_seconds"
| count
```

Threshold: `Count > 5` for one evaluation (5-minute window).
