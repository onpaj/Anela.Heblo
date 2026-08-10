import {
  EventMessage,
  EventType,
  InteractionType,
  IPublicClientApplication,
} from "@azure/msal-browser";

// Mock dependencies (hoisted). These apply to fresh requires under jest.resetModules too.
jest.mock("../../api/client", () => ({
  clearTokenCache: jest.fn(),
}));

jest.mock("../userStorage", () => ({
  UserStorage: {
    clearUserInfo: jest.fn(),
  },
}));

jest.mock("../msalConfig", () => ({
  apiRequest: {
    scopes: ["api://backend-client-id/.default"],
  },
  loginRedirectRequest: {
    scopes: ["User.Read", "openid", "profile"],
    prompt: "select_account",
  },
}));

const RECOVERY_KEY = "auth.recovery";

const makeInstance = () => {
  const loginRedirect = jest.fn().mockResolvedValue(undefined);
  const logoutRedirect = jest.fn().mockResolvedValue(undefined);
  const instance = { loginRedirect, logoutRedirect } as unknown as IPublicClientApplication;
  return { instance, loginRedirect, logoutRedirect };
};

// Each require() under resetModules gives a fresh module (redirectInFlight === false),
// while sessionStorage persists across requires — exactly like a real page reload.
const loadModule = () => require("../authRecovery");

const seedRecoveryState = (count: number, ts: number) => {
  sessionStorage.setItem(RECOVERY_KEY, JSON.stringify({ count, ts }));
};

