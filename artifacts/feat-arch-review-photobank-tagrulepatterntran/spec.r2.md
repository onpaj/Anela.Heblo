# Specification: Remove Dead `TagRulePatternTranslator` and Sync Photobank Tag Rules Documentation

## Summary
The `TagRulePatternTranslator` class in the Photobank Domain layer is dead code: it has no production callers, only test coverage. This spec removes the translator and its tests, and updates the Photobank feature documentation so that the Tag Rules pattern syntax described matches what the code actually accepts (raw regex).

## Background
During a daily architecture review (2026-05-27), `TagRulePatternTranslator` (`backend/src/Anela.Heblo.Domain/Features/Photobank/TagRulePatternTranslator.cs`) was identified as unreferenced from production code. A repo-wide search confirmed its only usages live in `TagRulePatternTranslatorTests.cs`.

The translator was designed to convert glob-style patterns (e.g. `/PROFI_FOCENI/Produkty/*`) into regex equivalents (e.g. `^PROFI_FOCENI/Produkty/[^/]+(/|$)`). However, the production code path stores user input as raw regex without translation:

1. `AddRuleHandler` stores `request.PathPattern.Trim()` directly (no translation step).
2. `UpdateRuleHandler` similarly stores `request.PathPattern.Trim()` directly.
3. `TagRuleMatcher.Matches()` compiles `r.PathPattern` directly as a `Regex`.
4. `AddRuleRequestValidator.BeValidRegex` validates the input as a raw regex.

The test name `Translate_IdempotentCheck_SkipsAlreadyMigratedPatterns` indicates the translator was a one-time migration utility, used to convert legacy glob entries to regex form. That migration has already run; no further callers exist.

Additionally, the Photobank feature spec (`docs/features/photobank.md`, Tag Rules section) still describes the input syntax as glob-like with `*` as a single-segment wildcard. This is misleading: in practice, users must enter raw regex patterns.

The risks of leaving this in place:
- **Cognitive overhead** for future developers reading `TagRuleMatcher`, who will wonder why a translator exists but is never invoked.
- **False test-suite confidence**: `TagRulePatternTranslatorTests` passes green but exercises code with no production effect.
- **Stale documentation**: the next developer adding a tag rule will be confused about which syntax to use.

**Assumption (see Open Questions):** The glob → regex migration is complete and all `TagRule.PathPattern` values currently stored in the database are already in regex form. If this assumption is invalidated by data inspection, the alternative path is to wire the translator into the handlers instead of removing it.

## Functional Requirements

### FR-1: Remove the `TagRulePatternTranslator` class
Delete `backend/src/Anela.Heblo.Domain/Features/Photobank/TagRulePatternTranslator.cs`.

**Acceptance criteria:**
- The file no longer exists in the repository.
- A repo-wide search for the symbols `TagRulePatternTranslator` and `TagRulePatternTranslator.Translate` returns zero hits.
- `dotnet build` succeeds against the full solution.

### FR-2: Remove the `TagRulePatternTranslator` test suite
Delete the corresponding test file `TagRulePatternTranslatorTests.cs` (located under the Photobank test project; exact path to be confirmed at implementation time by following standard test-project layout).

**Acceptance criteria:**
- The test file no longer exists in the repository.
- `dotnet test` succeeds against the full solution with no missing-reference errors.
- The total test count drops by exactly the number of tests previously in `TagRulePatternTranslatorTests`.

### FR-3: Verify no other production code references the translator
Before deletion, perform a final repo-wide search to confirm that DI registration, reflection-based lookups, configuration files, and serialization metadata do not reference `TagRulePatternTranslator`.

**Acceptance criteria:**
- Search confirms zero non-test references (production code, DI container, JSON/YAML configuration, scaffolding).
- If any unexpected reference is found, halt the deletion and surface as a new open question.

### FR-4: Update Photobank feature documentation
Update the Tag Rules section of `docs/features/photobank.md` to accurately describe the accepted pattern syntax.

**Acceptance criteria:**
- The Tag Rules section states that `PathPattern` is a .NET-compatible regular expression (not a glob).
- Any prior wording suggesting `*` is a wildcard, or that glob syntax is supported, is removed or rewritten.
- At least one concrete example regex pattern matching a typical asset path is included, with a brief explanation of the anchors / character classes used.
- Validation behavior is documented: invalid regex is rejected at `AddRule` / `UpdateRule` time by `BeValidRegex`.

### FR-5: No behavioral change to tag-rule matching
This change is documentation + dead-code removal only. No change to runtime behavior is allowed.

**Acceptance criteria:**
- `AddRuleHandler`, `UpdateRuleHandler`, `TagRuleMatcher`, `AddRuleRequestValidator`, and `UpdateRuleRequestValidator` are not modified except, if absolutely required, to remove unused `using` directives left behind by the deletion.
- All existing Photobank tag-rule tests pass without modification.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — this is dead-code removal. No production code path is altered.

### NFR-2: Security
No security impact. Pattern validation (`BeValidRegex`) remains in place; raw-regex input was the de facto accepted form before this change.

### NFR-3: Backward Compatibility
Existing `TagRule.PathPattern` values stored in the database must continue to function as-is. Since the production matcher already compiles them as regex, no migration is required **provided the Open Question regarding stored-data shape is resolved as expected**.

### NFR-4: Validation
The repository's validation gate must pass:
- `dotnet build` (clean, no new warnings introduced by the change).
- `dotnet format` (no formatting drift).
- `dotnet test` (all tests pass; only the deleted translator tests should disappear).
- No frontend changes are expected; `npm run build` / `npm run lint` not required unless the change incidentally touches frontend files (it should not).

## Data Model
No schema changes. The `TagRule` entity is untouched; its `PathPattern : string` column continues to hold a raw .NET regex.

## API / Interface Design
No API surface changes.
- `POST /api/photobank/tag-rules` (Add): unchanged — accepts raw regex, validates via `BeValidRegex`.
- `PUT /api/photobank/tag-rules/{id}` (Update): unchanged — same behavior.
- `TagRuleMatcher.Matches()` (internal): unchanged.

## Dependencies
- **Database content**: the assumption that all stored `TagRule.PathPattern` values are already in regex form (see Open Questions).
- **Documentation**: `docs/features/photobank.md` is the single doc that must be updated; no other documents are known to reference the translator.

## Out of Scope
- Wiring `TagRulePatternTranslator` into the production handlers (the "Option 2" path from the brief). This spec assumes the translator is dead and removes it; if the data-inspection Open Question reveals legacy glob entries, that work becomes a separate spec.
- Re-introducing glob syntax for tag rules as a UX improvement.
- Any other dead-code cleanup in the Photobank module.
- Changes to the frontend tag-rules editor UI.
- Any change to `TagRuleMatcher`'s matching semantics.

## Open Questions

1. **Database state of `TagRule.PathPattern`** — Before removing the translator, can we confirm via a SELECT against the production (or most recent staging) database that no `TagRule.PathPattern` rows contain glob-style patterns (e.g. patterns containing `*` outside a regex quantifier context, or unanchored segment paths like `/PROFI_FOCENI/Produkty/*`)? If legacy glob rows exist, removal is unsafe and Option 2 (wire the translator into the handlers) becomes the correct path. The implementer should run this check as the first step.
2. **Test file location** — The brief does not specify the exact path of `TagRulePatternTranslatorTests.cs`. Implementer should locate it via repo search (expected under `backend/test/.../Photobank/`).

## Status: HAS_QUESTIONS