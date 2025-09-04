# E2E Tests for Staging Environment

## Overview

This directory contains end-to-end tests that run against the staging environment using Service Principal authentication with backend test-only endpoint.

## Configuration

Tests use credentials from `/frontend/.env.test`:
- `AZURE_CLIENT_ID` - Service Principal Application ID  
- `AZURE_CLIENT_SECRET` - Service Principal Secret
- `AZURE_TENANT_ID` - Azure Tenant ID
- `PLAYWRIGHT_BASE_URL` - Staging environment URL

## Authentication Strategy (Best Practice Implementation)

E2E tests use app-only authentication with backend test endpoint to create authenticated sessions:

### 1. App-Only Token Acquisition
- Playwright obtains **app-only access_token** via client_credentials flow
- Token audience: `api://your-api-app-id/.default` (backend API)
- No user interaction or MFA required - fully automated

### 2. Backend Test-Only Endpoint
**POST `/api/e2etest/auth`** (staging environment only):
- **Input**: Bearer token in Authorization header
- **Validation**: Backend verifies:
  - Token issuer (Microsoft)
  - Token audience (matches API app ID)  
  - App ID matches expected test client
  - App roles/permissions if required
- **Action**: Creates synthetic "e2e-test-user" with required roles/claims
- **Output**: Sets standard application session cookie (same as normal login)

### 3. Authenticated Test Execution
- Test calls auth endpoint to establish session
- Backend responds with `Set-Cookie` (standard app session)
- Subsequent requests use session cookie (no special headers needed)
- Tests run as authenticated user - no UI login required

### 4. Security Model
- **Staging-only**: Test endpoint disabled in production
- **Token validation**: Full Azure AD token verification
- **App identity**: Service Principal must be pre-registered
- **Session isolation**: Each test gets fresh session
- **Automatic cleanup**: Sessions expire normally

**Benefits:**
- ✅ No UI login complexity - tests focus on functionality
- ✅ No MFA interruption - fully automated
- ✅ Secure token validation - not just header bypass
- ✅ Standard session cookies - behaves like real user
- ✅ Fast execution - single auth call per test

## Running Tests

```bash
# Run all e2e tests
npx playwright test test/e2e/

# Run specific test
npx playwright test test/e2e/staging-auth.spec.ts

# Run with headed browser
npx playwright test test/e2e/ --headed

# Run with debug
npx playwright test test/e2e/ --debug
```

## Test Structure

- `staging-auth.spec.ts` - Main authentication and navigation tests
  - App-only authentication with service principal
  - Backend test endpoint session creation
  - Basic navigation verification  
  - API call authentication validation

## Setup

1. **Service Principal is pre-configured** in Azure AD with API permissions for backend
2. **Credentials are already set** in `/frontend/.env.test` (gitignored for security)
3. **Backend test endpoint** `/api/e2etest/auth` is configured in staging environment only

## Implementation Requirements

### Backend Requirements
- **Test-only controller** `E2ETestController` in staging environment
- **POST `/api/e2etest/auth`** endpoint with:
  - Bearer token validation (issuer, audience, app ID)
  - Synthetic user creation with required claims
  - Standard session cookie generation (same as normal login)
- **Environment restriction**: Endpoint disabled in production
- **Security validation**: Full Azure AD token verification

### Frontend Test Helper
- **Token acquisition**: Client credentials flow for app-only token
- **Session creation**: POST call to `/api/e2etest/auth` with Bearer token
- **Cookie management**: Automatic session cookie handling by browser
- **Error handling**: Clear error messages for auth failures

## Notes

- Tests run against live staging environment using app-only authentication
- Backend validates Service Principal tokens via standard Azure AD validation
- Test endpoint only works in Staging environment for security
- Each test gets fresh authenticated session via backend endpoint
- Tests focus on application functionality without dealing with login UI flows
- No MFA or user interaction required - fully automated
- Sessions behave identically to real user sessions