# Task Plan: Remove dead GetByNameAsync from FlexiSupplierRepository/MockSupplierRepository

### task: remove-dead-getbyname

**Goal:** Remove the unreachable `GetByNameAsync` method from both `FlexiSupplierRepository` and `MockSupplierRepository`, and remove the duplicate `using` directive in the mock.

**Files:**
- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs`
- `backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs`

**Steps:**
1. In `FlexiSupplierRepository.cs`, delete the `GetByNameAsync` method (around line 49). Do not touch `SearchSuppliersAsync` or `GetByIdAsync`.
2. In `MockSupplierRepository.cs`, delete the `GetByNameAsync` method (around line 56).
3. In `MockSupplierRepository.cs`, remove the duplicate `using Anela.Heblo.Domain.Features.Purchase;` line (lines 1–2 currently import it twice — keep one).
4. Confirm `ISupplierRepository` is untouched (it never declared this method).
5. Run a repo-wide grep for `GetByNameAsync` to confirm no other reference exists before finishing.

**Acceptance criteria:**
- `GetByNameAsync` no longer exists anywhere in the codebase.
- `MockSupplierRepository.cs` has exactly one `using Anela.Heblo.Domain.Features.Purchase;` line.
- `dotnet build` succeeds.
- Existing tests referencing `MockSupplierRepository` / `ISupplierRepository` still pass (no test called `GetByNameAsync`, confirmed by the analyst's grep).

**Out of scope:** Adding `GetByNameAsync` to `ISupplierRepository` (Option B) — not needed, no call site.
