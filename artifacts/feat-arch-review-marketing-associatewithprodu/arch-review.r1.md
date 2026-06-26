# Architecture Review: Fix `MarketingAction.AssociateWithProduct` Duplicate Detection

## Skip Design: true

This is a domain-layer bug fix with no API, DTO, or UI changes. No new visual components or layout decisions are required.

## Architectural Fit Assessment

The change sits cleanly inside an existing aggregate root (`MarketingAction`) in the Domain layer, exactly where invariant enforcement belongs in the project's Clean Architecture / Vertical Slice setup. The fix moves a normalization step that already exists for the *write* path (`ProductCodePrefix = productCode.Trim().ToUpperInvariant()`) up so the same value drives the *guard*. No layer boundaries are crossed, no contracts shift, no new dependencies appear. Two MediatR handlers in the Application layer drive this method (`CreateMarketingActionHandler`, `UpdateMarketingActionHandler`) and both already iterate `.Distinct()`-filtered code lists — but `Enumerable.Distinct` is case-sensitive, so the domain-level dedup is the only line of defence today. Tightening the entity's guard preserves the contract direction (Application → Domain) and removes a leaky abstraction where the EF persistence layer's composite key was silently handling what the entity claims to handle.

Note on spec accuracy: the spec's Background section names `ImportFromOutlookHandler` as a caller of `AssociateWithProduct`. The current code at `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/ImportFromOutlook/ImportFromOutlookHandler.cs` and its mapper (`OutlookEventImportMapper`) do not call this method — product codes are not parsed out of Outlook event content today. Real callers are `CreateMarketingActionHandler` and `UpdateMarketingActionHandler`. This belongs in the spec as an amendment.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application (MediatR handlers — unchanged)          │
│  ├── CreateMarketingActionHandler  ─┐                            │
│  └── UpdateMarketingActionHandler  ─┤  foreach (...Distinct())   │
│                                     │  action.AssociateWithProduct(code)
│                                     ▼                            │
├─────────────────────────────────────────────────────────────────┤
│ Anela.Heblo.Domain (THE ONLY CHANGE LIVES HERE)                 │
│  └── MarketingAction.AssociateWithProduct(string productCode)   │
│        1. Guard: throw on null/empty/whitespace                 │
│        2. var normalized = productCode.Trim().ToUpperInvariant()│
│        3. Dedup guard against `normalized` (in-memory)          │
│        4. Add MarketingActionProduct with normalized prefix     │
├─────────────────────────────────────────────────────────────────┤
│ Anela.Heblo.Persistence (unchanged)                             │
│  └── Composite PK (MarketingActionId, ProductCodePrefix)        │
│      remains the safety net — never the primary defence.        │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Normalize once, compare and store the same value
**Options considered:**
- (A) Normalize input once at the top; use the normalized value for both the `Any()` guard and the new row's `ProductCodePrefix`.
- (B) Keep raw input around; change the comparison to `StringComparer.OrdinalIgnoreCase` but persist `Trim().ToUpperInvariant()`.
- (C) Push normalization down into a configured EF Core value converter / case-insensitive DB collation and remove the domain normalization entirely.

**Chosen approach:** (A).

**Rationale:** Single source of truth for "what is this prefix?". Option B leaves two normalization paths in the same method (read uses one rule, write uses another), which is the exact class of bug we are fixing. Option C is a larger surface change — schema/collation, value converter, migration risk — and is explicitly out of scope per the spec; it also moves invariant enforcement out of the entity, weakening the domain model. (A) is the smallest change that restores the entity as the source of truth.

#### Decision 2: Throw `ArgumentException` (do not silently no-op on empty input)
**Options considered:**
- (A) Throw `ArgumentException` with `paramName = "productCode"` (current spec).
- (B) Silently return on null/empty/whitespace.

**Chosen approach:** (A) — already implemented in the current file at lines 73–74.

