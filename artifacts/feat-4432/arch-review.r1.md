# Architecture Review: DataQualityMappingProfile tests

## Skip Design: true
Test-only, backend-only change; no UI.

## Architectural Fit Assessment
Fits existing convention: tests live in `backend/test/Anela.Heblo.Tests/Features/DataQuality/` (namespace `Anela.Heblo.Tests.Features.DataQuality`), and mapper profiles are tested via a real `MapperConfiguration` (see `Features/Catalog/CatalogMappingProfileStockPriceTests.cs`). The private static `GetMismatchFlags` is verified through the public mapping behaviour.

## Proposed Architecture

### Component Overview
`DataQualityMappingProfileTests` -> `IMapper` (real, built from `DataQualityMappingProfile`) -> `InvoiceDqtResult.Create(...)` input -> `InvoiceDqtResultDto` assertions.

### Key Design Decisions

#### Decision 1: Test through AutoMapper, not reflection
**Options considered:** reflection on private method; widening visibility; map via IMapper.
**Chosen approach:** map via IMapper.
**Rationale:** no production change, tests real contract, CLAUDE.md demands surgical changes.

#### Decision 2: Pin enum numeric values
Use `[Theory]` with explicit literal ints (1,2,4,8,16,32) rather than `(int)enum` on both sides, so enum reordering is caught.

## Implementation Guidance

### Directory / Module Structure
One new file: `backend/test/Anela.Heblo.Tests/Features/DataQuality/DataQualityMappingProfileTests.cs`.

### Interfaces and Contracts
Mapper creation: `new MapperConfiguration(cfg => cfg.AddProfile<DataQualityMappingProfile>(), NullLoggerFactory.Instance).CreateMapper()`. Input via `InvoiceDqtResult.Create(Guid, string, InvoiceMismatchType, string?, string?, string?)`.

### Data Flow
Create entity -> `mapper.Map<InvoiceDqtResultDto>(entity)` -> assert `MismatchType` and `MismatchFlags`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Profile config fails validation for unrelated maps | Low | Do not call AssertConfigurationIsValid unless it passes; map only needed types |
| Flag order assertions brittle | Low | Order is ascending enum order by design; document in test |

## Specification Amendments
None.

## Prerequisites
None.
