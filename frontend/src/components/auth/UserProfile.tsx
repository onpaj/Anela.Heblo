import React, { useState } from "react";
import { User, LogIn, LogOut, X, ShieldCheck, KeyRound, Users } from "lucide-react";
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

  const [showModal, setShowModal] = useState(false);
  const userInfo = getUserInfo();
  const storedUserInfo = getStoredUserInfo();
  const { permissions, groups, isSuperUser, isLoading: permissionsLoading } =
    usePermissionsContext();

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
      setShowModal(false);
    } catch (error) {
      console.error("Logout error:", error);
    }
  };

  if (inProgress === "login" || inProgress === "logout") {
    return (
      <div className={`flex items-center ${compact ? "justify-center" : "justify-center p-2"}`}>
        <div className="w-8 h-8 bg-gray-300 rounded-full animate-pulse"></div>
      </div>
    );
  }

  if (!isAuthenticated) {
    if (compact) {
      return (
        <button
          onClick={handleLogin}
          className="w-8 h-8 bg-gray-400 rounded-full flex items-center justify-center hover:bg-gray-500 transition-colors"
          title="Sign in"
        >
          <User className="h-4 w-4 text-white" />
        </button>
      );
    }

    return (
      <button
        onClick={handleLogin}
        className="flex items-center space-x-3 p-2 w-full text-left hover:bg-secondary-blue-pale/50 rounded-md transition-colors group"
        title="Sign in"
      >
        <div className="w-8 h-8 bg-gray-400 rounded-full flex items-center justify-center">
          <User className="h-4 w-4 text-white" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-gray-700 group-hover:text-gray-900">Sign in</p>
          <p className="text-xs text-gray-500">Click to authenticate</p>
        </div>
        <LogIn className="h-4 w-4 text-gray-400 group-hover:text-gray-600" />
      </button>
    );
  }

  const triggerButton = compact ? (
    <button
      onClick={() => setShowModal(true)}
      className="w-8 h-8 bg-primary-blue rounded-full flex items-center justify-center hover:bg-accent-blue-bright transition-colors"
      title={`${userInfo?.name} (${userInfo?.email})`}
    >
      <span className="text-white text-sm font-medium">{userInfo?.initials || "U"}</span>
    </button>
  ) : (
    <button
      onClick={() => setShowModal(true)}
      className="flex items-center space-x-3 p-2 w-full text-left hover:bg-secondary-blue-pale/50 rounded-md transition-colors group"
    >
      <div className="w-8 h-8 bg-primary-blue rounded-full flex items-center justify-center">
        <span className="text-white text-sm font-medium">{userInfo?.initials || "U"}</span>
      </div>
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium text-neutral-slate group-hover:text-neutral-slate truncate">
          {userInfo?.name || "User"}
        </p>
        <p className="text-xs text-gray-500 truncate">{userInfo?.email || "user@example.com"}</p>
      </div>
    </button>
  );

  return (
    <>
      {triggerButton}

      {showModal && (
        <div className="fixed inset-0 z-50 overflow-y-auto">
          <div
            className="fixed inset-0 bg-black bg-opacity-50 transition-opacity"
            onClick={() => setShowModal(false)}
          />

          <div className="flex min-h-full items-center justify-center p-4">
            <div className="relative bg-primary-white rounded-xl shadow-xl max-w-sm w-full">
              {/* Header */}
              <div className="flex items-center justify-between px-5 py-4 border-b border-gray-100">
                <h3 className="text-base font-semibold text-neutral-slate">Uživatel</h3>
                <button
                  onClick={() => setShowModal(false)}
                  className="text-gray-400 hover:text-gray-600 transition-colors"
                >
                  <X className="h-5 w-5" />
                </button>
              </div>

              {/* User identity */}
              <div className="px-5 py-5 flex items-center space-x-4 border-b border-gray-100">
                <div className="w-12 h-12 bg-primary-blue rounded-full flex items-center justify-center shrink-0">
                  <span className="text-white text-lg font-semibold">
                    {userInfo?.initials || "U"}
                  </span>
                </div>
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-neutral-slate truncate">
                    {userInfo?.name}
                  </p>
                  <p className="text-xs text-gray-500 truncate">{userInfo?.email}</p>
                  {storedUserInfo?.lastLogin && (
                    <p className="text-xs text-gray-400 mt-0.5">
                      Poslední přihlášení:{" "}
                      {new Date(storedUserInfo.lastLogin).toLocaleString("cs-CZ")}
                    </p>
                  )}
                </div>
              </div>

              {/* Roles */}
              {userInfo?.roles && userInfo.roles.length > 0 && (
                <div className="px-5 py-4 border-b border-gray-100">
                  <div className="flex items-center space-x-2 mb-3">
                    <ShieldCheck className="h-4 w-4 text-primary-blue" />
                    <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                      Role
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {userInfo.roles.map((role) => (
                      <span
                        key={role}
                        className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-secondary-blue-pale text-primary-blue"
                      >
                        {role}
                      </span>
                    ))}
                  </div>
                </div>
              )}

              {/* Permissions */}
              {!permissionsLoading && (isSuperUser || permissions.length > 0) && (
                <div className="px-5 py-4 border-b border-gray-100">
                  <div className="flex items-center space-x-2 mb-3">
                    <KeyRound className="h-4 w-4 text-emerald-600" />
                    <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                      Oprávnění
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {isSuperUser ? (
                      <span className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-amber-50 text-amber-700">
                        Super User · vše povoleno
                      </span>
                    ) : (
                      permissions.map((perm) => (
                        <span
                          key={perm}
                          className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-emerald-50 text-emerald-700"
                        >
                          {perm}
                        </span>
                      ))
                    )}
                  </div>
                </div>
              )}

              {/* Groups */}
              {!permissionsLoading && groups.length > 0 && (
                <div className="px-5 py-4 border-b border-gray-100">
                  <div className="flex items-center space-x-2 mb-3">
                    <Users className="h-4 w-4 text-primary-blue" />
                    <span className="text-xs font-semibold text-gray-500 uppercase tracking-wide">
                      Skupiny
                    </span>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    {groups.map((group) => (
                      <span
                        key={group}
                        className="inline-flex items-center px-3 py-1 rounded-full text-xs font-medium bg-secondary-blue-pale text-primary-blue"
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
                  className="flex items-center justify-center space-x-2 w-full px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
                >
                  <LogOut className="h-4 w-4" />
                  <span>Odhlásit se</span>
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
};

export default UserProfile;
