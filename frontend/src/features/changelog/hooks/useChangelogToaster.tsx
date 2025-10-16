/**
 * Changelog toaster hook for managing toaster state and behavior
 * Anela.Heblo - Automatic Changelog Generation and Display System
 */

import { useState, useCallback, useEffect, useRef } from 'react';
import { 
  ToasterState, 
  UseChangelogToasterReturn, 
  ChangelogEntry 
} from '../types';
import { 
  isNewVersion as checkIsNewVersion,
  markVersionAsSeen
} from '../utils/version-tracking';


/**
 * Hook for managing changelog toaster state and behavior
 */
export function useChangelogToaster(): UseChangelogToasterReturn {
  const [toaster, setToaster] = useState<ToasterState>({
    isVisible: false,
    isAutoHiding: false,
  });

  const autoHideTimeoutRef = useRef<NodeJS.Timeout | null>(null);

  /**
   * Clear auto-hide timeout
   */
  const clearAutoHideTimeout = useCallback(() => {
    if (autoHideTimeoutRef.current) {
      clearTimeout(autoHideTimeoutRef.current);
      autoHideTimeoutRef.current = null;
    }
  }, []);


  /**
   * Show toaster for new version
   */
  const showToaster = useCallback((version: string, changes: ChangelogEntry[]) => {
    try {
      // Clear any existing timeout
      clearAutoHideTimeout();

      setToaster({
        isVisible: true,
        version,
        changes,
        isAutoHiding: false,
      });

      // Do not start auto-hide - user must close manually
    } catch (error) {
      console.error('Failed to show changelog toaster:', error);
    }
  }, [clearAutoHideTimeout]);

  /**
   * Hide toaster manually
   */
  const hideToaster = useCallback(() => {
    try {
      clearAutoHideTimeout();
      
      // Mark current version as seen if showing
      if (toaster.version) {
        markVersionAsSeen(toaster.version);
      }

      setToaster({
        isVisible: false,
        isAutoHiding: false,
        version: undefined,
        changes: undefined,
      });
    } catch (error) {
      console.error('Failed to hide changelog toaster:', error);
    }
  }, [clearAutoHideTimeout, toaster.version]);

  /**
   * Check if version is new for user
   */
  const isNewVersion = useCallback((version: string): boolean => {
    try {
      return checkIsNewVersion(version);
    } catch (error) {
      console.error('Failed to check if version is new:', error);
      return false;
    }
  }, []);

  /**
   * Mark version as seen
   */
  const markVersionAsSeenCallback = useCallback((version: string): void => {
    try {
      markVersionAsSeen(version);
    } catch (error) {
      console.error('Failed to mark version as seen:', error);
    }
  }, []);


  // Cleanup timeout on unmount
  useEffect(() => {
    return () => {
      clearAutoHideTimeout();
    };
  }, [clearAutoHideTimeout]);

  // Automatically mark version as seen when toaster auto-hides
  useEffect(() => {
    if (!toaster.isVisible && toaster.version) {
      markVersionAsSeenCallback(toaster.version);
    }
  }, [toaster.isVisible, toaster.version, markVersionAsSeenCallback]);

  return {
    toaster,
    showToaster,
    hideToaster,
    isNewVersion,
    markVersionAsSeen: markVersionAsSeenCallback,
  };
}

/**
 * Hook that automatically checks for new versions and shows toaster
 */
export function useAutoChangelogToaster(
  currentVersion?: string,
  changes?: ChangelogEntry[]
): UseChangelogToasterReturn {
  const toasterHook = useChangelogToaster();
  const { showToaster, isNewVersion } = toasterHook;

  // Auto-check when currentVersion and changes are available
  useEffect(() => {
    if (currentVersion && changes && changes.length > 0) {
      // Check if this is a new version for the user
      if (isNewVersion(currentVersion)) {
        showToaster(currentVersion, changes);
      }
    }
  }, [currentVersion, changes, showToaster, isNewVersion]);

  return toasterHook;
}