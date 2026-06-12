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
