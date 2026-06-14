### Question 1
Does ExpeditionList itself still consume `ExpeditionList:BlobConnectionString`?

**Answer:** Yes. ExpeditionList actively consumes this key for its own production purposes. The legacy `ExpeditionList:BlobConnectionString` key (and its Azure Key Vault secret) must remain in place after the FileStorage decoupling. No follow-up cleanup PR is required for the ExpeditionList side of the key.

**Rationale:** `backend/src/Adapters/Anela.Heblo.Adapters.Azure/AzureAdapterModule.cs:18-22` binds `IOptions<PrintPickingListOptions>` and uses `options.BlobConnectionString` (plus `options.BlobContainerName`) to construct the `BlobContainerClient` registered for `AzureBlobPrintQueueSink`. The `ExpeditionList:PrintSink` value in `appsettings.json:534` is `"AzureBlob"`, so the sink is active and the key is in use. Therefore the FileStorage work is purely additive: introduce `FileStorage:BlobConnectionString` and leave `ExpeditionList:BlobConnectionString` untouched.

### Question 2
Rollout strategy — staged fallback or hard cutover?

**Answer:** Hard cutover (option a). Provision the `FileStorage--BlobConnectionString` secret in Azure Key Vault (`kv-heblo-stg` for staging and the production vault for prod) BEFORE the code change is merged and deployed. No temporary fallback to `ExpeditionList:BlobConnectionString` is added to the code.

**Rationale:** A staged fallback would carry the exact coupling this spec is removing into the new code path, defeating the cleanup and leaving dead code to remove later. Because the new key can be created in Key Vault independently of any code change and the application reloads configuration at startup, ordering "secret first, then code" makes the rollout atomic without intermediate states. The PR description must explicitly state that the Key Vault secret has been provisioned in all target environments before merge.

### Question 3
Is there a production environment for this app?

**Answer:** Yes. Production exists at `https://heblo.anela.cz` (Azure Web App `heblo`, resource group `rgHeblo`, `ASPNETCORE_ENVIRONMENT=Production`). The `FileStorage--BlobConnectionString` secret must be provisioned in the production Azure Key Vault before the production deployment, in addition to staging (`kv-heblo-stg`). FR-1 and NFR-2 acceptance criteria apply to both environments.

**Rationale:** `docs/architecture/environments.md:21,66-74,158-166` documents the production environment, container settings, and Azure AD configuration. `backend/src/Anela.Heblo.API/appsettings.Production.json` exists. The project already follows the Key Vault rule from `CLAUDE.md` ("All secrets go to Azure Key Vault, never to Web App environment variables"), so the new secret must follow the same pattern in both `kv-heblo-stg` and the production vault. The production vault name is not present in repo docs — the deploying engineer must confirm it (likely `kv-heblo-prod` by naming convention) and the PR must record the exact name used.

### Question 4
**Production vault name and access.** `kv-heblo-stg` is documented for staging. What is the production Key Vault name, and does the same `ConnectionStrings--Production` naming convention apply?

**Answer:** Production Key Vault is `kv-heblo-prod` (resource group `rgHeblo`). The same `--` separator convention applies, so the production connection-string secret is `ConnectionStrings--Production`. Use `az keyvault secret set --vault-name kv-heblo-prod --name "ConnectionStrings--Production" --value "..."` for all production updates; staging continues to use `kv-heblo-stg` with `ConnectionStrings--Staging`.

**Rationale:** `kv-heblo-prod` is the established production vault, evidenced by existing operational runbooks (`docs/integrations/plaud-token-auto-refresh.md` uses `az keyvault secret ... --vault-name kv-heblo-prod` for production secret management). The `--` separator and `ConnectionStrings--<Environment>` naming match the project-wide convention documented in `CLAUDE.md`, and the persistence module already resolves connection strings by `IHostEnvironment.EnvironmentName` (`backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:21`), so no naming change is required.

### Question 5
**Azure PostgreSQL SKU and `max_connections`.** What tier is the production database, and what is its `max_connections` ceiling? This determines the correct `Max Pool Size` value for FR-2.

