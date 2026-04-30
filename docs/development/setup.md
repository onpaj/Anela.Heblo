# Development Setup & Commands

This document provides all development commands and setup instructions for the Anela Heblo project.

## Prerequisites

- .NET 8 SDK
- Node.js 18+ and npm
- Docker (for containerized deployment)
- PostgreSQL (for local database)
- Git

## Backend (.NET 8) Commands

### Build & Test

```bash
# Build the solution
dotnet build

# Run unit and integration tests (includes mock authentication tests)
dotnet test

# Run with code coverage
dotnet test /p:CollectCoverage=true /p:CoverageReporter=html
```

### Database Migrations

```bash
# Create new migration
dotnet ef migrations add <MigrationName> --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API

# Apply migrations to database
dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API

# Remove last migration (if not applied)
dotnet ef migrations remove --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API

# List all migrations
dotnet ef migrations list --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API
```

### Run Development Server

```bash
# Start development server with mock authentication (if UseMockAuth=true)
cd backend/src/Anela.Heblo.API
dotnet run

# Server will start on https://localhost:5001
```

### Code Formatting

```bash
# Format all C# code (run before commits)
dotnet format

# Check formatting without applying changes
dotnet format --verify-no-changes
```

### OpenAPI Client Generation

```bash
# Manually generate TypeScript client for frontend
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual

# Note: Client is auto-generated on build in Debug mode
```

## Frontend (React) Commands

### Install & Build

```bash
cd frontend

# Install dependencies
npm install

# Start development server with hot reload (localhost:3000)
npm start

# Build static files for production deployment
npm run build

# Run linter
npm run lint

# Fix linting issues automatically
npm run lint:fix
```

### Testing

```bash
# Run Jest unit tests with React Testing Library
npm test

# Run tests in watch mode
npm test -- --watch

# Run tests with coverage
npm test -- --coverage
```

### Playwright E2E Tests

```bash
# Run all E2E tests against staging
./scripts/run-playwright-tests.sh

# Run specific test file
./scripts/run-playwright-tests.sh catalog-ui

# Run tests with visible browser (headed mode)
npx playwright test --headed

# View test results
npx playwright show-report

# Record interactions against staging
npx playwright codegen https://heblo.stg.anela.cz

# Install Playwright browsers (run once)
npx playwright install
```

See `docs/testing/playwright-e2e-testing.md` for detailed E2E testing guide.

## Authentication Setup

### Local Development (Mock Mode)

**Recommended for development and testing:**

1. Set `"UseMockAuth": true` in `backend/src/Anela.Heblo.API/appsettings.Development.json`
2. Mock authentication automatically provides a "Mock User" with standard claims
3. No real Microsoft Entra ID credentials needed
4. API accepts all requests as authenticated with mock user data

**Mock User Claims:**
- Name: "Mock User"
- Email: "mock.user@example.com"
- User ID (oid): Generated GUID
- Tenant ID (tid): Generated GUID
- Roles: Standard user roles

### Production/Real Authentication

**For production or real authentication testing:**

1. Set `"UseMockAuth": false` in configuration
2. Copy `frontend/.env.example` to `frontend/.env`
3. Fill in actual Microsoft Entra ID credentials:
   ```
   REACT_APP_CLIENT_ID=<your-client-id>
   REACT_APP_TENANT_ID=<your-tenant-id>
   REACT_APP_REDIRECT_URI=http://localhost:3000
   ```
4. The `.env` file is gitignored and contains sensitive data - **NEVER commit it**
5. Contact project owner for actual credential values

### E2E Test Authentication

**For E2E tests against staging:**

1. Create `frontend/.env.test` with Azure service principal credentials
2. Use `loadTestCredentials()` utility to load credentials securely
3. See `docs/testing/playwright-e2e-testing.md` for authentication flow details

**NEVER commit credentials to git** - all `.env*` files are gitignored.

## Docker Commands

### Development Environment

```bash
# Start local development environment (if needed)
docker-compose up

# Stop and remove containers
docker-compose down

# View logs
docker-compose logs -f
```

### Production Build

```bash
# Build single production image (multi-stage: frontend build + backend + static files)
docker build -t anela-heblo .

# Run production container locally
docker run -p 8080:8080 anela-heblo

# Tag for Docker Hub
docker tag anela-heblo <dockerhub-username>/anela-heblo:latest

# Push to Docker Hub
docker push <dockerhub-username>/anela-heblo:latest
```

### Deployment Architecture

