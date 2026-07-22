## Module
Purchase

## Finding
`GetByNameAsync` is implemented on both the concrete repository and its test mock, but is not declared on the interface:

- `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs` — line 49
- `backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs` — line 56

`ISupplierRepository` (`backend/src/Anela.Heblo.Domain/Features/Purchase/ISupplierRepository.cs`) declares only two methods:
```csharp
Task<IEnumerable<Supplier>> SearchSuppliersAsync(string searchTerm, int limit = 0, CancellationToken cancellationToken = default);
Task<Supplier?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
```

`FlexiSupplierRepository` is registered only as `ISupplierRepository` (singleton, `FlexiAdapterServiceCollectionExtensions.cs:69`) — it is never registered as its concrete type. All call-sites receive `ISupplierRepository`, so `GetByNameAsync` is unreachable through the DI container. No call site in the codebase invokes this method via any path.

The presence of the method on the mock as well suggests it was intended to be on the interface but was either removed or never promoted.

## Why it matters
- **YAGNI / dead code**: the method exists in two files but is unreachable. It signals intent to readers without fulfilling it — a future developer reading `FlexiSupplierRepository` or the mock may reasonably assume it is used somewhere and spend time searching.
- **Mock contract drift**: `MockSupplierRepository` implements a method that `ISupplierRepository` does not require. If someone adds `GetByNameAsync` to the interface and the mock implementation is wrong, the compiler won't catch it.
- **Duplicate `using` directive**: `MockSupplierRepository.cs` lines 1–2 import `Anela.Heblo.Domain.Features.Purchase` twice (same namespace, consecutive lines) — a minor hygiene issue introduced alongside the dead method.

## Suggested fix
Two options depending on intent:

**Option A — the method is not needed**: Remove `GetByNameAsync` from both `FlexiSupplierRepository` and `MockSupplierRepository`. Remove the duplicate `using` from `MockSupplierRepository.cs`.

**Option B — name-based lookup is actually needed**: Add `Task<Supplier?> GetByNameAsync(string name, CancellationToken cancellationToken = default)` to `ISupplierRepository`. The implementations already exist and are correct. Wire a call site.

Option A is correct unless a concrete handler needs this method.

---
_Filed by daily arch-review routine on 2026-07-19._
