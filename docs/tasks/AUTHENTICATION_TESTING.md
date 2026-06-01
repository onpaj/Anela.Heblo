# Authentication Testing Guide

## Security Notice
**NEVER store real credentials in source control or share them in code.**

## Safe Testing Approaches

### 1. Manual Testing (Recommended)
- Use your real credentials directly in the browser
- Open http://localhost:3000
- Click "Sign in" button
- Use the MS popup to enter your credentials manually
- Test the user profile and logout functionality

### 2. Mock Authentication (Development)
For development testing without real credentials:

1. Add to your `.env` file:
```
REACT_APP_USE_MOCK_AUTH=true
```

2. This will simulate a logged-in user with mock data
3. All authentication flows work without real MS credentials

### 3. Browser DevTools Testing
1. After successful login, check:
   - Application tab → Session Storage → User info stored
   - Console for authentication logs
   - Network tab for MS authentication requests

## What to Test

### Login Flow
- [ ] Sign in button appears for anonymous users
- [ ] MS popup opens correctly
- [ ] User info displays after successful login
- [ ] Session storage contains user data
- [ ] User initials appear in sidebar

### User Profile
- [ ] User menu shows name, email, roles
- [ ] Last login timestamp displays
- [ ] Role badges appear correctly
- [ ] Profile works in both expanded/collapsed sidebar

### Logout Flow
- [ ] Sign out button works
- [ ] Session storage cleared
- [ ] Returns to anonymous state
- [ ] Sign in button reappears

### Persistence
- [ ] Page refresh maintains login state
- [ ] User info survives browser restart (within session)
- [ ] Data expires after 24 hours

## Playwright Testing Commands

```bash
# Test the full flow (will show sign-in button)
npx playwright codegen localhost:3000

# Test manually in browser
npm start
# Then navigate to http://localhost:3000
```

## Troubleshooting

### Common Issues
1. **Popup blocked**: Allow popups for localhost:3000
2. **CORS errors**: Ensure MS Entra ID config allows localhost
3. **Storage issues**: Check browser permissions for sessionStorage

### Debug Information
Check browser console for:
- MSAL errors
- Authentication flow logs  
- Storage operation logs
- Network request failures

## Security Best Practices
- ✅ Credentials never in source code
- ✅ SessionStorage used (not localStorage)
- ✅ Auto-expiration after 24 hours
- ✅ Proper cleanup on logout
- ✅ HTTPS required in production