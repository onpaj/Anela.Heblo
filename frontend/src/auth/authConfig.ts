import { Configuration } from '@azure/msal-browser';

// MSAL configuration for MS Entra ID authentication
// These values should be set via environment variables
export const msalConfig: Configuration = {
  auth: {
    clientId: process.env.REACT_APP_AZURE_CLIENT_ID || 'your-client-id',
    authority: process.env.REACT_APP_AZURE_AUTHORITY || 'https://login.microsoftonline.com/your-tenant-id',
    redirectUri: process.env.REACT_APP_REDIRECT_URI || window.location.origin,
  },
  cache: {
    cacheLocation: 'sessionStorage',
    storeAuthStateInCookie: false,
  },
};

// Login request configuration
export const loginRequest = {
  scopes: ['User.Read'],
};

// Graph API endpoint for user info
export const graphConfig = {
  graphMeEndpoint: 'https://graph.microsoft.com/v1.0/me',
};