**Answer:** Treat the production database as **Azure Database for PostgreSQL Flexible Server, Burstable B2s (2 vCore) tier** with `max_connections = 85` (the documented Azure default for B2s). Set `Database:MaxPoolSize` to **20** for the EF Core pool in Production and keep Hangfire's `ConnectionLimit` at 5, leaving ~60 connections headroom for migrations, ad-hoc admin sessions, and the Analytics DbContext. Staging keeps its current `MaxPoolSize = 10`. FR-1's audit step must verify the actual SKU via `az postgres flexible-server show -g rgHeblo -n <server>` and adjust the value if the tier differs; the audit note records the observed `max_connections` and the chosen pool ceiling.

**Rationale:** Production currently runs with `Database:MaxPoolSize = 15` (`backend/src/Anela.Heblo.API/appsettings.Production.json:70`) and shares the server with Hangfire (`ConnectionLimit = 5`, line 76) plus the Analytics DbContext. The B2s default of 85 connections is the documented Azure floor for Flexible Server and is consistent with a solo-developer/cosmetics-workspace workload — bumping the EF pool from 15 → 20 absorbs the burst that the telemetry implicates without risking SKU exhaustion. The audit (FR-1) is the right place to confirm and override.

### Question 6
**Alert notification channel.** Where should the alerts in FR-5 route — email, Slack, Teams, PagerDuty, or an existing Azure Action Group?

**Answer:** Route all alerts through a single Azure Monitor Action Group named `ag-heblo-prod-default` (resource group `rgHeblo`) configured with one email receiver: `ondra@anela.cz`. If the action group does not already exist, create it as part of FR-5 via `az monitor action-group create` and document its name in `docs/architecture/infrastructure.md`. No Teams/Slack/PagerDuty integration in this iteration.

**Rationale:** `CLAUDE.md` documents a solo developer + AI-assisted PR review setup, and the session context confirms `ondra@anela.cz` as the user's address. `docs/architecture/observability.md` notes alerting is "Not configured" and references Teams/Email aspirationally — a single email Action Group is the minimal, project-fit choice that matches the team size and avoids introducing new chat tooling. Centralizing on one Action Group also makes future fan-out (add a Teams webhook receiver) a one-line change without rewiring individual alerts.

### Question 7
**Polly version already in use.** The brief references "Polly retries" in the stack trace (`Polly.Outcome.GetResultOrRethrow`), implying Polly is already wired somewhere. Where, and what does it currently cover? This may change FR-4 from "add resilience" to "extend / fix existing resilience."

**Answer:** Polly v8.4.1 is wired around **outbound HTTP adapter calls only** (Anthropic, OpenAI, MetaAds, Flexi, Comgate, Smartsupp, SerpApi, plus the in-application `CatalogResilienceService` and `DownloadResilienceService` for catalog/file-storage workflows). There is **no Polly pipeline around EF Core / Npgsql today** — the `Polly.Outcome.GetResultOrRethrow` frames in the telemetry come from adapter HTTP retries whose inner network failures happen to share Npgsql's SocketException type via shared TCP infrastructure, not from a DB-targeted pipeline. FR-4 stands as written: **add** a new EF Core-targeted resilience pipeline, do not extend an existing one. Reuse Polly v8.4.1 (already referenced from `Anela.Heblo.Application.csproj:27`) to avoid a new package dependency.

**Rationale:** A repo-wide grep for `Polly` returns only adapter projects and two named `*ResilienceService` classes (`backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogResilienceService.cs`, `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/DownloadResilienceService.cs`), all of which target `HttpRequestException` / `TaskCanceledException`. `PersistenceModule.AddPersistenceServices` wires `UseNpgsql(dataSource!)` with no `EnableRetryOnFailure` call and no resilience wrapper. The DB layer is genuinely unprotected today.

### Question 8
**Write transaction policy.** Are there write operations that are not safely idempotent under retry (e.g., non-transactional multi-statement writes)? If yes, the carve-out in FR-4 needs an explicit list.

