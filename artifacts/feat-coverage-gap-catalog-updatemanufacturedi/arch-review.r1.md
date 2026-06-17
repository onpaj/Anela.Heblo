I have enough grounding. Now writing the architecture review.

# Architecture Review: Unit Test Coverage for UpdateManufactureDifficultyHandler

## Skip Design: true

## Architectural Fit Assessment

This is a pure test-coverage addition with zero production code change. The handler under test (`backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/UpdateManufactureDifficulty/UpdateManufactureDifficultyHandler.cs`) already follows the project's standard pattern:

- **MediatR vertical-slice handler** colocated with its `Request`/`Response` DTOs.
- Depends on `IManufactureDifficultyRepository`, `ICatalogRepository`, `IMapper`, `TimeProvider`, `ILogger<>`.
- Returns a `BaseResponse`-derived envelope with `Success`/`ErrorCode`/`Params` (`ErrorCodes` enum from `Anela.Heblo.Application.Shared`).

The sibling `CreateManufactureDifficultyHandlerTests.cs` already establishes the testing convention for this exact subsystem: **xUnit + Moq + FluentAssertions**, constructor-initialized mocks per fixture, `Method_Scenario_Expectation` naming, and tests living flat under `backend/test/Anela.Heblo.Tests/Features/Catalog/`. The new test file should mirror that file's structure exactly — no new patterns, no new helpers.

Integration points are clean: handler boundary, mocked repos, no I/O, no time dependency required because the handler never reads `TimeProvider.GetUtcNow()`.

## Proposed Architecture

### Component Overview

```
UpdateManufactureDifficultyHandlerTests (xUnit fixture)
    │
    ├── Mock<IManufactureDifficultyRepository>   ← GetByIdAsync / HasOverlapAsync / UpdateAsync
    ├── Mock<ICatalogRepository>                 ← RefreshManufactureDifficultySettingsData
    ├── Mock<IMapper>                            ← Map<request→entity>, Map<entity→DTO>
    ├── Mock<TimeProvider>                       ← unused by handler, pass Mock.Object
    └── Mock<ILogger<UpdateManufactureDifficultyHandler>>
            │
            ▼
    UpdateManufactureDifficultyHandler  (system under test)
```

### Key Design Decisions

#### Decision 1: Flat test file location (no sub-folder per use case)
**Options considered:**
- A) `backend/test/Anela.Heblo.Tests/Features/Catalog/UpdateManufactureDifficultyHandlerTests.cs` (flat — sibling convention).
- B) `backend/test/Anela.Heblo.Tests/Features/Catalog/UseCases/UpdateManufactureDifficulty/UpdateManufactureDifficultyHandlerTests.cs` (mirrors production tree, as suggested by spec).

**Chosen approach:** A — flat under `Features/Catalog/`.

**Rationale:** Every sibling Catalog handler test (`CreateManufactureDifficultyHandlerTests.cs`, `AcceptStockUpOperationHandlerTests.cs`, `GetCatalogDetailHandlerTests.cs`, etc.) lives flat. Following the spec's "mirror production tree" would create a one-off subdirectory pattern. Convention beats stated preference here.

#### Decision 2: Mocking library — Moq + FluentAssertions
**Options considered:** Moq vs NSubstitute / FluentAssertions vs raw `Assert`.

**Chosen approach:** Moq with FluentAssertions, matching `CreateManufactureDifficultyHandlerTests.cs` exactly.

**Rationale:** Already the house style for this exact subsystem. NFR-3 says "match what's already there."

#### Decision 3: Treat `AutoMapper` as a mock, not real mapper
**Options considered:**
- A) Mock `IMapper` (current sibling convention).
- B) Use a real `MapperConfiguration` loaded with `CatalogMappingProfile`.

**Chosen approach:** A — mock `IMapper`.

**Rationale:** Sibling tests mock it. Real-mapper tests would coverage-test the mapping profile, which is out of scope per "Out of Scope" in the spec.

#### Decision 4: How to assert FR-1 entity mutation
The handler calls `_mapper.Map(request, existing)` (the two-arg overload that mutates `existing` in place via the AutoMapper profile). With a mocked `IMapper`, the in-place mutation does NOT happen automatically.

**Chosen approach:** In FR-1, use `_mapperMock.Setup(m => m.Map(request, existing))` with a `Callback` that manually assigns the request fields onto `existing`, then assert post-call. Alternatively, assert via the `UpdateAsync` argument captured with `It.Is<>` / `Callback`.

**Rationale:** Without the callback, asserting "fields were updated" tests `IMapper`, not the handler. The handler's job is "pass the request and the entity to the mapper, then persist." Assert that contract, not the mapping itself.

## Implementation Guidance

### Directory / Module Structure

```
backend/test/Anela.Heblo.Tests/Features/Catalog/
  UpdateManufactureDifficultyHandlerTests.cs   ← NEW (single file)
```

No new helper classes, no new builders, no shared fixtures.

### Interfaces and Contracts

The test file consumes only existing public types — no new contracts:

| Type | Source |
|---|---|
| `UpdateManufactureDifficultyHandler` | `Anela.Heblo.Application.Features.Catalog.UseCases.UpdateManufactureDifficulty` |
| `UpdateManufactureDifficultyRequest` | same namespace |
| `UpdateManufactureDifficultyResponse` | same namespace |
| `ManufactureDifficultySetting` (entity) | `Anela.Heblo.Domain.Features.Catalog` |
| `ManufactureDifficultySettingDto` | `Anela.Heblo.Application.Features.Catalog.Contracts` |
| `IManufactureDifficultyRepository` | `Anela.Heblo.Domain.Features.Catalog` |
| `ICatalogRepository` | `Anela.Heblo.Domain.Features.Catalog` |
| `ErrorCodes` (enum) | `Anela.Heblo.Application.Shared` |

