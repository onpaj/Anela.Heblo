Plan saved to `docs/superpowers/plans/2026-06-08-gridlayouts-slim-persistence-payload.md`.

**Summary of the plan:**

8 tasks, all TDD-style with exact code and commands:

1. **Task 1** — Create `GridLayoutPersistencePayload.cs` (internal sealed record with `[JsonPropertyName("columns")]`).
2. **Task 2** — Tighten the existing Save test (RED): assert JSON does *not* contain `gridKey` or `lastModified`.
3. **Task 3** — Switch `SaveGridLayoutHandler` to serialize the slim record (GREEN).
4. **Task 4** — Add a round-trip pin test using a local `RoundTripShape` (tests don't reference the internal type).
5. **Task 5** — Add the legacy-format read test (FR-3).
6. **Task 6** — Switch `GetGridLayoutHandler` to deserialize the slim payload; assemble `GridLayoutDto` from `(payload.Columns ?? new(), entity.GridKey, entity.LastModified)`.
7. **Task 7** — Add the empty-object (`{}`) read test (covers arch-review Amendment 3 — the null-guard).
8. **Task 8** — Full build + suite-wide run + grep checks confirming no stragglers serialize the full DTO and `GridLayoutDto` doesn't leak into persistence.

**Architect amendments incorporated:**
- Amendment 1 (strip `LastModified` from JSON too) — folded in: same dead-data pattern, zero marginal cost. The slim record carries only `Columns`.
- Amendment 2 (negative assertions in Save test) — Task 2.
- Amendment 3 (empty-`{}` behavior) — Task 7.

Per the pipeline note in the prompt, skipping the execution-handoff prompt — the plan file is the artifact.