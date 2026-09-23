### task: surface-validation-message-in-frontend

**Files:**
- Modify: `frontend/src/api/hooks/useScanPackingOrder.ts:101-131`
- Test: `frontend/src/api/hooks/__tests__/useScanPackingOrder.test.ts`

Depends on: `regenerate-frontend-api-client`, `map-validation-exception-to-error-code` (for the fixed `"ShoptetMessage"` `Params` key name).

- [ ] **Step 1: Write the failing tests**

Append to `useScanPackingOrder.test.ts`, directly after the existing `'throws the curated Czech message for a known business error code'` test:

```typescript
it('throws an actionable message naming the missing address fields for ShipmentValidationFailed', async () => {
  mockPackaging_ScanOrder.mockResolvedValue({
    success: false,
    errorCode: 'ShipmentValidationFailed',
    params: { ShoptetMessage: 'Invalid recipient of order, missing fields: city, zip.' },
  });

  const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
  result.current.mutate({ orderCode: '126020133' });

  await waitFor(() => expect(result.current.isError).toBe(true));
  expect(result.current.error?.message).toBe(
    'Adresu příjemce nelze použít pro vytvoření zásilky — opravte v Shoptetu: Invalid recipient of order, missing fields: city, zip..',
  );
});

it('throws the base actionable message for ShipmentValidationFailed when no detail is present', async () => {
  mockPackaging_ScanOrder.mockResolvedValue({
    success: false,
    errorCode: 'ShipmentValidationFailed',
  });

  const { result } = renderHook(() => useScanPackingOrder(), { wrapper });
  result.current.mutate({ orderCode: '126020133' });

  await waitFor(() => expect(result.current.isError).toBe(true));
  expect(result.current.error?.message).toBe(
    'Adresu příjemce nelze použít pro vytvoření zásilky (chybí povinné údaje) — opravte ji v Shoptetu.',
  );
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd frontend && npx react-scripts test src/api/hooks/__tests__/useScanPackingOrder.test.ts --watchAll=false`
Expected: FAIL — both new tests get the generic `'Chyba při skenování objednávky.'` message today, since `ShipmentValidationFailed` isn't in `SCAN_ERROR_MESSAGES` and `params` is discarded by the current `toMessage` callback.

- [ ] **Step 3: Implement the message mapping**

In `useScanPackingOrder.ts`, replace:

```typescript
const SCAN_ERROR_MESSAGES: Partial<Record<string, string>> = {
  ShoptetOrderNotFound: 'Objednávka nebyla nalezena.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit pro tuto objednávku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit zásilku — zkuste znovu.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
  PackingUserNotEligible: 'Vybraný balič není aktivní nebo nemá oprávnění balit. Vyberte baliče znovu.',
};

const GENERIC_SCAN_ERROR = 'Chyba při skenování objednávky.';
```

with:

```typescript
const SCAN_ERROR_MESSAGES: Partial<Record<string, string>> = {
  ShoptetOrderNotFound: 'Objednávka nebyla nalezena.',
  ShipmentCarrierNotResolved: 'Dopravce se nepodařilo určit pro tuto objednávku.',
  ShipmentCreationFailed: 'Shoptet nemohl vytvořit zásilku — zkuste znovu.',
  ShipmentOrderWeightUnavailable: 'Nelze zjistit hmotnost objednávky.',
  PackingUserNotEligible: 'Vybraný balič není aktivní nebo nemá oprávnění balit. Vyberte baliče znovu.',
};

// Shoptet permanently rejected the recipient/shipment data (e.g. missing city/zip) — this is
// not a "try again" condition, so the wording explicitly says to fix the order in Shoptet.
// When the backend forwards the Shoptet message (Params["ShoptetMessage"] — see
// ShipmentCreationService.CreateAndPersistAsync), it's appended so the operator sees exactly
// which fields are missing instead of only the generic category.
const SHIPMENT_VALIDATION_FAILED_BASE =
  'Adresu příjemce nelze použít pro vytvoření zásilky (chybí povinné údaje) — opravte ji v Shoptetu.';

const GENERIC_SCAN_ERROR = 'Chyba při skenování objednávky.';
```

