# fix-comments r1

Reworded misleading streaming/memory comments in MarginCalculator.cs, GetProductMarginSummaryHandler.cs (class summary, step 3 comment, GenerateTopProducts summary) and AnalyticsRepository.cs (class and method summaries). Comment lines only; wording follows design.r1.md.

Verification: dotnet build of Application project (0 errors), dotnet format --verify-no-changes on Analytics folder (clean), dotnet test Features.Analytics (153 passed).
