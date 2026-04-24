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

### MCP & Integration
- **🔌 MCP Server**: See "MCP Server" section below - 15 tools across Catalog, Manufacturing, and Batch Planning
- **🛒 Shoptet API**: `docs/integrations/shoptet-api.md` - All findings about Shoptet REST API (orders, statuses, shipping, ShoptetPay, test seeding)

## MCP Server

**Model Context Protocol Integration** - The application exposes MCP tools for AI assistants to query catalog data, manufacturing orders, and perform batch calculations.

**Available Tools:**

**Catalog Tools (7):**
- `GetCatalogList` - List products with filtering/pagination
- `GetCatalogDetail` - Get detailed product information
- `GetProductComposition` - Get product composition/ingredients
- `GetMaterialsForPurchase` - Get materials needed for purchase
- `GetAutocomplete` - Search products for autocomplete
- `GetProductUsage` - Get product usage in compositions
- `GetWarehouseStatistics` - Get warehouse statistics

**Manufacture Order Tools (4):**
- `GetManufactureOrders` - List manufacture orders with filtering
- `GetManufactureOrder` - Get single manufacture order details
- `GetCalendarView` - Get calendar view of manufacture orders
- `GetResponsiblePersons` - Get responsible persons from Entra ID

**Manufacture Batch Tools (4):**
- `GetBatchTemplate` - Get batch template for product
- `CalculateBatchBySize` - Calculate batch by desired size
- `CalculateBatchByIngredient` - Calculate batch by ingredient quantity
- `CalculateBatchPlan` - Calculate batch plan for multiple products

**Knowledge Base Tools (2):**
- `SearchKnowledgeBase` - Semantic search over ingested documents, returns ranked chunks with source references
- `AskKnowledgeBase` - AI-generated answer grounded in company documents, returns prose answer with cited sources

**Implementation:**
- Tool classes: `backend/src/Anela.Heblo.API/MCP/Tools/`
- Registration: `McpModule.cs` (AddMcpServer + WithHttpTransport + WithTools)
- Pattern: Thin wrappers around MediatR handlers
- Error handling: `McpException` from `ModelContextProtocol` namespace for protocol errors
- Authentication: Uses existing Microsoft Entra ID authentication

**Testing:**
- Test location: `backend/test/Anela.Heblo.Tests/MCP/Tools/`
- Total tests: 29 (comprehensive coverage including parameter mapping, JSON serialization, and error handling)
- See existing test files for examples of MCP tool testing patterns

**Status:** ✅ Active - MCP server running on /mcp endpoint using official ModelContextProtocol.AspNetCore SDK

**Endpoint:** `/mcp` (requires Microsoft Entra ID authentication)

**Transport:** SSE (Server-Sent Events) for web-based MCP clients

**SDK:** Official MCP C# SDK from https://github.com/modelcontextprotocol/csharp-sdk

**Tool Pattern:**
- Tools decorated with `[McpServerToolType]` (class) and `[McpServerTool]` (methods)
- Parameters use `[Description]` attribute for documentation
- Methods return `Task<string>` with JSON-serialized responses
- Errors thrown as `McpException` (handled by SDK)

**Configuration:**

Available endpoints by environment:
- **Production**: `https://heblo.anela.cz/mcp`
- **Staging**: `https://heblo.stg.anela.cz/mcp`
- **Local Development**: `https://localhost:5001/mcp`

**MCP Client Setup:**

