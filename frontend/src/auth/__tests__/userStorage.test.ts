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
  });
});
