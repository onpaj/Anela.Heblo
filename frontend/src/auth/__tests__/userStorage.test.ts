import { UserStorage, StoredUserInfo } from "../userStorage";
import { UserInfo } from "../useAuth";

const USER_INFO_KEY = "anela_heblo_user_info";
const LAST_LOGIN_KEY = "anela_heblo_last_login";

const baseUserInfo: UserInfo = {
  name: "Test User",
  email: "test@example.com",
  initials: "TU",
};

describe("UserStorage", () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  describe("getUserInfo", () => {
    it("returns null and clears storage when expiresAt is in the past", () => {
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: new Date().toISOString(),
        expiresAt: new Date(Date.now() - 1000).toISOString(),
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));
      sessionStorage.setItem(LAST_LOGIN_KEY, stored.lastLogin);

      const result = UserStorage.getUserInfo();

      expect(result).toBeNull();
      expect(sessionStorage.getItem(USER_INFO_KEY)).toBeNull();
      expect(sessionStorage.getItem(LAST_LOGIN_KEY)).toBeNull();
    });

    it("returns stored info unchanged when expiresAt is in the future", () => {
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: new Date().toISOString(),
        expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));

      const result = UserStorage.getUserInfo();

      expect(result).toEqual(stored);
      expect(sessionStorage.getItem(USER_INFO_KEY)).not.toBeNull();
    });

    it("treats a missing expiresAt as never-expiring", () => {
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: new Date().toISOString(),
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));

      const result = UserStorage.getUserInfo();

      expect(result).toEqual(stored);
      expect(sessionStorage.getItem(USER_INFO_KEY)).not.toBeNull();
    });
  });

  describe("updateUserInfo", () => {
    it("is a silent no-op when no session exists", () => {
      expect(sessionStorage.getItem(USER_INFO_KEY)).toBeNull();

      expect(() =>
        UserStorage.updateUserInfo({ name: "New Name" }),
      ).not.toThrow();

      expect(sessionStorage.getItem(USER_INFO_KEY)).toBeNull();
    });

    it("merges updates into an existing session without touching lastLogin or expiresAt", () => {
      const originalLastLogin = new Date(Date.now() - 60 * 1000).toISOString();
      const originalExpiresAt = new Date(
        Date.now() + 60 * 60 * 1000,
      ).toISOString();
      const stored: StoredUserInfo = {
        ...baseUserInfo,
        lastLogin: originalLastLogin,
        expiresAt: originalExpiresAt,
      };
      sessionStorage.setItem(USER_INFO_KEY, JSON.stringify(stored));

      UserStorage.updateUserInfo({ name: "Updated Name" });

      const raw = sessionStorage.getItem(USER_INFO_KEY);
      expect(raw).not.toBeNull();
      const updated: StoredUserInfo = JSON.parse(raw as string);
      expect(updated.name).toBe("Updated Name");
      expect(updated.email).toBe(baseUserInfo.email);
      expect(updated.lastLogin).toBe(originalLastLogin);
      expect(updated.expiresAt).toBe(originalExpiresAt);
    });
  });

  describe("getLastLogin", () => {
    it("returns null when LAST_LOGIN_KEY is absent", () => {
      expect(sessionStorage.getItem(LAST_LOGIN_KEY)).toBeNull();

      const result = UserStorage.getLastLogin();

      expect(result).toBeNull();
    });
  });
});
