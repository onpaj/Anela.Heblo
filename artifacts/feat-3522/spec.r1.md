# Specification: Refactor GetClassificationHistoryHandler to Use IMapper

## Summary
`GetClassificationHistoryHandler.Handle` hand-writes a 17-line `Select` projection from the `ClassificationHistory` domain entity to `ClassificationHistoryDto`, duplicating a mapping that `InvoiceClassificationMappingProfile` already defines in full. This spec covers replacing the manual projection with a single `IMapper.Map` call so the handler matches every sibling handler in the module and the mapping lives in exactly one place. It is a behavior-preserving internal refactor with no change to inputs, outputs, or the HTTP contract.

## Background
The InvoiceClassification module uses AutoMapper (via `InvoiceClassificationMappingProfile`) as its single source of truth for domain-to-DTO translation. Four of the module's handlers — `GetClassificationRulesHandler`, `GetInvoiceDetailsHandler`, `CreateClassificationRuleHandler`, `UpdateClassificationRuleHandler` — inject `IMapper` and delegate translation to it.

`GetClassificationHistoryHandler` is the lone exception. It injects only `IClassificationHistoryRepository` and `ILogger`, and manually constructs each `ClassificationHistoryDto` inside a `Select` lambda, assigning all 14 properties by hand (lines 31–47). The mapping profile (line 14–16) already defines the identical `CreateMap<ClassificationHistory, ClassificationHistoryDto>()`, including the two non-trivial member rules:
- `InvoiceId` ← `src.AbraInvoiceId`
- `RuleName` ← `src.ClassificationRule != null ? src.ClassificationRule.Name : null`

The remaining 12 properties are name-identical and map by AutoMapper convention.

Because the mapping is expressed twice, adding, removing, or renaming a field on either `ClassificationHistory` or `ClassificationHistoryDto` requires a coordinated edit in two files, and nothing in the compiler or type system flags the second site if a developer forgets it. This is a latent maintenance defect: the profile's mapping is effectively dead code for this path today, and the manual projection can silently drift out of sync.

## Functional Requirements

### FR-1: Delegate history mapping to IMapper
The handler must translate the paged `ClassificationHistory` items to `ClassificationHistoryDto` via the injected `IMapper` using the existing profile mapping, instead of the inline `Select` projection.

