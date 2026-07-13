# Architecture Review: Deduplicate the "critical error" business rule in IssuedInvoice

## Skip Design: true

No UI, contract, or DTO surface changes. This is an internal refactor confined to the Domain and Persistence layers — `IssuedInvoiceDto`, `IssuedInvoiceSyncStats`, and every downstream API response are byte-identical before and after.

## Architectural Fit Assessment

This fits cleanly and requires no new pattern. The codebase already has a resolved instance of the exact same problem class — "EF Core cannot translate a computed C# property into SQL, so the predicate gets re-typed as a raw lambda in the repository" — in `TransportBox` (`backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs:39-50`). I read the file directly and confirmed the pattern verbatim:

```csharp
public static Expression<Func<TransportBox, bool>> IsInTransportPredicate = b => b.State == TransportBoxState.InTransit || ...;
public static Func<TransportBox, bool> IsInTransportFunc = IsInTransportPredicate.Compile();
public bool IsInTransit => IsInTransportFunc(this);
```

This is repeated three times in the same file (`IsInTransportPredicate`/`IsInReservePredicate`/`IsInQuarantinePredicate`), so it is an established, multi-instance convention within this entity's own module, not a one-off. `IssuedInvoice.IsCriticalError` (`backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs:53`) and `IssuedInvoiceRepository.GetSyncStatsAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs:43`) are structurally identical to the problem `TransportBox` already solves. Applying the same shape here is the correct move — introducing a different abstraction (specification objects, a rules engine, a shared static helper class) would add a second convention for one problem the codebase has already standardized on.

**Layering check (Clean Architecture):**
- `IssuedInvoice` lives in `Anela.Heblo.Domain`, which already references `System.Linq.Expressions` transitively through no external package — it's part of the BCL (`System.Linq.Expressions` namespace, no NuGet dependency). `TransportBox.cs:2` already does `using System.Linq.Expressions;` in the same project, so this introduces zero new project references and zero new package dependencies.
- The repository (`Anela.Heblo.Persistence`) already references `Anela.Heblo.Domain` (it depends on `IssuedInvoice` as its entity type), so consuming a `static` member off `IssuedInvoice` from `IssuedInvoiceRepository` doesn't cross any boundary that isn't already crossed.
- No Application-layer or API-layer changes are implicated. `InvoicesMappingProfile.cs:16` and `:19` map `IssuedInvoiceDto.IsCriticalError`/`IssuedInvoiceDetailDto.IsCriticalError` from `src.IsCriticalError` — the public property, whose signature is preserved — so AutoMapper configuration needs no changes. Verified directly by reading `InvoicesMappingProfile.cs`.
- Per `docs/architecture/development_guidelines.md`, DTOs must be classes (not records) and business logic belongs in the domain/handlers, not controllers — this change reinforces that guidance by moving the one true definition of "critical error" fully into the domain entity, rather than splitting it between domain and persistence.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs
  ├── static Expression<Func<IssuedInvoice,bool>> IsCriticalErrorPredicate   (single source of truth)
  ├── static Func<IssuedInvoice,bool> IsCriticalErrorFunc = Predicate.Compile()
  └── bool IsCriticalError => IsCriticalErrorFunc(this)        (in-memory / entity consumers)
              │
              ├── consumed in-memory by ──► InvoicesMappingProfile (IssuedInvoiceDto / IssuedInvoiceDetailDto)
              └── consumed as SQL by ─────► IssuedInvoiceRepository.GetSyncStatsAsync
                                              query.CountAsync(IssuedInvoice.IsCriticalErrorPredicate, ct)
