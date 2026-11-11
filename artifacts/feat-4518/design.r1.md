# Design: Coverage for GoogleAdsNegativeKeywordHandler

## Component Design
One new xUnit class `GoogleAdsNegativeKeywordHandlerTests` (namespace `Anela.Heblo.Adapters.GoogleAds.Tests.Execution`, `public sealed`). Each test builds `ExecutorHarness.SeededApi()` and `ExecutorHarness.Create(api)`, calls `ExecuteAsync`/`ReadCurrentAsync`/`RevertAsync`, and asserts with FluentAssertions. No production code is changed.

## Data Schemas
Inputs: `AdAction(AddNegativeKeyword, GoogleAds, TestSettings.CustomerId, level, targetId, Absent, Present, payload{Text, MatchType})`. Revert originals: `new AdExecutionResult(outcome, Absent, Present, resourceName, "{}", null)`. Expected mutation body: `operations[0].create.{campaign|adGroup, negative:true, keyword{text, matchType UPPER}}`; revert body `operations[0].remove = <resourceName>`.
