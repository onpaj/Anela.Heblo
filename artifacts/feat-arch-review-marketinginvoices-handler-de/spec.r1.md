# Specification: Decouple ImportMarketingInvoicesHandler via IMarketingInvoiceImportService

## Summary
Introduce an `IMarketingInvoiceImportService` abstraction to invert the dependency between `ImportMarketingInvoicesHandler` (a MediatR high-level policy) and `MarketingInvoiceImportService` (a low-level implementation detail). The change enables true unit-test isolation of the handler, restores Dependency Inversion compliance, and allows alternative import strategies to be supplied without modifying the handler.

## Background
A daily architecture review (2026-05-25) flagged that `ImportMarketingInvoicesHandler` takes the concrete `MarketingInvoiceImportService` rather than an interface. Two consequences follow:

1. **Test coupling.** `ImportMarketingInvoicesHandlerTests` instantiates the real service with a mocked repository; failures in service logic surface as handler-test failures, obscuring root cause and making the handler test effectively an integration test of the service.
2. **DIP violation.** A MediatR handler is a high-level policy; it should depend on an abstraction, not on a service implementation. Today the import strategy cannot be swapped or mocked without touching the handler.

This change is a small, localized refactor with no behavioral change. It aligns the `MarketingInvoices` slice with the project's existing pattern of service-behind-interface (used elsewhere in the codebase) and restores symmetry with how the handler already depends on `IMarketingInvoiceRepository`.

## Functional Requirements

### FR-1: Introduce `IMarketingInvoiceImportService` interface
Define a new interface `IMarketingInvoiceImportService` in `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/` that mirrors the public surface of `MarketingInvoiceImportService` currently consumed by `ImportMarketingInvoicesHandler`.

The interface exposes exactly one method (matching the existing service signature):
```csharp
Task<MarketingImportResult> ImportAsync(
    IMarketingTransactionSource source,
    DateTime from,
    DateTime to,
    CancellationToken ct = default);
```

**Acceptance criteria:**
- File `Services/IMarketingInvoiceImportService.cs` exists in the `MarketingInvoices` feature folder.
- Interface lives in the same namespace as `MarketingInvoiceImportService`.
- Method signature matches the existing concrete method (parameter names, types, order, `CancellationToken` default).
- No other public method on `MarketingInvoiceImportService` currently consumed by the handler is omitted; if additional public members exist that are used by the handler, they are included.

### FR-2: Implement the interface on `MarketingInvoiceImportService`
Declare `MarketingInvoiceImportService : IMarketingInvoiceImportService`. The existing method body is unchanged.

**Acceptance criteria:**
- `MarketingInvoiceImportService` class declaration explicitly implements `IMarketingInvoiceImportService`.
- No changes to method bodies, internal state, constructor dependencies, or behavior.
- `dotnet build` succeeds.

### FR-3: Switch handler to the interface
Change `ImportMarketingInvoicesHandler` to depend on `IMarketingInvoiceImportService` instead of the concrete type.

**Acceptance criteria:**
- Field at line 12 of `ImportMarketingInvoicesHandler.cs` is typed `IMarketingInvoiceImportService`.
- Constructor parameter at line 17 is typed `IMarketingInvoiceImportService`.
- No other code in the handler is modified.
- Handler behavior is unchanged (same `Handle` flow, same error handling, same return shape).

### FR-4: Update DI registration
In `MarketingInvoicesModule.cs`, register the interface-to-implementation mapping rather than the concrete type alone.

**Acceptance criteria:**
- Line 13 changes from `services.AddScoped<MarketingInvoiceImportService>();` to `services.AddScoped<IMarketingInvoiceImportService, MarketingInvoiceImportService>();`.
- Lifetime remains `Scoped`.
- No other registrations in the module are modified.
- Application starts successfully; the handler resolves the implementation through the container.

### FR-5: Refactor handler test to mock the interface
Update `ImportMarketingInvoicesHandlerTests` so the handler is exercised against a mocked `IMarketingInvoiceImportService` rather than the real service.

