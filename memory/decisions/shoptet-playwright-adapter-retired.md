---
name: Shoptet Playwright adapter retired
description: Decision to retire the legacy Playwright-based Shoptet admin scraping adapter after completing the REST API migration.
type: project
---

Retired legacy `Anela.Heblo.Adapters.Shoptet` Playwright code on 2026-05-05.

**Why:** REST migration epic complete (EPIC #639). Invoice, stock, expedition, and order operations fully migrated to `Anela.Heblo.Adapters.ShoptetApi`. Playwright path was "kept only for local dev" since 2026-04-15; parity tests confirmed equivalence. Dead surface removed.

**How to apply:** The only remaining non-REST code in `Anela.Heblo.Adapters.Shoptet` is the CSV-based `ShoptetPriceClient` (product prices) and `ShoptetStockClient`-equivalent CSV stock reads. All other Shoptet access is REST via `Anela.Heblo.Adapters.ShoptetApi`.
