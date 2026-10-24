import {
  PopupRequest,
  RedirectRequest,
} from "@azure/msal-browser";

// Add scopes here for ID token to be used at Microsoft identity platform endpoints.
export const loginRequest: PopupRequest = {
  scopes: ["User.Read", "openid", "profile"],
  prompt: "select_account",
};

// Redirect request configuration
export const loginRedirectRequest: RedirectRequest = {
  scopes: ["User.Read", "openid", "profile"],
  prompt: "select_account",
};

// API scopes for backend
// Request token specifically for our backend API
export const apiRequest: PopupRequest = {
  scopes: [`api://${process.env.REACT_APP_AZURE_BACKEND_CLIENT_ID}/.default`], // Backend API scope
};