**Answer:** No code-level carve-out is required. Repository writes go through EF Core's `SaveChangesAsync`, which wraps the unit of work in a single PostgreSQL transaction — retrying a `SaveChangesAsync` call that failed mid-flight rolls back the partial work, so the retry is idempotent at the DB layer. A grep for `BeginTransaction` / `UseTransaction` in `backend/src` returns no matches, so there are no hand-rolled multi-statement transactions to exclude. The Polly pipeline **must, however, exclude `DbUpdateConcurrencyException` and `DbUpdateException` whose inner `PostgresException.SqlState` indicates a logical conflict (e.g. `23505` unique_violation, `23503` foreign_key_violation, `23502` not_null_violation)** — those are not transient and retrying would mask real bugs. Document this exclusion in FR-4 alongside the transient-exception allowlist.

**Rationale:** EF Core's default behavior already gives each `SaveChanges` its own implicit transaction, and the codebase relies on that — there are no manual transactions to worry about. The real carve-out is the *opposite* of what the question implies: excluding non-transient `DbUpdateException` variants so we retry only socket/connect/timeout faults, not constraint violations. The existing `PostgresExceptionLoggingInterceptor` (`backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionLoggingInterceptor.cs:50`) already surfaces `SqlState`, which the retry predicate can inspect.

### Question 9
**EF Core execution strategy vs. Polly outer wrapper.** `EnableRetryOnFailure` and an outer Polly pipeline can conflict (double-retry, transaction rollback issues). Confirm which layer is canonical for this codebase.

