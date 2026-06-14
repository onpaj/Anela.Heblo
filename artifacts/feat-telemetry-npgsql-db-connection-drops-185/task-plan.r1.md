Plan saved to `docs/superpowers/plans/2026-06-13-npgsql-db-connection-resilience.md`.

**Summary:** 18 tasks covering the full FR-1 → FR-5 scope plus arch-review amendments (analytics pool cap, sibling interceptor, AI meter registration, managed-tx CI guard).

- Tasks 0, 16, 17: runbook tasks (audit, Key Vault rotation, Az CLI alert creation).
- Tasks 1–11: code + TDD path (Polly package → `TransientErrorClassifier` → options → metrics → provider → strategy → connection interceptor → wiring → analytics pool cap → appsettings → production-config regression test).
- Tasks 12–15: telemetry surface (AI meter registration, KQL doc, alert documentation, managed-tx CI guard).
- Task 18: final validation gate.

Per pipeline note: skipping the execution-handoff prompt; the plan file content is the artifact.