- **Development**: Separate React dev server (hot reload on port 3000) + ASP.NET Core API server (port 5001)
- **Production**: Single container serves both React static files and ASP.NET Core API (port 8080)

## Port Mappings

See `docs/architecture/environments.md` for complete port configuration.

**Local Development:**
- Frontend (React dev server): `http://localhost:3000`
- Backend (ASP.NET Core API): `https://localhost:5001`
- API base URL for frontend: `https://localhost:5001/api`

**Staging:**
- Full application: `https://heblo.stg.anela.cz`

**Production:**
- Full application: `https://heblo.anela.cz`

## Common Development Workflows

### Starting Local Development

```bash
# Terminal 1: Start backend
cd backend/src/Anela.Heblo.API
dotnet run

# Terminal 2: Start frontend
cd frontend
npm start

# Frontend will be available at http://localhost:3000
# Backend API at https://localhost:5001/api
```

### Making Code Changes

1. Make changes to code
2. For UI changes: Test locally with `npm start` (hot reload enabled)
3. For backend changes: Restart `dotnet run` or use hot reload (if enabled)
4. Run appropriate tests:
   - Backend: `dotnet test`
   - Frontend: `npm test`
   - E2E: `./scripts/run-playwright-tests.sh <test-name>`
5. Validate builds:
   - Backend: `dotnet build`
   - Frontend: `npm run build`
6. Format code:
   - Backend: `dotnet format`
   - Frontend: `npm run lint`

### Creating Database Migration

```bash
# 1. Make changes to entity configurations or domain models
# 2. Create migration
dotnet ef migrations add AddNewFeature --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API

# 3. Review generated migration files in Persistence/Migrations
# 4. Apply to local database
dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API

# 5. Test the migration
dotnet test

# 6. Commit migration files to git
# Note: Migrations are applied manually in staging/production (not automated)
```

### Generating API Client

The TypeScript API client is auto-generated when building the backend in Debug mode.

**Manual generation:**
```bash
# Generate TypeScript client for frontend
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual

# Client is generated at: frontend/src/api/generated/api-client.ts
```

See `docs/development/api-client-generation.md` for details.

### Running E2E Tests

```bash
# Always test against staging environment
./scripts/run-playwright-tests.sh

# Run specific test
./scripts/run-playwright-tests.sh transport-box-workflow

# Debug specific test with headed browser
./scripts/run-playwright-tests.sh transport-box-workflow
```

See `docs/testing/playwright-e2e-testing.md` for E2E testing guide.

## Troubleshooting

### Backend Issues

**Build errors:**
```bash
# Clean solution
dotnet clean
rm -rf backend/src/*/bin backend/src/*/obj

# Restore packages
dotnet restore

# Rebuild
dotnet build
```

**Database connection issues:**
- Check PostgreSQL is running
- Verify connection string in `appsettings.Development.json`
- Ensure database exists: `dotnet ef database update`

**Authentication issues:**
- For local dev: Set `UseMockAuth: true` in `appsettings.Development.json`
- For real auth: Verify `.env` file has correct Entra ID credentials

### Frontend Issues

**Module not found errors:**
```bash
# Clear node_modules and reinstall
rm -rf node_modules package-lock.json
npm install
```

**API client not found:**
```bash
# Regenerate API client
dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual

# Verify client exists at: frontend/src/api/generated/api-client.ts
```

**Port already in use:**
```bash
# Find process using port 3000
lsof -ti:3000

# Kill process
kill -9 <PID>

# Or use different port
PORT=3001 npm start
```

### E2E Test Issues

**Authentication failures:**
- Verify `frontend/.env.test` exists with correct service principal credentials
- Check staging environment is accessible: `curl https://heblo.stg.anela.cz/health/live`
- See `docs/testing/playwright-e2e-testing.md` for authentication troubleshooting

**Test data not found:**
- Tests use fixtures from `frontend/test/e2e/fixtures/test-data.ts`
- See `docs/testing/test-data-fixtures.md` for available test data
- Tests MUST fail (not skip) when data is missing

## Environment Variables

### Backend (`appsettings.Development.json`)

```json
{
  "Authentication": {
    "UseMockAuth": true  // Set to false for real Microsoft Entra ID
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=anela_heblo;Username=postgres;Password=<password>"
  }
}
```

### Frontend (`.env` - gitignored)

```bash
# Microsoft Entra ID Configuration
REACT_APP_CLIENT_ID=<your-client-id>
REACT_APP_TENANT_ID=<your-tenant-id>
REACT_APP_REDIRECT_URI=http://localhost:3000

# API Configuration
REACT_APP_API_BASE_URL=https://localhost:5001
```

