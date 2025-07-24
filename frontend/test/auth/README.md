# Authentication Tests

## ⚠️ SECURITY FIRST - Setup Required

Before running these tests, you MUST create a local credentials file:

```bash
# Create this file (NEVER commit it):
frontend/test/auth/.env.test
```

**File content:**
```bash
TEST_EMAIL=your_email@domain.com
TEST_PASSWORD=your_password
```

**Security rules:**
- ❌ NEVER commit `.env.test` files
- ❌ NEVER put real credentials in source code
- ✅ Keep credentials only in local files
- ✅ File is already in `.gitignore`

## Available Tests

### `login_test.js`
Tests basic MS Entra ID login flow:
- Click sign in button
- Redirect to Microsoft login
- Fill credentials
- Redirect back to app
- Verify login success

```bash
node login_test.js
```

### `profile_test.js` 
Tests user profile functionality:
- Check if user is logged in
- Verify user initials display
- Test profile menu opening

```bash 
node profile_test.js
```

### `full_test.js`
Comprehensive test covering complete flow:
- Login process
- Profile functionality
- User menu interactions

```bash
node full_test.js
```

## Test Results

Tests exit with clear status codes:
- ✅ Exit code 0: SUCCESS
- ❌ Exit code 1: FAILURE

All tests provide detailed console output with emoji indicators for easy debugging.

## Prerequisites

1. Frontend development server running on `localhost:3000`
2. Playwright installed: `npx playwright install`
3. Valid MS Entra ID credentials in `.env.test`

## Security Compliance

These tests follow security best practices:
- No hardcoded credentials
- Local-only credential storage
- Proper gitignore configuration
- Clear setup instructions