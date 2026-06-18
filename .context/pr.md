# PR Context

- **PR**: #3185 — fix: Widen SmartsuppConversations varchar(500) columns to text
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3185
- **Branch**: `feature/fix/pr3161-extend-db-schema` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +4367 / -17 across 5 files
- **Absorbed**: backmerged with `main`, all tests passing (real failures resolved; remaining 57 failures are Docker/Testcontainers + live-API integration tests that cannot run in this sandbox)

## Description

## Problem

`POST SmartsuppWebhook/Receive` returned HTTP 500 in bursts when Smartsupp sent content exceeding the `varchar(500)` column limit in the Smartsupp persistence schema (`Npgsql.PostgresException 22001`).

PR #3161 attempted to fix this by widening some columns **and** adding app-layer string truncation. That premise is invalid — truncation causes silent data loss.

## Fix

Extend the DB schema only. Four columns in `SmartsuppConversations` widened from `varchar(500)` → `text`:

- `Subject`
- `Referer`
- `ContactAvatarUrl`
- `LastMessagePreview`

Also removes the inline `LastMessagePreview` 200-char truncation that was in `SmartsuppPayloadMapper`.

## Test plan

- [ ] Run migration against staging: `dotnet ef database update`
- [ ] Replay any failed webhook events via `tools/SmartsuppWebhookReplay`
- [ ] Verify no 500s on `POST SmartsuppWebhook/Receive` in Application Insights

Closes #3069

## Absorb notes

- Backmerge of `origin/main` merged cleanly (no conflicts).
- One real test failure surfaced: `SmartsuppPayloadMapperTests.MapConversation_TruncatesLastMessagePreview_AtTwoHundredChars` — a stale test from `main` asserting the truncation this PR intentionally removed. Updated it to `MapConversation_PreservesFullLastMessagePreview_WithoutTruncation`, asserting the full preview is preserved.
- Remaining 57 test failures are all `*IntegrationTests` requiring Docker/Testcontainers (Postgres) or live external services (Flexi ERP, Shoptet API) — environment limitations, not regressions.
