### task: remove-dead-invoice-classification-statistics-code

**Scope reminder:** This is a surgical dead-code removal. Touch only the files listed below —
nothing else. Do not add comments explaining the removal to any surviving file. Do not modify
`IClassificationHistoryRepository.cs`, `InvoiceClassificationModule.cs`,
`ClassificationStats.tsx`, `InvoiceClassificationPage.tsx`, or any EF migration/configuration.

#### Recommended order (for fast local build feedback; all edits land in the same commit)

1. Edit the mapping profile first (removes references to the types about to be deleted).
2. Delete the domain types and DTOs.
3. Delete the repository method.
4. Remove the frontend query-key line.

#### 1. Edit: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs`

Remove exactly these two lines (leave every other `CreateMap<...>()` call untouched):

```csharp
        CreateMap<ClassificationStatistics, ClassificationStatisticsDto>();
        CreateMap<RuleUsageStatistic, RuleUsageStatisticDto>();
```

Result should read (blank line collapses naturally — do not leave a double blank line; match
surrounding spacing style of the file):

```csharp
        CreateMap<ClassificationHistory, ClassificationHistoryDto>()
            .ForMember(dest => dest.InvoiceId, opt => opt.MapFrom(src => src.AbraInvoiceId))
            .ForMember(dest => dest.RuleName, opt => opt.MapFrom(src => src.ClassificationRule != null ? src.ClassificationRule.Name : null));

        CreateMap<AccountingTemplate, AccountingTemplateDto>();
        CreateMap<ReceivedInvoiceItem, ReceivedInvoiceItemDto>();
        CreateMap<ReceivedInvoice, ReceivedInvoiceDto>();
```

No `using` statements need to change in this file (it still uses other types from the same
`Domain.Features.InvoiceClassification` and `...Contracts` namespaces).

#### 2. Delete: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationStatistics.cs`

Whole file deletion. Contains only the `ClassificationStatistics` class.

#### 3. Delete: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/RuleUsageStatistic.cs`

Whole file deletion. Contains only the `RuleUsageStatistic` class.

#### 4. Delete: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ClassificationStatisticsDto.cs`

Whole file deletion. Contains only the `ClassificationStatisticsDto` class.

#### 5. Delete: `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/RuleUsageStatisticDto.cs`

Whole file deletion. Contains only the `RuleUsageStatisticDto` class.

#### 6. Edit: `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs`

Delete the entire `GetStatisticsAsync` method (currently lines 81–121, immediately following
`GetPagedHistoryAsync` and immediately before the closing brace of the class):

```csharp
    public async Task<ClassificationStatistics> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var query = _context.ClassificationHistory.AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(h => h.Timestamp >= fromDate.Value);

        if (toDate.HasValue)
        {
            var endOfDay = toDate.Value.Date.AddDays(1);
            query = query.Where(h => h.Timestamp < endOfDay);
        }

        var totalProcessed = await query.CountAsync();
        var successCount = await query.CountAsync(h => h.Result == ClassificationResult.Success);
        var manualReviewCount = await query.CountAsync(h => h.Result == ClassificationResult.ManualReviewRequired);
        var errorCount = await query.CountAsync(h => h.Result == ClassificationResult.Error);

        var ruleUsage = await query
            .Where(h => h.ClassificationRuleId.HasValue && h.Result == ClassificationResult.Success)
            .Include(h => h.ClassificationRule)
            .GroupBy(h => new { h.ClassificationRuleId, h.ClassificationRule!.Name })
            .Select(g => new RuleUsageStatistic
            {
                RuleId = g.Key.ClassificationRuleId!.Value,
                RuleName = g.Key.Name,
                UsageCount = g.Count(),
                UsagePercentage = totalProcessed > 0 ? (decimal)g.Count() / totalProcessed * 100 : 0
            })
            .OrderByDescending(r => r.UsageCount)
            .ToListAsync();

        return new ClassificationStatistics
        {
            TotalInvoicesProcessed = totalProcessed,
            SuccessfulClassifications = successCount,
            ManualReviewRequired = manualReviewCount,
            Errors = errorCount,
            RuleUsage = ruleUsage
        };
    }
```

Leave the class's remaining methods (`AddAsync`, `GetHistoryAsync`, `GetHistoryByInvoiceIdAsync`,
`GetPagedHistoryAsync`), its `using` directives, and the class closing brace exactly as they are.
Do not modify `IClassificationHistoryRepository.cs` — the method was never declared there, so
there is no interface member to remove.

#### 7. Edit: `frontend/src/api/hooks/useInvoiceClassification.ts`

In the `CLASSIFICATION_QUERY_KEYS` object (around line 27–32), remove exactly this line:

```typescript
  statistics: ['invoice-classification', 'statistics'] as const,
```

Leave every other entry in `CLASSIFICATION_QUERY_KEYS` (`rules`, `ruleTypes`,
`accountingTemplates`, `history`, etc.) untouched, and leave all downstream usages of
`CLASSIFICATION_QUERY_KEYS.rules` / `.history` / etc. untouched — none of them reference
`.statistics`.

#### Files touched summary

| Action | File |
|---|---|
| Edit | `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/InvoiceClassificationMappingProfile.cs` |
| Delete | `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/ClassificationStatistics.cs` |
| Delete | `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/RuleUsageStatistic.cs` |
| Delete | `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/ClassificationStatisticsDto.cs` |
| Delete | `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/Contracts/RuleUsageStatisticDto.cs` |
| Edit | `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs` |
| Edit | `frontend/src/api/hooks/useInvoiceClassification.ts` |

No other files are in scope. Specifically do NOT touch: `IClassificationHistoryRepository.cs`,
`InvoiceClassificationModule.cs`, `ClassificationStats.tsx`, `InvoiceClassificationPage.tsx`,
or any EF migration/configuration file.

#### Acceptance criteria

1. **Zero-match grep (FR-6).** Each of the following returns no matches across
   `backend/src`, `backend/test`, and `frontend/src`:
   - `GetStatisticsAsync`
   - `ClassificationStatistics`
   - `RuleUsageStatistic`
   - `ClassificationStatisticsDto`
   - `RuleUsageStatisticDto`

   Example check:
   ```bash
   grep -rn -E "GetStatisticsAsync|ClassificationStatistics|RuleUsageStatistic|ClassificationStatisticsDto|RuleUsageStatisticDto" backend/src backend/test frontend/src
   ```
   must exit with no output (grep exit code 1 / empty result).

2. **Backend build.** `dotnet build` succeeds from the repo's backend solution/project root
   with zero errors and no new unused-`using` warnings in the two edited backend files
   (`InvoiceClassificationMappingProfile.cs`, `ClassificationHistoryRepository.cs`).

3. **Backend format.** `dotnet format` produces no diffs (or only whitespace normalization
   consistent with the surrounding file style) in the touched backend files.

4. **Backend tests.** The full existing backend test suite passes with no test file changes
   required — no test currently references any of the five removed symbols (verified during
   planning: zero hits for these symbols under `backend/test`).

5. **Frontend build.** `npm run build` succeeds with no errors.

6. **Frontend lint.** `npm run lint` passes with no new errors/warnings (in particular no
   "unused variable" warnings on `CLASSIFICATION_QUERY_KEYS`, since `.rules`/`.history`/etc.
   remain in use).

7. **Surgical scope.** `git diff --stat` (or equivalent) shows changes touching only the
   7 files listed in the "Files touched summary" table above — 4 deletions, 3 edits — and
   no other file in the repository is modified.
