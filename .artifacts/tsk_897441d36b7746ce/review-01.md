# Review: Fix residual `PhotobankRepository.SaveChangesAsync` DateTime Kind=Unspecified exception

## Verdict: done

## What I checked

Read plan-01.md, design-01.md, architecture-01.md, development-01.md, then independently verified every
claim against the actual working-tree diff (`5e5032c8`) rather than trusting the development summary:

1. **`PhotobankIndexRootConfiguration.cs`** — confirmed `builder.Property(x => x.LastIndexedAt)` now has
   `.AsUtcTimestamp()` appended, matching `CreatedAt`'s existing (correct) mapping on the line above. Matches
   FR-1 exactly.

2. **Migration `20260724120000_AlignPhotobankIndexRootTimestampWithoutTimeZone.cs`** — diffed against
   PR #3330's `20260624115315_AlignPhotoTimestampsWithoutTimeZone.cs`. Structurally identical
   (`ALTER COLUMN ... TYPE timestamp USING col AT TIME ZONE 'UTC'` in `Up()`, reverse cast in `Down()`),
   only table/column names differ (`PhotobankIndexRoots.LastIndexedAt` vs `Photos.TakenAt`/
   `LastAutoTaggedAt`). Correctly UTC-preserving and NULL-safe, same as the already-production-validated
   precedent.

3. **Designer.cs** — hand-copied from the true most-recent migration
   (`20260715165951_ReformatManufactureOrderLotToWwYy.Designer.cs`, confirmed via directory listing sorted
   by migration timestamp — no later migration exists). Ran `diff` myself: exactly two hunks differ — the
   `[Migration(...)]`/`partial class` name, and `PhotobankIndexRoot.LastIndexedAt`'s
   `HasColumnType("timestamp with time zone")` → `HasColumnType("timestamp")`. No stray drift.

4. **`ApplicationDbContextModelSnapshot.cs`** — `git diff` shows exactly one hunk, the same column-type
   change, consistent with the migration and Designer.cs.

5. **`PhotoSchemaTests.cs`** — new `[Theory]` covering `PhotobankIndexRoot.CreatedAt` (control, already
   correct) and `LastIndexedAt` (regression guard). Confirmed `PhotobankIndexRoot` is already in scope via
   the file's existing `using Anela.Heblo.Domain.Features.Photobank;`, so it compiles without a new import.
   Test methodology mirrors the existing, presumably-passing `Photo_DateTimeColumns_...` theory exactly.

6. **Completeness of the audit** — grepped `DateTime`/`DateTime?` across
   `backend/src/Anela.Heblo.Domain/Features/Photobank/*.cs` myself. Only two properties exist on
   `PhotobankIndexRoot` (`CreatedAt`, `LastIndexedAt`) and both are now covered by the config fix + test.
   No other entity in the module has an unconfigured `DateTime` column. The plan's root-cause claim holds.

7. **Scope discipline** — `git diff --stat` confirms only the config file, new migration + Designer, model
   snapshot, and test file changed. `PhotobankIndexJob.cs`, `PhotobankRepository.cs`, and all
   DTO/controller surfaces are untouched, as the plan required.

## Assessment against spec/architecture

- Meets FR-1 (config fix), FR-2 (mirrored migration), FR-3 (test extension) from plan-01.md.
- Follows the architecture's mandated pattern exactly — no deviation, no new abstractions, no scope creep
  into the (explicitly rejected) global-convention refactor.
- No functional requirement is unmet, no architecture conflict, no missing required test, no logic bug
  found on inspection.

## Caveat (non-blocking)

`dotnet build`/`dotnet format`/`dotnet test` could not be run in this sandbox (no .NET SDK available), so
compiler-level verification is missing. This is a process gap, not a defect in the change — the change is
small, mechanical, and closely mirrors an already-merged, production-validated precedent (PR #3330), and
the Designer.cs was verified by structural diff rather than regenerated. This should be confirmed by CI
(`dotnet build` + full test run) before merge, and the manual migration must be applied to staging/
production after merge per project rules — both already called out in development-01.md's PR-description
guidance. Not a reason to request changes; it's an execution-environment limitation, not an implementation
flaw.

## Outcome

```json
{"outcome": "done", "summary": "Implementation matches plan/design/architecture exactly: LastIndexedAt now mapped via AsUtcTimestamp(), migration mirrors PR #3330's proven pattern with correct Up/Down SQL, Designer.cs and model snapshot diffs are minimal and correct (verified by direct diff), and the new schema test closes the regression gap. No functional requirement missed, no architecture conflict, no correctness bug found. Only gap is that dotnet build/test couldn't run in this sandbox (no SDK) — flag for CI verification before merge, not a reason to block."}
```
