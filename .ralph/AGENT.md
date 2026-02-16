# Agent Build Instructions - Anela Heblo

**Project**: Anela Heblo - Cosmetics company workspace application
**Stack**: .NET 8 + React, Clean Architecture with Vertical Slice organization, MediatR + MVC Controllers
**Deployment**: Single Docker image, Azure Web App for Containers

## Quick Reference

- **Full Documentation**: See `CLAUDE.md` for comprehensive project guidance
- **Architecture**: `docs/📘 Architecture Documentation – MVP Work.md`
- **Setup Guide**: `docs/development/setup.md`
- **UI Design**: `docs/design/ui_design_document.md`
- **Testing Guide**: `docs/testing/playwright-e2e-testing.md`

---

## Project Setup

### Initial Setup (First Time)
```bash
# Backend (.NET 8)
cd backend
dotnet restore

# Frontend (React + TypeScript)
cd frontend
npm install

# Docker (for full-stack development)
docker-compose up -d
```

### Development Environment Setup
See `docs/development/setup.md` for detailed setup instructions including:
- PostgreSQL database setup
- Microsoft Entra ID authentication configuration
- OpenAPI client generation
- Environment variables

---

## Running Tests

### Backend Tests (.NET)
```bash
# Run all backend tests
cd backend
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverageReportsFormat=opencover

# Run specific test project
dotnet test tests/Anela.Heblo.Application.Tests
```

### Frontend Tests (React + Jest)
```bash
# Run all frontend tests
cd frontend
npm test

# Run with coverage
npm test -- --coverage

# Run in watch mode
npm test -- --watch

# Run specific test file
npm test -- Dashboard.test.tsx
```

### E2E Tests (Playwright)
```bash
# Run all E2E tests against staging
./scripts/run-playwright-tests.sh

# Run specific module
./scripts/run-playwright-tests.sh catalog
./scripts/run-playwright-tests.sh issued-invoices
./scripts/run-playwright-tests.sh core

# Run with UI (headed mode)
./scripts/run-playwright-tests.sh --headed

# Generate test report
./scripts/run-playwright-tests.sh --reporter=html
```

