# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Anela Heblo** - A cosmetics company workspace application built with Clean Architecture principles.

**Stack**: Monorepo (.NET 8 + React), Clean Architecture with Vertical Slice organization, MediatR + MVC Controllers, Single Docker image deployment, Azure Web App for Containers.

## Quick Reference Documentation

### Architecture & Structure
- **📘 Core Architecture**: `docs/📘 Architecture Documentation – MVP Work.md` - Module definitions, data flow, business logic
- **🗂️ Filesystem Structure**: `docs/architecture/filesystem.md` - Directory organization, component placement rules
- **🏗️ Infrastructure**: `docs/architecture/application_infrastructure.md` - Deployment, CI/CD, Docker
- **🌐 Environments**: `docs/architecture/environments.md` - Port mappings, CORS, Azure settings

### Development
- **🚀 Setup & Commands**: `docs/development/setup.md` - Development commands, authentication setup, Docker
- **🔌 API Client Generation**: `docs/development/api-client-generation.md` - OpenAPI client generation (C# & TypeScript)

### Design & UI
- **🎨 UI Design System**: `docs/design/ui_design_document.md` - Design system, colors, typography, components
- **📐 Layout Definition**: `docs/design/layout_definition.md` - Page layout standards (CRITICAL for UI work)

### Testing
- **🎭 Playwright E2E Testing**: `docs/testing/playwright-e2e-testing.md` - E2E testing setup, authentication, commands
- **📊 Test Data Fixtures**: `docs/testing/test-data-fixtures.md` - Available test data for E2E tests

## Architecture Principles

**Clean Architecture with Vertical Slice Organization:**
- **Domain Layer** (`Anela.Heblo.Domain`): Entities, repository interfaces, domain services
- **Application Layer** (`Anela.Heblo.Application`): MediatR handlers, business logic, DTOs
- **Infrastructure Layer** (`Anela.Heblo.Persistence`): EF Core contexts, entity configurations, repositories
- **API Layer** (`Anela.Heblo.API`): MVC Controllers (NOT FastEndpoints), authentication, serves React app

**Key Principles:**
- **Vertical organization**: Each feature contains all its layers
- **MediatR pattern**: Controllers send requests to handlers via MediatR
- **Standard API Pattern**: All endpoints follow `/api/{controller}` REST conventions
- **Feature autonomy**: Each feature manages its own contracts, services, and infrastructure
- **SOLID principles**: Applied within each vertical slice

See `docs/architecture/filesystem.md` for detailed structure and component placement rules.

## CRITICAL RULES - Must Follow Always

### 1. Security Rules

**NEVER commit credentials to source control:**
- ❌ No real passwords, API keys, or tokens in any committed files
- ❌ No hardcoded credentials in test files
- ❌ No authentication secrets in configuration files

**Required approach:**
- ✅ Use local `.env.test` files (always gitignored)
- ✅ Load credentials via secure utility functions like `loadTestCredentials()`
- ✅ Exit with error if credentials file is missing

### 2. Code Formatting Standards

**Backend (.NET)**:
- **MANDATORY**: All C# code MUST pass `dotnet format` validation
- Indentation: 4 spaces (never tabs)
- Brace style: Allman style (opening braces on new line)
- Run `dotnet format` before commits
- CI/CD validates formatting and fails on violations

**Frontend (React/TypeScript)**:
- Use Prettier for consistent formatting
- 2 spaces indentation for TypeScript/JSX
- Semicolons required, single quotes for strings
- Run `npm run lint` before commits

### 3. Backend DTO Rules

**CRITICAL: OpenAPI Client Generation Compatibility**

**Use Classes Instead of Records for API DTOs:**
- ❌ **NEVER use C# records for API request/response DTOs** - OpenAPI generators have issues with parameter order
- ✅ **ALWAYS use classes with properties for API DTOs** - ensures reliable OpenAPI schema generation
- ✅ **Add proper validation attributes** - `[Required]`, `[JsonPropertyName]`, etc.

**Enforcement:**
- All MediatR requests/responses for API endpoints must use classes
- Internal domain objects can still use records if not exposed via API

### 4. API Client URL Construction

**MANDATORY**: All API hooks MUST use absolute URLs with baseUrl to avoid calling wrong endpoints.

**❌ WRONG - Relative URL (calls wrong port)**:
```typescript
const url = `/api/catalog`; // Calls localhost:3001 instead of localhost:5001
const response = await (apiClient as any).http.fetch(url, {method: 'GET'});
```

**✅ CORRECT - Absolute URL with baseUrl**:
```typescript
const relativeUrl = `/api/catalog`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`; // http://localhost:5001/api/catalog
const response = await (apiClient as any).http.fetch(fullUrl, {method: 'GET'});
```

**Enforcement:**
- NEVER use relative URLs directly in fetch calls
- Use generated API client via `getAuthenticatedApiClient()`

See `docs/development/api-client-generation.md` for details.

### 5. E2E Test Authentication

**MANDATORY**: All E2E tests MUST use proper authentication setup to avoid Microsoft Entra ID login screen.

**✅ CORRECT - Use navigateToApp() for full authentication:**
```typescript
import { navigateToApp } from './helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  await navigateToApp(page); // Full auth: backend + frontend session
  await page.goto('/your-page');
});
```

**✅ CORRECT - Use navigation helpers (include auth internally):**
```typescript
import { navigateToCatalog, navigateToTransportBoxes } from './helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  await navigateToCatalog(page); // Includes full auth setup
});
```

**❌ WRONG - Never use createE2EAuthSession() alone:**
```typescript
await createE2EAuthSession(page); // ❌ Only backend session, missing frontend
await page.goto('/your-page');    // ❌ Will show Microsoft login screen!
```

**Why this matters:**
- `createE2EAuthSession()` - Only backend auth (service principal token)
- `navigateToApp()` - Full setup: backend + frontend session (cookies, sessionStorage, E2E flag)
- Without frontend session → Microsoft Entra ID sign-in screen → test failure

See `docs/testing/playwright-e2e-testing.md` for complete authentication flow.

### 6. Test Data Fixtures

**MANDATORY**: When writing E2E tests that require consistent, reliable data, ALWAYS use test data fixtures.

**Usage Rules:**
- ✅ Use fixtures from `frontend/test/e2e/fixtures/test-data.ts`
- ✅ Reference `docs/testing/test-data-fixtures.md` for available test data
- ✅ Tests MUST fail (not skip) when expected data is missing - throw clear error messages
- ✅ Example: Use `TestCatalogItems.bisabolol` instead of hardcoding "AKL001"

**Fail, don't skip:**
```typescript
import { TestCatalogItems } from './fixtures/test-data';

