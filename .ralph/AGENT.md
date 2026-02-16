# Agent Build Instructions

## Project Overview

**Anela Heblo** - A cosmetics company workspace application built with Clean Architecture principles.

**Stack**: Monorepo (.NET 8 + React), Clean Architecture with Vertical Slice organization, MediatR + MVC Controllers, Single Docker image deployment, Azure Web App for Containers.

## Project Setup

```bash
# Backend (.NET 8)
cd backend
dotnet restore
dotnet build

# Frontend (React + TypeScript)
cd frontend
npm install
```

## Running Tests

**Three types of tests:**

1. **Backend Tests (.NET unit/integration)**:
```bash
cd backend
dotnet test
```

2. **Frontend Tests (Jest + React Testing Library)**:
```bash
cd frontend
npm test

# With coverage
npm run test:coverage
```

3. **E2E Tests (Playwright against staging)**:
```bash
# Always use the script (includes auth setup)
./scripts/run-playwright-tests.sh

# Run specific module
./scripts/run-playwright-tests.sh catalog
./scripts/run-playwright-tests.sh issued-invoices
./scripts/run-playwright-tests.sh stock-operations
./scripts/run-playwright-tests.sh transport
./scripts/run-playwright-tests.sh manufacturing
./scripts/run-playwright-tests.sh core
```

