# Code Review: extract-helpers

## Summary
Helpers moved verbatim into stockOperationsUtils.ts with the specified signatures; the page no longer defines them and imports them. getStuckMessage and other code are untouched. tsc shows no src errors and eslint is clean.

## Review Result: PASS

### task: extract-helpers
**Status:** PASS

## Overall Notes
The only cast is `createdAt as Date | string`, needed because the new typed parameter makes createdAt optional.
