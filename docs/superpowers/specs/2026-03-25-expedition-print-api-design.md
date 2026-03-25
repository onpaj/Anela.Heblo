# Expedition Print — API-Based Redesign

**Date:** 2026-03-25
**Status:** Approved
**Branch:** feature/expedition_list

## Summary

Replace the Playwright UI-scraping approach for generating expedition picking list PDFs with a direct Shoptet REST API integration. PDF generation moves from Playwright's HTML-to-PDF export to QuestPDF (code-first, no browser dependency).

Everything downstream of `IPickingListSource` — the print queue sinks (Azure, CUPS, Combined), the scheduled job, the service layer, and all configuration — remains unchanged.

---

## Architecture & Components

All new code lives in `Anela.Heblo.Adapters.ShoptetApi` (already scaffolded as a stub project).

### `ShoptetApiOrdersClient`

Thin HTTP client wrapping the Shoptet REST API. Registered as a typed `HttpClient`. Sets `Authorization: Bearer {ApiToken}` on every request.

**Methods:**
- `GetOrdersByStateAsync(int stateId, int page)` → paginated order list (code, shipping method ID, customer info)
- `GetOrderDetailAsync(string code)` → full order with items (name, variant, warehouse position, stock count, price)
- `UpdateOrderStateAsync(string code, int stateId)` → PATCH to transition order state

### `ExpeditionProtocolDocument`

QuestPDF `IDocument` implementation. Receives `ExpeditionProtocolData` (carrier display name + up to 8 fully-loaded orders). Renders the complete PDF including barcode per order (MUST) and the aggregated summary page.

### `ShoptetApiExpeditionListSource`

Implements `IPickingListSource`. Orchestrates: fetch → group by carrier → paginate → render PDF → optionally change order states → return file paths.

---

## Data Models

```csharp
public class ShoptetApiOptions
{
    public string BaseUrl { get; set; }    // e.g. https://api.myshoptet.com
    public string ApiToken { get; set; }   // static bearer token
}

record ExpeditionProtocolData(
    string CarrierDisplayName,
    List<ExpeditionOrder> Orders   // max 8
);

record ExpeditionOrder(
    string Code,
    string CustomerName,
    string Address,
    string Phone,
    List<ExpeditionOrderItem> Items
);

record ExpeditionOrderItem(
    string ProductCode,
    string Name,
    string Variant,
    string WarehousePosition,
    int Quantity,
    int StockCount,
    decimal UnitPrice
);
```

---

## Data Flow

```
PrintPickingListJob (cron: 0 4,11 * * *)
  → ExpeditionListService.PrintPickingListAsync(request)
    → IPickingListSource.CreatePickingList(request)   [ShoptetApiExpeditionListSource]
        1. ShoptetApiOrdersClient.GetOrdersByStateAsync(sourceStateId)
           - Paginate until exhausted
           - Filter to carriers requested in PrintPickingListRequest

        2. Group by shipping method ID → Carriers enum
           (same 17-ID mapping as previous Playwright implementation)
           - Zasilkovna: 8 shipping method variants
           - PPL: 6 variants
           - GLS: 2 variants
           - Osobak: 1 variant (pageSize = 1 per batch)

        3. For each carrier × batch of up to 8 orders:
           a. ShoptetApiOrdersClient.GetOrderDetailAsync(code) for each order
           b. ExpeditionProtocolDocument.Generate() → write PDF to temp folder
           c. Filename: {timestamp}_{carrier}_{shippingId}_{page}.pdf

        4. If request.ChangeOrderState:
           ShoptetApiOrdersClient.UpdateOrderStateAsync(code, desiredStateId)
           for every processed order

        5. Return PrintPickingListResult { ExportedFiles, TotalCount }

    → IPrintQueueSink.SendAsync(files)   [CombinedPrintQueueSink: Azure + CUPS]
    → SendGrid email (if recipients configured)
    → Cleanup temp files
```

---

## PDF Structure

Produced by `ExpeditionProtocolDocument` via QuestPDF.

### Per-order pages (up to 8 orders per document)

```
Title: "Objednávky k expedici – {CarrierDisplayName}"

── Per order (repeated) ────────────────────────────────────
  "Objednávka {code}"
  [Code128 Barcode of order code]                    ← MANDATORY
  Customer: Name, Street, PostCode City   Phone      (right-aligned)

  Table:
  Kód | Popis položky                    | Množství | Stav skladu | Cena za m.j. | Zkompletováno
      | (name)                           |          |             |              | [ ] checkbox
      | Varianta: {variant}              |          |             |              |
      | Pozice ve skladu: {position}     |          |             |              |
```

### Summary page ("Položky objednávek") — always last page

```
Table (aggregated across all orders in the batch):
Kód | Popis položky                    | Množství | Stav skladu | Skladové nároky | Reálný stav
    | (name, Varianta, Pozice)         | (summed) |             | (other demands) | (stock+demands)
```

Quantities are summed per product code across all orders in the batch. Warehouse demands and real stock come directly from the Shoptet order detail response.

---

## Configuration

**New section in appsettings.json:**
```json
{
  "ShoptetApi": {
    "BaseUrl": "https://api.myshoptet.com",
    "ApiToken": "..."
  }
}
```

**Unchanged:**
```json
{
  "ExpeditionList": {
    "SourceStateId": 73,
    "DesiredStateId": 26,
    "ChangeOrderStateByDefault": true,
    "SendToPrinterByDefault": false,
    "DefaultEmailRecipients": [],
    "PrintSink": "Combined"
  }
}
```

---

## DI Registration

In `HebloShoptetApiAdapterModule.cs` (new module in ShoptetApi project):

```csharp
services.Configure<ShoptetApiOptions>(configuration.GetSection("ShoptetApi"));
services.AddHttpClient<ShoptetApiOrdersClient>();
services.AddScoped<IPickingListSource, ShoptetApiExpeditionListSource>();
```

Remove from `HebloShoptetAdapterModule.cs` (existing Shoptet adapter):
```csharp
// services.AddScoped<IPickingListSource, ShoptetPlaywrightExpeditionListSource>();
```

The `ShoptetApi` project must be added to the solution file and referenced from `Anela.Heblo.API`.

---

## Deletions

| File | Action |
|------|--------|
| `Adapters.Shoptet/Playwright/ShoptetPlaywrightExpeditionListSource.cs` | Delete |
| `Adapters.Shoptet/Playwright/Scenarios/PrintPickingListScenario.cs` | Delete |

All other Playwright scenarios (stock taking, stock up, invoice export, cash register) are unaffected.

---

## Testing

### `ShoptetApiExpeditionListSource` unit tests
- Mock `ShoptetApiOrdersClient` returns fixed orders → verify carrier grouping correctness
- Verify batch splitting: 8 orders per batch (1 for Osobak)
- Verify file naming pattern: `{timestamp}_{carrier}_{shippingId}_{page}.pdf`
- Verify `UpdateOrderStateAsync` called per order when `ChangeOrderState = true`
- Verify `UpdateOrderStateAsync` NOT called when `ChangeOrderState = false`

### `ExpeditionProtocolDocument` unit tests
- `Document.GeneratePdf()` does not throw with valid sample data
- Summary page aggregates quantities correctly across multiple orders with shared product codes
- Barcode renders without exception

### Integration tests
None against live Shoptet API — consistent with existing policy for other Shoptet adapter clients.
