# Design: Sales multiplier cookie tests

## Component Design
Single new `describe("sales multiplier cookie validation", ...)` inside the existing suite in `ManufacturingStockAnalysis.test.tsx`.

- `COOKIE = "manufacturing-stock-sales-multiplier"`
- `setCookieRaw(value)`: `document.cookie = \`${COOKIE}=${value}; path=/; max-age=31536000\``
- `clearAll()`: expire the cookie plus any helper cookies (`a`, `b`, `x-<COOKIE>`) with `max-age=0; path=/`; run in `beforeEach` and `afterEach`.
- `renderPage()`: set `mockUseManufacturingStockAnalysisQuery.mockReturnValue({data: mockData, isLoading:false, error:null, refetch: jest.fn()})` then render with `createWrapper()`.

Cases:
1. `it.each([["0.1","0.1x"],["3.0","3.0x"],["1.5","1.5x"]])` accepted values.
2. `it.each(["0.09","3.01","0","-1","100"])` -> "1.0x".
3. `it.each(["abc","NaN",""])` -> "1.0x".
4. Multiple cookies: `a=1`, target `2.5`, `b=2` -> "2.5x".
5. Prefix-named cookie `x-<COOKIE>=2.5` only -> "1.0x".
6. Hook arg: valid 2.5 -> `expect.objectContaining({salesMultiplier: 2.5})`; invalid -> `salesMultiplier: 1`.
7. (documenting) `1.5=junk` -> "1.5x".

## Data Schemas
None.
