# Make.com: Zpracování plaud.ai transkripcí → Loop + Planner

**GitHub Issue:** #409
**Připraveno:** 2026-03-21
**Typ:** Externí Make.com scénář (neinteraguje přímo s Anela Heblo API)

---

## Přehled

Automatizovaný Make.com scénář, který zpracovává přepisy schůzek z plaud.ai doručené emailem na sdílenou schránku `automation@anela.cz`. Scénář pomocí AI extrahuje shrnutí schůzky a akční body, vytvoří stránku v Microsoft Loop a založí úkoly v Microsoft Planner.

### Cíle

- Eliminovat manuální zpracování zápisů ze schůzek
- Automaticky generovat strukturované meeting notes v Loop
- Automaticky vytvářet akční úkoly v Planneru s přiřazením konkrétním lidem

---

## Trigger

| Parametr | Hodnota |
|----------|---------|
| Modul | Microsoft 365 Email — Watch Emails |
| Schránka | `automation@anela.cz` (shared mailbox) |
| Podmínka | Nový email s TXT přílohou |
| Povolení odesílatelé | `ondra@anela.cz`, `eliska@anela.cz`, `andy@anela.cz` |

---

## Tok scénáře (krok po kroku)

### Krok 1: Watch Email

- Trigger na nový email v `automation@anela.cz`
- Zachytí: odesílatele, předmět, přílohy

### Krok 2: Stažení TXT přílohy

- **Modul:** Microsoft 365 Email — Download Attachment
- Stáhne první TXT přílohu z emailu
- Přečte obsah jako plain text

### Krok 3: AI extrakce (OpenAI modul)

- **Modul:** OpenAI — Create a Completion (nativní Make.com modul)
- **Model:** GPT-4o (nebo novější)
- **Vstup:** Obsah TXT transkripce

**Prompt:**

```
Jsi asistent pro zpracování zápisů z obchodních schůzek. Analyzuj přiloženou transkripci a extrahuj strukturovaná data ve formátu JSON.

Extrahuj:
1. datum - datum schůzky ve formátu YYYY-MM-DD (z kontextu transkripce; pokud není jasné, použij dnešní datum)
2. cas - čas schůzky ve formátu HH:MM (nebo null pokud není zmíněn)
3. tema - krátký název/téma schůzky (max 1 věta, max 80 znaků)
4. shrnuti - pole bullet points s klíčovými body diskuse (max 10 bodů)
5. akce - pole akčních bodů, každý s:
   - popis: co se má udělat (stručně, max 120 znaků)
   - prirazeni: jméno osoby odpovědné za úkol (přesně jak se jmenuje v transkripci), nebo null

Odpověz POUZE validním JSON objektem, bez markdown, bez komentářů.

Transkripce:
{{2.data}}
```

**Výstupní formát JSON:**

```json
{
  "datum": "2026-03-20",
  "cas": "14:00",
  "tema": "Plánování jarní kolekce",
  "shrnuti": [
    "Diskuse o nových produktech pro jarní sezónu",
    "Rozhodnutí o dodavateli obalů",
    "Termín launche stanoven na duben"
  ],
  "akce": [
    { "popis": "Připravit návrh obalů", "prirazeni": "Petra" },
    { "popis": "Objednat vzorky surovin", "prirazeni": "Ondra" },
    { "popis": "Aktualizovat ceník", "prirazeni": null }
  ]
}
```

### Krok 4: Mapování jmen na emaily

- **Modul:** Tools — Set Variable (JSON parse)
- Inline JSON mapovací tabulka v Make.com modulu

**Mapovací tabulka:**

```json
{
  "Ondra": "ondra@anela.cz",
  "Eliška": "eliska@anela.cz",
  "Andy": "andy@anela.cz",
  "Petra": "petra@anela.cz"
}
```

**Fallback logika:**

- `prirazeni` je `null` → přiřadit email odesílatele původního emailu
- Jméno není v mapovací tabulce → přiřadit email odesílatele původního emailu

### Krok 5: Vytvoření Loop stránky

- **Modul:** HTTP — Make a Request (Make.com nemá nativní Loop modul)
- **API:** Microsoft Graph API
- **Endpoint:** `POST https://graph.microsoft.com/v1.0/...` *(viz poznámky o dostupnosti Loop API níže)*
- **Autentizace:** OAuth 2.0 přes Azure AD App Registration

**Struktura Loop stránky:**

```markdown
# [Téma schůzky]
📅 Datum: [datum] | ⏰ Čas: [čas]

## Shrnutí
- [bod 1]
- [bod 2]
- [bod 3]

## Akční body
- [ ] [popis] → [přiřazená osoba]
- [ ] [popis] → [přiřazená osoba]
```

- Výstup: URL vytvořené Loop stránky (předávána do kroku 7)

### Krok 6: Iterator přes akční body

- **Modul:** Flow Control — Iterator
- Iteruje přes pole `akce` z kroku 3

### Krok 7: Vytvoření Planner tasku

- **Modul:** Microsoft 365 Planner — Create a Task (nativní Make.com modul)

