## Module
InvoiceClassification

## Finding
`GetClassificationHistoryHandler.Handle` (lines 31–47 of `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationHistory/GetClassificationHistoryHandler.cs`) manually projects `ClassificationHistory → ClassificationHistoryDto` using a `Select` lambda, including explicit member assignments for all 14 properties.

`InvoiceClassificationMappingProfile` (`backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs`, line 16) already defines this exact mapping:
```csharp
CreateMap<ClassificationHistory, ClassificationHistoryDto>()
    .ForMember(dest => dest.InvoiceId, opt => opt.MapFrom(src => src.AbraInvoiceId))
    .ForMember(dest => dest.RuleName, opt => opt.MapFrom(src => src.ClassificationRule != null ? src.ClassificationRule.Name : null));
```

The handler does not inject `IMapper`, despite every sibling handler in the same module (`GetClassificationRulesHandler`, `GetInvoiceDetailsHandler`, `CreateClassificationRuleHandler`, `UpdateClassificationRuleHandler`) doing so.

## Why it matters
Adding or renaming a field in `ClassificationHistory` or `ClassificationHistoryDto` now requires updating two places — the mapping profile and the handler's manual projection — with no compiler enforcement to remind a developer of the second site. The mapping profile exists precisely to prevent this duplication.

## Suggested fix
Inject `IMapper` into `GetClassificationHistoryHandler` and replace lines 31–47 with:
```csharp
var historyDtos = _mapper.Map<List<ClassificationHistoryDto>>(historyItems);
```

This is a one-line change that removes ~17 lines of manual boilerplate and brings the handler in line with all its siblings.

---
_Filed by daily arch-review routine on 2026-07-07._
