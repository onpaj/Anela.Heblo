---
process: module-label-identification
kind: module
module: label-identification
summary: Warehouse terminal tool that tells staff which product an unmarked roll of stickers (etiquettes) belongs to, by photographing the label and matching its INCI ingredient list against a built-in reference set.
owns: []
verified_at: "5e993f9e2"
related:
  - flow-label-identification
---

# Label identification (Identifikace štítku)

## Purpose
Anela's product stickers (round etiquettes sold on rolls) print **only the INCI ingredient
list** — no product code, no name, no barcode. When a roll is found without its box or tag,
staff would otherwise have to compare ingredient lists by hand. This module lets an operator
photograph the sticker on the warehouse terminal and get the **product code and name** back.

A sticker identifies a product **family** (first six characters of the code, e.g. `KRE005`),
never its size: the `015`/`030` suffix is the sticker size, and both sizes carry the same
text. When a family exists in two sizes, the operator picks the size by hand.

Nothing is stored. The module has no database tables, no scheduled jobs and writes to no
business system; its only external call is the photo sent to Anthropic (Claude vision) for
reading the text.

## Users & screens
- **Warehouse / production staff** on the terminal (phone or tablet) — tile *Identifikace
  štítku* ("Vyfoťte štítek a zjistěte kód produktu") on `/terminal`, page
  `/terminal/label-identification`. Steps: *Vyfotit štítek* (opens the rear camera) →
  *Čtu štítek…* → either the code directly, a list of up to 3 candidates (*Vyberte produkt*,
  with score and the reference sticker image), or a size step (*Vyberte velikost*). Every
  candidate shows the reference artwork, tap to zoom (*Klepnutím zvětšíte*).
- API: `POST /api/label-identification/identify` (multipart field `photo`).
- Access: the endpoint requires **Katalog** read (`Feature.Products_Catalog`); the Terminal
  menu entry is gated by `Feature.Warehouse_Logistics` read. An operator needs both.
- No MCP tool, no dashboard tile.

## Processes
- `flow-label-identification` — photo → Claude vision OCR → fuzzy match against the
  embedded reference index → product code(s) with names from the catalogue. On demand, per
  photo.

No other user actions. Refreshing the reference data (new or changed sticker artwork) is a
manual developer step, described in `flow-label-identification` under *Reference data*.

## Data owned
- `Features/LabelIdentification/Data/label-references.json` — **embedded resource**, not a
  table: 25 families / 37 product codes (KRE001–007, MAS001–009, OCH004/006/009, PEE002,
  PMA001–005), each with the normalized INCI text extracted from the print-ready sticker
  PDFs. Loaded once per process into the singleton `LabelReferenceIndex`.
- `frontend/public/label-references/{family}.png` (+ `{family}-2.png` for two-page
  ingredient lists) — static reference artwork shown to the operator, 34 PNGs.
- No database tables, caches or blob containers.

## External systems
- **Anthropic Messages API** (`https://api.anthropic.com/v1/messages`) — outbound, one call
  per photo, via the shared `IChatClient` (`AnthropicChatClient`). The photo (JPEG, longest
  edge ≤ 2048 px) and a fixed prompt are sent; the reply is the ingredient list as text.

## Dependencies
- **Catalog** (`ICatalogRepository.GetByIdsAsync`) — product names for the matched codes,
  read from the in-memory catalogue cache (see the catalog module's cache-load process). A
  code missing from the catalogue is still returned, with an empty name.
- **Anthropic adapter** (`Anela.Heblo.Adapters.Anthropic`) — model and API key come from
  shared configuration (`KnowledgeBase:ChatModel`, `Anthropic:ApiKey`).
- No module reads from this one.

## Known quirks
- **Fixed reference set.** Only the 37 codes in `label-references.json` can ever be
  identified. A new product, or changed sticker artwork, is invisible until a developer
  re-runs the offline extractor on the source PDFs (gitignored, kept only on the developer's
  machine under `data/labels/`) and the app is redeployed. The catalogue is not consulted to
  build the index.
- **Size is never detected.** For the 12 two-size families the operator must pick 015 or 030;
  a wrong pick is not caught.
- **Shared AI model setting.** The OCR uses `KnowledgeBase:ChatModel` (repo default
  `claude-sonnet-4-6`) and `KnowledgeBase:ChatMaxTokens` (1024) — changing the knowledge-base
  chat model also changes the model reading labels.
- **Two different permissions** guard the menu (`Warehouse_Logistics`) and the endpoint
  (`Products_Catalog`); a terminal user without Katalog read sees the tile but every photo
  fails with HTTP 403.
- Results are not logged anywhere persistent (only application logs: decision + top family).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/LabelIdentification/LabelIdentificationModule.cs` — DI registration
- `backend/src/Anela.Heblo.Application/Features/LabelIdentification/UseCases/IdentifyLabel/IdentifyLabelHandler.cs` — the whole request flow
- `backend/src/Anela.Heblo.API/Controllers/LabelIdentificationController.cs` — endpoint, upload limits
- `frontend/src/components/terminal/label-identification/LabelIdentificationScreen.tsx` — terminal UI states
- `backend/tools/Anela.Heblo.LabelReferenceExtractor/Program.cs` — offline reference-index builder
- `docs/superpowers/specs/2026-08-03-label-identification-terminal-design.md` — original design and measurements
