# Implementation Plan: Convert `ConsumeInventoryResult` from a record to a class

## Goal
`ConsumeInventoryResult` (`backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs:18`) is currently `public sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome);`. Per CLAUDE.md's "DTOs are classes, never C# records" rule, convert it to a `sealed class` exposing an `init`-only `Outcome` property and three static factory methods (`Success()`, `InventoryNotFound()`, `InsufficientStock()`), then update the two files that construct it via the record's positional constructor so the solution keeps compiling and behaving identically.

## Architecture approach
This is a pure, behavior-preserving type-declaration change confined to one file, plus mechanical call-site substitutions in two other files. No interfaces, DTOs consumed by controllers, module boundaries, or NSwag/OpenAPI surface are touched — `ConsumeInventoryResult` is never serialized and never returned from a controller action. The change is split into two independently committable tasks: (1) the class conversion plus the production call site, which together keep the main solution compiling; (2) the test call site updates, which keep the test project compiling and passing.

## Tech stack
- .NET 8, C# (sealed class with `init`-only auto-property + static factory methods)
- xUnit + Moq + FluentAssertions (`backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`)
- Solution root: `/home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses/Anela.Heblo.sln` — run all `dotnet` commands from the repository root.

## Verified call-site inventory (re-checked against source on disk)
- `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs:18` — the record declaration.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs:42,57,61` — three positional constructions.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs:175,222,260` — three positional constructions inside `.ReturnsAsync(...)`.
- A repo-wide `grep -rn "new ConsumeInventoryResult(" backend/` returns exactly these 6 hits — no other call sites exist.
- Unaffected (read `.Outcome` only, or declare the unchanged return type — confirmed, not edited by this plan): `AddItemToBoxHandler.cs`, `ManufactureInventoryReservationAdapterTests.cs`, `IInventoryReservationService.cs`.

---

### task: convert-consumeinventoryresult-to-class

This task converts the type and fixes the one production call site, so `Anela.Heblo.Application` (and everything that depends on it except the test project) compiles cleanly.

1. Open `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs`. Its current full contents are:

   ```csharp
   namespace Anela.Heblo.Application.Features.Logistics.Contracts;

   /// <summary>
   /// Outcome of an <see cref="IInventoryReservationService.TryConsumeAsync"/> call.
   /// </summary>
   public enum ConsumeInventoryOutcome
   {
       Success,
       InventoryNotFound,
       InsufficientStock,
   }

   /// <summary>
   /// Logistics-owned result of attempting to consume inventory.
   /// Sealed record with an outcome discriminator — extensible to carry an optional
   /// available-amount field without breaking the contract.
   /// </summary>
   public sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome);
   ```

   Replace the final doc comment + type declaration (the `ConsumeInventoryOutcome` enum above it is unchanged) so the file becomes:

   ```csharp
   namespace Anela.Heblo.Application.Features.Logistics.Contracts;

   /// <summary>
   /// Outcome of an <see cref="IInventoryReservationService.TryConsumeAsync"/> call.
   /// </summary>
   public enum ConsumeInventoryOutcome
   {
       Success,
       InventoryNotFound,
       InsufficientStock,
   }

   /// <summary>
   /// Logistics-owned result of attempting to consume inventory.
   /// Sealed class with an outcome discriminator — extensible to carry an optional
   /// available-amount field without breaking the contract.
   /// </summary>
   public sealed class ConsumeInventoryResult
   {
       public ConsumeInventoryOutcome Outcome { get; init; }

       public static ConsumeInventoryResult Success() => new() { Outcome = ConsumeInventoryOutcome.Success };
       public static ConsumeInventoryResult InventoryNotFound() => new() { Outcome = ConsumeInventoryOutcome.InventoryNotFound };
       public static ConsumeInventoryResult InsufficientStock() => new() { Outcome = ConsumeInventoryOutcome.InsufficientStock };
   }
   ```

2. Confirm the expected compile break. From the repository root, build just the Application project (it will fail — this is expected, and confirms the three production call sites you're about to fix are the only ones breaking in this project):

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
   ```

   Expected output: build fails with three `CS1729` errors (`'ConsumeInventoryResult' does not contain a constructor that takes 1 arguments`), each pointing at `ManufactureInventoryReservationAdapter.cs` lines 42, 57, and 61.

3. Open `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` and make these three substitutions inside `TryConsumeAsync`:

   Line 42 — change:
   ```csharp
           if (item is null)
           {
               return new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound);
           }
   ```
   to:
   ```csharp
           if (item is null)
           {
               return ConsumeInventoryResult.InventoryNotFound();
           }
   ```

   Line 57 — change:
   ```csharp
           catch (InvalidOperationException)
           {
               return new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock);
           }
   ```
   to:
   ```csharp
           catch (InvalidOperationException)
           {
               return ConsumeInventoryResult.InsufficientStock();
           }
   ```

   Line 61 — change:
   ```csharp
           await _inventoryRepository.UpdateAsync(item, cancellationToken);
           return new ConsumeInventoryResult(ConsumeInventoryOutcome.Success);
   ```
   to:
   ```csharp
           await _inventoryRepository.UpdateAsync(item, cancellationToken);
           return ConsumeInventoryResult.Success();
   ```

   Do not change anything else in this file — the branching logic (item-not-found check, `try`/`catch (InvalidOperationException)`, the success path) is unchanged, only the three `ConsumeInventoryResult` construction expressions.

4. Build the Application project again and confirm it now succeeds:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
   ```

   Expected output: `Build succeeded.` with `0 Error(s)`.

5. Confirm there are no remaining positional-constructor calls in the source tree (test project excluded — that's task 2):

   ```bash
   grep -rn "new ConsumeInventoryResult(" backend/src/
   ```

   Expected output: no matches (empty output).

6. Run `dotnet format` on the touched files only, scoped to the Application project, to match the project's formatting conventions:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --include backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs
   ```

   Expected output: exits with no errors (it may reformat whitespace only — re-open both files afterward and confirm the code shown in steps 1 and 3 above is still semantically intact).

7. Stage and commit just these two files:

   ```bash
   cd /home/user/worktrees/feature-4088-Arch-Review-Logistics-Consumeinventoryresult-Uses
   git add backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs
   git commit -m "Convert ConsumeInventoryResult from record to class per CLAUDE.md DTO rule"
   ```

   Expected output: a new commit containing exactly these two files.

---

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
