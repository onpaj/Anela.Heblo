### Question 1
Does the Plaud authentication model support a non-interactive refresh token flow, or is every re-auth interactive? If interactive-only, FR-3 is not implementable and FR-4's alerting becomes the primary mitigation. Assumption made: a refresh path exists.

**Answer:** Yes. Plaud supports a non-interactive OAuth refresh flow. It is already implemented in `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshClient.cs`, which `POST`s the current `refresh_token` to `https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh` and receives a new `{ access_token, refresh_token, expires_at }` tuple. FR-3 is implementable and must be retained in the spec.

**Rationale:** The refresh endpoint, request shape, and response contract are already exercised by the existing `PlaudTokenRefreshJob` and its unit tests (`PlaudTokenRefreshClientTests.cs`, `PlaudTokenRefreshJobTests.cs`). The weekly job exists but is `DefaultIsEnabled = false`, which explains why production drifted into the expired-token state — the refresh capability is built but not active.

### Question 2
What is the current KV secret name for the Plaud session token in `kv-heblo-prod`? The runbook (FR-1) and config defaults need the exact existing name; renaming may require a one-time migration.

**Answer:** The single secret is `Plaud--TokensJson` in both `kv-heblo-prod` and `kv-heblo-stg`. It stores the full JSON blob `{ "access_token": "...", "refresh_token": "...", "expires_at": <unix-seconds> }` — not two separate secrets. The spec's proposed `Plaud--SessionToken` / `Plaud--RefreshToken` split must be replaced with the single `Plaud--TokensJson` secret, and `PlaudCredentialsOptions.RefreshTokenSecretName` should be dropped.

**Rationale:** Confirmed by `PlaudTokenRefreshJob.cs:74` (`_secretClient.SetSecretAsync("Plaud--TokensJson", newJson, ct)`), `docs/integrations/plaud-token-auto-refresh.md`, the existing test fixture (`PlaudTokenRefreshJobTests.cs:168`), and the bootstrapper's `PlaudOptions.TokensJson` configuration binding. No renaming is needed; no migration is required.

### Question 3
What is the typical Plaud session token lifetime? Required to choose a sensible default for `ExpiryBuffer` and the FR-4 alert threshold.

**Answer:** The `access_token` is short-lived (the CLI auto-refreshes it on every call when the refresh token is valid). The hard cap that triggers `AUTH_FAILED` in production is the **`refresh_token` TTL of approximately 30 days** (observed, not officially documented by Plaud). Use these defaults: `ExpiryBuffer = 72 hours` (well below the weekly auto-refresh cadence so a single missed run still leaves room), and FR-4 alert threshold = **count > 0 within a 15-minute window, evaluated every 5 minutes** — matching the existing `Heblo-Plaud-AuthExpired` rule.

**Rationale:** Documented observation in `docs/integrations/plaud-token-auto-refresh.md:72-74` ("hard TTL appears to be ~30 days") and confirmed by the existing weekly refresh cron (`0 4 * * 0`) being well inside that window. The 24h buffer originally proposed is too tight given the weekly refresh cadence — 72h gives one full retry opportunity before expiry. The 5/15 alert threshold matches the existing production rule and triage cadence.

### Question 4
Is there a Plaud sandbox or staging environment usable for the FR-5 integration test, or must the test mock the Plaud auth endpoint?

**Answer:** There is no Plaud sandbox. FR-5 integration tests must mock the refresh endpoint with a fake `HttpMessageHandler` (the pattern already used in `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenRefreshClientTests.cs`). Hitting the live `platform.plaud.ai` refresh endpoint from CI is explicitly out of scope — it would rotate the production refresh token and break the running app.

**Rationale:** Plaud has not published a sandbox environment and the codebase has historically treated the live endpoint as the only target (consistent with the same constraint documented for the Shoptet integration in CLAUDE.md). The existing test infrastructure already proves the mocked approach is sufficient for verifying the refresh round-trip.

### Question 5
Which Application Insights alert channel should FR-4 route to — the same channel as the existing weekly telemetry-anomaly digest, or a higher-urgency channel (since the failure is active in production)?

**Answer:** Reuse the existing action group **`ag-heblo-ops`** (email `ondra@anela.cz`), which already backs the live `Heblo-Plaud-AuthExpired` alert. Severity = 2 (Warning) for `PlaudAuthExpiredException`. Add a separate, lower-severity (Sev 3 / Informational) alert for `PlaudTokenNearExpiry` so it never wakes anyone but still surfaces in the inbox. Do not create new channels.

**Rationale:** This is a solo-developer project (CLAUDE.md: "Solo developer + AI-assisted PR review"), so there is no on-call rotation to split traffic across. The existing `Heblo-Plaud-AuthExpired` rule already defines this routing in `docs/superpowers/plans/2026-05-27-plaud-circuit-breaker-and-monitoring.md:573-575`; the new alerts should reuse it rather than fragment ops signals.

### Question 6
Should the immediate FR-1 rotation be performed before or after the FR-2/FR-3 code lands? Recommendation: rotate immediately to restore production, then ship the code fix; confirm this sequencing.

**Answer:** Rotate first, code-fix second. Order: (1) Operator runs `plaud login` locally, updates `Plaud--TokensJson` in `kv-heblo-prod`, restarts the `Heblo` Web App, verifies exceptions stop in App Insights. (2) **Enable** the existing `plaud-token-refresh` Hangfire job in production via the Background Jobs admin UI (it is `DefaultIsEnabled = false` today, which is the root cause of this incident's recurrence). (3) Ship FR-2/FR-3 (proactive in-line refresh in `PlaudCliClient`) so the system no longer depends on the weekly cron firing successfully.

**Rationale:** Production is actively failing, so manual rotation is the only path to restore service within the 15-minute target in FR-1. Step (2) is critical and currently missing from the spec — the failure occurred precisely because the existing refresh job was never enabled; without flipping that flag, the system will re-enter this state ~30 days after every manual rotation. The code fix (FR-2/FR-3) hardens against the case where the weekly job itself fails.
