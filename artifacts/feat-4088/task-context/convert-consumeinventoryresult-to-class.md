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