```

No new components, no new files, no new module. One entity gains two `static` members; one repository method swaps an inline lambda for a reference to those members.

### Key Design Decisions

#### Decision 1: Reuse the `TransportBox` Expression+Func+property pattern verbatim
**Options considered:**
- (a) Copy the `TransportBox` pattern onto `IssuedInvoice` (static `Expression<Func<T,bool>>` + compiled `Func<T,bool>` + delegating property).
- (b) Introduce a generic reusable "Specification<T>" or predicate-registry abstraction shared across entities.
- (c) Keep two definitions but add a test asserting they agree (guard rail only, no dedup).

**Chosen approach:** (a).

**Rationale:** The spec explicitly scopes this to matching the existing convention ("Out of Scope: broader introduction of a specification pattern"), and the codebase already has this exact solution proven in production (`TransportBox`) with three live instances. Option (b) is a premature abstraction for a single call site and would introduce a new pattern where a proven one already exists — that's a net increase in surface area for a "small, well-scoped fix." Option (c) doesn't fix the root cause (the finding explicitly asks for deduplication, not just a regression guard), though FR-4 correctly still adds the guard test on top of (a) as defense-in-depth.

#### Decision 2: Where the static members live
**Options considered:**
- Inside `IssuedInvoice` itself (mirrors `TransportBox`).
- On `IIssuedInvoiceRepository` (as the original brief's suggested-fix snippet floated).

**Chosen approach:** Inside `IssuedInvoice`, per the spec (FR-1/FR-2) and the `TransportBox` precedent — predicates live on the entity, not the repository interface.

**Rationale:** `TransportBox` puts all three of its predicates on the entity, not on `ITransportBoxRepository`. Putting the predicate on the repository interface would mean the Persistence layer (or its interface, defined in Domain per this codebase's repository-interface convention) owns a piece of business logic that conceptually belongs to the entity — the entity is the aggregate that knows what "critical" means, the repository is just a data-access mechanism. This also keeps the single source of truth reachable both in-memory (`entity.IsCriticalError`) and as SQL (`IssuedInvoice.IsCriticalErrorPredicate`) without an extra indirection through the repository contract.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Two existing files change:

- `backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoice.cs` — add `using System.Linq.Expressions;` at the top; add the two `static` members immediately above the existing `IsCriticalError` property (line 53), then replace that property's body.
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — replace the inline lambda at line 43 in `GetSyncStatsAsync` with `IssuedInvoice.IsCriticalErrorPredicate`.

Test file: per FR-4, no `IssuedInvoiceTests.cs` (entity-only unit test file) currently exists in `backend/test/Anela.Heblo.Tests/Features/Invoices/` — confirmed by directory listing. Create it there, sibling to `IssuedInvoiceRepositoryTests.cs`, rather than folding the entity-level test into the repository test file — the repository test class is already set up around an `IDisposable` in-memory-DB fixture (`IssuedInvoiceRepositoryTests.cs:12-21`), which is unnecessary overhead for a pure entity/predicate agreement test that needs no database.

### Interfaces and Contracts

Exact shape (matches spec FR-1/FR-2 verbatim, and mirrors `TransportBox.cs:39-41` structurally):

```csharp
// IssuedInvoice.cs — replace line 53, add using System.Linq.Expressions; at top
public static Expression<Func<IssuedInvoice, bool>> IsCriticalErrorPredicate =
    x => x.ErrorType != null && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired;
