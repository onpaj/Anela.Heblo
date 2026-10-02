# Design: Unit tests for UpdateClassificationRuleHandler

## Component Design
Single test class `UpdateClassificationRuleHandlerTests` (namespace `Anela.Heblo.Tests.Features.InvoiceClassification`) with private fields for the three mocks, a `CreateHandler()` helper, and a `CreateExistingRule()` helper using `new ClassificationRule("old-name","old-type","old-pattern","OLD-TPL","old-dept","creator")`.

Tests:
1. `Handle_WhenRuleNotFound_ThrowsArgumentExceptionWithId`
2. `Handle_WhenRuleNotFound_DoesNotCallUpdateAsync`
3. `Handle_WhenRuleExists_UpdatesAllFieldsAndSetsUpdatedByFromCurrentUser`
4. `Handle_WhenRuleExists_PersistsAndReturnsMappedDtoOfUpdatedRule`
5. `Handle_WithNullDepartmentAndInactive_PropagatesValues`
6. `Handle_WhenCurrentUserNameIsNull_ThrowsArgumentNullExceptionAndDoesNotPersist`

## Data Schemas
No schema changes. Request: `UpdateClassificationRuleRequest { Id, Name, RuleTypeIdentifier, Pattern, AccountingTemplateCode, Department?, IsActive }`. Response: `UpdateClassificationRuleResponse { Rule: ClassificationRuleDto }`.