| Pole | Hodnota |
|------|---------|
| Plan ID | `{{PLACEHOLDER_PLAN_ID}}` |
| Bucket ID | `{{PLACEHOLDER_BUCKET_ID}}` |
| Title | Popis akčního bodu z kroku 6 |
| Assigned To | Email z mapovací tabulky (krok 4) |
| Description | Téma + datum + odkaz na Loop stránku z kroku 5 |
| Due Date | Nenastavovat (lze doplnit později) |

**Šablona description:**

```
Schůzka: [téma] ([datum])
Loop zápis: [URL Loop stránky z kroku 5]
```

### Krok 8: Error Handler

- **Trigger:** Selhání kteréhokoliv kroku 3–7
- **Modul:** Microsoft 365 Email — Send an Email
- **Komu:** Odesílatel původního emailu

**Email při chybě:**

| Pole | Hodnota |
|------|---------|
| Předmět | `[CHYBA] Zpracování transkripce selhalo — [původní předmět]` |
| Tělo | Popis chyby + název kroku kde selhalo |
| Příloha | Původní TXT transkripce |

---

## Přehled Make.com modulů

| Krok | Modul | Typ |
|------|-------|-----|
| 1 | Microsoft 365 Email — Watch Emails | Trigger |
| 2 | Microsoft 365 Email — Download Attachment | Action |
| 3 | OpenAI — Create a Completion | Action |
| 4 | Tools — Set Variable (JSON parse) | Utility |
| 5 | HTTP — Make a Request (Graph API) | Action |
| 6 | Flow Control — Iterator | Flow |
| 7 | Microsoft 365 Planner — Create a Task | Action |
| 8 | Microsoft 365 Email — Send an Email | Error handler |

---

## Mapování speaker → email

| Jméno v transkripci | Email | Poznámka |
|---------------------|-------|---------|
| Ondra | ondra@anela.cz | Také fallback odesílatel |
| Eliška | eliska@anela.cz | Také fallback odesílatel |
| Andy | andy@anela.cz | Také fallback odesílatel |
| Petra | petra@anela.cz | |

> Doplnit dle potřeby (max ~10 záznamů — inline JSON je dostatečné řešení).

---

## Azure AD App Registration

Nutno vytvořit novou registraci v Azure AD pro Make.com autentizaci.

| Parametr | Hodnota |
|----------|---------|
| App Registration | Nová registrace pro Make.com scénář |
| Client ID | `{{PLACEHOLDER_CLIENT_ID}}` |
| Tenant ID | `{{PLACEHOLDER_TENANT_ID}}` |

**Required permissions:**

| Permission | Scope | Důvod |
|-----------|-------|-------|
| `Mail.Read` | Application | Čtení shared mailboxu |
| `Mail.Send` | Application | Odesílání error notifikací |
| `Tasks.ReadWrite` | Application | Vytváření Planner tasků |
| Loop/OneNote scope | Application | Vytváření Loop stránek (ověřit aktuální stav) |

---

## Otevřené body

- [ ] **Azure AD App Registration** — vytvořit a doplnit Client ID + Tenant ID
- [ ] **Planner Plan ID** — specifikovat cílový plán (nebo vytvořit nový)
- [ ] **Planner Bucket ID** — specifikovat cílový bucket (nebo vytvořit nový)
- [ ] **Loop API dostupnost** — ověřit, zda Microsoft Graph API pro Loop pages je v GA nebo preview:
  - Primární: Loop page creation přes Graph API
  - Fallback A: OneNote stránka (`POST /me/onenote/pages`)
  - Fallback B: SharePoint page
- [ ] **Kompletní mapovací tabulka** — doplnit všechna jména včetně diakritiky i bez
- [ ] **OpenAI prompt** — otestovat na reálných českých transkriptech a doladit extrakci JSON
- [ ] **Due date strategie** — rozhodnout o termínech úkolů (z transkripce / fixní +7 dní / vůbec)

---

## Integrace s Anela Heblo Knowledge Base (budoucí)

Scénář aktuálně neinteraguje s Anela Heblo API. Potenciální budoucí integrace:

### A. Automatická archivace transkripcí do Knowledge Base

Transkripci lze odeslat do Knowledge Base přes existující endpoint:

```
POST /api/knowledgebase/documents/upload
Authorization: Bearer <Entra ID token>
Content-Type: multipart/form-data

file: <TXT soubor transkripce>
```

**Autentizace pro Make.com:**
- Použít OAuth 2.0 Client Credentials flow (service principal)
- Make.com HTTP modul podporuje Bearer token autentizaci
- Vyžaduje `KnowledgeBaseUpload` roli pro service principal

**Výhody:**
- Transkripce budou vyhledatelné přes `SearchKnowledgeBase` MCP tool
- AI asistent bude moci odpovídat na otázky o historii schůzek
- Propojení s `AskKnowledgeBase` pro dotazování nad meeting notes

### B. Přidání kroku do Make.com scénáře

Po kroku 2 (stažení TXT přílohy) přidat:

- **Krok 2b:** HTTP POST na `/api/knowledgebase/documents/upload`
- Soubor pojmenovat: `transkripce-{datum}-{tema-slugified}.txt`
- Error handler: Selhání nahrání do KB neblokuje zbytek scénáře (non-critical)
