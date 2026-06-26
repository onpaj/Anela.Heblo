I've verified the spec against the codebase. The five "Find*" methods are referenced only in the three files the spec targets, `CreateTestSyncData()` is shared with retained tests (must stay), and `SetLastSyncTime()` is used only by the to-be-removed `FindStaleInvoicesAsync` test (becomes dead code).

# Architecture Review: Remove Dead Query Methods from `IIssuedInvoiceRepository`

## Skip Design: true

## Architectural Fit Assessment
The change strengthens existing patterns and introduces nothing new. The Invoices module already follows the project's Vertical Slice + Clean Architecture layout, with the repository contract in `Application/Features/Invoices/Contracts/` and the EF Core implementation in `Application/Features/Invoices/Infrastructure/`. All production consumers — `GetIssuedInvoicesListHandler`, `GetIssuedInvoiceSyncStatsHandler`, `GetIssuedInvoiceDetailHandler`, `InvoiceImportService`, `InvoiceConsumptionSourceAdapter` — drive their queries through `GetPaginatedAsync`, `GetSyncStatsAsync`, `GetByIdAsync`, `GetByIdWithSyncHistoryAsync`, and `GetHeadersByDateAsync`. A targeted grep across `backend/` confirms the five removal candidates are referenced only in the three files the spec touches. Removing them is therefore a contract narrowing that aligns the interface with actual use-cases (ISP/YAGNI) without affecting integration points.

There is one secondary consumer worth naming: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` references the type `IIssuedInvoiceRepository` for boundary assertions. This test asserts placement/visibility of the type, not specific members, so it remains green.

## Proposed Architecture

### Component Overview
```
Application/Features/Invoices/
├── Contracts/
│   └── IIssuedInvoiceRepository  ── narrowed: 4 specialized + IRepository<,> base
├── Infrastructure/
│   └── IssuedInvoiceRepository   ── 5 method bodies removed; remaining methods untouched
└── UseCases/, Services/, Infrastructure/ adapters  ── no edits (already use retained API)

test/Anela.Heblo.Tests/Features/Invoices/
└── IssuedInvoiceRepositoryTests   ── 7 [Fact] methods + SetLastSyncTime helper removed
```

No new components, no DI changes, no controller/contract changes.

### Key Design Decisions

#### Decision 1: Pure deletion vs. `[Obsolete]` deprecation window
**Options considered:**
1. Mark methods `[Obsolete]` and remove later.
2. Delete outright (spec's approach).

**Chosen approach:** Delete outright.

**Rationale:** The methods are repository-internal — no public API, no NuGet consumers, no cross-module callers. Deprecation provides value only when external clients exist. Reversibility is trivial (single-file git revert), so the cost of restoration if needed is negligible.

#### Decision 2: Helper-method cleanup scope
**Options considered:**
1. Delete only the seven `[Fact]` methods listed in FR-3.
2. Also delete private helpers that become unreferenced after the test removal.

**Chosen approach:** Delete helpers that become dead.

**Rationale:** `SetLastSyncTime` (line 458) is used **only** by `FindStaleInvoicesAsync_WithStaleInvoices_ReturnsUnsyncedAndOldSynced`. After that test goes, the helper is dead reflection-based code and should be removed in the same commit — leaving it would re-create the YAGNI smell on a smaller scale and is explicitly within the spec's intent ("arrange-only helper code that exists solely to support these tests"). `CreateTestSyncData` (line 445) **must remain** — it is still used by `GetByIdWithSyncHistoryAsync_…`, `GetSyncStatsAsync_…`, and the two `GetPaginatedAsync_…` sync/error tests.

#### Decision 3: `using` directive cleanup
**Options considered:** Re-run `dotnet format` to drop newly-unused imports vs. manual review.

**Chosen approach:** Trust `dotnet format` + verify with `--verify-no-changes`.

**Rationale:** In `IssuedInvoiceRepository.cs`, every current `using` (`Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging`, the project namespaces) is still required by retained methods. No drift expected, but `dotnet format` is the project's enforced gate per NFR-1.

## Implementation Guidance

### Directory / Module Structure
No structural change. Edits are confined to three existing files:
- `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs` — remove method declarations + XML doc-comment blocks at lines 13–16, 18–21, 23–26, 28–31, 38–41.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/IssuedInvoiceRepository.cs` — remove method bodies at lines 37–88.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` — remove seven `[Fact]` methods at lines 119–164, 166–187, 189–214, 216–243, 285–326, and the `SetLastSyncTime` helper at lines 458–462.

### Interfaces and Contracts
Post-change `IIssuedInvoiceRepository` shape:
```csharp
public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken ct = default);
    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken ct = default);
    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken ct = default);
    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken ct = default);
}
```
All existing parameter names and types (`cancellationToken` per repo convention, not `ct`) are preserved verbatim from the current file — do not introduce stylistic edits during the deletion.

### Data Flow
Unchanged for every production path. Filtering on the live read path continues to flow:
```
Controller/MediatR → GetIssuedInvoicesListHandler → IIssuedInvoiceRepository.GetPaginatedAsync(IssuedInvoiceFilters) → EF Core query
```
Sync stats, detail lookup, header-by-date, and import paths likewise route through their existing retained methods.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Hidden caller via reflection or DI extension | Low | `grep -r` across `backend/` shows zero non-test references; no `nameof(...)` or string-keyed lookups found. |
| Removing `SetLastSyncTime` breaks an unrelated test | Low | Verified: it is referenced only on lines 224 & 228, both inside the to-be-deleted `FindStaleInvoicesAsync_…` test. |
| `dotnet format` reformats adjacent code, violating "Surgical diff" (NFR-4) | Low–Medium | The repo's existing files are already formatted; run `dotnet format --verify-no-changes` after the edit and revert any incidental changes outside the three target files. |
| OpenAPI/TS-client regeneration triggered by build | Very Low | The removed methods are not exposed via controllers/handlers — the OpenAPI surface is unaffected. CI generation will produce a no-op diff. |
| Coverage report visibly drops | Low (cosmetic) | Expected and acceptable — only deleted-method coverage disappears. Document briefly in the PR description. |
| Architecture boundary test (`ModuleBoundariesTests`) fails | Very Low | The test asserts type placement, not member counts; the interface type itself is retained. |

## Specification Amendments
The spec is sound. Two small refinements should be folded in to avoid ambiguity during implementation:

1. **FR-3 — name the helper explicitly.** The spec says "arrange-only helper code that exists solely to support these tests … must be removed together." Make this concrete: also remove `private static void SetLastSyncTime(IssuedInvoice invoice, DateTime syncTime)` at lines 458–462. Keep `CreateTestSyncData` (line 445) — it is shared with retained tests.

2. **FR-4 — clarify scope of "untouched."** `git diff` is expected to show edits to **three files**: the interface, the repository implementation, and the test file. Adding "no other files in the repo may show modifications" makes the surgical-diff requirement objectively verifiable.

No other changes to the spec are warranted; functional and non-functional requirements remain accurate and complete.

## Prerequisites
None. No migration, configuration change, infrastructure update, feature flag, or coordinated rollout is required. The change is a single self-contained commit and is fully reversible by `git revert`. Implementation can begin immediately and proceed under the project's standard validation gates (`dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`).