test('should filter by product', async ({ page }) => {
  const product = TestCatalogItems.bisabolol;
  await searchForProduct(page, product.name);

  const rowCount = await getRowCount(page);
  if (rowCount === 0) {
    throw new Error(
      `Test data missing: Expected to find "${product.name}" (${product.code})`
    );
  }
  // Continue with assertions...
});
```

### 7. E2E Test File Structure

**MANDATORY**: All E2E test files MUST use flat structure - directly in `frontend/test/e2e/` directory.

**Only allowed subdirectories:**
- `helpers/` - Test helper functions and utilities
- `fixtures/` - Test data fixtures

**❌ WRONG - Nested test files in subdirectories:**
```
frontend/test/e2e/
├── customer/
│   └── issued-invoices-filters.spec.ts  # ❌ NO nested test files
├── stock/
│   └── operations-retry.spec.ts         # ❌ NO subdirectories for tests
```

**✅ CORRECT - Flat structure with naming convention:**
```
frontend/test/e2e/
├── helpers/                              # ✅ Only helpers allowed
├── fixtures/                             # ✅ Only fixtures allowed
├── issued-invoices-filters.spec.ts       # ✅ Flat structure at root
├── stock-operations-retry.spec.ts        # ✅ Use naming for grouping
└── catalog-filters.spec.ts               # ✅ All tests directly in e2e/
```

**Import paths with flat structure:**
```typescript
// ✅ CORRECT - All tests are at root level
import { navigateToApp } from './helpers/e2e-auth-helper';
import { TestCatalogItems } from './fixtures/test-data';

