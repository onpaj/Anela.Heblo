Plan saved to `docs/superpowers/plans/2026-06-16-smartsupp-webhook-500-oversized-fields.md`.

**Summary of the plan**

12 tasks across 5 phases, each broken into 2-5 minute TDD steps with copy-paste-ready code blocks and explicit `Run:` / `Expected:` checks:

- **Phase A — Persistence (Tasks 1-2):** Widen `SmartsuppConversation.Subject` and `ContactAvatarUrl` to `varchar(2000)` and `Referer` to `text`; auto-generate the EF migration. Configuration tests assert column widths via the EF model.
- **Phase B — Truncation infra (Tasks 3-6):** Add `RecordTruncation(string field)` to `ISmartsuppWebhookMetrics` and the meter; create an internal `StringTruncation` helper (UTF-16-surrogate-pair-safe, never logs the value); make `SmartsuppPayloadMapper.MapConversation` accept `ILogger` + `ISmartsuppWebhookMetrics` and truncate every bounded field; thread both deps through all 9 conversation reactions.
- **Phase C — Audit isolation (Tasks 7-8):** Switch `SmartsuppWebhookAuditWriter` to `IDbContextFactory<ApplicationDbContext>` so a polluted change tracker from a failed domain save can never block the audit write; register the factory in `PersistenceModule`; defensively truncate the bounded `varchar` audit columns; wrap controller `_audit.UpdateOutcomeAsync` calls in their own try/catch as belt-and-braces.
- **Phase D — Regression coverage (Tasks 9-10):** Parameterised handler test for oversized `subject`/`contact_avatar_url`/`contact_name`; HTTP-level controller tests for oversized `subject` and `referer` returning 200 with `RawBody` preserved.
- **Phase E — Validation (Tasks 11-12):** Verify replay path still round-trips through the new schema; full build, `dotnet format`, full Smartsupp + backend test gates; operator runbook for the manual migration + replay added to the PR description.

The plan implements the arch-review's amendment to FR-3 (context isolation via `IDbContextFactory`, not just try/catch wrapping) and explicitly excludes truncating `Referer` (its column is now unbounded `text`). Every task has a failing-test step before the implementation step, and frequent commits are scoped per-task.