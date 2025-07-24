import { Configuration, PopupRequest, RedirectRequest } from '@azure/msal-browser';

// MSAL configuration
export const msalConfig: Configuration = {
  auth: {
    clientId: process.env.REACT_APP_AZURE_CLIENT_ID!,
    authority: process.env.REACT_APP_AZURE_AUTHORITY!,
    redirectUri: process.env.REACT_APP_REDIRECT_URI || window.location.origin,
    postLogoutRedirectUri: process.env.REACT_APP_REDIRECT_URI || window.location.origin,
    clientCapabilities: ['CP1'] // This lets the resource owner know that this client is capable of handling claims challenges.
  },
  cache: {
    cacheLocation: 'sessionStorage', // This configures where your cache will be stored
    storeAuthStateInCookie: false, // Set this to "true" if you are having issues on IE11 or Edge
  },
  system: {
    allowNativeBroker: false // Disables WAM Broker
  }
};

// Add scopes here for ID token to be used at Microsoft identity platform endpoints.
export const loginRequest: PopupRequest = {
  scopes: ['User.Read', 'openid', 'profile'],
  prompt: 'select_account',
};

// Redirect request configuration
export const loginRedirectRequest: RedirectRequest = {
  scopes: ['User.Read', 'openid', 'profile'],
  prompt: 'select_account',
};

// Add the endpoints here for Microsoft Graph API services you'd like to use.
export const graphConfig = {
  graphMeEndpoint: 'https://graph.microsoft.com/v1.0/me',
};

// API scopes for backend
export const apiRequest: PopupRequest = {
  scopes: [`api://${process.env.REACT_APP_AZURE_CLIENT_ID}/access_as_user`],
};