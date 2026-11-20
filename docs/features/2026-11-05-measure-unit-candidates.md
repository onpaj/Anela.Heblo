# Shoptet measure units: candidate products (draft)

Read from the **production store** on 2026-11-05. Scope: groups A and B, with Měrná jednotka fixed at 100 ml (100 g for gram products). The variant parameter is the source of truth for volume; variant codes are not used to check it. Variant-level data, including every code, is in `2026-11-05-measure-unit-candidates.csv`.

| Group | Products | Variants |
|---|---:|---:|
| A. Standard retail sizes (ready) | 72 | 228 |
| B. Samples (`vzorek`) | 0 | 0 |
| **All candidates** | **72** | **228** |

## Result (applied to production 2026-11-05)

192 variants across 63 products were set: packaging = the `Obsah` parameter in ml, Měrná jednotka = 100 ml. A full catalogue comparison before and after showed these 192 `measureUnit` values as the only variant or product differences (454 variants, `changeTime` ignored). The CSV column `applied_2026_11_05` marks each one.

Skipped on purpose: variants already set (`Růžové z nebe`), hidden variants, zero price, `N + N ml` double-shots, `Chladicí vložka`, and `DEZ001100` / `OCH010030` (implausible weight).

## Needs checking

| Code | Product | Value | Issue |
|---|---|---|---|
| `DEZ001100` | Očistím ruce - gel | 100 ml | weight 1.878 kg looks too high |
| `SER004005` | Růžové z nebe | 4,9 ml | already has measure 100.000 ml |
| `SER004030` | Růžové z nebe | 30 ml | already has measure 100.000 ml |
| `SER004100` | Růžové z nebe | 100 ml | already has measure 100.000 ml |

## A. Standard retail sizes (ready)

