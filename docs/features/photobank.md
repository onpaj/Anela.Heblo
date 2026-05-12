# Fotobanka

Interní katalog fotek uložených na SharePointu. Aplikace si udržuje databázi metadat (název, cesta, štítky) a zobrazuje galerii s filtrováním. Fotky samotné zůstávají na SharePointu — aplikace je pouze indexuje.

**Rozsah:** 1 000–20 000 fotek.  
**Přístup:** Všichni přihlášení uživatelé mohou procházet galerii. Role `administrator` je potřeba pro správu nastavení.

---

## Jak to funguje

1. Admin nakonfiguruje jednu nebo více **kořenových složek** (Index Roots) — každá odkazuje na konkrétní složku na SharePointu pomocí Drive ID a cesty.
2. **Hangfire job** (každý den ve 3:00) volá Microsoft Graph delta API a synchronizuje všechny soubory z nakonfigurovaných složek do databáze.
3. **Tag rules** — admin nastaví pravidla, která automaticky přiřazují štítky fotkám podle jejich cesty (např. všechno v `/PROFI_FOCENI/Produkty/*` dostane štítek `produkty`).
4. Uživatelé procházejí galerii, filtrují podle štítků a otevírají fotky přímo na SharePointu.

---

## Galerie (všichni uživatelé)

**URL:** `/marketing/photobank`

- **Levý panel:** Vyhledávání dle názvu souboru + seznam štítků s počty. Výběr více štítků funguje jako AND filtr.
- **Mřížka fotek:** Miniatury načítané přímo z Graph API přes MSAL token uživatele. Stránkování po 48 fotkách.
- **Pravý panel (po kliknutí na fotku):**
  - Větší náhled
  - Název souboru a cesta
  - Štítky
  - Tlačítka „Otevřít na SharePointu" a „Kopírovat odkaz"
  - *(Pouze admin)* Přidání/odebrání štítků ručně

### Hromadné tagování (pouze admin)

**Umístění:** Tlačítko „Otagovat" s ikonou štítku v horní liště (vedle přepínače zobrazení).

**Jak funguje:**
1. Admin aplikuje filtry (vyhledávání, cesta složky, výběr štítků)
2. Klikne na tlačítko **„Otagovat"** — otevře se dialog
3. Dialog zobrazuje:
   - Shrnutí aktivních filtrů
   - Počet fotek, které odpovídají filtrům
   - Vstupní pole pro výběr štítku (s napovídáním ze seznamu existujících štítků)
4. Potvrzením se štítek přiřadí všem fotkám odpovídajícím filtrům

**Důležitá omezení:**
- Tlačítko je **vypnuté, pokud nejsou nastaveny žádné filtry**
- Systém má **tvrdý limit 5 000 fotek** na jednu operaci — pokud je fotek více, backend vrátí chybu s výzvou zúžit filtry
- Hromadně lze přidat pouze **jeden štítek na operaci** (hromadné odstranění není zatím podporováno)
- Štítky přidané hromadně jsou typu **`Manual`** — nikdy se neodstraní automatickou re-aplikací tag rules

---

## Nastavení (pouze admin)

**URL:** `/marketing/photobank/settings`

### Záložka Index Roots

Konfigurace složek, které se indexují.

**Přidání nové složky:**
1. **Cesta** — relativní cesta k složce v rámci document library, např. `/Grafika_interní/PROFI_FOCENI`
2. **Drive ID** — identifikátor document library v Graph API (viz níže, jak ho zjistit)
3. **Název** — volitelný popis pro přehlednost

> `Root Item ID` se doplní automaticky při prvním spuštění indexovacího jobu — není potřeba ho zadávat ručně.

Po uložení se složka indexuje při nejbližším spuštění jobu (3:00). Lze také spustit job ručně přes Hangfire dashboard.

**Jak zjistit Drive ID:**
- Jdi na libovolný soubor nebo složku ve správné document library
- Zkopíruj odkaz (Share)
- Nebo použij Graph Explorer: `GET https://graph.microsoft.com/v1.0/sites/anelacz.sharepoint.com:/sites/Marketing:/drives` a zkopíruj `id` z výsledku pro „Documents"

Pro Marketing site je Drive ID:
```
b!jj_-5-FohEybxp_61HKbJLmG6KD2FqJOmZeQYQNbxqrCWYOs1rorQ5BtZfHEjWKj
```

### Záložka Tag Rules

Pravidla pro automatické přiřazování štítků podle cesty.

- **Vzor cesty** — např. `/PROFI_FOCENI/Produkty/*` (hvězdička nahrazuje jeden segment cesty)
- **Štítek** — název štítku (automaticky převeden na malá písmena)
- **Pořadí** — nižší číslo = vyšší priorita (platí všechna shodná pravidla, ne jen první)

Tlačítko **Re-aplikovat pravidla** přepočítá pravidlové štítky pro všechny fotky v databázi (manuální štítky zůstanou nedotčeny).

---

## Synchronizace a štítky

### Jak probíhá indexace

- Job běží každý den ve 3:00
- Používá Graph API **delta endpoint** — první spuštění projde celou složku, další spuštění načtou pouze změny od předchozího běhu
- Přejmenování nebo přesunutí souboru na SharePointu se správně promítne (díky stabilnímu Graph item ID)
- Smazané soubory jsou odstraněny z databáze

### Typy štítků

| Typ | Zdroj | Chování při re-apply |
|-----|-------|----------------------|
| `Rule` | Tag rule pravidla | Přepočítávají se automaticky |
| `Manual` | Admin přidal ručně | Nikdy se nemažou |
| `AI` | Rezervováno pro fázi 2 | — |

---

## Budoucí rozšíření (fáze 2)

- **AI štítky** — Azure AI Vision pro automatické generování štítků z obsahu fotky
- Schéma databáze je připraveno (sloupec `Source` v tabulce `PhotoTag` má hodnotu `AI`)
