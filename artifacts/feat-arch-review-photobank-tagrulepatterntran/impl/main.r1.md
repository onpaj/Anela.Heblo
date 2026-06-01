Implementation complete. Commit `e4fd5c81` is on branch `feat-arch-review-photobank-tagrulepatterntran` and passed both spec compliance and code quality review.

**What was done:**
- Deleted `TagRulePatternTranslator.cs` and its test suite (zero production callers confirmed)
- Updated `docs/features/photobank.md` Tag Rules section from stale glob/`*` syntax to an accurate .NET regex description with a worked example in Czech
- All existing Photobank tests pass; build is clean

**Status: DONE**