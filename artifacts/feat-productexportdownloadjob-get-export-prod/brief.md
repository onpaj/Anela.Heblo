# Feature Brief: ProductExportDownloadJob: GET /export/products.csv failing with Faulted status

## Telemetry

**Date**: 2026-04-29 (last 24 h)

| Finding | 24h count |
|---------|-----------|
| `GET /export/products.csv` dependency → `Faulted` | 3 |

## Context

`ProductExportDownloadJob` (runs daily at 02:00) calls `DownloadFromUrlHandler`, which uses `IBlobStorageService.DownloadFromUrlAsync()` to download a product export CSV from a configured external URL and store it in Azure Blob Storage.

The App Insights dependency failure shows `resultCode = Faulted`, which in .NET's `HttpClient` telemetry indicates the HTTP call raised an exception (socket-level failure, DNS resolution failure, TLS error, or hard timeout) rather than completing with a non-2xx HTTP status code.

3 failures in 24 hours for a job that runs once/day is unusual — either the scheduled job is being retried, or the URL is also hit by other code paths.

## Probable root causes

1. **External product export URL is unreachable** — the host serving `/export/products.csv` is intermittently down, returns a connection refused, or has DNS issues.
2. **SSL/TLS certificate error** — the certificate on the external export host has expired or is untrusted.
3. **Timeout** — the CSV file is large and the download times out before completing (no explicit timeout is set in `DownloadFromUrlHandler`, so it uses the default `HttpClient` timeout of 100 s).

## Suggested fix

1. **Check the configured export URL** (`ProductExportOptions.Url`) — verify the external host is accessible and the certificate is valid.
2. **Add an explicit timeout** in `DownloadFromUrlHandler` or in the named `HttpClient` registration to fail fast rather than wait 100 s.
3. **Add retry logic** (e.g. Polly) in `ProductExportDownloadJob` or `DownloadFromUrlHandler` for transient network failures.
4. **Investigate why 3 failures** appeared in 24 h for a once-daily job — check if there is a retry mechanism or a duplicate job trigger.

---
*Converted from GitHub issue #842: https://github.com/onpaj/Anela.Heblo/issues/842*