// ❌ WRONG - Don't use relative paths for nested structure
import { navigateToApp } from '../helpers/e2e-auth-helper';  // NO!
```

**Enforcement:**
- Use `{feature}-{aspect}.spec.ts` naming convention to group related tests
- NEVER create subdirectories for test files
- See `docs/testing/playwright-e2e-testing.md` for complete testing guide

### 8. Design Document Alignment

**MANDATORY**: All implementation work MUST align with design documents in `/docs`.

**Before ANY implementation:**
- Read and understand relevant design documents
- Verify proposed changes align with documented architecture
- If conflicts arise, ask for clarification rather than making assumptions

**Required documents for different change types:**
- **Backend/API changes**: `docs/📘 Architecture Documentation – MVP Work.md`
- **Infrastructure/deployment**: `docs/architecture/application_infrastructure.md`
- **Environment/config**: `docs/architecture/environments.md`
- **Filesystem/structure**: `docs/architecture/filesystem.md`
- **Frontend/UI**: `docs/design/ui_design_document.md`
- **Page layouts**: `docs/design/layout_definition.md` (CRITICAL for UI work)

**Enforcement:**
- NO implementation without consultation
- NO architectural deviations without approval
- MANDATORY documentation updates when implementation changes

## Testing Strategy

**Three types of tests:**
1. **BE (Backend)**: .NET unit/integration tests - Run with `dotnet test`
2. **FE (Frontend)**: Jest + React Testing Library - Run with `npm test`
3. **UI (E2E)**: Playwright against staging - Run with `./scripts/run-playwright-tests.sh`

**Test Organization:**
- **Unit/Integration**: Co-located in `__tests__/` folders next to components
- **E2E Tests**: Located in `/frontend/test/e2e/` directory (MUST use flat structure - see rule 7)

**Playwright Testing:**
- **Target**: Always run against staging environment (https://heblo.stg.anela.cz)
- **Authentication**: Real Microsoft Entra ID (not mock)
- **Commands**: See `docs/testing/playwright-e2e-testing.md`

**CI/CD Testing Strategy:**
- **In CI**: Frontend Jest + Backend .NET tests (fast feedback, 15-20 min)
- **Nightly**: Full Playwright E2E suite against staging (comprehensive, 10-15 min)
- **E2E tests do NOT run in PR CI** - only nightly regression

## Implementation Guidelines

### When Creating New Features:
1. **Start with Domain**: Define entities and repository interfaces in `Domain/Features/{Feature}/`
2. **Add Application Logic**: Create handlers in `Application/Features/{Feature}/UseCases/`
3. **Configure Persistence**: Create entity configurations in `Persistence/{Feature}/`
4. **Expose via API**: Create controller in `API/Controllers/{Feature}Controller.cs`
5. **Register Dependencies**: Update `{Feature}Module.cs` and `PersistenceModule.cs`

### Naming Conventions:
- **Controllers**: `{Feature}Controller` (e.g., `CatalogController`)
- **Handlers**: `{Action}{Entity}Handler` (e.g., `GetCatalogListHandler`)
- **Requests/Responses**: `{Action}{Entity}Request/Response`
- **DTOs**: `{Entity}Dto` (e.g., `CatalogItemDto`)
- **Services**: `{Entity}Service` and `I{Entity}Service`
- **Entity Configurations**: `{Entity}Configuration`
- **Repositories**: `{Entity}Repository`

See `docs/architecture/filesystem.md` for complete feature organization patterns.

## Development Workflow

1. **Make changes** to code
2. **For UI changes**: Test locally with `npm start` (port 3000)
3. **For E2E testing**: Use staging environment (`./scripts/run-playwright-tests.sh`)
4. **Validate builds**: Run `dotnet build` (BE) or `npm run build` (FE) before finishing
5. **Format code**: Run `dotnet format` (BE) or `npm run lint` (FE)
6. **Follow best practices**: YAGNI, SOLID, KISS, DRY
7. **Ask for clarification** if unsure about implementation details

See `docs/development/setup.md` for detailed development commands and setup instructions.

## Important Notes

- Solo developer project with AI-assisted PR reviews
- Database migrations are manual (not automated in deployment)
- OpenAPI client auto-generated on build
- All Docker images pushed to Docker Hub
- Backend follows Clean Architecture with Vertical Slice organization
- To run Playwright tests: Always use `./scripts/run-playwright-tests.sh` script
- Validate builds before claiming completion
