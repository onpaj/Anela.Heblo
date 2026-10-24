## Module / File
`backend/src/Anela.Heblo.Application/Features/DataQuality/DataQualityMappingProfile.cs`

## Coverage
Line coverage: 54% (12/22 lines, filter threshold: 60%)

## What's not tested
`GetMismatchFlags` converts a `[Flags]` enum value into a list of set flag names, skipping the `None` sentinel. Three behaviours are uncovered: (1) a compound value (multiple flags set simultaneously) must produce a list with exactly those flag names; (2) `InvoiceMismatchType.None` must never appear in the output; (3) a zero value (no flags) must return an empty list. The AutoMapper profile also maps `MismatchType` from the enum to its `int` cast — if that cast is wrong (e.g. a flag is reordered in the enum), the API surface will silently send different integers.

## Why it matters
The `MismatchFlags` field is the primary human-readable explanation of a data-quality mismatch sent to the frontend. A bug in flag extraction (e.g. iterating in wrong order, including `None`, or losing a flag due to incorrect `HasFlag` usage) would send misleading mismatch descriptions to the user without any test catching it.

## Suggested approach
Pure unit tests (no database): (1) single flag → list of one; (2) compound flags (e.g. two flags ORed) → list of exactly those two names; (3) `None` → empty list; (4) `None` does not appear in any non-None result. ~1h.

---
_Filed by weekly coverage-gap routine on 2026-10-05. Based on CI run #36876655065 (923c24940cf65d6ce38a8b56d75709bba333ca91)._
