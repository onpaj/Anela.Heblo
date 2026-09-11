### task: update-consumeinventoryresult-test-call-sites

This task updates the remaining three positional-constructor calls in the test project, then verifies the whole solution builds and the affected tests pass.

1. Open `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs`. Confirm it currently fails to compile against the class from task 1 by building the test project:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected output: build fails with three `CS1729` errors pointing at `AddItemToBoxHandlerTests.cs` lines 175, 222, and 260.

2. In `AddItemToBoxHandlerTests.cs`, make these three substitutions (each is inside a `.Setup(...).ReturnsAsync(...)` chain — nothing else on the surrounding lines changes):

   Line 175 — change:
   ```csharp
               .ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.Success));
   ```
   to:
   ```csharp
               .ReturnsAsync(ConsumeInventoryResult.Success());
   ```

   Line 222 — change:
   ```csharp
               .ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock));
   ```
   to:
   ```csharp
               .ReturnsAsync(ConsumeInventoryResult.InsufficientStock());
   ```

   Line 260 — change:
   ```csharp
               .ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound));
   ```
   to:
   ```csharp
               .ReturnsAsync(ConsumeInventoryResult.InventoryNotFound());
   ```

   Do not change any `Setup(...)` matcher arguments, any `Assert`/`Should()` lines, or any other test in the file — only these three construction expressions.

3. Build the test project again and confirm it now succeeds:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

4. Run the `AddItemToBoxHandlerTests` test class specifically and confirm all its tests still pass, unmodified in behavior:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AddItemToBoxHandlerTests"
   ```

   Expected output: `Passed!` summary line reporting 7 passed, 0 failed (the fixture's `Handle_BoxNotFound_ReturnsFailure`, `Handle_WithoutSourceInventoryId_AddsItemWithoutInventoryInteraction`, `Handle_AddingSameProductAndLotTwice_MergesIntoSingleRow`, `Handle_WithSourceInventoryId_ConsumesInventoryAndSetsLotOnItem`, `Handle_WithSourceInventoryId_InsufficientStock_ReturnsError`, `Handle_WithSourceInventoryId_InventoryNotFound_ReturnsError`, `Handle_BoxNotInOpenedState_ReturnsTransportBoxInvalidStateTransition`).

5. Run the `ManufactureInventoryReservationAdapterTests` test class to confirm the adapter's own tests — which only read `.Outcome` and were never edited — still pass unchanged:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureInventoryReservationAdapterTests"
   ```

   Expected output: `Passed!` summary line, 0 failed.

6. Confirm no positional-constructor call remains anywhere in the repository:

   ```bash
   grep -rn "new ConsumeInventoryResult(" backend/
   ```

   Expected output: no matches (empty output).

7. Build the full solution to confirm nothing else regressed:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet build Anela.Heblo.sln
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

8. Run the full backend test suite as the final regression gate:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet test Anela.Heblo.sln
   ```

   Expected output: `Passed!` summary line, 0 failed (the count will match whatever the suite reported before this change — this is a behavior-preserving refactor, so no new failures and no newly-skipped tests should appear).

9. Run `dotnet format` scoped to the touched test file:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --include backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs
   ```

   Expected output: exits with no errors (whitespace-only changes at most — re-open the file afterward and confirm the three substitutions from step 2 are still intact).

10. Stage and commit the test file:

    ```bash
    cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
    git add backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs
    git commit -m "Update AddItemToBoxHandlerTests to use ConsumeInventoryResult factory methods"
    ```

    Expected output: a new commit containing exactly this one file.

---

## Self-review

**Spec coverage:**
- FR-1 (convert to sealed class with `Outcome` init property + three static factories, doc comment updated) — `convert-consumeinventoryresult-to-class` step 1.
- FR-2 (update the three production call sites in `ManufactureInventoryReservationAdapter.cs`) — `convert-consumeinventoryresult-to-class` step 3.
- FR-3 (update the three test call sites in `AddItemToBoxHandlerTests.cs`) — `update-consumeinventoryresult-test-call-sites` step 2.
- FR-4 (no edits to `AddItemToBoxHandler.cs`, `ManufactureInventoryReservationAdapterTests.cs`, `IInventoryReservationService.cs`) — explicitly not touched by either task; step 5 of task 2 runs `ManufactureInventoryReservationAdapterTests` to prove it still passes untouched, and the full-repo `grep`/build/test steps (task 2 steps 6–8) confirm nothing else needed changes.
- NFR-1 (behavior preservation) — verified by the build-then-test sequence in both tasks and the final full-solution `dotnet test` in task 2 step 8; no equality/deconstruction usage exists to regress (confirmed via the call-site inventory above).
- NFR-2 (security, N/A) — no action needed, no task references it.
- "No public constructor... acceptable per FR-2" note in FR-1 — satisfied as written: the class in step 1 has no explicit constructor, matching the spec's accepted design.

**Placeholder scan:** No "TBD"/"add validation"/"handle edge cases"/"similar to Task N" phrasing anywhere above; every code-bearing step shows the exact before/after text, every command step shows the exact command and expected output.

**Type consistency:** `ConsumeInventoryResult.Success()`, `.InventoryNotFound()`, `.InsufficientStock()` and the `Outcome` property name are identical across the class definition (task 1 step 1), the production call sites (task 1 step 3), and the test call sites (task 2 step 2) — no drift between tasks.
