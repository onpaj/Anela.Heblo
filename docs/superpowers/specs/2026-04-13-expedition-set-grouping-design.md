# Expedition List — Set Item Grouping Design

**Date:** 2026-04-13
**Branch:** fix/expedition_list_visual_updates

## Problem

When an order contains a product set, its components are currently rendered in the order table with only a blue background to distinguish them. There is no visual indication of which set the items belong to, and there is no grouping — components of the same set may appear scattered among regular items.

## Goal

- **Order section (per-order table):** Group set components visually under a clearly labelled header row showing the set name.
- **Summary page (aggregated list):** No change — set items already appear in the total product list, which is the correct behaviour.

## Design

### 1. Data Model — `ExpeditionOrderItem`

Add a `SetName` property:

```csharp
public string? SetName { get; set; }
```

- `null` → regular product item
- non-null → component of a named set; value is the set's display name

`IsFromSet` is kept unchanged (still used by the summary page rendering).

### 2. Mapping — `ShoptetApiExpeditionListSource.MapOrderItems`

When processing a `product-set` item, populate `SetName` on each component with the parent set item's `Name`:

```csharp
SetName = item.Name ?? string.Empty,
```

No additional API calls or lookups are required — the name is already available on the `item` variable in the `product-set` branch.

### 3. PDF Rendering — `ExpeditionProtocolDocument` (order section only)

Before rendering the items table for each order, items are partitioned into two groups:

1. **Regular items** (`SetName == null`) — rendered in their natural order.
2. **Set items** (`SetName != null`) — grouped by `SetName`, each group preceded by a sub-header row.

**Sub-header row format:**
- Spans all 6 columns (full table width)
- Text: `"Sada: [SetName]"`, bold
- Background: `Colors.Blue.Lighten3` (slightly darker than the `Lighten4` used for set item cells)
- Same border style as other cells

Set item cells continue to use the existing `SetDataCell` / `SetCenteredDataCell` styles (blue Lighten4 background).

The summary page aggregation logic is untouched.

## Files Changed

| File | Change |
|------|--------|
| `ExpeditionProtocolData.cs` | Add `SetName` property to `ExpeditionOrderItem` |
| `ShoptetApiExpeditionListSource.cs` | Populate `SetName` in `MapOrderItems` |
| `ExpeditionProtocolDocument.cs` | Group set items under labelled sub-header rows in order table |
