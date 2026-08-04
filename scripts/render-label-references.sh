#!/bin/bash

# Render reference label artwork (PNG) for the Terminal label-identification feature.
#
# The reference stickers live as gitignored PDFs in data/labels/{productCode}.pdf (dev
# machine only — see docs/superpowers/specs/2026-08-03-label-identification-terminal-design.md).
# This is a ONE-TIME offline step, like backend/tools/Anela.Heblo.LabelReferenceExtractor:
# it renders one PNG per FAMILY (the operator confirms a family visually; the 015/030 size
# variants share identical artwork) so the terminal can show the real label next to the
# product it proposes. The committed source of truth for the family->code mapping is
# label-references.json.
#
# Long ingredient lists span two PDF pages (page 1 ends with a "continued" arrow); those
# families get a second image {family}-2.png. Single-page families get only {family}.png.
# The frontend stacks page 1 + page 2 and hides a missing second page via onError.
#
# Usage: ./scripts/render-label-references.sh
#   DPI=400   render resolution (default 400 — legible INCI text at ~766px square, ~100 KB)
#
# Requires: pdftoppm, pdfinfo, jq (poppler + jq, e.g. `brew install poppler jq`).

set -euo pipefail

DPI="${DPI:-400}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REFERENCES_JSON="$REPO_ROOT/backend/src/Anela.Heblo.Application/Features/LabelIdentification/Data/label-references.json"
PDF_DIR="$REPO_ROOT/data/labels"
OUT_DIR="$REPO_ROOT/frontend/public/label-references"

for tool in pdftoppm pdfinfo jq; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "ERROR: '$tool' not found. Install poppler + jq (brew install poppler jq)." >&2
    exit 1
  fi
done

if [[ ! -f "$REFERENCES_JSON" ]]; then
  echo "ERROR: reference index not found: $REFERENCES_JSON" >&2
  exit 1
fi

if [[ ! -d "$PDF_DIR" ]]; then
  echo "ERROR: source PDFs not found: $PDF_DIR (gitignored; present only on the dev machine)." >&2
  exit 1
fi

mkdir -p "$OUT_DIR"

rendered=0
missing=0

# Each family renders from its representative code (codes[0]); every size in a family shares
# the same artwork, so any member is a valid representative.
while IFS=' ' read -r family code; do
  pdf="$PDF_DIR/$code.pdf"
  if [[ ! -f "$pdf" ]]; then
    echo "MISSING $family: $pdf" >&2
    missing=$((missing + 1))
    continue
  fi

  pages="$(pdfinfo "$pdf" | awk '/^Pages/{print $2}')"

  # Page 1 -> {family}.png; pages 2..N -> {family}-{n}.png.
  pdftoppm -png -r "$DPI" -f 1 -l 1 -singlefile "$pdf" "$OUT_DIR/$family"
  for ((p = 2; p <= pages; p++)); do
    pdftoppm -png -r "$DPI" -f "$p" -l "$p" -singlefile "$pdf" "$OUT_DIR/$family-$p"
  done

  echo "$family <- $code ($pages page(s))"
  rendered=$((rendered + 1))
done < <(jq -r '.[] | "\(.family) \(.codes[0])"' "$REFERENCES_JSON")

echo "----"
echo "Rendered $rendered families to $OUT_DIR at ${DPI} DPI."
if [[ "$missing" -gt 0 ]]; then
  echo "WARNING: $missing families had no source PDF." >&2
  exit 2
fi