The detailed and base messages are two independently-worded sentences (the detailed variant drops "ji" for grammatical fit with the appended clause), not one derived from the other — add this helper next to `SHIPMENT_VALIDATION_FAILED_BASE`:

```typescript
const shipmentValidationFailedDetailed = (detail: string) =>
  `Adresu příjemce nelze použít pro vytvoření zásilky — opravte v Shoptetu: ${detail}.`;
```

Then replace the `toMessage` callback passed to `callApi`:

```typescript
  const response = await callApi(
    () =>
      apiClient.packaging_ScanOrder(
        orderCode,
        numberOfPackages,
        new ScanOrderBody({ packingUserId: packingUserId ?? undefined }),
      ),
    ({ errorCode }) => (errorCode && SCAN_ERROR_MESSAGES[errorCode]) ?? GENERIC_SCAN_ERROR,
  );
```

with:

```typescript
  const response = await callApi(
    () =>
      apiClient.packaging_ScanOrder(
        orderCode,
        numberOfPackages,
        new ScanOrderBody({ packingUserId: packingUserId ?? undefined }),
      ),
    ({ errorCode, params }) => {
      if (errorCode === 'ShipmentValidationFailed') {
        const detail = params?.ShoptetMessage;
        return detail ? shipmentValidationFailedDetailed(detail) : SHIPMENT_VALIDATION_FAILED_BASE;
      }
      return (errorCode && SCAN_ERROR_MESSAGES[errorCode]) ?? GENERIC_SCAN_ERROR;
    },
  );
```

This wording matches Step 1's test expectations verbatim — treat the test assertions there as the source of truth for the exact literal strings.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd frontend && npx react-scripts test src/api/hooks/__tests__/useScanPackingOrder.test.ts --watchAll=false`
Expected: PASS (all tests in the file, new and pre-existing).

- [ ] **Step 5: Lint and typecheck**

Run: `cd frontend && npm run lint && npm run build`
Expected: no lint errors; build succeeds (the build step also re-runs `generate-client`, which should be a no-op diff after the previous task already regenerated it — if it isn't, investigate before committing).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useScanPackingOrder.ts frontend/src/api/hooks/__tests__/useScanPackingOrder.test.ts
git commit -m "feat(packaging): show actionable Shoptet validation message instead of generic retry text"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (distinguish permanent validation failure) → `parse-shoptet-422-in-shipment-client`.
- FR-2 (new non-retryable error code + message in Params) → `add-validation-exception-and-error-code` + `map-validation-exception-to-error-code`.
- FR-3 (actionable operator message) → `regenerate-frontend-api-client` + `surface-validation-message-in-frontend`.
- NFR-1 (no behavior change elsewhere) → every task's test-run steps explicitly re-run the full surrounding test file/module, not just the new test, and `forward-validation-params-through-scan-response` explicitly re-runs the Packaging module including `ResetOrderShipment*` tests, which are deliberately left unmodified.
- NFR-2 (observability parity) → `map-validation-exception-to-error-code` Step 4 keeps a `_logger` call on the new branch (Warning instead of Error, with rationale noted inline).
- Data Model / API design / Dependencies / Out of Scope sections of the spec describe no additional net-new work beyond what's covered above.

**2. Placeholder scan:** No TBD/TODO, no "add appropriate error handling" hand-waving — every step has literal code or an exact command. The one edit in `forward-validation-params-through-scan-response`'s Step 3 (optional parameter with `= null` default) is intentionally minimal-diff rather than a new overload, to avoid touching other `ScanPackingOrderResponse(ErrorCodes)` call sites unnecessarily.

**3. Type consistency:** `ShoptetShipmentValidationException(orderCode, shoptetErrorCode, message, instance)`'s constructor signature is used identically in `parse-shoptet-422-in-shipment-client` (throw site) and `map-validation-exception-to-error-code` (test construction site). `ShipmentCreationResult.Params` (`Dictionary<string, string>?`) is used identically in the service, the handler, and the handler's test. The `"ShoptetMessage"` `Params` key is used identically in the backend (`ShipmentCreationService`), its test, and the frontend (`useScanPackingOrder.ts`) and its test — this is the one value that had to be kept in lockstep across tasks/files per the design doc, and it is spelled identically (`"ShoptetMessage"`) in all four places above.
