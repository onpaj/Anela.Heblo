# Arch Review — feat-3404

## Assessment
This is a pure dead-code removal with no architectural implications:
- The two removed blocks were already completely inert (commented out).
- The interfaces they referenced (`ISalesCostCalculationService`, `IManufactureCostCalculationService`) are no longer registered or used anywhere in the codebase — removing the comments does not alter the DI graph.
- No downstream modules depend on these registration entries.
- No migration, schema, or API surface change is involved.

## Risks
None. The change reduces file size and cognitive overhead with zero runtime impact.

## Decision
Proceed with deletion.
