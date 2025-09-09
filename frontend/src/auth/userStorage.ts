import { UserInfo } from "./useAuth";

const USER_INFO_KEY = "anela_heblo_user_info";
const LAST_LOGIN_KEY = "anela_heblo_last_login";

export interface StoredUserInfo extends UserInfo {
  lastLogin: string;
  expiresAt?: string;
}

/**
 * Local storage utility for user information
 * Uses sessionStorage for security as recommended by auth documentation
 */
export class UserStorage {
  /**
   * Store user information in session storage
   */
  static setUserInfo(userInfo: UserInfo): void {
    try {
      const storedUserInfo: StoredUserInfo = {
        ...userInfo,
        lastLogin: new Date().toISOString(),
        // Set expiration to 24 hours from now
        expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
      };

      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(storedUserInfo));
      sessionStorage.setItem(LAST_LOGIN_KEY, storedUserInfo.lastLogin);

      console.log("User info stored successfully");
    } catch (error) {
      console.warn("Failed to store user info:", error);
    }
  }

  /**
   * Retrieve user information from session storage
   */
  static getUserInfo(): StoredUserInfo | null {
    try {
      const stored = sessionStorage.getItem(USER_INFO_KEY);
      if (!stored) return null;

      const userInfo: StoredUserInfo = JSON.parse(stored);

      // Check if expired
      if (userInfo.expiresAt && new Date() > new Date(userInfo.expiresAt)) {
        console.log("Stored user info expired, clearing...");
        this.clearUserInfo();
        return null;
      }

      return userInfo;
    } catch (error) {
      console.warn("Failed to retrieve user info:", error);
      return null;
    }
  }

  /**
   * Clear user information from storage
   */
  static clearUserInfo(): void {
    try {
      sessionStorage.removeItem(USER_INFO_KEY);
      sessionStorage.removeItem(LAST_LOGIN_KEY);
      console.log("User info cleared from storage");
    } catch (error) {
      console.warn("Failed to clear user info:", error);
    }
  }

  /**
   * Check if user info exists and is valid
   */
  static hasValidUserInfo(): boolean {
    const userInfo = this.getUserInfo();
    return userInfo !== null;
  }

  /**
   * Get last login timestamp
   */
  static getLastLogin(): Date | null {
    try {
      const lastLogin = sessionStorage.getItem(LAST_LOGIN_KEY);
      return lastLogin ? new Date(lastLogin) : null;
    } catch (error) {
      console.warn("Failed to get last login:", error);
      return null;
    }
  }

  /**
   * Update user info without changing login timestamp
   */
  static updateUserInfo(updates: Partial<UserInfo>): void {
    const current = this.getUserInfo();
    if (current) {
      const updated: StoredUserInfo = {
        ...current,
        ...updates,
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(updated));
    }
  }
}
