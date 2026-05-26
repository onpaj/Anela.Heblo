## Module
InvoiceClassification

## Finding
`InvoiceClassificationController.GetAvailableRuleTypes()` (lines 76–86 of `backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs`) does not dispatch via MediatR. Instead, the controller:

1. Declares a second constructor dependency on `IEnumerable<IClassificationRule>` — a Domain interface — injected directly into the API layer.
2. Iterates over `_classificationRules`, projects each to a `ClassificationRuleTypeDto`, and returns the list from the action method itself.

No handler exists for this operation; it is the only endpoint in the module that bypasses MediatR entirely.

## Why it matters
`development_guidelines.md` forbids business logic in controllers: _"Business logic must be in MediatR handlers, NOT in controllers"_. Projecting domain rule metadata to a DTO is business logic. Additionally, the controller taking a direct dependency on `IEnumerable<IClassificationRule>` creates a Domain→API layer dependency that bypasses the Application layer entirely, violating Clean Architecture's dependency rule. It also makes this operation untestable via handler unit tests.

## Suggested fix
Create a `GetClassificationRuleTypesHandler` in `Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/` that accepts the `IEnumerable<IClassificationRule>` dependency and maps to `ClassificationRuleTypeDto`. The controller action becomes a one-liner dispatching to MediatR, matching all other actions in the same controller. Remove `IEnumerable<IClassificationRule>` from the controller constructor.

---
_Filed by daily arch-review routine on 2026-05-25._