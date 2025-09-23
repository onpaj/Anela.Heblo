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
 * Default auto-hide timeout (10 seconds)
 */
const DEFAULT_AUTO_HIDE_TIMEOUT = 10000;

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
   * Start auto-hide countdown
   */
  const startAutoHide = useCallback(() => {
    clearAutoHideTimeout();
    
    setToaster(prev => ({
      ...prev,
      isAutoHiding: true,
    }));

    autoHideTimeoutRef.current = setTimeout(() => {
      setToaster(prev => ({
        ...prev,
        isVisible: false,
        isAutoHiding: false,
        version: undefined,
        changes: undefined,
      }));
    }, DEFAULT_AUTO_HIDE_TIMEOUT);
  }, [clearAutoHideTimeout]);

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

      // Start auto-hide countdown
      startAutoHide();
    } catch (error) {
      console.error('Failed to show changelog toaster:', error);
    }
  }, [clearAutoHideTimeout, startAutoHide]);

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
 * Extended return type with additional utility methods
 */
export interface UseChangelogToasterExtendedReturn extends UseChangelogToasterReturn {
  checkAndShowNewVersion: (currentVersion: string, changes: ChangelogEntry[]) => void;
}

/**
 * Hook that automatically checks for new versions and shows toaster
 */
export function useAutoChangelogToaster(
  currentVersion?: string,
  changes?: ChangelogEntry[]
): UseChangelogToasterExtendedReturn {
  const toasterHook = useChangelogToaster() as UseChangelogToasterExtendedReturn;

  // Auto-check when currentVersion and changes are available
  useEffect(() => {
    if (currentVersion && changes && changes.length > 0) {
      toasterHook.checkAndShowNewVersion(currentVersion, changes);
    }
  }, [currentVersion, changes, toasterHook]);

  return toasterHook;
}