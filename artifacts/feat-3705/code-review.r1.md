## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

The diff is a pure, minimal deletion: the unreachable `GetByNameAsync` method is removed from `FlexiSupplierRepository.cs` and `MockSupplierRepository.cs`, and the duplicate `using Anela.Heblo.Domain.Features.Purchase;` line is removed from the mock. `ISupplierRepository` still declares only `SearchSuppliersAsync` and `GetByIdAsync`, both untouched. A repo-wide grep confirms zero remaining references to `GetByNameAsync`. No behavior change, no new surface area, exactly matches spec.r1.md and task-plan.r1.md.
