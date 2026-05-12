# Architecture Review: Remove unimplemented `FamilyEntries` from `JournalIndicator`

## Skip Design: true

No UI/UX work — backend-only YAGNI cleanup. No new screens, no visual components, no design tokens touched. The TypeScript client regenerates mechanically from the OpenAPI schema; there is no human-facing design surface here.

## Architectural Fit Assessment

This is a **negative-delta, contract-shrinking** change. It removes one property from a domain entity and its mirrored DTO, and tightens `TotalEntries` from a misleading sum to a single-field passthrough. There is no new architecture: the change reduces surface area inside an existing, otherwise untouched repository slice (`Anela.Heblo.Domain/Features/Journal` ↔ `Anela.Heblo.Application/Features/Journal` ↔ `Anela.Heblo.Persistence/Catalog/Journal`).

Two facts make the fit clean:

1. **No live consumer.** Grep for `FamilyEntries|familyEntries` finds only the two declaration files and `brief.md`. Grep for `GetJournalIndicatorsAsync|JournalIndicator|JournalIndicatorDto` finds only the declaration trio plus the repository and its interface. `JournalIndicatorDto` is imported in `JournalRepositoryIntegrationTests.cs:1` but never used in any test body — it is dead `using`. There is no MediatR handler, no controller, no hook.
2. **The Vertical Slice boundary stays intact.** Domain entity, contract DTO, and repository implementation are all inside the Journal slice. The change does not cross slice boundaries, does not touch MediatR pipeline, and does not affect any shared `Xcc` infrastructure.

The DTO/Domain duplication (`JournalIndicatorDto` mirrors `JournalIndicator` shape) is itself a smell — there is no mapper from one to the other — but per `docs/architecture/development_guidelines.md` DTOs live in `Contracts/` of the slice and the spec correctly scopes the wider dead-code question out. **Endorsed**.

The chosen approach (path 2: Remove) is the right call. Path 1 (implement prefix-based family count) would solidify a contract no one has asked for, and the prefix-matching semantics already used by `GetEntriesByProductAsync` at `JournalRepository.cs:169` make a future reintroduction mechanically cheap when a real consumer arrives.

## Proposed Architecture

### Component Overview

```
Domain (Anela.Heblo.Domain/Features/Journal)
└── JournalIndicator.cs                       ← edit: drop FamilyEntries, TotalEntries => DirectEntries
└── IJournalRepository.cs                     ← unchanged
└── JournalEntry.cs / JournalEntryProduct.cs  ← unchanged

Application (Anela.Heblo.Application/Features/Journal)
└── Contracts/JournalIndicatorDto.cs          ← edit: drop FamilyEntries, TotalEntries => DirectEntries

Persistence (Anela.Heblo.Persistence/Catalog/Journal)
└── JournalRepository.cs                      ← edit: collapse blank line at 209–210; no semantic change

Tests (Anela.Heblo.Tests/Features/Journal)
└── JournalRepositoryIntegrationTests.cs      ← add 3 facts for GetJournalIndicatorsAsync
                                                  + remove dead `using ...Contracts;` if unused

API client (auto)
└── frontend/src/api/generated/*              ← regenerated on `npm run build`; FamilyEntries disappears
```

No new components. No DI registrations. No migrations. No new files in `Contracts/`, `UseCases/`, or `Application/Features/Journal/Mappers`.

### Key Design Decisions

#### Decision 1: Remove rather than implement

**Options considered:**
- (A) Implement `FamilyEntries` via a prefix-matching query mirroring `GetEntriesByProductAsync`.
- (B) Remove `FamilyEntries`; redefine `TotalEntries => DirectEntries`.
- (C) Remove `JournalIndicator`, `JournalIndicatorDto`, and `GetJournalIndicatorsAsync` entirely.

**Chosen approach:** B.

**Rationale:** A solidifies an unused contract — exactly the YAGNI failure mode the brief calls out. C exceeds the brief's scope (which targets the `FamilyEntries`/`TotalEntries` semantics) and risks breaking out-of-tree consumers we cannot enumerate. B is minimal, mechanical, and reversible: when a real consumer needs a family count, both the field and the prefix query are trivial to reintroduce because the underlying pattern (`JournalEntryProduct.ProductCodePrefix` + `StartsWith` matching) is already in place at `JournalRepository.cs:169`.

#### Decision 2: Keep `TotalEntries` rather than delete it

**Options considered:**
- (A) Replace `TotalEntries` with raw `DirectEntries` everywhere (zero new properties).
- (B) Keep `TotalEntries` as a property defined as `=> DirectEntries`.

**Chosen approach:** B (matches the brief's option 2 and the spec's FR-1/FR-2 acceptance criteria).

