# Specification: Remove unreachable `GetByNameAsync` from Supplier repositories

## Summary
`GetByNameAsync` is implemented on `FlexiSupplierRepository` and `MockSupplierRepository` but is absent from the `ISupplierRepository` interface they both implement, making it unreachable dead code (both types are consumed only via the interface). This change removes the method from both implementations and cleans up an unrelated duplicate `using` directive introduced in the same file as the mock's dead method.

## Background
`FlexiSupplierRepository` is registered in DI only as `ISupplierRepository` (singleton, `FlexiAdapterServiceCollectionExtensions.cs:69`), never as its concrete type, so any member not declared on the interface is unreachable through normal application code paths. A codebase-wide search for `GetByNameAsync` confirms the only two occurrences are the method's own definitions in `FlexiSupplierRepository.cs` and `MockSupplierRepository.cs` — there is no caller anywhere in `backend/`. This was flagged by the daily architecture review routine on 2026-07-19 as dead code / mock contract drift, filed as brief `feat-3705`.

## Functional Requirements

### FR-1: Remove `GetByNameAsync` from `FlexiSupplierRepository`
Delete the `GetByNameAsync` method (currently lines 49–53) from `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs`. Leave `SearchSuppliersAsync` and `GetByIdAsync` and all other members unchanged.
**Acceptance criteria:**
- `FlexiSupplierRepository` no longer declares a `GetByNameAsync` member.
- `FlexiSupplierRepository` still compiles and still implements `ISupplierRepository` in full (`SearchSuppliersAsync`, `GetByIdAsync`).
- No other member of the class is modified.

### FR-2: Remove `GetByNameAsync` from `MockSupplierRepository`
Delete the `GetByNameAsync` method (currently lines 56–60) from `backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs`. Leave `SearchSuppliersAsync` and `GetByIdAsync` unchanged.
**Acceptance criteria:**
- `MockSupplierRepository` no longer declares a `GetByNameAsync` member.
- `MockSupplierRepository` still compiles and still implements `ISupplierRepository` in full.

### FR-3: Remove duplicate `using` directive in `MockSupplierRepository.cs`
Lines 1–2 of `backend/test/Anela.Heblo.Tests/Controllers/MockSupplierRepository.cs` both import `Anela.Heblo.Domain.Features.Purchase`. Remove the duplicate so the namespace is imported once.
**Acceptance criteria:**
- Only one `using Anela.Heblo.Domain.Features.Purchase;` line remains in the file.
- File still compiles.

## Non-Functional Requirements

### NFR-1: No behavior change
This is a pure dead-code removal. No runtime behavior, API surface, or DI registration changes as a result of this work — `ISupplierRepository` itself is not modified.

### NFR-2: Build and test integrity
The solution must build cleanly and the full existing test suite must pass after the change, since no test currently exercises `GetByNameAsync` (it is unreachable) and none should need updating.

## Data Model
Not applicable — no data model or entity changes.

## API / Interface Design
Not applicable — `ISupplierRepository` is unchanged; no public/external API surface is affected. This is an internal implementation-only cleanup.

## Dependencies
None beyond the two files identified. No other file references `GetByNameAsync` on a supplier repository (verified via full-repository search).

## Out of Scope
- Any change to `ISupplierRepository` itself.
- Adding name-based supplier lookup to any call site (Option B from the brief) — the brief and codebase evidence (no call sites) confirm Option A (removal) applies.
- Any other cleanup in `FlexiSupplierRepository.cs`, `MockSupplierRepository.cs`, or `FlexiAdapterServiceCollectionExtensions.cs` beyond what is listed in FR-1 through FR-3.

## Open Questions
None.

## Status: COMPLETE
