Plan saved to `docs/superpowers/plans/2026-06-11-process-daily-consumption-handler-tests.md`.

**Summary of the plan (5 tasks, ~25 minutes of work):**

1. **Task 1 — Scaffold + idempotency test (FR-1):** Create the test file with a `MakeSut()` helper using `Mock<IConsumptionCalculationService>` + `Mock<ILogger<…>>`, plus the `Handle_ReturnsFailure_WhenAlreadyProcessed` test. Service stub returns `WasRun=false, MaterialsProcessed=42` to prove the handler ignores the count when the date was already processed.
2. **Task 2 — Success with N materials (FR-2):** Append `Handle_ReturnsSuccess_WhenMaterialsUpdated`, asserting the count is propagated and message contains date, N, and "materials updated".
3. **Task 3 — Success with no invoices (FR-3):** Append `Handle_ReturnsSuccessWithZeroCount_WhenNoInvoicesFound`, asserting `Success=true` with `MaterialsProcessed=0` and message contains "No invoices".
4. **Task 4 — Exception path (FR-4 + NFR-2):** Append `Handle_ReturnsGenericError_WhenServiceThrows`, asserting the generic message literal, affirmative absence of the secret/exception-type-name, and a single `LogLevel.Error` `Verify` carrying the same exception instance (via a small `VerifyErrorLogged` helper).
5. **Task 5 — Final validation:** Run `dotnet format`, full `ProcessDailyConsumptionHandlerTests` filter run, `git diff --name-only main...HEAD -- 'backend/src/**'` to enforce FR-6 (no prod changes), and full PackingMaterials regression sweep.

**Key adjustments baked in from the arch review:**
- `DateOnly` everywhere (not `DateTime`).
- Flat folder path `Features/PackingMaterials/` (not nested `UseCases/ProcessDailyConsumption/`).
- Moq for both service and logger (the existing `MockLogger<T>` is a no-op and can't satisfy the log-verification requirement).
- `ProcessDailyConsumptionResult` is a `sealed record` with **positional** constructor — flagged with an explicit warning so the engineer doesn't try object-initializer syntax.

Each task commits independently with conventional `test(packingmaterials): …` messages, giving four behavioral commits + one optional formatting commit.