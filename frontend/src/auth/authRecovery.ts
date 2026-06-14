import { IPublicClientApplication } from "@azure/msal-browser";
import { apiRequest, loginRedirectRequest } from "./msalConfig";
import { UserStorage } from "./userStorage";
import { clearTokenCache } from "../api/client";

/**
 * Automatic recovery from 401 responses, with a loop breaker.
 *
 * Background: a 401 means the access token MSAL handed us was rejected (expired,
 * stale, or unusable). The naive recovery — always redirect with `prompt: "none"`
 * (silent SSO) — cannot recover when the Azure session / refresh token is also gone:
 * Azure returns `login_required`, the app comes back, fires the same calls, gets 401
 * again, and redirects with `prompt: "none"` again — an infinite redirect loop. Only a
 * manual logout (which clears MSAL's localStorage cache) breaks it.
 *
 * This module escalates instead of looping:
 *   attempt 1 -> silent SSO (`prompt: "none"`) — invisible for the common expiry case
 *   attempt 2 -> interactive (`prompt: "select_account"`) — forces a fresh sign-in
 *   attempt 3 -> `logoutRedirect` — fully clears the stale MSAL cache (the manual fix)
 *
 * The attempt counter lives in `sessionStorage` so it survives the redirect round-trip
 * (a module-level boolean would reset to its initial value on every page reload and
 * could never count across reloads). A module-level flag still dedupes the burst of
 * concurrent 401s within a single page load.
 */

const RECOVERY_KEY = "auth.recovery";
const RETURN_URL_KEY = "auth.returnUrl";

/**
 * If the previous recovery attempt is older than this, treat the new 401 as a fresh
 * incident (reset the counter). Prevents a legitimate token expiry hours later from
 * being mistaken for the next step of an active loop.
 */
const STALE_ATTEMPT_MS = 2 * 60 * 1000;
const MAX_SILENT_ATTEMPT = 1;
const MAX_INTERACTIVE_ATTEMPT = 2;

interface RecoveryState {
  count: number;
  ts: number;
}

// Dedupes the burst of concurrent 401s within one page load.
let redirectInFlight = false;

const readState = (): RecoveryState | null => {
  try {
    const raw = sessionStorage.getItem(RECOVERY_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as RecoveryState;
    if (typeof parsed?.count !== "number" || typeof parsed?.ts !== "number") {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
};

const writeState = (state: RecoveryState): void => {
  try {
    sessionStorage.setItem(RECOVERY_KEY, JSON.stringify(state));
  } catch {
    // sessionStorage unavailable (private mode / quota) — degrade gracefully.
  }
};

/**
 * Clear the recovery counter. Call this when an authenticated request succeeds so the
 * next token expiry starts recovery again from the silent path.
 */
export const clearAuthRecoveryState = (): void => {
  redirectInFlight = false;
  try {
    sessionStorage.removeItem(RECOVERY_KEY);
  } catch {
    // ignore
  }
};

const saveReturnUrl = (): void => {
  const returnUrl = window.location.pathname + window.location.search;
  if (returnUrl && returnUrl !== "/") {
    try {
      localStorage.setItem(RETURN_URL_KEY, returnUrl);
    } catch {
      // ignore
    }
  }
};

/**
 * Determine which recovery step this 401 should trigger, advancing (and persisting)
 * the attempt counter. Exported for unit testing.
 */
export const nextRecoveryAttempt = (now: number): number => {
  const prev = readState();
  const isRecent = prev !== null && now - prev.ts < STALE_ATTEMPT_MS;
  const attempt = isRecent ? prev!.count + 1 : 1;
  writeState({ count: attempt, ts: now });
  return attempt;
};

/**
 * Recover from a 401 by re-authenticating, escalating across attempts so a dead session
 * cannot spin the app in an infinite redirect loop.
 */
export const recoverAuth = (instance: IPublicClientApplication): void => {
  if (redirectInFlight) return;
  redirectInFlight = true;

  // Clear app-level session state (MSAL's own PKCE state is left intact for the
  // in-flight auth code exchange).
  UserStorage.clearUserInfo();
  clearTokenCache();
  saveReturnUrl();

  const attempt = nextRecoveryAttempt(Date.now());

  if (attempt <= MAX_SILENT_ATTEMPT) {
    console.log("🔐 Auth recovery: attempting silent SSO (prompt: none)");
    instance
      .loginRedirect({ ...apiRequest, prompt: "none" })
      .catch((error) => {
        console.warn("🔐 Silent SSO redirect failed to start, escalating:", error);
        escalateInteractive(instance);
      });
    return;
  }

  if (attempt <= MAX_INTERACTIVE_ATTEMPT) {
    escalateInteractive(instance);
    return;
  }

  // Attempts exhausted: the stale MSAL session can't be recovered silently or via
  // re-consent. Fully clear it — this is the automated equivalent of a manual logout,
  // which is the only thing known to break the loop.
  console.warn("🔐 Auth recovery: attempts exhausted, clearing MSAL session via logout");
  clearAuthRecoveryState();
  instance
    .logoutRedirect({ postLogoutRedirectUri: window.location.origin })
    .catch((error) => {
      console.error("❌ logoutRedirect failed during auth recovery:", error);
      window.location.href = "/";
    });
};

const escalateInteractive = (instance: IPublicClientApplication): void => {
  console.log("🔐 Auth recovery: escalating to interactive login (select_account)");
  instance
    .loginRedirect({ ...loginRedirectRequest, prompt: "select_account" })
    .catch((error) => {
      console.error("❌ Interactive login redirect failed during auth recovery:", error);
      clearAuthRecoveryState();
      window.location.href = "/";
    });
};
