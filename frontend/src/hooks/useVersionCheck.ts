import { useEffect, useCallback } from "react";
import { versionService } from "../services/versionService";
import { useToast } from "../contexts/ToastContext";

export interface UseVersionCheckProps {
  /** Whether to enable automatic version checking (default: true) */
  enabled?: boolean;
  /** Whether to show update notifications (default: true) */
  showNotifications?: boolean;
  /** Custom callback when new version is detected */
  onNewVersionDetected?: (newVersion: string, currentVersion: string) => void;
}

export interface UseVersionCheckReturn {
  /** Check for new version manually */
  checkVersion: () => Promise<void>;
  /** Initialize version tracking */
  initializeVersion: () => Promise<void>;
  /** Update to new version (clear cache and reload) */
  updateToNewVersion: (version: string) => void;
  /** Get current stored version */
  getCurrentVersion: () => string | null;
}

/**
 * Hook to manage application version checking and updates
 */
export const useVersionCheck = (
  props: UseVersionCheckProps = {},
): UseVersionCheckReturn => {
  const {
    enabled = true,
    showNotifications = true,
    onNewVersionDetected,
  } = props;

  const { showInfo } = useToast();

  /**
   * Handle new version detection
   */
  const handleNewVersionDetected = useCallback(
    (newVersion: string, currentVersion: string) => {
      console.log(
        `New version available: ${newVersion} (current: ${currentVersion})`,
      );

      // Call custom callback if provided
      if (onNewVersionDetected) {
        onNewVersionDetected(newVersion, currentVersion);
      }

      // Show notification if enabled
      if (showNotifications) {
        showInfo(
          "New Version Available",
          `Version ${newVersion} is available. Click to update and refresh the application.`,
          {
            duration: 0, // Don't auto-hide
            action: {
              label: "Update Now",
              onClick: () => versionService.updateToNewVersion(newVersion),
            },
          },
        );
      }
    },
    [onNewVersionDetected, showNotifications, showInfo],
  );

  /**
   * Check for new version manually
   */
  const checkVersion = useCallback(async () => {
    if (!enabled) return;

    try {
      const result = await versionService.hasNewVersion();

      if (result.hasUpdate && result.newVersion && result.currentVersion) {
        handleNewVersionDetected(result.newVersion, result.currentVersion);
      }
    } catch (error) {
      console.error("Manual version check failed:", error);
    }
  }, [enabled, handleNewVersionDetected]);

  /**
   * Initialize version tracking
   */
  const initializeVersion = useCallback(async () => {
    if (!enabled) return;

    try {
      await versionService.initializeVersion();
    } catch (error) {
      console.error("Version initialization failed:", error);
    }
  }, [enabled]);

  /**
   * Update to new version
   */
  const updateToNewVersion = useCallback((version: string) => {
    versionService.updateToNewVersion(version);
  }, []);

  /**
   * Get current stored version
   */
  const getCurrentVersion = useCallback(() => {
    return versionService.getCurrentStoredVersion();
  }, []);

  /**
   * Set up periodic checking and cleanup
   */
  useEffect(() => {
    if (!enabled) return;

    // Start periodic checking
    versionService.startPeriodicCheck(handleNewVersionDetected);

    // Cleanup on unmount
    return () => {
      versionService.stopPeriodicCheck();
    };
  }, [enabled, handleNewVersionDetected]);

  return {
    checkVersion,
    initializeVersion,
    updateToNewVersion,
    getCurrentVersion,
  };
};