**Acceptance criteria:**
- `GetClassificationHistoryHandler` declares a `private readonly IMapper _mapper` field, assigned from a constructor parameter.
- The constructor signature becomes `(IClassificationHistoryRepository historyRepository, IMapper mapper, ILogger<GetClassificationHistoryHandler> logger)` (parameter order at the implementer's discretion, but `IMapper` added alongside the existing dependencies; existing dependencies retained).
- Lines 31–47 (the inline `Select(... new ClassificationHistoryDto { ... }).ToList()`) are replaced with `var historyDtos = _mapper.Map<List<ClassificationHistoryDto>>(historyItems);`.
- No manual per-property assignment of `ClassificationHistoryDto` remains anywhere in the handler.
- `using AutoMapper;` is added to the file's using directives.

### FR-2: Preserve output equivalence
The DTOs produced after the refactor must be field-for-field identical to those produced by the current manual projection for the same input.

**Acceptance criteria:**
- All 14 `ClassificationHistoryDto` properties are populated identically to the pre-refactor code: `Id`, `InvoiceId` (from `AbraInvoiceId`), `InvoiceNumber`, `InvoiceDate`, `CompanyName`, `Description`, `ClassificationRuleId`, `RuleName` (from `ClassificationRule?.Name`, null when the rule is null), `Department`, `Result`, `AccountingTemplateCode`, `ErrorMessage`, `Timestamp`, `ProcessedBy`.
- When `ClassificationRule` is null, `RuleName` is null (the profile's conditional already guarantees this).
- The `GetClassificationHistoryResponse` envelope is unchanged: `Items`, `TotalCount`, `Page`, and `PageSize` are populated exactly as before.
- The repository call `GetPagedHistoryAsync(...)` and its arguments are unchanged.

### FR-3: Registration and resolution remain valid
The handler must continue to resolve from the DI container with all dependencies satisfied.

**Acceptance criteria:**
- `IMapper` is already registered in the application's service collection (it is, since sibling handlers consume it); no new registration is required. Confirm during implementation that no module-specific registration change is needed.
- The application builds and the handler is constructible via the container with no runtime "unable to resolve" errors.

### FR-4: Verify mapping profile completeness before deletion
Before removing the manual projection, confirm the profile mapping covers every field the manual code sets, so no property is silently dropped.

**Acceptance criteria:**
- Each of the 14 manual assignments corresponds to either an explicit `ForMember` in the profile (`InvoiceId`, `RuleName`) or a convention-based name match (the other 12). This has been verified against the current source and holds; the implementer re-confirms if either type changed since.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change to latency or throughput is expected. AutoMapper compiles its mappings once at startup; per-item mapping cost is comparable to the manual projection. The single-page result set (bounded by `PageSize`) makes any per-item difference negligible. No new database round-trips are introduced.

### NFR-2: Security
No change to authentication, authorization, data exposure, or the set of fields returned to the client. The refactor is internal to the application layer and touches no endpoint surface, no data sensitivity classification, and no logging of sensitive values.

### NFR-3: Maintainability
After this change, the `ClassificationHistory → ClassificationHistoryDto` mapping exists in exactly one location (`InvoiceClassificationMappingProfile`), and all five module handlers follow one consistent pattern for DTO translation.

### NFR-4: Backward compatibility
The HTTP API response shape and values are unchanged. No client, contract, or generated OpenAPI client regeneration is required.

## Data Model
No schema or entity changes.

- **`ClassificationHistory`** (domain entity, `Anela.Heblo.Domain.Features.InvoiceClassification`) — source. Relevant fields: `Id`, `AbraInvoiceId`, `InvoiceNumber`, `InvoiceDate`, `CompanyName`, `Description`, `ClassificationRuleId`, `ClassificationRule` (navigation, nullable), `Department`, `Result`, `AccountingTemplateCode`, `ErrorMessage`, `Timestamp`, `ProcessedBy`.
- **`ClassificationHistoryDto`** (`...Application.Features.InvoiceClassification.Contracts`) — destination, 14 properties, class (not record) per project DTO rule.
- **Mapping** (`InvoiceClassificationMappingProfile`, lines 14–16) — `CreateMap<ClassificationHistory, ClassificationHistoryDto>()` with `ForMember` for `InvoiceId` and `RuleName`; unchanged by this spec.

## API / Interface Design
No public interface changes.

- **Handler:** `GetClassificationHistoryHandler : IRequestHandler<GetClassificationHistoryRequest, GetClassificationHistoryResponse>` — constructor gains an `IMapper` parameter; `Handle` body swaps the projection for a mapper call. Signature of `Handle` unchanged.
- **Request/Response contracts:** `GetClassificationHistoryRequest`, `GetClassificationHistoryResponse` — unchanged.
- **Endpoint:** the MVC controller action fronting this request — unchanged.

## Dependencies
- **AutoMapper** — already referenced by the Application project and configured via `InvoiceClassificationMappingProfile`; already registered in DI and consumed by sibling handlers.
- **MediatR** — unchanged.
- No new packages, services, or configuration.

## Testing
- Existing behavior is unit-testable: no test currently references `GetClassificationHistoryHandler` (confirmed — none found under `backend/test`). Adding a focused unit test is recommended but optional per this spec's scope; if added, it should assert that a `ClassificationHistory` (including one case with a null `ClassificationRule`) maps to the expected `ClassificationHistoryDto` field values and that the response envelope (`TotalCount`, `Page`, `PageSize`) is populated.
- An AutoMapper `ConfigurationProvider.AssertConfigurationIsValid()` check, if one exists in the test suite, must still pass.
- Validation gate per project rules: `dotnet build` succeeds and `dotnet format` reports no changes on the touched file.

## Out of Scope
- Refactoring any other handler or any other manual projection elsewhere in the codebase.
- Changing the mapping profile, the DTO, the domain entity, or the repository.
- Altering paging, filtering, or sorting behavior of `GetPagedHistoryAsync`.
- Modifying the controller, request/response contracts, or the OpenAPI client.
- Removing the `ILogger` dependency (retained even though it is currently unused in `Handle`; its removal is a separate concern).

## Open Questions
None.

## Status: COMPLETE
