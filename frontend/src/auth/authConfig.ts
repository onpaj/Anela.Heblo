import { Configuration } from "@azure/msal-browser";
import { shouldUseMockAuth } from "./mockAuth";

/**
 * Authentication Configuration
 * Detects whether to use mock or real authentication according to documentation
 * (Authentication.md section 7.1.3)
 */

/**
 * Check if we should use mock authentication
 * Detekce probíhá v frontend/src/auth/authConfig.ts
 */
export const useMockAuth = (): boolean => {
  return shouldUseMockAuth();
};

// MSAL configuration for MS Entra ID authentication
// These values should be set via environment variables
export const msalConfig: Configuration = {
  auth: {
    clientId: process.env.REACT_APP_AZURE_CLIENT_ID || "your-client-id",
    authority:
      process.env.REACT_APP_AZURE_AUTHORITY ||
      "https://login.microsoftonline.com/your-tenant-id",
    redirectUri: process.env.REACT_APP_REDIRECT_URI || window.location.origin,
  },
  cache: {
    cacheLocation: "sessionStorage",
    storeAuthStateInCookie: false,
  },
};

// Login request configuration
export const loginRequest = {
  scopes: ["User.Read"],
};

// Graph API endpoint for user info
export const graphConfig = {
  graphMeEndpoint: "https://graph.microsoft.com/v1.0/me",
};
