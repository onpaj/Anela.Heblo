import { useMsal } from "@azure/msal-react";
import { loginRequest, loginRedirectRequest, apiRequest } from "./msalConfig";
import { AccountInfo, InteractionRequiredAuthError } from "@azure/msal-browser";
import { UserStorage, StoredUserInfo } from "./userStorage";
import { useEffect, useState } from "react";
import { clearTokenCache } from "../api/client";

export interface UserInfo {
  name: string;
  email: string;
  initials: string;
  roles?: string[];
}

export const useAuth = () => {
  const { instance, accounts, inProgress } = useMsal();
  const [storedUserInfo, setStoredUserInfo] = useState<StoredUserInfo | null>(
    null,
  );

  const isAuthenticated = accounts.length > 0;
  const account: AccountInfo | null = accounts[0] || null;

  // Load stored user info on mount
  useEffect(() => {
    const stored = UserStorage.getUserInfo();
    setStoredUserInfo(stored);
  }, []);

  // Update stored user info when authentication state changes
  useEffect(() => {
    if (isAuthenticated && account) {
      const userInfo = extractUserInfoFromAccount(account);
      UserStorage.setUserInfo(userInfo);
      setStoredUserInfo({ ...userInfo, lastLogin: new Date().toISOString() });
    } else if (!isAuthenticated) {
      UserStorage.clearUserInfo();
      setStoredUserInfo(null);
    }
  }, [isAuthenticated, account]);

  // Helper function to extract user info from account
  const extractUserInfoFromAccount = (account: AccountInfo): UserInfo => {
    const name = account.name || account.username || "Unknown User";
    const email = account.username || "";

    // Generate initials from name
    const initials = name
      .split(" ")
      .map((part) => part.charAt(0).toUpperCase())
      .slice(0, 2)
      .join("");

    // Extract roles from ID token claims
    const idTokenClaims = account.idTokenClaims as any;
    const roles = idTokenClaims?.roles || [];

    return {
      name,
      email,
      initials: initials || email.charAt(0).toUpperCase(),
      roles,
    };
  };

  const login = async () => {
    try {
      // Try redirect flow first (better for SPA)
      await instance.loginRedirect(loginRedirectRequest);
    } catch (error) {
      console.error("Login failed:", error);
      // Fallback to popup if redirect fails
      try {
        await instance.loginPopup(loginRequest);
      } catch (popupError) {
        console.error("Popup login also failed:", popupError);
        throw popupError;
      }
    }
  };

  const logout = async () => {
    try {
      // Clear stored user info before logout
      UserStorage.clearUserInfo();
      setStoredUserInfo(null);

      // Clear token cache
      clearTokenCache();

      // Use redirect logout (more reliable for SPA)
      await instance.logoutRedirect({
        account: account,
        postLogoutRedirectUri: window.location.origin,
      });
    } catch (error) {
      console.error("Logout failed:", error);
      throw error;
    }
  };

  const getAccessToken = async (forceRefresh: boolean = false): Promise<string | null> => {
    if (!account) {
      console.log("🔐 No account available for token acquisition");
      return null;
    }

    try {
      console.log("🔐 Attempting silent token acquisition...");
      const response = await instance.acquireTokenSilent({
        ...apiRequest,
        account: account,
        forceRefresh, // Allow forcing refresh of tokens
      });
      console.log("✅ Silent token acquisition successful");
      return response.accessToken;
    } catch (error) {
      console.log("⚠️ Silent token acquisition failed:", error);
      
      if (error instanceof InteractionRequiredAuthError) {
        console.log("🔐 Interaction required - redirecting to login...");

        // Clear token cache before redirect
        clearTokenCache();

        // Clear stored user info to ensure clean state
        UserStorage.clearUserInfo();

        try {
          // Attempt silent SSO first; only show account picker if SSO itself fails
          await instance.loginRedirect({
            ...loginRedirectRequest,
            prompt: "none",
          });

          // This won't be reached due to redirect, but return null for type safety
          return null;
        } catch (redirectError) {
          console.error("❌ Silent SSO redirect failed, falling back to account picker:", redirectError);

          // Fallback to popup with account picker if silent SSO redirect fails
          try {
            console.log("🔐 Fallback to popup login...");
            const popupResponse = await instance.loginPopup({
              ...loginRequest,
              prompt: "select_account",
            });
            
            // After successful popup login, try to get token again
            const tokenResponse = await instance.acquireTokenSilent({
              ...apiRequest,
              account: popupResponse.account,
            });
            
            return tokenResponse.accessToken;
          } catch (popupError) {
            console.error("❌ Popup login also failed:", popupError);
            return null;
          }
        }
      } else {
        console.error("❌ Non-interaction token error:", error);
        return null;
      }
    }
  };

  const getUserInfo = (): UserInfo | null => {
    // Return stored user info if available (faster and persists across refreshes)
    if (storedUserInfo) {
      return {
        name: storedUserInfo.name,
        email: storedUserInfo.email,
        initials: storedUserInfo.initials,
        roles: storedUserInfo.roles,
      };
    }

    // Fallback to extracting from account if not stored yet
    if (account) {
      return extractUserInfoFromAccount(account);
    }

    return null;
  };

  const getStoredUserInfo = (): StoredUserInfo | null => {
    return storedUserInfo;
  };

  return {
    isAuthenticated,
    account,
    inProgress,
    login,
    logout,
    getAccessToken,
    getUserInfo,
    getStoredUserInfo,
  };
};