| Product | Sizes | Status |
|---|---|---|
| Arganový olej BIO | 30 ml, 100 ml | visible |
| Bezstarostná krása | 4.9 ml, 30 ml, 100 ml | visible |
| Bezstarostná teenka | 4.9 ml, 30 ml, 100 ml | visible |
| Bezstarostný motýl | 4.9 ml, 30 ml, 100 ml | visible |
| Bubla bublá | 30 ml, 250 ml, 500 ml, 1000 ml | visible |
| Bílá noční teenka | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| Chladicí vložka | 200 g, 400 g | hidden |
| Double shot s Krásou | 4.9 ml, 30 ml, 100 ml | visible |
| Double shot s Motýlem | 4.9 ml, 30 ml, 100 ml | visible |
| Double shot s Teenkou | 4.9 ml, 30 ml, 100 ml | visible |
| Důvěrný pan Jasmín | 4.9 ml, 30 ml, 50 ml | visible |
| Důvěrný pan Levandule | 4.9 ml, 30 ml, 50 ml | visible |
| Důvěrný pan Mandarinka | 4.9 ml, 30 ml, 50 ml | visible |
| Důvěrný pan Nenápadný | 4.9 ml, 30 ml, 50 ml | visible |
| Důvěrný pan Růže | 4.9 ml, 30 ml, 50 ml | visible |
| Důvěrný pan Santal | 4.9 ml, 30 ml, 50 ml | visible |
| Důvěrný pan Ylang | 4.9 ml, 30 ml, 50 ml | visible |
| Hedvábný pan Jasmín | 15 ml, 180 ml, 500 ml | visible |
| Hedvábný pan Levandule | 15 ml, 180 ml, 500 ml | visible |
| Hedvábný pan Mandarinka | 15 ml, 180 ml, 450 ml | visible |
| Hedvábný pan Růže | 15 ml, 180 ml, 500 ml | visible |
| Hedvábný pan Ylang | 15 ml, 180 ml, 500 ml | visible |
| HladíMe | 30 ml, 100 ml, 250 ml, 500 ml | mixed |
| Klidné nožky | 15 ml, 50 ml, 180 ml, 450 ml | visible |
| Klidné nožky 50 ml 1+1 ZDARMA | 50 ml | hidden |
| Klidný dech | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| KoupeMe | 30 ml, 250 ml | visible |
| Kávová drbna | 180 ml, 450 ml | visible |
| Lesa pán | 30 ml, 100 ml, 500 ml, 1000 ml | visible |
| Malá moringová víla | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| Malá čarodějka | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| Mandlový olej BIO | 100 ml, 500 ml, 1000 ml | visible |
| Meruňkový olej BIO | 30 ml, 100 ml | visible |
| Modré z nebe | 30 ml, 100 ml | visible |
| Moje Hebké | 4.9 ml, 15 ml | visible |
| Moje Moringová | 4.9 ml, 15 ml, 30 ml, 50 ml | mixed |
| Moje Ovesná | 4.9 ml, 15 ml, 30 ml, 50 ml | mixed |
| Moje Z nebe | 4.9 ml, 15 ml, 30 ml, 50 ml | mixed |
| Moje Čokoládová | 4.9 ml, 15 ml, 30 ml, 50 ml | mixed |
| Moringový olej BIO | 4.9 ml, 30 ml, 100 ml | visible |
| Mátová drbna | 180 ml, 450 ml | visible |
| Měsíční koupel | 30 ml, 250 ml | visible |
| Měsíční pohlazení | 30 ml, 100 ml, 250 ml, 500 ml | mixed |
| Něžná paní Ovesná | 15 ml, 50 ml, 180 ml, 500 ml | visible |
| Něžná paní Ovesná 2. jakost - zklidňující šlehané máslo bez ořechů pro citlivou pokožku | 50 ml, 180 ml, 500 ml | hidden |
| Něžný pan Moringa | 15 ml, 50 ml, 180 ml, 500 ml | visible |
| Něžný pan Oves | 15 ml, 50 ml, 180 ml, 500 ml | visible |
| Ochráním bradavky | 4.9 ml, 15 ml, 30 ml | visible |
| Ochráním bříška | 100 ml, 250 ml, 500 ml | visible |
| Ochráním chodidla | 15 ml, 50 ml, 180 ml, 500 ml | visible |
| Ochráním tváře | 15 ml, 50 ml | visible |
| Ochráním zadečky | 4.9 ml, 30 ml, 50 ml, 180 ml, 500 ml | visible |
| Odlíčím | 30 ml, 100 ml | visible |
| Očistím ruce - gel | 30 ml, 100 ml, 500 ml, 1000 ml | mixed |
| Očistím ruce - sprej | 30 ml, 100 ml, 500 ml, 1000 ml | visible |
| Očistím tvář | 30 ml, 100 ml, 500 ml, 1000 ml | visible |
| Pohladím miminka | 30 ml, 100 ml, 250 ml | mixed |
| Pozdravím slunce | 4.9 ml, 50 ml, 100 ml | visible |
| Půlnoční krása | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| Půlnoční motýl | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| Půlnoční teenka | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| Růžové z nebe | 4.9 ml, 30 ml, 100 ml | visible |
| Sladce bezstarostná | 4.9 ml, 30 ml, 100 ml | visible |
| Sladký Double shot | 4.9 ml, 30 ml, 100 ml | visible |
| Svěží krása | 30 ml, 100 ml, 500 ml | visible |
| Svěží motýl | 30 ml, 100 ml | visible |
| Svěží teenka | 30 ml, 100 ml | visible |
| TEST1 | 30 ml, 100 ml, 250 ml | hidden |
| Umyju miminka | 30 ml, 250 ml, 500 ml, 1000 ml | visible |
| Umyju něžně | 30 ml, 250 ml, 500 ml, 1000 ml | visible |
| Zorro ctitel | 4.9 ml, 15 ml, 30 ml, 50 ml | visible |
| Čokoládová drbna | 180 ml, 500 ml | visible |

## B. Samples (`vzorek`)

| Product | Sizes | Status |
|---|---|---|

## Not included

- Store testers (C): 64 variants
- Sold loose by the gram (D): 1 variants
- Size plus gift, or volume only in the name (E): 0 variants
- Other products with no volume in parameters or name (bundles, vouchers, accessories, soaps, courses): 113 variants
- Already have a gram measure but no volume in parameters or name: 7 variants
- Product sets (`product-set`): 41 variants
