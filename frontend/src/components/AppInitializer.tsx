import React, { useEffect, ReactNode } from "react";
import { useToast } from "../contexts/ToastContext";
import { setGlobalToastHandler } from "../api/client";
import { useVersionCheck } from "../hooks/useVersionCheck";

interface AppInitializerProps {
  children: ReactNode;
}

/**
 * Component that initializes global toast handler for API errors and version checking
 * Must be rendered inside ToastProvider context
 */
export const AppInitializer: React.FC<AppInitializerProps> = ({ children }) => {
  const { showError } = useToast();
  const { initializeVersion } = useVersionCheck({
    enabled: true,
    showNotifications: true,
    onNewVersionDetected: (newVersion, currentVersion) => {
      console.log(
        `Version update available: ${currentVersion} → ${newVersion}`,
      );
    },
  });

  useEffect(() => {
    // Set global toast handler for API errors
    setGlobalToastHandler(showError);

    // Initialize version tracking
    initializeVersion().catch((error) => {
      console.error("Failed to initialize version checking:", error);
    });

    return () => {
      // Cleanup on unmount (though this shouldn't happen in normal app lifecycle)
      setGlobalToastHandler(() => {});
    };
  }, [showError, initializeVersion]);

  return <>{children}</>;
};