### E2E Tests (`.env.test` - gitignored)

```bash
# Azure Service Principal for E2E Testing
E2E_CLIENT_ID=<service-principal-client-id>
E2E_CLIENT_SECRET=<service-principal-client-secret>
E2E_TENANT_ID=<tenant-id>

# Staging Environment
E2E_BASE_URL=https://heblo.stg.anela.cz
```

**CRITICAL**: Never commit `.env` or `.env.test` files to git!

## Additional Resources

- **Architecture**: `docs/architecture/filesystem.md`
- **API Client**: `docs/development/api-client-generation.md`
- **E2E Testing**: `docs/testing/playwright-e2e-testing.md`
- **CI/CD**: `docs/architecture/application_infrastructure.md`
- **UI Design**: `docs/design/ui_design_document.md`

## Database Migrations Runbook

Database migrations in this project are **manual**. They are not applied by the deployment pipeline. Code that depends on a new migration MUST NOT be deployed before the migration is applied to the target environment, or the application will return HTTP 500 with `Npgsql.PostgresException: 42P01: relation "<table>" does not exist`.

### Pre-deploy checklist

Before merging or deploying code that introduces or depends on a new EF Core migration:

1. Identify the migration(s) the new code depends on (look at `backend/src/Anela.Heblo.Persistence/Migrations/` and `dotnet ef migrations list`).
2. Connect to the target environment's database using your authorized read-only credentials. Do NOT embed credentials in source control or scripts.
3. Run the diagnostic SQL pair below (substitute the `LIKE` patterns and table names for the migration in question).
4. Confirm the migration ID is present in `__EFMigrationsHistory` AND the expected post-migration physical schema is in place.
5. If the migration is missing, apply it via the project's standard manual migration procedure BEFORE rolling out the dependent application code.

### Post-deploy verification

After every deployment that touches schema or schema-mapping code:

1. `curl https://<environment-host>/health/ready` (replace `<environment-host>` with the deployed environment URL).
2. Confirm HTTP 200 and that the JSON body contains every health check with `status: "Healthy"`. In particular, confirm `data-quality-schema` is `Healthy`.
3. If any check is `Unhealthy`, inspect the structured `data` field on that check (e.g., `entity`, `expectedTable`, `schema`, `sqlState`) — these point directly at the drift.
4. If `data-quality-schema` reports `sqlState: "42P01"` with `expectedTable: "DqtRuns"`, the production DB is missing the `DqtRuns` rename. Apply `StandardizeTableNamingToPascalCase` (or the relevant pending migration) before allowing the instance back into rotation. Note that Azure App Service will already have removed the unhealthy instance from rotation automatically.

### Ordering hazard

The pair `AddDataQualityTables` (creates `dqt_runs`) → `StandardizeTableNamingToPascalCase` (renames to `DqtRuns`) is the canonical example of an ordering hazard: deploying the application code that depends on the rename before applying the rename migration produces user-facing 500s. Always confirm the most recent dependent migration is applied before deploying its consumer code.

### Diagnostic SQL for suspected schema drift

When you suspect a "relation does not exist" error is caused by code/DB migration drift, use this read-only diagnostic SQL pair. Substitute the `LIKE` patterns and table names for the suspect entity.

Migration history check:

```sql
SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%<migration-fragment-1>%'
   OR "MigrationId" LIKE '%<migration-fragment-2>%'
ORDER BY "MigrationId";
```

Physical table existence check:

```sql
SELECT table_schema, table_name
FROM information_schema.tables
WHERE table_schema = 'public'
  AND lower(table_name) IN ('<old-name-lower>', '<new-name-lower>');
```

Interpret the combined output as one of three states:

- **State A** — both expected migrations present in history AND only the new (post-rename) physical table exists → code and DB are consistent. Investigate stale application instances; perform a rolling restart of Azure App Service.
- **State B** — only the older migration present in history AND only the old physical table exists → the rename migration is unapplied. Apply it via the standard manual procedure. Inverse rollback (if ever needed) for a metadata-only rename is `ALTER TABLE "<NewName>" RENAME TO <old_name>;` — note this restores the table name but does not undo any other changes a multi-step migration may have included.
- **State C** — both migrations present in history but the old physical table still exists (or both exist) → manual intervention required. Do not attempt automated remediation. Escalate.

These diagnostic queries are read-only and safe to run against any environment.
