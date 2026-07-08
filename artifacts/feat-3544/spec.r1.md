# Specification: Remove unused methods from IClassificationHistoryRepository

## Summary
`IClassificationHistoryRepository` in the InvoiceClassification module declares two methods, `GetHistoryAsync` and `GetHistoryByInvoiceIdAsync`, that have no callers anywhere in the codebase. This spec covers removing both methods from the interface and deleting their corresponding implementations in `ClassificationHistoryRepository`, leaving the interface's actual consumed surface (`AddAsync`, `GetPagedHistoryAsync`) unchanged.

## Background
This is a dead-code cleanup identified by the daily architecture-review routine on 2026-07-07. `IClassificationHistoryRepository` currently declares four members, but only two are used:

- `AddAsync(ClassificationHistory history)` — called from `InvoiceClassificationService.cs:113`.
- `GetPagedHistoryAsync(...)` — called from `GetClassificationHistoryHandler.cs:27`, the sole handler that reads classification history.

The other two members are unused dead code:

- `GetHistoryAsync(int skip = 0, int take = 50)` — declared at `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs:7`, implemented at `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs:22-30`.
- `GetHistoryByInvoiceIdAsync(string abraInvoiceId)` — declared at `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs:9`, implemented at `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs:32-39`.

A repository-wide search confirms neither method is referenced by any handler, service, controller, test, or mock setup outside their own declaration/implementation. The only consumer of `IClassificationHistoryRepository` besides the repository implementation itself is `InvoiceClassificationServiceTests.cs`, which mocks the interface via `Mock<IClassificationHistoryRepository>` but never stubs or asserts against these two methods.

Dead methods on a domain-facing interface violate YAGNI and the Interface Segregation Principle: every consumer and every mock of `IClassificationHistoryRepository` is forced to account for surface area it never uses. Additionally, `GetHistoryByInvoiceIdAsync`'s parameter name `abraInvoiceId` is filtered against the `AbraInvoiceId` column, which is a separate but easily confused concept from `InvoiceNumber` (used as a filter in `GetPagedHistoryAsync`) — removing the method also removes this latent source of confusion for a future implementer. (The naming ambiguity itself is tracked as a companion issue and is not in scope here.)

## Functional Requirements

### FR-1: Remove dead methods from IClassificationHistoryRepository
Delete the following two member declarations from `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs`:
- `Task<List<ClassificationHistory>> GetHistoryAsync(int skip = 0, int take = 50);` (line 7)
- `Task<List<ClassificationHistory>> GetHistoryByInvoiceIdAsync(string abraInvoiceId);` (line 9)

The interface retains exactly two members after this change: `AddAsync` and `GetPagedHistoryAsync`.

**Acceptance criteria:**
- `IClassificationHistoryRepository` no longer declares `GetHistoryAsync` or `GetHistoryByInvoiceIdAsync`.
- `AddAsync` and `GetPagedHistoryAsync` signatures are unchanged (no parameter, return type, or default-value modifications).
- No blank/orphaned lines or stray whitespace left behind where the two method declarations were removed.

### FR-2: Remove dead implementations from ClassificationHistoryRepository
Delete the following two method implementations from `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs`:
- `GetHistoryAsync(int skip = 0, int take = 50)` (lines 22-30)
- `GetHistoryByInvoiceIdAsync(string abraInvoiceId)` (lines 32-39)

**Acceptance criteria:**
- `ClassificationHistoryRepository` no longer defines `GetHistoryAsync` or `GetHistoryByInvoiceIdAsync`.
- `ClassificationHistoryRepository` still implements `IClassificationHistoryRepository` with no other changes to `AddAsync` or `GetPagedHistoryAsync`.
- The class continues to compile without the removed methods (no other class member depends on them; none was found in the current codebase).

### FR-3: No behavioral change to existing consumers
`InvoiceClassificationService` (uses `AddAsync`) and `GetClassificationHistoryHandler` (uses `GetPagedHistoryAsync`) must continue to function identically — this change is a pure interface/implementation surface reduction with zero impact on any calling code, DI registration, or test setup.

**Acceptance criteria:**
- `InvoiceClassificationModule.cs:18` DI registration (`services.AddScoped<IClassificationHistoryRepository, ClassificationHistoryRepository>();`) requires no change.
- `GetClassificationHistoryHandler.cs` requires no change.
- `InvoiceClassificationService.cs` requires no change.
- `InvoiceClassificationServiceTests.cs` (which mocks `IClassificationHistoryRepository`) requires no change, since it never stubs the two removed methods.
- `ClassificationHistoryRepositoryTests.cs` (which exercises `GetPagedHistoryAsync` directly against the repository) requires no change, since it does not exercise the two removed methods.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable. This change removes unused code paths; it has no runtime performance impact on any executed code path.

### NFR-2: Security
Not applicable. No authentication, authorization, or data-sensitivity surface is touched. The removed methods performed read-only queries against `ClassificationHistory` and carried no distinct security posture from the retained `GetPagedHistoryAsync`.

## Data Model
No data model changes. `ClassificationHistory` (EF Core entity, mapped via `ApplicationDbContext.ClassificationHistory`) and its relationship to `ClassificationRule` (via `.Include(h => h.ClassificationRule)`) are unaffected — only two query methods against this existing model are removed.

## API / Interface Design
This is a backend-internal contract cleanup with no HTTP-facing surface:

**Before:**
```csharp
public interface IClassificationHistoryRepository
{
    Task<ClassificationHistory> AddAsync(ClassificationHistory history);
    Task<List<ClassificationHistory>> GetHistoryAsync(int skip = 0, int take = 50);
    Task<List<ClassificationHistory>> GetHistoryByInvoiceIdAsync(string abraInvoiceId);
    Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
        int page = 1, int pageSize = 20, DateTime? fromDate = null,
        DateTime? toDate = null, string? invoiceNumber = null, string? companyName = null);
}
```

**After:**
```csharp
public interface IClassificationHistoryRepository
{
    Task<ClassificationHistory> AddAsync(ClassificationHistory history);
    Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
        int page = 1, int pageSize = 20, DateTime? fromDate = null,
        DateTime? toDate = null, string? invoiceNumber = null, string? companyName = null);
}
```

No REST endpoints, MediatR requests/responses, or frontend contracts are affected — these methods were never exposed through `GetClassificationHistoryHandler` or any controller.

## Dependencies
None. This is a self-contained, two-file change within the InvoiceClassification module (`Anela.Heblo.Domain` and `Anela.Heblo.Persistence` projects). No other module, feature, or external service depends on the removed methods.

## Out of Scope
- Renaming or clarifying the `AbraInvoiceId` vs. `InvoiceNumber` naming ambiguity noted in the brief — tracked separately as a companion issue.
- Any change to `GetPagedHistoryAsync` or `AddAsync` behavior, signature, or tests.
- Any change to `GetClassificationHistoryHandler`, `InvoiceClassificationService`, `InvoiceClassificationModule`, or any test file.
- Re-introducing either removed method — if a future use case needs paged-free history retrieval or lookup-by-invoice-id, it should be added fresh when a concrete consumer exists, per YAGNI.

## Open Questions
None.

## Status: COMPLETE
