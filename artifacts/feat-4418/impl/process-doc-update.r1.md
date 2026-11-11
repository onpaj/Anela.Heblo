# Implementation: process-doc-update

## What was implemented
Updated the AMOUNT rule note in the feed process doc: patterns are parsed with InvariantCulture, dot decimal separator, comma not accepted (unparseable -> no match). Bumped verified_at to d2479f97 in both feed and module docs. Ran the index generator.

## Files created/modified
- `docs/processes/feed-invoice-classification.md` - AMOUNT parsing note, verified_at
- `docs/processes/module-invoice-classification.md` - verified_at
- `docs/processes/INDEX.md` - regenerated (no change needed)

## Tests
`python3 scripts/process-docs/check.py index` exits 0.

## How to verify
`python3 scripts/process-docs/check.py index`

## Notes
Module doc does not describe AMOUNT parsing, so only verified_at changed.

## PR Summary
Documented culture-invariant AMOUNT parsing in the invoice-classification process doc.

## Status
DONE
