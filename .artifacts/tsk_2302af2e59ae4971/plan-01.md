# Plan: Split `IPhotobankRepository` into per-entity-family interfaces

## Summary

`IPhotobankRepository` (`backend/src/Anela.Heblo.Domain/Features/Photobank/IPhotobankRepository.cs`) is a
41-method god-interface spanning six unrelated entity families (Photos, Tags, Photo–Tag joins, Index Roots,
Tag Rules, Auto-tagging/rule-reapply). Every one of its 21 consumers (16 MediatR handlers + 2 background
jobs + the DI module + the single `PhotobankRepository` implementation) depends on the full surface even
though each consumer only calls 1–5 methods from 1–4 families. This is a pure interface-segregation
refactor: split the declaration into six narrow interfaces, have the existing `PhotobankRepository` class
implement all of them, and repoint each consumer at only the interface(s) it actually calls. No persistence
logic, handler logic, or DTO/API contract changes.

## Context

Filed by the daily arch-review routine (2026-07-18) as an ISP violation. The interface is a de facto
merge-conflict hotspot: any feature touching one entity family (e.g. adding an auto-tagging method) forces
every unrelated test double and handler dependency list to be recompiled/reviewed. `GetTagsHandler` only
needs `GetTagsWithCountsAsync`; `GetPhotosHandler` only needs `GetPhotosAsync`; both currently carry a
33+ method dependency surface. Splitting the interface makes each handler's true data dependency visible
at the constructor, and shrinks the Moq/test-double surface for single-concern unit tests.

I inspected every consumer (`grep` over `Features/Photobank/**`) to build the method → interface mapping
below — this refines the finding's suggested grouping in two places where the original table's family
labels didn't match actual call sites (see FR-1 notes on `GetPhotosByIdsAsync` and
`RemovePhotoTagsBySourceAsync`), and covers one method (`GetPhotoRuleCandidatesPageAsync`, used only by
`ReapplyRulesHandler`) that the finding's table omitted entirely.

## Functional requirements

**FR-1 — Split `IPhotobankRepository` into six narrower interfaces**, all in
`backend/src/Anela.Heblo.Domain/Features/Photobank/`, one file per interface (matching the existing
one-type-per-file convention in that folder):

| New interface | Methods | Consumers |
|---|---|---|
| `IPhotobankPhotoRepository` | `GetPhotosAsync`, `CountFilteredPhotosAsync`, `GetFilteredPhotoIdsMissingTagAsync`, `GetExistingPhotoIdsMissingTagAsync`, `CountExistingPhotosAsync`, `GetPhotoByIdAsync`, `GetLocatorAsync`, `GetPhotoBySharePointFileIdAsync`, `AddPhotoAsync`, `RemovePhotoAsync`, `GetPhotosByIdsAsync` | GetPhotosHandler, GetThumbnailHandler, AddPhotoTagHandler, RemovePhotoTagHandler, BulkAddPhotoTagHandler, BulkAddPhotoTagByIdsHandler, RetagPhotosHandler, PhotobankIndexJob |
| `IPhotobankTagRepository` | `GetTagsWithCountsAsync`, `GetOrCreateTagAsync`, `GetOrCreateTagsAsync`, `GetTagByIdAsync`, `GetTagByNameAsync`, `DeleteTagAsync` | GetTagsHandler, CreateTagHandler, DeleteTagHandler, AddPhotoTagHandler, BulkAddPhotoTagHandler, BulkAddPhotoTagByIdsHandler, ReapplyRulesHandler, PhotobankIndexJob, PhotobankAutoTagJob |
| `IPhotobankPhotoTagRepository` | `AddPhotoTagAsync`, `AddPhotoTagsAsync`, `RemovePhotoTagAsync`, `PhotoTagExistsAsync`, `RemoveRuleTagsAsync`, `GetOccupiedTagPairsAsync`, `GetPhotoTagsByPhotoAndSourceAsync`, `RemovePhotoTagsAsync`, `RemovePhotoTagsBySourceAsync` | AddPhotoTagHandler, RemovePhotoTagHandler, BulkAddPhotoTagHandler, BulkAddPhotoTagByIdsHandler, ReapplyRulesHandler, RetagPhotosHandler, PhotobankIndexJob, PhotobankAutoTagJob |
| `IPhotobankRootRepository` | `GetRootsAsync`, `AddRootAsync`, `DeleteRootAsync`, `GetActiveRootsWithDriveAsync` | GetRootsHandler, AddRootHandler, DeleteRootHandler, PhotobankIndexJob |
| `IPhotobankTagRuleRepository` | `GetRulesAsync`, `AddRuleAsync`, `GetRuleByIdAsync`, `UpdateRuleAsync`, `DeleteRuleAsync`, `GetActiveTagRulesAsync` | GetRulesHandler, AddRuleHandler, UpdateRuleHandler, DeleteRuleHandler, ReapplyRulesHandler, PhotobankIndexJob |
| `IPhotobankAutoTagRepository` | `GetPhotosPendingAutoTagAsync`, `StampAutoTaggedAtAsync`, `ResetAutoTaggedAtAsync`, `GetPhotoRuleCandidatesPageAsync` | PhotobankAutoTagJob, RetagPhotosHandler, ReapplyRulesHandler |

