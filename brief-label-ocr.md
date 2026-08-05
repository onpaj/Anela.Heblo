# Feature Brief: Label OCR Product Identifier (one-shot app)

**Status: Superseded.** This brief's standalone Python/FastAPI proposal was superseded
before implementation by
[`docs/superpowers/specs/2026-08-03-label-identification-terminal-design.md`](docs/superpowers/specs/2026-08-03-label-identification-terminal-design.md)
(catalogue analysis showed 11 reference PDF pairs are byte-identical after
normalization, making the auto-confirm margin logic in this brief unachievable for
those products; the approved design instead builds on Heblo's existing terminal/.NET
infrastructure). The feature was implemented per that design and shipped in
[#3847](https://github.com/onpaj/Anela.Heblo/pull/3847),
"feat(terminal): identify products by photographing their INCI label". This document is
kept only as a historical record of the original proposal — do not use it as a current
spec.

## Problem Statement

Anela Heblo has rolls of product etiquettes (round stickers) that carry only the INCI
ingredient composition — no product code, name, or barcode. When a roll needs to be
identified (e.g. in production or warehouse), the only way is to manually compare the
ingredient text against product composition records, which is slow and error-prone.

We need a tool where the user photographs a label with their phone and immediately gets
back the product code.

This is a **standalone one-shot application** — it is NOT part of the Heblo monorepo
app/deployment. It lives in its own folder, runs locally or is deployed manually to
Azure, and has no authentication.

## Goals

- Photo of a label roll → product code in under ~10 seconds, in one tap.
- ≥95% correct auto-identification on reasonably sharp photos; ambiguous cases always
  resolvable via a top-3 candidate pick list (never a silent wrong answer).
- Zero-maintenance reference data: drop `{productCode}.pdf` label artwork files into a
  folder, restart, done.

## Functional Requirements

### Reference index (startup)
- Scan a data folder for label artwork PDFs named `{productCode}.pdf`.
- Extract the printed text from each PDF's text layer (they are generated artwork, so a
  text layer is expected). This gives the *exact* printed ingredient text per product.
- Normalize each composition into a canonical form: strip `Ingredients:` prefix,
  lowercase, collapse whitespace/hyphenation, split into an ingredient token list.
- Optionally load an Excel file (`products.xlsx`: product code + composition text +
  product name) as fallback for products without a PDF and as the source of display
  names. A PDF-derived entry wins over an Excel entry for the same code.
- Log and expose (on a `/health` or index page) any PDFs whose text extraction failed
  or came back empty, so bad artwork files are visible instead of silently missing.

### Identification flow
- Single mobile-first web page: big "Take photo" button using
  `<input type="file" accept="image/*" capture="environment">`.
- Backend `POST /api/identify` (multipart image):
  1. Downscale/compress the photo server-side (max ~2048 px longest edge, JPEG) to keep
     the vision call fast and cheap.
  2. Send to **Claude Haiku (vision)** with a prompt: read the sharpest/most complete
     label in the photo and return the ingredient list as a single comma-separated
     line (labels appear on a roll — all visible labels are identical; rotation, blur
     and ghost text from neighboring stickers are expected).
  3. Normalize the returned text the same way as the index.
  4. Fuzzy-match against every indexed product using RapidFuzz
     (`token_set_ratio` on the normalized ingredient string; ingredient-set overlap as
     a secondary signal).
  5. Return top-3 candidates with scores + the raw extracted text.
- Decision logic:
  - Best score ≥ high threshold (default 90) AND margin to 2nd candidate ≥ 5 points →
    **auto-confirmed**: show product code + name prominently (green).
  - Otherwise → show top-3 candidates with scores; user taps the correct one to
    confirm, or "none of these".
- Thresholds configurable via env vars.

### Caching / learning
- SQLite database (single file) stores every identification: normalized OCR text,
  matched product code, score, auto/manual confirmation flag, timestamp.
- Before matching against the PDF index, match the OCR text against previously
  **confirmed** texts (higher threshold, e.g. 95). A hit returns that product code
  immediately as auto-confirmed — repeated rolls of the same label skip the
  confirmation step even when the artwork text and OCR text drift slightly.
- Note: the Haiku vision call itself always runs (each photo is new pixels); the cache
  removes the *decision* friction, not the API call.

### UI (single page, no build step)
- State 1: camera button.
- State 2: spinner ("Reading label…").
- State 3a: auto-confirmed result — product code (large), product name, score.
- State 3b: top-3 candidate list with tap-to-confirm + "none of these".
- "Scan another" resets to state 1.
- Plain HTML + vanilla JS served by FastAPI (`/static`), styled minimally for phone
  screens. No React, no bundler.

