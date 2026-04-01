# Manufacture Error Transformation Layer — Design

**Date:** 2026-04-01
**Branch:** feature/manufacture-error-transformation
**Status:** Approved

---

## Problem

When manufacturing orders are submitted to Flexi ERP, exceptions are caught and stored verbatim to the database error log. These raw messages contain stack traces, internal system codes, and English technical text that non-technical users cannot act on.

**Goal:** Transform exceptions into concise Czech user-facing messages before persisting them. Raw exceptions are logged for developers but never stored in the DB.

---

## Decisions

| Question | Decision |
|---|---|
| Where is message stored? | Backend transforms before DB write; frontend renders stored string as-is |
| Language | Czech |
| Message style | Describe the problem only — no action hints |
| Unrecognized errors | Generic Czech fallback + raw `exception.Message` appended |
| Architecture | Pluggable: one class per error type, auto-discovered via DI assembly scanning |
| Filter input | Full `Exception` object (not just message string) |

---

## Core Contracts

```csharp
// Application/Features/Manufacture/ErrorFilters/IManufactureErrorFilter.cs
public interface IManufactureErrorFilter
{
    bool CanHandle(Exception exception);
    string Transform(Exception exception);
}

// Application/Features/Manufacture/ErrorFilters/IManufactureErrorTransformer.cs
public interface IManufactureErrorTransformer
{
    string Transform(Exception exception);
}
```

### ManufactureErrorTransformer (orchestrator)

- Iterates all registered `IManufactureErrorFilter` implementations in registration order
- Returns result of first filter where `CanHandle` returns `true`
- Fallback when no filter matches:
  > *"Při zpracování výroby došlo k neočekávané chybě. Technické detaily: {exception.Message}"*

---

## Concrete Filters

All filters live in `Application/Features/Manufacture/ErrorFilters/Filters/`.

Each filter uses simple string inspection (no regex) to match and extract values.

| Class | Detection | Czech message |
|---|---|---|
| `InsufficientSemiProductFilter` | Message contains `"Nelze vytvořit příjemku výrobku"` AND `"POLOTOVARY"` | *"Nedostatek meziproduktu '{název}' na skladu POLOTOVARY (požadováno: {x}, dostupné: {y})"* |
| `InsufficientRawMaterialFilter` | Message contains `"Nelze vytvořit příjemku výrobku"` AND `"MATERIAL"` | *"Nedostatek materiálu '{název}' na skladu MATERIAL (požadováno: {x}, dostupné: {y})"* |
| `ProductCodeNotFoundFilter` | Message contains `"musí identifikovat objekt"` | *"Produkt s kódem '{kód}' nebyl nalezen v systému Flexi."* |
| `ReferenceTooLongFilter` | Message contains `"Číslo došlé"` AND `"40 znaků"` | *"Číslo objednávky je příliš dlouhé pro systém Flexi (max. 40 znaků)."* |
| `ZeroQuantityItemFilter` | Exception is `ArgumentException` AND message contains `"Item quantity must be greater than zero"` | *"Položka výrobní zakázky má nulové množství."* |
| `InsufficientLotAllocationFilter` | Message contains `"Could not allocate sufficient lots"` | *"Nedostatek šarží pro ingredienci '{název}' (chybí: {x} g)."* |
| `WarehouseNotConfiguredFilter` | Message contains `"Pole 'Sklad' musí být vyplněno"` | *"Skladový pohyb nemá nastaven sklad."* |
| `NegativeStockFilter` | Message contains `"Není povolen záporný stav skladu"` | *"Operace by způsobila záporný stav skladu."* |

---

## Registration

In `ManufactureModule.cs` — auto-scans assembly, no manual registration per filter:

```csharp
services.Scan(scan => scan
    .FromAssemblyOf<IManufactureErrorFilter>()
    .AddClasses(c => c.AssignableTo<IManufactureErrorFilter>())
    .AsImplementedInterfaces()
    .WithTransientLifetime());

services.AddTransient<IManufactureErrorTransformer, ManufactureErrorTransformer>();
```

**Adding a new error type:** create a class implementing `IManufactureErrorFilter` in the `Filters/` folder — nothing else to touch.

---

## Usage

In `SubmitManufactureHandler` (the single entry point for all these errors):

```csharp
catch (Exception ex)
{
    var userMessage = _errorTransformer.Transform(ex);
    await _errorLogRepository.SaveAsync(orderId, userMessage);
    _logger.LogError(ex, "Submit manufacture failed for order {OrderId}", orderId);
}
```

---

## File Structure

```
Application/Features/Manufacture/ErrorFilters/
├── IManufactureErrorFilter.cs
├── IManufactureErrorTransformer.cs
├── ManufactureErrorTransformer.cs
└── Filters/
    ├── InsufficientSemiProductFilter.cs
    ├── InsufficientRawMaterialFilter.cs
    ├── ProductCodeNotFoundFilter.cs
    ├── ReferenceTooLongFilter.cs
    ├── ZeroQuantityItemFilter.cs
    ├── InsufficientLotAllocationFilter.cs
    ├── WarehouseNotConfiguredFilter.cs
    └── NegativeStockFilter.cs
```

---

## Out of Scope

- Frontend re-interpretation of stored messages
- Action hints or remediation guidance in messages
- Localization beyond Czech
- Errors from handlers other than `SubmitManufactureHandler`