public static Func<IssuedInvoice, bool> IsCriticalErrorFunc = IsCriticalErrorPredicate.Compile();
public bool IsCriticalError => IsCriticalErrorFunc(this);
```

```csharp
// IssuedInvoiceRepository.cs:43 — replace the inline lambda
var criticalErrors = await query.CountAsync(IssuedInvoice.IsCriticalErrorPredicate, cancellationToken);
```

No interface (`IIssuedInvoiceRepository`) changes — this is a private implementation-body edit inside an existing method, confirmed by reading `IIssuedInvoiceRepository`'s usage; the spec's API/Interface Design section already states this and the code confirms `GetSyncStatsAsync`'s signature is unaffected.

One naming caution not spelled out in the spec: `TransportBox` declares its statics with `=` field initializers rather than `=>` expression-bodied properties (`public static Expression<...> X = ...;`, note **no** `get`). Follow that exact style (fields, not properties) for consistency — mixing field-style statics on `IssuedInvoice` with property-style elsewhere in the same file would be a needless stylistic divergence from the precedent this change is explicitly modeled on.

### Data Flow

1. **In-memory / DTO path** (list view, detail view): handler loads `IssuedInvoice` → AutoMapper (`InvoicesMappingProfile`) reads `src.IsCriticalError` → property invokes `IsCriticalErrorFunc(this)` → same boolean as today, computed via the compiled delegate instead of a restated condition.
2. **SQL aggregation path** (`GetSyncStatsAsync` → dashboard stats): `IIssuedInvoiceRepository.GetSyncStatsAsync` → `query.CountAsync(IssuedInvoice.IsCriticalErrorPredicate, ct)` → EF Core translates the `Expression<Func<IssuedInvoice,bool>>` into a SQL `WHERE` clause inside `COUNT(*)`, same as the current inline lambda does today (this is the same translation mechanism EF already performs for the lambda at line 43; swapping the lambda's source location does not change how EF compiles it to SQL).

Both paths now read from the one expression tree defined at `IssuedInvoice.IsCriticalErrorPredicate`; there is no code path left where the boolean condition is retyped.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| EF Core InMemory provider (used by `IssuedInvoiceRepositoryTests`, per `IssuedInvoiceRepositoryTests.cs:20-21`) fails to evaluate a `static` field-backed `Expression<Func<T,bool>>` the same way as an inline lambda | Low | `TransportBox`'s identical pattern is already exercised against the same `ApplicationDbContext`/InMemory setup elsewhere in this codebase without issue; the expression tree shape (`x.Prop != null && x.Prop != Enum.Value`) is fully standard and provider-agnostic. Existing test `IssuedInvoiceRepositoryTests.cs:156` (`Assert.Equal(1, stats.CriticalErrors)`) exercises this exact path and must keep passing unmodified, per spec FR-3. |
| Static mutable fields (not `readonly`) on `IssuedInvoice`, matching `TransportBox`'s style, are technically reassignable at runtime | Low | This is pre-existing, accepted risk carried over from the `TransportBox` precedent, not a new one introduced by this change. Not in scope to fix — flagging only for awareness; do not "improve" it into `readonly`/`const`-style beyond what `TransportBox` already does, per the surgical-change rule. |
| Someone later adds a third call site with a hand-rolled predicate again (the original root cause) | Medium | FR-4's regression test is the correct mitigation — it enumerates every `IssuedInvoiceErrorType` value (including `null`) and asserts `IsCriticalError` agrees with the compiled predicate, so any future divergent hand-rolled condition fails loudly instead of silently. |

## Specification Amendments

None required to FR-1 through FR-4 — they are architecturally sound and directly match the verified `TransportBox` precedent. One clarification worth stating explicitly since the spec's code sample uses `=>` (property) syntax where `TransportBox` uses `=` (field) syntax for the two static members:

- **Style note (non-blocking):** Declare `IsCriticalErrorPredicate` and `IsCriticalErrorFunc` as plain `static` fields with `=` initializers (`public static Expression<Func<IssuedInvoice, bool>> IsCriticalErrorPredicate = x => ...;`), exactly matching `TransportBox.cs:39-40`, rather than as expression-bodied `static` properties (`=>` with `get`). Both compile to the same effective public surface for callers (`IssuedInvoice.IsCriticalErrorPredicate` reads identically either way), so this doesn't change FR-1/FR-3's acceptance criteria — it's purely about matching the precedent's exact style so the two entities don't visibly diverge in convention.

## Prerequisites

None. No migrations, no config, no new packages, no infrastructure changes. `System.Linq.Expressions` is part of the .NET BCL and already `using`'d in the same project (`TransportBox.cs:2`). Implementation can start immediately.