Two deliberate deviations from the finding's suggested table (call these out to the design step for
sign-off, not silent):
- `GetPhotosByIdsAsync` moves from "Auto-tagging" to `IPhotobankPhotoRepository` — it's a plain photo
  lookup by id (used by `RetagPhotosHandler` before it touches auto-tag state), not an auto-tag concern.
- `RemovePhotoTagsBySourceAsync` moves from "Auto-tagging" to `IPhotobankPhotoTagRepository` — it deletes
  photo–tag rows by source, same shape as the other `RemovePhotoTag*` methods already grouped there.
- `GetPhotoRuleCandidatesPageAsync` (used only by `ReapplyRulesHandler`, absent from the finding's table)
  is placed in `IPhotobankAutoTagRepository` alongside `GetPhotosPendingAutoTagAsync` since both return
  `PhotoAutoTagCandidate` and are the two paged-candidate-scan methods in the file; open to renaming that
  interface (e.g. `IPhotobankCandidateScanRepository`) if the design step prefers not to conflate
  auto-tagging with rule-reapply scanning.

Also move the `PhotoLocator` record (currently declared in `IPhotobankRepository.cs`) into
`IPhotobankPhotoRepository.cs`, since it's a Photo-family DTO with no other logical home.

Acceptance: `IPhotobankRepository.cs` no longer exists; the six new interface files compile; every method
from the original interface appears in exactly one new interface (no drops, no duplication of business
methods).

**FR-2 — `SaveChangesAsync` becomes part of every one of the six interfaces**, not a separate seventh
interface. Every family has at least one write method, so every consumer that writes already needs to
persist via *some* interface it holds; duplicating the identical signature across all six avoids forcing
read-only handlers (`GetTagsHandler`, `GetPhotosHandler`, `GetRootsHandler`, `GetRulesHandler`,
`GetThumbnailHandler`) to depend on a persistence method they never call.
Acceptance: `GetTagsHandler` et al. (pure-read handlers) have zero `SaveChangesAsync` in their injected
interface; every write handler can call `SaveChangesAsync` off whichever interface(s) it already holds.

**FR-3 — `PhotobankRepository` (Persistence layer,
`backend/src/Anela.Heblo.Persistence/Photobank/PhotobankRepository.cs`) implements all six new interfaces**,
replacing `: IPhotobankRepository`. No method bodies change — this is a signature/declaration change only
(add `,` and the five new interface names to the class declaration; the existing single `SaveChangesAsync`
method body at line 442 already satisfies all six interface copies of that method).
Acceptance: `PhotobankRepository` compiles against all six interfaces with zero method-body edits.

**FR-4 — Every consumer is repointed to inject only the interface(s) it uses**, per the table in FR-1.
Multi-family consumers (`ReapplyRulesHandler`, `PhotobankIndexJob`, `PhotobankAutoTagJob`,
`RetagPhotosHandler`) inject 2–4 narrow interfaces instead of the one god-interface — this is expected and
correct, not a smell; they'd otherwise need those methods regardless.
Acceptance: no file under `backend/src/Anela.Heblo.Application/Features/Photobank/` references
`IPhotobankRepository` after the change; `dotnet build` succeeds.

**FR-5 — DI registration wires all six interfaces to a single shared `PhotobankRepository` instance per
scope.** This is the one correctness-critical part of an otherwise mechanical refactor: if
`PhotobankModule.AddPhotobankModule` naively does
`services.AddScoped<IPhotobankPhotoRepository, PhotobankRepository>(); services.AddScoped<IPhotobankTagRepository, PhotobankRepository>(); ...`
for each interface, DI creates **six independent instances per request scope** — a multi-family consumer
like `PhotobankIndexJob` would get a different `PhotobankRepository` (and likely a different tracked
`DbContext` state) per injected interface, breaking the existing single-`SaveChangesAsync`-commits-everything
transaction semantics.
Correct pattern: register the concrete class once, then forward each interface to it:
```csharp
services.AddScoped<PhotobankRepository>();
services.AddScoped<IPhotobankPhotoRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankTagRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankPhotoTagRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankRootRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankTagRuleRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
services.AddScoped<IPhotobankAutoTagRepository>(sp => sp.GetRequiredService<PhotobankRepository>());
```
Acceptance: a test/manual check confirms that within one scope, resolving two different narrow interfaces
and calling a write method on one followed by `SaveChangesAsync` on the other persists the write (proves
they share one `DbContext`-backed instance). The existing Photobank integration/unit test suite passing is
the practical proxy for this.

**FR-6 — Update all test doubles.** ~13 test files under
`backend/test/Anela.Heblo.Tests/Features/Photobank/` currently declare `Mock<IPhotobankRepository>` (Moq).
Each becomes `Mock<T>` for whichever narrow interface(s) the handler under test now injects (multi-family
handler tests — `ReapplyRulesHandlerTests`, `PhotobankIndexJobTests`, `PhotobankAutoTagJobTests`,
`RetagPhotosHandlerTests` — get multiple `Mock<T>` fields). Mechanical rename, no behavioral test changes.
Acceptance: `dotnet test` on the Photobank test folder is green with no skipped/modified assertions.

## Non-functional requirements

- **Zero behavior change.** No handler logic, persistence query, or API/DTO contract changes. This is a
  compile-time dependency-surface change only.
- **No migration.** No database schema involved.
- **Build/format compliance.** `dotnet build` and `dotnet format` must pass per repo validation rules.

## Data model

No entity or schema changes. Only the C# interface declarations describing access to existing entities
(`Photo`, `Tag`, `PhotoTag`, `PhotobankIndexRoot`, `TagRule`, `PhotoAutoTagCandidate`, `PhotoLocator`) are
reorganized.

## Interfaces

- Six new C# interfaces replacing one, all in `Domain/Features/Photobank/` (see FR-1 table).
- `PhotobankModule.AddPhotobankModule` DI registration changes (FR-5).
- No HTTP/API surface, no frontend changes — this is entirely internal to the backend Domain/Application/
  Persistence layers.

## Dependencies and scope

**In scope:**
- `IPhotobankRepository.cs` → deleted, replaced by 6 new interface files (+ `PhotoLocator` relocated).
- `PhotobankRepository.cs` (Persistence) — interface list on class declaration only.
- `PhotobankModule.cs` — DI registration (the one part requiring careful, not just mechanical, attention).
- 16 UseCase handlers + 2 background jobs (`PhotobankIndexJob`, `PhotobankAutoTagJob`) — constructor/field
  type changes only.
- ~13 test files — `Mock<T>` type changes only.

**Out of scope:**
- Any change to query logic, SQL, EF configuration, or persistence behavior.
- Any change to MediatR request/response DTOs or the public API surface.
- Renaming/restructuring the `PhotobankRepository` class itself (e.g. splitting it into six persistence
  classes) — the finding explicitly says the single class can implement all six interfaces with no
  refactor of persistence logic, and there's no indication multiple physical repository classes are wanted.
- Re-litigating the family boundaries beyond the two documented deviations in FR-1 — if the design step
  disagrees with a placement, it's a one-line move between interface files, not a scope change.

## Rough plan

1. Create the 6 new interface files in `Domain/Features/Photobank/`, moving methods (and `PhotoLocator`)
   per the FR-1 table; delete `IPhotobankRepository.cs`.
2. Update `PhotobankRepository.cs` (Persistence) class declaration to implement all 6 interfaces.
3. Update `PhotobankModule.cs` DI registration per FR-5 (shared-instance forwarding pattern).
4. Update each of the 21 consumers (16 handlers + 2 jobs, per FR-1/FR-4) to inject only the interfaces
   they use; remove the now-unused `using Anela.Heblo.Domain.Features.Photobank;` only if nothing else from
   that namespace is referenced (most files also reference `Photo`, `PhotoTag`, etc. from the same
   namespace, so the using likely stays).
5. Update the ~13 affected test files' `Mock<IPhotobankRepository>` fields to the corresponding narrow
   interface mock(s).
6. `dotnet build` (solution-wide, to catch any missed consumer) + `dotnet format`.
7. Run the full Photobank test suite (`backend/test/Anela.Heblo.Tests/Features/Photobank/**`) — must be
   green with no assertion changes beyond mock type renames.
8. Spot-check a multi-family consumer (e.g. `PhotobankIndexJobTests`) to confirm the DI-sharing concern in
   FR-5 doesn't manifest as a real bug in tests (tests construct handlers directly with mocks, so this is
   mainly a manual/runtime sanity check — see FR-5 acceptance).

## Open questions

- **`GetPhotoRuleCandidatesPageAsync` interface placement** (`IPhotobankAutoTagRepository` vs. a
  differently-named interface) — flagged in FR-1, default chosen, but it's a naming call for the design
  step, not a blocking ambiguity.
- **Whether to keep the interface split at exactly six** or fold the smallest ones together (e.g.
  `IPhotobankRootRepository` has only 4 methods and 3 single-family consumers) — the finding explicitly
  requests six, and I see no consumer complexity that argues for merging any of them, so default is to
  follow the finding as refined above.
- **FR-5's DI pattern is the one part of this "pure declaration change" that is not purely mechanical** —
  it must be implemented correctly on the first pass since a wrong registration (separate instances per
  interface) would silently produce data-consistency bugs (a `SaveChangesAsync` call on one interface not
  persisting writes made via another) that unit tests using mocks won't catch. Flagging this explicitly so
  the design/development steps don't treat FR-5 as equally trivial to the rest of the refactor.
