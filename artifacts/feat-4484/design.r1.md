# Design: PackingMaterials daily-consumption date validation

## Component Design
- `GetDailyConsumptionBreakdownRequest` (class): `string? Date`, `ConsumptionGroupBy GroupBy = Material`.
- `GetDailyConsumptionBreakdownRequestValidator`: `RuleFor(x => x.Date).NotEmpty().WithMessage("Date is required in yyyy-MM-dd format.").Must(BeValidDate).WithMessage("Date must be in yyyy-MM-dd format.")` with `BeValidDate` = `DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)`. Use `.When`/cascade stop so only one message fires for empty input (`.Cascade(CascadeMode.Stop)`).
- `GetDailyConsumptionBreakdownHandler`: at top of `Handle`, `TryParseExact`; on failure return `new GetDailyConsumptionBreakdownResponse { Success = false, Error = "Invalid date format. Expected yyyy-MM-dd." }`; otherwise use `parsedDate` in the log, repository call and both `Date =` assignments.
- `PackingMaterialsModule`: add validator + `ValidationBehavior` registrations below the existing `GetConsumptionHistoryRequest` ones (add `using` for the new namespace).
- `PackingMaterialsController.GetDailyConsumptionBreakdown(GetDailyConsumptionBreakdownRequest request [FromQuery], CancellationToken)`; body is send + `Success ? Ok : BadRequest(new { error = response.Error })`. Remove `System.Globalization` using only if unused elsewhere in the file.

## Data Schemas
Request (query): `date: string (yyyy-MM-dd, required)`, `groupBy: ConsumptionGroupBy (default Material)`.
Response 200: unchanged. 400 invalid date: ProblemDetails `{ status: 400, title: "Validation Failed", errors: [{ propertyName: "Date", errorMessage }] }`.
