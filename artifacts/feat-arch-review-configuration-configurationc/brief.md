## Module
Configuration

## Finding
`backend/src/Anela.Heblo.Domain/Features/Configuration/ConfigurationConstants.cs` lives in the Domain layer but is full of infrastructure/composition-root constants:

- AppInsights keys: `APPLICATION_INSIGHTS_CONNECTION_STRING`, `APPINSIGHTS_INSTRUMENTATION_KEY`, `APPLICATIONINSIGHTS_CONNECTION_STRING`
- Database: `DEFAULT_CONNECTION`
- CORS: `CORS_ALLOWED_ORIGINS`, `CORS_POLICY_NAME`
- Health check tags: `DB_TAG` (`"db"`), `POSTGRESQL_TAG`, `DATABASE_HEALTH_CHECK`
- Auth scheme name: `MOCK_AUTH_SCHEME`

These constants are consumed by the API project's infrastructure glue (`ServiceCollectionExtensions.cs`, `AuthenticationExtensions.cs`, `ApplicationBuilderExtensions.cs`, `LoggingExtensions.cs`, `HangfireAuthenticationMiddleware.cs`) — every usage is in `Anela.Heblo.API`, not in any domain or application handler.

## Why it matters
Clean Architecture requires the Domain layer to be free of infrastructure concepts. A Domain type should not know about Application Insights, CORS policies, health check tag names, or authentication schemes. Having these constants in Domain means the Domain project takes on a dependency on knowing how the API host wires itself up — inverting the proper dependency direction. If the API's CORS policy were renamed or health check tags changed, the Domain layer would need to be modified.

## Suggested fix
Split the file: keep only the truly application-level keys (`APP_VERSION`, `USE_MOCK_AUTH`, `BYPASS_JWT_VALIDATION`) in `ConfigurationConstants.cs` in the Domain layer. Move the infrastructure constants (`APPLICATION_INSIGHTS_CONNECTION_STRING`, `CORS_POLICY_NAME`, `DB_TAG`, `MOCK_AUTH_SCHEME`, etc.) into a new file in the API project, e.g. `backend/src/Anela.Heblo.API/Infrastructure/InfrastructureConstants.cs`. Update the consuming API extension methods to reference the new location.

---
_Filed by daily arch-review routine on 2026-05-29._