**Rationale:** The existing entity already throws (the spec's FR-2 is partially fulfilled; only the guard-line bug is open). Both call sites pre-feed values through `.Distinct()` over a list that comes from a validated request DTO; an empty value reaching the entity is a programmer error, not a data condition, so an exception is the right contract. We keep this behaviour rather than weakening it.

#### Decision 3: Do not refactor `UpdateMarketingActionHandler.ProductAssociations.Clear()` / re-add pattern
**Options considered:**
- (A) Leave the Clear() + re-add loop as-is; the entity's own guard makes duplicates harmless either way.
- (B) Switch the update handler to a diff-based merge (compute add/remove sets, only mutate what changed).

**Chosen approach:** (A). Out of scope per the spec.

**Rationale:** Spec is surgical. Re-evaluating the Clear/re-add pattern (which interacts with EF Core change tracking and is currently functioning) is a separate concern and a separate PR. Flag for follow-up if perf or audit-trail concerns arise.

## Implementation Guidance

### Directory / Module Structure

**Files to modify:**
- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — change one line (line 76) from `pa.ProductCodePrefix == productCode` to `pa.ProductCodePrefix == normalized`; introduce the `normalized` local above the guard; move the `Trim().ToUpperInvariant()` call out of the `Add` initializer so the same value is used twice.

**Files to add (tests):**
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionAssociateWithProductTests.cs` — new xUnit test class adjacent to existing `MarketingActionSyncTests.cs`. Same conventions: xUnit + FluentAssertions, no mocks needed (pure domain), shared `CreateAction()` factory pattern.

**Files to leave untouched:**
- `MarketingActionProduct.cs`, `IMarketingActionRepository.cs`, EF configurations, migrations, `ImportFromOutlookHandler.cs`, `UpdateMarketingActionHandler.cs`, `CreateMarketingActionHandler.cs`, all DTOs, all controllers, all frontend code.

### Interfaces and Contracts

No interface changes. Method signature stays:

```csharp
public void AssociateWithProduct(string productCode)
```

Contract pre/post:
- **Pre:** `productCode` is non-null, non-empty, non-whitespace. Mixed casing and surrounding whitespace are tolerated.
- **Post (no-op):** If `productCode.Trim().ToUpperInvariant()` already exists in `ProductAssociations.ProductCodePrefix`, return without mutation.
- **Post (add):** A new `MarketingActionProduct` is appended with `ProductCodePrefix == productCode.Trim().ToUpperInvariant()`, `MarketingActionId == this.Id`, `CreatedAt == DateTime.UtcNow`.
- **Throws:** `ArgumentException(paramName: "productCode")` on null/empty/whitespace.

### Data Flow

**Create / Update path (unchanged at the handler, fixed at the entity):**

1. Controller receives `Create/UpdateMarketingActionRequest` with `List<string>? AssociatedProducts` (may contain mixed casing).
2. Handler iterates `request.AssociatedProducts.Distinct()` — note this is a case-sensitive distinct, so `["abc", "ABC"]` survives as two entries.
3. For each, handler calls `action.AssociateWithProduct(code)`.
4. **(Fix lands here)** Entity normalizes, dedups against normalized value, only the first wins.
5. `_repository.SaveChangesAsync()` writes the single row; the composite-PK safety net never fires for in-batch duplicates.

**Update path edge case:** `action.ProductAssociations.Clear()` is called before re-adding, so within a single Update call the dedup is effectively against an empty collection at first and accumulates as the loop runs. The fix correctly handles in-loop duplicates from the *same* request.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec names `ImportFromOutlookHandler` as a caller; it isn't one today. Reviewer/test author could waste time on it. | Low | Amend spec (see below). Tests target the entity directly — they don't depend on the handler claim. |
| `Distinct()` in handlers stays case-sensitive after the fix. If a future caller bypasses the entity method, the duplicate bug reappears at the persistence layer. | Low | Long-term: consider switching handler-side `Distinct()` to `Distinct(StringComparer.OrdinalIgnoreCase)` for efficiency (avoids redundant calls into the entity). Out of scope for this PR — record as follow-up. |
| Behavioural change: callers that previously got `DbUpdateException` on duplicate now get a silent no-op. If any caller (or test) currently relies on the exception, it breaks silently. | Low | Grep confirms no production code depends on the duplicate-throws behaviour. Tests assert the new (intended) behaviour explicitly. |
| `ToUpperInvariant()` uses invariant culture; a future Turkish-locale deployment could produce surprising results — but only if normalization moves to a culture-sensitive variant. | Very Low | Existing code already uses `ToUpperInvariant`. Keep this exact call. Do not switch to `ToUpper()`. |
| EF Core change-tracking with `ProductAssociations.Clear()` in the update path is not affected, but if the dedup guard were moved out of the entity later, lazy-loading state could re-introduce the bug. | Very Low | Keep the guard in the aggregate. Document in code only if non-obvious — current spec is sufficient. |
| Inconsistent normalization with sibling method `LinkToFolder` (which only trims, no case fold). Could mislead a reader into assuming both behave the same. | Low | Out of scope — the spec is explicit. File a follow-up if folder-key duplication ever surfaces. |

## Specification Amendments

1. **Background paragraph 1 is inaccurate.** Replace "Two handlers call it: `UpdateMarketingActionHandler` … and `ImportFromOutlookHandler`" with: "Three handlers depend on the method indirectly through their entity mutations, but only `CreateMarketingActionHandler` (`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs:64`) and `UpdateMarketingActionHandler` (`...UpdateMarketingActionHandler.cs:99`) call it directly. `ImportFromOutlookHandler` does not parse product codes today and is not a caller."

2. **FR-2 is partially already implemented.** Lines 73–74 of `MarketingAction.cs` already throw `ArgumentException` with `paramName = "productCode"`. The remaining real change is line 76 — the comparison against raw `productCode` instead of `normalized`. The spec's "reference implementation" block is correct; just note that the empty-check is not new behaviour.

3. **Add explicit test for in-loop duplicate within a single handler call.** Mixed-case duplicates can arrive within one `request.AssociatedProducts` list (handler's `Distinct()` is case-sensitive). Add an acceptance test: calling `AssociateWithProduct("abc")` and `AssociateWithProduct("ABC")` on the same entity instance, in sequence, leaves exactly one row. This is the realistic production scenario, not just successive calls across requests. (The spec already mentions this in FR-1's last bullet — good — but emphasise it represents the actual handler-loop scenario.)

4. **Recommended (non-blocking) follow-up to record in spec's "Out of scope" or as a linked future issue:** Switch handler-side `request.AssociatedProducts.Distinct()` to `Distinct(StringComparer.OrdinalIgnoreCase)` to avoid redundant calls into the entity. Pure perf/clarity; correctness is fully handled by the domain fix.

## Prerequisites

None. The change is self-contained:
- No DB migration (existing rows already uppercase by construction of the buggy-but-write-correct code).
- No new configuration or feature flag.
- No new package dependency.
- No infrastructure change.
- Validation gates per `CLAUDE.md`: `dotnet build`, `dotnet format`, run `backend/test/Anela.Heblo.Tests/Domain/Marketing/*` and `backend/test/Anela.Heblo.Tests/Application/Marketing/*` tests touched by the change. No E2E impact (API contract unchanged).