describe("authRecovery", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    jest.resetModules();
    sessionStorage.clear();
    localStorage.clear();
  });

  describe("nextRecoveryAttempt", () => {
    it("starts at attempt 1 with no prior state", () => {
      const { nextRecoveryAttempt } = loadModule();
      expect(nextRecoveryAttempt(1_000)).toBe(1);
    });

    it("increments when the previous attempt is recent", () => {
      const { nextRecoveryAttempt } = loadModule();
      expect(nextRecoveryAttempt(1_000)).toBe(1);
      expect(nextRecoveryAttempt(2_000)).toBe(2);
      expect(nextRecoveryAttempt(3_000)).toBe(3);
    });

    it("resets to attempt 1 when the previous attempt is stale", () => {
      const { nextRecoveryAttempt } = loadModule();
      expect(nextRecoveryAttempt(1_000)).toBe(1);
      // > 2 minutes later: a fresh incident, not a continuing loop
      expect(nextRecoveryAttempt(1_000 + 2 * 60 * 1000 + 1)).toBe(1);
    });
  });

  describe("recoverAuth escalation", () => {
    it("first 401 attempts silent SSO (prompt: none)", () => {
      const { recoverAuth } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      recoverAuth(instance);

      expect(loginRedirect).toHaveBeenCalledTimes(1);
      expect(loginRedirect).toHaveBeenCalledWith(
        expect.objectContaining({ prompt: "none" }),
      );
      expect(logoutRedirect).not.toHaveBeenCalled();
    });

    it("second 401 (after a recent silent attempt) escalates to interactive select_account", () => {
      seedRecoveryState(1, Date.now());
      const { recoverAuth } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      recoverAuth(instance);

      expect(loginRedirect).toHaveBeenCalledTimes(1);
      expect(loginRedirect).toHaveBeenCalledWith(
        expect.objectContaining({ prompt: "select_account" }),
      );
      expect(logoutRedirect).not.toHaveBeenCalled();
    });

    it("third 401 clears the stale MSAL session via logoutRedirect", () => {
      seedRecoveryState(2, Date.now());
      const { recoverAuth } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      recoverAuth(instance);

      expect(logoutRedirect).toHaveBeenCalledTimes(1);
      expect(loginRedirect).not.toHaveBeenCalled();
    });

    it("dedupes a burst of concurrent 401s within a single page load", () => {
      const { recoverAuth } = loadModule();
      const { instance, loginRedirect } = makeInstance();

      recoverAuth(instance);
      recoverAuth(instance);
      recoverAuth(instance);

      // Only the first 401 triggers a redirect; the rest are guarded.
      expect(loginRedirect).toHaveBeenCalledTimes(1);
    });
  });

  describe("handleMsalAuthEvent", () => {
    const makeLoginFailureEvent = (
      overrides: Partial<EventMessage> = {},
    ): EventMessage => {
      // Re-require after jest.resetModules() so the error is an instance of the SAME
      // InteractionRequiredAuthError class the freshly loaded authRecovery module
      // checks against with instanceof.
      const { InteractionRequiredAuthError } = require("@azure/msal-browser");
      return {
        eventType: EventType.LOGIN_FAILURE,
        interactionType: InteractionType.Redirect,
        error: new InteractionRequiredAuthError("login_required"),
        payload: null,
        timestamp: 0,
        ...overrides,
      } as EventMessage;
    };

    it("escalates to interactive login when a redirect login fails with InteractionRequiredAuthError", () => {
      // The silent prompt:none redirect just bounced back with login_required —
      // its failure IS this event, so the next rung is interactive.
      seedRecoveryState(1, Date.now());
      const { handleMsalAuthEvent } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      handleMsalAuthEvent(instance, makeLoginFailureEvent());

      expect(loginRedirect).toHaveBeenCalledTimes(1);
      expect(loginRedirect).toHaveBeenCalledWith(
        expect.objectContaining({ prompt: "select_account" }),
      );
      expect(logoutRedirect).not.toHaveBeenCalled();
    });

    it("goes interactive even when the recovery counter was wiped", () => {
      // Even with no recorded silent attempt, a redirect that came back with
      // login_required must never retry prompt:none — that is the loop.
      const { handleMsalAuthEvent } = loadModule();
      const { instance, loginRedirect } = makeInstance();

      handleMsalAuthEvent(instance, makeLoginFailureEvent());

      expect(loginRedirect).toHaveBeenCalledTimes(1);
      expect(loginRedirect).toHaveBeenCalledWith(
        expect.objectContaining({ prompt: "select_account" }),
      );
    });

    it("clears the MSAL session via logoutRedirect when interactive login also failed", () => {
      seedRecoveryState(2, Date.now());
      const { handleMsalAuthEvent } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      handleMsalAuthEvent(instance, makeLoginFailureEvent());

      expect(logoutRedirect).toHaveBeenCalledTimes(1);
      expect(loginRedirect).not.toHaveBeenCalled();
    });

    it("ignores silent (non-redirect) token failures", () => {
      const { handleMsalAuthEvent } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      handleMsalAuthEvent(
        instance,
        makeLoginFailureEvent({ interactionType: InteractionType.Silent }),
      );

      expect(loginRedirect).not.toHaveBeenCalled();
      expect(logoutRedirect).not.toHaveBeenCalled();
    });

    it("ignores redirect login failures with other errors (e.g. user cancelled)", () => {
      const { handleMsalAuthEvent } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      handleMsalAuthEvent(
        instance,
        makeLoginFailureEvent({ error: new Error("access_denied") }),
      );

      expect(loginRedirect).not.toHaveBeenCalled();
      expect(logoutRedirect).not.toHaveBeenCalled();
    });

    it("ignores unrelated event types", () => {
      const { handleMsalAuthEvent } = loadModule();
      const { instance, loginRedirect, logoutRedirect } = makeInstance();

      handleMsalAuthEvent(
        instance,
        makeLoginFailureEvent({ eventType: EventType.LOGIN_SUCCESS }),
      );

      expect(loginRedirect).not.toHaveBeenCalled();
      expect(logoutRedirect).not.toHaveBeenCalled();
    });

    it("dedupes against a redirect already in flight", () => {
      const { handleMsalAuthEvent } = loadModule();
      const { instance, loginRedirect } = makeInstance();

      handleMsalAuthEvent(instance, makeLoginFailureEvent());
      handleMsalAuthEvent(instance, makeLoginFailureEvent());

      expect(loginRedirect).toHaveBeenCalledTimes(1);
    });
  });

  describe("clearAuthRecoveryState", () => {
    it("resets the counter so the next 401 recovers from the silent path again", () => {
      seedRecoveryState(2, Date.now());
      const { clearAuthRecoveryState, recoverAuth } = loadModule();
      const { instance, loginRedirect } = makeInstance();

      clearAuthRecoveryState();
      recoverAuth(instance);

      expect(loginRedirect).toHaveBeenCalledWith(
        expect.objectContaining({ prompt: "none" }),
      );
      expect(sessionStorage.getItem(RECOVERY_KEY)).not.toBeNull();
    });
  });
});
