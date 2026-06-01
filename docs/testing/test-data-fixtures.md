# Test Data Fixtures

This document contains a reference list of test data available in the development/staging environment for E2E testing.

**Last Updated:** 2026-01-25
**Environment:** Development (localhost:3000 / localhost:5000)
**Data Source:** Extracted via Playwright browser automation

**⚠️ SECURITY NOTE:** Supplier names and other sensitive business information have been anonymized using placeholders (Supplier A, Supplier B, etc.) to protect confidential business relationships.

---

## Table of Contents

- [Catalog Items (Products & Materials)](#catalog-items-products--materials)
- [Purchase Orders](#purchase-orders)
- [Manufacturing Orders](#manufacturing-orders)
- [Transport Boxes](#transport-boxes)
- [Suppliers](#suppliers)
- [Product Types](#product-types)
- [Usage Guidelines](#usage-guidelines)

---

## Catalog Items (Products & Materials)

**Total Items:** 906 items in catalog

### Sample Materials (AKL Series)

| Product Code | Product Name | Type | Available Stock | MOQ | Supplier |
|--------------|--------------|------|-----------------|-----|----------|
| AKL001 | Bisabolol | Materiál | 2762.39 | 1500g | Supplier MH |
| AKL003 | Dermosoft Eco 1388 | Materiál | 47033.34 | 25000g | - |
| AKL007 | Glycerol 99% Ph.Eur | Materiál | 47692.24 | 28500g | - |
| AKL009 | Hyacolor TM | Materiál | 500 | - | - |
| AKL011 | Pentylen Glykol Green+ | Materiál | 80773.31 | 25000g | - |
| AKL012 | Soda Bicarbona | Materiál | 86653.3 | 25000g | - |
| AKL014 | Triethylcitrát | Materiál | 7745.54 | 1000g | - |
| AKL018 | Vitamín E Tocopherol | Materiál | 17105.92 | 25000g | - |
| AKL019 | Xperse (dispergovaný oxid zinečnatý) | Materiál | 39263.64 | 35000g | - |
| AKL020 | Arrowroot škrob BIO | Materiál | 17767.83 | 25000g | - |
| AKL021 | Oxid zinečnatý | Materiál | 11485.84 | 1000g | - |
| AKL023 | DERMOSOFT GMCY - Glyceryl Caprylate | Materiál | 7647.85 | 1000g | - |
| AKL027 | Demineralizovaná voda | Materiál | 16412.92 | - | - |
| AKL028 | HA-Micro (OligoHyaferre) | Materiál | 1557.46 | 500g | - |
| AKL029 | HA-Oligo (HyActive) | Materiál | 1045.46 | 500g | - |
| AKL032 | Perfect - C ester | Materiál | 1667.58 | 1000g | - |
| AKL037 | Laktát sodný 60% | Materiál | 1274 | 1000g | - |
| AKL038 | Pentavitin | Materiál | 10393.63 | 10000g | - |
| AKL039 | Koloidní oves | Materiál | 26733.99 | 5000g | - |
| AKL040 | Sclerotium | Materiál | 1430.39 | 1000g | - |

### Sample Semi-Products (MAS Series)

| Product Code | Product Name | Type | Variant Count |
|--------------|--------------|------|---------------|
| MAS001001M | Hedvábný pan Jasmín | Polotovar | 4 variants |

**Required for batch planning tests:**
- MAS001001M has 10 products configured in its recipe/composition
- Minimum 2 products needed for error handling tests
- Used in: `manufacturing/batch-planning-error-handling.spec.ts`

### Sample Products (Finished Goods)

| Product Code | Product Name | Type | Available Stock | Notes |
|--------------|--------------|------|-----------------|-------|
| DAR001 | Dárkové balení | Produkt | - | Has margins data for testing |
| DAR0010 | Dárkové balení mini | Produkt | - | - |
| DEO001005 | Důvěrný pan Jasmín 5ml | Produkt | 67 | Has margins and sales data |

### Key Product Details (Example: AKL001 - Bisabolol)

```yaml
Product Code: AKL001
Product Name: Bisabolol
Type: Materiál
Stock:
  Available: 2762.39
  Shoptet: 0
  Transport: 0
  ABRA: 2762.39
  Reserved: 0
MOQ: 1500g
MMQ: 0
Supplier: "Supplier MH"
Purchase Price: [REDACTED]
Properties:
  Optimal Stock Days: 365
  Min Stock: 1000
  Batch Size: -
  Manufacturing Complexity: Nenastaveno
Consumption:
  Total Year: 2301.99
  Monthly Average: 191.83
```

---

## Purchase Orders

**Total Orders:** 8 active purchase orders

### Draft Orders

| Order Number | Supplier | Order Date | Status | Invoice | Total Amount | Items |
|--------------|----------|------------|--------|---------|--------------|-------|
| PO20251119-0855 | Supplier A | 19. 11. 2025 | Návrh | Ne | [REDACTED] | 1 |
| PO20251119-1128 | Supplier B | 19. 11. 2025 | Návrh | Ne | [REDACTED] | 3 |

### In Transit Orders

| Order Number | Supplier | Order Date | Status | Invoice | Total Amount | Items |
|--------------|----------|------------|--------|---------|--------------|-------|
| PO20251113-0850 | Supplier C | 13. 11. 2025 | V přepravě | Ano | [REDACTED] | 5 |
| PO20251113-1251 | Supplier D | 13. 11. 2025 | V přepravě | Ne | [REDACTED] | 2 |
| PO20251112-1051 | Supplier E | 12. 11. 2025 | V přepravě | Ne | 0,00 Kč | 0 |
| PO20251112-1408 | Supplier F | 12. 11. 2025 | V přepravě | Ne | [REDACTED] | 3 |
| PO20251107-0918 | Supplier G | 7. 11. 2025 | V přepravě | Ne | [REDACTED] | 3 |
| PO20250922-0958 | Supplier A | 22. 9. 2025 | V přepravě | Ne | [REDACTED] | 2 |

---

## Manufacturing Orders

**Total Orders:** Multiple orders (mostly historical/completed)

### Active/Recent Orders

| Order Number | Status | Production Date | ERP # (Semi) | ERP # (Product) | Product | Variants |
|--------------|--------|-----------------|--------------|-----------------|---------|----------|
| MO-2026-005 | Návrh | 24. 1. 2029 | - | - | Hedvábný pan Jasmín (MAS001001M) | 4 |

**Note:** Manufacturing order MO-2026-005 has a future date (2029) which may be a test data anomaly.

---

## Transport Boxes

**Total Boxes:** 2902 boxes
**Active Boxes:** 837 boxes

### Box State Distribution

| State | Count |
|-------|-------|
| Celkem | 2902 |
| Aktivní | 837 |
| Nový | 0 |
| Otevřený | 1 |
| V přepravě | 0 |
| Přijatý | 0 |
| Naskladněný | 708 |
| V rezervě | 128 |
| Uzavřený | 2065 |
| Chyba | 0 |

### Sample Transport Boxes

| Box Code | State | Items | Location | Last Update | Description |
|----------|-------|-------|----------|-------------|-------------|
| B989 | Naskladněný | 1 | - | 08. 01. 2026 20:23 | - |
| B999 | Otevřený | 0 | - | 08. 01. 2026 20:19 | - |
| B999 | Uzavřený | 1 | - | 08. 01. 2026 20:19 | - |
| B414 | Naskladněný | 1 | - | 05. 01. 2026 11:43 | - |
| B010 | Naskladněný | 2 | - | 19. 11. 2025 14:45 | - |
| B020 | Naskladněný | 1 | - | 08. 01. 2026 20:18 | - |
| B195 | Naskladněný | 1 | - | 19. 11. 2025 14:45 | - |
| B181 | Naskladněný | 1 | - | 19. 11. 2025 10:55 | - |
| B561 | Naskladněný | 1 | - | 19. 11. 2025 10:51 | - |
| B032 | Naskladněný | 1 | - | 19. 11. 2025 10:30 | - |
| B567 | Naskladněný | 1 | - | 19. 11. 2025 10:30 | - |
| B859 | Naskladněný | 1 | - | 19. 11. 2025 09:16 | - |
| B849 | Naskladněný | 1 | - | 19. 11. 2025 10:50 | - |
| B033 | Naskladněný | 2 | - | 19. 11. 2025 09:40 | - |
| B020 | Uzavřený | 1 | - | 19. 11. 2025 13:48 | - |
| B008 | Naskladněný | 1 | - | 19. 11. 2025 09:15 | - |
| B252 | Naskladněný | 1 | - | 19. 11. 2025 09:16 | - |
| B796 | Naskladněný | 1 | - | 19. 11. 2025 11:26 | - |
| B855 | Naskladněný | 1 | - | 19. 11. 2025 11:26 | - |
| B618 | Naskladněný | 1 | - | 19. 11. 2025 11:25 | - |

**Note:** Box code "B999" appears twice with different states (Otevřený and Uzavřený) - potential test data for state transitions.
**Note:** Box code "B020" also appears twice - another state transition example.

---

## Suppliers

### Supplier References (Anonymized)

**Note:** Real supplier names are not included in documentation for security reasons. Purchase orders and materials are referenced by placeholder codes.

| Supplier Placeholder | Used In |
|---------------------|---------|
| Supplier A | Draft orders, in-transit orders |
| Supplier B | Draft orders |
| Supplier C | In-transit orders (with invoice) |
| Supplier D | In-transit orders |
| Supplier E | Zero-amount orders |
| Supplier F | In-transit orders |
| Supplier G | In-transit orders |
| Supplier MH | Material AKL001 (Bisabolol) |

---

## Product Types

### Available Product Type Filters

1. **Všechny typy** (All types)
2. **Produkt** (Product)
3. **Zboží** (Goods)
4. **Materiál** (Material)
5. **Polotovar** (Semi-product)
6. **Dárkový balíček** (Gift package)
7. **Nedefinováno** (Undefined)

---

## Usage Guidelines

### For E2E Testing

1. **Catalog Testing:**
   - Use `AKL001` (Bisabolol) as a stable material reference
   - Use `MAS001001M` (Hedvábný pan Jasmín) for semi-product testing
   - Total catalog has 906 items - use pagination tests

2. **Purchase Order Testing:**
   - Draft orders: `PO20251119-0855`, `PO20251119-1128`
   - In-transit orders: `PO20251113-0850` (has invoice), `PO20251113-1251` (no invoice)
   - Zero-amount order for edge cases: `PO20251112-1051`

3. **Manufacturing Order Testing:**
   - Use `MO-2026-005` for draft order testing
   - Note: This order has a future date (2029) - may need data correction

4. **Transport Box Testing:**
   - Open box: `B999` (0 items)
   - Stocked boxes with items: `B989`, `B414`, `B010`
   - Closed box: `B020`, `B999` (different instance)
   - Multi-item boxes: `B010` (2 items), `B033` (2 items)

5. **Supplier Testing:**
   - Multiple suppliers available (anonymized as Supplier A, B, C, etc.)
   - Use "Supplier A" for testing (appears in multiple orders)

### Data Stability Notes

- **High Stability:** Catalog items (products/materials) are relatively stable
- **Medium Stability:** Purchase orders may change state over time
- **Low Stability:** Transport boxes change state frequently
- **Test Data Anomalies:**
  - Manufacturing order `MO-2026-005` has date in 2029 (likely test data error)
  - Duplicate box codes (B999, B020) with different states

### Recommended Test Patterns

```typescript
// Example: Catalog item lookup
const testMaterial = {
  code: 'AKL001',
  name: 'Bisabolol',
  type: 'Materiál',
  expectedStock: '>= 2700' // Allow some variance
};

// Example: Purchase order lookup
const testPurchaseOrder = {
  orderNumber: 'PO20251119-0855',
  supplier: 'Supplier A',
  status: 'Návrh',
  hasInvoice: false
};

// Example: Transport box lookup
const testTransportBox = {
  code: 'B989',
  state: 'Naskladněný',
  minItems: 1
};
```

---

## Dashboard Statistics (Snapshot)

### Tile Overview (as of test run)

- **Faktury importované včera:** 0
- **Produkty inventarizované (30dní):** 0
- **Materiály inventarizované (30dní):** 0
- **Produkty podle stáří inventury:**
  - < 180 dní: 201
  - 180-365 dní: 163
  - \> 365 dní: 0
- **Inventury surovin:**
  - < 180 dní: 143
  - 180-365 dní: 5
  - \> 365 dní: 58
- **Inventury obalů a etiket:**
  - < 180 dní: 0
  - 180-365 dní: 1
  - \> 365 dní: 234
- **Materiál NS < 20%:** 7 materiálů s kritickou zásobou
- **Dnešní výroba (25.01.2026):** Žádná výroba
- **Zítřejší výroba (26.01.2026):** Žádná výroba
- **Výrobní příkazy:** 0 vyžadující manuální zásah
- **Boxy v přepravě:** 0
- **Boxy přijaté:** 0
- **Boxy v chybě:** 0
- **Kritické balíčky:** 0
- **Stav background tasků:** 20/24 completed

---

## Version Information

- **App Version:** v1.0.0+131aee33d0c4db46c2e98873efe7197c3e87d172
- **Environment:** Development
- **Authentication:** Mock Auth
- **API:** localhost:5000
- **Health Status:**
  - Live Health: Healthy ✅
  - Ready Health: Chyba ⚠️

---

## Notes for Test Maintenance

1. **Regenerate this file periodically** - data changes over time
2. **Validate critical test data before test runs** - especially purchase orders and manufacturing orders
3. **Use fixtures for stable data** - catalog items are more stable than transactional data
4. **Consider seeding known test data** - for more predictable test outcomes
5. **Document data anomalies** - like the 2029 manufacturing order date

---

**Generated by:** Playwright browser automation
**Extraction Date:** 2026-01-25
**Test Environment:** Development (localhost)
