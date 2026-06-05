Plan saved to `docs/superpowers/plans/2026-06-04-orgchart-consolidate-error-logging.md`.

**Self-review pass:**

- **Spec coverage:** The Spec → Plan map at the bottom traces every FR (1–4), every NFR (1–4), the FR-1 amendment (line-49 `LogError`), the FR-4 amendment (null-deserialization regression test), and both highlighted risks from the architecture review to a concrete task/step.
- **Placeholders:** None — all four test methods, both edit deltas (before/after), and every command (`dotnet test --filter`, `dotnet format`, `dotnet build`, the two `grep` sanity checks) are fully written out.
- **Type/name consistency:** `OrgChartService(HttpClient, IOptions<OrgChartOptions>, ILogger<OrgChartService>)` matches the production constructor; `OrgChartOptions.DataSourceUrl` matches the actual property; the `StubHttpMessageHandler.Returns(...)` / `ThrowsOnSend(...)` / `CreateService(...)` / `VerifyNoErrorLog()` helpers are referenced identically across all four tests.

**Summary of the plan:**

- **Task 1 (RED):** Add `OrgChartServiceTests.cs` with four failing tests — one per failure path (`HttpRequestException`, `JsonException`, generic `Exception`, null-deserialization) — each asserting both the expected throw and `LogLevel.Error` `Times.Never`.
- **Task 2 (GREEN):** Surgically delete the four `LogError` calls at lines 49/62/67/72 of `OrgChartService.GetOrganizationStructureAsync`, preserving every `throw` and every `LogInformation`. Verify tests now pass, then format + build.
- **Task 3:** Refresh the inline comment on the existing handler test so the single-owner rule is described positively rather than as a contradiction.
- **Task 4:** Full backend build + test, plus two `grep` sanity checks (no `LogError` remains in the service; controller still has the single `LogError`).

An operator-facing note for the PR description (covering NFR-3's ~50% drop in OrgChart error counts) is included as a PR-time instruction, not a code change.