# Design: computePercentage unit tests

## Component Design
No UI. The only unit under test is the pure function `computePercentage` (exported from `ManufactureBatchCalculator.tsx`). Tests live in the existing `describe('computePercentage helper')` block of `__tests__/ManufactureBatchCalculator.test.tsx`. Expected case table (all present today): (184.5,1000)->'18.45%'; (500,500)->'100.00%'; (100,0|null|undefined|NaN|-500|Infinity|-Infinity)->'N/A'; (0,1000)->'0.00%'; (1,3)->'33.33%'; (-50,1000)->'-5.00%'.

## Data Schemas
None.
