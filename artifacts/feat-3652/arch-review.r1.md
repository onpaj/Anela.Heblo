# Architecture Review: Remove Analytics dependency from Bank's `IBankStatementImportRepository`

## Skip Design: true

Backend-only type/layering refactor. No controller, MediatR contract, OpenAPI surface, or frontend
code is touched — verified by direct read of every file in the call graph (`IBankStatementImportRepository.cs`,
`BankStatementImportRepository.cs`, `BankStatementStatisticsSourceAdapter.cs`, `AnalyticsRepository.cs`,
and the adapter's test file). `IBankStatementStatisticsSource` (the Analytics-facing contract actually
consumed by `AnalyticsRepository` → `GetInvoiceImportStatisticsHandler`'s sibling handler) is explicitly
unchanged. No new or changed UI components are implied by this change.

## Architectural Fit Assessment

This is a textbook instance of the **Consumer-Owned Contract** pattern already documented in
`docs/architecture/development_guidelines.md` under "Cross-Module Communication Example:
ILeafletKnowledgeSource" and already implemented correctly for Bank at the *Application* layer:
`BankStatementStatisticsSourceAdapter` implements Analytics' `IBankStatementStatisticsSource` and lives
in `Anela.Heblo.Application/Features/Bank/Infrastructure/`. The violation is one layer deeper: someone
serviced that adapter by adding `GetDailyStatisticsAsync(..., BankStatementDateType, ...) :
IReadOnlyList<DailyBankStatementStatistics>` directly onto the *Domain*-owned
`IBankStatementImportRepository`, which forced `using Anela.Heblo.Domain.Features.Analytics;` into
Bank's Domain layer — the one layer where this project's rules are strictest (`docs/architecture/development_guidelines.md`,
"Forbidden Practices": *Direct access to another module's entities*).

Two confirming facts from direct source inspection:

1. **A structurally identical, correctly-implemented precedent already exists one module over.**
   `IInvoiceImportStatisticsSource` (Analytics-owned) is implemented by
   `InvoiceImportStatisticsSourceAdapter` (Invoices-owned, `Application/Features/Invoices/Infrastructure/`),
   which queries `ApplicationDbContext` directly and returns `DailyInvoiceCount` — an Analytics-owned
   record. Notably, Invoices' adapter does *not* route through an `IInvoiceRepository`-style Domain
   interface at all for this query, sidestepping the leak by construction. Bank's situation differs only
   in that Bank already has a domain-owned `IBankStatementImportRepository` in active use for other
   queries, so routing this query through it (rather than duplicating a raw `DbContext` query in the
   adapter) is the more consistent choice for this module — which is exactly what the spec's Option A
   does.
2. **The existing architecture-boundary test suite has a directional gap that let this regress
   silently.** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` already encodes
   `"Analytics (Application) -> Bank"` and `"Analytics (Domain) -> Bank"` rules (forbidding Analytics
   from referencing Bank types) but has **no rule in the reverse direction** (`Bank (Domain) ->
   Analytics`). That is precisely the direction this bug violates, and precisely why `dotnet build` and
   the existing test suite did not catch it. See Prerequisites/Amendments below — closing this gap is a
   compounding requirement, not optional polish.

The fix (Option A, already selected in the spec) fits existing conventions exactly and requires no new
architectural concepts — `BankDailyCount` is a same-shape sibling of `DailyInvoiceCount`, and the
adapter-does-the-mapping-and-gap-fill shape mirrors `InvoiceImportStatisticsSourceAdapter` byte for byte
(same `ToUniversalTime`/`SpecifyKind` normalization, same dictionary-based gap-fill loop).

## Proposed Architecture

### Component Overview

```
Analytics (consumer, owns the contract)
  AnalyticsRepository (Persistence/Features/Analytics)
        │  calls IBankStatementStatisticsSource.GetDailyStatisticsAsync   [UNCHANGED — out of scope]
        ▼
  IBankStatementStatisticsSource (Domain/Features/Analytics)              [UNCHANGED — out of scope]
        △  implements
        │
Bank (provider, owns the adapter + its own repository contract)
  BankStatementStatisticsSourceAdapter (Application/Features/Bank/Infrastructure)
        │  maps BankDailyCount → DailyBankStatementStatistics, gap-fills   [CHANGED — internals only]
        │  calls IBankStatementImportRepository.GetDailyCountsAsync(..., bool byStatementDate, ...)
        ▼
  IBankStatementImportRepository (Domain/Features/Bank)                    [CHANGED — signature]
        △  implements
        │
  BankStatementImportRepository (Persistence/Features/Bank)                [CHANGED — signature + body]
        │  EF Core query against ApplicationDbContext.BankStatements
        ▼
  BankDailyCount (Domain/Features/Bank)                                    [NEW — Bank-owned record]
```

Dependency direction after the fix: `Anela.Heblo.Domain.Features.Bank` has zero references to
`Anela.Heblo.Domain.Features.Analytics`. The only file that still imports both namespaces is
`BankStatementStatisticsSourceAdapter` (Application layer) — which is correct and expected, since a
cross-module adapter's entire job is to sit at the seam and translate between the two.

### Key Design Decisions

#### Decision 1: Where the Bank-owned raw-count type lives, and whether to keep the query on the Domain repository at all (spec's Option A vs Option B)

**Options considered:**
- **Option A (spec-selected):** Add `BankDailyCount` to `Domain/Features/Bank/`, add
  `GetDailyCountsAsync(DateTime, DateTime, bool, CancellationToken)` to `IBankStatementImportRepository`,
  implement it in the EF repository, and have the adapter do the `BankDailyCount → DailyBankStatementStatistics`
  projection plus gap-fill.
- **Option B (spec-rejected):** Introduce an Analytics-aware internal query interface
  (`IBankDailyStatisticsQuery`) scoped to the Application layer's `Infrastructure/` folder, bypassing
  `IBankStatementImportRepository` entirely — closer to how `InvoiceImportStatisticsSourceAdapter`
  queries `ApplicationDbContext` directly rather than through `IInvoiceRepository`.

**Chosen approach:** Option A, as specified.

**Rationale:** `IBankStatementImportRepository` is Bank's single, actively-used repository abstraction
(`GetFilteredAsync`, `GetByIdAsync`, `AddAsync`, `GetExistingResultsByTransferIdsAsync`,
`GetMaxStatementDateAsync`, `GetByTransferIdAsync`, `UpdateAsync` all already live there). Adding one
more Bank-scoped query method to it is the path of least surprise for anyone maintaining Bank; Option B
would create a second, parallel data-access surface for `BankStatements` with unclear ownership rules
about which queries go where. The Invoices module's direct-`DbContext`-in-adapter style is not a
counter-argument here — it reflects Invoices not having (or needing) a general-purpose repository for
this query, not a preferred pattern to import into a module that already has one. Confirmed via
`ModuleBoundariesTests.cs`: this rule set already enforces "adapter may reference both namespaces,
Domain must not" as the load-bearing constraint — Option A satisfies it identically to Option B, so the
simpler, more-consistent-with-existing-Bank-code option wins.

#### Decision 2: Parameter shape for the date-type selector (`bool byStatementDate` vs. a Bank-owned enum)

**Options considered:**
- A Bank-owned enum mirroring `BankStatementDateType` (e.g. `BankDateSelector { StatementDate,
  ImportDate }`).
- A plain `bool byStatementDate` (spec-selected).

**Chosen approach:** `bool byStatementDate`, as specified.

**Rationale:** The only two states are "group by statement date" or "group by import date" and no third
state is foreseeable for this Bank-internal query — introducing a Bank-owned enum purely to mirror
Analytics' `BankStatementDateType` would be manufacturing a parallel type for no behavioral gain. If a
third grouping dimension is ever needed, upgrade to an enum then; do not pre-build it now. This keeps
the boolean's two call sites (`BankStatementStatisticsSourceAdapter`'s
`dateType == BankStatementDateType.StatementDate` translation, and the repository's `if
(byStatementDate)` branch) trivially readable without an extra type to import across the seam.

## Implementation Guidance

### Directory / Module Structure

No new files/folders beyond one new type:

- **New:** `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs`
  ```csharp
  namespace Anela.Heblo.Domain.Features.Bank;

  public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);
  ```
  Follows the existing convention of `DailyInvoiceCount`/`DailyBankStatementStatistics` living directly
  under their owning module's `Domain/Features/{Module}/` folder as a standalone file (not nested under
  a subfolder) — match that flat placement, do not create a `Domain/Features/Bank/Statistics/` subfolder
  for a single record.

- **Modified:** `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — drop
  the `using Anela.Heblo.Domain.Features.Analytics;` line, replace the `GetDailyStatisticsAsync` method
  with `GetDailyCountsAsync` per FR-2.

- **Modified:** `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` —
  rename/retype the implementation (lines 143–195 today), drop the same `using`, switch the
  `dateType switch` on `BankStatementDateType.StatementDate`/`.ImportDate` to an `if (byStatementDate) /
  else` on the two existing LINQ query bodies (grouping/projection logic is otherwise untouched), return
  `BankDailyCount` instead of constructing `DailyBankStatementStatistics`.

- **Modified:** `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` —
  change the internal call to `_repository.GetDailyCountsAsync(startDate, endDate, dateType ==
  BankStatementDateType.StatementDate, cancellationToken)`, then map `BankDailyCount` → the existing
  `DailyBankStatementStatistics { Date, ImportCount, TotalItemCount }` construction already present in
  the gap-fill loop. The adapter's public method signature, its `IBankStatementStatisticsSource`
  implementation, and its UTC-normalization logic (lines 21–24) do not change.

- **Unmodified (do not touch, per spec and confirmed by grep):** `Domain/Features/Analytics/IBankStatementStatisticsSource.cs`,
  `Persistence/Features/Analytics/AnalyticsRepository.cs`,
  `test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs`.

### Interfaces and Contracts

```csharp
// Domain/Features/Bank/BankDailyCount.cs — NEW
namespace Anela.Heblo.Domain.Features.Bank;
public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);

// Domain/Features/Bank/IBankStatementImportRepository.cs — method replaced
Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    bool byStatementDate,
    CancellationToken cancellationToken = default);
```

Note: `BankDailyCount` is a `record` (internal domain type), not a DTO exposed across an API boundary —
this does not conflict with the project's "DTOs are classes, never records" rule, which governs
API-facing `Request`/`Response` contracts in `contracts/` folders, not Domain-internal value types like
this one or its sibling `DailyInvoiceCount`/`DailyBankStatementStatistics` (both already records/classes
used purely as in-process query results).

`IBankStatementStatisticsSource` (Analytics-owned, Domain/Features/Analytics) is unchanged — confirmed
byte-for-byte by direct read; this review does not propose touching it.

### Data Flow

1. `AnalyticsRepository.GetBankStatementImportStatisticsAsync(startDate, endDate, dateType, ct)` calls
   `_bankStatementStatisticsSource.GetDailyStatisticsAsync(...)` — **unchanged**.
2. `BankStatementStatisticsSourceAdapter.GetDailyStatisticsAsync` (implements
   `IBankStatementStatisticsSource`) normalizes `startDate`/`endDate` to UTC (unchanged), then calls
   `_repository.GetDailyCountsAsync(startDate, endDate, dateType == BankStatementDateType.StatementDate,
   cancellationToken)` — **new call site, same two inputs translated to a bool**.
3. `BankStatementImportRepository.GetDailyCountsAsync` runs the same EF Core grouping query as today
   (branch selected by `byStatementDate` instead of the enum), returns `IReadOnlyList<BankDailyCount>` —
   **new return type, same query semantics**.
4. Back in the adapter: build a `resultsByDate` dictionary from the `BankDailyCount` list, then run the
   existing day-by-day loop (`currentDate` from `startDate.Date` to `endDate.Date` inclusive), mapping
   each found `BankDailyCount` to a new `DailyBankStatementStatistics { Date, ImportCount, TotalItemCount
   }`, and gap-filling zero rows for missing dates exactly as today — **new mapping step inserted, same
   loop structure and output shape**.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Silent behavioral drift during the type swap (e.g. `TotalItemCount` field dropped, gap-fill loop broken) | Medium | `BankStatementStatisticsSourceAdapterTests.cs` already exercises exactly this: 5 tests covering both date-type branches, empty range, inclusive boundaries, and gap-fill, run against a real EF Core in-memory `BankStatementImportRepository` (not mocked). Spec correctly requires these to pass **unmodified** — treat any need to edit this test file as a signal the refactor changed observable behavior. |
| The same class of violation (Domain leaking another module's types) regresses again later, undetected until the next arch-review pass | Medium | `ModuleBoundariesTests.cs` has `"Analytics (Domain) -> Bank"` and `"Analytics (Application) -> Bank"` rules but **no `"Bank (Domain) -> Analytics"` rule** — the exact direction this bug occurred in. Add one (see Specification Amendments). This is what would have caught the original violation in CI instead of via a daily manual arch-review routine. |
| `byStatementDate` boolean is less self-documenting at call sites than the enum it replaces | Low | Confined to two call sites (adapter → repository interface, and inside the EF implementation), both already reviewed here; not worth an enum for a two-state, module-internal parameter (see Decision 2). |
| Renaming the interface method breaks a mock/stub elsewhere in the test suite | Low | Spec's grep-based reconnaissance (already verified in `brief.md`) found no other test file mocking `IBankStatementImportRepository.GetDailyStatisticsAsync`. Still run the full Bank test suite, not just the adapter test, before calling this done. |

## Specification Amendments

Add one item to FR scope, surfaced by this review's exploration of `ModuleBoundariesTests.cs` (not
present in `spec.r1.md`):

- **New FR-5 (recommended): Close the missing `Bank (Domain) -> Analytics` boundary rule.** In
  `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, add a `ModuleBoundaryRule`
  entry analogous to the existing `"Analytics (Domain) -> Bank"` rule (around line 477), but in the
  reverse direction:
  ```csharp
  new ModuleBoundaryRule(
      Name: "Bank (Domain) -> Analytics",
      InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Bank",
      ForbiddenNamespacePrefixes: new[]
      {
          "Anela.Heblo.Domain.Features.Analytics",
          "Anela.Heblo.Application.Features.Analytics",
          "Anela.Heblo.Persistence.Analytics",
      },
      Allowlist: new HashSet<string>(StringComparer.Ordinal),
      InspectedAssembly: "Anela.Heblo.Domain"),
  ```
  This is the rule that should have failed CI when `GetDailyStatisticsAsync` was originally added to
  `IBankStatementImportRepository`. Without it, this exact class of regression can reappear silently.
  This is a small, mechanical addition to an existing test file and fits inside this refactor's scope —
  it directly enforces NFR-3's acceptance criterion ("verifiable via a project/namespace reference check
  or a `using` grep") rather than leaving that verification manual. Treat it as in-scope; it requires no
  design discussion and follows the exact pattern already used four times in that file (Leaflet,
  Invoices, and the existing forward-direction Bank/Analytics rules).

No other amendments — FR-1 through FR-4, the NFRs, and the Out of Scope list in `spec.r1.md` are
accurate and complete per direct source verification.

## Prerequisites

None beyond the repository/adapter/test files already identified. No database migration (both
`BankDailyCount` and `DailyBankStatementStatistics` are transient in-memory query DTOs, not EF entities
— confirmed by their absence from any `*Configuration.cs` or `DbSet<>` in `ApplicationDbContext`), no
new DI registration (the adapter's binding and the repository's binding are unaffected — signature
changes only, not lifetime/registration changes), and no config/infrastructure changes. Implementation
can start immediately in the order FR-1 → FR-2 → FR-3 → FR-4 → (recommended) FR-5, running `dotnet build`
after FR-2/FR-3 to confirm the Analytics `using` is gone and the solution still compiles before touching
the adapter.
