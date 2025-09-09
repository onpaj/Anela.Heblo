import React, { useState } from "react";
import { User, LogIn, LogOut, ChevronUp } from "lucide-react";
import { useAuth } from "../../auth/useAuth";
import { useMockAuth, shouldUseMockAuth } from "../../auth/mockAuth";

interface UserProfileProps {
  compact?: boolean;
}

const UserProfile: React.FC<UserProfileProps> = ({ compact = false }) => {
  // Use mock auth in development if enabled
  const realAuth = useAuth();
  const mockAuth = useMockAuth();

  const auth = shouldUseMockAuth() ? mockAuth : realAuth;
  const {
    isAuthenticated,
    login,
    logout,
    getUserInfo,
    getStoredUserInfo,
    inProgress,
  } = auth;

  const [showMenu, setShowMenu] = useState(false);
  const userInfo = getUserInfo();
  const storedUserInfo = getStoredUserInfo();

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
      setShowMenu(false);
    } catch (error) {
      console.error("Logout error:", error);
    }
  };

  if (inProgress === "login" || inProgress === "logout") {
    return (
      <div
        className={`flex items-center ${compact ? "justify-center" : "justify-center p-2"}`}
      >
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
          <p className="text-sm font-medium text-gray-700 group-hover:text-gray-900">
            Sign in
          </p>
          <p className="text-xs text-gray-500">Click to authenticate</p>
        </div>
        <LogIn className="h-4 w-4 text-gray-400 group-hover:text-gray-600" />
      </button>
    );
  }

  if (compact) {
    return (
      <div className="relative">
        <button
          onClick={() => setShowMenu(!showMenu)}
          className="w-8 h-8 bg-primary-blue rounded-full flex items-center justify-center hover:bg-accent-blue-bright transition-colors"
          title={`${userInfo?.name} (${userInfo?.email})`}
        >
          <span className="text-white text-sm font-medium">
            {userInfo?.initials || "U"}
          </span>
        </button>

        {/* Compact User Menu */}
        {showMenu && (
          <div className="absolute bottom-full left-1/2 transform -translate-x-1/2 mb-2 bg-primary-white border border-border-light rounded-xl shadow-hover py-1 min-w-48 z-50">
            <div className="px-4 py-2 border-b border-gray-100">
              <p className="text-sm font-medium text-neutral-slate">
                {userInfo?.name}
              </p>
              <p className="text-xs text-gray-500">{userInfo?.email}</p>
              {storedUserInfo?.lastLogin && (
                <p className="text-xs text-gray-400 mt-1">
                  Last login:{" "}
                  {new Date(storedUserInfo.lastLogin).toLocaleString()}
                </p>
              )}
              {userInfo?.roles && userInfo.roles.length > 0 && (
                <div className="mt-2">
                  {userInfo.roles.map((role) => (
                    <span
                      key={role}
                      className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-secondary-blue-pale text-primary-blue mr-1"
                    >
                      {role}
                    </span>
                  ))}
                </div>
              )}
            </div>
            <button
              onClick={handleLogout}
              className="flex items-center space-x-2 px-4 py-2 text-sm text-neutral-slate hover:bg-secondary-blue-pale/50 w-full text-left"
            >
              <LogOut className="h-4 w-4" />
              <span>Sign out</span>
            </button>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="relative">
      <button
        onClick={() => setShowMenu(!showMenu)}
        className="flex items-center space-x-3 p-2 w-full text-left hover:bg-secondary-blue-pale/50 rounded-md transition-colors group"
      >
        <div className="w-8 h-8 bg-primary-blue rounded-full flex items-center justify-center">
          <span className="text-white text-sm font-medium">
            {userInfo?.initials || "U"}
          </span>
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium text-neutral-slate group-hover:text-neutral-slate truncate">
            {userInfo?.name || "User"}
          </p>
          <p className="text-xs text-gray-500 truncate">
            {userInfo?.email || "user@example.com"}
          </p>
        </div>
        <ChevronUp
          className={`h-4 w-4 text-gray-400 group-hover:text-gray-600 transition-transform ${
            showMenu ? "rotate-180" : ""
          }`}
        />
      </button>

      {/* User Menu */}
      {showMenu && (
        <div className="absolute bottom-full left-0 right-0 mb-2 bg-primary-white border border-border-light rounded-xl shadow-hover py-1 z-50">
          <div className="px-4 py-2 border-b border-gray-100">
            <p className="text-sm font-medium text-neutral-slate">
              {userInfo?.name}
            </p>
            <p className="text-xs text-gray-500">{userInfo?.email}</p>
            {storedUserInfo?.lastLogin && (
              <p className="text-xs text-gray-400 mt-1">
                Last login:{" "}
                {new Date(storedUserInfo.lastLogin).toLocaleString()}
              </p>
            )}
            {userInfo?.roles && userInfo.roles.length > 0 && (
              <div className="mt-2">
                {userInfo.roles.map((role) => (
                  <span
                    key={role}
                    className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-secondary-blue-pale text-primary-blue mr-1"
                  >
                    {role}
                  </span>
                ))}
              </div>
            )}
          </div>
          <button
            onClick={handleLogout}
            className="flex items-center space-x-2 px-4 py-2 text-sm text-neutral-slate hover:bg-secondary-blue-pale/50 w-full text-left"
          >
            <LogOut className="h-4 w-4" />
            <span>Sign out</span>
          </button>
        </div>
      )}
    </div>
  );
};

export default UserProfile;
