# Design: Validate `TimeWindow` on GetProductMarginSummaryRequest

## Component Design

### `TimeWindowParser` (modified)
`backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`

Responsibility unchanged: parse a `TimeWindow` string literal into a `(fromDate, toDate)` range. Gains one new public static member so the allowed-value set has a single source of truth (per arch-review Decision 1):

```csharp
public class TimeWindowParser : ITimeWindowParser
{
    public static readonly string[] SupportedTimeWindows =
        ["current-year", "current-and-previous-year", "last-6-months", "last-12-months", "last-24-months"];

    // ParseTimeWindow(...) switch body unchanged, including its
    // ArgumentException fallback for any other caller that bypasses
    // the MediatR pipeline.
}
```
`ITimeWindowParser` (the interface) is **not** changed — `SupportedTimeWindows` is a static field on the concrete class, not an instance member, so it carries no per-instance state and needs no DI mock surface.

### `GetProductMarginSummaryRequestValidator` (new)
`backend/src/Anela.Heblo.Application/Features/Analytics/Validators/GetProductMarginSummaryRequestValidator.cs`

Responsibility: reject any `GetProductMarginSummaryRequest` whose `TimeWindow` is not in `TimeWindowParser.SupportedTimeWindows`, following the exact idiom of the sibling `GetMarginReportRequestValidator` (error code as string via `WithErrorCode`, offending value carried in `Params` via `WithState`, human-readable `WithMessage` last).

```csharp
public class GetProductMarginSummaryRequestValidator : AbstractValidator<GetProductMarginSummaryRequest>
{
    public GetProductMarginSummaryRequestValidator()
    {
        RuleFor(x => x.TimeWindow)
            .Must(v => TimeWindowParser.SupportedTimeWindows.Contains(v))
            .WithErrorCode(((int)ErrorCodes.InvalidTimeWindow).ToString())
            .WithState(x => (object)new Dictionary<string, string>
            {
                { "timeWindow", x.TimeWindow }
            })
            .WithMessage(x => string.Format(
                AnalyticsConstants.ValidationMessages.INVALID_TIME_WINDOW,
                x.TimeWindow,
                string.Join(", ", TimeWindowParser.SupportedTimeWindows)));
    }
}
```

Comparison is case-sensitive and exact (`string[].Contains` with default `Ordinal` comparer), matching `TimeWindowParser`'s `switch` — no `.Trim()`/`.ToLowerInvariant()` normalization is introduced on either side, so a value the parser would reject is never accepted by the validator and vice versa.

### `AnalyticsModule` (modified)
`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`

Two new registrations added immediately alongside the existing sibling pair (no reordering of existing lines):

```csharp
services.AddScoped<IValidator<GetProductMarginSummaryRequest>, GetProductMarginSummaryRequestValidator>();
// ...
services.AddScoped<IPipelineBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>,
    ValidationResultBehavior<GetProductMarginSummaryRequest, GetProductMarginSummaryResponse>>();
```

`ValidationResultBehavior<TRequest, TResponse>` itself is reused unmodified — no component change there.

### `AnalyticsController` (no change)
`GetProductMarginSummary` already calls `HandleResponse(response)`, which already maps `[HttpStatusCode(...)]`-tagged `ErrorCode`s to the correct status. No controller code changes; behavior change is entirely upstream in the MediatR pipeline.

## Data Schemas

### `ErrorCodes` enum addition
`backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs`, appended to the existing "Analytics module errors (17XX)" block:

```csharp
[HttpStatusCode(HttpStatusCode.BadRequest)]
InvalidTimeWindow = 1706,
```

### `AnalyticsConstants.ValidationMessages` addition
`backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsConstants.cs`:

```csharp
public const string INVALID_TIME_WINDOW = "Invalid time window: '{0}'. Supported values: {1}";
```

### API response shape (no new DTO — existing shape, now actually reachable for this cause)
`GET /api/analytics/product-margin-summary?timeWindow=bogus` → HTTP 400:

```json
{
  "success": false,
  "errorCode": 1706,
  "params": {
    "timeWindow": "bogus"
  }
}
```

(Exact JSON property casing/shape follows whatever `BaseResponse`/`GetProductMarginSummaryResponse` already serializes to for the two sibling endpoints today — no change to the response DTO's shape, field names, or serialization settings.)

Success path (`timeWindow` in the supported set) is completely unchanged: same `GetProductMarginSummaryResponse` payload as before this change.
