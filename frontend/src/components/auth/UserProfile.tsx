import React, { useState, useRef, useEffect } from "react";
import { Transition } from "@headlessui/react";
import { User, LogIn, LogOut, ShieldCheck, KeyRound, Users } from "lucide-react";
import { useAuth } from "../../auth/useAuth";
import { useMockAuth, shouldUseMockAuth } from "../../auth/mockAuth";
import { usePermissionsContext } from "../../auth/PermissionsContext";

interface UserProfileProps {
  compact?: boolean;
  menuPosition?: "above" | "below";
}

const UserProfile: React.FC<UserProfileProps> = ({
  compact = false,
  menuPosition = "above",
}) => {
  const realAuth = useAuth();
  const mockAuth = useMockAuth();

  const auth = shouldUseMockAuth() ? mockAuth : realAuth;
  const { isAuthenticated, login, logout, getUserInfo, getStoredUserInfo, inProgress } = auth;

  const [showPanel, setShowPanel] = useState(false);
  const panelRef = useRef<HTMLDivElement>(null);
  const userInfo = getUserInfo();
  const storedUserInfo = getStoredUserInfo();
  const { permissions, groups, isSuperUser, isLoading: permissionsLoading } =
    usePermissionsContext();

  const tokenRoles = userInfo?.roles ?? [];
  const displayRoles = isSuperUser && !tokenRoles.includes("super_user")
    ? ["super_user", ...tokenRoles]
    : tokenRoles;

  useEffect(() => {
    const handleMouseDown = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) {
        setShowPanel(false);
      }
    };
    if (showPanel) {
      document.addEventListener("mousedown", handleMouseDown);
    }
    return () => {
      document.removeEventListener("mousedown", handleMouseDown);
    };
  }, [showPanel]);

  const handleLogin = async () => {
    try {
      await login();
    } catch (error) {
      console.error("Login error:", error);
    }
  };

  const handleLogout = async () => {
    try {
      await logout();
      setShowPanel(false);
    } catch (error) {
      console.error("Logout error:", error);
    }
  };

  if (inProgress === "login" || inProgress === "logout") {
    return (
      <div className={`flex items-center ${compact ? "justify-center" : "justify-center p-2"}`}>
        <div className="w-8 h-8 bg-gray-300 dark:bg-white/10 rounded-full animate-pulse"></div>
      </div>
    );
  }

  if (!isAuthenticated) {
    if (compact) {
      return (
        <button
          onClick={handleLogin}
          className="w-8 h-8 bg-gray-400 dark:bg-graphite-border-strong rounded-full flex items-center justify-center hover:bg-gray-500 dark:hover:bg-graphite-hover transition-colors"
          title="Sign in"
        >
          <User className="h-4 w-4 text-white" />
        </button>
      );
    }

    return (
      <button
        onClick={handleLogin}
        className="flex items-center space-x-3 p-2 w-full text-left hover:bg-secondary-blue-pale/50 dark:hover:bg-white/5 rounded-md transition-colors group"
        title="Sign in"
      >
        <div className="w-8 h-8 bg-gray-400 dark:bg-graphite-border-strong rounded-full flex items-center justify-center">
          <User className="h-4 w-4 text-white" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-700 dark:text-graphite-muted group-hover:text-gray-900 dark:group-hover:text-graphite-text">Sign in</p>
          <p className="text-xs text-gray-500 dark:text-graphite-muted">Click to authenticate</p>
        </div>
        <LogIn className="h-4 w-4 text-gray-400 dark:text-graphite-faint group-hover:text-gray-600 dark:group-hover:text-graphite-muted" />
      </button>
    );
  }

  const verticalClass = menuPosition === "below" ? "top-full" : "bottom-full";
  const panelPositionClass = compact
    ? `absolute ${verticalClass} left-0 w-60`
    : `absolute ${verticalClass} inset-x-0`;

  return (
    <div ref={panelRef}>
      {/* Trigger */}
      {compact ? (
        <button
          onClick={() => setShowPanel((prev) => !prev)}
          className="w-8 h-8 bg-primary-blue dark:bg-graphite-accent rounded-full flex items-center justify-center hover:bg-accent-blue-bright dark:hover:bg-graphite-accent-strong transition-colors"
          title={`${userInfo?.name} (${userInfo?.email})`}
        >
          <span className="text-white dark:text-graphite-accent-ink text-sm font-medium">{userInfo?.initials || "U"}</span>
        </button>
      ) : (
        <button
          onClick={() => setShowPanel((prev) => !prev)}
          className="flex items-center space-x-3 p-2 w-full text-left hover:bg-secondary-blue-pale/50 dark:hover:bg-white/5 rounded-md transition-colors group"
        >
          <div className="w-8 h-8 bg-primary-blue dark:bg-graphite-accent rounded-full flex items-center justify-center">
            <span className="text-white dark:text-graphite-accent-ink text-sm font-medium">{userInfo?.initials || "U"}</span>
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm font-medium text-neutral-slate dark:text-graphite-text group-hover:text-neutral-slate truncate">
              {userInfo?.name || "User"}
            </p>
            <p className="text-xs text-gray-500 dark:text-graphite-muted truncate">{userInfo?.email || "user@example.com"}</p>
          </div>
        </button>
      )}

      {/* Slide-up panel */}
      <Transition
        show={showPanel}
        enter="transition ease-out duration-200"
        enterFrom="opacity-0 translate-y-2"
        enterTo="opacity-100 translate-y-0"
        leave="transition ease-in duration-150"
        leaveFrom="opacity-100 translate-y-0"
        leaveTo="opacity-0 translate-y-2"
      >
        <div
          className={`${panelPositionClass} z-10 ${menuPosition === "below" ? "rounded-b-xl" : "rounded-t-xl"} shadow-lg dark:shadow-soft-dark bg-white dark:bg-graphite-surface border border-gray-100 dark:border-graphite-border overflow-hidden`}
        >
          <div className="max-h-[80vh] overflow-y-auto">
            {/* User identity */}
            <div className="px-5 py-5 flex items-center space-x-4 border-b border-gray-100 dark:border-graphite-border">
              <div className="w-12 h-12 bg-primary-blue dark:bg-graphite-accent rounded-full flex items-center justify-center shrink-0">
                <span className="text-white dark:text-graphite-accent-ink text-lg font-semibold">
                  {userInfo?.initials || "U"}
                </span>
              </div>
              <div className="min-w-0">
                <p className="text-sm font-semibold text-neutral-slate dark:text-graphite-text truncate">
                  {userInfo?.name}
                </p>
                <p className="text-xs text-gray-500 dark:text-graphite-muted truncate">{userInfo?.email}</p>
                {storedUserInfo?.lastLogin && (
                  <p className="text-xs text-gray-400 dark:text-graphite-faint mt-0.5">
                    Poslední přihlášení:{" "}
                    {new Date(storedUserInfo.lastLogin).toLocaleString("cs-CZ")}
                  </p>
                )}
              </div>
            </div>

            {/* Roles */}
            {displayRoles.length > 0 && (
              <div className="px-5 py-4 border-b border-gray-100 dark:border-graphite-border">
                <div className="flex items-center space-x-2 mb-3">
                  <ShieldCheck className="h-4 w-4 text-primary-blue dark:text-graphite-accent" />
                  <span className="text-xs font-semibold text-gray-500 dark:text-graphite-muted uppercase tracking-wide">
                    Role
                  </span>
                </div>
                <div className="flex flex-wrap gap-2">
                  {displayRoles.map((role) => (
                    <span
                      key={role}
                      className={`inline-flex items-center px-3 py-1 rounded-full text-xs font-medium ${
                        role === "super_user"
                          ? "bg-amber-50 dark:bg-amber-400/15 text-amber-700 dark:text-amber-400"
                          : "bg-secondary-blue-pale dark:bg-graphite-accent/10 text-primary-blue dark:text-graphite-accent"
                      }`}
                    >
                      {role}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {/* Permissions */}
            {!permissionsLoading && (isSuperUser || permissions.length > 0) && (
              <div className="px-5 py-4 border-b border-gray-100 dark:border-graphite-border">
                <div className="flex items-center space-x-2 mb-3">
                  <KeyRound className="h-4 w-4 text-emerald-600 dark:text-emerald-400" />
                  <span className="text-xs font-semibold text-gray-500 dark:text-graphite-muted uppercase tracking-wide">
                    Oprávnění
                  </span>
                </div>
                {isSuperUser && (
                  <p className="text-xs text-amber-700 dark:text-amber-400 bg-amber-50 dark:bg-amber-400/15 px-3 py-1.5 rounded-md mb-3">
                    Super User · vše povoleno
                  </p>
                )}
                <div className="flex flex-wrap gap-2">
                  {permissions.map((perm) => (
                    <span
                      key={perm}
                      className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-emerald-50 dark:bg-emerald-400/15 text-emerald-700 dark:text-emerald-400"
                    >
                      {perm}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {/* Groups */}
            {!permissionsLoading && groups.length > 0 && (
              <div className="px-5 py-4 border-b border-gray-100 dark:border-graphite-border">
                <div className="flex items-center space-x-2 mb-3">
                  <Users className="h-4 w-4 text-primary-blue dark:text-graphite-accent" />
                  <span className="text-xs font-semibold text-gray-500 dark:text-graphite-muted uppercase tracking-wide">
                    Skupiny
                  </span>
                </div>
                <div className="flex flex-wrap gap-2">
                  {groups.map((group) => (
                    <span
                      key={group}
                      className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-secondary-blue-pale dark:bg-graphite-accent/10 text-primary-blue dark:text-graphite-accent"
                    >
                      {group}
                    </span>
                  ))}
                </div>
              </div>
            )}

            {/* Footer */}
            <div className="px-5 py-4">
              <button
                onClick={handleLogout}
                className="flex items-center justify-center space-x-2 w-full px-4 py-2 text-sm font-medium text-gray-700 dark:text-graphite-text bg-white dark:bg-graphite-surface-2 border border-gray-200 dark:border-graphite-border rounded-lg hover:bg-gray-50 dark:hover:bg-white/5 transition-colors"
              >
                <LogOut className="h-4 w-4" />
                <span>Odhlásit se</span>
              </button>
            </div>
          </div>
        </div>
      </Transition>
    </div>
  );
};

export default UserProfile;
