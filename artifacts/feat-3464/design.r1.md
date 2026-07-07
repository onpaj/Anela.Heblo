# Design: Extract `ITimeWindowParser` interface in Analytics module

## Component Design

### `ITimeWindowParser` (new interface)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs` (colocated with its implementation, matching the existing `IMarginCalculator`/`MarginCalculator` pattern in `MarginCalculator.cs`).
- **Namespace:** `Anela.Heblo.Application.Features.Analytics.Services`.
- **Responsibility:** Abstraction for converting a named time-window string (e.g. `"last-6-months"`) into a concrete `(fromDate, toDate)` range.
- **Contract:**
  ```csharp
  public interface ITimeWindowParser
  {
      (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
  }
  ```
  - `timeWindow`: named time-window token, same accepted values/format as today.
  - Returns: value tuple with named elements `fromDate` and `toDate` (inclusive of existing semantics — unchanged).
  - No new exceptions or error-handling contract introduced; existing behavior for unrecognized input is preserved as-is.

### `TimeWindowParser` (existing class, modified)
- **Responsibility:** Unchanged — resolves "now" via injected `TimeProvider` and computes the date range for a given time-window token.
- **Change:** Class declaration becomes `public class TimeWindowParser : ITimeWindowParser`. Constructor signature, field(s), and `ParseTimeWindow` method body are unchanged.

### `AnalyticsModule` (DI composition root, modified)
- **Responsibility:** Unchanged — module-level service registration for Analytics.
- **Change:** Replace the concrete-type registration with an interface-to-implementation registration, consistent with sibling registrations in the same module:
  ```csharp
  services.AddScoped<ITimeWindowParser, TimeWindowParser>();
  ```
  - Lifetime stays `Scoped`.
  - No forwarding/dual registration; nothing else resolves `TimeWindowParser` by concrete type post-change.
  - No other lines in `AnalyticsModule.cs` are touched.

### `GetProductMarginSummaryHandler` (consumer, modified)
- **Responsibility:** Unchanged — orchestrates margin summary retrieval using `IAnalyticsRepository`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`, and the time-window parser.
- **Change:** Constructor-injected dependency type changes from concrete `TimeWindowParser` to `ITimeWindowParser`:
  ```csharp
  public GetProductMarginSummaryHandler(
      IAnalyticsRepository analyticsRepository,
      IMarginCalculator marginCalculator,
      IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
      ITimeWindowParser timeWindowParser)
  ```
  - Private field: `private readonly ITimeWindowParser _timeWindowParser;`
  - Call site `_timeWindowParser.ParseTimeWindow(request.TimeWindow)` and its role in the `Handle` method's data flow (feeding `DateRange`, the repository query, and the calculators) are unchanged.

### `GetProductMarginSummaryHandlerTests` (test, optionally modified)
- **Responsibility:** Unchanged test intent — exercises the handler using a real `TimeWindowParser` instance (no mocking framework introduced).
- **Change:** The test continues to do `new TimeWindowParser(timeProvider)` and pass the result wherever `ITimeWindowParser` is expected. The field type may remain `TimeWindowParser` or be updated to `ITimeWindowParser` — both compile since `TimeWindowParser` implements the new interface. No assertions or test cases change.

### Component interaction (unchanged data flow, new compile-time seam)

```
AnalyticsModule.cs (DI composition root)
        │
        │  services.AddScoped<ITimeWindowParser, TimeWindowParser>();
        ▼
GetProductMarginSummaryHandler
        │  private readonly ITimeWindowParser _timeWindowParser;
        │  _timeWindowParser.ParseTimeWindow(request.TimeWindow)
        ▼
ITimeWindowParser  (interface, Features/Analytics/Services/TimeWindowParser.cs)
        △
        │  implements
TimeWindowParser  (concrete class, same file, depends on TimeProvider — unchanged)
```

## Data Schemas

No persistent, transient, or wire-format data model changes.

- **`ParseTimeWindow` return shape** (unchanged): value tuple `(DateTime fromDate, DateTime toDate)`. Not serialized, not exposed via any DTO.
- **MediatR contracts** (`GetProductMarginSummaryRequest` / `GetProductMarginSummaryResponse`): unchanged.
- **HTTP / OpenAPI surface**: unchanged. `ITimeWindowParser` is an internal Application-layer abstraction; it never appears in a request/response DTO and is invisible to the generated TypeScript client.
- **DI registration shape** (composition metadata, not a data schema, but the only "shape" change in this refactor):
  - Before: `services.AddScoped<TimeWindowParser>();`
  - After: `services.AddScoped<ITimeWindowParser, TimeWindowParser>();`
