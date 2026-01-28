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
