# Main photo dry run — which products get a new top photo (2026-11-02 launch)

> **Read-only dry run.** Generated 2026-11-01 from the live Shoptet API (`GET` only, nothing written) and the folder listings on the Marketing SharePoint. Companion to [2026-11-02-vyhodne-sady-launch-checklist.md](2026-11-02-vyhodne-sady-launch-checklist.md) (items F-3…F-5, D-2). Regenerate rather than hand-edit the table.

**Base path of new photos** (Marketing site → `Shared Documents/`):

`Grafika_interní/PROFI_FOCENI/PRODUKTOVÉ FOTO_web/2026_produktové_foto_nové_etikety/`

## Result

- **64 products** have new photos and are in scope; **142** are out of scope (no new file for any variant).
- Current top photo size: **030 ml** ×26, **100 ml** ×18, **180 ml** ×13, **250 ml** ×5, **015 ml** ×1, **050 ml** ×1. So the top photo is **not** always 30 ml (earlier 3-product sample was misleading): by code prefix, MAS and PEE products mostly show 180 ml, vials (TON, ODL, JOL, DEZ, SEZ, ZEN) mostly 100 ml, and ZEN/OCH bath products 250 ml.
- Rule used: the new top photo is the new file for the **variant whose size equals the size of the current top photo** (taken from the image description, confirmed by the variant that points to that image). If the same size has no new file the row says so.
- Photo #2 (kept) = the only image whose description has no size. In 60 of 64 products it is exactly photo #2. **4 products have no unsized photo** (see below).
- **Not verified:** that the current top size is really the best-selling one — that would need the Heblo sales data.

## Dry-run table