**CRITICAL**: E2E tests target staging environment (https://heblo.stg.anela.cz) and use real Microsoft Entra ID authentication.

## Build Commands

```bash
# Backend build (with format validation)
cd backend
dotnet format --verify-no-changes
dotnet build

# Frontend build
cd frontend
npm run lint
npm run build

# Docker build (from root)
docker build -t anela/heblo:latest .
```

## Development Server

```bash
# Frontend development server (hot reload)
cd frontend
npm start
# Runs on http://localhost:3000

# Backend development (with watch)
cd backend
dotnet watch run
# API runs on http://localhost:5001
```

## Key Learnings

### Architecture Patterns
- **Vertical Slice Organization**: Each feature contains all its layers (Domain → Application → Infrastructure → API)
- **MediatR Pattern**: Controllers send requests to handlers via MediatR (NOT FastEndpoints)
- **Standard REST**: All endpoints follow `/api/{controller}` conventions
- **Feature Modules**: Each feature has its own module (e.g., `CatalogModule.cs`, `PersistenceModule.cs`)

### Critical API Patterns
- **DTOs MUST be classes**: Records break OpenAPI client generation (parameter order issues)
- **Absolute URLs required**: Frontend API calls must use `baseUrl + relativeUrl` to avoid port conflicts
- **Generated clients**: OpenAPI client auto-generated on build (C# & TypeScript)

### Testing Patterns
- **E2E Auth**: Always use `navigateToApp()` or navigation helpers (NOT `createE2EAuthSession()` alone)
- **Test Data Fixtures**: Use fixtures from `frontend/test/e2e/fixtures/test-data.ts` - tests MUST fail if data missing
- **Modular E2E Structure**: Tests organized in modules for parallel execution (catalog/, issued-invoices/, stock-operations/, etc.)
- **CI/CD Strategy**: Frontend + Backend tests in CI (15-20 min), Full E2E nightly (10-15 min)

### Code Formatting
- **Backend**: 4 spaces, Allman braces, `dotnet format` MANDATORY before commits
- **Frontend**: 2 spaces, Prettier, semicolons required, single quotes

### Documentation Alignment
- **MANDATORY**: All implementation MUST align with `/docs` design documents
- Read relevant docs BEFORE implementation
- Ask for clarification if conflicts arise

## CRITICAL RULES - MUST FOLLOW ALWAYS

### 1. Security Rules

**NEVER commit credentials:**
- ❌ No real passwords, API keys, or tokens in committed files
- ❌ No hardcoded credentials in test files
- ✅ Use local `.env.test` files (gitignored)
- ✅ Load credentials via `loadTestCredentials()`
- ✅ Exit with error if credentials missing

### 2. Code Formatting Standards

**Backend (.NET)**:
- **MANDATORY**: All C# code MUST pass `dotnet format` validation
- Indentation: 4 spaces (never tabs)
- Brace style: Allman style (opening braces on new line)
- Run before commits: `dotnet format`
- CI/CD validates and fails on violations

**Frontend (React/TypeScript)**:
- Use Prettier for consistency
- 2 spaces indentation
- Semicolons required, single quotes
- Run before commits: `npm run lint`

### 3. Backend DTO Rules

**CRITICAL: OpenAPI Client Generation Compatibility**

- ❌ **NEVER use C# records for API DTOs** - OpenAPI generators have parameter order issues
- ✅ **ALWAYS use classes with properties** - ensures reliable schema generation
- ✅ **Add validation attributes** - `[Required]`, `[JsonPropertyName]`, etc.

### 4. API Client URL Construction

**MANDATORY**: All API hooks MUST use absolute URLs with baseUrl.

**❌ WRONG - Relative URL**:
```typescript
const url = `/api/catalog`; // Calls wrong port!
```

**✅ CORRECT - Absolute URL**:
```typescript
const relativeUrl = `/api/catalog`;
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
const response = await (apiClient as any).http.fetch(fullUrl, {method: 'GET'});
```

### 5. E2E Test Authentication

**MANDATORY**: All E2E tests MUST use proper authentication.

**✅ CORRECT - Full authentication**:
```typescript
import { navigateToApp, navigateToCatalog } from './helpers/e2e-auth-helper';

test.beforeEach(async ({ page }) => {
  await navigateToApp(page); // Full auth: backend + frontend
  await page.goto('/your-page');
});
```

**❌ WRONG - Incomplete auth**:
```typescript
await createE2EAuthSession(page); // ❌ Only backend, missing frontend
```

### 6. Test Data Fixtures

**MANDATORY**: Use test data fixtures for consistent E2E tests.

**✅ Usage Rules**:
- Use fixtures from `frontend/test/e2e/fixtures/test-data.ts`
- Reference `docs/testing/test-data-fixtures.md`
- Tests MUST fail (not skip) when data missing
- Example: `TestCatalogItems.bisabolol` instead of hardcoding

### 7. E2E Test Modular Structure

**MANDATORY**: E2E tests organized in 6 modules for parallel execution:
- `catalog/` - Catalog page tests
- `issued-invoices/` - Invoice tests
- `stock-operations/` - Stock operation tests
- `transport/` - Transport box tests
- `manufacturing/` - Manufacturing tests
- `core/` - Core functionality tests

Shared: `helpers/`, `fixtures/`

### 8. Design Document Alignment

**MANDATORY**: All implementation MUST align with `/docs`.

**Before implementation:**
- Read relevant design documents
- Verify alignment with architecture
- Ask for clarification if conflicts

**Required documents:**
- Backend/API: `docs/📘 Architecture Documentation – MVP Work.md`
- Infrastructure: `docs/architecture/application_infrastructure.md`
- Environment: `docs/architecture/environments.md`
- Structure: `docs/architecture/filesystem.md`
- Frontend/UI: `docs/design/ui_design_document.md`
- Layouts: `docs/design/layout_definition.md` (CRITICAL for UI)

## Feature Development Quality Standards

**CRITICAL**: All new features MUST meet the following mandatory requirements before being considered complete.

### Testing Requirements

- **Test Pass Rate**: 100% - all tests must pass, no exceptions
- **Test Types Required**:
  - Backend: .NET unit/integration tests (`dotnet test`)
  - Frontend: Jest + React Testing Library (`npm test`)
  - E2E: Playwright tests (`./scripts/run-playwright-tests.sh`)
- **Test Quality**: Tests must validate behavior, not just coverage
- **Test Documentation**: Complex scenarios need explanatory comments

### Git Workflow Requirements

Before moving to next feature, ALL changes must be:

1. **Committed with Clear Messages**:
   ```bash
   git add .
   git commit -m "feat(module): descriptive message"
   ```
   - Use conventional commits: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`
   - Include scope: `feat(api):`, `fix(ui):`, `test(auth):`
   - Explain WHAT changed and WHY
   - Add `Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>` to commits

2. **Pushed to Remote Repository**:
   ```bash
   git push origin <branch-name>
   ```
   - Never leave completed features uncommitted
   - Ensure CI/CD pipelines pass

3. **Branch Hygiene**:
   - Work on feature branches, never directly on `main`
   - Naming: `feature/<name>`, `fix/<name>`, `docs/<name>`
   - Create pull requests for significant changes

4. **Ralph Integration**:
   - Update `.ralph/fix_plan.md` with new tasks before starting
   - Mark items complete upon completion
   - Update `.ralph/PROMPT.md` if patterns change

### Documentation Requirements

**ALL documentation MUST stay synchronized**:

1. **Code Documentation**:
   - Language-appropriate docs (JSDoc, XML comments)
   - Update when implementation changes
   - Remove outdated comments immediately

2. **Implementation Documentation**:
   - Update relevant sections in this AGENT.md
   - Keep build/test commands current
   - Document breaking changes prominently

3. **README Updates**:
   - Keep feature lists current
   - Update setup instructions when dependencies change
   - Maintain accurate command examples

4. **AGENT.md Maintenance**:
   - Add new build patterns
   - Update "Key Learnings" with insights
   - Keep command examples tested
   - Document new testing patterns

### Feature Completion Checklist

Before marking ANY feature as complete, verify:

- [ ] All backend tests pass (`dotnet test`)
- [ ] All frontend tests pass (`npm test`)
- [ ] E2E tests pass (if applicable) (`./scripts/run-playwright-tests.sh`)
- [ ] Code formatted: `dotnet format` (BE) and `npm run lint` (FE)
- [ ] Type checking passes (if applicable)
- [ ] All changes committed with conventional commit messages
- [ ] All commits pushed to remote repository
- [ ] `.ralph/fix_plan.md` task marked as complete
- [ ] Implementation documentation updated
- [ ] Inline code comments updated
- [ ] `.ralph/AGENT.md` updated (if new patterns)
- [ ] Breaking changes documented
- [ ] CI/CD pipeline passes
- [ ] Design document alignment verified

### Rationale

These standards ensure:
- **Quality**: High test pass rates prevent regressions
- **Traceability**: Git commits and `.ralph/fix_plan.md` provide clear history
- **Maintainability**: Current documentation reduces onboarding time
- **Collaboration**: Pushed changes enable visibility and review
- **Reliability**: Consistent quality gates maintain stability
- **Automation**: Ralph integration ensures continuous practices

**Enforcement**: AI agents should automatically apply these standards to all tasks without requiring explicit instruction.

## Implementation Guidelines

### Creating New Features:
1. **Domain First**: Define entities and interfaces in `Domain/Features/{Feature}/`
2. **Application Logic**: Create handlers in `Application/Features/{Feature}/UseCases/`
3. **Persistence**: Create configurations in `Persistence/{Feature}/`
4. **API Exposure**: Create controller in `API/Controllers/{Feature}Controller.cs`
5. **Register**: Update `{Feature}Module.cs` and `PersistenceModule.cs`

### Naming Conventions:
- Controllers: `{Feature}Controller` (e.g., `CatalogController`)
- Handlers: `{Action}{Entity}Handler` (e.g., `GetCatalogListHandler`)
- Requests/Responses: `{Action}{Entity}Request/Response`
- DTOs: `{Entity}Dto` (e.g., `CatalogItemDto`)
- Services: `{Entity}Service` and `I{Entity}Service`
- Configurations: `{Entity}Configuration`
- Repositories: `{Entity}Repository`

## Development Workflow

1. **Make changes** to code
2. **For UI changes**: Test locally with `npm start` (port 3000)
3. **For E2E testing**: Use staging environment (`./scripts/run-playwright-tests.sh`)
4. **Validate builds**: Run `dotnet build` (BE) or `npm run build` (FE)
5. **Format code**: Run `dotnet format` (BE) or `npm run lint` (FE)
6. **Run tests**: Verify all test suites pass
7. **Follow best practices**: YAGNI, SOLID, KISS, DRY
8. **Ask for clarification** if unsure

## Important Notes

- Solo developer project with AI-assisted PR reviews
- Database migrations are manual (not automated)
- OpenAPI client auto-generated on build
- All Docker images pushed to Docker Hub
- Backend follows Clean Architecture with Vertical Slice organization
- Always use `./scripts/run-playwright-tests.sh` for E2E tests
- Validate builds before claiming completion
- Read design documents before implementation