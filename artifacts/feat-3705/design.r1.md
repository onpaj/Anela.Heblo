# Design: Remove dead GetByNameAsync from FlexiSupplierRepository/MockSupplierRepository

## Component Design
No new components. `FlexiSupplierRepository` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs`) and `MockSupplierRepository` (`backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs`) each lose their unreachable `GetByNameAsync` method. Both classes continue implementing only `ISupplierRepository` (`SearchSuppliersAsync`, `GetByIdAsync`) unchanged. The duplicate `using Anela.Heblo.Domain.Features.Purchase;` line in `MockSupplierRepository.cs` is removed.

## Data Schemas
None — no interface, DTO, or API shape changes.
