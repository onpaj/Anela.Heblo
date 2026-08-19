## Module
Transport Boxes (Logistics)

## Finding
Every handler in this part injects `TimeProvider` and calls `_timeProvider.GetUtcNow()` — confirmed in `AddItemToBoxHandler`, `RemoveItemFromBoxHandler`, `ChangeTransportBoxStateHandler`, `OpenOrResumeBoxByCodeHandler`, and `CreateNewTransportBoxHandler` (all take a `TimeProvider` constructor parameter). `TransportBoxCompletionService` (`backend/src/Anela.Heblo.Application/Features/Logistics/Services/TransportBoxCompletionService.cs`) is the one class in the part that doesn't: it has no `TimeProvider` dependency and calls `DateTime.UtcNow` directly three times — `:91` (`box.Error(...)`), `:111` (`box.ToPick(...)`), `:131` (`box.Error(...)`). Its test suite (`backend/test/Anela.Heblo.Tests/Features/Logistics/Services/TransportBoxCompletionServiceTests.cs`) has no time-mocking correspondingly and runs against the real wall clock.

## Why it matters
This service is recurring background work that writes timestamped `TransportBoxStateLog` entries the rest of the module treats as deterministic and controllable via `TimeProvider`. It can't be time-travel-tested like its siblings, and it's the one place in the part where "when did this transition actually happen" can silently diverge from a test's or a future debugging session's injected/faked clock.

## Suggested direction
Inject `TimeProvider` into `TransportBoxCompletionService` and replace the three `DateTime.UtcNow` call sites, matching the pattern already used by every handler in this part.

