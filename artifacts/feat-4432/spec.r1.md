# Specification: Unit tests for DataQualityMappingProfile (GetMismatchFlags / MismatchType)

## Summary
Add pure unit tests covering `DataQualityMappingProfile`, specifically the `InvoiceDqtResult -> InvoiceDqtResultDto` map: the private `GetMismatchFlags` bitflag decomposition and the `(int)MismatchType` cast. No production code changes.

## Background
Issue #4432 (coverage-gap): the profile is at 54% line coverage (12/22). `MismatchFlags` is the human-readable mismatch explanation sent to the frontend; `MismatchType` is the raw int. `InvoiceMismatchType` is a `[Flags]`-style enum: None=0, MissingInFlexi=1, MissingInShoptet=2, TotalWithVatDiffers=4, TotalWithoutVatDiffers=8, ItemsDiffer=16, DuplicateInvoiceCode=32. `GetMismatchFlags` is private static, so it is exercised through AutoMapper.

## Functional Requirements

### FR-1: Single flag
**Acceptance criteria:**
- Mapping a result with one flag yields `MismatchFlags` with exactly that flag name.

### FR-2: Compound flags
**Acceptance criteria:**
- Two or more ORed flags yield exactly those names, in enum declaration (ascending value) order, no extras.
- All flags set yields all six names.

### FR-3: None excluded
**Acceptance criteria:**
- `InvoiceMismatchType.None` yields an empty (non-null) list.
- The string "None" never appears in any result.

### FR-4: MismatchType int cast
**Acceptance criteria:**
- `MismatchType` int equals `(int)` of the enum for each defined value, pinning numeric values (1,2,4,8,16,32) so reordering the enum fails a test.

### FR-5: Other maps (optional, cheap)
**Acceptance criteria:**
- `DqtRun` TestType/Status/TriggerType map to enum `ToString()`; `DqtDriftResult.TestType` likewise; profile config passes `AssertConfigurationIsValid` only if it already does (otherwise skip).

## Non-Functional Requirements
### NFR-1: Performance
Pure in-memory, no database; milliseconds.
### NFR-2: Security
N/A.

## Data Model
No changes.

## API / Interface Design
No changes. Tests build a mapper via `new MapperConfiguration(cfg => cfg.AddProfile<DataQualityMappingProfile>(), NullLoggerFactory.Instance).CreateMapper()` (existing repo pattern, e.g. CatalogMappingProfileStockPriceTests).

## Dependencies
xUnit, AutoMapper, existing `InvoiceDqtResult.Create(...)` factory (entity has private setters).

## Out of Scope
Changing production code, making `GetMismatchFlags` public/internal, handler tests, undefined/out-of-range enum values.

## Open Questions
None.

## Status: COMPLETE
