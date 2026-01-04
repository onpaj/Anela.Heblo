using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Services;

// NOTE: These tests are preserved as reference for future M2 implementation.
// They test SalesCostCalculationService which was replaced by SalesCostSource.
// Phase 1 uses stub implementation. Re-enable and adapt when implementing real M2 logic.
//
// Key concepts to preserve:
// - ILedgerService.GetDirectCosts(department: "SKLAD", "MARKETING")
// - Cost allocation by sales volume (SalesHistory.SumB2B + SalesHistory.SumB2C)
// - Per-unit cost calculation
// - Monthly cost aggregation
//
// Original test methods (commented out to avoid compilation errors):
// - GetCostsAsync_WithValidData_ReturnsCorrectCostAllocation
// - GetCostsAsync_WithEmptyProducts_ReturnsEmptyResult
// - GetCostsAsync_WithNoSalesHistory_ReturnsEmptyResult
// - GetCostsAsync_WithNoCosts_ReturnsEmptyResult
// - GetCostsAsync_WithSpecificProductCodes_ReturnsFilteredResults
// - GetCostsAsync_WithDateRange_FiltersCorrectly
// - IsLoaded_InitialState_ReturnsFalse
// - Reload_WithValidData_SetsIsLoadedTrue
// - Reload_WithNoData_DoesNotSetIsLoaded
// - Reload_WithException_DoesNotUpdateCacheAndThrows

/// <summary>
/// Reference tests for SalesCostCalculationService (M2 implementation)
/// </summary>
public class SalesCostCalculationServiceTests
{
    // Test bodies removed to prevent compilation errors with dynamic types
    // Refer to git history for full test implementation details
}