**Constructor injection order to honor** (from production code):
`(IManufactureDifficultyRepository, ICatalogRepository, IMapper, TimeProvider, ILogger<UpdateManufactureDifficultyHandler>)`.
Pass `new Mock<TimeProvider>().Object` even though the handler does not use it — it is a required ctor parameter.

### Data Flow (per test)

```
Arrange:                          Act:                       Assert:
  setup mocks   ──────────► handler.Handle(request, ct) ──► verify result + mock invocations
```

Branch coverage map:
| Test | Branch covered |
|---|---|
| FR-1 happy path | post-guards → mapper.Map → UpdateAsync → RefreshManufactureDifficultySettingsData |
| FR-2 not-found | `existing == null` early return |
| FR-3/FR-4 invalid range | `ValidFrom >= ValidTo` early return |
| FR-5 boundary | passes the date guard (overlap mock invoked) |
| FR-6 overlap | `hasOverlap == true` early return |
| FR-7 excludeId | argument-match assertion inside FR-1 |

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec field-name drift: spec refers to `ManufactureDifficultyHistory` entity / `Params` field name that does not exist verbatim in code. | Medium | Use the actual production names: entity is `ManufactureDifficultySetting`; response field is `DifficultyHistory` (a `ManufactureDifficultySettingDto`); `Params` is a `Dictionary<string, string>` with keys `"id"`, `"field"`, `"productCode"` (handler source confirms). See Specification Amendments below. |
| FR-3/FR-4 expected `Params` payload differs from spec wording. The handler's `InvalidValue` branch emits `Params["field"] = "ValidFrom must be earlier than ValidTo"` — **not** the offending field names per FR-3. | Low | Assert the actual payload that exists in the handler today. Do not change the handler. See Specification Amendments. |
| FR-1 "mutable fields were updated" assertion can accidentally test `IMapper` rather than the handler. | Medium | Use `_mapperMock.Setup(m => m.Map(request, existing)).Callback(...)` to mutate `existing`, then assert. Or capture the argument passed to `UpdateAsync` via `It.Is<>` / `Callback`. |
| FR-5 boundary test relies on `ValidFrom = ValidTo - 1 day` always passing — but the handler only runs the date check when **both** values are non-null. A `null/non-null` mix bypasses the check entirely. | Low | Use two non-null DateTimes in FR-5. Optionally add a single "both null → no early return" test; out of FR scope so skip. |
| Coverage threshold (NFR-1) might not hit 60% if logging lines or AutoMapper invocation paths are counted separately. | Low | Confirm with `dotnet test /p:CollectCoverage=true` against the project's coverage filter after writing tests. If under, add one more verification test (e.g., logger called on success). |
| `TimeProvider` ctor parameter is currently unused — easy to overlook and break the build by passing `null`. | Low | Always pass `new Mock<TimeProvider>().Object`. Document this with a one-line comment in the fixture ctor. |

## Specification Amendments

The following spec items conflict with the actual code and must be reconciled before implementation:

1. **Entity name.** Spec §Data Model says `ManufactureDifficultyHistory (entity)`. The real entity is **`ManufactureDifficultySetting`** (`backend/src/Anela.Heblo.Domain/Features/Catalog/ManufactureDifficultySetting.cs`). Tests must use the real name.
2. **Response payload field.** Spec implies the DTO field is named per the entity. Actual: `UpdateManufactureDifficultyResponse.DifficultyHistory` (typed `ManufactureDifficultySettingDto`). Use the real names.
3. **FR-3 acceptance criteria — `InvalidValue` `Params`.** Spec asks for `Params` to identify "the offending field(s) (`ValidFrom` / `ValidTo`)". Handler actually emits `Params["field"] = "ValidFrom must be earlier than ValidTo"` (single string explanation). **Adjust acceptance criteria to assert this exact payload**, matching how `CreateManufactureDifficultyHandlerTests.Handle_ValidFromAfterValidTo_ThrowsArgumentException` already asserts it. Do not change the handler.
4. **FR-6 acceptance criteria — `ManufactureDifficultyConflict` `Params`.** Handler emits `Params["productCode"] = existing.ProductCode`. Spec is consistent — keep as-is and assert the key is `"productCode"`.
5. **FR-2 acceptance criteria — not-found `Params`.** Handler emits `Params["id"] = request.Id.ToString()`. Spec is consistent — assert key `"id"` with the stringified request Id.
6. **TimeProvider dependency.** Spec lists mocked repositories but omits the `TimeProvider` ctor parameter (introduced in the handler). Update NFR-2 to include `Mock<TimeProvider>` (even though unused) so the test compiles.
7. **FR-1 — "mutable fields updated" assertion mechanism.** With `IMapper` mocked, the in-place `Map(request, existing)` does not mutate. Spec must permit asserting the contract via mapper-callback or `UpdateAsync` argument capture rather than direct entity field equality.

## Prerequisites

None. All required infrastructure already exists:
- `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` already references xUnit, Moq, FluentAssertions, and the Application/Domain projects (sibling test file proves this).
- No new NuGet packages.
- No DB, no migrations, no config.
- No production code changes.

Implementation can begin immediately after the spec amendments above are acknowledged.