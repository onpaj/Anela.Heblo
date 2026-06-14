The plan is saved. Per the pipeline note, I'm skipping the execution handoff prompt.

**Plan saved:** `docs/superpowers/plans/2026-06-14-eliminate-azure-blob-409-conflicts.md`

**Plan summary (7 tasks):**

1. **FR-1 diagnostic** (hard gate) — run KQL against staging + production App Insights, fill in `docs/features/azure-blob-409-diagnostic.md` with results, deployment timestamp, and a Conclusion paragraph that decides whether Task 6 fires.
2. **Telemetry processor chain ordering test** — new xUnit test in `backend/test/Anela.Heblo.Tests/Telemetry/TelemetryProcessorChainOrderingTests.cs` that resolves the DI-configured chain and asserts `BlobIdempotent409TelemetryProcessor` runs before `CostOptimizedTelemetryProcessor`, with a sanity-check swap to prove the test fails when broken.
3. **Alert as code** — extend `scripts/create-azure-infrastructure.sh` with idempotent provisioning of `ag-heblo-prod-default` action group + `alert-heblo-blob-409-failures` scheduled-query alert (>10 failed Azure Blob deps / 1h, severity 3).
4. **Docs** — flip `observability.md` "Alerting" status row, append a new alert line, and mirror the alert into `infrastructure.md` next to the existing Npgsql alerts.
5. **Spec status** — downgrade `Status: COMPLETE` → `Status: VERIFICATION REQUIRED` (only if the spec artifact is editable in-repo).
6. **Conditional `IIdempotentBlobUploader`** — full TDD task (5 unit tests, helper implementation, DI registration, narrow URI-shape matcher extension to the telemetry processor, architecture test banning direct `BlobClient.UploadAsync(..., overwrite: false)` outside the helper) — gated on Task 1's Conclusion explicitly identifying a blob-level 409 source.
7. **Validation** — full build/test/format gates plus a spec-coverage cross-check table mapping each arch-review amendment to a completed task.