## Non-Functional Requirements

- End-to-end identification ≤ ~10 s on mobile data (dominated by upload + Haiku call).
- Cost: one Haiku vision call per photo (fractions of a cent); no other paid services.
- Works in a phone browser (iOS Safari + Android Chrome); no app install.
- No authentication (internal tool, obscure URL; optionally protectable later at the
  Azure level — out of scope now).
- Graceful errors: unreadable photo → clear "try again, get closer/steadier" message;
  Anthropic API failure → readable error, no crash.

## Technical Constraints

- **Python 3.12 + FastAPI + Uvicorn** (user's choice: "python api").
- `anthropic` SDK, model `claude-haiku-4-5-20251001`; API key via `ANTHROPIC_API_KEY`
  env var (user provides the key).
- `pdfplumber` (or `pypdf`) for PDF text extraction, `rapidfuzz` for matching,
  `Pillow` for image downscaling, `openpyxl` for the optional Excel.
- SQLite via stdlib `sqlite3` — no ORM, no external DB.
- Standalone folder in the repo: `tools/label-identifier/` with its own
  `requirements.txt`, `README.md`, and `Dockerfile`. It must not touch the .NET
  backend, React frontend, or CI/CD of the main app.
- Deployment: manual — run locally (`uvicorn app:app`) or push the Docker image and
  create an Azure Web App for Containers by hand. No pipeline changes.
- Reference data folder layout (mounted/copied, not committed):
  `data/labels/*.pdf`, optional `data/products.xlsx`, `data/cache.db` (created).

## Out of Scope

- Integration into Heblo (backend modules, frontend, auth, deployment pipeline).
- Local/offline OCR (Tesseract/PaddleOCR) — noted as a possible future fallback; the
  matching layer is engine-agnostic, so it can be added behind a flag later.
- Label detection/cropping/perspective correction — the vision model handles this.
- Automated CI/CD, E2E tests, monitoring, user management.
- Editing reference data through the UI (data is prepared out-of-band by the user).

## Success Criteria

- The two provided sample photos (massage-oil label, powder/deodorant label) identify
  the correct product code once their PDFs are in the data folder.
- A deliberately ambiguous case (two similar compositions) presents a candidate list
  instead of a confident wrong answer.
- Second photo of an already-confirmed label returns auto-confirmed via cache.
- A PDF with no extractable text is reported at startup, not silently ignored.
- Runs from a clean checkout with: `pip install -r requirements.txt`, set
  `ANTHROPIC_API_KEY`, drop PDFs into `data/labels/`, `uvicorn app:app`.

## Additional Context

- Sample labels are INCI (Latin/English), straight-line centered text inside a circle,
  small font; photos are handheld, often rotated, with ghost text from adjacent
  stickers on the roll. All labels in one photo are the same product — reading
  overlapping text is harmless for bag-of-ingredients matching.
- Excel composition text may drift from printed text (ordering/wording), which is why
  the PDFs are the primary matching reference and Excel is fallback + display names.

## Implementation Plan

1. **Scaffold** `tools/label-identifier/`: `app.py` (FastAPI), `indexer.py`,
   `matcher.py`, `ocr.py`, `store.py`, `static/index.html`, `requirements.txt`,
   `README.md`, `Dockerfile`.
   ✓ Check: `uvicorn app:app` serves the page; `/health` returns index stats.
2. **Indexer**: PDF text extraction + normalization + optional Excel merge; unit tests
   with a synthetic PDF and the two sample compositions.
   ✓ Check: `pytest` green; `/health` lists N products, 0 failures.
3. **Matcher**: normalization + RapidFuzz scoring + threshold/margin decision logic;
   unit tests including mangled-OCR variants (dropped chars, duplicated text) and a
   near-duplicate-composition ambiguity case.
   ✓ Check: mangled text still matches right product; ambiguous case yields top-3.
4. **OCR module**: image downscale + Haiku vision call + response cleanup; mockable
   interface so tests don't hit the API.
   ✓ Check: manual run against the two sample photos returns plausible ingredient text.
5. **Cache store**: SQLite schema + confirmed-match lookup + write-on-confirm; unit
   tests.
   ✓ Check: repeat identification of same text short-circuits to auto-confirmed.
6. **Wire up UI + endpoint**, error states, config via env vars.
   ✓ Check: full flow works locally end-to-end with a real photo from a phone.
7. **Docker + README**: build image, document local run, data folder layout, and
   manual Azure Web App deployment steps.
   ✓ Check: `docker run` with mounted `data/` works identically.