| # | Product | Size | Current top photo (file — description) | Replace with (new file, relative to base) | Photo #2 kept (file — description) |
|---|---|---|---|---|---|
| 1 | Arganový olej BIO (JOL001100) | 100 | `192-5.jpg` — Arganový olej 100 ml | `Lahvičky_web_hotové/JOL001100.jpg` | `192-3.jpg` — Argan |
| 2 | Bezstarostná krása (SER001030) | 030 | `78-6.jpg` — Bezstarostna krasa 30 | `Lahvičky_web_hotové/SER001030.jpg` | `78-1.jpg` — Bezstarostná krása |
| 3 | Bezstarostná teenka (SER002030) | 030 | `66-7.jpg` — Bezstarostna teenka 30 | `Lahvičky_web_hotové/SER002030.jpg` | `66-3.jpg` — Bezstarostná teenka |
| 4 | Bezstarostný motýl (SER003030) | 030 | `69-7.jpg` — Bezstarostny motyl 30 | `Lahvičky_web_hotové/SER003030.jpg` | `69-2.jpg` — Bezstarostný motýl |
| 5 | Bubla bublá (OCH010250) | 250 | `757-5.jpg` — Bubla bubla 250 p | `Lahvičky_web_hotové/OCH010250.jpg` | `757-8.jpg` — Bubla |
| 6 | Bílá noční teenka (KRE001030) | 030 | `72-6.jpg` — Bila nocni teenka 30 p | `Kelímky_web_hotové/KRE001030.jpg` | `72.jpg` — bila nocni teenka |
| 7 | Důvěrný pan Jasmín (DEO001030) | 030 | `57-4.jpg` — DP Jasmin 30 p | `Kelímky_web_hotové/DEO001030.jpg` | `57-2.jpg` — Důvěrný pan všechny |
| 8 | Důvěrný pan Levandule (DEO002030) | 030 | `54-7.jpg` — DP Levandule 30 p | `Kelímky_web_hotové/DEO002030.jpg` | `54-4.jpg` — Důvěrný pan všechny |
| 9 | Důvěrný pan Mandarinka (DEO003030) | 030 | `111-4.jpg` — DP Mandarinka 30 p | `Kelímky_web_hotové/DEO003030.jpg` | `111-2.jpg` — Důvěrný pan všechny |
| 10 | Důvěrný pan Nenápadný (DEO004030) | 030 | `84-8.jpg` — DP Nenapadny 30 p | `Kelímky_web_hotové/DEO004030.jpg` | `84-6.jpg` — Důvěrný pan všechny |
| 11 | Důvěrný pan Růže (DEO007030) | 030 | `1149-4.jpg` — DP Ruze 30 p | `Kelímky_web_hotové/DEO007030.jpg` | `1149-2.jpg` — Důvěrný pan všechny |
| 12 | Důvěrný pan Santal (DEO005030) | 030 | `90-7.jpg` — DP Santal 30 p | `Kelímky_web_hotové/DEO005030.jpg` | `90-1.jpg` — Důvěrný pan všechny |
| 13 | Důvěrný pan Ylang (DEO006030) | 030 | `96-4.jpg` — DP Ylang 30 p | `Kelímky_web_hotové/DEO006030.jpg` | `96-2.jpg` — Důvěrný pan všechny |
| 14 | Hedvábný pan Jasmín (MAS001180) | 180 | `60-2.jpg` — HP Jasmin 180 p | `Kelímky_web_hotové/MAS001180.jpg` | `60-3.jpg` — Hedvábný pan všechny |
| 15 | Hedvábný pan Levandule (MAS002180) | 180 | `63-4.jpg` — HP Levandule 180 p | `Kelímky_web_hotové/MAS002180.jpg` | `63-2.jpg` — Hedvábný pan všechny |
| 16 | Hedvábný pan Mandarinka (MAS003180) | 180 | `99-2.jpg` — HP Mandarinka 180 p | `Kelímky_web_hotové/MAS003180.jpg` | `99-5.jpg` — Hedvábný pan všechny |
| 17 | Hedvábný pan Růže (MAS004180) | 180 | `102-3.jpg` — HP Ruze 180 p | `Kelímky_web_hotové/MAS004180.jpg` | `102-1.jpg` — Hedvábný pan všechny |
| 18 | Hedvábný pan Ylang (MAS005180) | 180 | `105-2.jpg` — HP Ylang 180 p | `Kelímky_web_hotové/MAS005180.jpg` | `105-3.jpg` — Hedvábný pan všechny |
| 19 | HladíMe (ZEN001100) | 100 | `957-5.jpg` — HladiMe 100 | `Lahvičky_web_hotové/ZEN001100.jpg` | `957-1.jpg` — Hladíme |
| 20 | Klidné nožky (MAS009180) | 180 | `745-8.jpg` — Klidne nozky 180 p | `Kelímky_web_hotové/MAS009180.jpg` | `745-5.jpg` — klidne nozky |
| 21 | Klidný dech (OCH009030) | 030 | `793-2.jpg` — Klidny dech 30 p | `Kelímky_web_hotové/OCH009030.jpg` | `793-5.jpg` — klidny dech |
| 22 | KoupeMe (ZEN002250) | 250 | `954-5.jpg` — KoupeMe 250 | `Lahvičky_web_hotové/ZEN002250.jpg` | `954.jpg` — Koupeme |
| 23 | Kávová drbna (PEE001180) | 180 | `213-2.jpg` — Kavova drbna 180 p | `Kelímky_web_hotové/PEE001180.jpg` | `213-1.jpg` — kávová drbna |
| 24 | Lesa pán (SEZ001100) | 100 | `413-4.jpg` — Lesa pan 100 | `Lahvičky_web_hotové/SEZ001100.jpg` | `413.jpg` — Lesa pán |
| 25 | Malá moringová víla (KRE005030) | 030 | `455-2.jpg` — Mala moringova vila 30 p | `Kelímky_web_hotové/KRE005030.jpg` | `455.jpg` — mala moringova vila |
| 26 | Malá čarodějka (KRE002030) | 030 | `174-1.jpg` — Mala carodejka 30 p | `Kelímky_web_hotové/KRE002030.jpg` | `174-4.jpg` — Malá čarodějka |
| 27 | Mandlový olej BIO (JOL002100) | 100 | `201-5.jpg` — Mandlovy olej 100 | `Lahvičky_web_hotové/JOL002100.jpg` | `201-4.jpg` — Mandle |
| 28 | Meruňkový olej BIO (JOL003100) | 100 | `195-6.jpg` — Merunkovy olej 100 | `Lahvičky_web_hotové/JOL003100.jpg` | `195-4.jpg` — Meruňka |
| 29 | Modré z nebe (TON002100) | 100 | `703-1.jpg` — Modre z nebe 100 | `Lahvičky_web_hotové/TON002100.jpg` | `703-3.jpg` — Modré z nebe |
| 30 | Moje Hebké (PMA005015) | 015 | `1109-1.jpg` — Moje Hebke 15 p | `Kelímky_web_hotové/PMA005015.jpg` | **none unsized** (photo #2: `1109-2.jpg` — Moje Hebke 05 p) |
| 31 | Moje Moringová (PMA002030) | 030 | `1100-1.jpg` — Moje Moringova 30 p | `Kelímky_web_hotové/PMA002030.jpg` | `1100.jpg` — Moje moringova |
| 32 | Moje Ovesná (PMA003030) | 030 | `1103-2.jpg` — Moje Ovesna 30 p | `Kelímky_web_hotové/PMA003030.jpg` | `1103-1.jpg` — Moje ovesna |
| 33 | Moje Z nebe (PMA004030) | 030 | `1106-3.jpg` — Moje Z nebe 30 p | `Kelímky_web_hotové/PMA004030.jpg` | `1106.jpg` — Moje z nebe |
| 34 | Moje Čokoládová (PMA001030) | 030 | `1097-2.jpg` — Moje Cokoladova 30 p | `Kelímky_web_hotové/PMA001030.jpg` | `1097-4.jpg` — Moje cokoladova |
| 35 | Moringový olej BIO (JOL004100) | 100 | `198-8.jpg` — Moringovy olej 100 | `Lahvičky_web_hotové/JOL004100.jpg` | `198.jpg` — Moringa |
| 36 | Mátová drbna (PEE004180) | 180 | `951-2.jpg` — Mátová drbna 180 p | `Kelímky_web_hotové/PEE004180.jpg` | `951-3.jpg` — Matova drbna |
| 37 | Měsíční koupel (ZEN004250) | 250 | `1094-5.jpg` — Mesicni koupel 250 | `Lahvičky_web_hotové/ZEN004250.jpg` | `1094.jpg` — Koupeme |
| 38 | Měsíční pohlazení (ZEN003100) | 100 | `960-1.jpg` — Mesicni pohlazeni 100 | `Lahvičky_web_hotové/ZEN003100.jpg` | `960-5.jpg` — Měsíční pohlazení |
| 39 | Něžná paní Ovesná (MAS008180) | 180 | `742-8.jpg` — NP Ovesna 180 p | `Kelímky_web_hotové/MAS008180.jpg` | `742-4.jpg` — Něžná paní Ovesná |
| 40 | Něžný pan Moringa (MAS007180) | 180 | `458-5.jpg` — NP Moringa 180 p | `Kelímky_web_hotové/MAS007180.jpg` | `458-1.jpg` — HP všechny |
| 41 | Něžný pan Oves (MAS006180) | 180 | `108-6.jpg` — NP Oves 180 p | `Kelímky_web_hotové/MAS006180.jpg` | `108.jpg` — Nezny pan oves |
| 42 | Ochráním bradavky (OCH006030) | 030 | `437-1.jpg` — Ochranim bradavky 30 p | `Kelímky_web_hotové/OCH006030.jpg` | `437-5.jpg` — Ochráním bradavky |
| 43 | Ochráním bříška (OCH003100) | 100 | `117-1.jpg` — Ochranim briska 100 | `Lahvičky_web_hotové/OCH003100.jpg` | `117-3.jpg` — Ochráním bříška |
| 44 | Ochráním chodidla (PEE002180) | 180 | `216-4.jpg` — Ochranim chodidla 180 p | `Kelímky_web_hotové/PEE002180.jpg` | `216-1.jpg` — Hedvábný pan všechny |
| 45 | Ochráním tváře (OCH004050) | 050 | `623-7.jpg` — Ochranim tvare 50 p | `Kelímky_web_hotové/OCH004050.jpg` | `623-5.jpg` — Ochranim tvare |
| 46 | Ochráním zadečky (OCH001030) | 030 | `51-4.jpg` — Ochranim zadecky 30 p | `Kelímky_web_hotové/OCH001030.jpg` | `51.jpg` — Ochráním zadečky |
| 47 | Odlíčím (ODL001100) | 100 | `428-5.jpg` — Odlicim 100 | `Lahvičky_web_hotové/ODL001100.jpg` | `428-2.jpg` — Odlicim |
| 48 | Očistím ruce - gel (DEZ001100) | 100 | `383-5.jpg` — Ocistim ruce gel 100 | `Lahvičky_web_hotové/DEZ001100.jpg` | **none unsized** (photo #2: `383-4.jpg` — Ocistim ruce gel 30) |
| 49 | Očistím ruce - sprej (DEZ002100) | 100 | `389-2.jpg` — Ocistim ruce sprej 100 | `Lahvičky_web_hotové/DEZ002100.jpg` | **none unsized** (photo #2: `389.jpg` — Ocistim ruce sprej 30) |
| 50 | Očistím tvář (ODL002100) | 100 | `632-2.jpg` — Ocistim tvar 100 | `Lahvičky_web_hotové/ODL002100.jpg` | `632.jpg` — ocistim tvar |
| 51 | Pohladím miminka (OCH005100) | 100 | `210-2.jpg` — Pohladim miminka 100 | `Lahvičky_web_hotové/OCH005100.jpg` | `210-1.jpg` — Pohladím miminka |
| 52 | Pozdravím slunce (SEZ002100) | 100 | `419-7.jpg` — Pozdravim slunce 100 | `Lahvičky_web_hotové/SEZ002100.jpg` | **none unsized** (photo #2: `419-6.jpg` — Pozdravim slunce 50) |
| 53 | Půlnoční krása (KRE003030) | 030 | `129-2.jpg` — Pulnocni krasa 30p | `Kelímky_web_hotové/KRE003030.jpg` | `129-1.jpg` — Pulnocni krasa |
| 54 | Půlnoční motýl (KRE007030) | 030 | `727.jpg` — Pulnocni motyl 30p | `Kelímky_web_hotové/KRE007030.jpg` | `727-3.jpg` — Pulnocni motyl |
| 55 | Půlnoční teenka (KRE006030) | 030 | `718.jpg` — Pulnocni teenka 30p | `Kelímky_web_hotové/KRE006030.jpg` | `718-2.jpg` — Pulnocni teenka |
| 56 | Růžové z nebe (SER004030) | 030 | `281-1.jpg` — Ruzove z nebe 30 | `Lahvičky_web_hotové/SER004030.jpg` | `281-4.jpg` — Růžové z nebe |
| 57 | Sladce bezstarostná (SER005030) | 030 | `434-7.jpg` — Sladce bezstarostna 30 | `Lahvičky_web_hotové/SER005030.jpg` | `434-3.jpg` — Sladce bezstarostná |
| 58 | Svěží krása (TON003100S) | 100S | `748-1.jpg` — Svezi krasa 100 | `Lahvičky_web_hotové/TON003100S.jpg` | `748-7.jpg` — Svěží krá sa |
| 59 | Svěží motýl (TON004100S) | 100S | `754-1.jpg` — Svěží motýl 100ml_tonikum pro citlivou pleť | `Lahvičky_web_hotové/TON004100S.jpg` | `754-7.jpg` — Svěží motýl - kapka tonika |
| 60 | Svěží teenka (TON001100S) | 100S | `626-1.jpg` — Svezi teenka 100 | `Lahvičky_web_hotové/TON001100S.jpg` | `626-4.jpg` — Svěží teenka |
| 61 | Umyju miminka (OCH007250) | 250 | `638-9.jpg` — Umyju miminka 250 | `Lahvičky_web_hotové/OCH007250.jpg` | `638-2.jpg` — Umyju miminka |
| 62 | Umyju něžně (OCH008250) | 250 | `644-1.jpg` — Umyju nezne 250 | `Lahvičky_web_hotové/OCH008250.jpg` | `644.jpg` — Umyju něžně |
| 63 | Zorro ctitel (KRE004030) | 030 | `422-4.jpg` — Zorro ctitel 30 p | `Kelímky_web_hotové/KRE004030.jpg` | `422.jpg` — Zorro ctitel |
| 64 | Čokoládová drbna (PEE003180) | 180 | `948-3.jpg` — Čokoládová drbna 180ml p | `Kelímky_web_hotové/PEE003180.jpg` | `948-1.jpg` — cokoladova drbna |

## Things to decide / watch

1. **No unsized photo #2** (nothing to preserve, check that a drops / cup-interior photo is meant to exist): Moje Hebké, Očistím ruce - gel, Očistím ruce - sprej, Pozdravím slunce.
2. **Testers (`…T`) have no new file** in either folder. They use the top photo today, so after the swap they should point to the new top photo.
3. **Variants without a new file** (non-testers; the list below was made before `SEZ002005` was found and is therefore one variant longer than the final list of 16). If old size photos are deleted, these variants lose their photo unless re-pointed (fallback: the new top photo):

   - Důvěrný pan Růže: 1149/1
   - HladíMe: ZEN001250, ZEN001500
   - Modré z nebe: TON002500
   - Moje Moringová: PMA002050
   - Moje Ovesná: PMA003050
   - Moje Z nebe: PMA004050
   - Moje Čokoládová: PMA001050
   - Měsíční pohlazení: ZEN003250, ZEN003500
   - Ochráním bříška: OCH003500
   - Pohladím miminka: OCH005030, OCH005250
   - Svěží krása: TON003500
   - Svěží motýl: TON004500
   - Svěží teenka: TON001500

4. ~~Orphan file `ZEZ002005.jpg`~~ — **there is no such file.** The file is `Kelímky_web_hotové/SEZ002005.jpg` (Pozdravím slunce 5 ml); its name was misread in the listing. The photo was applied afterwards (see the photo-swap report).
5. **Cups' second photo:** Bára's note says textures (drops, smears, cup views) stay as they are; the folder `Pohledy do kelímků_nové 2026` contains *new* cup views. Not used in this table (decision D-3).
6. The new-file folders hold 119 cup files + 76 vial files = 195, Bára said 213 photos — the other ~18 are probably set photos or not yet in these two folders.

## Out of scope (no new file for any variant) — 142 products

- Sets / double shots (SA…): 45
- Gift packages (BAL…): 44
- Accessories / soaps / vouchers / gifts (DOP, POU, DAR, SLU…): 40
- Tests / second-grade / other: 7
- Chocolates (COK…): 6

This matches the meeting: sets, gift packages and chocolates are not touched. Full list is reproducible from the scan; second-grade / test products (TEST_*, `xxx test`, `…2. jakost`, `MAS2-…`) are among the last group.

## Log

- 2026-11-01 — Dry run generated (64 in scope, 142 out of scope). No shop data was changed.