**Acceptance criteria:**
- Test no longer instantiates `MarketingInvoiceImportService` directly.
- A mock (`Mock<IMarketingInvoiceImportService>`, consistent with the test project's mocking framework) is supplied to the handler.
- The mocked repository previously used solely to satisfy the real service may be removed if no longer required by the handler directly.
- Existing test scenarios are preserved: each assertion that previously verified handler behavior continues to do so; tests of service-internal behavior are out of scope for this change (see Out of Scope).
- All existing tests in `ImportMarketingInvoicesHandlerTests` pass.

### FR-6: Preserve existing service-level coverage
If existing tests in `ImportMarketingInvoicesHandlerTests` were de facto covering service logic via the real service, leave equivalent coverage in place. Either (a) leave a dedicated `MarketingInvoiceImportServiceTests` class if one already exists, or (b) note in a code comment on any removed scenario which test file now owns the coverage. Do **not** delete coverage without a documented replacement.

**Acceptance criteria:**
- No net reduction in assertions covering `MarketingInvoiceImportService` behavior.
- If scenarios are moved, the destination test file exists and contains the equivalent assertions.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact expected. DI resolution of an interface-bound scoped service is equivalent in cost to resolution of a concrete scoped service.

### NFR-2: Security
None. Pure refactor; no change to authentication, authorization, data validation, or persistence behavior.

### NFR-3: Backward compatibility
- No public API contract changes (no controller, MediatR request, or DTO signatures modified).
- No database schema changes.
- No configuration changes.
- No changes to any caller of `ImportMarketingInvoicesHandler`.

### NFR-4: Testability
Handler must be unit-testable in true isolation: no construction or invocation of `MarketingInvoiceImportService` or its transitive dependencies (e.g., repository) inside `ImportMarketingInvoicesHandlerTests`.

### NFR-5: Code quality gates
- `dotnet build` passes.
- `dotnet format` passes.
- All tests in the `MarketingInvoices` feature area pass.
- No new analyzer warnings introduced.

## Data Model
No data model changes. The interface operates on existing types:
- `IMarketingTransactionSource` (existing)
- `MarketingImportResult` (existing)
- `MarketingInvoice` and related repository entities are untouched.

## API / Interface Design

### New interface
```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public interface IMarketingInvoiceImportService
{
    Task<MarketingImportResult> ImportAsync(
        IMarketingTransactionSource source,
        DateTime from,
        DateTime to,
        CancellationToken ct = default);
}
```

### Modified files
| File | Change |
|------|--------|
| `Services/IMarketingInvoiceImportService.cs` | **New.** Interface definition. |
| `Services/MarketingInvoiceImportService.cs` | Add `: IMarketingInvoiceImportService` to class declaration. |
| `UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs` | Change field type (line 12) and constructor parameter type (line 17) to `IMarketingInvoiceImportService`. |
| `MarketingInvoicesModule.cs` | Change line 13 registration to interface-to-implementation form. |
| `ImportMarketingInvoicesHandlerTests.cs` | Replace real-service instantiation with mocked interface. |

### Public HTTP / MediatR surface
Unchanged.

## Dependencies
- **Internal:** Existing `MarketingInvoices` feature slice (handler, service, repository, module, tests). No other feature slices touched.
- **External:** None. No new NuGet packages. The test project's existing mocking framework (already in use for `IMarketingInvoiceRepository`) is reused.

## Out of Scope
- Refactoring `MarketingInvoiceImportService` internals.
- Adding new import strategies or alternative implementations of `IMarketingInvoiceImportService` (the change only enables this).
- Renaming or relocating `MarketingInvoiceImportService`.
- Introducing interfaces for other concrete dependencies in the `MarketingInvoices` slice that may also violate DIP — those are separate findings if they exist.
- Changing the MediatR handler's logic, error handling, return shape, or logging.
- Modifying `IMarketingTransactionSource`, `MarketingImportResult`, or repository contracts.
- Adding integration tests; the existing test pyramid is unchanged.

## Open Questions
None.

## Status: COMPLETE