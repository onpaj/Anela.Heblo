# Photo swap — report (production, 2026-11-01)

Replacement of the product photos of 64 Anela products with the new-label photos, done through the Shoptet API one product at a time (upload new → set main + order → re-point variants → verify → delete old). Companion to [the launch checklist](2026-11-02-vyhodne-sady-launch-checklist.md) and the [main-photo dry run](2026-11-02-main-photo-dry-run.md).

## Result

- **64 / 64 products done.** After the run every product and every variant was re-read from the live shop: **0 problems**.
- **Images:** 236 before → 268 after (new top photo + one new photo per size, photo #2 kept).
- **Variants: 275** — see below. **16 were skipped** on purpose (no photo data).
- **Not touched:** 142 other products (sets and double shots, gift packages, chocolates, accessories, soaps, vouchers, tests).
- **Backup of everything replaced:** `~/Work/backups/shoptet-photos-2026-11-01/` (originals of all 236 old images, `manifest.json` with image and variant mapping, per-product run logs). Not in git.

## Variants

| Variants | Count | Photo now |
|---|---|---|
| Own size photo | 195 | New photo for their exact code (`SER002005`, `SER002030`, …); 64 of them are the main size and show the new top photo |
| Testers (`…T`) | 64 | New top photo of the product |
| **Skipped** | **16** | **Unchanged — still the old-label photo** |
| Total | 275 | none left on a deleted image; none left on an old photo except the 16 |

### Skipped variants (left untouched)

| Product | Variant | Still uses (old photo) |
|---|---|---|
| Důvěrný pan Růže | `1149/1` | `1149-4.jpg` |
| HladíMe | `ZEN001250` | `957-5.jpg` |
| HladíMe | `ZEN001500` | `957-5.jpg` |
| Modré z nebe | `TON002500` | `703-1.jpg` |
| Moje Moringová | `PMA002050` | `1100-1.jpg` |
| Moje Ovesná | `PMA003050` | `1103-2.jpg` |
| Moje Z nebe | `PMA004050` | `1106-3.jpg` |
| Moje Čokoládová | `PMA001050` | `1097-2.jpg` |
| Měsíční pohlazení | `ZEN003250` | `960-1.jpg` |
| Měsíční pohlazení | `ZEN003500` | `960-1.jpg` |
| Ochráním bříška | `OCH003500` | `117-1.jpg` |
| Pohladím miminka | `OCH005030` | `210-2.jpg` |
| Pohladím miminka | `OCH005250` | `210-2.jpg` |
| Svěží krása | `TON003500` | `748-1.jpg` |
| Svěží motýl | `TON004500` | `754-1.jpg` |
| Svěží teenka | `TON001500` | `626-1.jpg` |

Why skipped: no new photo exists for these sizes (mostly 250 / 500 ml and the three 50 ml of the "Moje" line). In 13 products the old *top* photo therefore stays as the last gallery image — 13 old photos in total.

To move them later: `PATCH /api/products/{guid}` with `variants:[{code, image:<new main image name>}]`, then `DELETE /api/products/{guid}/images/shop/{old name}`. (Checklist item F-8.)

## What went wrong on the way (all handled)

- **Uploads were refused (`403 required-webhook-registration`)** until a `job:finished` webhook existed. A placeholder webhook was registered for the run and deleted afterwards (the shop has no webhooks again).
- **Shoptet moves variants by itself.** When a product's top photo changes, variants that used the old top photo are moved onto the new top photo even if the request does not touch them. This hit the skipped variants of 11 products; the script moved them back to their old photo (logged per product in the run logs). Products: Modré z nebe, Moje Moringová, Moje Ovesná, Moje Z nebe, Moje Čokoládová, Měsíční pohlazení, Ochráním bříška, Pohladím miminka, Svěží krása, Svěží motýl, Svěží teenka.
- Two products (Důvěrný pan Růže, HladíMe) were rolled back automatically by the safety check on the first attempt and finished on the retry. The shop matched the backup after each rollback.
- **`SEZ002005` was skipped by mistake and fixed afterwards.** I misread the file name `SEZ002005.jpg` in the SharePoint listing as `ZEZ002005.jpg` (the listing runs entries together), so Pozdravím slunce 5 ml looked like it had no photo. Found when the file could not be located; the new 5 ml photo was then uploaded, the variant re-pointed and the old 5 ml photo deleted (live check: variant on `419-3.jpg`, other variants unchanged).
- The temporary hosting (private blob container in `stheblo`, SAS links) was deleted after the run.

## Per product

| Product | New main photo (live) | Size | Replaces | Images now | Kept as photo #2 |
|---|---|---|---|---|---|
| Arganový olej BIO | `192.jpg` — Arganový olej 100 ml | 100 | `192-5.jpg` | 3 | `192-3.jpg` |
| Bezstarostná krása | `78.jpg` — Bezstarostna krasa 30 | 030 | `78-6.jpg` | 4 | `78-1.jpg` |
| Bezstarostná teenka | `66.jpg` — Bezstarostna teenka 30 | 030 | `66-7.jpg` | 4 | `66-3.jpg` |
| Bezstarostný motýl | `69.jpg` — Bezstarostny motyl 30 | 030 | `69-7.jpg` | 4 | `69-2.jpg` |
| Bubla bublá | `757.jpg` — Bubla bubla 250 p | 250 | `757-5.jpg` | 5 | `757-8.jpg` |
| Bílá noční teenka | `72-1.jpg` — Bila nocni teenka 30 p | 030 | `72-6.jpg` | 5 | `72.jpg` |
| Důvěrný pan Jasmín | `57.jpg` — DP Jasmin 30 p | 030 | `57-4.jpg` | 4 | `57-2.jpg` |
| Důvěrný pan Levandule | `54.jpg` — DP Levandule 30 p | 030 | `54-7.jpg` | 4 | `54-4.jpg` |
| Důvěrný pan Mandarinka | `111.jpg` — DP Mandarinka 30 p | 030 | `111-4.jpg` | 4 | `111-2.jpg` |
| Důvěrný pan Nenápadný | `84.jpg` — DP Nenapadny 30 p | 030 | `84-8.jpg` | 4 | `84-6.jpg` |
| Důvěrný pan Růže | `1149.jpg` — DP Ruze 30 p | 030 | `1149-4.jpg` | 5 | `1149-2.jpg` |
| Důvěrný pan Santal | `90.jpg` — DP Santal 30 p | 030 | `90-7.jpg` | 4 | `90-1.jpg` |
| Důvěrný pan Ylang | `96.jpg` — DP Ylang 30 p | 030 | `96-4.jpg` | 4 | `96-2.jpg` |
| Hedvábný pan Jasmín | `60-1.jpg` — HP Jasmin 180 p | 180 | `60-2.jpg` | 4 | `60-3.jpg` |
| Hedvábný pan Levandule | `63-1.jpg` — HP Levandule 180 p | 180 | `63-4.jpg` | 4 | `63-2.jpg` |
| Hedvábný pan Mandarinka | `99-1.jpg` — HP Mandarinka 180 p | 180 | `99-2.jpg` | 4 | `99-5.jpg` |
| Hedvábný pan Růže | `102-2.jpg` — HP Ruze 180 p | 180 | `102-3.jpg` | 4 | `102-1.jpg` |
| Hedvábný pan Ylang | `105-1.jpg` — HP Ylang 180 p | 180 | `105-2.jpg` | 4 | `105-3.jpg` |
| HladíMe | `957.jpg` — HladiMe 100 | 100 | `957-5.jpg` | 4 | `957-1.jpg` |
| Klidné nožky | `745.jpg` — Klidne nozky 180 p | 180 | `745-8.jpg` | 5 | `745-5.jpg` |
| Klidný dech | `793.jpg` — Klidny dech 30 p | 030 | `793-2.jpg` | 5 | `793-5.jpg` |
| KoupeMe | `954-2.jpg` — KoupeMe 250 | 250 | `954-5.jpg` | 3 | `954.jpg` |
| Kávová drbna | `213.jpg` — Kavova drbna 180 p | 180 | `213-2.jpg` | 3 | `213-1.jpg` |
| Lesa pán | `413-2.jpg` — Lesa pan 100 | 100 | `413-4.jpg` | 5 | `413.jpg` |
| Malá moringová víla | `455-1.jpg` — Mala moringova vila 30 p | 030 | `455-2.jpg` | 5 | `455.jpg` |
| Malá čarodějka | `174.jpg` — Mala carodejka 30 p | 030 | `174-1.jpg` | 5 | `174-4.jpg` |
| Mandlový olej BIO | `201.jpg` — Mandlovy olej 100 | 100 | `201-5.jpg` | 4 | `201-4.jpg` |
| Meruňkový olej BIO | `195.jpg` — Merunkovy olej 100 | 100 | `195-6.jpg` | 3 | `195-4.jpg` |
| Modré z nebe | `703.jpg` — Modre z nebe 100 | 100 | `703-1.jpg` | 4 | `703-3.jpg` |
| Moje Hebké | `1109.jpg` — Moje Hebke 15 p | 015 | `1109-1.jpg` | 2 | — |
| Moje Moringová | `1100-2.jpg` — Moje Moringova 30 p | 030 | `1100-1.jpg` | 5 | `1100.jpg` |
| Moje Ovesná | `1103.jpg` — Moje Ovesna 30 p | 030 | `1103-2.jpg` | 5 | `1103-1.jpg` |
| Moje Z nebe | `1106-4.jpg` — Moje Z nebe 30 p | 030 | `1106-3.jpg` | 5 | `1106.jpg` |
| Moje Čokoládová | `1097-3.jpg` — Moje Cokoladova 30 p | 030 | `1097-2.jpg` | 5 | `1097-4.jpg` |
| Moringový olej BIO | `198-1.jpg` — Moringovy olej 100 | 100 | `198-8.jpg` | 4 | `198.jpg` |
| Mátová drbna | `951.jpg` — Mátová drbna 180 p | 180 | `951-2.jpg` | 3 | `951-3.jpg` |
| Měsíční koupel | `1094-1.jpg` — Mesicni koupel 250 | 250 | `1094-5.jpg` | 3 | `1094.jpg` |
| Měsíční pohlazení | `960-2.jpg` — Mesicni pohlazeni 100 | 100 | `960-1.jpg` | 4 | `960-5.jpg` |
| Něžná paní Ovesná | `742.jpg` — NP Ovesna 180 p | 180 | `742-8.jpg` | 5 | `742-4.jpg` |
| Něžný pan Moringa | `458-2.jpg` — NP Moringa 180 p | 180 | `458-5.jpg` | 5 | `458-1.jpg` |
| Něžný pan Oves | `108-3.jpg` — NP Oves 180 p | 180 | `108-6.jpg` | 5 | `108.jpg` |
| Ochráním bradavky | `437.jpg` — Ochranim bradavky 30 p | 030 | `437-1.jpg` | 4 | `437-5.jpg` |
| Ochráním bříška | `117.jpg` — Ochranim briska 100 | 100 | `117-1.jpg` | 4 | `117-3.jpg` |
| Ochráním chodidla | `216.jpg` — Ochranim chodidla 180 p | 180 | `216-4.jpg` | 5 | `216-1.jpg` |
| Ochráním tváře | `623.jpg` — Ochranim tvare 50 p | 050 | `623-7.jpg` | 3 | `623-5.jpg` |
| Ochráním zadečky | `51-2.jpg` — Ochranim zadecky 30 p | 030 | `51-4.jpg` | 6 | `51.jpg` |
| Odlíčím | `428.jpg` — Odlicim 100 | 100 | `428-5.jpg` | 3 | `428-2.jpg` |
| Očistím ruce - gel | `383.jpg` — Ocistim ruce gel 100 | 100 | `383-5.jpg` | 4 | — |
| Očistím ruce - sprej | `389-1.jpg` — Ocistim ruce sprej 100 | 100 | `389-2.jpg` | 4 | — |
| Očistím tvář | `632-1.jpg` — Ocistim tvar 100 | 100 | `632-2.jpg` | 5 | `632.jpg` |
| Pohladím miminka | `210.jpg` — Pohladim miminka 100 | 100 | `210-2.jpg` | 3 | `210-1.jpg` |
| Pozdravím slunce | `419.jpg` — Pozdravim slunce 100 | 100 | `419-7.jpg` | 3 | — |
| Půlnoční krása | `129-4.jpg` — Pulnocni krasa 30p | 030 | `129-2.jpg` | 5 | `129-1.jpg` |
| Půlnoční motýl | `727-2.jpg` — Pulnocni motyl 30p | 030 | `727.jpg` | 5 | `727-3.jpg` |
| Půlnoční teenka | `718-3.jpg` — Pulnocni teenka 30p | 030 | `718.jpg` | 5 | `718-2.jpg` |
| Růžové z nebe | `281-3.jpg` — Ruzove z nebe 30 | 030 | `281-1.jpg` | 4 | `281-4.jpg` |
| Sladce bezstarostná | `434.jpg` — Sladce bezstarostna 30 | 030 | `434-7.jpg` | 4 | `434-3.jpg` |
| Svěží krása | `748-2.jpg` — Svezi krasa 100 | 100S | `748-1.jpg` | 4 | `748-7.jpg` |
| Svěží motýl | `754.jpg` — Svěží motýl 100ml_tonikum pro citlivou pleť | 100S | `754-1.jpg` | 4 | `754-7.jpg` |
| Svěží teenka | `626-2.jpg` — Svezi teenka 100 | 100S | `626-1.jpg` | 4 | `626-4.jpg` |
| Umyju miminka | `638.jpg` — Umyju miminka 250 | 250 | `638-9.jpg` | 5 | `638-2.jpg` |
| Umyju něžně | `644-5.jpg` — Umyju nezne 250 | 250 | `644-1.jpg` | 5 | `644.jpg` |
| Zorro ctitel | `422-1.jpg` — Zorro ctitel 30 p | 030 | `422-4.jpg` | 5 | `422.jpg` |
| Čokoládová drbna | `948-2.jpg` — Čokoládová drbna 180ml p | 180 | `948-3.jpg` | 3 | `948-1.jpg` |

## Open

- F-7: visual spot check on the live site (only the pilot product was checked in a browser).
- F-8: decide about the 16 skipped variants.
- F-10: revoke the temporary "Storage Blob Data Contributor" role on `stheblo`.
- Best-seller size of the top photo was not checked against sales (the new top photo has the same size as the old one).

## Restore (if ever needed)

Old originals are in the backup folder, one folder per product (`NN_<product>/NN[_MAIN]_<seoName>`). `manifest.json` lists for each product the images (name, priority, description, main flag) and each variant's photo. A restore re-uploads the files by public URL (needs a `job:finished` webhook, see `docs/integrations/shoptet-api.md`), sets the priorities and re-points the variants.
