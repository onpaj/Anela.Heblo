# Flexi user query 41 is per-company, lower-cased, and silently shapes every ERP price

`FlexiProductPriceErpClient` reads ALL ERP prices through Flexi **user query 41** (`CENIK`,
"Ceny produktu"), executed as `GET /c/{firma}/uzivatelsky-dotaz/41/call.json?limit=0` with
rows under `winstrom.DotazView`. That one read feeds the catalogue's ERP price, margins,
`FinancialOverviewStockValueAdapter` stock valuation, `FlexiProductVatRateProvider`, and the
price-comparison screen. Three things about it bite.

## 1. The query is a live ERP object, not code — and each company has its own copy

Its SQL lives in Flexi, not in this repo, and `anela_cosmetics_test` and the production
company each hold a **separate** query 41. Extending one does nothing for the other. Read the
current definition with `GET /c/{firma}/uzivatelsky-dotaz/41.json` — the `dotaz` field is the
literal SQL. Do that before concluding anything about what the adapter "can" see.

## 2. `cena` is raw `cenazakl` — meaningless without `typcenydphk`

A ceník item's `cenaZakl` has no inherent VAT meaning; `typCenyDphK`
(`typCeny.bezDph` / `typCeny.sDph`) says which it is. Query 41 originally selected
`cenazakl as cena` and **not** `typcenydphk`, so the adapter saw a null price type, assumed
excl-VAT and grossed every `sDph` item up a second time. `OCH005100` held at 285.00 incl. VAT
rendered as **344.85** on the comparison screen, with every row badged "Neznámý typ ceny".
Shoptet and Flexi were both correct the whole time — only the read was wrong.

Extended server-side 2026-09-11 to select `typcenydphk`; no app code changed. 79 of 1252
items in the test company are `sDph`, so this was never only about newly-edited products —
it had been misreporting pre-existing items, in the catalogue too.

## 3. Query 41 returns columns LOWER-CASED; the `cenik` evidence returns camelCase

`typcenydphk`, `idkusovnik`, `typszbdphk`, `typzasobyk` — all lower case. `ProductPriceFlexiDto`
declares `[JsonProperty("typCenyDphK")]` and `[JsonProperty("idKusovnik")]`, which bind **only**
through Newtonsoft's case-insensitive fallback in `ResourceClient.GetAsync`'s
`JObject...ToObject<List<T>>()`. Move that DTO to System.Text.Json, or set a stricter
contract resolver, and the price type silently reads null — the double-VAT bug is back with
nothing failing. `ProductPriceFlexiDtoWireShapeTests` pins this against a verbatim response.

**Verified vocabulary (live 2026-09-11):** query 41 returns the ENUM form
(`typSzbDph.dphZakl`, `typZasoby.vyrobek`), never the Czech labels. Note
`ProductPriceFlexiDto.HasCalculatedPurchasePrice` compares `ProductType == "Výrobek"` — a
Czech label this query never returns, so it is always false. It and `HasBillOfMaterials` are
currently unreferenced; left in place, flagged here.

## The lesson that generalises

Every other test in `Adapters/Flexi` builds the DTO in C#, which proves the arithmetic and
proves nothing about whether Flexi's JSON ever reaches those properties. **For an adapter,
assert against a verbatim captured response**, not a hand-built DTO — same family as
`substring asserts hide wire shape`. Two Flexi column-contract bugs have now hidden in
exactly that gap.
