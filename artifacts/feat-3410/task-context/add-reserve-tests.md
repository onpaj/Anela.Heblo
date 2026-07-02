### task: add-reserve-tests

**Goal:** Add three `[Fact]` test methods to `ChangeTransportBoxStateHandlerTests` covering:
1. `Opened→Reserve` with null Location returning `TransportBoxStateChangeError` (transition condition guard)
2. `Opened→Reserve` with valid Location succeeding
3. `Reserve→Received` aggregating items by ProductCode into a single stock-up operation

**File to modify:**
`backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

See full task details in `artifacts/feat-3410/task-plan.r1.md`.