Add to your MCP client configuration (e.g., Claude Desktop `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "anela-heblo": {
      "url": "https://heblo.anela.cz/mcp",
      "transport": "sse",
      "authentication": {
        "type": "bearer",
        "token": "YOUR_ENTRA_ID_TOKEN"
      }
    }
  }
}
```

For local development, use `https://localhost:5001/mcp` as the URL.

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

### 7. E2E Test Modular Structure

**MANDATORY**: All E2E test files MUST be organized into modular structure for parallel execution.

**Module Structure:**
Tests are organized into 6 logical modules:
- `catalog/` - Catalog page tests (filters, sorting, pagination, charts)
- `issued-invoices/` - Issued invoices tests (filters, import, navigation, badges)
- `stock-operations/` - Stock operations tests (filters, retry, badges, panel)
- `transport/` - Transport box tests (creation, management, workflow, EAN)
- `manufacturing/` - Manufacturing tests (batch planning, order creation)
- `core/` - Core functionality tests (dashboard, changelog, navigation, auth)

**Shared directories:**
- `helpers/` - Test helper functions and utilities
- `fixtures/` - Test data fixtures

**✅ CORRECT - Modular structure:**
```
frontend/test/e2e/
├── helpers/                              # ✅ Shared test utilities
├── fixtures/                             # ✅ Test data fixtures
├── catalog/                              # ✅ Catalog module tests
│   ├── clear-filters.spec.ts
│   ├── combined-filters.spec.ts
│   └── ...
├── issued-invoices/                      # ✅ Issued invoices module tests
│   ├── filters.spec.ts
│   ├── import-modal.spec.ts
│   └── ...
└── stock-operations/                     # ✅ Stock operations module tests
    ├── filters.spec.ts
    ├── retry.spec.ts
    └── ...
```

**Import paths with modular structure:**
```typescript
// ✅ CORRECT - Tests in modules use relative paths
import { navigateToApp } from '../helpers/e2e-auth-helper';
import { TestCatalogItems } from '../fixtures/test-data';
```

**Running tests:**
```bash
# Run all modules
./scripts/run-playwright-tests.sh

# Run specific module
./scripts/run-playwright-tests.sh catalog
./scripts/run-playwright-tests.sh issued-invoices
./scripts/run-playwright-tests.sh stock-operations

# Run by pattern
./scripts/run-playwright-tests.sh auth
```

**Enforcement:**
- Tests MUST be placed in appropriate module directory
- Module selection enables parallel CI/CD execution (3-4x speedup)
- See `docs/testing/playwright-e2e-testing.md` for complete testing guide
- See `docs/testing/e2e-module-guide.md` for module definitions and boundaries

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

### 9. Shoptet API Knowledge Base

**MANDATORY**: Any new finding about the Shoptet REST API MUST be documented in `docs/integrations/shoptet-api.md` before being used in code or tests.

This includes:
- New endpoints used or discovered
- Order status values and their meaning
- Shipping/payment method GUIDs per environment
- Quirks, gotchas, or undocumented behaviors
- Test environment constraints or workarounds

**Why:** Shoptet has no sandbox — everything runs against a live store. Undocumented assumptions cause data corruption or flaky tests.

### 10. Build Validation Before Completion

**MANDATORY**: Both backend AND frontend builds must pass before any task, PR, or feature is declared done.

```bash
# Backend
dotnet build

# Frontend
npm run build   # runs tsc — catches type errors Jest/Babel misses
```

**CRITICAL:** Jest uses Babel to transform TypeScript — it strips types without checking them. A test suite can pass 100% while the production build fails due to TypeScript errors. `npm run build` is the only gate that runs `tsc`.

**Rule:** Run both builds as the **final step**, after all commits including code review fixes. If a review fix is applied, re-run both builds before pushing.

**Enforcement:**
- NEVER declare a task complete without a passing `npm run build` (FE) and `dotnet build` (BE)
- Build failures block push — fix first, then re-verify

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
4. **Run tests**: `dotnet test` (BE) and `CI=true npm test -- --no-coverage` (FE)
5. **Validate builds**: `dotnet build` (BE) and `npm run build` (FE) — MANDATORY, catches TypeScript errors that tests miss
6. **Format code**: Run `dotnet format` (BE) or `npm run lint` (FE)
7. **Follow best practices**: YAGNI, SOLID, KISS, DRY
8. **Ask for clarification** if unsure about implementation details

**Steps 4 and 5 must be repeated after any code review fixes before pushing.**

See `docs/development/setup.md` for detailed development commands and setup instructions.

## Important Notes

- Solo developer project with AI-assisted PR reviews
- Database migrations are manual (not automated in deployment)
- OpenAPI client auto-generated on build
- All Docker images pushed to Docker Hub
- Backend follows Clean Architecture with Vertical Slice organization
- To run Playwright tests: Always use `./scripts/run-playwright-tests.sh` script
- Validate builds before claiming completion

## Memory

Cross-session knowledge lives in `memory/`. Read relevant files at the start of each session. Write new learnings, decisions, and patterns during the session.

- `memory/decisions/` — architectural and library choices with reasoning
- `memory/patterns/` — confirmed implementation patterns for this codebase
- `memory/gotchas/` — bugs, edge cases, and hard-won lessons
- `memory/context/` — current project state, active work, pending decisions

Update `memory/context/state.md` at the end of significant sessions with: current branch, what was completed, what is pending, any blockers.