**Rationale:** Keeping `TotalEntries` preserves the small amount of forward-compatibility headroom — a future "family" reintroduction can change the expression to `DirectEntries + FamilyEntries` without renaming a public property and without breaking any OpenAPI consumer that hasn't yet appeared. The cost is one trivial computed property. The alternative (delete it) is fine in isolation but offers no payoff over B.

#### Decision 3: Tests target the public repository API, not the DTO

**Chosen approach:** New tests exercise `JournalRepository.GetJournalIndicatorsAsync` and assert on the domain `JournalIndicator`, not on `JournalIndicatorDto`. The DTO is structurally identical and has no mapper; testing the DTO would test nothing extra.

**Rationale:** The DTO has no consumer and no mapping logic. The repository is where regressions can hide (the `DirectEntries` grouped query, `LastEntryDate`, `HasRecentEntries` 30-day boundary). Test what can break.

## Implementation Guidance

### Directory / Module Structure

No new directories or files. Edits are localized to four existing files (three production + one test).

| Path | Action |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Journal/JournalIndicator.cs` | Delete `FamilyEntries`. Change `TotalEntries` to `public int TotalEntries => DirectEntries;`. |
| `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalIndicatorDto.cs` | Same edit, mirroring the domain entity. Remains a `class` (not a `record`) per project rule. |
| `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs` | Remove the orphan blank line between lines 208 and 211. **No other changes**: keep the grouped `directAssociations` query, `LastEntryDate` assignment, and `HasRecentEntries` loop verbatim. |
| `backend/test/Anela.Heblo.Tests/Features/Journal/JournalRepositoryIntegrationTests.cs` | Add three `[Fact]` methods for `GetJournalIndicatorsAsync` (see Data Flow). Inspect and **delete** `using Anela.Heblo.Application.Features.Journal.Contracts;` at line 1 only if no remaining test in the file references it after the edit (today it is already dead). |

### Interfaces and Contracts

- **`IJournalRepository.GetJournalIndicatorsAsync` signature: unchanged.** Returning `Dictionary<string, JournalIndicator>` keyed on product code stays as-is.
- **`JournalIndicator` (domain) — final shape:**

  ```csharp
  public class JournalIndicator
  {
      public string ProductCode { get; set; } = null!;
      public int DirectEntries { get; set; }
      public int TotalEntries => DirectEntries;
      public DateTime? LastEntryDate { get; set; }
      public bool HasRecentEntries { get; set; } // Within last 30 days
  }
  ```

- **`JournalIndicatorDto` (contract) — final shape: identical to above, as a `class`.**

- **OpenAPI / TypeScript client:** the `familyEntries` field disappears from the generated schema. `totalEntries` remains. The TS client regenerates on `npm run build`. No manual changes to `frontend/src/api/generated/*` — they are derived artifacts.

### Data Flow

For the happy path of `GetJournalIndicatorsAsync(["TON002", "CREAM001"])`:

```
caller passes IEnumerable<string> productCodes
  → repository materializes productCodeList
  → initializes result[code] = new JournalIndicator { ProductCode = code } for each code
  → EF Core query joins JournalEntryProduct (where ProductCodePrefix ∈ codes) with non-deleted JournalEntry
    → groups by ProductCode → returns { ProductCode, Count, LastEntryDate }
  → loop assigns DirectEntries and LastEntryDate from grouped result
  → loop computes HasRecentEntries := LastEntryDate >= today - 30 days
  → returns Dictionary<string, JournalIndicator>
```

This flow is unchanged. `TotalEntries` is a computed read-only property and never appears in the query or the assignment loop.

**Test data flow (new FR-4 tests):**

| Test | Arrange | Assert |
|------|---------|--------|
| `GetJournalIndicatorsAsync_WithMultipleDirectEntries_ReturnsCorrectCount` | Insert 3 entries each associating with `"TON002"` (exact `ProductCodePrefix == "TON002"`). | `result["TON002"].DirectEntries == 3`, `TotalEntries == 3`, `LastEntryDate == latest`, `HasRecentEntries == true`. |
| `GetJournalIndicatorsAsync_WithNoEntries_ReturnsZero` | Pass an unused product code; insert no entries for it. | `DirectEntries == 0`, `TotalEntries == 0`, `LastEntryDate == null`, `HasRecentEntries == false`. |
| `GetJournalIndicatorsAsync_WithRecentEntry_FlagsHasRecentEntries` | Insert one entry with `EntryDate = DateTime.Today.AddDays(-5)` for `"CREAM001"`. | `HasRecentEntries == true`, `LastEntryDate` close to today. |

Use the same `UseInMemoryDatabase($"JournalTestDb_{Guid.NewGuid()}")` pattern already in the file (`JournalRepositoryIntegrationTests.cs:21`) and `entry.AssociateWithProduct(prefix)` to wire the association. FluentAssertions + AAA.

> **Note on 30-day boundary behavior:** the production code uses `DateTime.Today.AddDays(-30)` (`JournalRepository.cs:212`). Do not test the exact 30-day boundary — it is wall-clock-coupled and would flake. Use `-5` for "recent" and rely on the "no entries" test to cover the `false` branch. If the future spec calls for boundary coverage, refactor to an injected `IClock` first; **out of scope here**.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| An undiscovered consumer (e.g. a frontend hook authored on a branch, a yet-uncommitted local change) reads `familyEntries`. | Low | Codebase grep confirms zero consumers on `main`. `npm run build` regenerates the TS client; any consumer in the worktree will fail TypeScript compilation immediately. Verify both `dotnet build` and `npm run build` succeed before completion. |
| Removing the orphan blank line at `JournalRepository.cs:209–210` accidentally drags in unrelated reformatting from `dotnet format`. | Low | Run `dotnet format` once after the edit and inspect `git diff --stat`; revert any non-Journal hunks. The spec is explicit about no semantic changes to `DirectEntries`/`LastEntryDate`/`HasRecentEntries`. |
| The new tests bind to wall-clock time and become flaky around midnight or in slow CI. | Low–Medium | Use `DateTime.Today.AddDays(-5)` for "recent" and `null`/no-entry for the negative case. Do **not** assert exact `LastEntryDate` equality; assert with a small tolerance (`BeCloseTo`) or compare to the value used in arrange. Do not test the 30-day boundary directly. |
| EF Core `UseInMemoryDatabase` semantics differ from the production provider for `StartsWith`/grouping. | Low (for this change) | The current query uses exact `Contains` on prefixes, not `StartsWith`. In-memory faithfully supports this. The existing prefix-matching tests in the same file (`JournalRepositoryIntegrationTests.cs:115`) already use in-memory; the new tests reuse the same harness. |
| Stale `using Anela.Heblo.Application.Features.Journal.Contracts;` at top of test file becomes a build warning under stricter analyzers later. | Low | Drop the unused `using` as part of this change since the file is being edited anyway. Surgical: only that one line. |
| Future reintroduction of `FamilyEntries` requires the property name to stay free of collisions with `TotalEntries`. | Negligible | Keeping `TotalEntries` as a named property today ensures the future expression change is a one-line edit. |

## Specification Amendments

The spec is solid. Two small clarifications worth adding to **spec.r1.md** before implementation:

1. **FR-4 — clarify the time-based test.** The current acceptance criterion says "a product code whose latest direct entry is within 30 days (asserts `HasRecentEntries == true`)". Append: *Use a relative offset of −5 days from `DateTime.Today`. Do not assert exact `LastEntryDate` equality; use `BeCloseTo` with a 1-second tolerance or compare to the arrange value. Do not test the 30-day boundary directly — that requires injecting an `IClock` abstraction which is out of scope.*

2. **FR-2 — explicitly call out the unused `using` in the test file.** The DTO is currently imported in `JournalRepositoryIntegrationTests.cs:1` but never used. Since FR-2 removes the only thing in that namespace that could plausibly justify the `using`, add: *Remove the `using Anela.Heblo.Application.Features.Journal.Contracts;` line from `JournalRepositoryIntegrationTests.cs` if and only if no test added under FR-4 references the DTO (none should — tests target the domain `JournalIndicator`).*

3. **Out of Scope — make the recommendation explicit.** The "Removing the unused `GetJournalIndicatorsAsync` method or `JournalIndicatorDto` entirely" bullet is good; recommend adding: *A follow-up arch-review item should be filed to evaluate full removal of `JournalIndicator`, `JournalIndicatorDto`, and `GetJournalIndicatorsAsync` once this YAGNI cleanup lands, since the broader dead-code question stands.*

No other amendments. Scope, acceptance criteria, and NFRs are appropriately tight.

## Prerequisites

None blocking. To be safe, verify before starting:

- Working tree is clean and on the feature branch (`feat-arch-review-journal-familyentries-on-jou`) — already true per session context.
- `dotnet build` and `npm run build` succeed against `main`-merged state so the pre-change baseline is green.
- The OpenAPI generation pipeline runs as part of `npm run build` (per `docs/development/api-client-generation.md`); no manual step is required to refresh the TypeScript client.
- No migrations, no config changes, no secrets, no infrastructure changes, no Docker rebuilds.

Validation gates (from project CLAUDE.md) the implementation must clear before marking complete:
- `dotnet build` + `dotnet format` clean
- `npm run build` + `npm run lint` clean
- `dotnet test` — all touched tests plus the three new tests pass
- E2E suite is **not required** for this change (no UI, no API endpoint added; runs nightly anyway)