**Answer:** Use a **custom EF Core `IExecutionStrategy`** that delegates to a Polly v8 `ResiliencePipeline` (registered as a singleton via `IDbResiliencePipelineProvider`). Wire it via `optionsBuilder.UseNpgsql(dataSource, npgsql => npgsql.ExecutionStrategy(deps => new PollyExecutionStrategy(deps, pipelineProvider)))` in `PersistenceModule.AddPersistenceServices`. **Do not** call `EnableRetryOnFailure` (Npgsql's built-in `NpgsqlRetryingExecutionStrategy`) and **do not** wrap repository calls in an outer Polly pipeline. There is exactly one retry layer, and EF Core owns it.

**Rationale:** EF Core's execution-strategy contract handles transaction-rollback correctness across retries (it forbids user-managed transactions while the strategy is active, which is fine here since the codebase has no `BeginTransaction` calls). Layering an outer Polly pipeline on top of `EnableRetryOnFailure` produces N×M retries and breaks the strategy's transaction guarantee. The custom-strategy-delegating-to-Polly pattern gives us shared retry telemetry/logging with the rest of the codebase (the adapter `ResilienceService` classes already use Polly v8) while keeping EF Core's transactional correctness intact, and it isolates the retry policy in one well-tested seam rather than scattering Polly calls across repositories.

### Question 10
Does the Plaud authentication model support a non-interactive refresh token flow, or is every re-auth interactive? If interactive-only, FR-3 is not implementable and FR-4's alerting becomes the primary mitigation. Assumption made: a refresh path exists.

**Answer:** Yes. Plaud supports a non-interactive OAuth refresh flow. It is already implemented in `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshClient.cs`, which `POST`s the current `refresh_token` to `https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh` and receives a new `{ access_token, refresh_token, expires_at }` tuple. FR-3 is implementable and must be retained in the spec.

**Rationale:** The refresh endpoint, request shape, and response contract are already exercised by the existing `PlaudTokenRefreshJob` and its unit tests (`PlaudTokenRefreshClientTests.cs`, `PlaudTokenRefreshJobTests.cs`). The weekly job exists but is `DefaultIsEnabled = false`, which explains why production drifted into the expired-token state — the refresh capability is built but not active.

### Question 11
What is the current KV secret name for the Plaud session token in `kv-heblo-prod`? The runbook (FR-1) and config defaults need the exact existing name; renaming may require a one-time migration.

**Answer:** The single secret is `Plaud--TokensJson` in both `kv-heblo-prod` and `kv-heblo-stg`. It stores the full JSON blob `{ "access_token": "...", "refresh_token": "...", "expires_at": <unix-milliseconds> }` — not two separate secrets. The spec's proposed `Plaud--SessionToken` / `Plaud--RefreshToken` split must be replaced with the single `Plaud--TokensJson` secret, and `PlaudCredentialsOptions.RefreshTokenSecretName` should be dropped.

**Rationale:** Confirmed by `PlaudTokenRefreshClient.cs:44` (computes `expiresAt` as Unix milliseconds), `docs/integrations/plaud-token-auto-refresh.md`, the existing test fixture (`PlaudTokenRefreshJobTests.cs`), and the bootstrapper's `PlaudOptions.TokensJson` configuration binding. No renaming is needed; no migration is required.

### Question 12
What is the typical Plaud session token lifetime? Required to choose a sensible default for `ExpiryBuffer` and the FR-4 alert threshold.

**Answer:** The `access_token` is short-lived (the CLI auto-refreshes it on every call when the refresh token is valid). The hard cap that triggers `AUTH_FAILED` in production is the **`refresh_token` TTL of approximately 30 days** (observed, not officially documented by Plaud). Use these defaults: `ExpiryBuffer = 72 hours` (well below the weekly auto-refresh cadence so a single missed run still leaves room), and FR-4 alert threshold = **count > 0 within a 15-minute window, evaluated every 5 minutes** — matching the existing `Heblo-Plaud-AuthExpired` rule.

**Rationale:** Documented observation in `docs/integrations/plaud-token-auto-refresh.md:72-74` ("hard TTL appears to be ~30 days") and confirmed by the existing weekly refresh cron (`0 4 * * 0`) being well inside that window. The 24h buffer originally proposed is too tight given the weekly refresh cadence — 72h gives one full retry opportunity before expiry. The 5/15 alert threshold matches the existing production rule and triage cadence.

### Question 13
Is there a Plaud sandbox or staging environment usable for the FR-5 integration test, or must the test mock the Plaud auth endpoint?

**Answer:** There is no Plaud sandbox. FR-5 integration tests must mock the refresh endpoint with a fake `HttpMessageHandler` (the pattern already used in `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenRefreshClientTests.cs`). Hitting the live `platform.plaud.ai` refresh endpoint from CI is explicitly out of scope — it would rotate the production refresh token and break the running app.

**Rationale:** Plaud has not published a sandbox environment and the codebase has historically treated the live endpoint as the only target (consistent with the same constraint documented for the Shoptet integration in CLAUDE.md). The existing test infrastructure already proves the mocked approach is sufficient for verifying the refresh round-trip.

### Question 14
Which Application Insights alert channel should FR-4 route to — the same channel as the existing weekly telemetry-anomaly digest, or a higher-urgency channel (since the failure is active in production)?

**Answer:** Reuse the existing action group **`ag-heblo-ops`** (email `ondra@anela.cz`), which already backs the live `Heblo-Plaud-AuthExpired` alert. Severity = 2 (Warning) for `PlaudAuthExpiredException`. Add a separate, lower-severity (Sev 3 / Informational) alert for `PlaudTokenNearExpiry` so it never wakes anyone but still surfaces in the inbox. Do not create new channels.

**Rationale:** This is a solo-developer project (CLAUDE.md: "Solo developer + AI-assisted PR review"), so there is no on-call rotation to split traffic across. The existing `Heblo-Plaud-AuthExpired` rule already defines this routing; the new alerts should reuse it rather than fragment ops signals.

### Question 15
Should the immediate FR-1 rotation be performed before or after the FR-2/FR-3 code lands? Recommendation: rotate immediately to restore production, then ship the code fix; confirm this sequencing.

**Answer:** Rotate first, code-fix second. Order: (1) Operator runs `plaud login` locally, updates `Plaud--TokensJson` in `kv-heblo-prod`, restarts the `Heblo` Web App, verifies exceptions stop in App Insights. (2) **Enable** the existing `plaud-token-refresh` Hangfire job in production via the Background Jobs admin UI (it is `DefaultIsEnabled = false` today, which is the root cause of this incident's recurrence). (3) Ship FR-2/FR-3 (proactive in-line refresh in `PlaudCliClient`) so the system no longer depends on the weekly cron firing successfully.

**Rationale:** Production is actively failing, so manual rotation is the only path to restore service within the 15-minute target in FR-1. Step (2) is critical and currently missing from the spec — the failure occurred precisely because the existing refresh job was never enabled; without flipping that flag, the system will re-enter this state ~30 days after every manual rotation. The code fix (FR-2/FR-3) hardens against the case where the weekly job itself fails.
