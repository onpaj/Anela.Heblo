## Module / File
`backend/src/Anela.Heblo.Application/Features/FeatureFlags/UseCases/ListFlags/ListFlagsHandler.cs`

## Coverage
Line coverage: 16.7% (filter threshold: 60%)

## What's not tested
The handler merges live flag values with an override repository and conditionally populates `IsOverridden`, `UpdatedBy`, and `UpdatedAt` on each DTO based on whether an override exists for the flag key. No test covers the "has override" path (should produce `IsOverridden=true` with populated author/date) or the "no override" path (should produce `IsOverridden=false` with null fields). The `StringComparer.Ordinal` lookup contract is also unverified — a case-mismatched key would silently miss the override.

## Why it matters
The feature flags admin UI uses `IsOverridden` to display which flags have been manually overridden. A regression in this branch would silently show all flags as non-overridden even when overrides exist, hiding operator customisations.

## Suggested approach
Unit tests with seeded override repository: one test with flags that have matching overrides (assert IsOverridden=true, UpdatedBy, UpdatedAt); one with no overrides (assert IsOverridden=false, null fields); one with a key whose case doesn't match (assert IsOverridden=false). ~1.5h effort.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._
