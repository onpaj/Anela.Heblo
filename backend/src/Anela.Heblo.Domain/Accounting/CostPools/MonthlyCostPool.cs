namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Total spend for one cost pool in one calendar month.
/// Internal domain type - never crosses the OpenAPI boundary, so a record
/// is allowed here (see CLAUDE.md: DTOs are classes, domain types may be records).
/// </summary>
/// <param name="Month">First day of the calendar month, at midnight.</param>
/// <param name="Pool">Which pool this total belongs to.</param>
/// <param name="Amount">Total spend in CZK. Zero when there was no spend.</param>
public record MonthlyCostPool(DateTime Month, CostPool Pool, decimal Amount);
