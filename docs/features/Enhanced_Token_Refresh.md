# Enhanced Silent Token Refresh Implementation

## Overview

This document describes the enhanced token refresh implementation that automatically handles token expiration and provides seamless user experience when tokens expire.

## Key Features

### 1. **Enhanced `getAccessToken` Method** (`frontend/src/auth/useAuth.ts`)

- **Silent token refresh**: Uses MSAL's `acquireTokenSilent` with enhanced error handling
- **Automatic redirect on expiration**: When `InteractionRequiredAuthError` occurs, automatically redirects to login
- **Popup fallback**: If redirect fails, falls back to popup authentication
- **Force refresh support**: Optional parameter to force token refresh
- **Clean session handling**: Clears cached data before redirecting

### 2. **Global 401 Interceptor** (`frontend/src/api/client.ts`)

- **Automatic detection**: Detects 401 Unauthorized responses from API calls
- **Cache clearing**: Automatically clears token cache on 401 errors
- **Redirect triggering**: Triggers automatic login redirect via global handler
- **Toast notifications**: Shows appropriate error messages to users

### 3. **Global Authentication State Management** (`frontend/src/App.tsx`)

- **Enhanced token provider**: Global token provider with force refresh support
- **Automatic redirect handler**: Centralized handler for authentication redirects
- **Session cleanup**: Clears session storage during redirects
- **Fallback handling**: Multiple fallback mechanisms for reliability

## Implementation Details

### Token Refresh Flow

```
1. API Call Made
   ↓
2. Token Cache Check (5-minute buffer)
   ↓
3. If expired/missing: acquireTokenSilent()
   ↓
4. If InteractionRequiredAuthError:
   ↓
5. Clear token cache
   ↓
6. Trigger loginRedirect() with account picker
   ↓
7. User completes authentication
   ↓
8. Return to application with fresh tokens
```

### 401 Error Handling Flow

```
1. API Response: 401 Unauthorized
   ↓
2. Clear token cache
   ↓
3. Trigger global auth redirect handler
   ↓
4. Clear session storage
   ↓
5. Execute loginRedirect()
   ↓
6. Show error toast (optional)
```

## Benefits

### For Users
- **Seamless experience**: No manual refresh needed when tokens expire
- **Automatic recovery**: Application handles token expiration transparently
- **Clear feedback**: Appropriate error messages when issues occur
- **Account picker**: Shows account selection for expired sessions

### For Developers
- **Centralized handling**: All token refresh logic in one place
- **Consistent behavior**: Same token handling across all API calls
- **Error resilience**: Multiple fallback mechanisms
- **Debug logging**: Comprehensive logging for troubleshooting

## Configuration

### Environment Variables
No new environment variables required. Uses existing MSAL configuration.

### MSAL Configuration
Uses existing configuration from `frontend/src/auth/msalConfig.ts`:
- `apiRequest`: Scopes for API access
- `loginRedirectRequest`: Configuration for login redirects

## Usage Examples

### Force Token Refresh
```typescript
const { getAccessToken } = useAuth();

// Force refresh token
const token = await getAccessToken(true);
```

### API Client Integration
Token refresh is automatic for all API calls using `getAuthenticatedApiClient()`:

```typescript
const apiClient = getAuthenticatedApiClient();
const response = await apiClient.someApiCall();
// Token refresh handled automatically if needed
```

## Testing

### Manual Testing
1. Wait for token to expire (typically 1 hour)
2. Make any API call
3. Verify automatic redirect to login occurs
4. Complete authentication
5. Verify application returns to normal operation

### Debug Logging
All token operations include console logging with 🔐 emoji for easy identification:
- `🔐 Attempting silent token acquisition...`
- `✅ Silent token acquisition successful`
- `⚠️ Silent token acquisition failed`
- `🔐 Interaction required - redirecting to login...`

## Browser Compatibility

- **Chrome/Edge**: Full support with redirect flow
- **Firefox**: Full support with redirect flow  
- **Safari**: Enhanced support with ITP (Intelligent Tracking Prevention) handling
- **Mobile browsers**: Optimized for mobile authentication flows

## Security Considerations

- **Session cleanup**: All session data cleared during authentication flows
- **Token caching**: 5-minute buffer prevents unnecessary token requests
- **HTTPS only**: All authentication flows require HTTPS
- **Account picker**: Prevents session hijacking in shared environments

## Troubleshooting

### Common Issues

1. **Redirect loops**: Check MSAL configuration and environment variables
2. **Popup blocked**: Browser popup blocker preventing fallback authentication
3. **CORS errors**: Verify API CORS configuration for authentication endpoints

### Debug Steps

1. Check browser console for 🔐 prefixed log messages
2. Verify MSAL instance initialization
3. Check network tab for authentication requests
4. Verify token cache state in session storage

## Future Enhancements

- **Background token refresh**: Proactive token refresh before expiration
- **Multiple account support**: Enhanced handling for multiple user accounts
- **Offline support**: Token handling when network is unavailable
- **Custom retry policies**: Configurable retry behavior for failed token requests