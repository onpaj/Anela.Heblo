## Module / File
`backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/UseCases/GetTaskStatus/GetTaskStatusHandler.cs`

## Coverage
Line coverage: 14.3% (filter threshold: 60%)

## What's not tested
The handler has two branches that are unexercised: (1) when the task is not found in the registry, it returns a response with `Found=false` and no `Status` field populated — no test asserts this contract; (2) when the task exists but has no recorded `LastExecution`, the DTO's `LastExecution` field is null — no test verifies the null-safe mapping. A regression where `Found=false` is not set, or where a null `LastExecution` causes a null-reference exception, would be invisible.

## Why it matters
Callers use `Found=false` to distinguish "task not registered" from "task registered but never run". If this flag is never asserted, a refactor that accidentally always returns `Found=true` would silently break the API contract consumed by the background-refresh status page.

## Suggested approach
Unit tests: (1) task not in registry → assert `Found=false`, no exception; (2) task in registry with null `LastExecution` → assert `Found=true` and `LastExecution=null` in DTO; (3) happy path with populated `LastExecution`. ~1h effort.

---
_Filed by weekly coverage-gap routine on 2026-09-14. Based on CI run #34699120372 (722ec6efc4c1f7e5db235606c763a2f2b4a9a374)._