**CRITICAL**: E2E tests run against staging environment (https://heblo.stg.anela.cz) with real Microsoft Entra ID authentication.

See `docs/testing/playwright-e2e-testing.md` for complete E2E testing guide.

---

## Build Commands

### Backend Build
```bash
cd backend
dotnet build

# Production build
dotnet build -c Release

# Build specific project
dotnet build src/Anela.Heblo.API
```

### Frontend Build
```bash
cd frontend

# Development build
npm run build

# Production build (optimized)
npm run build -- --production

# Type checking
npm run typecheck
```

### Docker Build
```bash
# Build Docker image
docker build -t anela-heblo:latest .

# Build with specific tag
docker build -t anela-heblo:v1.0.0 .

# Build and push to Docker Hub
docker build -t pajgrt/anela.heblo:latest .
docker push pajgrt/anela.heblo:latest
```

---

## Development Server

### Backend Development Server
```bash
cd backend
dotnet run --project src/Anela.Heblo.API

# Backend runs on: http://localhost:5001 (HTTPS)
# Swagger UI: http://localhost:5001/swagger
```

### Frontend Development Server
```bash
cd frontend
npm start

# Frontend runs on: http://localhost:3000
# Proxy configured to backend at localhost:5001
```

### Full Stack with Docker
```bash
# Start all services (backend + frontend + database)
docker-compose up

# Start in background
docker-compose up -d

# Stop all services
docker-compose down

# View logs
docker-compose logs -f
```

---

## Code Formatting & Validation

### Backend (.NET)
```bash
# Format code (MANDATORY before commits)
cd backend
dotnet format

# Check formatting without applying changes
dotnet format --verify-no-changes

# Format specific project
dotnet format src/Anela.Heblo.API
```

**CRITICAL**: All C# code MUST pass `dotnet format` validation. CI/CD will fail on formatting violations.

**Formatting Rules**:
- Indentation: 4 spaces (never tabs)
- Brace style: Allman style (opening braces on new line)
- Run `dotnet format` before every commit

### Frontend (React/TypeScript)
```bash
# Lint code
cd frontend
npm run lint

# Fix linting issues automatically
npm run lint -- --fix

# Type checking
npm run typecheck
```

**Formatting Rules**:
- Indentation: 2 spaces for TypeScript/JSX
- Semicolons required
- Single quotes for strings
- Use Prettier for consistent formatting

---

## Key Learnings & Best Practices

### Architecture Patterns

**Clean Architecture with Vertical Slice Organization:**
- Each feature contains all its layers (Domain → Application → Persistence → API)
- Controllers send requests to MediatR handlers
- All endpoints follow `/api/{controller}` REST conventions

**Feature Structure:**
```
src/
├── Anela.Heblo.Domain/Features/{Feature}/
│   ├── Entities/
│   ├── Repositories/
│   └── Services/
├── Anela.Heblo.Application/Features/{Feature}/UseCases/
│   ├── {Action}{Entity}Handler.cs
│   └── {Action}{Entity}Request.cs
├── Anela.Heblo.Persistence/{Feature}/
│   ├── Configurations/
│   └── Repositories/
└── Anela.Heblo.API/Controllers/
    └── {Feature}Controller.cs
```

See `docs/architecture/filesystem.md` for detailed component placement rules.

### Naming Conventions

- **Controllers**: `{Feature}Controller` (e.g., `CatalogController`)
- **Handlers**: `{Action}{Entity}Handler` (e.g., `GetCatalogListHandler`)
- **Requests/Responses**: `{Action}{Entity}Request/Response`
- **DTOs**: `{Entity}Dto` (e.g., `CatalogItemDto`)
- **Services**: `{Entity}Service` and `I{Entity}Service`
- **Entity Configurations**: `{Entity}Configuration`
- **Repositories**: `{Entity}Repository`

### Common Gotchas

1. **OpenAPI Client Generation**: Always use classes (not records) for API DTOs to ensure reliable schema generation
2. **API URL Construction**: Use absolute URLs with `baseUrl` to avoid calling wrong endpoints
3. **E2E Test Authentication**: Always use `navigateToApp()` or navigation helpers (never use `createE2EAuthSession()` alone)
4. **Test Data**: Use fixtures from `frontend/test/e2e/fixtures/test-data.ts` instead of hardcoding values
5. **E2E Test Organization**: Place tests in appropriate module directory (`catalog/`, `issued-invoices/`, etc.)

---

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

**❌ WRONG - Never use createE2EAuthSession() alone:**
```typescript
await createE2EAuthSession(page); // ❌ Only backend session, missing frontend
await page.goto('/your-page');    // ❌ Will show Microsoft login screen!
```

### 6. Test Data Fixtures

**MANDATORY**: When writing E2E tests that require consistent, reliable data, ALWAYS use test data fixtures.

**Usage Rules:**
- ✅ Use fixtures from `frontend/test/e2e/fixtures/test-data.ts`
- ✅ Reference `docs/testing/test-data-fixtures.md` for available test data
- ✅ Tests MUST fail (not skip) when expected data is missing - throw clear error messages
- ✅ Example: Use `TestCatalogItems.bisabolol` instead of hardcoding "AKL001"

### 7. E2E Test Modular Structure

**MANDATORY**: All E2E test files MUST be organized into modular structure for parallel execution.

**Module Structure:**
- `catalog/` - Catalog page tests
- `issued-invoices/` - Issued invoices tests
- `stock-operations/` - Stock operations tests
- `transport/` - Transport box tests
- `manufacturing/` - Manufacturing tests
- `core/` - Core functionality tests (dashboard, changelog, navigation, auth)

**Shared directories:**
- `helpers/` - Test helper functions and utilities
- `fixtures/` - Test data fixtures

**Running tests by module:**
```bash
./scripts/run-playwright-tests.sh catalog
./scripts/run-playwright-tests.sh issued-invoices
./scripts/run-playwright-tests.sh core
```

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
- NO implementation without documentation consultation
- NO architectural deviations without approval
- MANDATORY documentation updates when implementation changes

---

## Testing Strategy

**Three types of tests:**
1. **BE (Backend)**: .NET unit/integration tests - Run with `dotnet test`
2. **FE (Frontend)**: Jest + React Testing Library - Run with `npm test`
3. **UI (E2E)**: Playwright against staging - Run with `./scripts/run-playwright-tests.sh`

**Test Organization:**
- **Unit/Integration**: Co-located in `__tests__/` folders next to components
- **E2E Tests**: Located in `/frontend/test/e2e/` directory (MUST use modular structure - see rule 7)

**Playwright Testing:**
- **Target**: Always run against staging environment (https://heblo.stg.anela.cz)
- **Authentication**: Real Microsoft Entra ID (not mock)
- **Commands**: See `docs/testing/playwright-e2e-testing.md`

**CI/CD Testing Strategy:**
- **In CI**: Frontend Jest + Backend .NET tests (fast feedback, 15-20 min)
- **Nightly**: Full Playwright E2E suite against staging (comprehensive, 10-15 min)
- **E2E tests do NOT run in PR CI** - only nightly regression

---

## Feature Development Quality Standards

**CRITICAL**: All new features MUST meet the following mandatory requirements before being considered complete.

### Testing Requirements

- **Test Pass Rate**: 100% - all tests must pass, no exceptions
- **Test Types Required**:
  - Unit tests for all business logic and services
  - Integration tests for API endpoints or main functionality
  - End-to-end tests for critical user workflows (if applicable)
- **Test Commands**:
  ```bash
  # Backend tests
  cd backend && dotnet test

  # Frontend tests
  cd frontend && npm test

  # E2E tests
  ./scripts/run-playwright-tests.sh
  ```
- **Test Quality**: Tests must validate behavior, not just achieve coverage metrics
- **Test Documentation**: Complex test scenarios must include comments explaining the test strategy

### Code Quality Requirements

- **Backend Formatting**: `dotnet format` must pass (CI will fail otherwise)
- **Frontend Linting**: `npm run lint` must pass with no errors
- **Type Checking**: `npm run typecheck` must pass (TypeScript)
- **Build Validation**: Both `dotnet build` (BE) and `npm run build` (FE) must succeed

### Git Workflow Requirements

Before moving to the next feature, ALL changes must be:

1. **Committed with Clear Messages**:
   ```bash
   git add .
   git commit -m "feat(module): descriptive message following conventional commits"
   ```
   - Use conventional commit format: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, etc.
   - Include scope when applicable: `feat(api):`, `fix(ui):`, `test(auth):`
   - Write descriptive messages that explain WHAT changed and WHY

2. **Pushed to Remote Repository**:
   ```bash
   git push origin <branch-name>
   ```
   - Never leave completed features uncommitted
   - Push regularly to maintain backup and enable collaboration
   - Ensure CI/CD pipelines pass before considering feature complete

3. **Branch Hygiene**:
   - Work on feature branches, never directly on `main`
   - Branch naming convention: `feature/<feature-name>`, `fix/<issue-name>`, `docs/<doc-update>`
   - Create pull requests for all significant changes

4. **Ralph Integration**:
   - Update `.ralph/fix_plan.md` with new tasks before starting work
   - Mark items complete in `.ralph/fix_plan.md` upon completion
   - Update `.ralph/PROMPT.md` if development patterns change
   - Test features work within Ralph's autonomous loop

### Documentation Requirements

**ALL implementation documentation MUST remain synchronized with the codebase**:

1. **Code Documentation**:
   - JSDoc for TypeScript/React components
   - XML documentation comments for C# public APIs
   - Update inline comments when implementation changes
   - Remove outdated comments immediately

2. **Implementation Documentation**:
   - Update relevant sections in this AGENT.md file
   - Keep build and test commands current
   - Update configuration examples when defaults change
   - Document breaking changes prominently

3. **Architecture Documentation**:
   - Update `docs/` when adding new features or changing patterns
   - Keep `docs/architecture/filesystem.md` synchronized with file structure
   - Update `docs/design/ui_design_document.md` for new UI components
   - Maintain `docs/testing/` guides for test updates

4. **AGENT.md Maintenance**:
   - Add new build patterns to relevant sections
   - Update "Key Learnings" with new insights
   - Keep command examples accurate and tested
   - Document new testing patterns or quality gates

### Feature Completion Checklist

Before marking ANY feature as complete, verify:

- [ ] All backend tests pass: `cd backend && dotnet test`
- [ ] All frontend tests pass: `cd frontend && npm test`
- [ ] E2E tests pass (if applicable): `./scripts/run-playwright-tests.sh`
- [ ] Backend code formatted: `cd backend && dotnet format`
- [ ] Frontend code linted: `cd frontend && npm run lint`
- [ ] TypeScript type checking passes: `cd frontend && npm run typecheck`
- [ ] Backend builds successfully: `cd backend && dotnet build`
- [ ] Frontend builds successfully: `cd frontend && npm run build`
- [ ] All changes committed with conventional commit messages
- [ ] All commits pushed to remote repository
- [ ] `.ralph/fix_plan.md` task marked as complete
- [ ] Implementation documentation updated in `/docs`
- [ ] Inline code comments updated or added
- [ ] `.ralph/AGENT.md` updated (if new patterns introduced)
- [ ] Breaking changes documented
- [ ] Features tested within Ralph loop (if applicable)
- [ ] CI/CD pipeline passes

### Rationale

These standards ensure:
- **Quality**: Comprehensive testing prevents regressions
- **Traceability**: Git commits and `.ralph/fix_plan.md` provide clear history of changes
- **Maintainability**: Current documentation reduces onboarding time and prevents knowledge loss
- **Collaboration**: Pushed changes enable team visibility and code review
- **Reliability**: Consistent quality gates maintain production stability
- **Automation**: Ralph integration ensures continuous development practices

**Enforcement**: AI agents should automatically apply these standards to all feature development tasks without requiring explicit instruction for each task.

---

## Development Workflow

1. **Make changes** to code following architecture patterns
2. **For UI changes**: Test locally with `npm start` (port 3000)
3. **For backend changes**: Test with `dotnet run --project src/Anela.Heblo.API` (port 5001)
4. **For E2E testing**: Use staging environment (`./scripts/run-playwright-tests.sh`)
5. **Validate builds**: Run `dotnet build` (BE) and `npm run build` (FE) before finishing
6. **Format code**: Run `dotnet format` (BE) and `npm run lint` (FE)
7. **Run tests**: Execute all relevant test suites
8. **Commit changes**: Use conventional commits with clear messages
9. **Push to remote**: Ensure CI/CD pipeline passes
10. **Update documentation**: Keep all docs synchronized

See `docs/development/setup.md` for detailed development commands and setup instructions.

---

## Important Project Notes

- Solo developer project with AI-assisted PR reviews
- Database migrations are manual (not automated in deployment)
- OpenAPI client auto-generated on build
- All Docker images pushed to Docker Hub (`pajgrt/anela.heblo`)
- Backend follows Clean Architecture with Vertical Slice organization
- To run Playwright tests: Always use `./scripts/run-playwright-tests.sh` script
- Validate builds before claiming completion
- Follow YAGNI, SOLID, KISS, DRY principles
