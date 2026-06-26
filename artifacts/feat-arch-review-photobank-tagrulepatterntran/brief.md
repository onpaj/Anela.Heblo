## Module
Photobank

## Finding
`TagRulePatternTranslator` (`backend/src/Anela.Heblo.Domain/Features/Photobank/TagRulePatternTranslator.cs`) is a Domain-layer class that is **never called from any production code**. A project-wide search confirms its only usages are in the test file `TagRulePatternTranslatorTests.cs`.

The production path is:
1. `AddRuleHandler` (line 22) stores `request.PathPattern.Trim()` directly — no translation
2. `TagRuleMatcher.Matches()` (line 36) compiles `r.PathPattern` directly as a `Regex`
3. The `AddRuleRequestValidator` validates `PathPattern` as a raw regex via `BeValidRegex`

`TagRulePatternTranslator.Translate()` was designed to convert glob-style patterns like `/PROFI_FOCENI/Produkty/*` to their regex equivalents (`^PROFI_FOCENI/Produkty/[^/]+(/|$)`). The test name `Translate_IdempotentCheck_SkipsAlreadyMigratedPatterns` suggests this was a migration utility — it was used to convert old glob rules to regex, and the migration is now complete.

There is also a mismatch with the feature spec (`docs/features/photobank.md`, Tag Rules section), which still describes the input syntax as glob-like with `*` as a single-segment wildcard. In practice, users must enter raw regex patterns, not globs.

## Why it matters
- Dead code in the Domain layer creates cognitive overhead — future developers reading `TagRuleMatcher` will wonder why the translator exists but is never used
- The test suite gives false confidence: `TagRulePatternTranslatorTests` passes but the translator has no effect in production
- The feature doc is out of date and will confuse the next developer adding a tag rule

## Suggested fix
Two options:
1. **Remove** `TagRulePatternTranslator` and its tests if the glob-to-regex migration is complete and no glob patterns remain in the database
2. **Wire it in** if glob syntax is still desired: call `TagRulePatternTranslator.Translate(request.PathPattern.Trim())` in `AddRuleHandler` (line 22) and `UpdateRuleHandler` (line 24) before storing, and update `AddRuleRequestValidator` to validate the post-translation result

Whichever option is chosen, also update `docs/features/photobank.md` Tag Rules section to accurately describe the accepted pattern syntax.

---
_Filed by daily arch-review routine on 2026-05-27._