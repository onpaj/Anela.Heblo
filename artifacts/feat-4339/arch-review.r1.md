# Architecture Review: Unit tests for UpdateClassificationRuleHandler

## Skip Design: true
Backend test-only change; no UI.

## Architectural Fit Assessment
Fits existing convention: handler tests live in `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/*HandlerTests.cs` (e.g. `GetClassificationRuleTypesHandlerTests`), flat in that folder, xUnit + Moq + FluentAssertions, Arrange/Act/Assert with `Handle_<Scenario>_<Expected>` naming. No production code touched; docs consulted (testing-strategy, filesystem) impose no additional constraints.

## Proposed Architecture

### Component Overview
`UpdateClassificationRuleHandlerTests` -> constructs `UpdateClassificationRuleHandler(Mock<IClassificationRuleRepository>, Mock<IMapper>, Mock<ICurrentUserService>)`.

### Key Design Decisions

#### Decision 1: Mock IMapper vs real mapper
**Options considered:** real `MapperConfiguration` with `InvoiceClassificationMappingProfile`; `Mock<IMapper>`.
**Chosen approach:** `Mock<IMapper>` returning a sentinel `ClassificationRuleDto`.
**Rationale:** Mapping profile is already covered by `InvoiceClassificationMappingProfileTests`; the handler test should assert the handler passes the repo's *returned* entity to the mapper and returns its result. Note `Map<TDestination>(object)` is the overload used; set up `m.Map<ClassificationRuleDto>(It.IsAny<object>())` or with the specific instance.

#### Decision 2: Real ClassificationRule entity
**Chosen approach:** Build a real `ClassificationRule` via its public constructor (entity has private setters and is sealed to behavior), then assert state after `Update`. Do not mock it (non-virtual members).
**Rationale:** Verifies the real field assignment, which is the stated purpose.

## Implementation Guidance

### Directory / Module Structure
Create `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/UpdateClassificationRuleHandlerTests.cs`.

### Interfaces and Contracts
- `IClassificationRuleRepository.GetByIdAsync(Guid) : Task<ClassificationRule?>`; `UpdateAsync(ClassificationRule) : Task<ClassificationRule>`.
- `ICurrentUserService.GetCurrentUser()` returns `new CurrentUser("id","Test User","t@x.cz",true)`.
- Request/response are classes in `...UseCases.UpdateClassificationRule`; `ClassificationRuleDto` in `...Contracts`.

### Data Flow
Handle -> GetByIdAsync -> (null -> throw) -> GetCurrentUser -> entity.Update -> UpdateAsync -> mapper.Map -> response.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `CurrentUser.Name` nullable; null triggers ArgumentNullException | Low | Optional edge test documents it |
| Ambiguous Moq setup for `IMapper.Map` overloads | Low | Use `Map<ClassificationRuleDto>(It.IsAny<object>())` |
| Asserting on wrong instance (mapper input) | Low | Return a distinct entity from `UpdateAsync` and verify mapper received it |

## Specification Amendments
None.

## Prerequisites
None.
